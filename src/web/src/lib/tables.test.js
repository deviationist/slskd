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
      tables.nextSort({ column: 'size', current: 'name', direction: 'desc' }),
    ).toEqual({
      column: 'size',
      direction: 'asc',
    });
  });

  it('turns the same column around', () => {
    expect(
      tables.nextSort({ column: 'size', current: 'size', direction: 'asc' }),
    ).toEqual({
      column: 'size',
      direction: 'desc',
    });
  });

  it('gives up on the third click', () => {
    // without this there is no way back to the order the results arrived in,
    // which is itself meaningful -- the peers as the dropdown ranked them
    expect(
      tables.nextSort({ column: 'size', current: 'size', direction: 'desc' }),
    ).toEqual({
      column: undefined,
      direction: undefined,
    });
  });
});

describe('sortFromQuery / sortToQuery', () => {
  it('round-trips a sort', () => {
    const query = tables.sortToQuery({
      column: 'size',
      direction: 'desc',
      search: '',
    });

    expect(query).toBe('?sort=size&dir=desc');
    expect(tables.sortFromQuery(query, SORT_COLUMNS)).toEqual({
      column: 'size',
      direction: 'desc',
    });
  });

  it('ignores a column it does not know', () => {
    // the parameter comes from a url someone else wrote; a typo should not
    // empty the list or throw
    expect(
      tables.sortFromQuery('?sort=drop%20table&dir=desc', SORT_COLUMNS),
    ).toEqual({
      column: undefined,
      direction: 'asc',
    });
  });

  it('defaults an unknown direction to ascending', () => {
    expect(
      tables.sortFromQuery('?sort=user&dir=sideways', SORT_COLUMNS).direction,
    ).toBe('asc');
  });

  it('leaves other parameters alone', () => {
    expect(
      tables.sortToQuery({
        column: 'user',
        direction: 'asc',
        search: '?ignore=abc',
      }),
    ).toBe('?ignore=abc&sort=user&dir=asc');
  });

  it('clears the sort without emptying the query', () => {
    expect(
      tables.sortToQuery({
        column: undefined,
        search: '?ignore=abc&sort=user&dir=asc',
      }),
    ).toBe('?ignore=abc');
    expect(
      tables.sortToQuery({ column: undefined, search: '?sort=user' }),
    ).toBe('');
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

  it('keeps this file’s order, not the stored one', () => {
    // a column list is a set; letting a saved value decide the order would
    // leave a reordering nobody asked for alive in a browser forever
    expect(
      tables.parseColumns({ all: COLUMNS, stored: 'queue,name,size' }),
    ).toEqual(['name', 'size', 'queue']);
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
