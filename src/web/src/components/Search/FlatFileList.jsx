import {
  COLUMNS,
  describeSelection,
  downloadStateOf,
  groupByUser,
  nextSort,
  parseColumns,
  pathOf,
  selectionState,
  sortFromQuery,
  sortRows,
  sortToQuery,
  withColumn,
} from '../../lib/searches';
import * as transfers from '../../lib/transfers';
import {
  formatAttributes,
  formatBytes,
  formatSeconds,
  getFileName,
  offsetWithin,
  scrollParentOf,
} from '../../lib/util';
import { useVirtualizer } from '@tanstack/react-virtual';
import React, { useLayoutEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useHistory, useLocation } from 'react-router-dom';
import { toast } from 'react-toastify';
import {
  Button,
  Checkbox,
  Icon,
  Popup,
  Segment,
  Table,
} from 'semantic-ui-react';

/**
 * The height of a row, in px, and the reason the filename column is one line.
 *
 * The virtualiser positions rows by arithmetic rather than by measuring them,
 * so they have to agree on a height. A wrapped filename would make its row two
 * lines tall and every row below it land in the wrong place -- so the column
 * truncates instead, with the full remote path in the cell's title where it
 * was already. Measuring each row is the alternative, and it buys wrapping at
 * the cost of the list shifting under the pointer as rows are measured.
 *
 * Must match `.flatlist tbody td` in App.css. Changing one without the other
 * is a drift the eye catches only at the bottom of a long list.
 */
const ROW_H = 37;

/**
 * The results as one row per file, rather than one card per user.
 *
 * The grouped view answers "what does this user have"; this one answers
 * "what is here", which is the question a sort by size or bitrate is asking.
 * Both are kept -- this is a second way to read the same filtered results, not
 * a replacement -- and the toggle between them is remembered.
 *
 * Every row is in the list. Only the ones on screen are in the DOM, which is
 * what lets that be true for a search of nine hundred files: the rest are two
 * spacer rows holding the scrollbar at the right length. It virtualises
 * against the *window* rather than a box of its own, so the page scrolls the
 * way every other page here does and there is no scrollbar inside a scrollbar.
 *
 * Selection lives here as a set of keys rather than as a `selected` flag
 * written onto each file, which is what the grouped view does. A flat list is
 * rebuilt whenever a filter changes, so a flag written onto a row would be
 * lost with it; a key survives, because it names the file rather than the row.
 * @param {object} params
 * @param {boolean} params.disabled - Whether the search is in a state that forbids downloading.
 * @param {object[]} params.rows - Flattened, already-filtered results.
 * @returns {object} The list.
 */
/**
 * How a row that is already known to the transfer list should read.
 *
 * Semantic's own row states rather than colours of our own, so they follow the
 * theme. `active` for in flight rather than `warning`: yellow beside a green
 * says something went wrong, and nothing has.
 */
const MARKS = {
  downloaded: {
    colour: 'green',
    icon: 'check circle',
    row: 'positive',
    tip: 'Already downloaded from this user',
  },
  downloading: {
    colour: 'blue',
    icon: 'download',
    row: 'active',
    tip: 'Downloading from this user now',
  },
  failed: {
    colour: 'red',
    icon: 'exclamation circle',
    row: 'warning',
    tip: 'A download of this file from this user did not finish',
  },
  have: {
    colour: 'grey',
    icon: 'check',
    row: undefined,
    tip: 'A file with this name and size has already been downloaded, from someone else',
  },
};

/*
 * Which columns this browser shows. Per browser like the view toggle itself,
 * and for the same reason: it says how this operator reads results, not
 * anything about the search.
 *
 * Guarded on both sides -- localStorage throws outright where site data is
 * blocked, and the read runs on the first render.
 */
const COLUMNS_KEY = 'slskd-search-columns';

const readStoredColumns = () => {
  try {
    return parseColumns(window.localStorage.getItem(COLUMNS_KEY));
  } catch {
    return parseColumns(null);
  }
};

const storeColumns = (columns) => {
  try {
    window.localStorage.setItem(COLUMNS_KEY, columns.join(','));
  } catch {
    // a preference that cannot be saved is still a preference for this tab
  }
};

