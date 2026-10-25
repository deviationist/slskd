import './ShareLinks.css';
import * as shareLinks from '../../lib/shareLinks';
import { formatBytes, formatDate } from '../../lib/util';
import CopyField from './CopyField';
import FileNames from './FileNames';
import RecipientsInput, {
  isPickEscape,
  RecipientProblems,
} from './RecipientsInput';
import React, { useState } from 'react';
import {
  Button,
  Checkbox,
  Form,
  Icon,
  Input,
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
      <FileNames
        className="share-dialog-files"
        names={listed.map((file) => getFileName(file.filename))}
        total={files.length}
      />
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
 * The share as it was made: its general link, if it has one, and what became
 * of each address it was mailed to.
 */
const Created = ({ link, onClose }) => (
  <Modal
    onClose={onClose}
    open
    size="small"
  >
    <Modal.Header>
      <Icon name="share alternate" />
      Shared: {link.title}
    </Modal.Header>
    <Modal.Content>
      {link.url && (
        <>
          <p>General link, for anyone you give it to:</p>
          <CopyField value={link.url} />
        </>
      )}
      {link.mail?.length > 0 && (
        <>
          <p className="share-dialog-mailed">
            Mailed, each with a link of their own:
          </p>
          <MailResults results={link.mail} />
        </>
      )}
      <p className="share-dialog-note">
        Works until {formatDate(link.expiresAt)}; the copies are deleted then.
        Every link can be revoked on its own under Shared.
      </p>
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
  const [addresses, setAddresses] = useState({ draft: '', recipients: [] });
  const [general, setGeneral] = useState(true);
  const [remove, setRemove] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [created, setCreated] = useState(null);

  // judged as pills, and the address being typed counted once it is one
  const { invalid, recipients, tooMany } =
    shareLinks.summarizeRecipients(addresses);
  const dayCount = Number.parseInt(days, 10);
  const daysValid =
    Number.isInteger(dayCount) && dayCount >= 1 && dayCount <= maxDays;
  // a share nobody has a link to is a copy nobody can reach
  const reachable = general || recipients.length > 0;

  const handleCreate = async () => {
    setBusy(true);
    setError(null);

    try {
      const link = await shareLinks.create({
        days: dayCount,
        general,
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
      <Created
        link={created}
        onClose={onClose}
      />
    );
  }

  return (
    <Modal
      onClose={
        busy
          ? undefined
          : (event) => {
              if (!isPickEscape(event)) onClose();
            }
      }
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
              {/* a plain Input in the field: on Form.Input, `label` is the
                  field's own label, which left the unit an empty tag and the
                  box styled for a label it did not have */}
              <Form.Field error={!daysValid}>
                <Input
                  id="share-dialog-days"
                  label={{ basic: true, content: 'days' }}
                  labelPosition="right"
                  max={maxDays}
                  min={1}
                  onChange={(_event, { value }) => setDays(value)}
                  type="number"
                  value={days}
                />
              </Form.Field>
            </div>
          </Form.Field>
          <Form.Field>
            <Checkbox
              checked={general}
              label="Make a general link, to send yourself (in a chat, a message…)"
              onChange={(_event, { checked }) => setGeneral(checked)}
            />
          </Form.Field>
          <Form.Field disabled={!mailEnabled}>
            <label htmlFor="share-dialog-recipients">Email it to</label>
            <RecipientsInput
              disabled={!mailEnabled}
              draft={addresses.draft}
              id="share-dialog-recipients"
              onChange={setAddresses}
              placeholder={
                mailEnabled
                  ? 'Type an address and press Enter (optional)'
                  : 'Mail is not set up on this server'
              }
              recipients={addresses.recipients}
            />
            {mailEnabled && (
              <p className="share-dialog-note">
                Each address gets a link of its own.
              </p>
            )}
            <RecipientProblems
              invalid={invalid}
              tooMany={tooMany}
            />
          </Form.Field>
          {!reachable && (
            <p className="share-dialog-note share-dialog-unreachable">
              Make a general link or add an address, or nobody will be able to
              open it.
            </p>
          )}
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
            busy ||
            shareable.length === 0 ||
            !daysValid ||
            invalid.length > 0 ||
            tooMany ||
            !reachable
          }
          form="share-dialog-form"
          loading={busy}
          primary
          type="submit"
        >
          <Icon name="share alternate" />
          Share
        </Button>
      </Modal.Actions>
    </Modal>
  );
};

export default ShareDialog;
