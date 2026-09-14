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
 * The rows in the order a column asks for.
 *
 * A row the column cannot answer for sorts last in *both* directions rather
 * than at whichever end is smallest. Sorting by length to find the longest
 * track and being handed a screen of files whose length nobody reported is not
 * an answer to the question.
 * @param {object} params
 * @param {object[]} params.rows - The rows.
 * @param {string} params.column - A key of `columns`, or anything else for no sort.
 * @param {object} params.columns - The table's column specs, keyed by column.
 * @param {string} params.direction - 'asc' or 'desc'.
 * @returns {object[]} A new, sorted array; the input order where the column is unknown.
 */
export const sortRows = ({
  rows = [],
  column,
  direction = 'asc',
  columns = {},
}) => {
  const spec = columns[column];

  if (!spec) {
    return rows;
  }

  const sign = direction === 'desc' ? -1 : 1;

  // a copy: the caller's array is memoised upstream and sorting in place would
  // quietly reorder it for everything else reading the same reference
  return [...rows].sort((a, b) => {
    const left = spec.of(a);
    const right = spec.of(b);
    const leftMissing = missing(left, spec.kind);
    const rightMissing = missing(right, spec.kind);

    if (leftMissing || rightMissing) {
      return leftMissing && rightMissing ? 0 : leftMissing ? 1 : -1;
    }

    if (spec.kind === 'number') {
      return sign * (Number(left) - Number(right));
    }

    return sign * collator.compare(String(left), String(right));
  });
};

/**
 * What clicking a column header should do next.
 *
 * A new column starts ascending. The same column again turns around. A third
 * click gives up on it, because a sort that cannot be undone leaves no way
 * back to the order the results arrived in -- which is itself meaningful here,
 * being the peers ranked by whatever the dropdown last chose.
 * @param {object} params
 * @param {string} params.column - The column that was clicked.
 * @param {string} params.current - The column currently sorted on, if any.
 * @param {string} params.direction - Its direction.
 * @returns {{column: string|undefined, direction: string|undefined}} The next state.
 */
export const nextSort = ({ column, current, direction }) => {
  if (column !== current) {
    return { column, direction: 'asc' };
  }

  if (direction === 'asc') {
    return { column, direction: 'desc' };
  }

  return { column: undefined, direction: undefined };
};

/**
 * Reads the sort out of a query string, ignoring anything it does not know.
 * @param {string} search - `location.search`.
 * @param {object} columns - The table's column specs, so an unknown one is refused.
 * @returns {{column: string|undefined, direction: string}} The sort.
 */
export const sortFromQuery = (search, columns = {}) => {
  const params = new URLSearchParams(search ?? '');
  const column = params.get('sort');
  const direction = params.get('dir') === 'desc' ? 'desc' : 'asc';

  // an unknown column is dropped rather than honoured: the parameter comes
  // from a url someone else wrote, and a typo should not empty the list
  return columns[column]
    ? { column, direction }
    : { column: undefined, direction: 'asc' };
};

/**
 * Writes the sort into a query string, leaving every other parameter alone.
 * @param {object} params
 * @param {string} params.search - The current `location.search`.
 * @param {string} params.column - The column, or nothing to clear it.
 * @param {string} params.direction - The direction.
 * @returns {string} The new query string, with a leading '?' or empty.
 */
export const sortToQuery = ({ search, column, direction }) => {
  const params = new URLSearchParams(search ?? '');

  if (column) {
    params.set('sort', column);
    params.set('dir', direction === 'desc' ? 'desc' : 'asc');
  } else {
    params.delete('sort');
    params.delete('dir');
  }

  const next = params.toString();

  return next ? `?${next}` : '';
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
