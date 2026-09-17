import { userPath } from './users';

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
