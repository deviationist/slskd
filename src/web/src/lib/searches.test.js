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

describe('pathOf', () => {
  it('gives the whole folder path, not just the last segment', () => {
    // where a file came from says as much as what it sits next to: the same
    // album under `incoming` is a different thing from one filed properly
    expect(
      search.pathOf({
        filename: '@@abc\\Music\\FLAC\\Artist - Album (2003)\\01.flac',
      }),
    ).toBe('@@abc\\Music\\FLAC\\Artist - Album (2003)');
  });

  it('handles forward slashes too', () => {
    expect(search.pathOf({ filename: '/home/x/Some Album/track.flac' })).toBe(
      '/home/x/Some Album',
    );
  });

  it('is empty for a file with no folder', () => {
    // getDirectoryName returns the whole path when there is no separator, and
    // that is the filename -- showing it in a Path column would be a lie
    expect(search.pathOf({ filename: 'loose.mp3' })).toBe('');
    expect(search.pathOf({ filename: '' })).toBe('');
    expect(search.pathOf({})).toBe('');
  });
});

describe('describeSelection', () => {
  const state = (count, all) => ({ all, count, some: count > 0 && !all });

  it('says how many files there are when none are picked', () => {
    expect(
      search.describeSelection({ selection: state(0, false), total: 605 }),
    ).toBe('605 files');
  });

  it('counts a partial selection', () => {
    expect(
      search.describeSelection({ selection: state(12, false), total: 605 }),
    ).toBe('605 files, 12 selected');
  });

  it('says "all selected" rather than repeating the number', () => {
    // "605 files, 605 selected" makes the reader compare two numbers to learn
    // what the words can just say
    expect(
      search.describeSelection({ selection: state(605, true), total: 605 }),
    ).toBe('605 files, all selected');
  });

  it('gets the singular right', () => {
    expect(
      search.describeSelection({ selection: state(0, false), total: 1 }),
    ).toBe('1 file');
  });

  it('copes with no selection state at all', () => {
    expect(search.describeSelection({ total: 3 })).toBe('3 files');
    expect(search.describeSelection({})).toBe('0 files');
  });
});

describe('sortRows', () => {
  const rows = [
    {
      filename: 'a\\track 10.mp3',
      size: 900,
      length: 600,
      bitRate: 320,
      username: 'carol',
    },
    {
      filename: 'a\\track 2.mp3',
      size: 80_000,
      length: 369,
      bitRate: 128,
      username: 'alice',
    },
    {
      filename: 'b\\track 3.mp3',
      size: 5_000,
      length: 60,
      bitRate: 320,
      username: 'bob',
    },
  ];

  const order = (column, direction = 'asc') =>
    search.sortRows({ column, direction, rows }).map((r) => r.username);

  it('sorts length as seconds, not as the text it renders', () => {
    // 6:09 is 369 and 10:00 is 600; sorted as text, "10:00" precedes "6:09"
    expect(order('length')).toEqual(['bob', 'alice', 'carol']);
  });

  it('sorts size as bytes, not as the text it renders', () => {
    // 80000 renders smaller than 900 does not; "9.2 MB" vs "80 KB" as text is
    // the wrong order and the reason this reads the raw value
    expect(order('size')).toEqual(['carol', 'bob', 'alice']);
  });

  it('turns around for descending', () => {
    expect(order('length', 'desc')).toEqual(['carol', 'alice', 'bob']);
    expect(order('size', 'desc')).toEqual(['alice', 'bob', 'carol']);
  });

  it('sorts names alphabetically, with numbers read as numbers', () => {
    // "track 2" before "track 10", which is what anyone sorting tracks means
    // by alphabetical; a plain string compare puts "track 10" first
    const names = search
      .sortRows({ column: 'name', direction: 'asc', rows })
      .map((r) => r.filename.split('\\').pop());

    expect(names).toEqual(['track 2.mp3', 'track 3.mp3', 'track 10.mp3']);
  });

  it('groups equal attributes together', () => {
    // the point of sorting this column: all the 320s in one place
    const grouped = search
      .sortRows({ column: 'attributes', direction: 'asc', rows })
      .map((r) => r.bitRate);

    expect(grouped).toEqual([128, 320, 320]);
  });

  it('sorts by user and by folder alphabetically', () => {
    expect(order('user')).toEqual(['alice', 'bob', 'carol']);
    expect(order('path')).toEqual(['carol', 'alice', 'bob']);
  });

  it('puts rows the column cannot answer for last, whichever way it is sorted', () => {
    // sorting by length to find the longest and being handed the ones whose
    // length nobody reported is not an answer to the question
    const withGaps = [
      { filename: 'x\\a.mp3', length: 100, username: 'has' },
      { filename: 'x\\b.mp3', username: 'none' },
      { filename: 'x\\c.mp3', length: 200, username: 'also' },
    ];

    expect(
      search
        .sortRows({ column: 'length', direction: 'asc', rows: withGaps })
        .map((r) => r.username),
    ).toEqual(['has', 'also', 'none']);
    expect(
      search
        .sortRows({ column: 'length', direction: 'desc', rows: withGaps })
        .map((r) => r.username),
    ).toEqual(['also', 'has', 'none']);
  });

  it('leaves the order alone for a column it does not know', () => {
    expect(
      search.sortRows({ column: 'nonsense', rows }).map((r) => r.username),
    ).toEqual(['carol', 'alice', 'bob']);
  });

  it('does not sort the caller’s array in place', () => {
    // the rows are memoised upstream; sorting in place would reorder them for
    // everything else holding the same reference
    const original = [...rows];

    search.sortRows({ column: 'size', direction: 'desc', rows });

    expect(rows).toEqual(original);
  });
});

