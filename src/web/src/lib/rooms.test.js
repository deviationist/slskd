import { describeUnread, unreadOf } from './rooms';

describe('unreadOf', () => {
  const unread = { byRoom: [{ count: 3, roomName: 'Deep_House' }] };

  it('is the room’s count, matched exactly', () => {
    expect(unreadOf(unread, 'Deep_House')).toBe(3);
    expect(unreadOf(unread, 'deep_house')).toBe(0);
  });

  it('is 0 for a room not listed, or before the state arrives', () => {
    expect(unreadOf(unread, 'techno')).toBe(0);
    expect(unreadOf(undefined, 'techno')).toBe(0);
  });
});

describe('describeUnread', () => {
  it('says how many, and in how many rooms when more than one', () => {
    expect(describeUnread({ unreadMessages: 1, unreadRooms: 1 })).toBe(
      '1 unread room message',
    );
    expect(describeUnread({ unreadMessages: 4, unreadRooms: 1 })).toBe(
      '4 unread room messages',
    );
    expect(describeUnread({ unreadMessages: 4, unreadRooms: 2 })).toBe(
      '4 unread room messages in 2 rooms',
    );
  });

  it('says nothing when nothing is unread', () => {
    expect(describeUnread({ unreadMessages: 0, unreadRooms: 0 })).toBe('');
    expect(describeUnread(undefined)).toBe('');
  });
});
