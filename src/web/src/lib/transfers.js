import { apiBaseUrl } from '../config';
import api from './api';
import { downloadFile } from './util';

export const getAll = async ({ direction }) => {
  const response = (
    await api.get(`/transfers/${encodeURIComponent(direction)}s`)
  ).data;

  if (!Array.isArray(response)) {
    console.warn('got non-array response from transfers API', response);
    return undefined;
  }

  return response;
};

export const download = ({ username, files = [] }) => {
  return api.post(
    `/transfers/downloads/${encodeURIComponent(username)}`,
    files,
  );
};

/**
 * Enqueues a batch of downloads.
 * @param {object} params
 * @param {string} params.username - The user to download from.
 * @param {{ filename: string, size: number }[]} [params.files] - Files to enqueue.
 * @param {string} [params.id] - Optional batch GUID. Generated server-side if omitted.
 * @param {string} [params.searchId] - Optional GUID of an associated search.
 * @param {{ destination?: string, externalId?: string }} [params.options] - `destination`: path relative to the configured download directory; `externalId`: optional external identifier for the batch.
 * @returns {Promise} Resolves with the axios response.
 *   - 201: all files enqueued successfully
 *   - 200: batch created, but every file failed to enqueue — check `response.data.failures`
 *   - 207: partial — some enqueued, some failed — check `response.data.failures`
 * @throws On error responses:
 *   - 400: validation failure (username/files required; duplicate filenames; id or searchId not a valid GUID)
 *   - 403: forbidden — running as relay agent
 *   - 404: user is offline
 *   - 409: a batch with the supplied id already exists
 *   - 429: a concurrent enqueue request is already in progress
 *   - 500: unexpected server error
 */
export const enqueueBatch = ({
  username,
  files = [],
  id,
  searchId,
  options = { destination: undefined, externalId: undefined },
}) => {
  return api.post('/transfers/downloads/batches', {
    files,
    id,
    options,
    searchId,
    username,
  });
};

/**
 * Cancels a transfer, optionally removing the record of it.
 *
 * Whether a removal takes the file with it is the server's decision, made from
 * `transfers.download.delete_file_on_removal` rather than from anything sent
 * here -- so this is the request it always was, and a removal answers either
 * 204 (nothing was deleted) or the outcome per file.
 */
export const cancel = ({ direction, id, remove = false, username }) => {
  return api.delete(
    `/transfers/${direction}s/${encodeURIComponent(username)}/${encodeURIComponent(id)}?remove=${remove}`,
  );
};

/**
 * The most useful thing to say about why a group of removals went wrong.
 *
 * Reaches for the API's own explanation first, then the body of a failed
 * request, then its message -- three shapes for the same question, depending
 * on how far the request got.
 */
const firstReason = (list) =>
  list[0]?.data?.error ??
  list[0]?.error?.response?.data ??
  list[0]?.error?.message ??
  'see the log';

/**
 * What to say about a batch of removals, when the server took files with them.
 *
 * Returns null when it did not -- a removal that deletes nothing answers 204,
 * carries no body, and needs no report: the rows going is the report.
 *
 * The removal and the deletion are **two independent facts** and the API
 * reports both: `removed` comes from the removal itself, not from the absence
 * of an error. Deriving one from the other is how "Removed, but could not
 * delete" came to be said over a request that removed nothing at all.
 *
 * Saying only "removed" over a deletion that did nothing is the other half of
 * the same mistake -- it is how a delete that silently did nothing comes to
 * look like one that worked.
 *
 * One message for the batch rather than one per file: removing eleven tracks
 * from a folder is one gesture, and eleven toasts is not a report.
 */
