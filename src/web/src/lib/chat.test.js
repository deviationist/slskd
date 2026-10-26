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
