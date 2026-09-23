import * as tables from './tables';
import * as transfers from './transfers';

describe('describeEnqueueFailures', () => {
  it('uses the server\u2019s reason when every file was refused for it', () => {
    // which is nearly always "already in progress", and is the useful half
    expect(
      transfers.describeEnqueueFailures({
        failures: [
          { filename: 'a.flac', message: 'Skipped: Already in progress' },
          { filename: 'b.flac', message: 'Skipped: Already in progress' },
        ],
        username: 'bob',
      }),
    ).toBe('Could not enqueue 2 files from bob: Already in progress');
  });

  it('gets the singular right', () => {
    expect(
      transfers.describeEnqueueFailures({
        failures: [
          { filename: 'a.flac', message: 'Skipped: Already in progress' },
        ],
        username: 'bob',
      }),
    ).toBe('Could not enqueue 1 file from bob: Already in progress');
  });

  it('counts rather than guesses where the reasons differ', () => {
    // two reasons cannot both be the sentence, and picking one would report
    // the wrong thing about half the files
    expect(
      transfers.describeEnqueueFailures({
        failures: [
          { filename: 'a.flac', message: 'Skipped: Already in progress' },
          { filename: 'b.flac', message: 'Something else entirely' },
        ],
        username: 'bob',
      }),
    ).toBe('Could not enqueue 2 files from bob');
  });

  it('says something when the server said nothing', () => {
    expect(
      transfers.describeEnqueueFailures({
        failures: [{ filename: 'a.flac' }],
        username: 'bob',
      }),
    ).toBe('Could not enqueue 1 file from bob');
  });
});

describe('the search a download came from', () => {
  const row = {
    filename: 'x\\a.flac',
    searchId: '8f21c430-7c03-4199-8cce-35053f8892b3',
    searchText: 'aphex twin',
  };

  it('sorts on the text, which is what the column shows', () => {
    const rows = [
      { ...row, searchText: 'zomby' },
      { ...row, searchText: 'aphex twin' },
    ];

    expect(
      tables
        .sortRows({
          column: 'search',
          columns: transfers.TRANSFER_SORT_COLUMNS,
          direction: 'asc',
          rows,
        })
        .map((r) => r.searchText),
    ).toEqual(['aphex twin', 'zomby']);
  });

  it('puts the downloads that came from no search last', () => {
    // sorting by origin to find out where something came from, and being
    // handed the ones with no origin first, is not an answer
    const rows = [{ filename: 'x\\b.flac' }, row];

    expect(
      tables
        .sortRows({
          column: 'search',
          columns: transfers.TRANSFER_SORT_COLUMNS,
          direction: 'asc',
          rows,
        })
        .map((r) => r.searchText),
    ).toEqual(['aphex twin', undefined]);
  });

  it('is a column that is off until it is asked for', () => {
    expect(
      transfers.TRANSFER_COLUMNS.find((c) => c.key === 'search').optional,
    ).toBe(true);
  });
});

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

