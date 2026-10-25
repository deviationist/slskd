import api from './api';
import * as transfers from './transfers';
import { formatDate, formatDayMonth } from './util';

/**
 * Shares: copies of finished downloads that people without an account can
 * download, until the share expires. The server copies the files when the
 * share is made, so it outlives the downloads it came from, and deletes the
 * copies when it expires.
 *
 * Who can download is a matter of the share's links: a general one to hand
 * out by any means, and one per address it was mailed to, each revocable on
 * its own.
 */

/**
 * The validities the dialog offers, in days. Any number up to the server's
 * maximum can be typed; these are the ones worth a button. Two and three
 * months for a share meant to outlast a holiday or a season; the server's
 * default maximum is 90, and a preset above whatever it is set to is not shown.
 */
export const DURATIONS = [1, 7, 30, 60, 90];

/**
 * How many addresses one request may mail; the server refuses more
 * (ShareLinkService.MaximumRecipients).
 */
export const MAXIMUM_RECIPIENTS = 20;

export const list = async () => (await api.get('/links')).data;

export const create = async ({
  days,
  general = true,
  recipients = [],
  title,
  transferIds,
}) =>
  (
    await api.post('/links', {
      days,
      general,
      recipients,
      title,
      transferIds,
    })
  ).data;

export const extend = async ({ days, id }) =>
  (await api.put(`/links/${encodeURIComponent(id)}`, { days })).data;

export const mail = async ({ id, recipients }) =>
  (await api.post(`/links/${encodeURIComponent(id)}/mail`, { recipients }))
    .data;

export const revoke = async ({ id }) =>
  api.delete(`/links/${encodeURIComponent(id)}`);

export const createGeneral = async ({ id }) =>
  (await api.post(`/links/${encodeURIComponent(id)}/general`)).data;

export const revokeAccess = async ({ accessId, id }) =>
  (
    await api.delete(
      `/links/${encodeURIComponent(id)}/accesses/${encodeURIComponent(accessId)}`,
    )
  ).data;

/**
 * The share's general link that still works, if it has one.
 */
export const generalAccessOf = (link) =>
  link?.accesses?.find((access) => !access.recipient && !access.revokedAt);

/**
 * What one of a share's links has done, in words: revoked, never delivered,
 * or how often it has been used.
 */
export const describeDownloads = (count) => {
  if (!count) {
    return 'not downloaded yet';
  }

  return count === 1 ? 'downloaded once' : `downloaded ${count} times`;
};

export const describeAccess = (access) => {
  // a revoked link keeps what it did before it was revoked: that is the part
  // of the record worth having
  if (access.revokedAt) {
    return access.downloadCount
      ? `revoked ${formatDayMonth(access.revokedAt)} · ${describeDownloads(access.downloadCount)}`
      : `revoked ${formatDayMonth(access.revokedAt)}`;
  }

  if (access.recipient && !access.sentAt && access.mailError) {
    return `not delivered: ${access.mailError}`;
  }

  const downloads = describeDownloads(access.downloadCount);

  return access.downloadCount && access.lastDownloadedAt
    ? `${downloads}, last ${formatDate(access.lastDownloadedAt)}`
    : downloads;
};

/**
 * Whether a download can go into a share link: it has to have finished
 * successfully and still have a file the server recorded writing -- the
 * same question as whether it can be retrieved, since the server copies it
 * from the same place -- and the server must not already have reported that
 * file gone.
 */
export const isShareable = (file) =>
  file?.direction === 'Download' &&
  transfers.isRetrievable(file) &&
  file.localFileExists !== false;

/**
 * Splits what was typed into the recipients field into addresses: commas,
 * semicolons and whitespace all separate, and duplicates go.
 *
 * An address as a mail client copies it -- `Ola Nordmann <ola@x.no>` -- is
 * taken for the address alone, as Gmail takes it; the name would otherwise
 * become two addresses that are not, beside the real one in brackets.
 */
