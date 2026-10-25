import './ShareLinks.css';
import * as shareLinks from '../../lib/shareLinks';
import { formatBytes, formatDate, formatDayMonth } from '../../lib/util';
import AppContext from '../AppContext';
import { LoaderSegment, PlaceholderSegment } from '../Shared';
import CopyField from './CopyField';
import React, { useCallback, useContext, useEffect, useState } from 'react';
import { toast } from 'react-toastify';
import {
  Button,
  Confirm,
  Dropdown,
  Form,
  Header,
  Icon,
  List,
  Modal,
  Popup,
  Segment,
} from 'semantic-ui-react';

const REFRESH_MILLISECONDS = 30_000;

const errorOf = (error) =>
  error?.response?.data ?? error?.message ?? String(error);

/**
 * Asks for more addresses to mail a share to. Each gets a link of its own, or
 * the one it already has.
 */
const MailModal = ({ link, onClose }) => {
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);

  const recipients = shareLinks.parseRecipients(text);
  const invalid = shareLinks.invalidRecipients(recipients);

  const handleSend = async () => {
    setBusy(true);

    try {
      const { mail: results } = await shareLinks.mail({
        id: link.id,
        recipients,
      });
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
        <p className="share-dialog-note">
          Each address gets a link of its own, which can be revoked without
          affecting anyone else. An address that already has one is sent the
          same link again.
        </p>
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

const copy = async (url) => {
  try {
    await navigator.clipboard.writeText(url);
    toast.success('Link copied');
  } catch {
    toast.error("Couldn't copy the link");
  }
};

/**
 * The share's general link -- the one to paste into a chat -- or a way to make
 * one when there is none. A revoked general link is never revived: making
 * another issues a new address.
 */
const GeneralLink = ({ busy, general, onCreate, onRevoke }) =>
  general ? (
    <div className="share-link-general">
      <div className="share-link-general-label">
        <span>General link</span>
        <span className="share-link-access-status">
          {shareLinks.describeAccess(general)}
        </span>
      </div>
      <CopyField value={general.url} />
      <Button
        basic
        className="share-link-general-revoke"
        content="Revoke"
        disabled={busy}
        icon="ban"
        onClick={() => onRevoke(general)}
        size="small"
      />
    </div>
  ) : (
    <div className="share-link-general share-link-general-none">
      <span>No general link: only the addresses below can open this.</span>
      <Button
        basic
        content="Make a general link"
        disabled={busy}
        icon="linkify"
        onClick={onCreate}
        size="small"
      />
    </div>
  );

/**
 * Everyone the share was mailed to, and any general link that was revoked:
 * the record of who was given it, and what each link has done since.
 */
const AccessLog = ({ accesses, busy, onRevoke }) =>
  accesses.length > 0 && (
    <List
      className="share-link-accesses"
      divided
    >
      {accesses.map((access) => {
        const revoked = Boolean(access.revokedAt);

        return (
          <List.Item
            className={revoked ? 'share-link-access-revoked' : undefined}
            key={access.id}
          >
            <List.Content floated="right">
              {!revoked && (
                <>
                  <Popup
                    content="Copy this person's link"
                    trigger={
                      <Icon
                        link
                        name="copy outline"
                        onClick={() => copy(access.url)}
                      />
                    }
                  />
                  <Popup
                    content="Revoke this link; nobody else's is affected"
                    trigger={
                      <Icon
                        color="red"
                        disabled={busy}
                        link
                        name="ban"
                        onClick={() => onRevoke(access)}
                      />
                    }
                  />
                </>
              )}
            </List.Content>
            <Icon name={access.recipient ? 'mail outline' : 'linkify'} />
            <List.Content>
              <List.Header>{access.recipient ?? 'General link'}</List.Header>
              <List.Description>
                {access.sentAt && `sent ${formatDayMonth(access.sentAt)} · `}
                {shareLinks.describeAccess(access)}
              </List.Description>
            </List.Content>
          </List.Item>
        );
      })}
    </List>
  );

/**
 * One share: what it holds, its links and who has them, until when, and what
 * can be done to it.
 */
const LinkItem = ({ link, mailEnabled, maxDays, onChanged }) => {
  const [busy, setBusy] = useState(false);
  const [mailing, setMailing] = useState(false);
  const [confirming, setConfirming] = useState(null);

  const count = link.files.length;
  const general = shareLinks.generalAccessOf(link);
  const logged = link.accesses.filter((access) => access !== general);

  const run = async (action, success) => {
    setBusy(true);

    try {
      await action();
      if (success) toast.success(success);
      onChanged();
    } catch (error) {
      toast.error(errorOf(error));
    } finally {
      setBusy(false);
    }
  };

  const handleExtend = (_event, { value }) =>
    run(
      () => shareLinks.extend({ days: value, id: link.id }),
      `“${link.title}” now works for ${value === 1 ? '1 day' : `${value} days`}`,
    );

  const handleConfirmed = () => {
    const pending = confirming;
    setConfirming(null);

    if (pending.access) {
      run(
        () =>
          shareLinks.revokeAccess({ accessId: pending.access.id, id: link.id }),
        pending.access.recipient
          ? `${pending.access.recipient}'s link is revoked`
          : 'The general link is revoked',
      );
    } else {
      run(
        () => shareLinks.revoke({ id: link.id }),
        `“${link.title}” is withdrawn and its copies deleted`,
      );
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
            <span title={formatDate(link.expiresAt)}>
              {shareLinks.describeExpiry({ expiresAt: link.expiresAt })}
            </span>{' '}
            · {shareLinks.describeDownloads(link.downloadCount)}
          </Header.Subheader>
        </Header.Content>
      </Header>
      <GeneralLink
        busy={busy}
        general={general}
        onCreate={() =>
          run(() => shareLinks.createGeneral({ id: link.id }), null)
        }
        onRevoke={(access) => setConfirming({ access })}
      />
      <AccessLog
        accesses={logged}
        busy={busy}
        onRevoke={(access) => setConfirming({ access })}
      />
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
          onClick={() => setConfirming({})}
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
      <Confirm
        cancelButton="Cancel"
        confirmButton={confirming?.access ? 'Revoke' : 'Withdraw'}
        content={
          confirming?.access
            ? `${confirming.access.recipient ? `${confirming.access.recipient}'s link` : 'The general link'} stops working now. The share and every other link to it carry on.`
            : 'Every link to it stops working now, and the copies it serves are deleted. The downloads they were made from are not touched.'
        }
        header={
          confirming?.access ? 'Revoke this link?' : `Withdraw “${link.title}”?`
        }
        onCancel={() => setConfirming(null)}
        onConfirm={handleConfirmed}
        open={Boolean(confirming)}
        size="tiny"
      />
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
