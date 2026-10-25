import api from './api';
import * as transfers from './transfers';

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
 * maximum can be typed; these are the ones worth a button.
 */
export const DURATIONS = [1, 7, 30];

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
export const describeAccess = (access) => {
  if (access.revokedAt) {
    return `revoked ${new Date(access.revokedAt).toLocaleDateString()}`;
  }

  if (access.recipient && !access.sentAt && access.mailError) {
    return `not delivered: ${access.mailError}`;
  }

  if (access.downloadCount === 0) {
    return 'not downloaded yet';
  }

  const times =
    access.downloadCount === 1
      ? 'downloaded once'
      : `downloaded ${access.downloadCount} times`;

  return access.lastDownloadedAt
    ? `${times}, last ${new Date(access.lastDownloadedAt).toLocaleString()}`
    : times;
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
 */
export const parseRecipients = (text) => {
  const seen = new Set();

  return (text ?? '')
    .split(/[\s,;]+/u)
    .map((value) => value.trim())
    .filter((value) => {
      const key = value.toLowerCase();

      if (!value || seen.has(key)) {
        return false;
      }

      seen.add(key);
      return true;
    });
};

/**
 * The addresses that are plainly not addresses. Deliberately loose -- the
 * server has the last word -- and only here so a typo is caught before the
 * copies are made rather than after.
 */
export const invalidRecipients = (recipients) =>
  recipients.filter(
    (value) => !/^[^\s@]+@[^\s@][^\s.@]*\.[^\s@]+$/u.test(value),
  );

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
