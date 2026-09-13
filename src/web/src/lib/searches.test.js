import * as search from './searches';

describe('filterResponse', () => {
  it('removes VBR files if "iscbr" is specified', () => {
    const response = {
      files: [
        { bitRate: 123, isVariableBitRate: true },
        { bitRate: 320, isVariableBitRate: false },
      ],
    };

    const filters = { isCBR: true };

    expect(search.filterResponse({ filters, response })).toMatchObject({
      files: [{ bitRate: 320, isVariableBitRate: false }],
    });
  });

  it('removes CBR files if "isvbr" is specified', () => {
    const response = {
      files: [
        { bitRate: 123, isVariableBitRate: true },
        { bitRate: 320, isVariableBitRate: false },
      ],
    };

    const filters = { isVBR: true };

    expect(search.filterResponse({ filters, response })).toMatchObject({
      files: [{ bitRate: 123, isVariableBitRate: true }],
    });
  });

  it('removes all files if "iscbr" and "isvbr" are both specified', () => {
    const response = {
      files: [{ isVariableBitrate: true }, { isVariableBitrate: false }],
    };

    const filters = { isCBR: true, isVBR: true };

    expect(search.filterResponse({ filters, response })).toMatchObject({
      files: [],
    });
  });

  it('removes lossy files if "islossless" is specified', () => {
    const response = {
      files: [
        { bitDepth: 16, sampleRate: 41_000 },
        { bitRate: 320, isVariableBitRate: false },
      ],
    };

    const filters = { isLossless: true };

    expect(search.filterResponse({ filters, response })).toMatchObject({
      files: [{ bitDepth: 16, sampleRate: 41_000 }],
    });
  });

  it('removes lossless files if "islossy" is specified', () => {
    const response = {
      files: [
        { bitDepth: 16, sampleRate: 41_000 },
        { bitRate: 320, isVariableBitRate: false },
      ],
    };

    const filters = { isLossy: true };

    expect(search.filterResponse({ filters, response })).toMatchObject({
      files: [{ bitRate: 320, isVariableBitRate: false }],
    });
  });

  it('removes files with bitRate less than minBitRate', () => {
    const response = {
      files: [{ bitRate: 100 }, { bitRate: 99 }],
    };

    const filters = { minBitRate: 100 };

    expect(search.filterResponse({ filters, response })).toMatchObject({
      files: [{ bitRate: 100 }],
    });
  });

  it('removes files with size less than minFileSize', () => {
    const response = {
      files: [{ size: 100 }, { size: 99 }],
    };

    const filters = { minFileSize: 100 };

    expect(search.filterResponse({ filters, response })).toMatchObject({
      files: [{ size: 100 }],
    });
  });

  it('removes files with length less than minLength', () => {
    const response = {
      files: [{ length: 100 }, { length: 99 }],
    };

    const filters = { minLength: 100 };

    expect(search.filterResponse({ filters, response })).toMatchObject({
      files: [{ length: 100 }],
    });
  });

  describe('term filtering', () => {
    const response = {
      files: [
        { filename: '/path/to/foo.mp3' },
        { filename: '/path/to/bar.mp3' },
        { filename: '/path/to/baz.mp3' },
        { filename: '/path/to/qux.mp3' },
        { filename: '/path/to/info.nfo' },
        { filename: '/path/to/folder.jpg' },
      ],
    };

    it('removes files with filenames not containing included phrases', () => {
      const filters = { include: ['path', 'to', '.nfo'] };

      expect(search.filterResponse({ filters, response })).toMatchObject({
        files: [{ filename: '/path/to/info.nfo' }],
      });
    });

    it('removes files with filenames containing excluded phrases', () => {
      const filters = { exclude: ['bar', 'jpg', 'qux'] };

      expect(search.filterResponse({ filters, response })).toMatchObject({
        files: [
          { filename: '/path/to/foo.mp3' },
          { filename: '/path/to/baz.mp3' },
          { filename: '/path/to/info.nfo' },
        ],
      });
    });

    it('removes a mix of includes and excludes', () => {
      const filters = {
        exclude: ['foo', 'bar'],
        include: ['path', '.mp3'],
      };

      expect(search.filterResponse({ filters, response })).toMatchObject({
        files: [
          { filename: '/path/to/baz.mp3' },
          { filename: '/path/to/qux.mp3' },
        ],
      });
    });
  });
});

