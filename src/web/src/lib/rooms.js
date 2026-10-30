import api from './api';

export const getAvailable = async () => {
  const response = (await api.get('/rooms/available')).data;

  if (!Array.isArray(response)) {
    console.warn('got non-array response from rooms API', response);
    return undefined;
  }

  return response;
};

export const getJoined = async () => {
  const response = (await api.get('/rooms/joined')).data;

  if (!Array.isArray(response)) {
    console.warn('got non-array response from rooms API', response);
    return undefined;
  }

  return response;
};

export const getMessages = async ({ roomName }) => {
  const response = (
    await api.get(`/rooms/joined/${encodeURIComponent(roomName)}/messages`)
  ).data;

  if (!Array.isArray(response)) {
    console.warn('got non-array response from rooms API', response);
    return undefined;
  }

  return response;
};

export const getUsers = async ({ roomName }) => {
  const response = (
    await api.get(`/rooms/joined/${encodeURIComponent(roomName)}/users`)
  ).data;

  if (!Array.isArray(response)) {
    console.warn('got non-array response from rooms API', response);
    return undefined;
  }

  return response;
};

export const join = async ({ roomName }) => {
  return api.post('/rooms/joined', roomName);
};

export const leave = async ({ roomName }) => {
  return api.delete(`/rooms/joined/${encodeURIComponent(roomName)}`);
};

export const sendMessage = async ({ roomName, message }) => {
  return api.post(
    `/rooms/joined/${encodeURIComponent(roomName)}/messages`,
    JSON.stringify(message),
  );
};

export const get = async ({ roomName }) => {
  return (await api.get(`/rooms/joined/${encodeURIComponent(roomName)}`)).data;
};

/**
 * Tells the server everything in a room has been seen, which takes the room
 * off the unread count it pushes in `state.roomsUnread`.
 * @param {object} params
 * @param {string} params.roomName - The room.
 * @returns {Promise} The request.
 */
export const markRead = ({ roomName }) => {
  return api.put(`/rooms/joined/${encodeURIComponent(roomName)}/read`);
};

/**
 * How many of a room's messages are unread, from the server's
 * `state.roomsUnread`; 0 for any room it does not list.
 * @param {object} [unread] - `state.roomsUnread` from the server.
 * @param {string} roomName - The room.
 * @returns {number} The count.
 */
export const unreadOf = (unread, roomName) =>
  unread?.byRoom?.find((entry) => entry.roomName === roomName)?.count ?? 0;

/**
 * The Rooms badge's count in words, for a screen reader and a tooltip.
 * @param {object} [unread] - `state.roomsUnread` from the server.
 * @returns {string} The description, or '' when nothing is unread.
 */
export const describeUnread = (unread) => {
  const messages = unread?.unreadMessages ?? 0;
  const count = unread?.unreadRooms ?? 0;

  if (messages <= 0) return '';

  const what = `${messages} unread room message${messages === 1 ? '' : 's'}`;

  return messages === 1 || count <= 1 ? what : `${what} in ${count} rooms`;
};
