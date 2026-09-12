import api from './api';
import * as searches from './searches';
import { validateSearchText } from './searches';
import { v4 as uuidv4 } from 'uuid';

/**
 * The recurrences a watch can be given.
 *
 * A fixed set rather than a rule builder: the shortest useful interval here is
 * an hour, the realistic settings are "daily" and "every few hours", and the
 * whole of RFC 5545 is a large amount of interface for a choice that small. The
 * server* accepts any valid rule, so nothing here limits what a watch set
 * through the API can do — this is only what the page offers.
 */
export const PRESETS = [
  {
    hour: false,
    key: 'hourly',
    label: 'Every hour',
    rrule: 'FREQ=HOURLY;INTERVAL=1',
  },
  {
    hour: false,
    key: 'every6',
    label: 'Every 6 hours',
    rrule: 'FREQ=HOURLY;INTERVAL=6',
  },
  {
    hour: false,
    key: 'every12',
    label: 'Every 12 hours',
    rrule: 'FREQ=HOURLY;INTERVAL=12',
  },
  {
    hour: true,
    key: 'daily',
    label: 'Once a day',
    rrule: 'FREQ=DAILY;BYHOUR={h};BYMINUTE=0',
  },
  {
    hour: true,
    key: 'weekdays',
    label: 'Weekdays',
    rrule: 'FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR;BYHOUR={h};BYMINUTE=0',
  },
  {
    hour: true,
    key: 'weekly',
    label: 'Once a week, on Monday',
    rrule: 'FREQ=WEEKLY;BYDAY=MO;BYHOUR={h};BYMINUTE=0',
  },
];

export const DEFAULT_PRESET = 'daily';
export const DEFAULT_HOUR = 3;

const preset = (key) => PRESETS.find((p) => p.key === key);

/**
 * Builds the rule for a chosen preset and hour.
 * @param {object} params
 * @param {string} params.key - The preset key.
 * @param {number} params.hour - The hour of day, for presets that use one.
 * @returns {string|undefined} The rule, or undefined if the preset is unknown.
 */
export const rruleFor = ({ key, hour = DEFAULT_HOUR }) => {
  const found = preset(key);

  if (!found) {
    return undefined;
  }

  return found.rrule.replace('{h}', String(hour));
};

/**
 * Reads a rule back into the preset that would have produced it.
 *
 * Needed because a watch stores a rule, not a choice: editing one has to show
 * the control it came from. A rule set through the API that no preset produces
 * reads back as undefined, and the interface says so rather than silently
 * offering to overwrite it with something else.
 * @param {string} rrule - The rule.
 * @returns {{key: string, hour: number}|undefined} The preset and hour, or undefined.
 */
export const presetFor = (rrule) => {
  if (!rrule) {
    return undefined;
  }

  for (const candidate of PRESETS) {
    if (!candidate.hour) {
      if (candidate.rrule === rrule) {
        return { hour: DEFAULT_HOUR, key: candidate.key };
      }

      continue;
    }

    for (let hour = 0; hour < 24; hour++) {
      if (candidate.rrule.replace('{h}', String(hour)) === rrule) {
        return { hour, key: candidate.key };
      }
    }
  }

  return undefined;
};

/**
 * Says what a recurrence does, in a sentence.
 * @param {string} rrule - The rule.
 * @returns {string} The description.
 */
export const describeRecurrence = (rrule) => {
  const found = presetFor(rrule);

  if (!found) {
    // a rule the presets cannot express is still a rule the server will run;
    // showing it is more honest than pretending it is one of ours
    return rrule ? `Custom: ${rrule}` : 'No schedule';
  }

  const { hour, key } = found;
  const label = preset(key).label;

  return preset(key).hour
    ? `${label}, at ${String(hour).padStart(2, '0')}:00`
    : label;
};

/**
 * Says when a watch next runs, relative to now.
 * @param {object} params
 * @param {object} params.watch - The watch.
 * @param {Date} params.now - The current moment.
 * @returns {string} The description.
 */
export const describeNextRun = ({ watch, now = new Date() }) => {
  if (!watch?.enabled) {
    return 'Paused';
  }

  if (!watch.nextRunAt) {
    return 'Not scheduled';
  }

  const minutes = Math.round((new Date(watch.nextRunAt) - now) / 60_000);

  // a watch that is due but has not run yet is not late in any sense worth
  // reporting: it runs on the next tick, or when the server comes back
  if (minutes <= 0) {
    return 'Due now';
  }

  if (minutes < 60) {
    return `in ${minutes} min`;
  }

  const hours = Math.round(minutes / 60);

  if (hours < 48) {
    return `in ${hours} h`;
  }

  return `in ${Math.round(hours / 24)} days`;
};

/**
 * Says how a watched row should read.
 * @param {object} params
 * @param {object} params.watch - The watch, if there is one.
 * @param {object[]} params.notifications - Its notifications, newest first, if known.
 * @returns {{color: string, label: string, icon: string}|undefined} How to badge it.
 */
export const watchBadge = ({ watch, notifications = [] }) => {
  if (!watch) {
    return undefined;
  }

  // a failing notification outranks everything else this badge could say: a
  // watch whose mail has been bouncing looks exactly like one that has found
  // nothing, and that is the confusion worth spending the badge on
  if (notifications.length > 0 && notifications[0].sent === false) {
    return {
      color: 'red',
      icon: 'exclamation triangle',
      label: 'Mail failing',
    };
  }

  if (!watch.enabled) {
    return { color: 'grey', icon: 'pause', label: 'Paused' };
  }

  return { color: 'blue', icon: 'clock outline', label: 'Watching' };
};

