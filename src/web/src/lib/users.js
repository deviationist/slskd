import { urlBase } from '../config';
import api from './api';

/**
 * Where the Users page shows a user.
 *
 * Here rather than at each caller: the User column of two tables and anything
 * else with a username in hand all need the same address, and a page whose
 * route changes should not have to be chased through the components that link
 * to it.
 *
 * Encoded, because a Soulseek username may contain anything at all -- a space,
 * a slash, a percent sign -- and the route is one path segment.
 * @param {string} username - The user.
 * @returns {string} The path to link to.
 */
export const userPath = (username) =>
  `${urlBase}/users/${encodeURIComponent(username ?? '')}`;

/**
 * The presences the Soulseek server reports for a user who is connected.
 *
 * Everything else is `Offline`, which the server also answers for a name that
 * has never existed -- see `describeLookup`.
 */
const CONNECTED = new Set(['Online', 'Away']);

/**
 * What a user lookup found, and what to say about it.
 *
 * Three requests are made and they do not fail together. A user who is
 * connected answers all three; one who is not answers only `status`, because
 * their description and their address are read from *them* rather than from
 * the server, and there is nobody there to ask. Asking for all three with
 * `Promise.all` discarded the one answer that did come back, and reported the
 * lookup as having failed -- when it had in fact succeeded, and the answer was
 * "offline".
 *
 * What can be said about whether the name exists at all:
 *
 * - `Online` or `Away` settles it. They are connected now.
 * - `isPrivileged` settles it too, even while they are offline: it is account
 *   state the *server* holds, and it holds none for a name nobody has used.
 * - Otherwise nothing can be said. The server answers a status request for any
 *   name at all and reports one that has never existed exactly as it reports
 *   one that is merely offline. Measured, not assumed: an invented name and a
 *   real dormant one come back byte-identical. A misspelling looks like this.
 * @param {object} params
 * @param {string} params.username - The user looked up.
 * @param {{ok: boolean, data?: object, reason?: string}} params.info - The info request.
 * @param {{ok: boolean, data?: object, reason?: string}} params.status - The status request.
 * @param {{ok: boolean, data?: object, reason?: string}} params.endpoint - The endpoint request.
 * @returns {{user?: object, note?: {heading: string, body: string}, error?: string}} What to show.
 */
export const describeLookup = ({ username, info, status, endpoint }) => {
  const parts = [info, status, endpoint];

  if (parts.every((part) => !part?.ok)) {
    const reason = parts.map((part) => part?.reason).find(Boolean);

    return {
      error: reason
        ? `Could not look up ${username}: ${reason}`
        : `Could not look up ${username}`,
    };
  }

  const user = {
    ...info?.data,
    ...status?.data,
    ...endpoint?.data,

    // in none of the three responses: the caller is the only one that knows
    // which name it asked about, and without this the card's header is a
    // presence dot with nothing beside it
    username,
  };

  if (info?.ok) {
    return { user };
  }

  const presence = status?.data?.presence;

  if (CONNECTED.has(presence)) {
    return {
      note: {
        body: `The server says they are ${String(presence).toLowerCase()}, but the request for their details went unanswered. Some clients do not answer it, and some cannot be reached even while connected.`,
        heading: `${username} did not answer`,
      },
      user,
    };
  }

  const known = status?.data?.isPrivileged
    ? 'The name is certainly real: the server reports it as a privileged account, and it holds no such record for a name nobody has used.'
    : 'Whether the name exists cannot be told from this. The server answers a status request for any name at all, and reports one that has never existed exactly as it reports one that is merely offline — so a misspelling looks like this too.';

  return {
    note: {
      body: `Their description and their address are read from them rather than from the server, so there is nothing more to show while they are away. ${known}`,
      heading: `${username} is offline`,
    },
    user,
  };
};

export const getInfo = ({ username }) => {
  return api.get(`/users/${encodeURIComponent(username)}/info`);
};

export const getStatus = ({ username }) => {
  return api.get(`/users/${encodeURIComponent(username)}/status`);
};

export const getEndpoint = ({ username }) => {
  return api.get(`/users/${encodeURIComponent(username)}/endpoint`);
};

export const browse = async ({ username }) => {
  return (await api.get(`/users/${encodeURIComponent(username)}/browse`)).data;
};

export const getBrowseStatus = ({ username }) => {
  return api.get(`/users/${encodeURIComponent(username)}/browse/status`);
};

export const getDirectoryContents = async ({ username, directory }) => {
  return (
    await api.post(`/users/${encodeURIComponent(username)}/directory`, {
      directory,
    })
  ).data;
};
