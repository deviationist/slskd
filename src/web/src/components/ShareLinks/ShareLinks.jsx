import './ShareLinks.css';
import * as shareLinks from '../../lib/shareLinks';
import { formatBytes } from '../../lib/util';
import AppContext from '../AppContext';
import { LoaderSegment, PlaceholderSegment } from '../Shared';
import CopyField from './CopyField';
import React, { useCallback, useContext, useEffect, useState } from 'react';
import { toast } from 'react-toastify';
import {
  Button,
  Dropdown,
  Form,
  Header,
  Icon,
  List,
  Modal,
  Segment,
} from 'semantic-ui-react';

const REFRESH_MILLISECONDS = 30_000;

const errorOf = (error) =>
  error?.response?.data ?? error?.message ?? String(error);

/**
 * Asks for more addresses to mail an existing link to.
 */
const MailModal = ({ link, onClose }) => {
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);

  const recipients = shareLinks.parseRecipients(text);
  const invalid = shareLinks.invalidRecipients(recipients);

  const handleSend = async () => {
    setBusy(true);

    try {
      const results = await shareLinks.mail({ id: link.id, recipients });
      const failed = results.filter((result) => !result.sent);

      if (failed.length === 0) {
        toast.success(
          `Mailed to ${results.length === 1 ? results[0].recipient : `${results.length} addresses`}`,
        );
      } else {
        for (const result of failed)
          toast.error(`${result.recipient}: ${result.error}`);
      }

      onClose(true);
    } catch (error) {
      toast.error(errorOf(error));
      setBusy(false);
    }
  };

  return (
    <Modal
      onClose={busy ? undefined : () => onClose(false)}
      open
      size="tiny"
    >
      <Modal.Header>Email “{link.title}”</Modal.Header>
      <Modal.Content>
        <Form
          id="share-links-mail-form"
          onSubmit={handleSend}
        >
          <Form.TextArea
            autoFocus
            error={
              invalid.length > 0 && {
                content: `Not an email address: ${invalid.join(', ')}`,
              }
            }
            label="Addresses"
            onChange={(_event, { value }) => setText(value)}
            placeholder="Separated by commas or spaces"
            rows={2}
            value={text}
          />
        </Form>
      </Modal.Content>
      <Modal.Actions>
        <Button
          disabled={busy}
          onClick={() => onClose(false)}
          type="button"
        >
          Cancel
        </Button>
        <Button
          disabled={busy || recipients.length === 0 || invalid.length > 0}
          form="share-links-mail-form"
          loading={busy}
          primary
          type="submit"
        >
          Send
        </Button>
      </Modal.Actions>
    </Modal>
  );
};

/**
 * One link: what it holds, who has it, until when, and what can be done to it.
 */