export const summariseDeletions = (results = []) => {
  const total = results.length;

  if (total === 0) {
    return null;
  }

  const failed = results.filter((r) => !r.ok);
  // a request that landed but removed nothing. the API refuses up front
  // everything it knows would stop a removal, so this should not happen -- and
  // is reported rather than assumed away precisely because "should not" is not
  // the same as "cannot"
  const notRemoved = results.filter((r) => r.ok && r.data && !r.data.removed);
  const deleted = results.filter((r) => r.ok && r.data?.deleted);
  // a download that never started reports success with no filename: nothing was
  // ever written, so the end state holds. it counts as fine and counts as
  // nothing to announce -- "deleted 3 files" over three transfers that never
  // wrote one would be an invention.
  const deletedFiles = deleted.filter((r) => r.data.filename);
  const refused = results.filter((r) => r.ok && r.data?.error);
  // deleted nothing and did not fail, which is now only ever the one case: no
  // path was recorded, so nothing is known about where the file is. a file that
  // was already gone reports success, because the end state asked for is the
  // end state there is.
  const unrecorded = results.filter(
    (r) => r.ok && r.data?.removed && !r.data.deleted && !r.data.error,
  );
  const pruned = results.reduce(
    (sum, r) => sum + (r.data?.prunedDirectories ?? 0),
    0,
  );
  // said only when there were any, and never as the headline: a folder is
  // bookkeeping, and the files are what the operator asked about
  const folders = pruned
    ? ` (${pruned} empty folder${pruned === 1 ? '' : 's'} removed)`
    : '';

  // a batch of plain removals: nothing was deleted and nothing claims to have
  // been, so there is nothing to say that the rows disappearing does not
  if (
    !deletedFiles.length &&
    !refused.length &&
    !unrecorded.length &&
    !failed.length &&
    !notRemoved.length
  ) {
    return null;
  }

  if (failed.length) {
    return {
      kind: 'error',
      message: `${failed.length} of ${total} could not be removed: ${firstReason(failed)}`,
    };
  }

  if (notRemoved.length) {
    return {
      kind: 'error',
      message: `${notRemoved.length} of ${total} were not removed${
        deletedFiles.length
          ? `, though ${deletedFiles.length} file(s) were deleted`
          : ''
      }`,
    };
  }

  if (refused.length) {
    return {
      kind: 'error',
      message: `Removed ${total}, but ${refused.length} file(s) could not be deleted: ${firstReason(refused)}`,
    };
  }

  if (unrecorded.length) {
    return {
      kind: 'warning',
      message: deletedFiles.length
        ? `Removed ${total} and deleted ${deletedFiles.length}; there is no record of where the other ${unrecorded.length} were written${folders}`
        : `Removed ${total}, but deleted nothing: there is no record of where these were written`,
    };
  }

  return {
    kind: 'success',
    message: `Removed ${total} and deleted ${deletedFiles.length === 1 ? 'the file' : `${deletedFiles.length} files`}${folders}`,
  };
};

/**
 * Fetches the file a completed download produced, and hands it to the browser
 * as a save.
 *
 * The transfer is named by its **id**. The path of the file is resolved
 * server-side, from what the application recorded when it wrote it -- no path
 * is sent from here, which is what makes a traversal impossible rather than
 * merely guarded against.
 *
 * The response is read into a Blob before it is saved, because the API is
 * authenticated with a bearer token and a plain `<a download>` link cannot
 * carry one. The endpoint itself streams and serves ranges, so a deployment
 * that authenticates some other way -- a reverse proxy in front of it, or
 * `no_auth` -- can fetch the URL directly and stream it without this hop.
 * @param {object} params
 * @param {string} params.username - The user the download came from.
 * @param {string} params.id - The id of the download.
 * @param {string} params.filename - The name to save as.
 * @returns {Promise<void>} Resolves once the save has been handed to the browser.
 */
export const retrieveFile = async ({ username, id, filename }) => {
  const response = await api.get(
    `/transfers/downloads/${encodeURIComponent(username)}/${encodeURIComponent(id)}/file`,
    { responseType: 'blob' },
  );

  downloadFile(response.data, filename, response.headers['content-type']);
};

/**
 * What to say when a retrieval fails.
 *
 * The request asks for a Blob, so axios hands back a *Blob* on an error
 * response too -- the body of a 403 is a Blob, not a string, and toasting it
 * would print '[object Blob]'. The status is the part that is readable without
 * unpacking it, and it is the part that says what to do next.
 */
/**
 * Says what a retrieval will do, for the tooltip on every control that starts
 * one.
 *
 * Shared, and deliberately so: "download" already means a Soulseek transfer
 * everywhere else in this application, and the one word doing two jobs is
 * exactly what needs explaining. The row icon and the button over a selection
 * must not drift into describing the same act differently.
 */
export const describeRetrieval = (count = 1) =>
  count > 1
    ? `Download these ${count} files to your browser, as one zip`
    : 'Download this file to your browser';

export const describeRetrievalError = (error) => {
  switch (error?.response?.status) {
    case 403:
      return 'Downloading files to the browser is not enabled on this server (remote_file_retrieval)';
    case 404:
      return 'There is no file on disk for this download';
    default:
      return error?.message ?? 'the file could not be retrieved';
  }
};

