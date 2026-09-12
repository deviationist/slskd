import * as transfers from '../../lib/transfers';
import { getFileName } from '../../lib/util';
import TransferList, { ConfirmRemovalModal } from './TransferList';
import React, { Component } from 'react';
import { toast } from 'react-toastify';
import {
  Button,
  Card,
  Header,
  Icon,
  List,
  Modal,
  Popup,
} from 'semantic-ui-react';

/**
 * Says which of the selected files are not there, and offers to archive the
 * rest.
 *
 * Shown only when something is missing. An archive of everything that was
 * picked needs no confirmation -- there is nothing to tell the operator, and a
 * dialog that only ever says "yes, all of it is here" trains them to dismiss it
 * without reading. Where *nothing* is left there is nothing to continue with,
 * so the dialog says so and offers only the way out.
 */
const MissingFilesModal = ({
  available,
  busy,
  missing,
  onCancel,
  onConfirm,
}) => (
  <Modal
    actions={[
      available.length > 0 ? 'Cancel' : 'Close',
      ...(available.length > 0
        ? [
            {
              content: `Download the other ${available.length}`,
              key: 'continue',
              loading: busy,
              onClick: onConfirm,
              positive: true,
            },
          ]
        : []),
    ]}
    centered
    content={
      <Modal.Content>
        <p>
          {available.length > 0
            ? `${missing.length} of the ${missing.length + available.length} selected files are no longer on disk and cannot be included:`
            : 'None of the selected files are still on disk:'}
        </p>
        <List bulleted>
          {missing.map((m) => (
            <List.Item key={m.id}>{m.filename}</List.Item>
          ))}
        </List>
      </Modal.Content>
    }
    header={
      <Header
        content="Some files are missing"
        icon="exclamation triangle"
      />
    }
    onClose={onCancel}
    open
    size="small"
  />
);

class TransferGroup extends Component {
  constructor(props) {
    super(props);

    this.state = {
      // the selection waiting on a confirmation, with what it would delete.
      // undefined means nothing is pending
      confirmingSelection: undefined,
      isFolded: false,
      selections: new Set(),
      // what the pre-flight found, once it has found something worth asking
      // about. null means nothing is pending -- the modal is open exactly when
      // this is not null
      archive: null,
      archiveBusy: false,
    };
  }

  /**
   * Archives the selected files, asking first if any of them have gone.
   *
   * The check comes first because the archive is *streamed*: once it has begun
   * there is no longer any way to say that half of what was picked was not
   * there. Where everything is present there is nothing to ask about, and it
   * simply starts.
   */
  handleArchive = async (username, selected) => {
    const choice = transfers.chooseRetrieval(selected);

    if (choice.mode === 'none') {
      return;
    }

    try {
      this.setState({ archiveBusy: true });

      // the single-file path says the right thing when its file has gone, so it
      // needs no availability check of its own
      if (choice.mode === 'file') {
        await this.retrieveOne(username, choice.file);
        return;
      }

      const { available, missing } = await transfers.archiveAvailability({
        ids: choice.files.map((f) => f.id),
        username,
      });

      if (missing.length === 0) {
        await transfers.retrieveArchive({
          ids: available.map((a) => a.id),
          username,
        });
        return;
      }

      this.setState({ archive: { available, missing, username } });
    } catch (error) {
      console.error(error);
      toast.error(transfers.describeArchiveError(error));
    } finally {
      this.setState({ archiveBusy: false });
    }
  };

  handleArchiveConfirmed = async () => {
    const { available, username } = this.state.archive;

    try {
      this.setState({ archiveBusy: true });

      // the same rule after the modal as before it: dropping the missing ones
      // can leave a single file, and that is a file rather than an archive of one
      if (available.length === 1) {
        await this.retrieveOne(username, available[0]);
        this.setState({ archive: null });
        return;
      }

      await transfers.retrieveArchive({
        ids: available.map((a) => a.id),
        username,
      });

      this.setState({ archive: null });
    } catch (error) {
      console.error(error);
      toast.error(transfers.describeArchiveError(error));
    } finally {
      this.setState({ archiveBusy: false });
    }
  };

  /**
   * Fetches one file, by the same route its own row's button uses.
   *
   * Errors are described with the single-file vocabulary rather than the
   * archive's, because that is what the operator is actually getting.
   */
  retrieveOne = async (username, file) => {
    try {
      await transfers.retrieveFile({
        filename: getFileName(file.filename),
        id: file.id,
        username,
      });
    } catch (error) {
      console.error(error);
      toast.error(transfers.describeRetrievalError(error));
    }
  };

