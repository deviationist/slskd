import RoomsUnreadBadge from './RoomsUnreadBadge';
import { RoomsUnreadProvider } from './RoomsUnreadContext';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

const render = (unread) =>
  renderToStaticMarkup(
    <RoomsUnreadProvider unread={unread}>
      <RoomsUnreadBadge />
    </RoomsUnreadProvider>,
  );

describe('RoomsUnreadBadge', () => {
  it('shows the server’s count, and says it in words for a screen reader', () => {
    const html = render({ unreadMessages: 140, unreadRooms: 3 });

    expect(html).toContain('>99+</span>');
    expect(html).toContain('aria-label="140 unread room messages in 3 rooms"');
    expect(html).toContain('role="status"');
  });

  it('draws nothing when nothing is unread, or before the state arrives', () => {
    expect(render({ unreadMessages: 0, unreadRooms: 0 })).toBe('');
    expect(render(undefined)).toBe('');
  });
});