describe('planSelectionRemoval', () => {
  const download = (localFilename) => ({
    direction: 'Download',
    localFilename,
    state: 'Completed, Succeeded',
  });

  it('asks nothing when nothing would be deleted', () => {
    // the same argument the row makes: a dialog over a harmless action is one
    // that gets dismissed unread, which is how the one that matters gets
    // clicked through
    const plan = transfers.planSelectionRemoval({
      files: [{ direction: 'Upload', state: 'Completed, Succeeded' }],
    });

    expect(plan.confirm).toBe(false);
    expect(plan.deleting).toBe(0);
  });

  it('counts only the rows that would lose a file', () => {
    const plan = transfers.planSelectionRemoval({
      files: [download('/a.flac'), download(null), download('/b.flac')],
    });

    expect(plan.deleting).toBe(2);
    expect(plan.filenames).toEqual(['/a.flac', '/b.flac']);
  });

  it('names the paths the server will delete', () => {
    const plan = transfers.planSelectionRemoval({
      files: [download('/a.flac')],
    });

    expect(plan.filenames).toEqual(['/a.flac']);
    expect(plan.header).toBe('Delete this file?');
  });

  it('counts in the plural where there are several', () => {
    const plan = transfers.planSelectionRemoval({
      files: [download('/a.flac'), download('/b.flac')],
    });

    expect(plan.header).toBe('Delete 2 files?');
    expect(plan.confirmLabel).toBe('Remove and delete 2');
  });

  it('caps the list and says how many it did not name', () => {
    // a confirmation nobody reads to the end is one nobody read; the count
    // carries the weight
    const files = Array.from({ length: 25 }, (_, index) =>
      download(`/${index}.flac`),
    );
    const plan = transfers.planSelectionRemoval({ files });

    expect(plan.filenames).toHaveLength(10);
    expect(plan.remaining).toBe(15);
    expect(plan.deleting).toBe(25);
  });

  it('asks nothing when the server does not delete on removal', () => {
    const plan = transfers.planSelectionRemoval({
      deleteFileOnRemoval: false,
      files: [download('/a.flac')],
    });

    expect(plan.confirm).toBe(false);
  });
});

describe('flattenTransfers', () => {
  const users = [
    {
      username: 'alice',
      directories: [
        {
          directory: 'a\\Album',
          files: [
            {
              id: '1',
              username: 'alice',
              filename: 'a\\Album\\one.flac',
              size: 10,
              state: 'InProgress',
            },
            {
              id: '2',
              username: 'alice',
              filename: 'a\\Album\\two.flac',
              size: 20,
              state: 'Completed, Succeeded',
            },
          ],
        },
        {
          directory: 'a\\Other',
          files: [
            {
              id: '3',
              username: 'alice',
              filename: 'a\\Other\\three.mp3',
              size: 5,
            },
          ],
        },
      ],
    },
    {
      username: 'bob',
      directories: [
        {
          directory: 'b',
          files: [
            { id: '4', username: 'bob', filename: 'b\\four.mp3', size: 1 },
          ],
        },
      ],
    },
  ];

  it('puts every transfer from every folder and peer in one list', () => {
    expect(transfers.flattenTransfers(users).map((r) => r.id)).toEqual([
      '1',
      '2',
      '3',
      '4',
    ]);
  });

  it('carries the folder down onto each row', () => {
    // a flat table has no header above the rows to put it in
    expect(transfers.flattenTransfers(users).map((r) => r.directory)).toEqual([
      'a\\Album',
      'a\\Album',
      'a\\Other',
      'b',
    ]);
  });

  it('keys on the transfer id rather than building one', () => {
    // unlike a search result, a transfer is already unique across peers: two
    // peers sending the same path are two transfers with two ids
    const rows = transfers.flattenTransfers(users);

    expect(rows.map((r) => r.key)).toEqual(['1', '2', '3', '4']);
    expect(new Set(rows.map((r) => r.key)).size).toBe(4);
  });

  it('falls back to the user when a file does not name one', () => {
    expect(
      transfers.flattenTransfers([
        {
          username: 'carol',
          directories: [
            { directory: 'c', files: [{ id: '9', filename: 'c\\x.mp3' }] },
          ],
        },
      ])[0].username,
    ).toBe('carol');
  });

  it('copes with a user or folder that has nothing in it', () => {
    expect(transfers.flattenTransfers([{ username: 'empty' }])).toEqual([]);
    expect(
      transfers.flattenTransfers([
        { username: 'e', directories: [{ directory: 'd' }] },
      ]),
    ).toEqual([]);
    expect(transfers.flattenTransfers()).toEqual([]);
  });
});