  handleSelectionChange = (directoryName, file, selected) => {
    const { selections } = this.state;
    const object = JSON.stringify({
      directory: directoryName,
      filename: file.filename,
    });

    if (selected) {
      selections.add(object);
    } else {
      selections.delete(object);
    }

    this.setState({ selections });
  };

  isSelected = (directoryName, file) =>
    this.state.selections.has(
      JSON.stringify({ directory: directoryName, filename: file.filename }),
    );

  getSelectedFiles = () => {
    const { user } = this.props;

    return Array.from(this.state.selections)
      .map((s) => JSON.parse(s))
      .map((s) =>
        user.directories
          .find((d) => d.directory === s.directory)
          .files.find((f) => f.filename === s.filename),
      )
      .filter((s) => s !== undefined);
  };

  removeFileSelection = (file) => {
    const { selections } = this.state;

    const match = Array.from(selections)
      .map((s) => JSON.parse(s))
      .find((s) => s.filename === file.filename);

    if (match) {
      selections.delete(JSON.stringify(match));
      this.setState({ selections });
    }
  };

  retryAll = async (selected) => {
    await Promise.all(selected.map((file) => this.handleRetry(file)));
  };

  cancelAll = async (direction, username, selected) => {
    await Promise.all(
      selected.map((file) =>
        transfers.cancel({ direction, id: file.id, username }),
      ),
    );
  };

  /**
   * Removes the selected transfers.
   *
   * Whether the files go with them is not this button's decision: the server
   * takes its files with a removal or it does not, according to
   * `transfers.download.delete_file_on_removal`, and answers with what it did.
   * A removal that deleted nothing answers 204 and there is nothing to report;
   * one that deleted something answers with the outcome per file.
   */
  removeAll = async (direction, username, selected) => {
    const results = await Promise.all(
      selected.map((file) =>
        transfers
          .cancel({ direction, id: file.id, remove: true, username })
          .then((response) => {
            this.removeFileSelection(file);
            return { data: response?.data, ok: true };
          })
          // one file's failure must not abandon the rest of the batch, and it
          // has to be reported rather than logged into the void
          .catch((error) => ({ error, ok: false })),
      ),
    );

    const summary = transfers.summariseDeletions(results);

    if (summary) {
      toast[summary.kind](summary.message);
    }
  };

  /**
   * Removes one transfer, at the request of its own row.
   *
   * Deliberately `removeAll` with a selection of one rather than a second
   * removal path: the row gets the same request, the same clearing of any
   * selection it was part of, and the same summary of what the server did with
   * the file. Two ways of removing a transfer that reported differently would
   * be worse than one.
   */
  handleRemove = async (file) => {
    const { direction, user } = this.props;

    await this.removeAll(direction, user.username, [file]);
  };

  /**
   * Asks before removing a selection, where the removal would delete files.
   *
   * The same rule the row applies, and for the stronger reason: a selection is
   * the more destructive of the two paths, since it can take a folder's worth
   * at once. Where nothing would be deleted it does not ask -- a dialog over a
   * harmless action is one that gets dismissed unread, which is how the one
   * that matters gets clicked through.
   */
  confirmRemoveAll = (selected) => {
    const { deleteFileOnRemoval, direction, user } = this.props;
    const plan = transfers.planSelectionRemoval({
      deleteFileOnRemoval,
      files: selected,
    });

    if (!plan.confirm) {
      this.removeAll(direction, user.username, selected);
      return;
    }

    this.setState({ confirmingSelection: { plan, selected } });
  };

  handleRetry = async (file) => {
    const { filename, size, username } = file;

    try {
      await transfers.download({ files: [{ filename, size }], username });
    } catch (error) {
      console.error(error);
    }
  };

  handleFetchPlaceInQueue = async (file) => {
    const { id, username } = file;

    try {
      await transfers.getPlaceInQueue({ id, username });
    } catch (error) {
      console.error(error);
    }
  };

  toggleFolded = () => {
    this.setState((previousState) => ({ isFolded: !previousState.isFolded }));
  };

