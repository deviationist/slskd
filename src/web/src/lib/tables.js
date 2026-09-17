/*
 * Sorting, selection and column visibility for a table of rows.
 *
 * Written for the search results and then wanted again by the transfers list,
 * which is the moment to stop it being about searches. What is general is the
 * machinery -- compare on a value, turn around, remember it in the url, show
 * and hide columns, say what is selected. What is specific is *which* columns
 * a table has and what each one reads, and that stays with the table.
 *
 * Sorted from the values, never from what the cell shows: a length reads
 * `6:09` and sorts as 369, a size reads `9.2 MB` and sorts as its bytes.
 * Sorting the rendered text would put 10:00 before 6:09 and 9 MB before 80 KB.
 */

/**
 * The columns a table shows to someone who has never touched the setting.
 * @param {object[]} all - Every column the table has.
 * @returns {string[]} The keys of the ones not marked optional.
 */
export const defaultColumns = (all = []) =>
  all.filter((c) => !c.optional).map((c) => c.key);

/*
 * `numeric` so `track 2` comes before `track 10` rather than after it, which
 * is what anyone sorting a list of tracks means by alphabetical. `base` so
 * case and accents do not split names that read as the same.
 */
const collator = new Intl.Collator(undefined, {
  numeric: true,
  sensitivity: 'base',
});

/**
 * Whether a value is one the column can order at all.
 */
const missing = (value, kind) =>
  value === undefined ||
  value === null ||
  value === '' ||
  (kind === 'number' && Number.isNaN(Number(value)));

/**
 * A sort as a list of keys, whatever shape the caller had one in.
 *
 * Accepts the single-column shorthand as well, so a table with one key to
 * sort on never has to build an array to say so.
 * @param {object} params
 * @param {object[]} params.sort - The keys, most significant first.
 * @param {string} params.column - A single column, when there is no list.
 * @param {string} params.direction - Its direction.
 * @returns {{column: string, direction: string}[]} The keys.
 */
const sortKeys = ({ sort, column, direction }) => {
  if (Array.isArray(sort)) {
    return sort.filter((key) => key?.column);
  }

  return column ? [{ column, direction }] : [];
};

/**
 * Two rows as one key of the sort orders them.
 * @param {object} a - A row.
 * @param {object} b - Another.
 * @param {object} key - The column's spec, plus the direction as a sign.
 * @returns {number} Negative, zero or positive, as a comparator wants.
 */
const compare = (a, b, key) => {
  const left = key.of(a);
  const right = key.of(b);
  const leftMissing = missing(left, key.kind);
  const rightMissing = missing(right, key.kind);

  if (leftMissing || rightMissing) {
    return leftMissing && rightMissing ? 0 : leftMissing ? 1 : -1;
  }

  if (key.kind === 'number') {
    return key.sign * (Number(left) - Number(right));
  }

  return key.sign * collator.compare(String(left), String(right));
};

/**
 * The rows in the order the sort asks for.
 *
 * Several keys, each breaking the ties the one before it left: sorting by
 * extension and then by name is the difference between "the FLACs together"
 * and "the FLACs together, in order". Every key after the first decides
 * strictly fewer rows than the one before, so the list is self-limiting
 * however long it is allowed to grow.
 *
 * A row a key cannot answer for sorts last *within that key*, in both
 * directions, rather than at whichever end is smallest -- sorting by length to
 * find the longest track and being handed a screen of files whose length
 * nobody reported is not an answer to the question. It still takes part in the
 * keys around it.
 * @param {object} params
 * @param {object[]} params.rows - The rows.
 * @param {object[]} params.sort - Keys of `{column, direction}`, most significant first.
 * @param {string} params.column - A single column, for a table with one key; ignored when `sort` is given.
 * @param {string} params.direction - Its direction, 'asc' or 'desc'.
 * @param {object} params.columns - The table's column specs, keyed by column.
 * @returns {object[]} A new, sorted array; the input order where no key is known.
 */
export const sortRows = ({
  rows = [],
  sort,
  column,
  direction = 'asc',
  columns = {},
}) => {
  const keys = sortKeys({ column, direction, sort })
    .map((key) => ({
      ...columns[key.column],
      sign: key.direction === 'desc' ? -1 : 1,
    }))
    .filter((key) => key.of);

  if (keys.length === 0) {
    return rows;
  }

  // a copy: the caller's array is memoised upstream and sorting in place would
  // quietly reorder it for everything else reading the same reference
  return [...rows].sort((a, b) => {
    for (const key of keys) {
      const answer = compare(a, b, key);

      if (answer !== 0) {
        return answer;
      }
    }

    return 0;
  });
};