describe('parseFiltersFromString', () => {
  it('returns correct minBitrate', () => {
    expect(search.parseFiltersFromString('foo minbr:42 bar')).toMatchObject({
      minBitRate: 42,
    });

    expect(
      search.parseFiltersFromString('foo minbitrate:123 bar'),
    ).toMatchObject({
      minBitRate: 123,
    });
  });

  it('returns correct minFileSize', () => {
    expect(search.parseFiltersFromString('foo minfs:42 bar')).toMatchObject({
      minFileSize: 42,
    });

    expect(
      search.parseFiltersFromString('foo minfilesize:123 bar'),
    ).toMatchObject({
      minFileSize: 123,
    });
  });

  it('returns correct minLength', () => {
    expect(search.parseFiltersFromString('foo minlen:42 bar')).toMatchObject({
      minLength: 42,
    });

    expect(
      search.parseFiltersFromString('foo minlength:123 bar'),
    ).toMatchObject({
      minLength: 123,
    });
  });

  it('returns correct minFilesInFolder', () => {
    expect(search.parseFiltersFromString('foo minfif:42 bar')).toMatchObject({
      minFilesInFolder: 42,
    });

    expect(
      search.parseFiltersFromString('foo minfilesinfolder:123 bar'),
    ).toMatchObject({
      minFilesInFolder: 123,
    });
  });

  it('returns correct list of terms', () => {
    expect(search.parseFiltersFromString('foo minbr:42 bar')).toMatchObject({
      include: ['foo', 'bar'],
    });

    expect(search.parseFiltersFromString('foo iscbr isvbr bar')).toMatchObject({
      include: ['foo', 'bar'],
    });

    expect(search.parseFiltersFromString('foo some:thing bar')).toMatchObject({
      include: ['foo', 'bar'],
    });

    expect(search.parseFiltersFromString('foo -bar')).toMatchObject({
      exclude: ['bar'],
      include: ['foo'],
    });

    expect(search.parseFiltersFromString('-foo -bar -baz qux')).toMatchObject({
      exclude: ['foo', 'bar', 'baz'],
      include: ['qux'],
    });

    expect(search.parseFiltersFromString('foo bar baz -qux')).toMatchObject({
      exclude: ['qux'],
      include: ['foo', 'bar', 'baz'],
    });
  });

  it('returns isVBR and isCBR if terms are present', () => {
    expect(search.parseFiltersFromString('isvbr')).toMatchObject({
      isVBR: true,
    });

    expect(search.parseFiltersFromString('iscbr')).toMatchObject({
      isCBR: true,
    });
  });

  it('returns expected filters given a bit of everything', () => {
    expect(
      search.parseFiltersFromString(
        'big -mix of:everything isvbr iscbr minbr:42',
      ),
    ).toMatchObject({
      exclude: ['mix'],
      include: ['big'],
      isCBR: true,
      isVBR: true,
      minBitRate: 42,
    });
  });
});

describe('search.validateSearchText', () => {
  it('accepts a phrase', () => {
    expect(search.validateSearchText('aphex twin').ok).toBe(true);
  });

  it('refuses nothing at all', () => {
    expect(search.validateSearchText('').ok).toBe(false);
    expect(search.validateSearchText(undefined).ok).toBe(false);
    expect(search.validateSearchText(null).ok).toBe(false);
  });

  it('refuses whitespace, as the server does', () => {
    expect(search.validateSearchText('   ').ok).toBe(false);
    expect(search.validateSearchText('\t\n').ok).toBe(false);
  });

  it('refuses in the server own words, so the three buttons agree', () => {
    // the plus and magnifier learn this from the server; the watch button never
    // reaches it, and three different refusals for one rule is worse than one
    expect(search.validateSearchText('').reason).toBe(
      'The field SearchText can not be null, empty, or consist of only whitespace',
    );
  });
});

