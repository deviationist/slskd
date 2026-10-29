import { badgeOf, describeUnread } from '../../lib/chat';
import { useChatUnread } from './ChatUnreadContext';
import React from 'react';

/**
 * The unread count on the Chat menu item, so a message is seen to arrive
 * from any page rather than only from Chat. Draws nothing when nothing is
 * unread. The count is the server's, shared through ChatUnreadContext, so it
 * moves as messages arrive and are read -- with the Chat page's own counts.
 * @returns {object} The badge, or nothing.
 */
const UnreadBadge = () => {
  const { unread } = useChatUnread();
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
