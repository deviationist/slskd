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

export const cancel = ({ direction, username, id, remove = false }) => {
  return api.delete(
    `/transfers/${direction}s/${encodeURIComponent(username)}/${encodeURIComponent(id)}?remove=${remove}`,
  );
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