/**
 * What clicking a column header should do next.
 *
 * A plain click starts over with that column: it becomes the whole sort,
 * ascending. Clicking the column that is *already* the only one turns it
 * around, and a third click gives up on it -- because a sort that cannot be
 * undone leaves no way back to the order the rows arrived in, which is itself
 * meaningful here, being the peers ranked by whatever the dropdown last chose.
 *
 * A click that asks to `append` adds the column as a further key instead,
 * breaking the ties the keys before it leave. The same three clicks apply to
 * that one key: ascending, descending, gone -- and removing the middle key of
 * three leaves the other two in the order they were in.
 * @param {object} params
 * @param {string} params.column - The column that was clicked.
 * @param {object[]} params.sort - The sort now, most significant first.
 * @param {boolean} params.append - Whether to add to the sort rather than replace it.
 * @param {string} params.current - A single sorted column, for a caller that has no list.
 * @param {string} params.direction - Its direction.
 * @returns {{column: string, direction: string}[]} The new sort.
 */
export const nextSort = ({
  column,
  sort,
  append = false,
  current,
  direction,
}) => {
  const keys = sortKeys({ column: current, direction, sort });
  const existing = keys.find((key) => key.column === column);

  // asc -> desc -> gone, for the key that was clicked
  const turned = (key) =>
    key?.direction === 'asc'
      ? { column, direction: 'desc' }
      : key?.direction === 'desc'
        ? undefined
        : { column, direction: 'asc' };

  if (append) {
    const turnedKey = turned(existing);

    if (!existing) {
      return [...keys, turnedKey];
    }

    return keys
      .map((key) => (key.column === column ? turnedKey : key))
      .filter(Boolean);
  }

  // a plain click on a column that shares the sort with others collapses to
  // it rather than turning it around: "sort by this" is what the click means,
  // and starting somewhere other than ascending would be answering a question
  // nobody asked
  const only = keys.length === 1 && existing;
  const collapsed = only ? turned(existing) : { column, direction: 'asc' };

  return collapsed ? [collapsed] : [];
};

/**
 * What a sortable column header says it does, for anyone who has not guessed
 * that a second key is a shift away.
 */
export const SORT_HINT =
  'Click to sort by this column; shift-click to sort by it as well';

/**
 * The same thing said as a statement rather than as an instruction about one
 * header, for the line above the table.
 */
export const SORT_HINT_TEXT =
  'Hold shift while clicking a column header to sort by several at once';

/**
 * How a column header should draw itself, given the sort.
 *
 * The rank is shown only once there is more than one key, because "1" beside
 * the only sorted column answers a question nobody is asking; with two it is
 * the whole of what distinguishes them.
 * @param {object} params
 * @param {object[]} params.sort - The sort, most significant first.
 * @param {string} params.column - The column being drawn.
 * @returns {{sorted: string|undefined, rank: number|undefined}} What Semantic's `sorted` prop wants, and the key's position.
 */
export const sortStateOf = ({ sort = [], column }) => {
  const at = sort.findIndex((key) => key.column === column);

  if (at < 0) {
    return { rank: undefined, sorted: undefined };
  }

  return {
    rank: sort.length > 1 ? at + 1 : undefined,
    sorted: sort[at].direction === 'desc' ? 'descending' : 'ascending',
  };
};

/**
 * Reads the sort out of a query string, ignoring anything it does not know.
 *
 * `?sort=ext:asc,user:desc` -- and the older `?sort=ext&dir=desc`, which is
 * what a link written before there was more than one key looks like. An
 * unknown column is dropped rather than honoured: the parameter comes from a
 * url someone else wrote, and a typo should not empty the list.
 * @param {string} search - `location.search`.
 * @param {object} columns - The table's column specs, so an unknown one is refused.
 * @returns {{column: string, direction: string}[]} The sort, most significant first.
 */
export const sortFromQuery = (search, columns = {}) => {
  const params = new URLSearchParams(search ?? '');
  const raw = params.get('sort') ?? '';
  const fallback = params.get('dir') === 'desc' ? 'desc' : 'asc';
  const seen = new Set();

  return raw
    .split(',')
    .filter(Boolean)
    .map((part) => {
      const [column, direction] = part.split(':');

      return {
        column,
        // the legacy `dir` applies to the one column that shape could carry
        direction: direction ? direction : fallback,
      };
    })
    .filter((key) => {
      if (!columns[key.column] || seen.has(key.column)) {
        return false;
      }

      seen.add(key.column);
      return true;
    })
    .map((key) => ({
      column: key.column,
      direction: key.direction === 'desc' ? 'desc' : 'asc',
    }));
};

/**
 * Writes the sort into a query string, leaving every other parameter alone.
 *
 * Always the `column:direction` form, and the older `dir` parameter is cleared
 * with it -- one shape written, two read, so a stale `dir` left in the url
 * cannot outlive the sort it described.
 * @param {object} params
 * @param {string} params.search - The current `location.search`.
 * @param {object[]} params.sort - The keys, or nothing to clear the sort.
 * @param {string} params.column - A single column, for a caller that has no list.
 * @param {string} params.direction - Its direction.
 * @returns {string} The new query string, with a leading '?' or empty.
 */