describe('nextSort', () => {
  it('starts a new column ascending', () => {
    expect(
      search.nextSort({ column: 'size', current: 'name', direction: 'desc' }),
    ).toEqual({
      column: 'size',
      direction: 'asc',
    });
  });

  it('turns the same column around', () => {
    expect(
      search.nextSort({ column: 'size', current: 'size', direction: 'asc' }),
    ).toEqual({
      column: 'size',
      direction: 'desc',
    });
  });

  it('gives up on the third click', () => {
    // without this there is no way back to the order the results arrived in,
    // which is itself meaningful -- the peers as the dropdown ranked them
    expect(
      search.nextSort({ column: 'size', current: 'size', direction: 'desc' }),
    ).toEqual({
      column: undefined,
      direction: undefined,
    });
  });
});

describe('sortFromQuery / sortToQuery', () => {
  it('round-trips a sort', () => {
    const query = search.sortToQuery({
      column: 'size',
      direction: 'desc',
      search: '',
    });

    expect(query).toBe('?sort=size&dir=desc');
    expect(search.sortFromQuery(query)).toEqual({
      column: 'size',
      direction: 'desc',
    });
  });

  it('ignores a column it does not know', () => {
    // the parameter comes from a url someone else wrote; a typo should not
    // empty the list or throw
    expect(search.sortFromQuery('?sort=drop%20table&dir=desc')).toEqual({
      column: undefined,
      direction: 'asc',
    });
  });

  it('defaults an unknown direction to ascending', () => {
    expect(search.sortFromQuery('?sort=user&dir=sideways').direction).toBe(
      'asc',
    );
  });

  it('leaves other parameters alone', () => {
    expect(
      search.sortToQuery({
        column: 'user',
        direction: 'asc',
        search: '?ignore=abc',
      }),
    ).toBe('?ignore=abc&sort=user&dir=asc');
  });

  it('clears the sort without emptying the query', () => {
    expect(
      search.sortToQuery({
        column: undefined,
        search: '?ignore=abc&sort=user&dir=asc',
      }),
    ).toBe('?ignore=abc');
    expect(
      search.sortToQuery({ column: undefined, search: '?sort=user' }),
    ).toBe('');
  });
});
