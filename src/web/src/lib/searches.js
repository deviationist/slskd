import api from './api';
import { defaultColumns } from './tables';
import {
  formatAttributes,
  getDirectoryName,
  getFileExtension,
  getFileName,
} from './util';

/**
 * Whether a phrase can be searched for.
 *
 * The plus and magnifier buttons learn this from the server, which refuses the
 * request; the watch button never reaches the server, because a watch is set up
 * before the search is created. The rule is the server's own -- null, empty, or
 * only whitespace -- and so is the wording, so that the three buttons refuse
 * the same thing in the same words.
 *
 * See SearchRequest.Validate in src/slskd/Search/API/DTO/SearchRequest.cs.
 * @param {string} searchText - The phrase.
 * @returns {{ok: boolean, reason?: string}} Whether it can be searched for.
 */
export const validateSearchText = (searchText) => {
  if (!searchText || searchText.trim().length === 0) {
    return {
      ok: false,
      reason:
        'The field SearchText can not be null, empty, or consist of only whitespace',
    };
  }

  return { ok: true };
};

export const getAll = async () => {
  return (await api.get('/searches')).data;
};

export const stop = ({ id }) => {
  return api.put(`/searches/${encodeURIComponent(id)}`);
};

export const remove = ({ id }) => {
  return api.delete(`/searches/${encodeURIComponent(id)}`);
};

export const create = ({ id, searchText }) => {
  return api.post('/searches', { id, searchText });
};

export const getStatus = async ({ id, includeResponses = false }) => {
  return (
    await api.get(
      `/searches/${encodeURIComponent(id)}?includeResponses=${includeResponses}`,
    )
  ).data;
};

export const getResponses = async ({ id }) => {
  const response = (
    await api.get(`/searches/${encodeURIComponent(id)}/responses`)
  ).data;

  if (!Array.isArray(response)) {
    console.warn('got non-array response from searches API', response);
    return undefined;
  }

  return response;
};

const getNthMatch = (string, regex, n) => {
  const match = string.match(regex);

  if (match) {
    return Number.parseInt(match[n], 10);
  }

  return undefined;
};

export const parseFiltersFromString = (string) => {
  const filters = {
    exclude: [],
    include: [],
    isCBR: false,
    isLossless: false,
    isLossy: false,
    isVBR: false,
    minBitDepth: 0,
    minBitRate: 0,
    minFilesInFolder: 0,
    minFileSize: 0,
    minLength: 0,
  };

  filters.minBitRate =
    getNthMatch(string, /(minbr|minbitrate):(\d+)/iu, 2) || filters.minBitRate;
  filters.minBitDepth =
    getNthMatch(string, /(minbd|minbitdepth):(\d+)/iu, 2) ||
    filters.minBitDepth;
  filters.minFileSize =
    getNthMatch(string, /(minfs|minfilesize):(\d+)/iu, 2) ||
    filters.minFileSize;
  filters.minLength =
    getNthMatch(string, /(minlen|minlength):(\d+)/iu, 2) || filters.minLength;
  filters.minFilesInFolder =
    getNthMatch(string, /(minfif|minfilesinfolder):(\d+)/iu, 2) ||
    filters.minFilesInFolder;

  filters.isVBR = Boolean(/isvbr/iu.test(string));
  filters.isCBR = Boolean(/iscbr/iu.test(string));
  filters.isLossless = Boolean(/islossless/iu.test(string));
  filters.isLossy = Boolean(/islossy/iu.test(string));

  const terms = string
    .toLowerCase()
    .split(' ')
    .filter(
      (term) =>
        !term.includes(':') &&
        term !== 'isvbr' &&
        term !== 'iscbr' &&
        term !== 'islossless' &&
        term !== 'islossy',
    );

  filters.include = terms.filter((term) => !term.startsWith('-'));
  filters.exclude = terms
    .filter((term) => term.startsWith('-'))
    .map((term) => term.slice(1));

  return filters;
};

