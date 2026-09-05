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
 * Cancels a transfer, optionally removing the record of it, and optionally
 * deleting the file it produced.
 *
 * `deleteFile` is only sent when it is asked for, so a caller that does not
 * know about it makes exactly the request it always made.
 *
 * It is only meaningful alongside `remove`: the option that permits it is
 * `delete_file_on_removal`, and deleting the file while keeping the record
 * leaves a transfer listed as a completed download whose file is not there.
 * The API rejects that combination; this refuses to send it.
 */
export const cancel = ({
  deleteFile = false,
  direction,
  id,
  remove = false,
  username,
}) => {
  const query = new URLSearchParams({ remove });

  if (deleteFile) {
    if (!remove) {
      throw new Error('deleteFile requires remove');
    }

    query.set('deleteFile', true);
  }

  return api.delete(
    `/transfers/${direction}s/${encodeURIComponent(username)}/${encodeURIComponent(id)}?${query}`,
  );
};

/**
 * What to say about a batch of removals that asked for the files to go too.
 *
 * Pure, so it can be tested without a DOM or a server: it takes one entry per
 * transfer, either `{ok: true, data}` carrying the API's FileDeletionResult or
 * `{ok: false, error}` for a request that did not land.
 *
 * There are four outcomes and they are not interchangeable. A file that was
 * deleted, a file the API refused to delete (the removal still happened -- the
 * API refuses up front anything that would stop it), a download with no
 * recorded path so nothing to delete, and a request that failed outright.
 * Saying only "removed" over any of the last three is how a delete that
 * silently did nothing comes to look like one that worked.
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
  const deleted = results.filter((r) => r.ok && r.data?.deleted);
  const refused = results.filter((r) => r.ok && r.data?.error);
  // no path recorded: the download finished before slskd started recording
  // where it wrote, so there is nothing it can honestly delete
  const unrecorded = results.filter(
    (r) => r.ok && r.data && !r.data.deleted && !r.data.error,
  );

  const reason = (list) =>
    list[0]?.data?.error ??
    list[0]?.error?.response?.data ??
    list[0]?.error?.message ??
    'see the log';

  if (failed.length) {
    return {
      kind: 'error',
      message: `${failed.length} of ${total} could not be removed: ${reason(failed)}`,
    };
  }

  if (refused.length) {
    return {
      kind: 'error',
      message: `Removed ${total}, but ${refused.length} file(s) could not be deleted: ${reason(refused)}`,
    };
  }

  if (unrecorded.length) {
    return {
      kind: 'warning',
      message: deleted.length
        ? `Removed ${total} and deleted ${deleted.length}; there is no record of where the other ${unrecorded.length} were written`
        : `Removed ${total}, but deleted nothing: there is no record of where these were written`,
    };
  }

  if (deleted.length) {
    return {
      kind: 'success',
      message: `Removed ${total} and deleted ${deleted.length === 1 ? 'the file' : `${deleted.length} files`}`,
    };
  }

  return null;
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