const LinkItem = ({ link, mailEnabled, maxDays, onChanged }) => {
  const [busy, setBusy] = useState(false);
  const [mailing, setMailing] = useState(false);
  const [confirming, setConfirming] = useState(false);

  const count = link.files.length;

  const handleExtend = async (_event, { value }) => {
    setBusy(true);

    try {
      await shareLinks.extend({ days: value, id: link.id });
      toast.success(
        `“${link.title}” now works for ${value === 1 ? '1 day' : `${value} days`}`,
      );
      onChanged();
    } catch (error) {
      toast.error(errorOf(error));
    } finally {
      setBusy(false);
    }
  };

  const handleRevoke = async () => {
    setConfirming(false);
    setBusy(true);

    try {
      await shareLinks.revoke({ id: link.id });
      toast.success(`“${link.title}” is withdrawn and its copies deleted`);
      onChanged();
    } catch (error) {
      toast.error(errorOf(error));
      setBusy(false);
    }
  };

  return (
    <Segment className="share-link">
      <Header
        as="h4"
        className="share-link-header"
      >
        <Icon name={count === 1 ? 'file outline' : 'folder outline'} />
        <Header.Content>
          {link.title}
          <Header.Subheader>
            {count === 1 ? '1 file' : `${count} files`},{' '}
            {formatBytes(link.size)} · expires{' '}
            <span title={new Date(link.expiresAt).toLocaleString()}>
              {shareLinks.describeExpiry({ expiresAt: link.expiresAt })}
            </span>{' '}
            ·{' '}
            {link.downloadCount === 1
              ? 'downloaded once'
              : link.downloadCount === 0
                ? 'not downloaded yet'
                : `downloaded ${link.downloadCount} times`}
          </Header.Subheader>
        </Header.Content>
      </Header>
      <CopyField value={link.url} />
      {link.recipients.length > 0 && (
        <p className="share-link-recipients">
          <Icon name="mail outline" />
          {link.recipients.join(', ')}
        </p>
      )}
      <List
        bulleted
        className="share-link-files"
      >
        {link.files.slice(0, 5).map((file) => (
          <List.Item key={file.id}>{file.name}</List.Item>
        ))}
        {count > 5 && <List.Item>and {count - 5} more</List.Item>}
      </List>
      <div className="share-link-actions">
        <Dropdown
          button
          className="icon"
          disabled={busy}
          icon="clock outline"
          labeled
          onChange={handleExtend}
          options={shareLinks.DURATIONS.filter((days) => days <= maxDays).map(
            (days) => ({
              key: days,
              text: days === 1 ? '1 day from now' : `${days} days from now`,
              value: days,
            }),
          )}
          selectOnBlur={false}
          text="Extend"
          value={null}
        />
        {mailEnabled && (
          <Button
            content="Email"
            disabled={busy}
            icon="mail outline"
            labelPosition="left"
            onClick={() => setMailing(true)}
          />
        )}
        <Button
          color="red"
          content="Withdraw"
          disabled={busy}
          icon="trash alternate"
          labelPosition="left"
          loading={busy}
          onClick={() => setConfirming(true)}
        />
      </div>
      {mailing && (
        <MailModal
          link={link}
          onClose={(sent) => {
            setMailing(false);
            if (sent) onChanged();
          }}
        />
      )}
      {confirming && (
        <Modal
          onClose={() => setConfirming(false)}
          open
          size="tiny"
        >
          <Modal.Header>Withdraw “{link.title}”?</Modal.Header>
          <Modal.Content>
            The link stops working now, and the copies it serves are deleted.
            The downloads they were made from are not touched.
          </Modal.Content>
          <Modal.Actions>
            <Button onClick={() => setConfirming(false)}>Cancel</Button>
            <Button
              color="red"
              onClick={handleRevoke}
            >
              Withdraw
            </Button>
          </Modal.Actions>
        </Modal>
      )}
    </Segment>
  );
};

/**
 * Every share link this instance holds, newest first.
 */
const ShareLinks = () => {
  const { options } = useContext(AppContext) ?? {};
  const settings = options?.transfers?.shareLinks;
  const mailEnabled = options?.integrations?.mail?.enabled === true;

  const [links, setLinks] = useState(null);
  const [error, setError] = useState(null);

  const load = useCallback(async () => {
    try {
      setLinks(await shareLinks.list());
      setError(null);
    } catch (loadError) {
      setError(errorOf(loadError));
    }
  }, []);

  useEffect(() => {
    load();
    const interval = setInterval(load, REFRESH_MILLISECONDS);
    return () => clearInterval(interval);
  }, [load]);

  if (settings && !settings.enabled) {
    return (
      <PlaceholderSegment
        caption="Share links are turned off on this server"
        icon="share alternate"
      />
    );
  }

  if (error && !links) {
    return (
      <PlaceholderSegment
        caption={error}
        icon="share alternate"
      />
    );
  }

  if (!links) {
    return <LoaderSegment />;
  }

  if (links.length === 0) {
    return (
      <PlaceholderSegment
        caption="Nothing is shared. Share a finished download from Downloads."
        icon="share alternate"
      />
    );
  }

  return (
    <div className="share-links">
      {links.map((link) => (
        <LinkItem
          key={link.id}
          link={link}
          mailEnabled={mailEnabled}
          maxDays={settings?.maxDays ?? 90}
          onChanged={load}
        />
      ))}
    </div>
  );
};

export default ShareLinks;
