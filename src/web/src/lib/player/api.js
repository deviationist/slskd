import { apiBaseUrl } from '../../config';
import api from '../api';

/*
 * The player's routes. Every one names a download by its id; the server
 * resolves the file from what the download recorded, so nothing here names a
 * path. See src/slskd/Player/API/PlayerController.cs.
 */

const base = (id) => `/player/${encodeURIComponent(id)}`;

// a stream ticket: what the <audio> and <img> carry instead of a header. one
// per player, held before anything is pressed -- see StreamTickets.cs for why
// per player, held before anything is pressed -- see StreamTickets.cs for why.
// it also says whether an analysis provider is configured: { ticket, analysis }
export const getTicket = async () => (await api.post('/player/ticket')).data;

export const getPeaks = async (id, { signal } = {}) =>
  (await api.get(`${base(id)}/peaks`, { signal })).data;

export const getTags = async (id, { signal } = {}) =>
  (await api.get(`${base(id)}/tags`, { signal })).data;

// { state: 'off' | 'ready' | 'pending' | 'none' | 'unavailable', analysis, retryAfter }
export const getAnalysis = async (id, { signal, wait = true } = {}) =>
  (
    await api.get(`${base(id)}/analysis`, {
      params: wait ? undefined : { wait: false },
      signal,
    })
  ).data;

// what is kept of every analysis, for good: { configured, downloads }
export const getAnalysisCache = async () =>
  (await api.get('/player/analysis/cache')).data;

// drop it all, so each download is analysed again when next shown
export const clearAnalysisCache = async () =>
  (await api.delete('/player/analysis/cache')).data;

// absolute, because an element fetches it outside axios
export const audioUrl = (id, ticket, codec) => {
  const query = new URLSearchParams({ ticket });

  if (codec) {
    query.set('codec', codec);
  }

  return `${apiBaseUrl}${base(id)}/audio?${query}`;
};

export const artUrl = (id, ticket) =>
  `${apiBaseUrl}${base(id)}/art?${new URLSearchParams({ ticket })}`;

// the reason a request failed, as the player says it
export const failure = (error) => {
  const status = error?.response?.status;

  if (status === 404) {
    return 'not on disk any more';
  }

  if (status === 409) {
    return 'still arriving';
  }

  if (status === 403) {
    return 'not a file the player will open';
  }

  return error?.response?.data && typeof error.response.data === 'string'
    ? error.response.data
    : 'could not load it';
};
