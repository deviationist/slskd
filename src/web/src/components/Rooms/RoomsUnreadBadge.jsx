import { describeUnread } from '../../lib/rooms';
import CountBadge from '../Shared/CountBadge';
import { useRoomsUnread } from './RoomsUnreadContext';
import React from 'react';

/**
 * The unread count on the Rooms menu item: someone has posted in a room
 * joined, seen from any page. The server's count, through RoomsUnreadContext.
 * @returns {object} The badge, or nothing.
 */
const RoomsUnreadBadge = () => {
  const { unread } = useRoomsUnread();

  return (
    <CountBadge
      count={unread?.unreadMessages}
      words={describeUnread(unread)}
    />
  );
};

export default RoomsUnreadBadge;