const FlatFileList = ({ disabled, downloads, rows: unsorted }) => {
  const [selected, setSelected] = useState(() => new Set());
  const [downloading, setDownloading] = useState(false);
  const [rowDownloading, setRowDownloading] = useState(undefined);
  /*
   * The sort lives in the query string rather than in state or storage, so a
   * sorted view can be linked to and survives a reload without a second place
   * to keep it. Unknown columns are dropped on the way in -- the value comes
   * from a url someone else may have written.
   */
  const location = useLocation();
  const history = useHistory();
  const { column, direction } = sortFromQuery(location.search);

  const rows = useMemo(
    () => sortRows({ column, direction, rows: unsorted }),
    [column, direction, unsorted],
  );

  const sortBy = (key) => {
    const next = nextSort({ column: key, current: column, direction });

    history.replace({
      pathname: location.pathname,
      search: sortToQuery({ ...next, search: location.search }),
    });
  };

  const [columns, setColumns] = useState(readStoredColumns);
  const shown = COLUMNS.filter((col) => columns.includes(col.key));
  const show = (key) => columns.includes(key);

  const setColumn = (key, on) => {
    const next = withColumn({ columns, key, on });

    setColumns(next);
    storeColumns(next);
  };

  const [footerSlot, setFooterSlot] = useState(null);
  const [scroller, setScroller] = useState(null);
  const [scrollMargin, setScrollMargin] = useState(0);
  const listRef = useRef(null);

  /*
   * Which element the reader is actually scrolling, and how far down it this
   * list begins.
   *
   * Not the window. This app puts `overflow-y: auto` on a wrapper near the
   * root and lets the page grow inside it, so `window.scrollY` stays at 0 no
   * matter how far down the list you are -- a window virtualiser watching it
   * renders the first screenful, never hears about the scroll, and leaves you
   * looking at the blank spacer that holds the rest of the height. Measured,
   * not assumed: the first attempt at this assumed the window and did exactly
   * that.
   *
   * In a layout effect rather than read inline off the ref, which is null on
   * the first render -- reading it there fixes both values at their defaults
   * for the life of the component. Re-measured on resize and when the row
   * count changes, since the summary above reports it and changes height
   * between three digits and four.
   */
  useLayoutEffect(() => {
    const measure = () => {
      const parent = scrollParentOf(listRef.current);

      setScroller(parent);
      setScrollMargin(offsetWithin(listRef.current, parent));
    };

    measure();
    window.addEventListener('resize', measure);

    return () => window.removeEventListener('resize', measure);
  }, [rows.length]);

  /*
   * The footer's slot, looked up after the DOM is committed rather than during
   * render, when it may not exist yet. Null until then, and the action simply
   * is not drawn -- which is the right answer for the one frame it costs.
   */
  useLayoutEffect(() => {
    setFooterSlot(document.querySelector('#footer-action-slot'));
  }, []);

  const virtualizer = useVirtualizer({
    count: rows.length,
    estimateSize: () => ROW_H,
    getScrollElement: () => scroller,
    overscan: 15,
    scrollMargin,
  });

  const virtualRows = virtualizer.getVirtualItems();
  const paddingTop =
    virtualRows.length > 0
      ? virtualRows[0].start - virtualizer.options.scrollMargin
      : 0;
  const paddingBottom =
    virtualRows.length > 0
      ? virtualizer.getTotalSize() -
        (virtualRows[virtualRows.length - 1].end -
          virtualizer.options.scrollMargin)
      : 0;

  // a selection outlives the filter that was in force when it was made, so a
  // row can be selected and then filtered away. deriving from `rows` rather
  // than from the set is what stops the button offering to download a file
  // that is no longer on the page
  const selectedRows = useMemo(
    () => rows.filter((row) => selected.has(row.key)),
    [rows, selected],
  );

  const selection = selectionState({ rows, selected });
  const selectedSize = selectedRows.reduce((total, row) => total + row.size, 0);

  const toggle = (key, checked) =>
    setSelected((old) => {
      const next = new Set(old);

      if (checked) {
        next.add(key);
      } else {
        next.delete(key);
      }

      return next;
    });

  // every row is listed, so this is the whole filtered set and says so. it was
  // "the rows on this page" while the list was paged, which needed an extra
  // control to reach the rest; there are no pages now
  const toggleAll = (checked) =>
    setSelected(checked ? new Set(rows.map((row) => row.key)) : new Set());

  /*
   * One row, on its own. Goes through `groupByUser` like the bulk download so
   * there is one place that decides what is sent -- a second hand-built body
   * here is how the two drift into disagreeing about it.
   */
  const downloadRow = async (row) => {
    setRowDownloading(row.key);

    try {
      const [group] = groupByUser([row]);

      await transfers.download(group);
      toast.success(`Enqueued ${getFileName(row.filename)}`);
    } catch (error) {
      console.error(error);
      toast.error(`Could not enqueue from ${row.username}`);
    } finally {
      setRowDownloading(undefined);
    }
  };

  const download = async () => {
    setDownloading(true);

    // one request per peer: selecting across users is the point of a flat
    // list, and the transfer API takes a single user at a time
    const groups = groupByUser(selectedRows);
    const failed = [];

    for (const group of groups) {
      try {
        // eslint-disable-next-line no-await-in-loop
        await transfers.download(group);
      } catch (error) {
        console.error(error);
        failed.push(group.username);
      }
    }

    setDownloading(false);

    if (failed.length === 0) {
      toast.success(
        `Enqueued ${selectedRows.length} file${selectedRows.length === 1 ? '' : 's'} from ${groups.length} user${groups.length === 1 ? '' : 's'}`,
      );
      setSelected(new Set());
      return;
    }

    // named rather than counted: a failure is nearly always one peer having
    // gone offline, and which one is the useful half
    toast.error(`Could not enqueue from ${failed.join(', ')}`);
  };

  return (
    <Segment
      className="flatlist-segment"
      raised
    >
      <div className="flatlist-summary">
        <span>{describeSelection({ selection, total: rows.length })}</span>
        {selection.count > 0 && (
          <Button
            basic
            compact
            onClick={() => setSelected(new Set())}
            size="tiny"
          >
            Clear selection
          </Button>
        )}
        {/*
         * A Popup rather than a Dropdown. Semantic's Dropdown closes from a
         * *native* document listener, which a React `stopPropagation` cannot
         * reach -- measured, the menu shut on the first tick and swallowed
         * the tick itself, so nothing changed either. Popup's portal ignores
         * clicks inside itself, which is what lets several columns be
         * switched in one visit.
         */}
        <Popup
          content={
            <div className="flatlist-columns-menu">
              {COLUMNS.map((col) => (
                <Checkbox
                  checked={show(col.key)}
                  key={col.key}
                  label={col.label}
                  onChange={() => setColumn(col.key, !show(col.key))}
                />
              ))}
            </div>
          }
          on="click"
          position="bottom right"
          trigger={
            <Button
              basic
              className="flatlist-columns"
              compact
              content="Columns"
              icon="columns"
              size="tiny"
            />
          }
        />
      </div>
      {/*
       * Into the footer, which is the one strip always on screen and has room
       * to spare. It is also a portal for a second reason: this component sits
       * inside Semantic's Sidebar.Pushable, which carries a transform -- an
       * identity one, but a transform all the same -- making it the containing
       * block for fixed descendants and the element that scrolls. Anything
       * pinned in place here travels with the content instead of staying put,
       * `fixed` and `sticky` alike.
       */}
      {selectedRows.length > 0 &&
        footerSlot &&
        createPortal(
          <Button
            color="green"
            compact
            content="Download"
            disabled={disabled || downloading}
            icon="download"
            label={{
              as: 'a',
              basic: false,
              content: `${selectedRows.length} file${selectedRows.length === 1 ? '' : 's'}, ${formatBytes(selectedSize)}`,
            }}
            labelPosition="right"
            loading={downloading}
            onClick={download}
            size="tiny"
          />,
          footerSlot,
        )}
      <div ref={listRef}>
        <Table
          className="flatlist"
          compact
          selectable
          size="small"
          // without this Semantic renders no sort arrow at all: its styles for
          // a sorted column live under `.ui.sortable.table`, so the `sorted`
          // prop below was setting a class nothing was listening to
          sortable
        >
          <Table.Header>
            <Table.Row>
              <Table.HeaderCell className="flatlist-selector">
                <Popup
                  content={
                    selection.all
                      ? 'Deselect every file'
                      : `Select all ${rows.length} file${rows.length === 1 ? '' : 's'}`
                  }
                  position="top left"
                  trigger={
                    <Checkbox
                      checked={selection.all}
                      disabled={disabled || rows.length === 0}
                      fitted
                      indeterminate={selection.some}
                      onChange={(_event, data) => toggleAll(data.checked)}
                    />
                  }
                />
              </Table.HeaderCell>
              {shown.map((col) => (
                <Table.HeaderCell
                  className={col.className}
                  key={col.key}
                  onClick={() => sortBy(col.key)}
                  // Semantic draws the arrow from this, and it doubles as the
                  // announcement to a screen reader
                  sorted={
                    column === col.key
                      ? direction === 'desc'
                        ? 'descending'
                        : 'ascending'
                      : undefined
                  }
                >
                  {col.label}
                </Table.HeaderCell>
              ))}
              <Table.HeaderCell className="flatlist-download" />
            </Table.Row>
          </Table.Header>
          <Table.Body>
            {paddingTop > 0 && (
              <Table.Row>
                <Table.Cell
                  colSpan={shown.length + 2}
                  style={{ height: paddingTop, padding: 0 }}
                />
              </Table.Row>
            )}
            {virtualRows.map((virtual) => {
              const row = rows[virtual.index];
              const mark = MARKS[downloadStateOf({ index: downloads, row })];

              return (
                <Table.Row
                  active={mark?.row === 'active'}
                  key={row.key}
                  positive={mark?.row === 'positive'}
                  warning={mark?.row === 'warning'}
                >
                  <Table.Cell className="flatlist-selector">
                    <Checkbox
                      checked={selected.has(row.key)}
                      disabled={disabled}
                      fitted
                      onChange={(_event, data) => toggle(row.key, data.checked)}
                    />
                  </Table.Cell>
                  {show('name') && (
                    <Table.Cell
                      className="flatlist-filename"
                      // the full remote path, which the column truncates and
                      // which is the only way to tell two files apart when their
                      // names differ only past where the column ends
                      title={row.filename}
                    >
                      {/*
                       * An inner element, and media-bridge injects its play
                       * button and quality badge into *this* rather than into
                       * the cell. The layout has to be flex so the filename is
                       * the only thing that shrinks -- and a `<td>` that is not
                       * `table-cell` leaves the table's column layout, taking
                       * the column's width with it. So the cell stays a cell
                       * and this does the arranging.
                       */}
                      <div className="flatlist-cell">
                        {row.locked && <Icon name="lock" />}
                        {mark && (
                          <Popup
                            content={mark.tip}
                            position="top left"
                            trigger={
                              <Icon
                                color={mark.colour}
                                name={mark.icon}
                              />
                            }
                          />
                        )}
                        <span className="flatlist-name">
                          {getFileName(row.filename)}
                        </span>
                      </div>
                    </Table.Cell>
                  )}
                  {show('path') && (
                    <Table.Cell
                      className="flatlist-path"
                      // the filename too, since the column stops at the folder
                      // and truncates even that on a narrow window
                      title={row.filename}
                    >
                      {pathOf(row)}
                    </Table.Cell>
                  )}
                  {show('user') && (
                    <Table.Cell className="flatlist-user">
                      <Popup
                        content={`Upload speed ${formatBytes(row.uploadSpeed)}/s · Free upload slot ${row.hasFreeUploadSlot ? 'YES' : 'NO'} · Queue length ${row.queueLength}`}
                        position="top left"
                        trigger={
                          <span>
                            <Icon
                              color={row.hasFreeUploadSlot ? 'green' : 'yellow'}
                              name="circle"
                              size="small"
                            />
                            {row.username}
                          </span>
                        }
                      />
                    </Table.Cell>
                  )}
                  {show('size') && (
                    <Table.Cell className="flatlist-size">
                      {formatBytes(row.size)}
                    </Table.Cell>
                  )}
                  {show('attributes') && (
                    <Table.Cell className="flatlist-attributes">
                      {formatAttributes(row)}
                    </Table.Cell>
                  )}
                  {show('length') && (
                    <Table.Cell className="flatlist-length">
                      {formatSeconds(row.length)}
                    </Table.Cell>
                  )}
                  {show('speed') && (
                    <Table.Cell className="flatlist-speed">
                      {row.uploadSpeed === undefined
                        ? ''
                        : `${formatBytes(row.uploadSpeed)}/s`}
                    </Table.Cell>
                  )}
                  {show('slot') && (
                    <Table.Cell className="flatlist-slot">
                      <Icon
                        color={row.hasFreeUploadSlot ? 'green' : 'yellow'}
                        name={row.hasFreeUploadSlot ? 'check' : 'clock outline'}
                      />
                    </Table.Cell>
                  )}
                  {show('queue') && (
                    <Table.Cell className="flatlist-queue">
                      {row.queueLength}
                    </Table.Cell>
                  )}
                  <Table.Cell className="flatlist-download">
                    <Popup
                      content={`Download this file from ${row.username}`}
                      position="left center"
                      trigger={
                        <Icon
                          color="grey"
                          disabled={disabled || rowDownloading === row.key}
                          link
                          loading={rowDownloading === row.key}
                          name={
                            rowDownloading === row.key ? 'spinner' : 'download'
                          }
                          onClick={() => downloadRow(row)}
                          size="small"
                        />
                      }
                    />
                  </Table.Cell>
                </Table.Row>
              );
            })}
            {paddingBottom > 0 && (
              <Table.Row>
                <Table.Cell
                  colSpan={shown.length + 2}
                  style={{ height: paddingBottom, padding: 0 }}
                />
              </Table.Row>
            )}
          </Table.Body>
        </Table>
      </div>
    </Segment>
  );
};

export default FlatFileList;
