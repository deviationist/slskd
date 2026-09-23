import { urlBase } from '../../config';
import {
  describeEmpty,
  describeSelection,
  nextSort,
  parseColumns,
  selectionState,
  selectRange,
  SORT_HINT,
  sortFromQuery,
  sortRows,
  sortStateOf,
  sortToQuery,
} from '../../lib/tables';
import * as transfersLibrary from '../../lib/transfers';
import { userPath } from '../../lib/users';
import {
  formatBytes,
  formatDate,
  formatDuration,
  formatWhen,
  getFileExtension,
  getFileName,
  offsetWithin,
  scrollParentOf,
} from '../../lib/util';
import { ColumnPicker, EmptyTableRow, SortHint, SortRank } from '../Shared';
import TransferDetails from './TransferDetails';
import {
  ConfirmRemovalModal,
  getColor,
  isQueuedState,
  isRetryableState,
} from './TransferList';
import { useVirtualizer } from '@tanstack/react-virtual';
import React, { useLayoutEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Link, useHistory, useLocation } from 'react-router-dom';
import { toast } from 'react-toastify';
import {
  Button,
  Checkbox,
  Icon,
  Popup,
  Progress,
  Segment,
  Table,
} from 'semantic-ui-react';

/**
 * The height of a row, in px.
 *
 * Must match `.flatlist tbody td` in App.css, as on the searches page: the
 * virtualiser positions rows by arithmetic rather than by measuring them, so a
 * row taller than this puts every row below it in the wrong place.
 */
const ROW_H = 37;

/*
 * Which columns this browser shows, and which way it sorts, per direction.
 *
 * Downloads and uploads are different jobs -- one is a queue you are waiting
 * on, the other a queue someone else is -- so a choice made about one says
 * nothing about the other. The existing sort preference is already stored per
 * direction for the same reason.
 */
const columnsKey = (direction) => `slskd-transfers-columns-${direction}`;

const readStoredColumns = (direction) => {
  try {
    return parseColumns({
      all: transfersLibrary.TRANSFER_COLUMNS,
      stored: window.localStorage.getItem(columnsKey(direction)),
    });
  } catch {
    return parseColumns({
      all: transfersLibrary.TRANSFER_COLUMNS,
      stored: null,
    });
  }
};

const storeColumns = (direction, columns) => {
  try {
    window.localStorage.setItem(columnsKey(direction), columns.join(','));
  } catch {
    // a preference that cannot be saved is still a preference for this tab
  }
};

/**
 * When a download was asked for, or finished.
 *
 * Blank rather than guessed: `timingOf` refuses an instant it cannot believe,
 * and says why in its own comment. The full date is the tooltip, since the
 * cell drops the year and, for anything today, the date.
 * @param {object} params
 * @param {number|null} params.at - The instant, in ms.
 * @param {number} params.now - The present, in ms.
 * @returns {object} The cell.
 */
const WhenCell = ({ at, now }) => (
  <Table.Cell
    className="flatlist-when"
    title={at === null ? undefined : formatDate(at)}
  >
    {at !== null && (
      <time dateTime={new Date(at).toISOString()}>{formatWhen(at, now)}</time>
    )}
  </Table.Cell>
);

const STILL_GOING = {
  took: 'Still receiving — this is how long so far',
  waited: 'Still waiting — this is how long so far',
};

/**
 * How long a download waited, or how long it took -- set apart while the
 * number is still growing, so three minutes *so far* does not read the same as
 * three minutes and done.
 * @param {object} params
 * @param {string} params.column - 'waited' or 'took'.
 * @param {boolean} params.live - Whether the duration is still growing.
 * @param {number|null} params.seconds - The duration.
 * @returns {object} The cell.
 */
const DurationCell = ({ column, live, seconds }) => (
  <Table.Cell
    className={live ? 'flatlist-duration flatlist-live' : 'flatlist-duration'}
    title={live ? STILL_GOING[column] : undefined}
  >
    {seconds === null ? '' : formatDuration(seconds)}
  </Table.Cell>
);

/**
 * The speed column's text: blank where `speedOf` refuses the number.
 * @param {object} row - The transfer.
 * @returns {string} The speed, formatted, or ''.
 */
const speedText = (row) => {
  const speed = transfersLibrary.speedOf(row);

  return speed === null ? '' : `${formatBytes(speed)}/s`;
};

