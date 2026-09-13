import api from './api';

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

/**
 * What a select-all checkbox over a list of rows should show.
 *
 * Three states rather than two: a box that is merely unticked while half the
 * list is selected says the opposite of what is true. The count comes back
 * with it because the caller needs it in the same breath -- to label the
 * download, and to decide whether to offer clearing at all.
 * @param {object} params
 * @param {object[]} params.rows - Every row currently listed.
 * @param {Set<string>} params.selected - Keys of the selected rows.
 * @returns {{all: boolean, some: boolean, count: number}} The state of the box.
 */
export const selectionState = ({ rows = [], selected = new Set() }) => {
  const count = rows.filter((row) => selected.has(row.key)).length;

  return {
    all: rows.length > 0 && count === rows.length,
    count,

    // strictly between: `some` is what draws the dash, and a full selection
    // draws a tick instead
    some: count > 0 && count < rows.length,
  };
};