export const parseRecipients = (text) => {
  const seen = new Set();

  return (
    (text ?? '')
      // a quoted name first, since it may hold the very commas that separate
      .replaceAll(/"[^"]*"/gu, ' ')
      .replaceAll(/[^,;<>\n]*<([^\s<>][^\s<>@]*@[^\s<>]+)>/gu, ' $1 ')
      .split(/[\s,;]+/u)
      .map((value) => value.trim())
      .filter((value) => {
        const key = value.toLowerCase();

        if (!value || seen.has(key)) {
          return false;
        }

        seen.add(key);
        return true;
      })
  );
};

/**
 * Whether a value looks like an address. Deliberately loose -- the server has
 * the last word -- and only here so a typo is caught before the copies are
 * made rather than after.
 */
export const isRecipient = (value) =>
  /^[^\s@]+@[^\s@][^\s.@]*\.[^\s@]+$/u.test(value);

/**
 * The addresses that are plainly not addresses.
 */
export const invalidRecipients = (recipients) =>
  recipients.filter((value) => !isRecipient(value));

/**
 * The given addresses with whatever was typed or pasted added to the end:
 * split as {@link parseRecipients} splits, and without any already there,
 * compared as the server compares them, ignoring case.
 */
export const addRecipients = (recipients, text) => {
  const taken = new Set(recipients.map((value) => value.toLowerCase()));

  return [
    ...recipients,
    ...parseRecipients(text).filter((value) => !taken.has(value.toLowerCase())),
  ];
};

/**
 * Where a recipients field stands: who would be mailed, which pills are not
 * addresses, and whether there are more than one request may mail.
 *
 * An address is judged when it becomes a pill, and not before. Every address
 * is invalid until it is finished -- `a`, `a@`, `a@x` -- so judging what is
 * still being typed would flag each one red on its way to being right. What
 * is being typed still counts once it is an address, so that the last one
 * typed is not left out by clicking Share before ending it; one that is not
 * becomes a red pill as the field is left, which is before any click lands.
 * @param {object} addresses
 * @param {string[]} addresses.recipients - The pills.
 * @param {string} addresses.draft - What is being typed.
 * @returns {object} `recipients` to mail, `invalid` pills, and `tooMany`.
 */
export const summarizeRecipients = ({ draft, recipients }) => {
  const typed = draft.trim();
  const all = isRecipient(typed)
    ? addRecipients(recipients, typed)
    : recipients;

  return {
    invalid: invalidRecipients(recipients),
    recipients: all,
    tooMany: all.length > MAXIMUM_RECIPIENTS,
  };
};

/**
 * How long a link has left, in words: "in 6 days", "in 5 hours", or
 * "expired" once it has.
 */
export const describeExpiry = ({ expiresAt, now = new Date() }) => {
  const remaining = new Date(expiresAt).getTime() - now.getTime();

  if (remaining <= 0) {
    return 'expired';
  }

  const minutes = Math.round(remaining / 60_000);
  const hours = Math.round(remaining / 3_600_000);
  const days = Math.round(remaining / 86_400_000);

  if (minutes < 60) {
    return `in ${minutes} minute${minutes === 1 ? '' : 's'}`;
  }

  if (hours < 48) {
    return `in ${hours} hour${hours === 1 ? '' : 's'}`;
  }

  return `in ${days} days`;
};

/**
 * What a removal after sharing will do, for the checkbox that asks for it.
 * Whether the file goes too is the server's setting, so the label has to say
 * which -- and since the link holds its own copy, either is safe for the link.
 */
export const describeRemoval = ({ count, deleteFileOnRemoval }) => {
  const what = count === 1 ? 'the download' : `the ${count} downloads`;

  return deleteFileOnRemoval
    ? `Remove ${what} from Downloads and delete the original file${count === 1 ? '' : 's'}`
    : `Remove ${what} from Downloads`;
};
