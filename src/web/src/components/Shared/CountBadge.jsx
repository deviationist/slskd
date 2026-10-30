import { badgeOf } from '../../lib/chat';
import React from 'react';

/**
 * A menu item's unread count: the number, and the same in words for a screen
 * reader and a tooltip. Draws nothing when the count is nothing. Shared by
 * the Chat and Rooms menu items, so both look and read alike.
 * @param {object} params
 * @param {number} [params.count] - How many are unread.
 * @param {string} params.words - The count in words.
 * @returns {object} The badge, or nothing.
 */
const CountBadge = ({ count, words }) => {
  const text = badgeOf(count);

  if (!text) return null;

  return (
    <span
      aria-label={words}
      className="unread-badge"
      role="status"
      title={words}
    >
      {text}
    </span>
  );
};

export default CountBadge;
