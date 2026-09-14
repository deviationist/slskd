import { apiBaseUrl } from '../config';
import api from './api';
import { downloadFile, getFileName } from './util';

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
 * Whether a transfer got as far as producing a file.
 *
 * Whether that file can still be handed back is a separate question -- see
 * `isRetrievable` -- but this is the set of rows the retrieval column has
 * anything at all to say about. A cancelled or errored transfer produced
 * nothing, and its own state column already says so.
 * @param {object} file - The transfer.
 * @returns {boolean} Whether it finished and produced a file.
 */
export const isFinishedDownload = (file) =>
  file.direction === 'Download' && file.state === 'Completed, Succeeded';

/**
 * Whether the server could hand this transfer's file back.
 *
 * `localFilename` is null for downloads that finished before this application
 * recorded where it wrote them; the server answers 404 for those, and a button
 * that is always refused is worse than no button.
 * @param {object} file - The transfer.
 * @returns {boolean} Whether it can be fetched.
 */
export const isRetrievable = (file) =>
  isFinishedDownload(file) && Boolean(file.localFilename);

/**
 * Whether this row's file is known to have gone.
 *
 * Two sources, and the order matters. `localFileExists` is the server's answer
 * as of the last listing, which is cached and so may be up to half a minute
 * behind the filesystem. A retrieval that has actually been refused is proof,
 * and outranks it. Anything else -- including a server that did not answer at
 * all -- leaves the row alone.
 * @param {object} params
 * @param {object} params.file - The transfer.
 * @param {Set<string>} params.refused - Ids whose retrieval has been refused.
 * @returns {boolean} Whether the file is known to be gone.
 */
export const isFileGone = ({ file, refused = new Set() }) =>
  refused.has(file.id) || file.localFileExists === false;

/**
 * Decides how a selection should be fetched.
 *
 * One file is a file, not an archive of one: zipping it would make the operator
 * unwrap something to get back exactly what they picked. Everything else is an
 * archive, including a selection that is down to one file only because the rest
 * of it has gone.
 * @param {object[]} files - The selected transfers.
 * @returns {{mode: 'none'|'file'|'archive', file?: object, files?: object[]}} What to do.
 */
export const chooseRetrieval = (files = []) => {
  const retrievable = files.filter((file) => isRetrievable(file));

  if (retrievable.length === 0) {
    return { mode: 'none' };
  }

  if (retrievable.length === 1) {
    return { file: retrievable[0], mode: 'file' };
  }

  return { files: retrievable, mode: 'archive' };
};

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

/**
 * Says why a completed download cannot be fetched, or undefined if it can.
 *
 * Two different facts, and they must not be told as one. A path the server has
 * already refused is a file that has *gone*; a download with no recorded path
 * is one this application never knew the location of, and whose file may well
 * still be sitting on disk. Saying "missing" for the second would be a guess
 * presented as a fact.
 */
export const describeUnretrievable = ({ file, gone = false }) => {
  if (gone) {
    return 'This file is no longer on disk';
  }

  if (!file.localFilename) {
    return 'This download finished before slskd recorded where it saved files, so it cannot be fetched';
  }

  return undefined;
};

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
 * Whether removing this transfer would take a file off the disk with it.
 *
 * Three things have to hold, and the third is the one easy to miss:
 * `localFilename` is where this application recorded writing the bytes, and it
 * is the only path a removal can reach. A download that has none -- one that
 * finished before that column existed, or that never wrote anything -- leaves
 * the removal with nothing to delete, which is exactly what
 * `summariseDeletions` says about it afterwards.
 *
 * `localFileExists` is deliberately **not** consulted. It is the server's
 * answer as of the last listing and can be a little behind the filesystem, and
 * the two ways of being wrong here are not equal: a stale `true` costs a
 * dialog nobody needed, a stale `false` costs the file this dialog exists to
 * protect.
 *
 * The option is read as `!== false` rather than as a truth for the same
 * reason. It arrives over the application hub, so it is *undefined* for the
 * first moments this page is on screen -- and treating not-yet-known as
 * not-deleting would drop the confirmation in the one window where nothing can
 * show it is unnecessary.
 * @param {object} params
 * @param {object} params.file - The transfer.
 * @param {boolean} [params.deleteFileOnRemoval] - Whether the server deletes files on removal; undefined where that is not yet known.
 * @returns {boolean} Whether a removal would delete a file.
 */