  /**
   * The actions offered for whatever is currently selected.
   *
   * Its own method rather than part of `render`: every button here is
   * conditional on the states in the selection, and the whole lot in one
   * function is more branching than is worth reading in one go.
   */
  renderSelectionActions(selected) {
    const { direction, retrievalEnabled, user } = this.props;
    const { archiveBusy } = this.state;

    const all = selected.length > 1 ? ' Selected' : '';

    const allRetryable =
      selected.filter((f) => transfers.isStateRetryable(f.state)).length ===
      selected.length;
    const anyCancellable = selected.some((f) =>
      transfers.isStateCancellable(f.state),
    );
    const allRemovable =
      selected.filter((f) => transfers.isStateRemovable(f.state)).length ===
      selected.length;
    // offered as soon as *something* in the selection has a file. the rest of
    // the selection is not a reason to withhold the ones that do, and the
    // pre-flight is what reports the difference
    const anyRetrievable =
      retrievalEnabled && selected.some((f) => transfers.isRetrievable(f));

    // what a retrieval would actually take, which is not every selected row:
    // the tooltip has to promise the number of files the operator will get
    const retrievableCount = selected.filter((f) =>
      transfers.isRetrievable(f),
    ).length;

    return (
      <Card.Content extra>
        <Button.Group>
          {allRetryable && (
            <Button
              color="green"
              content={`Retry${all}`}
              icon="redo"
              onClick={() => this.retryAll(selected)}
            />
          )}
          {allRetryable && anyCancellable && <Button.Or />}
          {anyCancellable && (
            <Button
              color="red"
              content={`Cancel${all}`}
              icon="x"
              onClick={() => this.cancelAll(direction, user.username, selected)}
            />
          )}
          {(allRetryable || anyCancellable) && allRemovable && <Button.Or />}
          {allRemovable && (
            <Button
              content={`Remove${all}`}
              icon="trash alternate"
              onClick={() => this.confirmRemoveAll(selected)}
            />
          )}
          {(allRetryable || anyCancellable || allRemovable) &&
            anyRetrievable && <Button.Or />}
          {anyRetrievable && (
            <Popup
              content={transfers.describeRetrieval(retrievableCount)}
              position="top center"
              trigger={
                <Button
                  color="blue"
                  content={`Download${all}`}
                  disabled={archiveBusy}
                  icon="download"
                  loading={archiveBusy}
                  onClick={() => this.handleArchive(user.username, selected)}
                />
              }
            />
          )}
        </Button.Group>
      </Card.Content>
    );
  }

  render() {
    const { user } = this.props;
    const { archive, archiveBusy, confirmingSelection, isFolded } = this.state;

    const selected = this.getSelectedFiles();

    return (
      <Card
        className="transfer-card"
        key={user.username}
        raised
      >
        {confirmingSelection && (
          <ConfirmRemovalModal
            busy={false}
            onCancel={() => this.setState({ confirmingSelection: undefined })}
            onConfirm={() => {
              const { selected: files } = confirmingSelection;

              this.setState({ confirmingSelection: undefined });
              this.removeAll(this.props.direction, user.username, files);
            }}
            plan={confirmingSelection.plan}
          />
        )}
        <Card.Content>
          <Card.Header>
            <Icon
              link
              name={isFolded ? 'chevron right' : 'chevron down'}
              onClick={() => this.toggleFolded()}
            />
            {user.username}
          </Card.Header>
          {user.directories &&
            !isFolded &&
            user.directories.map((directory) => (
              <TransferList
                deleteFileOnRemoval={this.props.deleteFileOnRemoval}
                direction={this.props.direction}
                directoryName={directory.directory}
                files={(directory.files || []).map((f) => ({
                  ...f,
                  selected: this.isSelected(directory.directory, f),
                }))}
                key={directory.directory}
                onPlaceInQueueRequested={this.handleFetchPlaceInQueue}
                onRemoveRequested={this.handleRemove}
                onRetryRequested={this.handleRetry}
                onSelectionChange={this.handleSelectionChange}
                retrievalEnabled={this.props.retrievalEnabled}
                username={user.username}
              />
            ))}
        </Card.Content>
        {selected.length > 0 && this.renderSelectionActions(selected)}
        {archive && (
          <MissingFilesModal
            available={archive.available}
            busy={archiveBusy}
            missing={archive.missing}
            onCancel={() => this.setState({ archive: null })}
            onConfirm={this.handleArchiveConfirmed}
          />
        )}
      </Card>
    );
  }
}

export default TransferGroup;
