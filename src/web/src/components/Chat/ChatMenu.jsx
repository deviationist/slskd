import './Chat.css';
import { byRecency, describeAgo } from '../../lib/chat';
import SendMessageModal from './SendMessageModal';
import React from 'react';
import { Button, Dropdown, Icon, Label } from 'semantic-ui-react';

/*
 * One conversation as the picker lists it: who, how long since the last
 * message, whether any of it is unread, and the start of what was said.
 */
const ConversationOption = ({ conversation, now }) => {
  const { lastMessage, unAcknowledgedMessageCount, username } = conversation;

  return (
    <div className="conversation-option">
      <div className="conversation-option-head">
        <span className="conversation-option-name">{username}</span>
        {unAcknowledgedMessageCount > 0 && (
          <Label
            circular
            color="red"
            size="mini"
          >
            {unAcknowledgedMessageCount}
          </Label>
        )}
        {lastMessage && (
          <span className="conversation-option-ago">
            {describeAgo(lastMessage.timestamp, now)}
          </span>
        )}
      </div>
      {lastMessage && (
        <div className="conversation-option-preview">
          {lastMessage.direction === 'Out' ? 'You: ' : ''}
          {lastMessage.message}
        </div>
      )}
    </div>
  );
};

/*
 * The conversations as a searchable dropdown rather than a row of tabs. The
 * tabs did not scale: past a handful they ran off the side and had to be
 * scrolled through sideways to find anyone, in alphabetical order, with no way
 * to tell which had been spoken in lately. Newest first, and typing narrows it
 * to the names that match.
 */
const ChatMenu = ({ active, conversations, onConversationChange, ...rest }) => {
  const now = Date.now();
  const ordered = byRecency(conversations);

  // unread elsewhere: the active conversation's are being read as they arrive
  const unreadElsewhere = ordered
    .filter((c) => c.username !== active)
    .reduce((sum, c) => sum + (c.unAcknowledgedMessageCount || 0), 0);

  return (
    <div className="conversation-menu">
      <Dropdown
        className="conversation-picker"
        fluid
        noResultsMessage="No conversation with that name"
        onChange={(_, data) => onConversationChange(data.value)}
        options={ordered.map((conversation) => ({
          content: (
            <ConversationOption
              conversation={conversation}
              now={now}
            />
          ),
          key: conversation.username,
          text: conversation.username,
          value: conversation.username,
        }))}
        placeholder={
          ordered.length === 0 ? 'No conversations' : 'Find a conversation'
        }
        search
        selectOnNavigation={false}
        selection
        value={active || null}
      />
      {unreadElsewhere > 0 && (
        <Label
          className="conversation-unread"
          color="red"
          title="Unread in other conversations"
        >
          {unreadElsewhere}
        </Label>
      )}
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
    </div>
  );
};

export default ChatMenu;
