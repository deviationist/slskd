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
