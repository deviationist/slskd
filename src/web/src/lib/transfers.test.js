import * as transfers from './transfers';

const file = (requestedAt) => ({ filename: requestedAt, requestedAt });

const user = (username, directories) => ({
  directories: Object.entries(directories).map(([directory, instants]) => ({
    directory,
    files: instants.map((at) => file(at)),
  })),
  username,
});

/**
 * A sorted list as the page reads it: users, and the folders under each.
 */
const shape = (sorted) =>
  sorted.map((sortedUser) => [
    sortedUser.username,
    sortedUser.directories.map((directory) => directory.directory),
  ]);

describe('sortTransfers', () => {
  const alice = user('alice', {
    'old album': ['2026-01-01T00:00:00Z', '2026-01-01T00:00:01Z'],
  });
  const bob = user('bob', {
    'new album': ['2026-03-01T00:00:00Z'],
    'older album': ['2026-02-01T00:00:00Z'],
  });

  it('puts the user with the newest transfer first', () => {
    expect(shape(transfers.sortTransfers([alice, bob], 'newest'))).toEqual([
      ['bob', ['new album', 'older album']],
      ['alice', ['old album']],
    ]);
  });

  it('puts the user with the oldest transfer first when reversed', () => {
    expect(shape(transfers.sortTransfers([bob, alice], 'oldest'))).toEqual([
      ['alice', ['old album']],
      ['bob', ['older album', 'new album']],
    ]);
  });

  it('sorts newest by a group’s newest transfer, not its oldest', () => {
    // this folder started before alice's and is still receiving files. it is
    // the newest arrival on the page, and reading the other end of its range
    // would bury it
    const carol = user('carol', {
      ongoing: ['2025-01-01T00:00:00Z', '2026-06-01T00:00:00Z'],
    });

    expect(shape(transfers.sortTransfers([alice, carol], 'newest'))).toEqual([
      ['carol', ['ongoing']],
      ['alice', ['old album']],
    ]);
  });

  it('sorts oldest by a group’s oldest transfer, not its newest', () => {
    const carol = user('carol', {
      ongoing: ['2025-01-01T00:00:00Z', '2026-06-01T00:00:00Z'],
    });

    expect(shape(transfers.sortTransfers([alice, carol], 'oldest'))).toEqual([
      ['carol', ['ongoing']],
      ['alice', ['old album']],
    ]);
  });

  it('breaks ties on name, so one gesture does not reorder itself', () => {
    const at = '2026-01-01T00:00:00Z';
    const dave = user('dave', { b: [at], a: [at], c: [at] });

    expect(shape(transfers.sortTransfers([dave], 'newest'))).toEqual([
      ['dave', ['a', 'b', 'c']],
    ]);
  });

  it('leaves the files within a folder alone', () => {
    const [sorted] = transfers.sortTransfers([bob], 'newest');
    const [directory] = sorted.directories;

    expect(directory.files).toBe(
      bob.directories.find((d) => d.directory === 'new album').files,
    );
  });

  it('does not reorder what it is given', () => {
    const input = [alice, bob];
    const directories = [...bob.directories];

    transfers.sortTransfers(input, 'newest');

    expect(input).toEqual([alice, bob]);
    expect(bob.directories).toEqual(directories);
  });

  it('sinks a group with nothing in it, whichever way the list runs', () => {
    const empty = user('zach', { nothing: [] });

    expect(shape(transfers.sortTransfers([empty, alice], 'newest'))).toEqual([
      ['alice', ['old album']],
      ['zach', ['nothing']],
    ]);
    expect(shape(transfers.sortTransfers([empty, alice], 'oldest'))).toEqual([
      ['alice', ['old album']],
      ['zach', ['nothing']],
    ]);
  });

  it('treats a transfer with no readable instant as the oldest there is', () => {
    const undated = user('erin', { mystery: [null] });

    expect(shape(transfers.sortTransfers([undated, alice], 'newest'))).toEqual([
      ['alice', ['old album']],
      ['erin', ['mystery']],
    ]);
  });

  it('defaults to newest, and survives a list it cannot read', () => {
    expect(shape(transfers.sortTransfers([bob, alice]))).toEqual([
      ['bob', ['new album', 'older album']],
      ['alice', ['old album']],
    ]);
    expect(transfers.sortTransfers()).toEqual([]);
    expect(transfers.sortTransfers([{ username: 'nobody' }], 'newest')).toEqual(
      [{ directories: [], username: 'nobody' }],
    );
  });
});