export const filterResponse = ({
  filters = {
    exclude: [],
    include: [],
    isCBR: false,
    isLossless: false,
    isLossy: false,
    isVBR: false,
    minBitDepth: 0,
    minBitRate: 0,
    minFileSize: 0,
    minLength: 0,
  },
  response = {
    files: [],
    lockedFiles: [],
  },
}) => {
  const { files = [], lockedFiles = [] } = response;

  if (
    response.fileCount + response.lockedFileCount <
    filters.minFilesInFolder
  ) {
    return { ...response, files: [] };
  }

  const filterFiles = (filesToFilter) =>
    filesToFilter.filter((file) => {
      const {
        bitRate,
        size,
        length,
        filename,
        sampleRate,
        bitDepth,
        isVariableBitRate,
      } = file;
      const {
        isCBR,
        isVBR,
        isLossless,
        isLossy,
        minBitRate,
        minBitDepth,
        minFileSize,
        minLength,
        include = [],
        exclude = [],
      } = filters;

      if (isCBR && (isVariableBitRate === undefined || isVariableBitRate))
        return false;
      if (isVBR && (isVariableBitRate === undefined || !isVariableBitRate))
        return false;
      if (isLossless && (!sampleRate || !bitDepth)) return false;
      if (isLossy && (sampleRate || bitDepth)) return false;
      if (bitRate < minBitRate) return false;
      if (bitDepth < minBitDepth) return false;
      if (size < minFileSize) return false;
      if (length < minLength) return false;

      if (
        include.length > 0 &&
        include.filter((term) => filename.toLowerCase().includes(term))
          .length !== include.length
      ) {
        return false;
      }

      if (exclude.some((term) => filename.toLowerCase().includes(term)))
        return false;

      return true;
    });

  const filteredFiles = filterFiles(files);
  const filteredLockedFiles = filterFiles(lockedFiles);

  return {
    ...response,
    fileCount: filteredFiles.length,
    files: filteredFiles,
    lockedFileCount: filteredLockedFiles.length,
    lockedFiles: filteredLockedFiles,
  };
};

/*
 * The results, grouped by the user who holds them, are the only shape the
 * search page has ever had -- which makes "the biggest file in these results"
 * a question it cannot answer, because size lives on a file and the sort lives
 * on a response. Flattening is what puts every file in one list, where a
 * comparison between two of them is possible at all.
 *
 * Flatten *after* filtering, never before: `filterResponse`, the locked-file
 * toggle and the free-slot toggle all work on responses, so a list built from
 * the responses that survived them inherits every filter for free. Building it
 * from the raw results instead would mean reimplementing all three.
 */

/**
 * The identity of a file in a flattened list.
 *
 * A filename is not unique -- the same release sits on dozens of peers, which
 * is the normal case rather than a collision -- so the row is keyed on who has
 * it as well. A newline is the separator because it is the one character a
 * Soulseek path will not contain.
 * @param {object} params
 * @param {string} params.username - The peer holding the file.
 * @param {string} params.filename - Its full remote path.
 * @returns {string} A key unique within one search's results.
 */
export const fileKey = ({ username, filename }) => `${username}\n${filename}`;

/**
 * One row of a flattened list: the file, plus what the peer contributes.
 * @param {object} response - The response the file came from.
 * @param {object} file - The file.
 * @param {boolean} locked - Whether it came from the locked collection.
 * @returns {object} The row.
 */
const rowFor = (response, file, locked) => ({
  ...file,
  hasFreeUploadSlot: response.hasFreeUploadSlot,
  key: fileKey({ filename: file.filename, username: response.username }),
  locked,
  queueLength: response.queueLength,
  uploadSpeed: response.uploadSpeed,
  username: response.username,
});

/**
 * Every file in every response, as one list.
 *
 * Each row carries the fields a sort might use -- size, bitrate, length -- and
 * the ones that belong to the peer rather than the file, since a flat list has
 * nowhere else to show who has it or whether they can send it now.
 * @param {object} params
 * @param {object[]} params.responses - Responses, already filtered.
 * @returns {object[]} One row per file, locked files included and marked.
 */
export const flattenResponses = ({ responses = [] }) =>
  responses.flatMap((response) => [
    ...(response.files ?? []).map((file) => rowFor(response, file, false)),

    // locked files are a separate collection on the response rather than a
    // flag on the file, so the flag is applied here -- the grouped view does
    // the same thing in its own tree builder
    ...(response.lockedFiles ?? []).map((file) => rowFor(response, file, true)),
  ]);

/**
 * Groups selected rows back into one download request per peer.
 *
 * Selecting across users is the point of a flat list, and the transfer API
 * takes one user at a time -- so a selection of twenty files from three peers
 * is three requests, not twenty and not one.
 * @param {object[]} rows - The selected rows.
 * @returns {{username: string, files: {filename: string, size: number}[]}[]} One entry per peer.
 */
export const groupByUser = (rows = []) => {
  const byUser = new Map();

  for (const { username, filename, size } of rows) {
    if (!byUser.has(username)) {
      byUser.set(username, []);
    }

    byUser.get(username).push({ filename, size });
  }

  return [...byUser].map(([username, files]) => ({ files, username }));
};