const TIMING_COLUMNS = new Set(['finished', 'requested', 'took', 'waited']);

/**
 * The cell for one of the four timing columns.
 * @param {object} params
 * @param {string} params.key - The column.
 * @param {number} params.now - The present, in ms, shared by the whole render.
 * @param {object} params.row - The transfer.
 * @returns {object} The cell.
 */
const timingCell = ({ key, now, row }) => {
  const timing = transfersLibrary.timingOf(row, now);

  if (key === 'requested' || key === 'finished') {
    return (
      <WhenCell
        at={timing[key]}
        key={key}
        now={now}
      />
    );
  }

  return (
    <DurationCell
      column={key}
      key={key}
      live={timing[`${key}Live`]}
      seconds={timing[key]}
    />
  );
};

/**
 * What a selection can be told to do, and only what it can.
 *
 * Its own component because the list around it was at the linter's complexity
 * ceiling, and because these four conditions are the whole of it -- everything
 * here is one question asked four ways.
 * @param {object} params
 * @param {Function} params.onCancelAll - Cancels the selection.
 * @param {Function} params.onRemoveAll - Removes the selection.
 * @param {Function} params.onRetryAll - Retries the selection.
 * @param {boolean} params.retrievalEnabled - Whether files can be fetched to the browser.
 * @param {object[]} params.rows - The selected rows.
 * @returns {object} The bar, or nothing when nothing is selected.
 */
const SelectionActions = ({
  onCancelAll,
  onRemoveAll,
  onRetryAll,
  retrievalEnabled,
  rows,
}) => {
  const [archiving, setArchiving] = useState(false);
  /*
   * Which actions a selection actually offers, weighed exactly as the card
   * view weighs them -- the flat list showing three buttons that the cards
   * would have hidden is the two views disagreeing about the same selection.
   *
   * Retry and Remove want *every* row to qualify; Cancel wants any. Retrieval
   * is offered as soon as something in the selection has a file, since the
   * rest is not a reason to withhold the ones that do.
   */
  const allRetryable =
    rows.length > 0 &&
    rows.every((f) => transfersLibrary.isStateRetryable(f.state));
  const anyCancellable = rows.some((f) =>
    transfersLibrary.isStateCancellable(f.state),
  );
  const allRemovable =
    rows.length > 0 &&
    rows.every((f) => transfersLibrary.isStateRemovable(f.state));
  const retrievable = rows.filter((f) => transfersLibrary.isRetrievable(f));

  /*
   * One archive per peer. The card's version cannot need this -- a card is one
   * peer -- but a selection here crosses them, and the retrieval endpoint
   * takes a username and its own ids.
   */
  const archive = async () => {
    setArchiving(true);

    const byUser = new Map();

    for (const row of retrievable) {
      byUser.set(row.username, [...(byUser.get(row.username) ?? []), row.id]);
    }

    for (const [username, ids] of byUser) {
      try {
        // eslint-disable-next-line no-await-in-loop
        await transfersLibrary.retrieveArchive({ ids, username });
      } catch (error) {
        console.error(error);
        toast.error(transfersLibrary.describeArchiveError(error));
      }
    }

    setArchiving(false);
  };

  return (
    <div className="flatlist-bulk">
      <span>{`${rows.length} selected`}</span>
      {/*
       * A Button.Group with the same dividers the card view uses, not
       * loose buttons. Three reasons, and the third is the one that
       * settles it: the actions read as one control rather than three;
       * the two views look like the same page; and the ingress button
       * media-bridge injects looks for a `.ui.buttons` to append itself
       * to, with an `.or` before it.
       */}
      <Button.Group size="tiny">
        {allRetryable && (
          <Button
            color="green"
            content="Retry"
            icon="redo"
            onClick={() => onRetryAll(rows)}
          />
        )}
        {allRetryable && anyCancellable && <Button.Or />}
        {anyCancellable && (
          <Button
            color="red"
            content="Cancel"
            icon="x"
            onClick={() => onCancelAll(rows)}
          />
        )}
        {(allRetryable || anyCancellable) && allRemovable && <Button.Or />}
        {allRemovable && (
          <Button
            color="red"
            content="Remove"
            icon="trash alternate"
            onClick={() => onRemoveAll(rows)}
          />
        )}
        {(allRetryable || anyCancellable || allRemovable) &&
          retrievalEnabled &&
          retrievable.length > 0 && <Button.Or />}
        {/*
         * Blue and busy-aware, as the card view's is. The same action looking
         * like a different one is what makes two views feel like two
         * products.
         */}
        {retrievalEnabled && retrievable.length > 0 && (
          <Popup
            content={transfersLibrary.describeRetrieval(retrievable.length)}
            position="top right"
            trigger={
              <Button
                color="blue"
                content="Download"
                disabled={archiving}
                icon="download"
                loading={archiving}
                onClick={archive}
              />
            }
          />
        )}
      </Button.Group>
    </div>
  );
};