/**
 * Whether a keypress should confirm a removal dialog.
 *
 * Escape is Semantic's own; Enter is not, and it needs more care than it looks.
 * A held key repeats, and a dialog that acted on every repeat would delete and
 * then act again on whatever took its place. And the browser already activates
 * a focused button on Enter -- so acting again when focus is on Cancel would
 * both cancel and confirm, which is the worst possible pair.
 * @param {object} params
 * @param {string} params.key - The key pressed.
 * @param {boolean} params.repeat - Whether this is a key-repeat.
 * @param {boolean} params.busy - Whether the removal is already running.
 * @param {string} params.targetTag - The tag name of the focused element.
 * @returns {boolean} Whether it should confirm.
 */
export const confirmsRemoval = ({
  busy = false,
  key,
  repeat = false,
  targetTag = '',
}) =>
  key === 'Enter' &&
  !repeat &&
  !busy &&
  !['A', 'BUTTON', 'INPUT', 'SELECT', 'TEXTAREA'].includes(
    (targetTag ?? '').toUpperCase(),
  );

export const removalDeletesFile = ({ file, deleteFileOnRemoval }) =>
  deleteFileOnRemoval !== false &&
  file?.direction === 'Download' &&
  Boolean(file?.localFilename);

/**
 * What a selection's *Remove* must ask before it runs.
 *
 * The same rule the row applies, over a set: ask only where files would
 * actually be deleted, and name them. A selection is the more destructive of
 * the two paths -- it can take a folder's worth at once -- so the thing worth
 * showing is how many, and which.
 *
 * The list is capped. A confirmation nobody reads to the end is a confirmation
 * nobody read, and the count carries the weight anyway.
 * @param {object} params
 * @param {object[]} params.files - The selected transfers.
 * @param {boolean} [params.deleteFileOnRemoval] - Whether the server deletes files on removal.
 * @param {number} [params.limit] - The most paths to name.
 * @returns {{confirm: boolean, deleting: number, filenames: string[], remaining: number, header?: string, prompt?: string, confirmLabel?: string}} What to ask.
 */
export const planSelectionRemoval = ({
  deleteFileOnRemoval,
  files = [],
  limit = 10,
}) => {
  const deleting = files.filter((file) =>
    removalDeletesFile({ deleteFileOnRemoval, file }),
  );

  if (deleting.length === 0) {
    return { confirm: false, deleting: 0, filenames: [], remaining: 0 };
  }

  const filenames = deleting.slice(0, limit).map((file) => file.localFilename);

  return {
    confirm: true,
    confirmLabel:
      deleting.length === 1
        ? 'Remove and delete'
        : `Remove and delete ${deleting.length}`,
    deleting: deleting.length,
    filenames,
    header:
      deleting.length === 1
        ? 'Delete this file?'
        : `Delete ${deleting.length} files?`,
    prompt:
      deleting.length === 1
        ? 'Removing this download also deletes the file it wrote. This cannot be undone.'
        : `Removing these downloads also deletes the ${deleting.length} files they wrote. This cannot be undone.`,
    remaining: deleting.length - filenames.length,
  };
};

/**
 * What a row's own remove control offers, and what it must ask first.
 *
 * Which rows offer it is `isStateRemovable` and nothing else -- the same rule
 * the selection's *Remove* button applies, deliberately rather than a second
 * one that could drift from it. That rule is also half the safety argument:
 * only a `Completed` transfer is removable, so this control can never abort a
 * transfer that is still running, however badly it is aimed.
 *
 * The other half is the confirmation, and it is asked for **only** when the
 * removal would delete a file. A removal that deletes nothing destroys nothing
 * -- the row is a record of something already finished, and the transfer can
 * be enqueued again -- and a dialog raised over that would be dismissed
 * unread, which is precisely how the one that matters gets clicked through.
 * @param {object} params
 * @param {object} params.file - The transfer.
 * @param {boolean} [params.deleteFileOnRemoval] - Whether the server deletes files on removal; undefined where that is not yet known.
 * @returns {{offered: boolean, deletesFile: boolean, confirm: boolean, tooltip: string, header?: string, prompt?: string, filename?: string, confirmLabel?: string}} What the row offers.
 */
export const planRowRemoval = ({ file, deleteFileOnRemoval }) => {
  const offered = Boolean(file?.state && isStateRemovable(file.state));

  if (!removalDeletesFile({ deleteFileOnRemoval, file })) {
    return {
      confirm: false,
      deletesFile: false,
      offered,
      tooltip: 'Remove this transfer',
    };
  }

  return {
    confirm: true,
    confirmLabel: 'Remove and delete',
    deletesFile: true,
    // the path the server will delete, not the remote name the row shows. it is
    // the one thing that says which file this is about, and it is what makes a
    // row that shifted under the pointer visible before anything is destroyed
    filename: file.localFilename,
    header: 'Delete this file?',
    offered,
    prompt:
      'Removing this download also deletes the file it wrote. This cannot be undone.',
    tooltip: 'Remove this transfer, and delete the file it downloaded',
  };
};

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

