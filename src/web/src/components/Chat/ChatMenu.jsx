import './Chat.css';
import { byRecency, describeAgo } from '../../lib/chat';
import { userPath } from '../../lib/users';
import SendMessageModal from './SendMessageModal';
import React, { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Link } from 'react-router-dom';
import { Button, Icon, Label } from 'semantic-ui-react';

/*
 * A fine pointer means a mouse, and a keyboard to type into the filter with
 * straight away. On a touch screen, focusing the filter would put the on-screen
 * keyboard over half the list before anyone asked to search.
 */
const typesFirst = () =>
  window.matchMedia?.('(pointer: fine)').matches ?? false;

/*
 * One conversation as the picker lists it: who, how long since the last
 * message, whether any of it is unread, and the start of what was said. The
 * row picks the conversation; the icon beside it opens the peer on Users, a
 * sibling rather than a link inside the row, which is already a button.
 */
const ConversationRow = ({ active, conversation, now, onPick }) => {
  const { lastMessage, unAcknowledgedMessageCount, username } = conversation;
  const unread = unAcknowledgedMessageCount > 0;

  return (
    <li className="conversation-row-item">
      <button
        aria-current={active ? 'true' : undefined}
        className={`conversation-row${active ? ' active' : ''}${unread ? ' unread' : ''}`}
        onClick={() => onPick(username)}
        type="button"
      >
        <span className="conversation-row-head">
          <span className="conversation-row-name">{username}</span>
          {lastMessage && (
            <span className="conversation-row-ago">
              {describeAgo(lastMessage.timestamp, now)}
            </span>
          )}
        </span>
        <span className="conversation-row-foot">
          <span className="conversation-row-preview">
            {lastMessage
              ? `${lastMessage.direction === 'Out' ? 'You: ' : ''}${lastMessage.message}`
              : 'No messages'}
          </span>
          {unread && (
            <span className="conversation-row-count">
              {unAcknowledgedMessageCount}
            </span>
          )}
        </span>
      </button>
      <Link
        aria-label={`Open ${username} on Users`}
        className="conversation-row-user"
        title="Open on Users"
        to={userPath(username)}
      >
        <Icon name="user" />
      </Link>
    </li>
  );
};

/*
 * The conversations, in a sheet of their own. It replaced a Semantic dropdown,
 * which on an iPhone focused its search field on open -- the keyboard then
 * covered most of a menu that had been sized for the screen without it -- and
 * whose menu would not scroll by touch at all. The sheet is a plain list in a
 * plain scrolling box: the whole height of the window, newest first, with a
 * filter at the top for when the name is known.
 */
const ConversationSheet = ({ active, conversations, onClose, onPick }) => {
  const [filter, setFilter] = useState('');
  const filterRef = useRef(null);
  const listRef = useRef(null);
  const closeRef = useRef(onClose);
  closeRef.current = onClose;
  const now = Date.now();

  useEffect(() => {
    if (typesFirst()) filterRef.current?.focus();

    // open where the active conversation is, so its neighbours are at hand
    listRef.current
      ?.querySelector('.conversation-row.active')
      ?.scrollIntoView({ block: 'center' });

    const onKey = (event) => {
      if (event.key === 'Escape') closeRef.current();
    };

    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);

  const needle = filter.trim().toLowerCase();
  const shown = needle
    ? conversations.filter((c) => c.username.toLowerCase().includes(needle))
    : conversations;

  return createPortal(
    <div
      className="conversation-sheet-backdrop"
      onClick={(event) => event.target === event.currentTarget && onClose()}
      role="presentation"
    >
      <div
        aria-label="Conversations"
        aria-modal="true"
        className="conversation-sheet"
        role="dialog"
      >
        <div className="conversation-sheet-header">
          <div className="conversation-sheet-filter">
            <Icon name="search" />
            <input
              aria-label="Find a conversation"
              autoCapitalize="none"
              autoComplete="off"
              autoCorrect="off"
              enterKeyHint="go"
              onChange={(event) => setFilter(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter' && shown.length > 0) {
                  onPick(shown[0].username);
                }
              }}
              placeholder="Find a conversation"
              ref={filterRef}
              spellCheck={false}
              type="search"
              value={filter}
            />
          </div>
          <button
            className="conversation-sheet-close"
            onClick={onClose}
            type="button"
          >
            Done
          </button>
        </div>
        {shown.length === 0 ? (
          <div className="conversation-sheet-empty">
            {conversations.length === 0
              ? 'No conversations yet'
              : 'No conversation with that name'}
          </div>
        ) : (
          <ul
            className="conversation-sheet-list"
            ref={listRef}
          >
            {shown.map((conversation) => (
              <ConversationRow
                active={conversation.username === active}
                conversation={conversation}
                key={conversation.username}
                now={now}
                onPick={onPick}
              />
            ))}
          </ul>
        )}
      </div>
    </div>,
    document.body,
  );
};

/*
 * Who is being talked to, as a button that opens the list of everyone else.
 * The count beside the name is what is unread in the other conversations:
 * the active one's are being read as they arrive.
 */
const ChatMenu = ({ active, conversations, onConversationChange, ...rest }) => {
  const [open, setOpen] = useState(false);
  const ordered = byRecency(conversations);

  const unreadElsewhere = ordered
    .filter((c) => c.username !== active)
    .reduce((sum, c) => sum + (c.unAcknowledgedMessageCount || 0), 0);

  return (
    <div className="conversation-menu">
      <button
        aria-haspopup="dialog"
        className="conversation-trigger"
        disabled={ordered.length === 0}
        onClick={() => setOpen(true)}
        type="button"
      >
        <span className="conversation-trigger-name">
          {active ||
            (ordered.length === 0
              ? 'No conversations'
              : 'Choose a conversation')}
        </span>
        {unreadElsewhere > 0 && (
          <Label
            circular
            color="red"
            size="small"
            title="Unread in other conversations"
          >
            {unreadElsewhere}
          </Label>
        )}
        <Icon name="chevron down" />
      </button>
      <SendMessageModal
        centered
        size="small"
        trigger={
          <Button
            className="add-button"
            icon
            title="Start a conversation"
          >
            <Icon name="plus" />
          </Button>
        }
        {...rest}
      />
      {open && (
        <ConversationSheet
          active={active}
          conversations={ordered}
          onClose={() => setOpen(false)}
          onPick={(username) => {
            setOpen(false);
            onConversationChange(username);
          }}
        />
      )}
    </div>
  );
};

export default ChatMenu;
