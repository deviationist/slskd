import * as library from '../../../lib/watches';
import WatchModal from '../WatchModal';
import React, { useEffect, useState } from 'react';
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

const when = (iso) => (iso ? new Date(iso).toLocaleString() : '-');

/**
 * The watch on a search: what it does, when it next runs, and what it has sent.
 *
 * Shown only where there is one. A search that is not watched says nothing
 * here; the control for creating one lives on the searches page, beside the
 * search box, because a watch is created with the search rather than after it.
 */
const WatchPanel = ({ searchId, searchText }) => {
  const [watch, setWatch] = useState(undefined);
  const [notifications, setNotifications] = useState([]);
  const [runs, setRuns] = useState([]);
  const [editing, setEditing] = useState(false);
  const [showingLog, setShowingLog] = useState(false);
  const [working, setWorking] = useState(false);

  const load = async () => {
    try {
      const found = await library.get({ id: searchId });

      setWatch(found);
      setNotifications(await library.getNotifications({ id: searchId }));
      setRuns(await library.getRuns({ id: searchId }));
    } catch {
      // a 404 is the ordinary case: most searches are not watched
      setWatch(undefined);
    }
  };

  useEffect(() => {
    load();
  }, [searchId]); // eslint-disable-line react-hooks/exhaustive-deps

  const act = async (action, message) => {
    setWorking(true);

    try {
      await action();
      await load();

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

  if (!watch) {
    return null;
  }

  const badge = library.watchBadge({ notifications, watch });
  const lastRun = runs[0];

  return (
    <Segment
      className="search-detail-header-segment"
      raised
    >
      <div>
        <Label
          color={badge.color}
          horizontal
        >
          <Icon name={badge.icon} />
          {badge.label}
        </Label>
        <span style={{ marginLeft: '0.5em' }}>
          {library.describeRecurrence(watch.rrule)} ({watch.timeZone})
          {' — next run '}
          {library.describeNextRun({ watch })}
        </span>
        {lastRun && (
          <span style={{ marginLeft: '0.5em', opacity: 0.7 }}>
            {`· last run ${when(lastRun.startedAt)}, ${lastRun.newCount} new`}
          </span>
        )}
      </div>
      <div style={{ marginTop: '0.75em' }}>
        <Button.Group size="small">
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
            content={`Emails (${notifications.length})`}
            disabled={working}
            icon="mail"
            onClick={() => setShowingLog(true)}
          />
          <Button
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
                  <Table.HeaderCell>When</Table.HeaderCell>
                  <Table.HeaderCell>To</Table.HeaderCell>
                  <Table.HeaderCell>Files</Table.HeaderCell>
                  <Table.HeaderCell>Result</Table.HeaderCell>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {notifications.map((notification) => (
                  <Table.Row key={notification.id}>
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
                ))}
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