/*
 * Which search results you have already asked for, and which you already have.
 *
 * Two questions, and they deserve different confidence. A transfer records the
 * peer and the exact remote path, so "this row is that download" is a certainty
 * -- same key on both sides. "I already have this track" is a guess: the only
 * thing shared between a result from one peer and a file you took from another
 * is the name, and names like `01 - Intro.mp3` and `Cover.jpg` are not
 * evidence of anything.
 *
 * So the guess is qualified by size as well as name. Two files with the same
 * name and byte count are the same file often enough to be worth saying, and
 * the pairing is what stops every album's artwork lighting up at once.
 *
 * Both are only as complete as the transfer list: slskd keeps a download in it
 * until it is cleared, so clearing the list is also forgetting that any of this
 * was ever downloaded. There is no deeper record to consult -- which is worth
 * knowing before reading an unmarked row as "not downloaded".
 */

/**
 * A file's identity across peers: what it is called, and how big it is.
 */
/*
 * Undefined where there is no name to take, rather than throwing. This runs per
 * row on every render of a list of hundreds, and a search result is data from a
 * stranger's client -- one malformed entry would otherwise take the page down.
 */
const signatureOf = ({ filename, size }) =>
  filename === undefined || filename === null
    ? undefined
    : `${getFileName(filename)}\n${size}`;

/**
 * Sorts a transfer's state into the three that matter to a search result.
 * @param {string} state - The transfer state.
 * @returns {string} One of 'downloaded', 'failed' or 'downloading'.
 */
const outcomeOf = (state = '') => {
  if (state === 'Completed, Succeeded') {
    return 'downloaded';
  }

  // every other Completed is a way of not having the file: errored, cancelled,
  // timed out, rejected. worth marking rather than hiding, since the row is
  // then a second chance at the same file rather than a repeat of a success
  if (state.startsWith('Completed')) {
    return 'failed';
  }

  return 'downloading';
};

/**
 * Indexes the downloads so a search row can be looked up in constant time.
 * @param {object[]} users - The downloads API's response: users, directories, files.
 * @returns {{byFile: Map<string, string>, bySignature: Map<string, string>}} The index.
 */
export const indexDownloads = (users = []) => {
  const byFile = new Map();
  const bySignature = new Map();

  for (const user of users) {
    for (const directory of user.directories ?? []) {
      for (const file of directory.files ?? []) {
        const outcome = outcomeOf(file.state);

        // the transfer's id and whether its file is still there, not just the
        // outcome: a row that has been downloaded offers to fetch the file to
        // the browser, and that needs the id -- or to fetch it again, and that
        // needs to know the first attempt's file has gone
        byFile.set(
          fileKey({ filename: file.filename, username: file.username }),
          {
            id: file.id,
            localFileExists: file.localFileExists,
            outcome,
          },
        );

        // only successes: a failed download from one peer says nothing about
        // whether the same file from another peer is worth having
        const signature = signatureOf(file);

        if (outcome === 'downloaded' && signature !== undefined) {
          bySignature.set(signature, { outcome });
        }
      }
    }
  }

  return { byFile, bySignature };
};

/**
 * What a search row should say about itself, given what has been downloaded.
 * @param {object} params
 * @param {object} params.row - A flattened search row.
 * @param {object} params.index - The result of `indexDownloads`.
 * @returns {string|undefined} 'downloaded', 'downloading', 'failed', 'have', or nothing.
 */
export const downloadStateOf = ({ row, index }) => {
  if (!row || !index) {
    return undefined;
  }

  // the exact file from the exact peer outranks the guess, always: it is the
  // only one of the two that is certain, and it can say 'failed' where the
  // guess would have said nothing at all
  const exact = index.byFile.get(fileKey(row));

  if (exact) {
    return exact.outcome;
  }

  const signature = signatureOf(row);

  return signature !== undefined && index.bySignature.has(signature)
    ? 'have'
    : undefined;
};

/**
 * The folder a search result sits in: the whole path, minus the filename.
 *
 * All of it, not just the last segment. Someone else's library says where a
 * file came from as much as what it is next to -- `@@abcde\\Music\\FLAC\\Artist
 * - Album (2003)` is a different thing from the same album under `incoming`.
 * The column has the width for it now, and truncates from the end when it
 * does not.
 * @param {object} params
 * @param {string} params.filename - The full remote path.
 * @returns {string} The directory, or '' where the file has none.
 */
export const pathOf = ({ filename }) => {
  if (!filename) {
    return '';
  }

  const directory = getDirectoryName(filename);

  // getDirectoryName hands back the whole path when there is no separator in
  // it -- a file at the root of a share -- and that is a filename, not a path
  return directory === filename ? '' : directory;
};