describe('the transfers table sorts', () => {
  const rows = transfers.flattenTransfers([
    {
      username: 'zoe',
      directories: [
        {
          directory: 'z',
          files: [
            {
              id: '1',
              filename: 'z\\b.flac',
              size: 900,
              state: 'InProgress',
              averageSpeed: 10,
              attempts: 1,
            },
            {
              id: '2',
              filename: 'z\\a.flac',
              size: 10,
              state: 'Completed, Errored',
              averageSpeed: 90,
              attempts: 3,
            },
          ],
        },
      ],
    },
  ]);

  const order = (column, direction = 'asc') =>
    tables
      .sortRows({
        column,
        columns: transfers.TRANSFER_SORT_COLUMNS,
        direction,
        rows,
      })
      .map((r) => r.id);

  it('orders by size, speed and attempts numerically', () => {
    expect(order('size')).toEqual(['2', '1']);
    expect(order('speed')).toEqual(['1', '2']);
    expect(order('attempts', 'desc')).toEqual(['2', '1']);
  });

  it('orders by name and folder alphabetically', () => {
    expect(order('name')).toEqual(['2', '1']);
  });

  it('gathers the failures together rather than ranking progress', () => {
    // a finished transfer and one that never started are 100 and 0 with
    // nothing in between, so the useful thing this column does is group
    expect(order('state')).toEqual(['2', '1']);
  });

  it('shows everything but speed and attempts by default', () => {
    expect(tables.defaultColumns(transfers.TRANSFER_COLUMNS)).toEqual([
      'name',
      'path',
      'user',
      'state',
      'size',
    ]);
  });
});

describe('filterTransfers', () => {
  const users = [
    {
      username: 'alice',
      directories: [
        {
          directory: 'a\\Album',
          files: [
            {
              id: '1',
              username: 'alice',
              filename: 'a\\Album\\one.flac',
              state: 'InProgress',
            },
            {
              id: '2',
              username: 'alice',
              filename: 'a\\Album\\two.mp3',
              state: 'Completed, Errored',
            },
          ],
        },
      ],
    },
    {
      username: 'bob',
      directories: [
        {
          directory: 'b',
          files: [
            {
              id: '3',
              username: 'bob',
              filename: 'b\\three.flac',
              state: 'Queued, Remotely',
            },
          ],
        },
      ],
    },
  ];

  const ids = (query) =>
    transfers
      .filterTransfers({ query, users })
      .flatMap((u) => u.directories.flatMap((d) => d.files.map((f) => f.id)));

  it('returns everything for an empty query', () => {
    expect(transfers.filterTransfers({ users })).toBe(users);
    expect(ids('   ')).toEqual(['1', '2', '3']);
  });

  it('matches the filename', () => {
    expect(ids('flac')).toEqual(['1', '3']);
  });

  it('matches the peer, so one peer’s queue needs no control of its own', () => {
    expect(ids('bob')).toEqual(['3']);
  });

  it('matches the state, so the failures need none either', () => {
    expect(ids('errored')).toEqual(['2']);
  });

  it('requires every term', () => {
    expect(ids('album flac')).toEqual(['1']);
    expect(ids('album nothing')).toEqual([]);
  });

  it('excludes a term with a leading dash', () => {
    expect(ids('flac -bob')).toEqual(['1']);
    expect(ids('-flac')).toEqual(['2']);
  });

  it('is case insensitive', () => {
    expect(ids('FLAC')).toEqual(['1', '3']);
  });

  it('drops the folders and peers left holding nothing', () => {
    // what makes one filter serve both views: the cards show only the peers
    // with a match, and the table flattens whatever survived
    const kept = transfers.filterTransfers({ query: 'bob', users });

    expect(kept).toHaveLength(1);
    expect(kept[0].username).toBe('bob');
    expect(kept[0].directories).toHaveLength(1);
  });

  it('leaves the caller’s transfers alone', () => {
    transfers.filterTransfers({ query: 'flac', users });

    expect(users[0].directories[0].files).toHaveLength(2);
  });
});