/**
 * The transfers as one row per file, rather than one card per peer.
 *
 * The card view answers "what is this peer sending me"; this one answers "what
 * is in the queue", which is the question a sort by size or state is asking.
 * Both are kept, and the choice is remembered.
 *
 * Every row is drawn -- only the ones on screen are in the DOM, the rest being
 * two spacer rows holding the scrollbar at the right length. Virtualised
 * against the element that actually scrolls rather than the window: this app
 * puts `overflow-y: auto` on a wrapper near the root, so `window.scrollY`
 * never moves however far down the list you are.
 * @param {object} params
 * @param {boolean} params.deleteFileOnRemoval - Whether a removal takes the file with it.
 * @param {string} params.direction - 'download' or 'upload'.
 * @param {string} params.filterQuery - The filter in force, for an emptied table to quote.
 * @param {Function} params.onCancelAll - Cancels a selection.
 * @param {Function} params.onRemoveAll - Removes a selection.
 * @param {Function} params.onRetryAll - Retries a selection.
 * @param {Function} params.onPlaceInQueueRequested - Asks a peer where we are in its queue.
 * @param {Function} params.onRemoveRequested - Removes one transfer.
 * @param {Function} params.onRetryRequested - Retries one transfer.
 * @param {boolean} params.retrievalEnabled - Whether a finished file can be fetched to the browser.
 * @param {number} params.total - How many transfers there are before the filter.
 * @param {object[]} params.users - The transfers, grouped as the API returns them.
 * @returns {object} The list.
 */
