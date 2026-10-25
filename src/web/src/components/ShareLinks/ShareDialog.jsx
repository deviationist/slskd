import './ShareLinks.css';
import * as shareLinks from '../../lib/shareLinks';
import { formatBytes } from '../../lib/util';
import CopyField from './CopyField';
import React, { useState } from 'react';
import {
  Button,
  Checkbox,
  Form,
  Icon,
  List,
  Message,
  Modal,
} from 'semantic-ui-react';

const MAXIMUM_LISTED = 5;

const getFileName = (path) => path.split(/[\\/]/u).pop();

/**
 * What became of mailing the link, one line per address.
 */
const MailResults = ({ results }) => (
  <List>
    {results.map(({ error, recipient, sent }) => (
      <List.Item key={recipient}>
        <Icon
          color={sent ? 'green' : 'red'}
          name={sent ? 'check' : 'x'}
        />
        <List.Content>
          {recipient}
          {!sent && <List.Description>{error}</List.Description>}
        </List.Content>
      </List.Item>
    ))}
  </List>
);

/**
 * The files about to be shared: the first few by name, the rest counted.
 */
const Selection = ({ files, skipped }) => {
  const size = files.reduce((total, file) => total + file.size, 0);
  const listed = files.slice(0, MAXIMUM_LISTED);

  return (
    <>
      <p>
        {files.length === 1 ? '1 file' : `${files.length} files`},{' '}
        {formatBytes(size)}
        {files.length > 1 && ', downloadable one by one or as a zip'}
      </p>
      <List
        bulleted
        className="share-dialog-files"
      >
        {listed.map((file) => (
          <List.Item key={file.id}>{getFileName(file.filename)}</List.Item>
        ))}
        {files.length > listed.length && (
          <List.Item>and {files.length - listed.length} more</List.Item>
        )}
      </List>
      {skipped > 0 && (
        <Message
          info
          size="small"
        >
          {skipped === 1
            ? '1 selected download is left out: '
            : `${skipped} selected downloads are left out: `}
          only a finished download whose file is still on disk can be shared.
        </Message>
      )}
    </>
  );
};

/**
 * Makes a share link for some downloads: how long it lasts, who to mail it
 * to, and whether the downloads leave the list once it is made.
 *
 * The removal happens after the link exists and never instead of it -- the
 * server has copied the files by then, so removing the downloads, and their
 * files with them where the server is set to, cannot take anything from the
 * link.
 * @param {object} params
 * @param {object} params.defaults - The server's share link options.
 * @param {boolean} params.deleteFileOnRemoval - Whether a removal deletes the file.
 * @param {object[]} params.files - The selected downloads.
 * @param {boolean} params.mailEnabled - Whether the server can send mail.
 * @param {Function} params.onClose - Called when the dialog is done with.
 * @param {Function} params.onRemove - Removes the given downloads.
 */
const ShareDialog = ({
  defaults,
  deleteFileOnRemoval,
  files,
  mailEnabled,
  onClose,
  onRemove,
}) => {
  const shareable = files.filter(shareLinks.isShareable);
  const maxDays = defaults?.maxDays ?? 90;

  const [days, setDays] = useState(String(defaults?.defaultDays ?? 7));
  const [title, setTitle] = useState('');
  const [recipientsText, setRecipientsText] = useState('');
  const [remove, setRemove] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [created, setCreated] = useState(null);

  const recipients = shareLinks.parseRecipients(recipientsText);
  const invalid = shareLinks.invalidRecipients(recipients);
  const dayCount = Number.parseInt(days, 10);
  const daysValid =
    Number.isInteger(dayCount) && dayCount >= 1 && dayCount <= maxDays;

  const handleCreate = async () => {
    setBusy(true);
    setError(null);

    try {
      const link = await shareLinks.create({
        days: dayCount,
        recipients,
        title: title.trim() || undefined,
        transferIds: shareable.map((file) => file.id),
      });

      if (remove) {
        await onRemove(shareable);
      }

      setCreated(link);
    } catch (createError) {
      setError(createError?.response?.data ?? createError?.message ?? 'Failed');
    } finally {
      setBusy(false);
    }
  };

  if (created) {
    return (
      <Modal
        onClose={onClose}
        open
        size="small"
      >
        <Modal.Header>
          <Icon name="share alternate" />
          Shared: {created.title}
        </Modal.Header>
        <Modal.Content>
          <CopyField value={created.url} />
          <p className="share-dialog-note">
            Works until {new Date(created.expiresAt).toLocaleString()}; the
            copies are deleted then. Manage it under Shared.
          </p>
          {created.mail?.length > 0 && <MailResults results={created.mail} />}
        </Modal.Content>
        <Modal.Actions>
          <Button
            onClick={onClose}
            primary
          >
            Done
          </Button>
        </Modal.Actions>
      </Modal>
    );
  }

  return (
    <Modal
      onClose={busy ? undefined : onClose}
      open
      size="small"
    >
      <Modal.Header>
        <Icon name="share alternate" />
        Share
      </Modal.Header>
      <Modal.Content>
        <Selection
          files={shareable}
          skipped={files.length - shareable.length}
        />
        <Form
          id="share-dialog-form"
          onSubmit={handleCreate}
        >
          <Form.Input
            label="Title"
            onChange={(_event, { value }) => setTitle(value)}
            placeholder="From the files"
            value={title}
          />
          <Form.Field>
            <label htmlFor="share-dialog-days">Available for</label>
            <div className="share-dialog-days">
              {shareLinks.DURATIONS.filter((value) => value <= maxDays).map(
                (value) => (
                  <Button
                    active={dayCount === value}
                    key={value}
                    onClick={() => setDays(String(value))}
                    type="button"
                  >
                    {value === 1 ? '1 day' : `${value} days`}
                  </Button>
                ),
              )}
              <Form.Input
                error={!daysValid}
                id="share-dialog-days"
                label={{ basic: true, content: 'days' }}
                labelPosition="right"
                max={maxDays}
                min={1}
                onChange={(_event, { value }) => setDays(value)}
                type="number"
                value={days}
              />
            </div>
          </Form.Field>
          <Form.TextArea
            disabled={!mailEnabled}
            error={
              invalid.length > 0 && {
                content: `Not an email address: ${invalid.join(', ')}`,
              }
            }
            label="Email the link to"
            onChange={(_event, { value }) => setRecipientsText(value)}
            placeholder={
              mailEnabled
                ? 'Addresses, separated by commas or spaces (optional)'
                : 'Mail is not set up on this server'
            }
            rows={2}
            value={recipientsText}
          />
          <Form.Field>
            <Checkbox
              checked={remove}
              label={shareLinks.describeRemoval({
                count: shareable.length,
                deleteFileOnRemoval,
              })}
              onChange={(_event, { checked }) => setRemove(checked)}
            />
            <p className="share-dialog-note">
              The link keeps its own copy, so it works whatever happens to the
              download.
            </p>
          </Form.Field>
        </Form>
        {error && (
          <Message
            error
            size="small"
          >
            {String(error)}
          </Message>
        )}
      </Modal.Content>
      <Modal.Actions>
        <Button
          disabled={busy}
          onClick={onClose}
          type="button"
        >
          Cancel
        </Button>
        <Button
          disabled={
            busy || shareable.length === 0 || !daysValid || invalid.length > 0
          }
          form="share-dialog-form"
          loading={busy}
          primary
          type="submit"
        >
          <Icon name="share alternate" />
          Create link
        </Button>
      </Modal.Actions>
    </Modal>
  );
};

export default ShareDialog;
