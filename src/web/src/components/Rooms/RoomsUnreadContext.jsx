import * as rooms from '../../lib/rooms';
import React, {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
} from 'react';

/**
 * What is unread in the rooms joined, shared by the menu's badge, the Rooms
 * page's room list, and the Rooms page marking the open room read.
 *
 * The same shape as ChatUnreadContext, and for the same reason: the one
 * source of truth is the server's `state.roomsUnread`, pushed to every open
 * page as it changes. Reading a room tells the server, the server recounts
 * and pushes, and every count moves together.
 */
const RoomsUnreadContext = createContext({
  countOf: () => 0,
  markRead: () => {},
  unread: undefined,
});

/**
 * @param {object} params
 * @param {object} params.children - What may show or clear an unread count.
 * @param {object} [params.unread] - `state.roomsUnread` from the server.
 * @returns {object} The provider.
 */
export const RoomsUnreadProvider = ({ children, unread }) => {
  // a room being marked read: asked again before the server has answered --
  // the Rooms page re-renders every second -- it is not sent twice
  const inFlight = useRef(new Set());

  const countOf = useCallback(
    (roomName) => rooms.unreadOf(unread, roomName),
    [unread],
  );

  const markRead = useCallback(
    async (roomName) => {
      if (
        !roomName ||
        countOf(roomName) === 0 ||
        inFlight.current.has(roomName)
      ) {
        return;
      }

      inFlight.current.add(roomName);

      try {
        await rooms.markRead({ roomName });
      } catch (error) {
        // the count stays as the server last said; the next push tries again
        console.error(error);
      } finally {
        inFlight.current.delete(roomName);
      }
    },
    [countOf],
  );

  const value = useMemo(
    () => ({ countOf, markRead, unread }),
    [countOf, markRead, unread],
  );

  return (
    <RoomsUnreadContext.Provider value={value}>
      {children}
    </RoomsUnreadContext.Provider>
  );
};

export const useRoomsUnread = () => useContext(RoomsUnreadContext);

export default RoomsUnreadContext;
