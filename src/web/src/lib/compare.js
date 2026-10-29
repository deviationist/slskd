import api from './api';
import { extensionOf, PLAYABLE } from './player/rows';
import * as transfers from './transfers';

/**
 * Compare: which of several finished downloads are the same track, as the
 * server's compare provider judges it from the audio. The provider sends its
 * answer as words -- a verdict, a headline, a note per copy -- so nothing
 * here knows what a match score means; it only puts the words beside the
 * rows they are about. The contract is docs/transfers-compare-provider.md.
 */

/**
 * The most downloads one comparison may hold; the server refuses more
 * (CompareProvider.MaximumFiles). A copy, so the button says so before it is
 * pressed rather than after.
 */
export const MAXIMUM_FILES = 10;

/**
 * Whether Compare is on: enabled, and a provider configured. Options arrive
 * over the hub, so this is false until they do, which errs towards not
 * offering something that would not work.
 */
export const isEnabled = (options) =>
  options?.transfers?.compare?.enabled === true &&
  Boolean(options?.transfers?.compare?.provider?.url);

/**
 * Whether a download can be compared: finished, its file recorded and not
 * known to be gone, and audio -- the same file the server's PlayableFile
 * resolves, which decides again for itself.
 */
export const isComparable = (file) =>
  transfers.isRetrievable(file) &&
  file.localFileExists !== false &&
  PLAYABLE.has(extensionOf(file.filename));

/**
 * Whether the button would do something, and its tooltip either way.
 *
 * Counted, because "off" with two rows ticked is a puzzle: the rows are
 * ticked, so which of them does not count is the whole answer.
 * @param {object[]} ticked - Every row ticked, across every card.
 * @returns {object} `on`, `tip`, and the `picks` it would compare.
 */
export const compareWhy = (ticked) => {
  const done = ticked.filter(transfers.isFinishedDownload);
  const picks = ticked.filter(isComparable);

  if (ticked.length < 2) {
    return {
      on: false,
      picks,
      tip: 'Tick two or more finished downloads, in any card, to check whether they are the same track',
    };
  }

  if (done.length < 2) {
    return {
      on: false,
      picks,
      tip: `${ticked.length} ticked, but only ${done.length} ${done.length === 1 ? 'has' : 'have'} finished downloading — only finished downloads can be compared`,
    };
  }

  if (picks.length < 2) {
    return {
      on: false,
      picks,
      tip: `${done.length} finished, but only ${picks.length} ${picks.length === 1 ? 'is' : 'are'} an audio file still on disk`,
    };
  }

  if (picks.length > MAXIMUM_FILES) {
    return {
      on: false,
      picks,
      tip: `At most ${MAXIMUM_FILES} at a time — ${picks.length} are ticked`,
    };
  }

  return {
    on: true,
    picks,
    tip: `Check whether these ${picks.length} are the same track`,
  };
};

/**
 * Asks the server to compare downloads. With `wait` false it answers
 * `pending` at once while the provider works, and is asked again.
 */
export const compare = async ({ ids, wait = false }) =>
  (await api.post(`/compare?wait=${wait}`, { ids })).data;

/**
 * The rows an answer is about, in the order its indices follow: the ids the
 * server says it compared, not the list this page sent. The two agree
 * today, but the server drops a repeated id, and indices read against the
 * page's own list would then put one row's verdict on another.
 * @param {string[]} [ids] - The ids from the server's answer.
 * @param {object[]} picks - The rows the page sent.
 * @returns {object[]} The rows, in the server's order.
 */
export const rowsOf = (ids, picks) => {
  if (!Array.isArray(ids)) return picks;
  const byId = new Map(picks.map((row) => [row.id, row]));

  return ids.map((id) => byId.get(id)).filter(Boolean);
};

/**
 * The colour each verdict is drawn in. The provider's levels are words, so a
 * level this page does not know is drawn plainly rather than guessed at.
 */
export const LEVEL_COLORS = {
  different: 'red',
  likely: 'yellow',
  mixed: 'yellow',
  partly: 'orange',
  same: 'green',
  unknown: 'grey',
};

/**
 * The provider's document with each index replaced by the row it means.
 * `files` is in the order the rows were sent, which is the order `picks`
 * holds them in.
 * @param {object} document - The provider's answer.
 * @param {object[]} picks - The rows compared, in the order sent.
 * @returns {object} `verdict`, `groups` of members, `alone`, `partly` pairs and `unreadable` rows.
 */
export const arrange = (document, picks) => {
  const files = document?.files ?? [];
  const row = (index) => picks[index];
  const length = (index) => files[index]?.music_s;

  return {
    alone: (document?.alone ?? []).map((index) => ({
      file: row(index),
      musicSeconds: length(index),
    })),
    groups: (document?.groups ?? []).map((group) =>
      group.files.map((index, position) => ({
        file: row(index),
        musicSeconds: length(index),
        note: group.notes?.[position] ?? '',
      })),
    ),
    partly: (document?.partly ?? []).map((pair) => ({
      a: row(pair.a),
      b: row(pair.b),
      note: pair.note ?? '',
    })),
    unreadable: files
      .map((entry, index) => ({ entry, index }))
      .filter(({ entry }) => !entry.readable)
      .map(({ entry, index }) => ({
        file: row(index),
        note: entry.note ?? '',
      })),
    verdict: document?.verdict ?? {
      detail: '',
      headline: 'No answer',
      level: 'unknown',
    },
  };
};

/**
 * "9:36", or nothing for a length the provider did not give.
 */
export const clock = (seconds) => {
  if (seconds == null || !Number.isFinite(seconds) || seconds <= 0) return '';
  const whole = Math.round(seconds);

  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, '0')}`;
};
