import * as chat from '../../lib/chat';
import React, {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
} from 'react';

/**
 * What is unread in private chat, shared by every part of the page that
 * shows it: the menu's badge, the Chat page's conversation list, and the
 * Chat page marking the open conversation read.
 *
 * There is one source of truth, and it is the server: `state.conversations`,
 * which it pushes to every open page as it changes. This holds no count of
 * its own. Reading a conversation tells the server, the server recounts and
 * pushes, and everything showing a count moves together -- in every tab, on
 * every device.
 */
const ChatUnreadContext = createContext({
  acknowledge: () => {},
  countOf: () => 0,
  unread: undefined,
});

/**
 * @param {object} params
 * @param {object} params.children - What may show or clear an unread count.
 * @param {object} [params.unread] - `state.conversations` from the server.
 * @returns {object} The provider.
 */
export const ChatUnreadProvider = ({ children, unread }) => {
  // an acknowledgement on its way for these people: asked again before the
  // server has answered -- the page re-renders often -- it is not sent twice
  const inFlight = useRef(new Set());

  const countOf = useCallback(
    (username) => chat.unreadOf(unread, username),
    [unread],
  );

  const acknowledge = useCallback(
    async (username) => {
      if (
        !username ||
        countOf(username) === 0 ||
        inFlight.current.has(username)
      ) {
        return;
      }

      inFlight.current.add(username);

      try {
        await chat.acknowledge({ username });
      } catch (error) {
        // the count stays as the server last said; the next push, or the next
        // time the conversation is opened, tries again
        console.error(error);
      } finally {
        inFlight.current.delete(username);
      }
    },
    [countOf],
  );

  const value = useMemo(
    () => ({ acknowledge, countOf, unread }),
    [acknowledge, countOf, unread],
  );

  return (
    <ChatUnreadContext.Provider value={value}>
      {children}
    </ChatUnreadContext.Provider>
  );
};

export const useChatUnread = () => useContext(ChatUnreadContext);

export default ChatUnreadContext;