/**
 * Whether a failed retrieval means the file is gone, rather than something that
 * might work next time.
 *
 * Worth knowing because a file *going* is the normal end of a download's life
 * here -- something downstream moves finished files into a library -- so a row
 * whose file has gone is not an error state to retry, it is a button that
 * should stop offering itself.
 */
export const isRetrievalPermanentlyGone = (error) =>
  error?.response?.status === 404;

/**
 * Asks which of the specified downloads still have a file that could go into an
 * archive.
 *
 * Asked before an archive is started rather than discovered during it: the
 * archive is streamed, so once it has begun there is no way left to tell the
 * operator that a third of what they picked was not there.
 * @param {object} params
 * @param {string} params.username - The user the downloads came from.
 * @param {string[]} params.ids - The ids of the downloads.
 * @returns {Promise<{available: {id: string, filename: string}[], missing: {id: string, filename: string}[]}>} What is there and what is not.
 */
export const archiveAvailability = async ({ username, ids }) => {
  const response = await api.post(
    `/transfers/downloads/${encodeURIComponent(username)}/archive/availability`,
    { ids },
  );

  return response.data;
};

/**
 * Sends the browser to `url` to be downloaded, without navigating this page.
 *
 * A hidden iframe rather than assigning `location`: on success the response is
 * an attachment and nothing navigates either way, but on a failure it is a
 * page -- and assigning `location` would replace the running app with it. The
 * cost is that such a failure is invisible here, which is the right trade when
 * the ticket being used was issued a moment ago and for exactly this request.
 *
 * The frame is left in place for a while because removing it before the
 * response has begun cancels the download.
 */
const streamToBrowser = (url) => {
  const frame = document.createElement('iframe');

  frame.style.display = 'none';
  frame.src = url;

  document.body.append(frame);

  setTimeout(() => frame.remove(), 60_000);
};

/**
 * Fetches an archive of the specified downloads, and hands it to the browser's
 * own download manager.
 *
 * Unlike the single-file path this does **not** read the response in this tab.
 * An album is too big to hold in memory, so the browser is sent to the URL
 * instead and streams it to disk itself. A navigation cannot carry an
 * Authorization header, which is what the ticket is for: it is asked for here,
 * over the authenticated API, and spent immediately.
 * @param {object} params
 * @param {string} params.username - The user the downloads came from.
 * @param {string[]} params.ids - The ids of the downloads.
 * @returns {Promise<void>} Resolves once the browser has been sent to the archive.
 */
export const retrieveArchive = async ({ username, ids }) => {
  const { ticket } = (
    await api.post(
      `/transfers/downloads/${encodeURIComponent(username)}/archive/ticket`,
      { ids },
    )
  ).data;

  const url = `${apiBaseUrl}/transfers/downloads/${encodeURIComponent(
    username,
  )}/archive?ticket=${encodeURIComponent(ticket)}`;

  streamToBrowser(url);
};

/**
 * What to say when an archive could not be started.
 *
 * These responses are JSON, not Blobs -- the pre-flight and the ticket are
 * ordinary API calls -- so unlike a single-file retrieval the body is readable
 * and worth reaching for first.
 */
export const describeArchiveError = (error) =>
  error?.response?.data ??
  (error?.response?.status === 403
    ? 'Downloading files to the browser is not enabled on this server (remote_file_retrieval)'
    : error?.message ?? 'the archive could not be started');

export const clearCompleted = ({ direction }) => {
  return api.delete(`/transfers/${direction}s/all/completed`);
};

export const getPlaceInQueue = ({ username, id }) => {
  return api.get(
    `/transfers/downloads/${encodeURIComponent(username)}/${encodeURIComponent(id)}/position`,
  );
};

/*
  states: 

  'Requested'
  'Queued, Remotely'
  'Queued, Locally'
  'Initializing'
  'InProgress'
  'Completed, Succeeded'
  'Completed, Cancelled'
  'Completed, TimedOut'
  'Completed, Errored'
  'Completed, Rejected'
*/
export const isStateRetryable = (state) =>
  state.includes('Completed') && state !== 'Completed, Succeeded';

export const isStateCancellable = (state) =>
  [
    'InProgress',
    'Requested',
    'Queued',
    'Queued, Remotely',
    'Queued, Locally',
    'Initializing',
  ].find((s) => s === state);

export const isStateRemovable = (state) => state.includes('Completed');