/*
 * Sorting the flat list.
 *
 * The dropdown above the results sorts *responses* -- whole peers, by upload
 * speed or queue depth -- which is the only thing that can be sorted when the
 * results are drawn as one card per peer. A table of files can be asked a
 * different question, and the columns are where it is asked.
 *
 * Sorted from the values, never from what the cell shows: a length reads
 * `6:09` and sorts as 369, a size reads `9.2 MB` and sorts as its bytes.
 * Sorting the rendered text would put 10:00 before 6:09 and 9 MB before 80 KB.
 */

/**
 * How each column is compared, and what it is compared on.
 */
export const SORT_COLUMNS = {
  attributes: { kind: 'text', of: (row) => formatAttributes(row) },
  ext: { kind: 'text', of: (row) => getFileExtension(row.filename ?? '') },
  length: { kind: 'number', of: (row) => row.length },
  name: { kind: 'text', of: (row) => getFileName(row.filename ?? '') },
  path: { kind: 'text', of: (row) => pathOf(row) },
  queue: { kind: 'number', of: (row) => row.queueLength },
  size: { kind: 'number', of: (row) => row.size },

  // as a number, so it orders rather than merely groups: ascending puts the
  // peers who cannot send now first, which is why the useful click is the
  // second one
  slot: { kind: 'number', of: (row) => (row.hasFreeUploadSlot ? 1 : 0) },
  speed: { kind: 'number', of: (row) => row.uploadSpeed },
  user: { kind: 'text', of: (row) => row.username },
};

/*
 * Which columns the table shows.
 *
 * Three of them describe the *peer* rather than the file -- how fast they
 * upload, whether they can send now, how long their queue is. They decide
 * which copy of a file to take, which is a question worth asking and not one
 * worth nine columns of width while you are not asking it. So they exist, and
 * they are off until switched on.
 *
 * The order is fixed here rather than taken from what was stored: a column
 * list is a set, and letting a saved value decide the order would mean a
 * reordering nobody asked for surviving in a browser forever.
 */

/**
 * Every column, in the order they are drawn.
 */
export const COLUMNS = [
  { key: 'name', label: 'File', className: 'flatlist-filename' },
  { key: 'ext', label: 'Ext', className: 'flatlist-ext', optional: true },
  { key: 'path', label: 'Path', className: 'flatlist-path' },
  { key: 'user', label: 'User', className: 'flatlist-user' },
  { key: 'size', label: 'Size', className: 'flatlist-size' },
  { key: 'attributes', label: 'Attributes', className: 'flatlist-attributes' },
  { key: 'length', label: 'Length', className: 'flatlist-length' },
  { key: 'speed', label: 'Speed', className: 'flatlist-speed', optional: true },
  {
    key: 'slot',
    label: 'Free Slot',
    className: 'flatlist-slot',
    optional: true,
  },
  { key: 'queue', label: 'Queue', className: 'flatlist-queue', optional: true },
];

/**
 * The columns shown to someone who has never touched the setting.
 */
export const DEFAULT_COLUMNS = defaultColumns(COLUMNS);

/**
 * What the action on a search row should offer, and why.
 *
 * Three answers, and the difference between the last two is the whole point:
 * a file this peer already gave us is worth *fetching to the browser* rather
 * than asking for again, but only while it is still on disk. Once it is gone
 * -- moved into the library, cleared out -- the only thing left to offer is
 * asking the peer again.
 *
 * `retrieve` needs the transfer's id, which is why the index carries it.
 * @param {object} params
 * @param {object} params.row - A flattened search row.
 * @param {object} params.index - The result of `indexDownloads`.
 * @param {boolean} params.retrievalEnabled - Whether the server allows fetching a file out.
 * @returns {{kind: string, id?: string, tip: string}} What to draw and what it does.
 */
export const rowActionOf = ({ row, index, retrievalEnabled = false }) => {
  const entry = index?.byFile?.get(fileKey(row ?? {}));

  if (entry?.outcome === 'downloaded') {
    if (retrievalEnabled && entry.localFileExists) {
      return {
        id: entry.id,
        kind: 'retrieve',
        tip: 'Already downloaded — fetch the file to this browser',
      };
    }

    return {
      kind: 'redownload',
      // said plainly, because the row is marked as downloaded and the button
      // appearing to disagree with the mark is worth explaining rather than
      // leaving as a puzzle
      tip: entry.localFileExists
        ? 'Already downloaded — download it again'
        : 'Downloaded before, but the file is gone — download it again',
    };
  }

  return { kind: 'enqueue', tip: `Download this file from ${row?.username}` };
};