describe('instantOf', () => {
  const now = Date.parse('2026-09-23T12:00:00Z');

  it('reads a timestamp with a zone as that instant', () => {
    expect(transfers.instantOf('2026-09-17T00:02:33.8265042Z', now)).toBe(
      Date.parse('2026-09-17T00:02:33.826Z'),
    );
  });

  it('reads one with no zone as UTC, not as local time', () => {
    // the server writes every one of these as UTC; the database used to hand
    // some back without the Z, and read as local they are off by the offset
    expect(transfers.instantOf('2026-09-17T00:00:49.5357429', now)).toBe(
      Date.parse('2026-09-17T00:00:49.535Z'),
    );
  });

  it('respects an explicit offset', () => {
    expect(transfers.instantOf('2026-09-17T02:00:00+02:00', now)).toBe(
      Date.parse('2026-09-17T00:00:00Z'),
    );
  });

  it.each([
    ['nothing', undefined],
    ['null', null],
    ['an empty string', ''],
    ['something that is not a date', 'soon'],
    ['an unset DateTime', '0001-01-01T00:00:00'],
    ['a moment before slskd existed', '2019-01-01T00:00:00Z'],
    ['a moment well in the future', '2026-09-24T12:00:00Z'],
  ])('refuses %s', (_, value) => {
    expect(transfers.instantOf(value, now)).toBeNull();
  });

  it('allows a few minutes of disagreement between the two clocks', () => {
    expect(transfers.instantOf('2026-09-23T12:03:00Z', now)).not.toBeNull();
  });
});

const today = (hms) => `2026-09-23T${hms}Z`;
const todayMs = (hms) => Date.parse(today(hms));

describe('timingOf', () => {
  const now = Date.parse('2026-09-23T12:00:00Z');

  it('reports all four for a download that went as it should', () => {
    expect(
      transfers.timingOf(
        {
          endedAt: today('11:03:00'),
          requestedAt: today('11:00:00'),
          startedAt: today('11:01:30'),
          state: 'Completed, Succeeded',
        },
        now,
      ),
    ).toEqual({
      finished: todayMs('11:03:00'),
      requested: todayMs('11:00:00'),
      took: 90,
      tookLive: false,
      waited: 90,
      waitedLive: false,
    });
  });

  it('does not believe a start stamped at the same tick as the end', () => {
    // measured on a live row that errored: startedAt and endedAt identical to
    // the tick. a start copied from the end would say it "took 0s" after
    // "waiting" the whole time it sat before failing
    const timing = transfers.timingOf(
      {
        endedAt: '2026-09-17T00:02:33.8265042Z',
        requestedAt: '2026-09-17T00:00:49.5357429',
        startedAt: '2026-09-17T00:02:33.8265042Z',
        state: 'Completed, Errored',
      },
      now,
    );

    expect(timing.took).toBeNull();
    expect(timing.waited).toBeNull();

    // when it ended is still true, and still worth saying
    expect(timing.finished).toBe(Date.parse('2026-09-17T00:02:33.826Z'));
  });

  it('keeps the time a failed transfer really spent receiving', () => {
    // one that ran for a minute and then broke did take a minute
    const timing = transfers.timingOf(
      {
        endedAt: today('11:02:00'),
        requestedAt: today('11:00:00'),
        startedAt: today('11:01:00'),
        state: 'Completed, Errored',
      },
      now,
    );

    expect(timing.took).toBe(60);
    expect(timing.waited).toBe(60);
  });

  it('reports no duration measured backwards', () => {
    const timing = transfers.timingOf(
      {
        endedAt: today('11:00:00'),
        requestedAt: today('11:05:00'),
        startedAt: today('11:04:00'),
        state: 'Completed, Succeeded',
      },
      now,
    );

    expect(timing.waited).toBeNull();
    expect(timing.took).toBeNull();

    // an end before the request is not an end that can be placed either
    expect(timing.finished).toBeNull();

    // the request itself is not contradicted by anything but the others
    expect(timing.requested).toBe(todayMs('11:05:00'));
  });

  it('counts the wait so far for a download still in the queue', () => {
    const timing = transfers.timingOf(
      { requestedAt: today('11:50:00'), state: 'Queued, Remotely' },
      now,
    );

    expect(timing.waited).toBe(600);
    expect(timing.waitedLive).toBe(true);
    expect(timing.took).toBeNull();
    expect(timing.finished).toBeNull();
  });

  it('counts the time so far for a download still receiving', () => {
    const timing = transfers.timingOf(
      {
        requestedAt: today('11:50:00'),
        startedAt: today('11:59:00'),
        state: 'InProgress',
      },
      now,
    );

    expect(timing.waited).toBe(540);
    expect(timing.waitedLive).toBe(false);
    expect(timing.took).toBe(60);
    expect(timing.tookLive).toBe(true);
  });

  it('ignores an end on a row that has not finished', () => {
    // a row the list is still polling can carry a stale end from an earlier
    // attempt; it is not finished until its state says so
    expect(
      transfers.timingOf(
        {
          endedAt: today('11:55:00'),
          requestedAt: today('11:50:00'),
          startedAt: today('11:59:00'),
          state: 'InProgress',
        },
        now,
      ).finished,
    ).toBeNull();
  });

  it('is not live when there is nothing to count from', () => {
    const timing = transfers.timingOf({ state: 'Queued, Remotely' }, now);

    expect(timing.waited).toBeNull();
    expect(timing.waitedLive).toBe(false);
  });
});

