import api from './api';

export const getAll = async () => {
  return (await api.get('/conversations')).data;
};

export const get = async ({ username }) => {
  return (await api.get(`/conversations/${encodeURIComponent(username)}`)).data;
};

export const acknowledge = ({ username }) => {
  return api.put(`/conversations/${encodeURIComponent(username)}`);
};

export const send = ({ username, message }) => {
  return api.post(
    `/conversations/${encodeURIComponent(username)}`,
    JSON.stringify(message),
  );
};

export const remove = ({ username }) => {
  return api.delete(`/conversations/${encodeURIComponent(username)}`);
};

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;
const WEEK = 7 * DAY;
const YEAR = 365 * DAY;

/**
 * How long ago a message was, the way a conversation list says it: "just now",
 * "5m", "3h", "2d", "3w", "1y". Coarse on purpose -- the list is for choosing
 * whom to answer, and the history beside it has the exact time.
 * @param {string|number|Date} at - When the message was sent or received.
 * @param {number} [now] - The present, in ms.
 * @returns {string} The age, or '' when there is no time to measure.
 */
export const describeAgo = (at, now = Date.now()) => {
  const then = new Date(at).getTime();

  if (Number.isNaN(then)) {
    return '';
  }

  const age = Math.max(0, now - then);

  if (age < MINUTE) {
    return 'just now';
  }

  for (const [size, unit] of [
    [YEAR, 'y'],
    [WEEK, 'w'],
    [DAY, 'd'],
    [HOUR, 'h'],
  ]) {
    if (age >= size) {
      return `${Math.floor(age / size)}${unit}`;
    }
  }

  return `${Math.floor(age / MINUTE)}m`;
};

const lastMessageAt = (conversation) =>
  conversation.lastMessage
    ? new Date(conversation.lastMessage.timestamp).getTime()
    : Number.NEGATIVE_INFINITY;

/**
 * The conversations newest first, by their last message; ones with no message
 * at all go last, by name. Unread conversations get no priority of their own:
 * a message arriving is what makes one unread, so they are near the top anyway.
 * @param {object} conversations - Keyed by username, as the Chat page holds them.
 * @returns {object[]} The conversations, ordered.
 */
export const byRecency = (conversations) => {
  return Object.values(conversations).sort(
    (a, b) =>
      lastMessageAt(b) - lastMessageAt(a) ||
      a.username.localeCompare(b.username),
  );
};

/**
 * What the unread badge on the Chat menu item says: the count, or nothing
 * when there is nothing unread. Two digits is what a badge has room for.
 * @param {number} [count] - Unread messages, from everyone.
 * @returns {string} The badge's text.
 */
export const badgeOf = (count) => {
  if (!Number.isFinite(count) || count <= 0) return '';

  return count > 99 ? '99+' : String(count);
};

/**
 * The same, in words, for a screen reader and a tooltip: the badge's number
 * says how much, and this says from how many.
 * @param {object} [unread] - `state.conversations` from the server.
 * @returns {string} The description, or '' when nothing is unread.
 */
export const describeUnread = (unread) => {
  const messages = unread?.unAcknowledgedMessages ?? 0;
  const people = unread?.unAcknowledgedConversations ?? 0;

  if (messages <= 0) return '';

  const what = `${messages} unread message${messages === 1 ? '' : 's'}`;

  return messages === 1 || people <= 0
    ? what
    : `${what} from ${people} ${people === 1 ? 'person' : 'people'}`;
};
