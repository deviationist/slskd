import { initialStack, nextStack, parentOf, titleOf } from './navigation';

describe('nextStack', () => {
  const opened = initialStack({ key: undefined });

  it('starts from the entry the app was opened on', () => {
    expect(opened).toEqual(['initial']);
    expect(initialStack({ key: 'a' })).toEqual(['a']);
  });

  it('adds an entry for each push', () => {
    expect(nextStack(opened, { action: 'PUSH', key: 'a' })).toEqual([
      'initial',
      'a',
    ]);
  });

  it('swaps the last entry on a replace', () => {
    expect(
      nextStack(['initial', 'a'], { action: 'REPLACE', key: 'b' }),
    ).toEqual(['initial', 'b']);
  });

  it('drops the last entry on a pop back to the one before', () => {
    expect(
      nextStack(['initial', 'a', 'b'], { action: 'POP', key: 'a' }),
    ).toEqual(['initial', 'a']);
  });

  it('knows a pop back to the opening entry, which has no key', () => {
    expect(nextStack(['initial', 'a'], { action: 'POP' })).toEqual(['initial']);
  });

  it('goes back as far as a pop went', () => {
    expect(
      nextStack(['initial', 'a', 'b', 'c'], { action: 'POP', key: 'a' }),
    ).toEqual(['initial', 'a']);
  });

  it('starts again from a pop to an entry it never saw', () => {
    expect(nextStack(['initial', 'a'], { action: 'POP', key: 'z' })).toEqual([
      'z',
    ]);
  });
});

describe('parentOf', () => {
  it('goes up from one search to the list', () => {
    expect(parentOf('/searches/0a1b2c')).toBe('/searches');
  });

  it('goes up from a user and from a browse to their lookups', () => {
    expect(parentOf('/users/some%20one')).toBe('/users');
    expect(parentOf('/browse/someone/')).toBe('/browse');
  });

  it('has no up from a page that is not one of a list', () => {
    expect(parentOf('/searches')).toBeNull();
    expect(parentOf('/downloads')).toBeNull();
    expect(parentOf('/system/info')).toBeNull();
  });

  it('keeps the URL base the app is served under', () => {
    expect(parentOf('/slskd/searches/0a1b2c', '/slskd')).toBe(
      '/slskd/searches',
    );
    expect(parentOf('/searches/0a1b2c', '/')).toBe('/searches');
  });
});

describe('titleOf', () => {
  it('names a page as the menu does', () => {
    expect(titleOf('/searches')).toBe('Search');
    expect(titleOf('/searches/0a1b2c')).toBe('Search');
    expect(titleOf('/downloads')).toBe('Downloads');
    expect(titleOf('/system/info')).toBe('System');
  });

  it('names a user or a browse of one for whose it is', () => {
    expect(titleOf('/users/some%20one')).toBe('some one');
    expect(titleOf('/browse/someone/')).toBe('someone');
    expect(titleOf('/users')).toBe('Users');
  });

  it('shows a name that is not valid percent-encoding as it is', () => {
    expect(titleOf('/users/100%')).toBe('100%');
  });

  it('keeps out of the URL base, and has no name for what it does not know', () => {
    expect(titleOf('/slskd/chat', '/slskd')).toBe('Chat');
    expect(titleOf('/')).toBe('');
    expect(titleOf('/nowhere')).toBe('');
  });
});