describe('speedOf', () => {
  it.each([
    ['a real speed', 125_000, 125_000],
    ['a negative one, seen on a live errored row', -635_672_000, null],
    ['zero', 0, null],
    ['nothing', undefined, null],
    ['something that is not a number', 'fast', null],
    ['infinity', Number.POSITIVE_INFINITY, null],
  ])('%s', (_, averageSpeed, expected) => {
    expect(transfers.speedOf({ averageSpeed })).toBe(expected);
  });
});

describe('the timing columns', () => {
  const now = Date.now();
  const ago = (seconds) => new Date(now - seconds * 1_000).toISOString();

  it('are all off until they are asked for', () => {
    for (const key of ['requested', 'waited', 'took', 'finished']) {
      expect(transfers.TRANSFER_COLUMNS.find((c) => c.key === key)).toEqual(
        expect.objectContaining({ optional: true }),
      );
    }
  });

  it('sort a row with nothing believable last, in both directions', () => {
    const rows = [
      {
        endedAt: ago(10),
        id: 'garbage',
        requestedAt: ago(100),
        startedAt: ago(10),
        state: 'Completed, Errored',
      },
      {
        endedAt: ago(10),
        id: 'slow',
        requestedAt: ago(100),
        startedAt: ago(70),
        state: 'Completed, Succeeded',
      },
      {
        endedAt: ago(10),
        id: 'fast',
        requestedAt: ago(100),
        startedAt: ago(20),
        state: 'Completed, Succeeded',
      },
    ];

    const order = (direction) =>
      tables
        .sortRows({
          column: 'took',
          columns: transfers.TRANSFER_SORT_COLUMNS,
          direction,
          rows,
        })
        .map((r) => r.id);

    expect(order('asc')).toEqual(['fast', 'slow', 'garbage']);
    expect(order('desc')).toEqual(['slow', 'fast', 'garbage']);
  });

  it('sort speed on the speed that is shown', () => {
    const rows = [
      { averageSpeed: -635_672_000, id: 'garbage' },
      { averageSpeed: 200, id: 'fast' },
      { averageSpeed: 100, id: 'slow' },
    ];

    expect(
      tables
        .sortRows({
          column: 'speed',
          columns: transfers.TRANSFER_SORT_COLUMNS,
          direction: 'asc',
          rows,
        })
        .map((r) => r.id),
    ).toEqual(['slow', 'fast', 'garbage']);
  });
});