/**
 * The orders the transfer list can be shown in.
 *
 * Two, and they are inverses: the question the page cannot answer without this
 * is "what did I just add", and the answer is at one end of the list or the
 * other.
 */
export const SORT_OPTIONS = [
  { key: 'newest', text: 'Newest', value: 'newest' },
  { key: 'oldest', text: 'Oldest', value: 'oldest' },
];

export const DEFAULT_SORT = 'newest';

const isSortOption = (value) =>
  SORT_OPTIONS.some((option) => option.value === value);

/**
 * Which order to show the list in, from what this browser chose and what the
 * server's configured default says.
 *
 * The stored choice wins where there is one -- it is the operator's own, made
 * here. Where there is none the configured default applies, which is what lets
 * `transfers.<direction>.default_sort` reach a browser that has never touched
 * the control, and keep reaching it: options arrive over the hub rather than at
 * page load, so a default edited in System -> Options lands without a reload.
 *
 * The corollary is worth knowing: once an order has been picked in a browser,
 * that browser stops following the setting. A default only defaults.
 *
 * Anything unrecognised on either side is discarded rather than honoured. The
 * server validates its own value against the same two names, so a bad one
 * should not arrive; a stored one can be whatever a past or future version left
 * behind.
 */
export const resolveSort = (stored, configured) =>
  [stored, configured].find((value) => isSortOption(value)) ?? DEFAULT_SORT;

/**
 * When a transfer was asked for, as a number, or 0 when that cannot be read.
 *
 * `requestedAt` is set once, server-side, and never moves again -- which is the
 * whole reason to sort on it. `startedAt` and `endedAt` do move, and this list
 * re-fetches every second, so a card would climb the page out from under the
 * pointer as its transfer progressed.
 */
const requestedAt = (file) => {
  const at = Date.parse(file?.requestedAt);

  return Number.isNaN(at) ? 0 : at;
};

/**
 * The instant that stands for a whole group of transfers.
 *
 * Newest-first takes the group's *newest* transfer, so a folder that takes
 * delivery of another file climbs back to the top; oldest-first takes its
 * oldest, which reproduces the order the API hands the groups over in. Reading
 * the same end of the range for both would make one of the two orders a lie.
 *
 * A group with nothing in it gets the far end of the range instead, so it sinks
 * to the bottom whichever way the list is turned. The sentinel is finite on
 * purpose: two empty groups compared as infinities give NaN, and a comparator
 * that returns NaN has no defined behaviour.
 */
const groupInstant = (instants, oldestFirst) =>
  instants.reduce(
    (best, at) => (oldestFirst ? Math.min(best, at) : Math.max(best, at)),
    oldestFirst ? Number.MAX_SAFE_INTEGER : Number.MIN_SAFE_INTEGER,
  );

/**
 * Orders two decorated groups, falling back to their names.
 *
 * The tiebreak matters more than it looks: everything enqueued in one gesture
 * shares an instant to the millisecond, and without a second key their order
 * would be whatever the API happened to return this second.
 */
const compareGroups = (oldestFirst) => (a, b) =>
  (oldestFirst ? a.at - b.at : b.at - a.at) || a.name.localeCompare(b.name);

/**
 * Orders the transfer list: users, and the folders within each user.
 *
 * Files inside a folder are left alone. They were all asked for in the same
 * gesture, so ordering them by time only reverses the track listing of an
 * album -- and the folder itself already carries its newest file's instant, so
 * a folder that gains one is found at the top of the page rather than by
 * reading down it.
 *
 * Returns new arrays throughout; the response this is given is the one held in
 * state, and `Array.prototype.sort` would reorder it in place.
 */
export const sortTransfers = (transfers = [], sort = DEFAULT_SORT) => {
  const oldestFirst = sort === 'oldest';
  const compare = compareGroups(oldestFirst);

  return transfers
    .map((user) => {
      const directories = (user.directories ?? [])
        .map((directory) => ({
          at: groupInstant(
            (directory.files ?? []).map((file) => requestedAt(file)),
            oldestFirst,
          ),
          name: directory.directory ?? '',
          value: directory,
        }))
        .sort(compare);

      return {
        at: groupInstant(
          directories.map((directory) => directory.at),
          oldestFirst,
        ),
        name: user.username ?? '',
        value: {
          ...user,
          directories: directories.map((directory) => directory.value),
        },
      };
    })
    .sort(compare)
    .map((user) => user.value);
};