export const sortToQuery = ({ search, sort, column, direction }) => {
  const params = new URLSearchParams(search ?? '');
  const keys = sortKeys({ column, direction, sort });

  params.delete('dir');

  if (keys.length > 0) {
    params.set(
      'sort',
      keys
        .map(
          (key) => `${key.column}:${key.direction === 'desc' ? 'desc' : 'asc'}`,
        )
        .join(','),
    );
  } else {
    params.delete('sort');
  }

  const next = params.toString();

  // URLSearchParams escapes the two characters this format is punctuated with,
  // leaving `?sort=ext%3Aasc%2Cname%3Aasc` in the address bar. Both are legal
  // raw inside a query *value* and neither delimits anything -- `&` and `=`
  // do, and those stay escaped -- so putting these two back is readable
  // without being wrong, for this parameter or for any other the query holds.
  return next ? `?${next.replaceAll('%3A', ':').replaceAll('%2C', ',')}` : '';
};

/**
 * What a select-all checkbox over a list of rows should show.
 *
 * Three states rather than two: a box that is merely unticked while half the
 * list is selected says the opposite of what is true. The count comes back
 * with it because the caller needs it in the same breath -- to label the
 * download, and to decide whether to offer clearing at all.
 * @param {object} params
 * @param {object[]} params.rows - Every row currently listed.
 * @param {Set<string>} params.selected - Keys of the selected rows.
 * @returns {{all: boolean, some: boolean, count: number}} The state of the box.
 */
export const selectionState = ({ rows = [], selected = new Set() }) => {
  const count = rows.filter((row) => selected.has(row.key)).length;

  return {
    all: rows.length > 0 && count === rows.length,
    count,

    // strictly between: `some` is what draws the dash, and a full selection
    // draws a tick instead
    some: count > 0 && count < rows.length,
  };
};

/**
 * The line above the list: how many files there are, and how many are picked.
 *
 * "605 files, all selected" rather than "605 files, 605 selected". The second
 * makes the reader compare two numbers to learn something the first just says,
 * and they are the same number often enough for that to be a chore.
 * @param {object} params
 * @param {number} params.total - How many files are listed.
 * @param {object} params.selection - The result of `selectionState`.
 * @param {string} params.noun - What the rows are, singular.
 * @returns {string} The line.
 */
export const describeSelection = ({ total = 0, selection, noun = 'file' }) => {
  const files = `${total} ${noun}${total === 1 ? '' : 's'}`;

  if (!selection?.count) {
    return files;
  }

  return selection.all
    ? `${files}, all selected`
    : `${files}, ${selection.count} selected`;
};

/**
 * What a table with no rows should say for itself.
 *
 * A table that draws its header and then nothing looks like one still loading,
 * or like a fault. The distinction worth drawing is *why* it is empty: nothing
 * here at all is a different fact from nothing that matches, and only the
 * second one is answered by clearing a filter.
 * @param {object} params
 * @param {number} params.total - How many rows there are before filtering.
 * @param {string} params.noun - What the rows are, plural.
 * @param {string} [params.query] - The filter in force, if there is one.
 * @returns {string} The sentence.
 */
export const describeEmpty = ({ total = 0, noun = 'rows', query }) => {
  if (total === 0) {
    return `No ${noun}`;
  }

  if (query) {
    return `None of the ${total} ${noun} match '${query}'`;
  }

  // filtered by something with no text to quote -- the searches page hides
  // locked files and peers with no free slot, neither of which is a phrase
  return `None of the ${total} ${noun} are shown by the filters in force`;
};

/**
 * Reads a stored column list, and copes with anything else.
 *
 * Nothing stored means the defaults rather than nothing: an empty table is a
 * worse answer to a cleared browser than the table everyone else sees. An
 * unknown key is dropped -- the value outlives the version that wrote it, and
 * a column removed in a later release should not leave a hole.
 * @param {object} params
 * @param {string} params.stored - The saved value, or null.
 * @param {object[]} params.all - Every column the table has.
 * @returns {string[]} Column keys, in this file's order.
 */
export const parseColumns = ({ stored, all = [] }) => {
  if (typeof stored !== 'string') {
    return defaultColumns(all);
  }

  const asked = new Set(stored.split(',').filter(Boolean));
  const known = all.filter((c) => asked.has(c.key)).map((c) => c.key);

  // every known column switched off is a choice, but a stored value naming
  // *nothing* known is a value from another version or a typo, and the
  // defaults are the better answer to it
  return known.length > 0 || asked.size === 0 ? known : defaultColumns(all);
};

/**
 * Turns one column on or off, keeping the canonical order.
 * @param {object} params
 * @param {string[]} params.columns - The columns shown now.
 * @param {string} params.key - The column to change.
 * @param {boolean} params.on - Whether it should be shown.
 * @param {object[]} params.all - Every column the table has, for the order.
 * @returns {string[]} The new list.
 */
export const withColumn = ({ columns = [], key, on, all = [] }) => {
  const wanted = new Set(columns);

  if (on) {
    wanted.add(key);
  } else {
    wanted.delete(key);
  }

  return all.filter((c) => wanted.has(c.key)).map((c) => c.key);
};
