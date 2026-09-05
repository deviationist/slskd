import api from './api';

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
  const refused = results.filter((r) => r.ok && r.data?.error);
  // no path recorded: the download finished before slskd started recording
  // where it wrote, so there is nothing it can honestly delete
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
    !deleted.length &&
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
        deleted.length ? `, though ${deleted.length} file(s) were deleted` : ''
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
      message: deleted.length
        ? `Removed ${total} and deleted ${deleted.length}; there is no record of where the other ${unrecorded.length} were written${folders}`
        : `Removed ${total}, but deleted nothing: there is no record of where these were written`,
    };
  }

  return {
    kind: 'success',
    message: `Removed ${total} and deleted ${deleted.length === 1 ? 'the file' : `${deleted.length} files`}${folders}`,
  };
};

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
