import { ChatUnreadProvider } from '../Chat/ChatUnreadContext';
import { RoomsUnreadProvider } from '../Rooms/RoomsUnreadContext';
import React from 'react';

/**
 * What is unread in chat and in rooms, from the server's pushed state, for
 * the menu's badges and the Chat and Rooms pages alike.
 * @param {object} params
 * @param {object} params.chat - `state.conversations`.
 * @param {object} params.children - The page.
 * @param {object} params.rooms - `state.roomsUnread`.
 * @returns {object} The providers.
 */
const UnreadProviders = ({ chat, children, rooms }) => (
  <ChatUnreadProvider unread={chat}>
    <RoomsUnreadProvider unread={rooms}>{children}</RoomsUnreadProvider>
  </ChatUnreadProvider>
);

export default UnreadProviders;
