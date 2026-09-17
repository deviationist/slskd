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
