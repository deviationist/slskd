import { COLUMNS, DEFAULT_COLUMNS, SORT_COLUMNS } from './searches';
import * as tables from './tables';

// The machinery a table needs, tested against the search table's own columns --
// which is the only place it is used so far, and a realistic set: two numeric,
// four textual, three of them optional.

describe('selectionState', () => {
  const rows = [{ key: 'a' }, { key: 'b' }, { key: 'c' }];

  it('is empty when nothing is selected', () => {
    const state = tables.selectionState({ rows, selected: new Set() });

    expect(state).toEqual({ all: false, count: 0, some: false });
  });

  it('is indeterminate when only some are', () => {
    // the state that matters: an unticked box over a half-selected list says
    // the opposite of what is true
    const state = tables.selectionState({ rows, selected: new Set(['a']) });

    expect(state.some).toBe(true);
    expect(state.all).toBe(false);
    expect(state.count).toBe(1);
  });

  it('is full, not indeterminate, when every row is selected', () => {
    const state = tables.selectionState({
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
    const state = tables.selectionState({
      rows,
      selected: new Set(['a', 'b', 'c', 'gone']),
    });

    expect(state.count).toBe(3);
    expect(state.all).toBe(true);
  });

  it('is not "all" over an empty list', () => {
    expect(tables.selectionState({ rows: [], selected: new Set() }).all).toBe(
      false,
    );
  });
});

// / A `selectionState` result, without having to build the rows behind it.
const asSelection = (count, all) => ({ all, count, some: count > 0 && !all });

describe('describeSelection', () => {
  it('says how many files there are when none are picked', () => {
    expect(
      tables.describeSelection({
        selection: asSelection(0, false),
        total: 605,
      }),
    ).toBe('605 files');
  });

  it('counts a partial selection', () => {
    expect(
      tables.describeSelection({
        selection: asSelection(12, false),
        total: 605,
      }),
    ).toBe('605 files, 12 selected');
  });

  it('says "all selected" rather than repeating the number', () => {
    // "605 files, 605 selected" makes the reader compare two numbers to learn
    // what the words can just say
    expect(
      tables.describeSelection({
        selection: asSelection(605, true),
        total: 605,
      }),
    ).toBe('605 files, all selected');
  });

  it('gets the singular right', () => {
    expect(
      tables.describeSelection({ selection: asSelection(0, false), total: 1 }),
    ).toBe('1 file');
  });

  it('copes with no selection state at all', () => {
    expect(tables.describeSelection({ total: 3 })).toBe('3 files');
    expect(tables.describeSelection({})).toBe('0 files');
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
    tables
      .sortRows({ column, columns: SORT_COLUMNS, direction, rows })
      .map((r) => r.username);

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
    const names = tables
      .sortRows({
        column: 'name',
        columns: SORT_COLUMNS,
        direction: 'asc',
        rows,
      })
      .map((r) => r.filename.split('\\').pop());

    expect(names).toEqual(['track 2.mp3', 'track 3.mp3', 'track 10.mp3']);
  });

  it('groups equal attributes together', () => {
    // the point of sorting this column: all the 320s in one place
    const grouped = tables
      .sortRows({
        column: 'attributes',
        columns: SORT_COLUMNS,
        direction: 'asc',
        rows,
      })
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
      tables
        .sortRows({
          column: 'length',
          columns: SORT_COLUMNS,
          direction: 'asc',
          rows: withGaps,
        })
        .map((r) => r.username),
    ).toEqual(['has', 'also', 'none']);
    expect(
      tables
        .sortRows({
          column: 'length',
          columns: SORT_COLUMNS,
          direction: 'desc',
          rows: withGaps,
        })
        .map((r) => r.username),
    ).toEqual(['also', 'has', 'none']);
  });

  it('breaks the first key’s ties with the second', () => {
    // the whole point of a second key: the 320s together, and in order within
    const tied = [
      { filename: 'x\\c.mp3', bitRate: 320, username: 'carol' },
      { filename: 'x\\a.mp3', bitRate: 128, username: 'alice' },
      { filename: 'x\\b.mp3', bitRate: 320, username: 'bob' },
    ];

    expect(
      tables
        .sortRows({
          columns: SORT_COLUMNS,
          rows: tied,
          sort: [
            { column: 'attributes', direction: 'asc' },
            { column: 'user', direction: 'asc' },
          ],
        })
        .map((r) => r.username),
    ).toEqual(['alice', 'bob', 'carol']);
  });

  it('leaves the first key’s order alone where it decides', () => {
    // a second key may only break ties; reordering anything the first key
    // already settled would make the sort unreadable
    expect(
      tables
        .sortRows({
          columns: SORT_COLUMNS,
          rows,
          sort: [
            { column: 'user', direction: 'asc' },
            { column: 'size', direction: 'desc' },
          ],
        })
        .map((r) => r.username),
    ).toEqual(['alice', 'bob', 'carol']);
  });

  it('sorts each key its own way around', () => {
    const tied = [
      { filename: 'x\\a.mp3', bitRate: 320, username: 'alice' },
      { filename: 'x\\b.mp3', bitRate: 320, username: 'bob' },
      { filename: 'x\\c.mp3', bitRate: 128, username: 'carol' },
    ];

    expect(
      tables
        .sortRows({
          columns: SORT_COLUMNS,
          rows: tied,
          sort: [
            { column: 'attributes', direction: 'desc' },
            { column: 'user', direction: 'desc' },
          ],
        })
        .map((r) => r.username),
    ).toEqual(['bob', 'alice', 'carol']);
  });

  it('a row the first key cannot answer for still sorts by the second', () => {
    // it goes last within that key, but it is not exiled from the list -- the
    // keys after it still have something to say about where it lands
    const withGaps = [
      { filename: 'x\\b.mp3', username: 'bob' },
      { filename: 'x\\a.mp3', length: 100, username: 'alice' },
      { filename: 'x\\c.mp3', username: 'carol' },
    ];

    expect(
      tables
        .sortRows({
          columns: SORT_COLUMNS,
          rows: withGaps,
          sort: [
            { column: 'length', direction: 'asc' },
            { column: 'user', direction: 'asc' },
          ],
        })
        .map((r) => r.username),
    ).toEqual(['alice', 'bob', 'carol']);
  });

  it('drops a key it does not know and honours the rest', () => {
    expect(
      tables
        .sortRows({
          columns: SORT_COLUMNS,
          rows,
          sort: [
            { column: 'nonsense', direction: 'asc' },
            { column: 'user', direction: 'asc' },
          ],
        })
        .map((r) => r.username),
    ).toEqual(['alice', 'bob', 'carol']);
  });

  it('leaves the order alone for a column it does not know', () => {
    expect(
      tables
        .sortRows({ column: 'nonsense', columns: SORT_COLUMNS, rows })
        .map((r) => r.username),
    ).toEqual(['carol', 'alice', 'bob']);
  });

  it('does not sort the caller’s array in place', () => {
    // the rows are memoised upstream; sorting in place would reorder them for
    // everything else holding the same reference
    const original = [...rows];

    tables.sortRows({
      column: 'size',
      columns: SORT_COLUMNS,
      direction: 'desc',
      rows,
    });

    expect(rows).toEqual(original);
  });
});

describe('nextSort', () => {
  it('starts a new column ascending', () => {
    expect(
      tables.nextSort({
        column: 'size',
        sort: [{ column: 'name', direction: 'desc' }],
      }),
    ).toEqual([{ column: 'size', direction: 'asc' }]);
  });

  it('turns the same column around', () => {
    expect(
      tables.nextSort({
        column: 'size',
        sort: [{ column: 'size', direction: 'asc' }],
      }),
    ).toEqual([{ column: 'size', direction: 'desc' }]);
  });

  it('gives up on the third click', () => {
    // without this there is no way back to the order the results arrived in,
    // which is itself meaningful -- the peers as the dropdown ranked them
    expect(
      tables.nextSort({
        column: 'size',
        sort: [{ column: 'size', direction: 'desc' }],
      }),
    ).toEqual([]);
  });

  it('still understands a caller with a single column and no list', () => {
    expect(
      tables.nextSort({ column: 'size', current: 'size', direction: 'asc' }),
    ).toEqual([{ column: 'size', direction: 'desc' }]);
  });

  it('appends a second key rather than replacing the first', () => {
    expect(
      tables.nextSort({
        append: true,
        column: 'name',
        sort: [{ column: 'ext', direction: 'asc' }],
      }),
    ).toEqual([
      { column: 'ext', direction: 'asc' },
      { column: 'name', direction: 'asc' },
    ]);
  });

  it('turns an appended key around in place, keeping its rank', () => {
    // the key's *position* is what it means; turning it around must not
    // promote it past the key it breaks the ties of
    expect(
      tables.nextSort({
        append: true,
        column: 'ext',
        sort: [
          { column: 'ext', direction: 'asc' },
          { column: 'name', direction: 'asc' },
        ],
      }),
    ).toEqual([
      { column: 'ext', direction: 'desc' },
      { column: 'name', direction: 'asc' },
    ]);
  });

  it('drops one key of several, leaving the rest in order', () => {
    expect(
      tables.nextSort({
        append: true,
        column: 'name',
        sort: [
          { column: 'ext', direction: 'asc' },
          { column: 'name', direction: 'desc' },
          { column: 'size', direction: 'asc' },
        ],
      }),
    ).toEqual([
      { column: 'ext', direction: 'asc' },
      { column: 'size', direction: 'asc' },
    ]);
  });

  it('collapses a multi-key sort to the column plainly clicked', () => {
    // a plain click means "sort by this", and answering it by turning the
    // column around would be answering a different question
    expect(
      tables.nextSort({
        column: 'name',
        sort: [
          { column: 'ext', direction: 'asc' },
          { column: 'name', direction: 'desc' },
        ],
      }),
    ).toEqual([{ column: 'name', direction: 'asc' }]);
  });
});

describe('sortStateOf', () => {
  const sort = [
    { column: 'ext', direction: 'asc' },
    { column: 'name', direction: 'desc' },
  ];

  it('gives Semantic the word it draws its arrow from', () => {
    expect(tables.sortStateOf({ column: 'ext', sort }).sorted).toBe(
      'ascending',
    );
    expect(tables.sortStateOf({ column: 'name', sort }).sorted).toBe(
      'descending',
    );
    expect(tables.sortStateOf({ column: 'size', sort }).sorted).toBeUndefined();
  });

  it('ranks the keys when there is more than one', () => {
    expect(tables.sortStateOf({ column: 'ext', sort }).rank).toBe(1);
    expect(tables.sortStateOf({ column: 'name', sort }).rank).toBe(2);
  });

  it('says nothing about rank when there is only one key', () => {
    // "1" beside the only sorted column answers a question nobody asked
    expect(
      tables.sortStateOf({
        column: 'ext',
        sort: [{ column: 'ext', direction: 'asc' }],
      }).rank,
    ).toBeUndefined();
  });
});

describe('sortFromQuery / sortToQuery', () => {
  it('round-trips a sort', () => {
    const query = tables.sortToQuery({
      search: '',
      sort: [{ column: 'size', direction: 'desc' }],
    });

    expect(query).toBe('?sort=size:desc');
    expect(tables.sortFromQuery(query, SORT_COLUMNS)).toEqual([
      { column: 'size', direction: 'desc' },
    ]);
  });

  it('round-trips several keys, in order', () => {
    const query = tables.sortToQuery({
      search: '',
      sort: [
        { column: 'user', direction: 'asc' },
        { column: 'size', direction: 'desc' },
      ],
    });

    expect(query).toBe('?sort=user:asc,size:desc');
    expect(tables.sortFromQuery(query, SORT_COLUMNS)).toEqual([
      { column: 'user', direction: 'asc' },
      { column: 'size', direction: 'desc' },
    ]);
  });

  it('still reads a link written before there was more than one key', () => {
    expect(tables.sortFromQuery('?sort=size&dir=desc', SORT_COLUMNS)).toEqual([
      { column: 'size', direction: 'desc' },
    ]);
  });

  it('clears the older parameter when it writes', () => {
    // a `dir` left behind would outlive the sort it described and be read as
    // the direction of whatever came next
    expect(
      tables.sortToQuery({
        search: '?sort=size&dir=desc',
        sort: [{ column: 'user', direction: 'asc' }],
      }),
    ).toBe('?sort=user:asc');
  });

  it('ignores a column it does not know', () => {
    // the parameter comes from a url someone else wrote; a typo should not
    // empty the list or throw
    expect(
      tables.sortFromQuery('?sort=drop%20table&dir=desc', SORT_COLUMNS),
    ).toEqual([]);
    expect(
      tables.sortFromQuery('?sort=nonsense:asc,user:desc', SORT_COLUMNS),
    ).toEqual([{ column: 'user', direction: 'desc' }]);
  });

  it('keeps the first mention of a column repeated in the query', () => {
    expect(
      tables.sortFromQuery('?sort=user:asc,user:desc', SORT_COLUMNS),
    ).toEqual([{ column: 'user', direction: 'asc' }]);
  });

  it('defaults an unknown direction to ascending', () => {
    expect(
      tables.sortFromQuery('?sort=user:sideways', SORT_COLUMNS)[0].direction,
    ).toBe('asc');
    expect(
      tables.sortFromQuery('?sort=user&dir=sideways', SORT_COLUMNS)[0]
        .direction,
    ).toBe('asc');
  });

  it('leaves other parameters alone', () => {
    expect(
      tables.sortToQuery({
        search: '?ignore=abc',
        sort: [{ column: 'user', direction: 'asc' }],
      }),
    ).toBe('?ignore=abc&sort=user:asc');
  });

  it('clears the sort without emptying the query', () => {
    expect(
      tables.sortToQuery({
        search: '?ignore=abc&sort=user&dir=asc',
        sort: [],
      }),
    ).toBe('?ignore=abc');
    expect(tables.sortToQuery({ search: '?sort=user', sort: [] })).toBe('');
  });
});

describe('the peer columns sort', () => {
  const rows = [
    {
      filename: 'a\\x.mp3',
      username: 'slow',
      uploadSpeed: 100,
      queueLength: 9,
      hasFreeUploadSlot: false,
    },
    {
      filename: 'a\\y.mp3',
      username: 'fast',
      uploadSpeed: 9_000,
      queueLength: 0,
      hasFreeUploadSlot: true,
    },
    {
      filename: 'a\\z.mp3',
      username: 'mid',
      uploadSpeed: 900,
      queueLength: 3,
      hasFreeUploadSlot: false,
    },
  ];

  const order = (column, direction = 'asc') =>
    tables
      .sortRows({ column, columns: SORT_COLUMNS, direction, rows })
      .map((r) => r.username);

  it('orders by upload speed', () => {
    expect(order('speed')).toEqual(['slow', 'mid', 'fast']);
    expect(order('speed', 'desc')).toEqual(['fast', 'mid', 'slow']);
  });

  it('orders by queue length', () => {
    expect(order('queue')).toEqual(['fast', 'mid', 'slow']);
  });

  it('orders by free slot, rather than merely grouping', () => {
    // descending is the useful click: the peers who can send now, first
    expect(order('slot', 'desc')).toEqual(['fast', 'slow', 'mid']);
    expect(order('slot')[0]).not.toBe('fast');
  });
});

// / A selection as a sorted list, which is what the assertions compare.
const keys = (set) => [...set].sort();

describe('selectRange', () => {
  const rows = ['a', 'b', 'c', 'd', 'e'].map((key) => ({ key }));

  it('takes everything between the anchor and the click', () => {
    expect(
      keys(
        tables.selectRange({
          anchor: 'a',
          checked: true,
          key: 'd',
          rows,
          selected: new Set(['a']),
        }),
      ),
    ).toEqual(['a', 'b', 'c', 'd']);
  });

  it('works upwards as well as down', () => {
    // the anchor is where the range is measured from, not where it starts
    expect(
      keys(
        tables.selectRange({
          anchor: 'd',
          checked: true,
          key: 'b',
          rows,
          selected: new Set(['d']),
        }),
      ),
    ).toEqual(['b', 'c', 'd']);
  });

  it('unticks a range when the click unticks', () => {
    // always selecting would leave no way to take a run back out
    expect(
      keys(
        tables.selectRange({
          anchor: 'a',
          checked: false,
          key: 'c',
          rows,
          selected: new Set(['a', 'b', 'c', 'd']),
        }),
      ),
    ).toEqual(['d']);
  });

  it('leaves rows outside the range alone', () => {
    expect(
      keys(
        tables.selectRange({
          anchor: 'b',
          checked: true,
          key: 'c',
          rows,
          selected: new Set(['e']),
        }),
      ),
    ).toEqual(['b', 'c', 'e']);
  });

  it('is a plain toggle when the anchor is no longer listed', () => {
    // a sort or a filter can remove it between the two clicks, and a range
    // with one end missing would be measured from somewhere nobody pointed at
    expect(
      keys(
        tables.selectRange({
          anchor: 'gone',
          checked: true,
          key: 'c',
          rows,
          selected: new Set(),
        }),
      ),
    ).toEqual(['c']);
  });

  it('measures in the order the rows are drawn', () => {
    // "between" means between on screen, so it follows the sort in force
    const sorted = ['e', 'd', 'c', 'b', 'a'].map((key) => ({ key }));

    expect(
      keys(
        tables.selectRange({
          anchor: 'e',
          checked: true,
          key: 'c',
          rows: sorted,
          selected: new Set(),
        }),
      ),
    ).toEqual(['c', 'd', 'e']);
  });

  it('does not disturb the caller\u2019s set', () => {
    const selected = new Set(['a']);

    tables.selectRange({
      anchor: 'a',
      checked: true,
      key: 'c',
      rows,
      selected,
    });

    expect([...selected]).toEqual(['a']);
  });
});

describe('moveColumn', () => {
  const columns = ['name', 'path', 'user', 'size'];

  it('moves a column one place left', () => {
    expect(tables.moveColumn({ by: -1, columns, key: 'user' })).toEqual([
      'name',
      'user',
      'path',
      'size',
    ]);
  });

  it('moves a column one place right', () => {
    expect(tables.moveColumn({ by: 1, columns, key: 'path' })).toEqual([
      'name',
      'user',
      'path',
      'size',
    ]);
  });

  it('clamps rather than wrapping at each end', () => {
    // a column at the end that jumps to the front because its owner pressed
    // the arrow once too often has not done what the arrow says
    expect(tables.moveColumn({ by: -1, columns, key: 'name' })).toEqual(
      columns,
    );
    expect(tables.moveColumn({ by: 1, columns, key: 'size' })).toEqual(columns);
  });

  it('leaves a column it does not have alone', () => {
    expect(tables.moveColumn({ by: 1, columns, key: 'speed' })).toEqual(
      columns,
    );
  });

  it('does not disturb the caller’s array', () => {
    const original = [...columns];

    tables.moveColumn({ by: 1, columns, key: 'name' });

    expect(columns).toEqual(original);
  });
});

describe('withColumn, once the order is a choice', () => {
  it('puts a column back where this file would have it', () => {
    // switching Speed on should put it where Speed goes, which is what
    // appending would fail to do for anyone who has never reordered anything
    expect(
      tables.withColumn({
        all: COLUMNS,
        columns: ['name', 'user', 'size'],
        key: 'path',
        on: true,
      }),
    ).toEqual(['name', 'path', 'user', 'size']);
  });

  it('leaves a reordered list reordered', () => {
    // it reads the shown order rather than rebuilding from the canonical one,
    // so a column an operator has moved stays moved
    expect(
      tables.withColumn({
        all: COLUMNS,
        columns: ['user', 'name'],
        key: 'size',
        on: true,
      }),
    ).toEqual(['user', 'name', 'size']);
  });

  it('appends where nothing shown comes after it', () => {
    expect(
      tables.withColumn({
        all: COLUMNS,
        columns: ['name'],
        key: 'queue',
        on: true,
      }),
    ).toEqual(['name', 'queue']);
  });

  it('removes without disturbing the order of the rest', () => {
    expect(
      tables.withColumn({
        all: COLUMNS,
        columns: ['user', 'name', 'size'],
        key: 'name',
        on: false,
      }),
    ).toEqual(['user', 'size']);
  });
});

describe('describeEmpty', () => {
  it('says there are none when there are none', () => {
    expect(tables.describeEmpty({ noun: 'downloads', total: 0 })).toBe(
      'No downloads',
    );
  });

  it('says a filter is why, and quotes it', () => {
    // the distinction the row exists to draw: this one is answered by
    // clearing the filter, and the one above is not
    expect(
      tables.describeEmpty({ noun: 'files', query: 'flac', total: 1_170 }),
    ).toBe("None of the 1170 files match 'flac'");
  });

  it('copes with a filter that has no phrase to quote', () => {
    // hide-locked and hide-no-free-slots are filters with nothing to put in
    // quotes, and a table emptied by them still owes an explanation
    expect(tables.describeEmpty({ noun: 'files', total: 40 })).toBe(
      'None of the 40 files are shown by the filters in force',
    );
  });

  it('prefers "none at all" over "none match"', () => {
    // an empty list with a filter typed into it has nothing to say about the
    // filter: it would be empty either way
    expect(
      tables.describeEmpty({ noun: 'uploads', query: 'flac', total: 0 }),
    ).toBe('No uploads');
  });
});

describe('parseColumns / withColumn', () => {
  it('shows everything but the peer columns by default', () => {
    expect(DEFAULT_COLUMNS).toEqual([
      'name',
      'path',
      'user',
      'size',
      'attributes',
      'length',
    ]);
    expect(tables.parseColumns({ all: COLUMNS, stored: null })).toEqual(
      DEFAULT_COLUMNS,
    );
    expect(tables.parseColumns({ all: COLUMNS, stored: undefined })).toEqual(
      DEFAULT_COLUMNS,
    );
  });

  it('reads a stored list back', () => {
    expect(
      tables.parseColumns({ all: COLUMNS, stored: 'name,size,speed' }),
    ).toEqual(['name', 'size', 'speed']);
  });

  it('keeps the stored order, which is the order they are drawn', () => {
    // it used to re-sort into this file's order on the way out, on the
    // grounds that a column list is a set -- true until the order became
    // something an operator could choose, at which point discarding it
    // discards the choice
    expect(
      tables.parseColumns({ all: COLUMNS, stored: 'queue,name,size' }),
    ).toEqual(['queue', 'name', 'size']);
  });

  it('keeps the first mention of a column stored twice', () => {
    expect(
      tables.parseColumns({ all: COLUMNS, stored: 'name,size,name' }),
    ).toEqual(['name', 'size']);
  });

  it('drops a column it no longer has', () => {
    expect(
      tables.parseColumns({ all: COLUMNS, stored: 'name,bitrot,size' }),
    ).toEqual(['name', 'size']);
  });

  it('treats every column switched off as a choice', () => {
    // but a value naming nothing known is a value from another version
    expect(tables.parseColumns({ all: COLUMNS, stored: '' })).toEqual([]);
    expect(
      tables.parseColumns({ all: COLUMNS, stored: 'bitrot,gone' }),
    ).toEqual(DEFAULT_COLUMNS);
  });

  it('adds and removes a column, keeping the order', () => {
    expect(
      tables.withColumn({
        all: COLUMNS,
        columns: ['name', 'size'],
        key: 'path',
        on: true,
      }),
    ).toEqual(['name', 'path', 'size']);
    expect(
      tables.withColumn({
        all: COLUMNS,
        columns: ['name', 'path', 'size'],
        key: 'path',
        on: false,
      }),
    ).toEqual(['name', 'size']);
  });

  it('is unbothered by switching on what is already on', () => {
    expect(
      tables.withColumn({
        all: COLUMNS,
        columns: ['name'],
        key: 'name',
        on: true,
      }),
    ).toEqual(['name']);
    expect(
      tables.withColumn({ all: COLUMNS, columns: [], key: 'nope', on: true }),
    ).toEqual([]);
  });
});
