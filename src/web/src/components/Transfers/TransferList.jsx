import * as transfers from '../../lib/transfers';
import { formatBytes, formatBytesAsUnit, getFileName } from '../../lib/util';
import TransferDetails from './TransferDetails';
import React, { Component } from 'react';
import { toast } from 'react-toastify';
import {
  Button,
  Checkbox,
  Header,
  Icon,
  List,
  Popup,
  Progress,
  Table,
} from 'semantic-ui-react';

/* Distance the popup keeps from the edge of the window, in px. */
const VIEWPORT_MARGIN = 8;

/* Semantic UI React turns Popper's preventOverflow modifier on only when an
   `offset` prop is given, so by default a popup is drawn wherever its
   placement puts it -- including off the edge of the window, where the part
   that did not fit is simply not reachable. This one is a nineteen-row table
   anchored to a row that can sit anywhere in a long list, so it overflows
   often.

   Turning preventOverflow back on lets it slide along both axes into the space
   that exists, and the fallback placements let it move to another side of the
   icon entirely when neither left nor right has room. `tether: false` is what
   allows the slide to detach the popup from the icon; without it the popup
   stays glued to the row and overflows anyway.

   Only base placements are listed: Semantic maps `left-start` and its
   siblings to no class at all, which would leave the popup without an arrow.
   Sliding costs the arrow its alignment with the icon -- Semantic draws it as
   a :before at a fixed spot rather than through Popper's arrow modifier --
   which is the lesser of the two problems. */
const detailsPopperModifiers = [
  {
    name: 'flip',
    options: {
      fallbackPlacements: ['right', 'top', 'bottom'],
      padding: VIEWPORT_MARGIN,
    },
  },
  {
    enabled: true,
    name: 'preventOverflow',
    options: {
      altAxis: true,
      padding: VIEWPORT_MARGIN,
      tether: false,
    },
  },
];

const getColor = (state) => {
  switch (state) {
    case 'InProgress':
      return { color: 'blue' };
    case 'Completed, Succeeded':
      return { color: 'green' };
    case 'Requested':
    case 'Queued, Locally':
    case 'Queued, Remotely':
    case 'Queued':
      return {};
    case 'Initializing':
      return { color: 'teal' };
    default:
      return { color: 'red' };
  }
};

const isRetryableState = (state) => getColor(state).color === 'red';
const isQueuedState = (state) => state.includes('Queued');

/* Whether this row has a file the server can hand back.
 *
 * Three things have to hold, and the last is the one that is easy to forget:
 * `localFilename` is null for downloads that finished before this application
 * began recording where it wrote them, and the server answers 404 for those.
 * A button that is always refused is worse than no button. */
const isRetrievable = (file) =>
  file.direction === 'Download' &&
  file.state === 'Completed, Succeeded' &&
  Boolean(file.localFilename);

const formatBytesTransferred = ({ size, transferred }) => {
  const [s, sExtension] = formatBytes(size, 1).split(' ');
  const t = formatBytesAsUnit(transferred, sExtension, 1);

  return `${t}/${s} ${sExtension}`;
};

class TransferList extends Component {
  constructor(props) {
    super(props);

    this.state = {
      isFolded: false,
      retrieving: null,
      // ids whose file the server has already said is gone. something downstream
      // moves finished files out of the downloads directory, so a download whose
      // file has left is the normal end of its life rather than an error worth
      // retrying -- and a button that has been refused once should stop offering
      // itself
      unavailable: new Set(),
    };
  }

  handleRetrieve = async (file) => {
    const { username } = this.props;

    try {
      this.setState({ retrieving: file.id });

      await transfers.retrieveFile({
        filename: getFileName(file.filename),
        id: file.id,
        username,
      });
    } catch (error) {
      console.error(error);
      toast.error(transfers.describeRetrievalError(error));

      if (transfers.isRetrievalPermanentlyGone(error)) {
        this.setState((previousState) => ({
          unavailable: new Set(previousState.unavailable).add(file.id),
        }));
      }
    } finally {
      this.setState({ retrieving: null });
    }
  };

  handleClick = (file) => {
    const { direction, state } = file;

    if (direction === 'Download') {
      if (isRetryableState(state)) {
        return this.props.onRetryRequested(file);
      }

      if (isQueuedState(state)) {
        return this.props.onPlaceInQueueRequested(file);
      }
    }

    return undefined;
  };

  toggleFolded = () => {
    this.setState((previousState) => ({ isFolded: !previousState.isFolded }));
  };