describe('flattenResponses', () => {
  const responses = [
    {
      username: 'alice',
      hasFreeUploadSlot: true,
      uploadSpeed: 900,
      queueLength: 0,
      files: [
        { filename: 'a\\one.flac', size: 10, bitRate: 1_000, length: 200 },
        { filename: 'a\\two.flac', size: 20, bitRate: 1_000, length: 300 },
      ],
      lockedFiles: [{ filename: 'a\\three.flac', size: 30 }],
    },
    {
      username: 'bob',
      hasFreeUploadSlot: false,
      uploadSpeed: 100,
      queueLength: 4,
      files: [{ filename: 'b\\one.mp3', size: 5, bitRate: 320, length: 200 }],
    },
  ];

  it('puts every file from every response in one list', () => {
    expect(
      search.flattenResponses({ responses }).map((r) => r.filename),
    ).toEqual(['a\\one.flac', 'a\\two.flac', 'a\\three.flac', 'b\\one.mp3']);
  });

  it('carries the peer down onto each file', () => {
    // the whole point: in a flat list there is nowhere else to show who has it
    const [first] = search.flattenResponses({ responses });

    expect(first.username).toBe('alice');
    expect(first.hasFreeUploadSlot).toBe(true);
    expect(first.uploadSpeed).toBe(900);
    expect(first.queueLength).toBe(0);
  });

  it('keeps the fields a sort would use', () => {
    const [first] = search.flattenResponses({ responses });

    expect(first.size).toBe(10);
    expect(first.bitRate).toBe(1_000);
    expect(first).toHaveLength(200);
  });

  it('marks locked files rather than dropping them', () => {
    // hiding them is the toggle's job, upstream of this, and it does it by
    // emptying lockedFiles on the response
    const locked = search
      .flattenResponses({ responses })
      .filter((r) => r.locked);

    expect(locked).toHaveLength(1);
    expect(locked[0].filename).toBe('a\\three.flac');
  });

  it('keys a row on the peer as well as the path', () => {
    // the same release sits on dozens of peers; a filename alone would collide
    // constantly, and two rows sharing a key means selecting one selects both
    const keys = search.flattenResponses({
      responses: [
        { username: 'alice', files: [{ filename: 'x\\same.flac', size: 1 }] },
        { username: 'bob', files: [{ filename: 'x\\same.flac', size: 1 }] },
      ],
    });

    expect(keys[0].key).not.toBe(keys[1].key);
    expect(new Set(keys.map((k) => k.key)).size).toBe(2);
  });

  it('copes with a response that has neither collection', () => {
    expect(search.flattenResponses({ responses: [{ username: 'a' }] })).toEqual(
      [],
    );
    expect(search.flattenResponses({})).toEqual([]);
  });
});

describe('groupByUser', () => {
  it('turns a mixed selection into one request per peer', () => {
    const grouped = search.groupByUser([
      { username: 'alice', filename: 'a\\one.flac', size: 10 },
      { username: 'bob', filename: 'b\\one.mp3', size: 5 },
      { username: 'alice', filename: 'a\\two.flac', size: 20 },
    ]);

    expect(grouped).toHaveLength(2);
    expect(grouped.find((g) => g.username === 'alice').files).toEqual([
      { filename: 'a\\one.flac', size: 10 },
      { filename: 'a\\two.flac', size: 20 },
    ]);
    expect(grouped.find((g) => g.username === 'bob').files).toEqual([
      { filename: 'b\\one.mp3', size: 5 },
    ]);
  });

  it('sends only what the transfer API takes', () => {
    // a row carries the peer's upload speed and its own locked flag; posting
    // those would be sending the server fields it has no use for
    const [{ files }] = search.groupByUser([
      {
        username: 'alice',
        filename: 'a.flac',
        size: 1,
        locked: false,
        uploadSpeed: 9,
      },
    ]);

    expect(Object.keys(files[0]).sort()).toEqual(['filename', 'size']);
  });

  it('is empty for an empty selection', () => {
    expect(search.groupByUser([])).toEqual([]);
    expect(search.groupByUser()).toEqual([]);
  });
});

describe('selectionState', () => {
  const rows = [{ key: 'a' }, { key: 'b' }, { key: 'c' }];

  it('is empty when nothing is selected', () => {
    const state = search.selectionState({ rows, selected: new Set() });

    expect(state).toEqual({ all: false, count: 0, some: false });
  });

  it('is indeterminate when only some are', () => {
    // the state that matters: an unticked box over a half-selected list says
    // the opposite of what is true
    const state = search.selectionState({ rows, selected: new Set(['a']) });

    expect(state.some).toBe(true);
    expect(state.all).toBe(false);
    expect(state.count).toBe(1);
  });

  it('is full, not indeterminate, when every row is selected', () => {
    const state = search.selectionState({
      rows,
      selected: new Set(['a', 'b', 'c']),
    });

    expect(state.all).toBe(true);
    expect(state.some).toBe(false);
  });

  it('ignores selected keys that are no longer listed', () => {
    // a selection outlives the filter in force when it was made, so a row can
    // be selected and then filtered away. counting the set rather than the
    // rows would report more selected than the list contains, and mark a
    // fully-selected list as merely partial
    const state = search.selectionState({
      rows,
      selected: new Set(['a', 'b', 'c', 'gone']),
    });

    expect(state.count).toBe(3);
    expect(state.all).toBe(true);
  });

  it('is not "all" over an empty list', () => {
    expect(search.selectionState({ rows: [], selected: new Set() }).all).toBe(
      false,
    );
  });
});

