import {
  backTarget,
  initialStack,
  nextStack,
  parentOf,
  titleOf,
} from './navigation';

const at = (key, pathname = '/') => ({ key, pathname });
const keys = (stack) => stack.map((entry) => entry.key);

describe('nextStack', () => {
  const opened = initialStack({ key: undefined, pathname: '/searches' });

  it('starts from the entry the app was opened on', () => {
    expect(opened).toEqual([{ key: 'initial', pathname: '/searches' }]);
    expect(keys(initialStack({ key: 'a' }))).toEqual(['a']);
  });

  it('adds an entry for each push, with its path', () => {
    expect(
      nextStack(opened, { action: 'PUSH', location: at('a', '/searches/1') }),
    ).toEqual([
      { key: 'initial', pathname: '/searches' },
      { key: 'a', pathname: '/searches/1' },
    ]);
  });

  it('swaps the last entry on a replace', () => {
    expect(
      keys(
        nextStack([at('initial'), at('a')], {
          action: 'REPLACE',
          location: at('b'),
        }),
      ),
    ).toEqual(['initial', 'b']);
  });

  it('drops the last entry on a pop back to the one before', () => {
    expect(
      keys(
        nextStack([at('initial'), at('a'), at('b')], {
          action: 'POP',
          location: at('a'),
        }),
      ),
    ).toEqual(['initial', 'a']);
  });

  it('knows a pop back to the opening entry, which has no key', () => {
    expect(
      keys(
        nextStack([at('initial'), at('a')], { action: 'POP', location: {} }),
      ),
    ).toEqual(['initial']);
  });

  it('goes back as far as a pop went', () => {
    expect(
      keys(
        nextStack([at('initial'), at('a'), at('b'), at('c')], {
          action: 'POP',
          location: at('a'),
        }),
      ),
    ).toEqual(['initial', 'a']);
  });

  it('starts again from a pop to an entry it never saw', () => {
    expect(
      keys(
        nextStack([at('initial'), at('a')], {
          action: 'POP',
          location: at('z'),
        }),
      ),
    ).toEqual(['z']);
  });
});

describe('backTarget', () => {
  it('has no Back on a page without a parent, however it was reached', () => {
    expect(
      backTarget([at('1', '/downloads'), at('2', '/uploads')], '/uploads'),
    ).toBeNull();
    expect(backTarget([at('1', '/searches')], '/searches')).toBeNull();
  });

  it('steps back to the list when the page was opened from it', () => {
    expect(
      backTarget(
        [at('1', '/searches'), at('2', '/searches/abc')],
        '/searches/abc',
      ),
    ).toEqual({ mode: 'back' });
  });

  it('goes up to the list, not back, when the page was reached from elsewhere', () => {
    expect(
      backTarget(
        [at('1', '/downloads'), at('2', '/searches/abc')],
        '/searches/abc',
      ),
    ).toEqual({ mode: 'up', to: '/searches' });
  });

  it('goes up from a page the app was opened on', () => {
    expect(backTarget([at('1', '/users/someone')], '/users/someone')).toEqual({
      mode: 'up',
      to: '/users',
    });
  });

  it('keeps the URL base', () => {
    expect(
      backTarget(
        [at('1', '/slskd/browse'), at('2', '/slskd/browse/someone')],
        '/slskd/browse/someone',
        '/slskd',
      ),
    ).toEqual({ mode: 'back' });
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