  render() {
    const { directoryName, files, onSelectionChange, retrievalEnabled } =
      this.props;
    const { isFolded, retrieving, unavailable } = this.state;

    return (
      <div>
        <Header
          className="filelist-header"
          size="small"
        >
          <Icon
            link
            name={isFolded ? 'folder' : 'folder open'}
            onClick={() => this.toggleFolded()}
          />
          {directoryName}
        </Header>
        {isFolded === false ? (
          <List>
            <List.Item>
              <Table>
                <Table.Header>
                  <Table.Row>
                    <Table.HeaderCell className="transferlist-selector">
                      <Checkbox
                        checked={files.filter((f) => !f.selected).length === 0}
                        fitted
                        onChange={(event, data) =>
                          files.map((file) =>
                            onSelectionChange(
                              directoryName,
                              file,
                              data.checked,
                            ),
                          )
                        }
                      />
                    </Table.HeaderCell>
                    <Table.HeaderCell className="transferlist-filename">
                      File
                    </Table.HeaderCell>
                    <Table.HeaderCell className="transferlist-progress">
                      Progress
                    </Table.HeaderCell>
                    <Table.HeaderCell className="transferlist-size">
                      Size
                    </Table.HeaderCell>
                    {retrievalEnabled && (
                      <Table.HeaderCell className="transferlist-retrieve" />
                    )}
                    <Table.HeaderCell className="transferlist-detail">
                      <Icon
                        name="info circle"
                        size="small"
                      />
                    </Table.HeaderCell>
                  </Table.Row>
                </Table.Header>
                <Table.Body>
                  {files
                    .sort((a, b) =>
                      getFileName(a.filename).localeCompare(
                        getFileName(b.filename),
                      ),
                    )
                    .map((f) => (
                      <Table.Row key={f.filename}>
                        <Table.Cell className="transferlist-selector">
                          <Checkbox
                            checked={f.selected}
                            fitted
                            onChange={(event, data) =>
                              onSelectionChange(directoryName, f, data.checked)
                            }
                          />
                        </Table.Cell>
                        <Table.Cell className="transferlist-filename">
                          {getFileName(f.filename)}
                        </Table.Cell>
                        <Table.Cell className="transferlist-progress">
                          {f.state === 'InProgress' ? (
                            <Progress
                              color={getColor(f.state).color}
                              percent={Math.round(f.percentComplete)}
                              progress
                              style={{ margin: 0 }}
                            />
                          ) : (
                            <Button
                              fluid
                              size="mini"
                              style={{
                                cursor: f.direction === 'Upload' ? 'unset' : '',
                                margin: 0,
                                padding: 7,
                              }}
                              {...getColor(f.state)}
                              {...(!getColor(f.state).color && f.attempts > 1
                                ? { color: 'yellow' }
                                : {})}
                              active={f.direction === 'Upload'}
                              onClick={() => this.handleClick(f)}
                            >
                              {f.direction === 'Download' &&
                                isQueuedState(f.state) && (
                                  <Icon name="refresh" />
                                )}
                              {f.direction === 'Download' &&
                                isRetryableState(f.state) && (
                                  <Icon name="redo" />
                                )}
                              {f.state}
                              {f.placeInQueue ? ` (#${f.placeInQueue})` : ''}
                              {f.attempts > 1 ? ` (Retry #${f.attempts})` : ''}
                            </Button>
                          )}
                        </Table.Cell>
                        <Table.Cell className="transferlist-size">
                          <div
                            style={{
                              alignItems: 'center',
                              display: 'flex',
                              justifyContent: 'space-between',
                            }}
                          >
                            <span>
                              {formatBytesTransferred({
                                size: f.size,
                                transferred: f.bytesTransferred,
                              })}
                            </span>
                          </div>
                        </Table.Cell>
                        {retrievalEnabled && (
                          <Table.Cell className="transferlist-retrieve">
                            {isRetrievable(f) &&
                              (unavailable.has(f.id) ? (
                                <Popup
                                  content="This file is no longer on disk"
                                  position="left center"
                                  trigger={
                                    <Icon
                                      disabled
                                      name="download"
                                      size="small"
                                    />
                                  }
                                />
                              ) : (
                                <Popup
                                  content={transfers.describeRetrieval()}
                                  position="left center"
                                  trigger={
                                    <Icon
                                      color="grey"
                                      disabled={retrieving === f.id}
                                      link
                                      loading={retrieving === f.id}
                                      name={
                                        retrieving === f.id
                                          ? 'spinner'
                                          : 'download'
                                      }
                                      onClick={() => this.handleRetrieve(f)}
                                      size="small"
                                    />
                                  }
                                />
                              ))}
                          </Table.Cell>
                        )}
                        <Table.Cell className="transferlist-detail">
                          <Popup
                            className="transfer-details-popup"
                            content={<TransferDetails file={f} />}
                            on="click"
                            popperModifiers={detailsPopperModifiers}
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
                        </Table.Cell>
                      </Table.Row>
                    ))}
                </Table.Body>
              </Table>
            </List.Item>
          </List>
        ) : (
          ''
        )}
      </div>
    );
  }
}

export default TransferList;
