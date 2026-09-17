import { describeLookup, userPath } from './users';

describe('userPath', () => {
  it('addresses the Users page for a user', () => {
    expect(userPath('alice')).toBe('/users/alice');
  });

  it('encodes a name that would otherwise leave the segment', () => {
    // a Soulseek username may contain anything at all, and the route is one
    // path segment: a slash in a name would read as a route of its own
    expect(userPath('two words')).toBe('/users/two%20words');
    expect(userPath('a/b')).toBe('/users/a%2Fb');
    expect(userPath('100%')).toBe('/users/100%25');
    expect(userPath('a?b#c')).toBe('/users/a%3Fb%23c');
  });

  it('is still a path when there is no name', () => {
    // nothing links here without one, but a path that reads as `undefined`
    // would be a route that half works
    expect(userPath(undefined)).toBe('/users/');
  });
});

describe('describeLookup', () => {
  const ok = (data) => ({ data, ok: true });
  const failed = (reason) => ({ ok: false, reason });
  const offline = { isPrivileged: false, presence: 'Offline' };

  it('reports a connected user with everything that came back', () => {
    const outcome = describeLookup({
      endpoint: ok({ address: '1.2.3.4', port: 1_234 }),
      info: ok({ description: 'hi', uploadSlots: 2 }),
      status: ok({ isPrivileged: false, presence: 'Away' }),
      username: 'alice',
    });

    expect(outcome.error).toBeUndefined();
    expect(outcome.note).toBeUndefined();
    expect(outcome.user).toEqual({
      address: '1.2.3.4',
      description: 'hi',
      isPrivileged: false,
      port: 1_234,
      presence: 'Away',
      uploadSlots: 2,
      username: 'alice',
    });
  });

  it('names the user, which no response carries', () => {
    // without it the card's header is a presence dot with nothing beside it
    expect(
      describeLookup({
        endpoint: failed('nope'),
        info: failed('nope'),
        status: ok(offline),
        username: 'alice',
      }).user.username,
    ).toBe('alice');
  });

  it('is not an error when only the status came back', () => {
    // the whole bug: two of three requests fail for an offline user, and the
    // one that succeeds is the one that knows why
    const outcome = describeLookup({
      endpoint: failed('User alice appears to be offline'),
      info: failed('User alice appears to be offline'),
      status: ok(offline),
      username: 'alice',
    });

    expect(outcome.error).toBeUndefined();
    expect(outcome.note.heading).toBe('alice is offline');
  });

  it('will not say whether an ordinary offline name exists', () => {
    // measured: an invented name and a real dormant one answer identically
    const outcome = describeLookup({
      endpoint: failed('offline'),
      info: failed('offline'),
      status: ok(offline),
      username: 'alice',
    });

    expect(outcome.note.body).toContain('cannot be told');
  });

  it('says a privileged name is real, offline or not', () => {
    // account state the server holds, and it holds none for a name nobody has
    // ever used
    const outcome = describeLookup({
      endpoint: failed('offline'),
      info: failed('offline'),
      status: ok({ isPrivileged: true, presence: 'Offline' }),
      username: 'alice',
    });

    expect(outcome.note.body).toContain('certainly real');
  });

  it('distinguishes a connected user who did not answer', () => {
    const outcome = describeLookup({
      endpoint: failed('no'),
      info: failed('no'),
      status: ok({ isPrivileged: false, presence: 'Online' }),
      username: 'alice',
    });

    expect(outcome.note.heading).toBe('alice did not answer');
    expect(outcome.note.body).toContain('online');
  });

  it('is an error only when nothing at all came back', () => {
    const outcome = describeLookup({
      endpoint: failed('Network Error'),
      info: failed('Network Error'),
      status: failed('Network Error'),
      username: 'alice',
    });

    expect(outcome.user).toBeUndefined();
    expect(outcome.error).toBe('Could not look up alice: Network Error');
  });

  it('says something when the failure gave no reason', () => {
    const outcome = describeLookup({
      endpoint: failed(),
      info: failed(),
      status: failed(),
      username: 'alice',
    });

    expect(outcome.error).toBe('Could not look up alice');
  });
});
