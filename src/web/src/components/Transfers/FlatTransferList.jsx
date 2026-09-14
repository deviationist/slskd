import {
  describeSelection,
  nextSort,
  parseColumns,
  selectionState,
  sortFromQuery,
  sortRows,
  sortToQuery,
  withColumn,
} from '../../lib/tables';
import * as transfersLibrary from '../../lib/transfers';
import {
  formatBytes,
  getFileName,
  offsetWithin,
  scrollParentOf,
} from '../../lib/util';
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
import { useHistory, useLocation } from 'react-router-dom';
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
 * @param {Function} params.onCancelAll - Cancels a selection.
 * @param {Function} params.onRemoveAll - Removes a selection.
 * @param {Function} params.onRetryAll - Retries a selection.
 * @param {Function} params.onPlaceInQueueRequested - Asks a peer where we are in its queue.
 * @param {Function} params.onRemoveRequested - Removes one transfer.
 * @param {Function} params.onRetryRequested - Retries one transfer.
 * @param {boolean} params.retrievalEnabled - Whether a finished file can be fetched to the browser.
 * @param {object[]} params.users - The transfers, grouped as the API returns them.
 * @returns {object} The list.
 */
const FlatTransferList = ({
  deleteFileOnRemoval,
  direction,
  onCancelAll,
  onPlaceInQueueRequested,
  onRemoveAll,
  onRemoveRequested,
  onRetryAll,
  onRetryRequested,
  retrievalEnabled,
  users,
}) => {
  const [selected, setSelected] = useState(() => new Set());
  const [columns, setColumns] = useState(() => readStoredColumns(direction));
  const [confirming, setConfirming] = useState(null);
  const [confirmingSelection, setConfirmingSelection] = useState(null);
  const [removing, setRemoving] = useState(null);
  const [retrieving, setRetrieving] = useState(null);
  const [unavailable, setUnavailable] = useState(() => new Set());
  const [footerSlot, setFooterSlot] = useState(null);
  const [scroller, setScroller] = useState(null);
  const [scrollMargin, setScrollMargin] = useState(0);
  const listRef = useRef(null);

  const location = useLocation();
  const history = useHistory();
  const { column, direction: order } = sortFromQuery(
    location.search,
    transfersLibrary.TRANSFER_SORT_COLUMNS,
  );

  const rows = useMemo(
    () =>
      sortRows({
        column,
        columns: transfersLibrary.TRANSFER_SORT_COLUMNS,
        direction: order,
        rows: transfersLibrary.flattenTransfers(users),
      }),
    [column, order, users],
  );

  const shown = transfersLibrary.TRANSFER_COLUMNS.filter((col) =>
    columns.includes(col.key),
  );
  const show = (key) => columns.includes(key);

  const setColumn = (key, on) => {
    const next = withColumn({
      all: transfersLibrary.TRANSFER_COLUMNS,
      columns,
      key,
      on,
    });

    setColumns(next);
    storeColumns(direction, next);
  };

  const sortBy = (key) => {
    const next = nextSort({ column: key, current: column, direction: order });

    history.replace({
      pathname: location.pathname,
      search: sortToQuery({ ...next, search: location.search }),
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

  /*
   * The same question the card view asks over a selection, from the same
   * place. It is the more destructive of the two paths -- a selection can
   * carry a folder's worth of files where a row carries one -- so the table
   * view having a dialog for the row and none for the selection had it
   * exactly the wrong way round.
   *
   * Where nothing would be deleted it does not ask. A dialog raised over a
   * harmless action is one that gets dismissed unread, which is how the one
   * that matters gets clicked through.
   */
  const askThenRemoveAll = (rowsToRemove) => {
    const plan = transfersLibrary.planSelectionRemoval({
      deleteFileOnRemoval,
      files: rowsToRemove,
    });

    if (plan.confirm) {
      setConfirmingSelection({ plan, rows: rowsToRemove });
      return;
    }

    onRemoveAll(rowsToRemove);
  };

  const removeAllConfirmed = async () => {
    const pending = confirmingSelection;

    setConfirmingSelection(null);
    setRemoving('selection');

    try {
      await onRemoveAll(pending.rows);
    } finally {
      setRemoving(null);
    }
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

  const cell = (row, key) => {
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
            {row.username}
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
            {row.averageSpeed ? `${formatBytes(row.averageSpeed)}/s` : ''}
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
            <div className="flatlist-columns-menu">
              {transfersLibrary.TRANSFER_COLUMNS.map((col) => (
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
            onRemoveAll={askThenRemoveAll}
            onRetryAll={onRetryAll}
            retrievalEnabled={retrievalEnabled}
            rows={selectedRows}
          />,
          footerSlot,
        )}
      <div ref={listRef}>
        <Table
          className="flatlist"
          compact
          selectable
          size="small"
          sortable
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
                  onClick={() => sortBy(col.key)}
                  sorted={
                    column === col.key
                      ? order === 'desc'
                        ? 'descending'
                        : 'ascending'
                      : undefined
                  }
                >
                  {col.label}
                </Table.HeaderCell>
              ))}
              <Table.HeaderCell className="flatlist-actions" />
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
                      onChange={(_event, data) => toggle(row.key, data.checked)}
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
      {confirmingSelection && (
        <ConfirmRemovalModal
          busy={removing === 'selection'}
          onCancel={() => setConfirmingSelection(null)}
          onConfirm={removeAllConfirmed}
          plan={confirmingSelection.plan}
        />
      )}
    </Segment>
  );
};

export default FlatTransferList;
