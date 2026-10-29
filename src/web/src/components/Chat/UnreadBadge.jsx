import { badgeOf, describeUnread } from '../../lib/chat';
import React from 'react';

/**
 * The unread count on the Chat menu item, so a message is seen to arrive
 * from any page rather than only from Chat. Draws nothing when nothing is
 * unread. The count comes in the application state the server pushes on
 * every change, so it moves as messages arrive and are read.
 * @param {object} params
 * @param {object} [params.unread] - `state.conversations` from the server.
 * @returns {object} The badge, or nothing.
 */
const UnreadBadge = ({ unread }) => {
  const text = badgeOf(unread?.unAcknowledgedMessages);

  if (!text) return null;

  const words = describeUnread(unread);

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

export default UnreadBadge;
