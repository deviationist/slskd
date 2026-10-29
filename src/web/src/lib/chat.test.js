import * as chat from './chat';

jest.mock('./api', () => ({
  __esModule: true,
  default: {
    delete: jest.fn(),
    get: jest.fn(),
    post: jest.fn(),
    put: jest.fn(),
  },
}));

const NOW = Date.UTC(2_026, 9, 26, 12, 0, 0);
const ago = (ms) => new Date(NOW - ms).toISOString();

describe('describeAgo', () => {
  it.each([
    [0, 'just now'],
    [59_000, 'just now'],
    [60_000, '1m'],
    [59 * 60_000, '59m'],
    [60 * 60_000, '1h'],
    [23 * 3_600_000, '23h'],
    [24 * 3_600_000, '1d'],
    [6 * 86_400_000, '6d'],
    [7 * 86_400_000, '1w'],
    [364 * 86_400_000, '52w'],
    [365 * 86_400_000, '1y'],
  ])('says %ims is %s', (age, expected) => {
    expect(chat.describeAgo(ago(age), NOW)).toBe(expected);
  });

  it('calls a time a little in the future just now, not a negative age', () => {
    // the server's clock and this one need not agree
    expect(chat.describeAgo(ago(-5_000), NOW)).toBe('just now');
  });

  it('says nothing about a time it cannot read', () => {
    expect(chat.describeAgo(undefined, NOW)).toBe('');
  });
});

describe('byRecency', () => {
  it('puts the newest last message first and the silent ones last, by name', () => {
    const conversations = {
      alice: { lastMessage: { timestamp: ago(3_600_000) }, username: 'alice' },
      bob: { lastMessage: { timestamp: ago(60_000) }, username: 'bob' },
      dave: { lastMessage: null, username: 'dave' },
      carol: { username: 'carol' },
    };

    expect(chat.byRecency(conversations).map((c) => c.username)).toEqual([
      'bob',
      'alice',
      'carol',
      'dave',
    ]);
  });
});

describe('badgeOf', () => {
  it('shows nothing when nothing is unread', () => {
    expect(chat.badgeOf(0)).toBe('');
    expect(chat.badgeOf(undefined)).toBe('');
    expect(chat.badgeOf(-1)).toBe('');
  });

  it('shows the count, and stops counting where a badge runs out of room', () => {
    expect(chat.badgeOf(1)).toBe('1');
    expect(chat.badgeOf(22)).toBe('22');
    expect(chat.badgeOf(99)).toBe('99');
    expect(chat.badgeOf(100)).toBe('99+');
  });
});

describe('describeUnread', () => {
  it('says how many and from how many people, in words', () => {
    expect(
      chat.describeUnread({
        unAcknowledgedConversations: 18,
        unAcknowledgedMessages: 22,
      }),
    ).toBe('22 unread messages from 18 people');
    expect(
      chat.describeUnread({
        unAcknowledgedConversations: 1,
        unAcknowledgedMessages: 3,
      }),
    ).toBe('3 unread messages from 1 person');
    expect(
      chat.describeUnread({
        unAcknowledgedConversations: 1,
        unAcknowledgedMessages: 1,
      }),
    ).toBe('1 unread message');
  });

  it('says nothing when nothing is unread or the state has not arrived', () => {
    expect(
      chat.describeUnread({
        unAcknowledgedConversations: 0,
        unAcknowledgedMessages: 0,
      }),
    ).toBe('');
    expect(chat.describeUnread(undefined)).toBe('');
  });
});
