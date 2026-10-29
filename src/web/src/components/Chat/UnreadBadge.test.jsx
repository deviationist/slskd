import { ChatUnreadProvider } from './ChatUnreadContext';
import UnreadBadge from './UnreadBadge';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

const render = (unread) =>
  renderToStaticMarkup(
    <ChatUnreadProvider unread={unread}>
      <UnreadBadge />
    </ChatUnreadProvider>,
  );

describe('UnreadBadge', () => {
  it('shows the server’s count, and says it in words for a screen reader', () => {
    const html = render({
      unAcknowledgedConversations: 18,
      unAcknowledgedMessages: 22,
    });

    expect(html).toContain('>22</span>');
    expect(html).toContain('aria-label="22 unread messages from 18 people"');
    expect(html).toContain('role="status"');
  });

  it('draws nothing when nothing is unread, or before the state arrives', () => {
    expect(
      render({ unAcknowledgedConversations: 0, unAcknowledgedMessages: 0 }),
    ).toBe('');
    expect(render(undefined)).toBe('');
  });
});
