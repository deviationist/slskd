import { groupByUser } from '../../lib/searches';
import * as transfers from '../../lib/transfers';
import {
  formatAttributes,
  formatBytes,
  formatSeconds,
  getFileName,
} from '../../lib/util';
import React, { useMemo, useState } from 'react';
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
 * How many rows are drawn before the first "show more".
 *
 * Higher than the grouped view's five, which counts *users*: a search of any
 * size is hundreds of files, and a list that shows five of them is not a list.
 * Low enough that a thousand checkboxes are not mounted on arrival.
 */
const PAGE = 100;

/**
 * The results as one row per file, rather than one card per user.
 *
 * The grouped view answers "what does this user have"; this one answers
 * "what is here", which is the question a sort by size or bitrate is asking.
 * Both are kept -- this is a second way to read the same filtered results, not
 * a replacement -- and the toggle between them is remembered.
 *
 * Selection lives here as a set of keys rather than as a `selected` flag
 * written onto each file, which is what the grouped view does. A flat list is
 * rebuilt whenever a filter changes, so a flag written onto a row would be
 * lost with it; a key survives, because it names the file rather than the row.
 * @param {object} params
 * @param {boolean} params.disabled - Whether the search is in a state that forbids downloading.
 * @param {Function} params.onHideUser - Hides every result from one peer.
 * @param {object[]} params.rows - Flattened, already-filtered results.
 * @returns {object} The list.
 */
const FlatFileList = ({ disabled, onHideUser, rows }) => {
  const [selected, setSelected] = useState(() => new Set());
  const [shown, setShown] = useState(PAGE);
  const [downloading, setDownloading] = useState(false);

  const visible = useMemo(() => rows.slice(0, shown), [rows, shown]);

  // a selection outlives the filter that was in force when it was made, so a
  // row can be selected and then filtered away. counting from `rows` rather
  // than from the set is what stops the button offering to download a file
  // that is no longer on the page
  const selectedRows = useMemo(
    () => rows.filter((row) => selected.has(row.key)),
    [rows, selected],
  );

  const selectedSize = selectedRows.reduce((total, row) => total + row.size, 0);
  const allVisibleSelected =
    visible.length > 0 && visible.every((row) => selected.has(row.key));

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

  const toggleAllVisible = (checked) =>
    setSelected((old) => {
      const next = new Set(old);

      for (const row of visible) {
        if (checked) {
          next.add(row.key);
        } else {
          next.delete(row.key);
        }
      }

      return next;
    });

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
      <Table
        className="flatlist"
        compact
        selectable
        size="small"
      >
        <Table.Header>
          <Table.Row>
            <Table.HeaderCell className="flatlist-selector">
              <Checkbox
                checked={allVisibleSelected}
                disabled={disabled || visible.length === 0}
                fitted
                onChange={(_event, data) => toggleAllVisible(data.checked)}
              />
            </Table.HeaderCell>
            <Table.HeaderCell className="flatlist-filename">
              File
            </Table.HeaderCell>
            <Table.HeaderCell className="flatlist-user">User</Table.HeaderCell>
            <Table.HeaderCell className="flatlist-size">Size</Table.HeaderCell>
            <Table.HeaderCell className="flatlist-attributes">
              Attributes
            </Table.HeaderCell>
            <Table.HeaderCell className="flatlist-length">
              Length
            </Table.HeaderCell>
            <Table.HeaderCell className="flatlist-hide" />
          </Table.Row>
        </Table.Header>
        <Table.Body>
          {visible.map((row) => (
            <Table.Row key={row.key}>
              <Table.Cell className="flatlist-selector">
                <Checkbox
                  checked={selected.has(row.key)}
                  disabled={disabled}
                  fitted
                  onChange={(_event, data) => toggle(row.key, data.checked)}
                />
              </Table.Cell>
              <Table.Cell
                className="flatlist-filename"
                // the full remote path, which the column has no room for and
                // which is the only way to tell two identically named files apart
                title={row.filename}
              >
                {row.locked && <Icon name="lock" />}
                {getFileName(row.filename)}
              </Table.Cell>
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
              <Table.Cell className="flatlist-size">
                {formatBytes(row.size)}
              </Table.Cell>
              <Table.Cell className="flatlist-attributes">
                {formatAttributes(row)}
              </Table.Cell>
              <Table.Cell className="flatlist-length">
                {formatSeconds(row.length)}
              </Table.Cell>
              <Table.Cell className="flatlist-hide">
                <Popup
                  content={`Hide every result from ${row.username}. They come back when the search is reloaded or run again -- nothing is remembered.`}
                  position="left center"
                  trigger={
                    <Icon
                      color="red"
                      link
                      name="close"
                      onClick={() => onHideUser(row.username)}
                      size="small"
                    />
                  }
                />
              </Table.Cell>
            </Table.Row>
          ))}
        </Table.Body>
      </Table>
      {rows.length > shown && (
        <Button
          className="showmore-button"
          fluid
          onClick={() => setShown(shown + PAGE)}
          primary
          size="large"
        >
          {`Show ${Math.min(PAGE, rows.length - shown)} more (${rows.length - shown} remaining)`}
        </Button>
      )}
      {selectedRows.length > 0 && (
        <div className="flatlist-actions">
          <Button
            color="green"
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
          />
        </div>
      )}
    </Segment>
  );
};

export default FlatFileList;