describe('indexDownloads / downloadStateOf', () => {
  const downloads = [
    {
      username: 'alice',
      directories: [
        {
          files: [
            {
              username: 'alice',
              filename: 'a\\got-it.flac',
              size: 100,
              state: 'Completed, Succeeded',
            },
            {
              username: 'alice',
              filename: 'a\\in-flight.flac',
              size: 200,
              state: 'InProgress',
            },
            {
              username: 'alice',
              filename: 'a\\queued.flac',
              size: 300,
              state: 'Queued, Remotely',
            },
            {
              username: 'alice',
              filename: 'a\\broke.flac',
              size: 400,
              state: 'Completed, Errored',
            },
          ],
        },
      ],
    },
  ];

  const index = search.indexDownloads(downloads);
  const state = (row) => search.downloadStateOf({ index, row });

  it('knows a file it already took from that peer', () => {
    expect(
      state({ username: 'alice', filename: 'a\\got-it.flac', size: 100 }),
    ).toBe('downloaded');
  });

  it('treats anything not yet finished as in flight', () => {
    expect(
      state({ username: 'alice', filename: 'a\\in-flight.flac', size: 200 }),
    ).toBe('downloading');
    expect(
      state({ username: 'alice', filename: 'a\\queued.flac', size: 300 }),
    ).toBe('downloading');
  });

  it('marks a download that ended without the file', () => {
    // worth saying rather than hiding: the row is a second chance at the same
    // file, not a repeat of a success
    expect(
      state({ username: 'alice', filename: 'a\\broke.flac', size: 400 }),
    ).toBe('failed');
  });

  it('says nothing about a file it has never seen', () => {
    expect(
      state({ username: 'alice', filename: 'a\\new.flac', size: 500 }),
    ).toBeUndefined();
  });

  it('recognises the same file offered by a different peer', () => {
    // the useful case when searching again later: you took it from someone
    // else last time, and the row from this peer is the same file
    expect(
      state({ username: 'bob', filename: 'z\\got-it.flac', size: 100 }),
    ).toBe('have');
  });

  it('does not call it the same file on a matching name alone', () => {
    // `Cover.jpg` and `01 - Intro.mp3` are not evidence of anything; the size
    // is what stops every album's artwork lighting up at once
    expect(
      state({ username: 'bob', filename: 'z\\got-it.flac', size: 999 }),
    ).toBeUndefined();
  });

  it('does not offer another peer a failed download as reassurance', () => {
    expect(
      state({ username: 'bob', filename: 'z\\broke.flac', size: 400 }),
    ).toBeUndefined();
  });

  it('lets the exact match outrank the guess', () => {
    // same name and size as the succeeded one, but this peer's own attempt
    // failed -- the certain answer is the one worth showing
    const conflicting = search.indexDownloads([
      {
        username: 'alice',
        directories: [
          {
            files: [
              {
                username: 'alice',
                filename: 'a\\x.flac',
                size: 1,
                state: 'Completed, Succeeded',
              },
              {
                username: 'bob',
                filename: 'b\\x.flac',
                size: 1,
                state: 'Completed, Cancelled',
              },
            ],
          },
        ],
      },
    ]);

    expect(
      search.downloadStateOf({
        index: conflicting,
        row: { username: 'bob', filename: 'b\\x.flac', size: 1 },
      }),
    ).toBe('failed');
  });

  it('copes with an empty or absent list', () => {
    expect(
      search.downloadStateOf({ index: search.indexDownloads(), row: {} }),
    ).toBeUndefined();
    expect(search.downloadStateOf({ row: {} })).toBeUndefined();
  });
});

describe('folderOf', () => {
  it('takes the folder the file is actually in', () => {
    expect(
      search.folderOf({
        filename: '@@abc\\Music\\FLAC\\Artist - Album (2003)\\01.flac',
      }),
    ).toBe('Artist - Album (2003)');
  });

  it('takes the last segment, not the first', () => {
    // the end of someone else's library path is the part that says anything;
    // the start is their drive letter and their username
    expect(
      search.folderOf({
        filename: 'C:\\shared\\music\\Aphex Twin - SAW\\a.mp3',
      }),
    ).toBe('Aphex Twin - SAW');
  });

  it('handles forward slashes too', () => {
    expect(search.folderOf({ filename: '/home/x/Some Album/track.flac' })).toBe(
      'Some Album',
    );
  });

  it('is empty for a file with no folder', () => {
    // getDirectoryName returns the whole path when there is no separator, and
    // that is the filename -- showing it in a Folder column would be a lie
    expect(search.folderOf({ filename: 'loose.mp3' })).toBe('');
    expect(search.folderOf({ filename: '' })).toBe('');
    expect(search.folderOf({})).toBe('');
  });

  it('ignores a trailing separator rather than returning nothing', () => {
    expect(search.folderOf({ filename: 'a\\Album\\\\track.mp3' })).toBe(
      'Album',
    );
  });
});
