import { describeUnread } from '../../lib/chat';
import CountBadge from '../Shared/CountBadge';
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

  return (
    <CountBadge
      count={unread?.unAcknowledgedMessages}
      words={describeUnread(unread)}
    />
  );
};

export default UnreadBadge;
