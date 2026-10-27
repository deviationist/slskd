import {
  LAST_KEY,
  readLast,
  readVolume,
  setSetting,
  settingOn,
  VOLUME_KEY,
  writeLast,
} from './remember';

const memory = (initial = {}) => {
  const values = { ...initial };

  return {
    getItem: (key) => (key in values ? values[key] : null),
    removeItem: (key) => delete values[key],
    setItem: (key, value) => {
      values[key] = String(value);
    },
    values,
  };
};

const ID = '0b9a6c7e-1d2f-4a3b-8c9d-0e1f2a3b4c5d';

describe('the remembered track', () => {
  it('reads back what was written', () => {
    const storage = memory();
    writeLast({ at: 42, id: ID, title: 'x', username: 'u' }, storage);
    expect(readLast(storage)).toEqual({ at: 42, id: ID, title: 'x', username: 'u' });
  });

  it('forgets on null, so closing the player is not undone by a reload', () => {
    const storage = memory();
    writeLast({ at: 1, id: ID, title: 'x' }, storage);
    writeLast(null, storage);
    expect(readLast(storage)).toBeNull();
  });

  it.each([
    ['not json', '{'],
    ['no id', JSON.stringify({ title: 'x' })],
    ['an id that is not an id', JSON.stringify({ id: '../etc', title: 'x' })],
    ['no title', JSON.stringify({ id: ID })],
  ])('reads %s as nothing rather than as a track that cannot load', (_, raw) => {
    expect(readLast(memory({ [LAST_KEY]: raw }))).toBeNull();
  });

  it('reads a nonsense position as the start', () => {
    const storage = memory({ [LAST_KEY]: JSON.stringify({ at: 'x', id: ID, title: 't' }) });
    expect(readLast(storage).at).toBe(0);
  });

  it('survives storage that throws', () => {
    const throwing = {
      getItem: () => {
        throw new Error('blocked');
      },
    };
    expect(readLast(throwing)).toBeNull();
  });
});

describe('volume', () => {
  it('is full when never set, and clamped when set to nonsense', () => {
    expect(readVolume(memory())).toBe(1);
    expect(readVolume(memory({ [VOLUME_KEY]: '7' }))).toBe(1);
    expect(readVolume(memory({ [VOLUME_KEY]: '0.3' }))).toBe(0.3);
    expect(readVolume(memory({ [VOLUME_KEY]: 'loud' }))).toBe(1);
  });
});

describe('settings', () => {
  it('start at their defaults and keep what is set', () => {
    const storage = memory();
    expect(settingOn('slskd-player-meter', storage)).toBe(true);
    expect(settingOn('slskd-player-volume-control', storage)).toBe(false);
    setSetting('slskd-player-meter', false, storage);
    expect(settingOn('slskd-player-meter', storage)).toBe(false);
  });
});
