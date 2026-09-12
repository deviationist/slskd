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
    data: {
      deleted: false,
      error: 'permission denied',
      filename: '/a.flac',
      removed: true,
    },
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
    expect(
      transfers.summariseDeletions([neverStarted, neverStarted]),
    ).toBeNull();
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
    expect(
      transfers.summariseDeletions([unrecorded, unrecorded]),
    ).toMatchObject({
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
      message:
        'Removed 2, but 1 file(s) could not be deleted: permission denied',
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

  // a row's own remove control is this same call with a selection of one, so a
  // failure there is reported here or nowhere
  it('reports a batch of one, which is what a single row removes', () => {
    expect(transfers.summariseDeletions([failed])).toMatchObject({
      kind: 'error',
      message: '1 of 1 could not be removed: Network Error',
    });
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

describe('resolveSort', () => {
  it('prefers what this browser chose', () => {
    expect(transfers.resolveSort('oldest', 'newest')).toBe('oldest');
    expect(transfers.resolveSort('newest', 'oldest')).toBe('newest');
  });

  it('falls back to the configured default when nothing is stored', () => {
    expect(transfers.resolveSort(null, 'oldest')).toBe('oldest');
    expect(transfers.resolveSort(undefined, 'oldest')).toBe('oldest');
  });

  it('falls back again when there is no configured default either', () => {
    // options arrive over the hub, so this is the state of every first render
    expect(transfers.resolveSort(null, undefined)).toBe('newest');
    expect(transfers.resolveSort(null, null)).toBe('newest');
  });

  it('discards a stored value it does not recognise', () => {
    // whatever a past or future version left in localStorage
    expect(transfers.resolveSort('sideways', 'oldest')).toBe('oldest');
    expect(transfers.resolveSort('', 'oldest')).toBe('oldest');
  });

  it('discards a configured value it does not recognise', () => {
    // the server validates its own, so this should not arrive
    expect(transfers.resolveSort(null, 'sideways')).toBe('newest');
  });

  it('is the two names the sort itself understands, and no others', () => {
    for (const { value } of transfers.SORT_OPTIONS) {
      expect(transfers.resolveSort(value, undefined)).toBe(value);
    }
  });
});

// the request asks for a Blob, so the *body* of an error response is a Blob
// too -- unreadable without unpacking it, and '[object Blob]' if toasted. the
// status is the part that is readable, and the part that says what to do next
const blobBodied = (status) => ({
  message: 'Request failed',
  response: { data: new Blob(['nope']), status },
});

describe('describeRetrievalError', () => {
  it('names the option when the server refuses', () => {
    expect(transfers.describeRetrievalError(blobBodied(403))).toContain(
      'remote_file_retrieval',
    );
  });

  it('says there is no file when there is none', () => {
    expect(transfers.describeRetrievalError(blobBodied(404))).toBe(
      'There is no file on disk for this download',
    );
  });

  it('falls back to the error message for anything else', () => {
    expect(transfers.describeRetrievalError(blobBodied(500))).toBe(
      'Request failed',
    );
  });

  it('says something useful when there is no response at all', () => {
    // a request that never landed: no status to read, and undefined is not a
    // message
    expect(transfers.describeRetrievalError(undefined)).toBe(
      'the file could not be retrieved',
    );
  });
});

describe('isRetrievalPermanentlyGone', () => {
  // the distinction the row's button depends on: a file that is gone is gone,
  // and something downstream moving finished files out of the downloads
  // directory makes that the normal end of a download's life rather than a
  // fault worth offering to retry
  it('is true when the server says there is no file', () => {
    expect(transfers.isRetrievalPermanentlyGone(blobBodied(404))).toBe(true);
  });

  it('is false when the server refuses, which a config change could undo', () => {
    expect(transfers.isRetrievalPermanentlyGone(blobBodied(403))).toBe(false);
  });

  it('is false for a request that never landed', () => {
    expect(transfers.isRetrievalPermanentlyGone(undefined)).toBe(false);
    expect(transfers.isRetrievalPermanentlyGone(new Error('offline'))).toBe(
      false,
    );
  });
});

describe('describeArchiveError', () => {
  // unlike a single-file retrieval these are ordinary JSON calls, so the body
  // is readable and is the most specific thing available
  it('prefers what the server said', () => {
    expect(
      transfers.describeArchiveError({
        message: 'Request failed',
        response: { data: "'nope' is not a valid download id", status: 400 },
      }),
    ).toBe("'nope' is not a valid download id");
  });

  it('names the option when the server refuses without a body', () => {
    expect(
      transfers.describeArchiveError({
        message: 'Request failed',
        response: { data: undefined, status: 403 },
      }),
    ).toContain('remote_file_retrieval');
  });

  it('falls back to the error message, then to something sayable', () => {
    expect(transfers.describeArchiveError(new Error('offline'))).toBe(
      'offline',
    );
    expect(transfers.describeArchiveError(undefined)).toBe(
      'the archive could not be started',
    );
  });
});

describe('describeRetrieval', () => {
  it('names the browser as the destination, to distinguish it from a Soulseek download', () => {
    expect(transfers.describeRetrieval()).toBe(
      'Download this file to your browser',
    );
  });

  it('says how many files, and that they arrive as one zip', () => {
    expect(transfers.describeRetrieval(3)).toBe(
      'Download these 3 files to your browser, as one zip',
    );
  });

  it('describes a single file as a file, matching what the request will do', () => {
    expect(transfers.describeRetrieval(1)).toBe(
      'Download this file to your browser',
    );
  });
});

describe('describeUnretrievable', () => {
  const recorded = { localFilename: '/downloads/complete/a.flac' };
  const unrecorded = { localFilename: null };

  it('says a file the server has refused is gone', () => {
    expect(
      transfers.describeUnretrievable({ file: recorded, gone: true }),
    ).toBe('This file is no longer on disk');
  });

  it('does not claim a file is missing merely because its path was never recorded', () => {
    const described = transfers.describeUnretrievable({ file: unrecorded });

    expect(described).not.toContain('no longer');
    expect(described).toContain('recorded where it saved files');
  });

  it('describes a refusal as gone even where no path was recorded, because that is the stronger fact', () => {
    expect(
      transfers.describeUnretrievable({ file: unrecorded, gone: true }),
    ).toBe('This file is no longer on disk');
  });

  it('says nothing about a row that can still be fetched', () => {
    expect(transfers.describeUnretrievable({ file: recorded })).toBeUndefined();
  });
});

describe('isFinishedDownload / isRetrievable', () => {
  const done = {
    direction: 'Download',
    localFilename: '/downloads/a.flac',
    state: 'Completed, Succeeded',
  };

  it('recognises a download that produced a file', () => {
    expect(transfers.isFinishedDownload(done)).toBe(true);
    expect(transfers.isRetrievable(done)).toBe(true);
  });

  it('does not offer a download whose path was never recorded', () => {
    const unrecorded = { ...done, localFilename: null };

    // the column still has something to say about it, but it cannot be fetched
    expect(transfers.isFinishedDownload(unrecorded)).toBe(true);
    expect(transfers.isRetrievable(unrecorded)).toBe(false);
  });

  it('says nothing about a transfer that produced no file', () => {
    const states = ['Completed, Cancelled', 'Completed, Errored', 'InProgress'];

    for (const state of states) {
      expect(transfers.isFinishedDownload({ ...done, state })).toBe(false);
      expect(transfers.isRetrievable({ ...done, state })).toBe(false);
    }
  });

  it('says nothing about an upload', () => {
    expect(transfers.isFinishedDownload({ ...done, direction: 'Upload' })).toBe(
      false,
    );
  });
});

describe('isFileGone', () => {
  const present = { id: 'a', localFileExists: true };

  it('believes the server when it says the file is gone', () => {
    expect(
      transfers.isFileGone({ file: { ...present, localFileExists: false } }),
    ).toBe(true);
  });

  it('leaves a row alone while the server says the file is there', () => {
    expect(transfers.isFileGone({ file: present })).toBe(false);
  });

  it('leaves a row alone when the server did not answer at all', () => {
    // undefined is not false: a listing that carries no answer must not strike
    // out every row it describes
    expect(transfers.isFileGone({ file: { id: 'a' } })).toBe(false);
  });

  it('lets a refusal outrank a stale listing', () => {
    // the listing is cached and may be half a minute behind; a refusal is proof
    expect(
      transfers.isFileGone({ file: present, refused: new Set(['a']) }),
    ).toBe(true);
  });
});

const finishedDownload = (id, extra = {}) => ({
  direction: 'Download',
  id,
  localFilename: `/downloads/${id}.flac`,
  state: 'Completed, Succeeded',
  ...extra,
});

describe('chooseRetrieval', () => {
  it('fetches a single file as a file, not an archive of one', () => {
    const choice = transfers.chooseRetrieval([finishedDownload('a')]);

    expect(choice.mode).toBe('file');
    expect(choice.file.id).toBe('a');
  });

  it('archives more than one', () => {
    const choice = transfers.chooseRetrieval([
      finishedDownload('a'),
      finishedDownload('b'),
    ]);

    expect(choice.mode).toBe('archive');
    expect(choice.files.map((f) => f.id)).toEqual(['a', 'b']);
  });

  it('ignores rows that cannot be fetched when counting', () => {
    // two selected, one of them unfetchable: that is one file, so no archive
    const choice = transfers.chooseRetrieval([
      finishedDownload('a'),
      finishedDownload('b', { localFilename: null }),
    ]);

    expect(choice.mode).toBe('file');
    expect(choice.file.id).toBe('a');
  });

  it('does nothing when nothing in the selection can be fetched', () => {
    expect(
      transfers.chooseRetrieval([
        finishedDownload('a', { state: 'Completed, Errored' }),
      ]).mode,
    ).toBe('none');
  });

  it('does nothing with an empty selection', () => {
    expect(transfers.chooseRetrieval().mode).toBe('none');
    expect(transfers.chooseRetrieval([]).mode).toBe('none');
  });
});

const removableDownload = (extra = {}) => ({
  direction: 'Download',
  id: 'a',
  localFilename: '/downloads/complete/a.flac',
  state: 'Completed, Succeeded',
  ...extra,
});

describe('removalDeletesFile', () => {
  it('is true only where the server has a path it can delete', () => {
    expect(
      transfers.removalDeletesFile({
        deleteFileOnRemoval: true,
        file: removableDownload(),
      }),
    ).toBe(true);
  });

  it('is false with the option off, whatever the row looks like', () => {
    expect(
      transfers.removalDeletesFile({
        deleteFileOnRemoval: false,
        file: removableDownload(),
      }),
    ).toBe(false);
  });

  it('treats a configuration it does not know yet as one that deletes', () => {
    // options arrive over the hub, so this is the state of the first moments
    // the page is on screen. not-yet-known is not the same as not-deleting, and
    // reading it as the latter would drop the confirmation in the one window
    // where nothing can show it is unnecessary
    expect(transfers.removalDeletesFile({ file: removableDownload() })).toBe(
      true,
    );
  });

  it('is false for an upload, which the option does not govern', () => {
    expect(
      transfers.removalDeletesFile({
        deleteFileOnRemoval: true,
        file: removableDownload({ direction: 'Upload' }),
      }),
    ).toBe(false);
  });

  it('is false where no path was ever recorded', () => {
    // a download that finished before the column existed, or that never wrote
    // anything: there is nothing for the removal to reach
    expect(
      transfers.removalDeletesFile({
        deleteFileOnRemoval: true,
        file: removableDownload({ localFilename: null }),
      }),
    ).toBe(false);
  });
});

describe('planRowRemoval', () => {
  it('offers the action on exactly the rows the selection button takes', () => {
    const states = [
      'Requested',
      'Queued',
      'Queued, Remotely',
      'Queued, Locally',
      'Initializing',
      'InProgress',
      'Completed, Succeeded',
      'Completed, Cancelled',
      'Completed, TimedOut',
      'Completed, Errored',
      'Completed, Rejected',
    ];

    for (const state of states) {
      expect(
        transfers.planRowRemoval({
          deleteFileOnRemoval: false,
          file: removableDownload({ state }),
        }).offered,
      ).toBe(transfers.isStateRemovable(state));
    }
  });

  it('never offers it over a transfer that is still running', () => {
    // the safety this control leans on hardest: one click can remove a record,
    // and can never abort a transfer in flight
    expect(
      transfers.planRowRemoval({
        deleteFileOnRemoval: false,
        file: removableDownload({ state: 'InProgress' }),
      }).offered,
    ).toBe(false);
  });

  it('asks nothing when the removal deletes nothing', () => {
    expect(
      transfers.planRowRemoval({
        deleteFileOnRemoval: false,
        file: removableDownload(),
      }),
    ).toMatchObject({
      confirm: false,
      deletesFile: false,
      offered: true,
      tooltip: 'Remove this transfer',
    });
  });

  it('asks first when the removal deletes a file, and says so', () => {
    const plan = transfers.planRowRemoval({
      deleteFileOnRemoval: true,
      file: removableDownload(),
    });

    expect(plan.confirm).toBe(true);
    expect(plan.deletesFile).toBe(true);
    expect(plan.prompt).toContain('cannot be undone');
    expect(plan.tooltip).toContain('delete');
  });

  it('names the path it will delete, not the name the row shows', () => {
    // the one thing that identifies the file, and what makes a row that shifted
    // under the pointer visible before anything is destroyed
    expect(
      transfers.planRowRemoval({
        deleteFileOnRemoval: true,
        file: removableDownload({ filename: 'remote\\share\\a.flac' }),
      }).filename,
    ).toBe('/downloads/complete/a.flac');
  });

  it('does not ask over an upload, whose removal takes no file', () => {
    expect(
      transfers.planRowRemoval({
        deleteFileOnRemoval: true,
        file: removableDownload({ direction: 'Upload' }),
      }),
    ).toMatchObject({ confirm: false, deletesFile: false, offered: true });
  });

  it('does not ask when there is no recorded path to delete', () => {
    expect(
      transfers.planRowRemoval({
        deleteFileOnRemoval: true,
        file: removableDownload({ localFilename: null }),
      }).confirm,
    ).toBe(false);
  });

  it('asks even where the last listing said the file had gone', () => {
    // that answer is cached and may be behind the filesystem. a stale 'gone'
    // costs the file this dialog exists to protect; a stale 'there' costs a
    // dialog nobody needed
    expect(
      transfers.planRowRemoval({
        deleteFileOnRemoval: true,
        file: removableDownload({ localFileExists: false }),
      }).confirm,
    ).toBe(true);
  });

  it('survives a row it cannot read', () => {
    expect(transfers.planRowRemoval({}).offered).toBe(false);
    expect(transfers.planRowRemoval({ file: {} }).confirm).toBe(false);
  });

  it('offers the same rows whether or not files are being deleted', () => {
    // what can be removed is one rule; what that removal costs is another. a
    // row must not appear and disappear as the option is turned over
    for (const deleteFileOnRemoval of [true, false, undefined]) {
      expect(
        transfers.planRowRemoval({
          deleteFileOnRemoval,
          file: removableDownload(),
        }).offered,
      ).toBe(true);
    }
  });
});

describe('confirmsRemoval', () => {
  const press = (overrides = {}) =>
    transfers.confirmsRemoval({ key: 'Enter', targetTag: 'DIV', ...overrides });

  it('confirms on Enter', () => {
    expect(press()).toBe(true);
  });

  it('ignores every other key', () => {
    expect(press({ key: 'Escape' })).toBe(false);
    expect(press({ key: ' ' })).toBe(false);
    expect(press({ key: 'a' })).toBe(false);
  });

  it('ignores a key-repeat', () => {
    // a held Enter would confirm, and then act again on whatever took the
    // dialog's place
    expect(press({ repeat: true })).toBe(false);
  });

  it('does nothing while the removal is already running', () => {
    expect(press({ busy: true })).toBe(false);
  });

  it('leaves a focused control to the browser', () => {
    // the browser already activates a focused button on Enter, so acting again
    // when focus is on Cancel would both cancel and confirm
    for (const tag of [
      'BUTTON',
      'button',
      'A',
      'INPUT',
      'SELECT',
      'TEXTAREA',
    ]) {
      expect(press({ targetTag: tag })).toBe(false);
    }
  });
});
