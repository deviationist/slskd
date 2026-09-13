import { formatBytes, formatDate } from '../../../lib/util';
import * as library from '../../../lib/watches';
import WatchModal from '../WatchModal';
import React, { useEffect, useState } from 'react';
import { useHistory, useLocation } from 'react-router-dom';
import { toast } from 'react-toastify';
import {
  Button,
  Icon,
  Label,
  Modal,
  Popup,
  Segment,
  Table,
} from 'semantic-ui-react';

const when = (iso) => (iso ? formatDate(iso) : '-');

/**
 * The watch on a search: what it does, when it next runs, and what it has sent.
 *
 * Shown only where there is one. A search that is not watched says nothing
 * here; it is the header above that offers to start one, which is also why the
 * watch itself is owned by the page rather than by this panel -- both need the
 * same answer, and two fetches of it would disagree for as long as one is in
 * flight.
 */
const WatchPanel = ({ onWatchChanged, searchId, searchText, watch }) => {
  const [notifications, setNotifications] = useState([]);
  const [runs, setRuns] = useState([]);
  const [editing, setEditing] = useState(false);
  const [showingLog, setShowingLog] = useState(false);
  const [working, setWorking] = useState(false);
  const [opened, setOpened] = useState(undefined);
  const [ignores, setIgnores] = useState([]);
  const [showingIgnores, setShowingIgnores] = useState(false);

  // a link in a notification asks the page to offer this; the page asks before
  // it does anything, so following the link changes nothing by itself
  const location = useLocation();
  const history = useHistory();
  const asked = library.ignoreTargetFrom(location.search);

  // what the watch has done. asked for only where there is one: these
  // endpoints answer 404 for an unwatched search, and most searches are
  // unwatched, so asking anyway would make the ordinary case an error
  const loadLog = async () => {
    if (!watch) {
      setNotifications([]);
      setRuns([]);
      return;
    }

    try {
      setNotifications(await library.getNotifications({ id: searchId }));
      setRuns(await library.getRuns({ id: searchId }));
      setIgnores(await library.getIgnores());
    } catch {
      // the watch was deleted between these requests; the panel empties
    }
  };

  // reloads when a watch appears or disappears, not on every change to it: the
  // log is a consequence of the runs, and a rename does not move it
  useEffect(() => {
    loadLog();
  }, [searchId, Boolean(watch)]); // eslint-disable-line react-hooks/exhaustive-deps

  const act = async (action, message) => {
    setWorking(true);

    try {
      await action();
      await onWatchChanged();
      await loadLog();

      if (message) {
        toast.success(message);
      }
    } catch (error) {
      console.error(error);
      toast.error(error?.response?.data ?? error?.message ?? error);
    } finally {
      setWorking(false);
    }
  };

  const dismissAsked = () => history.replace(location.pathname);

  // the prompt is rendered whether or not there is still a watch here. a
  // notification outlives the watch that sent it, and an ignore is global --
  // so a link followed after the watch was deleted should still work, rather
  // than doing nothing with no explanation
  const askedPrompt = asked && (
    <Modal
      onClose={dismissAsked}
      open
      size="small"
    >
      <Modal.Header>
        <Icon name="ban" />
        Never report this again?
      </Modal.Header>
      <Modal.Content>
        <p>No watch will report a file with this name again, from any peer.</p>
        <p className="watch-panel-asked">{asked}</p>
        <p className="watch-panel-muted">
          Files already reported stay reported. This can be undone from Ignored
          on any watched search.
        </p>
      </Modal.Content>
      <Modal.Actions>
        <Button onClick={dismissAsked}>Cancel</Button>
        <Button
          negative
          onClick={async () => {
            await act(
              () => library.addIgnore({ kind: 'Name', value: asked }),
              'Ignored everywhere',
            );
            dismissAsked();
          }}
        >
          Ignore it
        </Button>
      </Modal.Actions>
    </Modal>
  );

  if (!watch) {
    return askedPrompt ?? null;
  }

  const badge = library.watchBadge({ notifications, watch });
  const lastRun = runs[0];
  const next = library.nextRun({ watch });

  return (
    <Segment
      className="watch-panel-segment"
      raised
    >
      {askedPrompt}
      <div className="watch-panel">
        <div className="watch-panel-summary">
          <div className="watch-panel-line">
            <Label
              color={badge.color}
              horizontal
            >
              <Icon name={badge.icon} />
              {badge.label}
            </Label>
            <span>
              {library.describeRecurrence(watch.rrule)}
              <span className="watch-panel-muted">{` · ${watch.timeZone}`}</span>
            </span>
          </div>
          <div className="watch-panel-line watch-panel-muted">
            <span>
              {'Next run '}
              {next.dateTime ? (
                // the relative phrasing is what is worth reading at a glance;
                // the moment it stands for is a hover away, and in the markup
                // for anything that reads the page rather than looks at it
                <time
                  dateTime={next.dateTime}
                  title={next.exact}
                >
                  {next.text}
                </time>
              ) : (
                next.text
              )}
            </span>
            {lastRun && (
              <span>
                {`Last run ${when(lastRun.startedAt)} · ${lastRun.newCount} new${
                  lastRun.enqueuedCount > 0
                    ? ` · ${lastRun.enqueuedCount} queued`
                    : ''
                }${
                  lastRun.ignoredCount > 0
                    ? ` · ${lastRun.ignoredCount} ignored`
                    : ''
                }`}
              </span>
            )}
          </div>
        </div>
        <Button.Group
          className="watch-panel-actions"
          size="small"
        >
          <Popup
            content={
              watch.enabled
                ? 'Stop running this search, but keep what it has reported'
                : 'Start running this search again'
            }
            trigger={
              <Button
                disabled={working}
                icon={watch.enabled ? 'pause' : 'play'}
                onClick={() =>
                  act(
                    () =>
                      library.put({
                        id: searchId,
                        watch: { ...watch, enabled: !watch.enabled },
                      }),
                    watch.enabled ? 'Watch paused' : 'Watch resumed',
                  )
                }
              />
            }
          />
          <Button
            content="Run now"
            disabled={working}
            icon="play circle"
            loading={working}
            onClick={() =>
              act(() => library.run({ id: searchId }), 'Watch run')
            }
          />
          <Button
            content="Edit"
            disabled={working}
            icon="pencil"
            onClick={() => setEditing(true)}
          />
          <Button
            content={`Ignored (${ignores.length})`}
            disabled={working}
            icon="ban"
            onClick={() => setShowingIgnores(true)}
          />
          <Button
            content={`Emails (${notifications.length})`}
            disabled={working}
            icon="mail"
            onClick={() => setShowingLog(true)}
          />
          <Button
            className="watch-panel-stop"
            content="Stop watching"
            disabled={working}
            icon="trash alternate"
            negative
            onClick={() =>
              act(
                () => library.remove({ id: searchId }),
                'No longer watching this search',
              )
            }
          />
        </Button.Group>
      </div>
      {editing && (
        <WatchModal
          existing={watch}
          onClose={() => setEditing(false)}
          onSave={async (updated) => {
            await act(
              () => library.put({ id: searchId, watch: updated }),
              'Watch saved',
            );
            setEditing(false);
          }}
          open
          searchText={searchText}
        />
      )}
      <Modal
        onClose={() => setShowingIgnores(false)}
        open={showingIgnores}
        size="small"
      >
        <Modal.Header>
          <Icon name="ban" />
          Never reported, by any watch
        </Modal.Header>
        <Modal.Content scrolling>
          {ignores.length === 0 ? (
            <p>
              Nothing is ignored. Use the ban icon beside a file in the email
              log to stop every watch reporting that name.
            </p>
          ) : (
            <Table size="small">
              <Table.Body>
                {ignores.map((ignore) => (
                  <Table.Row key={ignore.id}>
                    <Table.Cell>{library.describeIgnore(ignore)}</Table.Cell>
                    <Table.Cell collapsing>{when(ignore.createdAt)}</Table.Cell>
                    <Table.Cell collapsing>
                      <Popup
                        content="Stop ignoring this. A watch will report it again the next time it finds it."
                        position="left center"
                        trigger={
                          <Icon
                            link
                            name="undo"
                            onClick={() =>
                              act(
                                () => library.removeIgnore({ id: ignore.id }),
                                'No longer ignored',
                              )
                            }
                          />
                        }
                      />
                    </Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table>
          )}
        </Modal.Content>
        <Modal.Actions>
          <Button onClick={() => setShowingIgnores(false)}>Close</Button>
        </Modal.Actions>
      </Modal>
      <Modal
        onClose={() => setShowingLog(false)}
        open={showingLog}
        size="large"
      >
        <Modal.Header>
          <Icon name="mail" />
          Emails sent for this watch
        </Modal.Header>
        <Modal.Content scrolling>
          {notifications.length === 0 ? (
            <p>Nothing has been reported yet.</p>
          ) : (
            <Table size="small">
              <Table.Header>
                <Table.Row>
                  <Table.HeaderCell />
                  <Table.HeaderCell>When</Table.HeaderCell>
                  <Table.HeaderCell>To</Table.HeaderCell>
                  <Table.HeaderCell>Files</Table.HeaderCell>
                  <Table.HeaderCell>Result</Table.HeaderCell>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {notifications.map((notification) => {
                  const reported = library.filesFrom(notification);
                  const expanded = opened === notification.id;

                  return (
                    <React.Fragment key={notification.id}>
                      <Table.Row
                        onClick={() =>
                          setOpened(expanded ? undefined : notification.id)
                        }
                        style={{ cursor: 'pointer' }}
                      >
                        <Table.Cell collapsing>
                          <Icon
                            name={expanded ? 'chevron down' : 'chevron right'}
                          />
                        </Table.Cell>
                        <Table.Cell>{when(notification.sentAt)}</Table.Cell>
                        <Table.Cell>{notification.recipient}</Table.Cell>
                        <Table.Cell>{notification.fileCount}</Table.Cell>
                        <Table.Cell>
                          {notification.sent ? (
                            <span>
                              <Icon
                                color="green"
                                name="check"
                              />
                              {notification.adapter}
                            </span>
                          ) : (
                            <span>
                              <Icon
                                color="red"
                                name="exclamation triangle"
                              />
                              {notification.error}
                            </span>
                          )}
                        </Table.Cell>
                      </Table.Row>
                      {expanded && (
                        <Table.Row>
                          <Table.Cell colSpan="5">
                            {reported.files.length === 0 ? (
                              <em>
                                This message recorded no files. Older ones may
                                predate the list being kept.
                              </em>
                            ) : (
                              <>
                                {reported.truncated && (
                                  <div className="watch-panel-muted">
                                    {`Showing ${reported.shown} of ${reported.total}; the rest were reported but not recorded.`}
                                  </div>
                                )}
                                <Table
                                  basic="very"
                                  compact
                                  size="small"
                                >
                                  <Table.Body>
                                    {reported.files.map((file) => (
                                      <Table.Row
                                        key={`${file.username}/${file.filename}`}
                                      >
                                        <Table.Cell collapsing>
                                          {file.username}
                                        </Table.Cell>
                                        <Table.Cell>{file.filename}</Table.Cell>
                                        <Table.Cell collapsing>
                                          {formatBytes(file.size)}
                                        </Table.Cell>
                                        <Table.Cell collapsing>
                                          <Popup
                                            content="Never report a file with this name again, in any watch"
                                            position="left center"
                                            trigger={
                                              <Icon
                                                link
                                                name="ban"
                                                onClick={() =>
                                                  act(
                                                    () =>
                                                      library.addIgnore({
                                                        kind: 'Name',
                                                        value: file.filename,
                                                      }),
                                                    'Ignored everywhere',
                                                  )
                                                }
                                              />
                                            }
                                          />
                                        </Table.Cell>
                                      </Table.Row>
                                    ))}
                                  </Table.Body>
                                </Table>
                              </>
                            )}
                          </Table.Cell>
                        </Table.Row>
                      )}
                    </React.Fragment>
                  );
                })}
              </Table.Body>
            </Table>
          )}
        </Modal.Content>
        <Modal.Actions>
          <Button onClick={() => setShowingLog(false)}>Close</Button>
        </Modal.Actions>
      </Modal>
    </Segment>
  );
};

export default WatchPanel;
