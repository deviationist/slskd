import { urlBase } from '../../config';
import * as shareLinks from '../../lib/shareLinks';
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
  formatDuration,
  formatSeconds,
  getFileExtension,
  getFileName,
  offsetWithin,
  scrollParentOf,
} from '../../lib/util';
import CompareButton from '../Compare/CompareButton';
import { RowBadge, RowPlay } from '../Player/Parts';
import {
  ColumnPicker,
  EmptyTableRow,
  MeasuredRow,
  SortHint,
  SortRank,
  Timestamp,
} from '../Shared';
import TransferDetails from './TransferDetails';
import {
  ConfirmRemovalModal,
  getColor,
  isQueuedState,
  isRetryableState,
} from './TransferList';
import withDividers from './withDividers';
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
 * Must match `.flatlist tbody td` in App.css, as on the searches page. Rows are
 * measured, but this is the estimate every row starts from, and one that is
 * wrong makes the list shift under the pointer as rows are measured.
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
 * and says why in its own comment. `Timestamp` draws the rest, as it draws
 * every date in the UI.
 * @param {object} params
 * @param {number|null} params.at - The instant, in ms.
 * @param {number} params.now - The present, in ms.
 * @returns {object} The cell.
 */
const WhenCell = ({ at, now }) => (
  <Table.Cell className="flatlist-when">
    <Timestamp
      at={at}
      now={now}
    />
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

/*
 * The columns whose cell is nothing but a line of text, and what the text is.
 * Out of the switch in `cell` because each case there was the same eleven
 * lines with one expression changed, and the switch was at the linter's
 * complexity ceiling with nothing complicated in it.
 */
const TEXT_COLUMNS = {
  attempts: (row) => row.attempts,
  ext: (row) => getFileExtension(row.filename),

  // null until the server has read the file, and for one that is not audio --
  // formatSeconds would draw either as 00:00
  length: (row) =>
    Number.isFinite(row.length) ? formatSeconds(row.length) : '',
  size: (row) => formatBytes(row.size),
  speed: speedText,
};

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
 * @param {Function} [params.onCompareRequested] - Opens the compare dialog; absent where Compare is off.
 * @param {Function} params.onRemoveAll - Removes the selection.
 * @param {Function} params.onRetryAll - Retries the selection.
 * @param {Function} [params.onShareRequested] - Opens the share dialog; absent where links cannot be made.
 * @param {boolean} params.retrievalEnabled - Whether files can be fetched to the browser.
 * @param {object[]} params.rows - The selected rows.
 * @returns {object} The bar, or nothing when nothing is selected.
 */
const SelectionActions = ({
  onCancelAll,
  onCompareRequested,
  onRemoveAll,
  onRetryAll,
  onShareRequested,
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
  const shareable = onShareRequested ? rows.filter(shareLinks.isShareable) : [];

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
       * the two views look like the same page; and an injected
       * button can look for a `.ui.buttons` to append itself
       * to, with an `.or` before it.
       */}
      <Button.Group size="tiny">
        {withDividers([
          allRetryable && (
            <Button
              color="green"
              content="Retry"
              icon="redo"
              key="retry"
              onClick={() => onRetryAll(rows)}
            />
          ),
          anyCancellable && (
            <Button
              color="red"
              content="Cancel"
              icon="x"
              key="cancel"
              onClick={() => onCancelAll(rows)}
            />
          ),
          allRemovable && (
            <Button
              color="red"
              content="Remove"
              icon="trash alternate"
              key="remove"
              onClick={() => onRemoveAll(rows)}
            />
          ),
          // blue and busy-aware, as the card view's is. the same action looking
          // like a different one is what makes two views feel like two products
          retrievalEnabled && retrievable.length > 0 && (
            <Popup
              content={transfersLibrary.describeRetrieval(retrievable.length)}
              key="retrieve"
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
          ),
          shareable.length > 0 && (
            <Button
              color="teal"
              content="Share"
              icon="share alternate"
              key="share"
              onClick={() => onShareRequested(rows)}
            />
          ),
          // the selection here already crosses peers, so it is what is compared
          onCompareRequested && (
            <CompareButton
              key="compare"
              onCompareRequested={onCompareRequested}
              ticked={rows}
            />
          ),
        ])}
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
 * @param {Function} [params.onCompareRequested] - Opens the compare dialog for some rows; absent where Compare is off.
 * @param {Function} params.onPlaceInQueueRequested - Asks a peer where we are in its queue.
 * @param {Function} params.onRemoveRequested - Removes one transfer.
 * @param {Function} params.onRetryRequested - Retries one transfer.
 * @param {Function} [params.onShareRequested] - Opens the share dialog for some rows; absent where links cannot be made.
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
  onCompareRequested,
  onPlaceInQueueRequested,
  onRemoveAll,
  onRemoveRequested,
  onRetryAll,
  onRetryRequested,
  onShareRequested,
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

  /*
   * Rows are measured, not assumed. Every row of ours is ROW_H tall, so the
   * estimate is right and measuring moves nothing -- but an injected script may
   * open a panel *inside* a row, making that one row hundreds of pixels
   * taller. Positioned by arithmetic, the rows below it sat that far from where
   * the virtualiser thought, and the panel's row left the window early and came
   * back late. Keyed by the row rather than its index, so a measured height
   * follows its row through a re-sort instead of staying at the position.
   */
  const virtualizer = useVirtualizer({
    count: rows.length,
    estimateSize: () => ROW_H,
    getItemKey: (index) => rows[index].key,
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

  // the rows as shown, so a filter in force narrows a search's group with
  // everything else rather than reaching past it to rows nobody can see
  const searchGroups = useMemo(
    () => transfersLibrary.groupBySearch(rows),
    [rows],
  );

  /*
   * Selects everything that came from a row's search, in place of the
   * selection there was. The point is the cleanup after auditioning: of the
   * three candidates a search produced, the keeper is kept and the
   * rest go -- and they are scattered through the list by any sort that is not
   * on the Search column, which is most of them. Replacing rather than adding
   * is what makes the Remove that follows safe: a selection that silently kept
   * whatever was ticked before would delete that too.
   */
  const selectSearch = (keys) => {
    setAnchor(undefined);
    setSelected(new Set(keys));
  };

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

    if (Object.hasOwn(TEXT_COLUMNS, key)) {
      return (
        <Table.Cell
          className={`flatlist-${key}`}
          key={key}
        >
          {TEXT_COLUMNS[key](row)}
        </Table.Cell>
      );
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
              <RowPlay file={row} />
              <span className="flatlist-name">{getFileName(row.filename)}</span>
              <RowBadge file={row} />
            </div>
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
            onCompareRequested={onCompareRequested}
            onRemoveAll={onRemoveAll}
            onRetryAll={onRetryAll}
            onShareRequested={onShareRequested}
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
              const searchGroup = transfersLibrary.searchGroupOf(row);
              const fromSameSearch =
                searchGroup === null ? [] : searchGroups.get(searchGroup);

              return (
                <Table.Row
                  as={MeasuredRow}
                  /*
                   * The row's identity, for scripts injected into this
                   * page. They used to read the peer and the folder
                   * out of the cells, which stopped being safe the moment a
                   * column could be hidden -- hide User and the play button
                   * attaches to a file it cannot name.
                   */
                  data-filename={row.filename}
                  // and its id, which is what slskd's own player is asked for
                  // by (window.slskd.player)
                  data-id={row.id}
                  data-index={virtual.index}
                  /*
                   * The state too, for the same reason: an injected script can
                   * decide from it what a row offers,
                   * and reading it from the Progress cell would make that
                   * depend on a column the operator can switch off.
                   */
                  data-state={row.state}
                  data-username={row.username}
                  key={row.key}
                  measureRef={virtualizer.measureElement}
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
                      {/*
                       * Only where there is more than this row to select: a
                       * search that produced one download has nothing to
                       * gather, and the box beside the row already ticks it.
                       */}
                      {fromSameSearch.length > 1 && (
                        <Popup
                          content={transfersLibrary.describeSelectSearch({
                            count: fromSameSearch.length,
                            text: row.searchText,
                          })}
                          position="left center"
                          trigger={
                            <Icon
                              className="flatlist-select-search"
                              color="grey"
                              link
                              name="object group outline"
                              onClick={() => selectSearch(fromSameSearch)}
                              size="small"
                            />
                          }
                        />
                      )}
                      {onShareRequested && shareLinks.isShareable(row) && (
                        <Popup
                          content="Share: a link anyone can download this from, for a while"
                          position="left center"
                          trigger={
                            <Icon
                              color="grey"
                              link
                              name="share alternate"
                              onClick={() => onShareRequested([row])}
                              size="small"
                            />
                          }
                        />
                      )}
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