/**
 * Whether a draft can be saved.
 * @param {object} draft - The draft.
 * @returns {{ok: boolean, reason?: string}} Whether it can, and why not.
 */
export const validateDraft = (draft) => {
  // the phrase is editable here, so it can be emptied here. the rule and the
  // wording are the server's, the same ones the search buttons answer with
  const searchText = validateSearchText(draft?.searchText);

  if (!searchText.ok) {
    return searchText;
  }

  if (!rruleFor({ hour: draft?.hour, key: draft?.key })) {
    return { ok: false, reason: 'Choose how often this search should run' };
  }

  // an address is optional -- blank means the configured default -- but a
  // blank-looking address that is not blank is a typo, and a watch that cannot
  // deliver is one that reports nothing and says nothing
  const email = (draft.notifyEmail ?? '').trim();

  if (email.length > 0 && !/^[^\s@]+@[^\s@][^\s.@]*\.[^\s@]+$/u.test(email)) {
    return { ok: false, reason: 'That does not look like an email address' };
  }

  return { ok: true };
};

/**
 * Reads the files a notification reported.
 *
 * The record keeps at most a couple of hundred of them while the count is the
 * true one, so a mail that reported 1471 files has 200 to show and a number to
 * be honest about. Anything unreadable reads as empty rather than throwing: a
 * log that cannot render one row should not take the other rows with it.
 * @param {object} notification - The notification.
 * @returns {{files: object[], shown: number, total: number, truncated: boolean}} What it reported.
 */
export const filesFrom = (notification) => {
  let files = [];

  try {
    files = JSON.parse(notification?.filesJson ?? '[]') ?? [];
  } catch {
    files = [];
  }

  const total = notification?.fileCount ?? files.length;

  return {
    // read either casing: records written before the server sent camelCase are
    // still in the log, and a row that cannot be read is worse than one written
    // in the wrong shape
    files: files.map((file) => ({
      filename: file.filename ?? file.Filename,
      size: file.size ?? file.Size,
      username: file.username ?? file.Username,
    })),
    shown: files.length,
    total,
    truncated: total > files.length,
  };
};

/**
 * Says what an ignore does, in a sentence.
 * @param {object} ignore - The ignore.
 * @returns {string} The description.
 */
/**
 * Reads the file a link asked to ignore, from a query string.
 *
 * The link in a notification carries no authority: it asks the page to *offer*
 * the action, and the page asks before taking it. Anything that follows links
 * in mail on the reader's behalf therefore changes nothing.
 * @param {string} query - The query string, with or without its leading question mark.
 * @returns {string|undefined} The filename, or undefined if none was asked for.
 */
export const ignoreTargetFrom = (query) => {
  if (!query) {
    return undefined;
  }

  const value = new URLSearchParams(query).get('ignore');

  // a blank target would offer to ignore the empty string, which every file
  // would then match
  return value && value.trim().length > 0 ? value.trim() : undefined;
};

export const describeIgnore = (ignore) =>
  ignore?.kind === 'User' || ignore?.kind === 1
    ? `Everything from ${ignore.value}`
    : `Any file named ${ignore?.value}`;

export const getIgnores = async () => (await api.get('/watches/ignores')).data;

export const addIgnore = async ({ kind, note, value }) =>
  (await api.post('/watches/ignores', { kind, note, value })).data;

export const removeIgnore = async ({ id }) =>
  api.delete(`/watches/ignores/${encodeURIComponent(id)}`);

export const getAll = async () => (await api.get('/watches')).data;

export const get = async ({ id }) =>
  (await api.get(`/searches/${encodeURIComponent(id)}/watch`)).data;

export const put = async ({ id, watch }) =>
  (await api.put(`/searches/${encodeURIComponent(id)}/watch`, watch)).data;

export const remove = async ({ id }) =>
  api.delete(`/searches/${encodeURIComponent(id)}/watch`);

export const run = async ({ id }) =>
  (await api.post(`/searches/${encodeURIComponent(id)}/watch/run`)).data;

export const getRuns = async ({ id }) =>
  (await api.get(`/searches/${encodeURIComponent(id)}/watch/runs`)).data;

export const getNotifications = async ({ id }) =>
  (await api.get(`/searches/${encodeURIComponent(id)}/watch/notifications`))
    .data;

/**
 * Creates a search and puts a watch on it.
 *
 * In that order, and not by accident: a watch is an extension of a search
 * rather than a thing of its own, and what it has already reported is keyed on
 * the search's id -- so there is nothing to watch until the search exists.
 *
 * Shared, because two pages offer this and a second copy of the order would be
 * a second chance to get it wrong.
 * @param {object} params
 * @param {string} params.searchText - The phrase to search for.
 * @param {object} params.watch - The watch to put on it.
 * @returns {Promise<{id: string, watch: object, seeded: number}>} What was created.
 */
export const createWatchedSearch = async ({ searchText, watch }) => {
  const id = uuidv4();

  await searches.create({ id, searchText });

  const { seeded, watch: saved } = await put({ id, watch });

  return { id, seeded, watch: saved };
};
