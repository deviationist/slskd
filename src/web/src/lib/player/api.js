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

// how many downloads have an analysis saved, for good: { configured, downloads }
export const getSavedAnalyses = async () =>
  (await api.get('/player/analyses')).data;

// delete them all, so each download is analysed again when next shown
export const deleteSavedAnalyses = async () =>
  (await api.delete('/player/analyses')).data;

// absolute, because an element fetches it outside axios. `original` asks for
// the file as it is, where the browser has said it plays it; the server falls
// back to `codec` for a format it will not send as it is
export const audioUrl = (id, ticket, codec, { original = false } = {}) => {
  const query = new URLSearchParams({ ticket });

  if (codec) {
    query.set('codec', codec);
  }

  if (original) {
    query.set('original', 'true');
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
