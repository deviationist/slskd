import UnreadBadge from './UnreadBadge';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

describe('UnreadBadge', () => {
  it('shows the count, and says it in words for a screen reader', () => {
    const html = renderToStaticMarkup(
      <UnreadBadge
        unread={{ unAcknowledgedConversations: 18, unAcknowledgedMessages: 22 }}
      />,
    );

    expect(html).toContain('>22</span>');
    expect(html).toContain('aria-label="22 unread messages from 18 people"');
    expect(html).toContain('role="status"');
  });

  it('draws nothing when nothing is unread, or before the state arrives', () => {
    expect(
      renderToStaticMarkup(
        <UnreadBadge
          unread={{ unAcknowledgedConversations: 0, unAcknowledgedMessages: 0 }}
        />,
      ),
    ).toBe('');
    expect(renderToStaticMarkup(<UnreadBadge />)).toBe('');
  });
});
