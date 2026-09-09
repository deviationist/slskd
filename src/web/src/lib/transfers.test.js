import * as transfers from './transfers';

describe('summariseDeletions', () => {
  const deleted = {
    data: {
      deleted: true,
      error: null,
      filename: '/a.flac',
      prunedDirectories: 0,
      removed: true,
    },
    ok: true,
  };
  // a plain removal with the option off: 204, no body, nothing to report
  const removedOnly = { data: undefined, ok: true };
  // a download that never started: no file was ever written, so the end state
  // asked for holds and there is nothing to announce
  const neverStarted = {
    data: {
      deleted: true,
      error: null,
      filename: null,
      prunedDirectories: 0,
      removed: true,
    },
    ok: true,
  };

  const unrecorded = {
    data: { deleted: false, error: null, filename: null, removed: true },
    ok: true,
  };
  const refused = {
    data: { deleted: false, error: 'permission denied', filename: '/a.flac', removed: true },
    ok: true,
  };
  const failed = { error: { message: 'Network Error' }, ok: false };
  // the API refuses up front everything it knows would stop a removal, so this
  // should not happen -- which is not the same as cannot
  const notRemoved = {
    data: { deleted: false, error: null, filename: '/a.flac', removed: false },
    ok: true,
  };

  it('says nothing about nothing', () => {
    expect(transfers.summariseDeletions([])).toBeNull();
  });

  // With the option off, removing is what it always was and the rows going is
  // the whole report. A toast there would be noise on an unchanged action.
  it('says nothing when no files were in play', () => {
    expect(transfers.summariseDeletions([removedOnly, removedOnly])).toBeNull();
  });

  it('counts the folders it cleared up, but never leads with them', () => {
    const withFolders = {
      data: { ...deleted.data, prunedDirectories: 2 },
      ok: true,
    };

    expect(transfers.summariseDeletions([withFolders]).message).toBe(
      'Removed 1 and deleted the file (2 empty folders removed)',
    );
  });

  // A file that was already gone answers `deleted: true`: what was asked for is
  // that it not be there, and it is not. Only a file that is there, should go
  // and will not is a failure -- so the summary has nothing special to say
  // about the already-gone case, and that is the point.
  it('treats an already-gone file as the success it is', () => {
    expect(transfers.summariseDeletions([deleted]).kind).toBe('success');
  });

  it('announces nothing over downloads that never wrote a file', () => {
    // they are a success -- nothing was written, which is the end state asked
    // for -- but "deleted 2 files" over two of them would be an invention.
    expect(transfers.summariseDeletions([neverStarted, neverStarted])).toBeNull();
  });

  it('counts only the files it really deleted', () => {
    expect(transfers.summariseDeletions([deleted, neverStarted]).message).toBe(
      'Removed 2 and deleted the file',
    );
  });

  it('reports the plain success', () => {
    expect(transfers.summariseDeletions([deleted, deleted])).toMatchObject({
      kind: 'success',
      message: 'Removed 2 and deleted 2 files',
    });
  });

  it('counts one file as a file', () => {
    expect(transfers.summariseDeletions([deleted]).message).toBe(
      'Removed 1 and deleted the file',
    );
  });

  // The case that would otherwise be silent, and the one every download
  // predating the recording of local filenames lands in. "Removed" alone over
  // this is how a delete that did nothing comes to look like one that worked.
  it('does not let a delete that deleted nothing pass for one that worked', () => {
    expect(transfers.summariseDeletions([unrecorded, unrecorded])).toMatchObject({
      kind: 'warning',
      message:
        'Removed 2, but deleted nothing: there is no record of where these were written',
    });
  });

  it('separates the ones it could delete from the ones it had no record of', () => {
    expect(transfers.summariseDeletions([deleted, unrecorded]).message).toBe(
      'Removed 2 and deleted 1; there is no record of where the other 1 were written',
    );
  });

  // A refusal from the file service happens after the record is removed --
  // everything that would stop the removal is refused before it happens -- so
  // "removed, but" is accurate here and only here.
  it('reports a refused deletion as a removal that happened', () => {
    expect(transfers.summariseDeletions([deleted, refused])).toMatchObject({
      kind: 'error',
      message: 'Removed 2, but 1 file(s) could not be deleted: permission denied',
    });
  });

  it('reports a request that never landed as neither removed nor deleted', () => {
    expect(transfers.summariseDeletions([deleted, failed])).toMatchObject({
      kind: 'error',
      message: '1 of 2 could not be removed: Network Error',
    });
  });

  // The bug this whole shape exists for: the message used to claim a removal on
  // the strength of the deletion's error being set, over a request that removed
  // nothing at all.
  it('never claims a removal that did not happen', () => {
    expect(transfers.summariseDeletions([notRemoved]).message).toBe(
      '1 of 1 were not removed',
    );
    expect(transfers.summariseDeletions([notRemoved]).kind).toBe('error');
  });

  it('says so when a file went but the record did not', () => {
    expect(transfers.summariseDeletions([deleted, notRemoved]).message).toBe(
      '1 of 2 were not removed, though 1 file(s) were deleted',
    );
  });

  it('leads with the failure that says the least happened', () => {
    // A request that did not land is a bigger fact than a file that would not
    // delete, so it is the one reported when a batch contains both.
    expect(transfers.summariseDeletions([refused, failed]).message).toMatch(
      /could not be removed/,
    );
  });
});

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