/*
 * The transfers as one row per file.
 *
 * The page's own shape is user -> directory -> files, which is the right shape
 * for the question "what is this peer sending me" and the wrong one for "what
 * is the biggest thing in the queue". Flattening is what lets the second be
 * asked; the grouped view stays for the first.
 */

/**
 * Every file in every directory of every user, as one list.
 *
 * Keyed on the transfer's own id, which is already unique across peers -- so
 * unlike the search list there is nothing to construct, and two peers sending
 * the same path are two rows that cannot be confused for one.
 * @param {object[]} users - The transfers API's response.
 * @returns {object[]} One row per transfer, carrying its peer and folder.
 */
export const flattenTransfers = (users = []) =>
  users.flatMap((user) =>
    (user.directories ?? []).flatMap((directory) =>
      (directory.files ?? []).map((file) => ({
        ...file,
        directory: directory.directory,
        key: file.id,

        // the API repeats the username on the file; the user is the fallback
        // for a shape that does not
        username: file.username ?? user.username,
      })),
    ),
  );

/**
 * Every column the flat transfers table has, in the order they are drawn.
 */
export const TRANSFER_COLUMNS = [
  { key: 'name', label: 'File', className: 'flatlist-filename' },
  { key: 'path', label: 'Path', className: 'flatlist-path' },
  { key: 'user', label: 'User', className: 'flatlist-user' },
  { key: 'state', label: 'Progress', className: 'flatlist-progress' },
  { key: 'size', label: 'Size', className: 'flatlist-size' },
  { key: 'speed', label: 'Speed', className: 'flatlist-speed', optional: true },
  {
    key: 'attempts',
    label: 'Attempts',
    className: 'flatlist-attempts',
    optional: true,
  },
];

/**
 * What each of those columns is compared on.
 *
 * Progress sorts on the state's text rather than on percent complete: the
 * useful thing that column does is gather the failures together, and a
 * finished transfer and one that never started are both at 100 and 0 with
 * nothing in between to order.
 */
export const TRANSFER_SORT_COLUMNS = {
  attempts: { kind: 'number', of: (row) => row.attempts },
  name: { kind: 'text', of: (row) => getFileName(row.filename ?? '') },
  path: { kind: 'text', of: (row) => row.directory },
  size: { kind: 'number', of: (row) => row.size },
  speed: { kind: 'number', of: (row) => row.averageSpeed },
  state: { kind: 'text', of: (row) => row.state },
  user: { kind: 'text', of: (row) => row.username },
};

/**
 * The transfers a query matches, in the shape the page already holds them.
 *
 * Filters *files*, then drops the folders and peers left holding none -- so
 * the same answer serves both views: the cards show only the peers with a
 * match, and the table flattens what survives. One filter rather than one per
 * view, which is what stops the two disagreeing about what a query means.
 *
 * Space-separated terms, all of which must match, and a term with a leading
 * `-` must not. The same shape as the searches page's filter box, minus the
 * typed operators: `minbitrate:` has nothing to say about a transfer.
 *
 * Matched against the peer, the whole remote path and the state -- so `errored`
 * finds the failures and a username finds one peer's queue, without either
 * needing its own control.
 * @param {object} params
 * @param {object[]} params.users - The transfers API's response.
 * @param {string} params.query - What was typed.
 * @returns {object[]} The same shape, with what does not match removed.
 */
export const filterTransfers = ({ users = [], query = '' }) => {
  const terms = String(query).toLowerCase().split(/\s+/u).filter(Boolean);

  if (terms.length === 0) {
    return users;
  }

  const include = terms.filter((t) => !t.startsWith('-'));
  const exclude = terms
    .filter((t) => t.startsWith('-'))
    .map((t) => t.slice(1))
    .filter(Boolean);

  const matches = (file, username) => {
    const hay =
      `${username} ${file.filename ?? ''} ${file.state ?? ''}`.toLowerCase();

    return (
      include.every((t) => hay.includes(t)) &&
      !exclude.some((t) => hay.includes(t))
    );
  };

  return users
    .map((user) => ({
      ...user,
      directories: (user.directories ?? [])
        .map((directory) => {
          const files = (directory.files ?? []).filter((file) =>
            matches(file, file.username ?? user.username),
          );

          return { ...directory, fileCount: files.length, files };
        })
        .filter((directory) => directory.files.length > 0),
    }))
    .filter((user) => user.directories.length > 0);
};