const FlatTransferList = ({
  deleteFileOnRemoval,
  direction,
  filterQuery,
  onCancelAll,
  onPlaceInQueueRequested,
  onRemoveAll,
  onRemoveRequested,
  onRetryAll,
  onRetryRequested,
  retrievalEnabled,
  total,
  users,
}) => {
  const [selected, setSelected] = useState(() => new Set());

  // the last row ticked without shift; where a shift-click measures from
  const [anchor, setAnchor] = useState(undefined);
  const [columns, setColumns] = useState(() => readStoredColumns(direction));
  const [confirming, setConfirming] = useState(null);
  const [removing, setRemoving] = useState(null);
  const [retrieving, setRetrieving] = useState(null);
  const [unavailable, setUnavailable] = useState(() => new Set());
  const [footerSlot, setFooterSlot] = useState(null);
  const [scroller, setScroller] = useState(null);
  const [scrollMargin, setScrollMargin] = useState(0);
  const listRef = useRef(null);

  const location = useLocation();
  const history = useHistory();
  // memoised on the query string rather than rebuilt per render: it is the
  // dependency the sorted rows are memoised on, and a fresh array every render
  // would sort the whole list every render
  const sort = useMemo(
    () =>
      sortFromQuery(location.search, transfersLibrary.TRANSFER_SORT_COLUMNS),
    [location.search],
  );

  const rows = useMemo(
    () =>
      sortRows({
        columns: transfersLibrary.TRANSFER_SORT_COLUMNS,
        rows: transfersLibrary.flattenTransfers(users),
        sort,
      }),
    [sort, users],
  );

  // in the order they are shown, not the library's: the order is a choice now,
  // and rebuilding the list from the canonical one would quietly undo it
  const shown = columns
    .map((key) =>
      transfersLibrary.TRANSFER_COLUMNS.find((col) => col.key === key),
    )
    .filter(Boolean);

  const applyColumns = (next) => {
    setColumns(next);
    storeColumns(direction, next);
  };

  const sortBy = (key, append) => {
    const next = nextSort({ append, column: key, sort });

    history.replace({
      pathname: location.pathname,
      search: sortToQuery({ search: location.search, sort: next }),
    });
  };

  // see FlatFileList: the ref is null on the first render, so reading it there
  // fixes both values at their defaults for the life of the component
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
        (virtualRows.at(-1).end - virtualizer.options.scrollMargin)
      : 0;

  const selectedRows = useMemo(
    () => rows.filter((row) => selected.has(row.key)),
    [rows, selected],
  );
  const selection = selectionState({ rows, selected });

  /*
   * A click ticks one box; a shift-click takes everything between it and the
   * last box clicked without shift, as a file manager does.
   *
   * The anchor stays where it was through a run of shift-clicks, so a range
   * can be widened or narrowed after the fact rather than restarting from the
   * last click. `selectRange` is where the rest of it lives, with tests.
   */
  const toggle = (key, checked, event) => {
    if (event?.shiftKey && anchor !== undefined && anchor !== key) {
      setSelected((old) =>
        selectRange({ anchor, checked, key, rows, selected: old }),
      );

      return;
    }

    setAnchor(key);
    setSelected((old) => {
      const next = new Set(old);

      if (checked) {
        next.add(key);
      } else {
        next.delete(key);
      }

      return next;
    });
  };

  const retrieve = async (row) => {
    try {
      setRetrieving(row.id);
      await transfersLibrary.retrieveFile({
        filename: getFileName(row.filename),
        id: row.id,
        username: row.username,
      });
    } catch (error) {
      console.error(error);
      toast.error(transfersLibrary.describeRetrievalError(error));

      if (transfersLibrary.isRetrievalPermanentlyGone(error)) {
        setUnavailable((old) => new Set(old).add(row.id));
      }
    } finally {
      setRetrieving(null);
    }
  };

  const remove = async (row) => {
    try {
      setConfirming(null);
      setRemoving(row.id);
      await onRemoveRequested(row);
    } finally {
      setRemoving(null);
    }
  };

  // the same decision the card view makes, from the same place: whether a row
  // offers a removal at all, and whether it has to ask first
  const askThenRemove = (row) => {
    const plan = transfersLibrary.planRowRemoval({
      deleteFileOnRemoval,
      file: row,
    });

    if (plan.confirm) {
      setConfirming(row);
      return;
    }

    remove(row);
  };

  const act = (row) => {
    if (row.direction !== 'Download') {
      return undefined;
    }

    if (isRetryableState(row.state)) {
      return onRetryRequested(row);
    }

    if (isQueuedState(row.state)) {
      return onPlaceInQueueRequested(row);
    }

    return undefined;
  };

  // one present for the whole render, so every live duration on screen is
  // measured to the same instant rather than to whenever its cell was reached
  const now = Date.now();

  const cell = (row, key) => {
    if (TIMING_COLUMNS.has(key)) {
      return timingCell({ key, now, row });
    }

    switch (key) {
      case 'name': {
        return (
          <Table.Cell
            className="flatlist-filename"
            key={key}
            title={row.filename}
          >
            <div className="flatlist-cell">
              <span className="flatlist-name">{getFileName(row.filename)}</span>
            </div>
          </Table.Cell>
        );
      }

      case 'ext': {
        return (
          <Table.Cell
            className="flatlist-ext"
            key={key}
          >
            {getFileExtension(row.filename)}
          </Table.Cell>
        );
      }

      case 'path': {
        return (
          <Table.Cell
            className="flatlist-path"
            key={key}
            title={row.filename}
          >
            {row.directory}
          </Table.Cell>
        );
      }

      case 'user': {
        return (
          <Table.Cell
            className="flatlist-user"
            key={key}
          >
            <Link
              title={`Look up ${row.username}`}
              to={userPath(row.username)}
            >
              {row.username}
            </Link>
          </Table.Cell>
        );
      }

      case 'search': {
        /*
         * The search this download was started from, and a link to it while
         * it is still there. A batch records the *text* as well as the id, so
         * a download whose search has since been deleted or pruned still says
         * what was searched for -- it simply stops being a link. Which is the
         * distinction worth drawing: an empty cell then means "this did not
         * come from a search", rather than meaning both that and "it did, and
         * the search is gone".
         */
        return (
          <Table.Cell
            className="flatlist-search"
            key={key}
            title={
              row.searchText
                ? row.searchId
                  ? `Found by searching for '${row.searchText}' — click to open that search`
                  : `Found by searching for '${row.searchText}' — that search has since been deleted, so there is nothing to open`
                : undefined
            }
          >
            {row.searchId && row.searchText ? (
              <Link to={`${urlBase}/searches/${row.searchId}`}>
                {row.searchText}
              </Link>
            ) : (
              row.searchText
            )}
          </Table.Cell>
        );
      }

      case 'state': {
        return (
          <Table.Cell
            className="flatlist-progress"
            key={key}
          >
            {row.state === 'InProgress' ? (
              <Progress
                color={getColor(row.state).color}
                percent={Math.round(row.percentComplete)}
                progress
                style={{ margin: 0 }}
              />
            ) : (
              <Button
                fluid
                size="mini"
                style={{
                  cursor: row.direction === 'Upload' ? 'unset' : '',
                  margin: 0,
                  padding: 5,
                }}
                {...getColor(row.state)}
                {...(!getColor(row.state).color && row.attempts > 1
                  ? { color: 'yellow' }
                  : {})}
                active={row.direction === 'Upload'}
                onClick={() => act(row)}
              >
                {row.state}
                {row.placeInQueue ? ` (#${row.placeInQueue})` : ''}
              </Button>
            )}
          </Table.Cell>
        );
      }

      case 'size': {
        return (
          <Table.Cell
            className="flatlist-size"
            key={key}
          >
            {formatBytes(row.size)}
          </Table.Cell>
        );
      }

      case 'speed': {
        return (
          <Table.Cell
            className="flatlist-speed"
            key={key}
          >
            {speedText(row)}
          </Table.Cell>
        );
      }

      case 'attempts': {
        return (
          <Table.Cell
            className="flatlist-attempts"
            key={key}
          >
            {row.attempts}
          </Table.Cell>
        );
      }

      default: {
        return null;
      }
    }
  };

  const removalPlan = confirming
    ? transfersLibrary.planRowRemoval({ deleteFileOnRemoval, file: confirming })
    : null;

  return (
    <Segment
      className="flatlist-segment"
      raised
    >
      <div className="flatlist-summary">
        <span>
          {describeSelection({
            noun: 'transfer',
            selection,
            total: rows.length,
          })}
        </span>
        <SortHint />
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
        <Popup
          content={
            <ColumnPicker
              all={transfersLibrary.TRANSFER_COLUMNS}
              columns={columns}
              onChange={applyColumns}
            />
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
       * Into the footer's slot, as the search page's download is. The list is
       * every transfer now, so the end of it is a long way from the row just
       * ticked -- and this component sits inside Semantic's Sidebar.Pushable,
       * which carries a transform, so anything pinned in place here travels
       * with the scrolled content whether it is `fixed` or `sticky`.
       */}
      {selectedRows.length > 0 &&
        footerSlot &&
        createPortal(
          <SelectionActions
            onCancelAll={onCancelAll}
            onRemoveAll={onRemoveAll}
            onRetryAll={onRetryAll}
            retrievalEnabled={retrievalEnabled}
            rows={selectedRows}
          />,
          footerSlot,
        )}
      {/*
        The table scrolls sideways on a phone rather than being cut off. See
        `.flatlist-scroll`: the overflow is on this element, which is the one
        `scrollParentOf` starts *above*, so it cannot be mistaken for the
        element that scrolls the page.
      */}
      <div
        className="flatlist-scroll"
        ref={listRef}
      >
        <Table
          className="flatlist"
          compact
          selectable
          size="small"
          // without this Semantic renders no sort arrow at all: its styles for
          // a sorted column live under `.ui.sortable.table`, so the `sorted`
          // prop below was setting a class nothing was listening to
          sortable
          /*
           * Semantic stacks a table into blocks below 768px -- every cell
           * `display: block; width: 100%` -- which is a reasonable default for
           * a table of prose and ruinous for this one: the rows are positioned
           * by arithmetic on a fixed height, so a row that becomes seven
           * stacked blocks puts every row below it in the wrong place.
           * Measured at 600px, each header cell was 538px wide inside a 540px
           * table while File and Path were 19px.
           */
          unstackable
        >
          <Table.Header>
            <Table.Row>
              <Table.HeaderCell className="flatlist-selector">
                <Checkbox
                  checked={selection.all}
                  disabled={rows.length === 0}
                  fitted
                  indeterminate={selection.some}
                  onChange={(_event, data) =>
                    setSelected(
                      data.checked
                        ? new Set(rows.map((r) => r.key))
                        : new Set(),
                    )
                  }
                />
              </Table.HeaderCell>
              {shown.map((col) => (
                <Table.HeaderCell
                  className={`${col.className} flatlist-sortable`}
                  key={col.key}
                  onClick={(event) => sortBy(col.key, event.shiftKey)}
                  sorted={sortStateOf({ column: col.key, sort }).sorted}
                  title={SORT_HINT}
                >
                  {col.label}
                  <SortRank
                    rank={sortStateOf({ column: col.key, sort }).rank}
                  />
                </Table.HeaderCell>
              ))}
              <Table.HeaderCell className="flatlist-actions" />
            </Table.Row>
          </Table.Header>
          <Table.Body>
            {rows.length === 0 && (
              <EmptyTableRow columns={shown.length + 2}>
                {describeEmpty({
                  noun: `${direction}s`,
                  query: filterQuery,
                  total,
                })}
              </EmptyTableRow>
            )}
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
              const plan = transfersLibrary.planRowRemoval({
                deleteFileOnRemoval,
                file: row,
              });

              return (
                <Table.Row
                  /*
                   * The row's identity, for the scripts media-bridge injects
                   * into this page. They used to read the peer and the folder
                   * out of the cells, which stopped being safe the moment a
                   * column could be hidden -- hide User and the play button
                   * attaches to a file it cannot name.
                   */
                  data-filename={row.filename}
                  /*
                   * The state too, for the same reason: media-bridge decides
                   * from it whether a row can be sent into the ingress chain,
                   * and reading it from the Progress cell would make that
                   * depend on a column the operator can switch off.
                   */
                  data-state={row.state}
                  data-username={row.username}
                  key={row.key}
                >
                  <Table.Cell className="flatlist-selector">
                    <Checkbox
                      checked={selected.has(row.key)}
                      fitted
                      onChange={(event, data) =>
                        toggle(row.key, data.checked, event)
                      }
                    />
                  </Table.Cell>
                  {shown.map((col) => cell(row, col.key))}
                  <Table.Cell className="flatlist-actions">
                    <div className="flatlist-actions-row">
                      {retrievalEnabled && (
                        <>
                          {transfersLibrary.isFinishedDownload(row) &&
                            (!transfersLibrary.isRetrievable(row) ||
                            transfersLibrary.isFileGone({
                              file: row,
                              refused: unavailable,
                            }) ? (
                              <Popup
                                content={transfersLibrary.describeUnretrievable(
                                  {
                                    file: row,
                                    gone: transfersLibrary.isFileGone({
                                      file: row,
                                      refused: unavailable,
                                    }),
                                  },
                                )}
                                position="left center"
                                trigger={
                                  <span className="transferlist-retrieve-struck">
                                    <Icon
                                      disabled
                                      name="download"
                                      size="small"
                                    />
                                    <Icon
                                      className="transferlist-retrieve-strike"
                                      color="grey"
                                      name="ban"
                                      size="small"
                                    />
                                  </span>
                                }
                              />
                            ) : (
                              <Popup
                                content={transfersLibrary.describeRetrieval()}
                                position="left center"
                                trigger={
                                  <Icon
                                    color="grey"
                                    disabled={retrieving === row.id}
                                    link
                                    loading={retrieving === row.id}
                                    name={
                                      retrieving === row.id
                                        ? 'spinner'
                                        : 'download'
                                    }
                                    onClick={() => retrieve(row)}
                                    size="small"
                                  />
                                }
                              />
                            ))}
                        </>
                      )}
                      {plan.offered && (
                        <Popup
                          content={plan.tooltip}
                          position="left center"
                          trigger={
                            <Icon
                              color="grey"
                              disabled={removing === row.id}
                              link
                              loading={removing === row.id}
                              name={
                                removing === row.id
                                  ? 'spinner'
                                  : 'trash alternate'
                              }
                              onClick={() => askThenRemove(row)}
                              size="small"
                            />
                          }
                        />
                      )}
                      <Popup
                        className="transfer-details-popup"
                        content={<TransferDetails file={row} />}
                        on="click"
                        position="left center"
                        trigger={
                          <Icon
                            color="grey"
                            link
                            name="info circle"
                            size="small"
                          />
                        }
                        wide="very"
                      />
                    </div>
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
      {confirming && (
        <ConfirmRemovalModal
          busy={removing === confirming.id}
          onCancel={() => setConfirming(null)}
          onConfirm={() => remove(confirming)}
          plan={removalPlan}
        />
      )}
    </Segment>
  );
};

export default FlatTransferList;
