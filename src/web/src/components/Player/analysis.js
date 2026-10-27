import { getAnalysis } from '../../lib/player/api';
import { useEffect, useState } from 'react';

/*
 * One answer per download, shared by the bar and every row that shows it.
 *
 * Rows ask without waiting: the server says `pending` at once and keeps the
 * provider working, and the row asks again when told to. The bar waits, because
 * it is one request about the track being listened to. Either way an answer is
 * kept here for the page's life, so scrolling a list back and forth is not a
 * request per row per scroll.
 */

const answers = new Map();
const listeners = new Map();
const asking = new Set();

const publish = (id, answer) => {
  answers.set(id, answer);

  for (const listener of listeners.get(id) ?? []) {
    listener(answer);
  }
};

const settled = (answer) =>
  answer && answer.state !== 'pending' && answer.state !== 'unavailable';

const ask = async (id, wait) => {
  if (asking.has(id)) {
    return;
  }

  asking.add(id);

  try {
    publish(id, await getAnalysis(id, { wait }));
  } catch (error) {
    // a download that has gone, or was never playable, has nothing to show and
    // nothing to ask again about
    const status = error?.response?.status;
    publish(
      id,
      status === 404 || status === 403 || status === 400
        ? { state: 'none' }
        : { retryAfter: 30, state: 'unavailable' },
    );
  } finally {
    asking.delete(id);
  }
};

export const useAnalysis = (id, { enabled = true, wait = false } = {}) => {
  const [answer, setAnswer] = useState(() => answers.get(id) ?? null);

  useEffect(() => {
    if (!id || !enabled) {
      setAnswer(null);
      return undefined;
    }

    setAnswer(answers.get(id) ?? null);
    const set = listeners.get(id) ?? new Set();
    set.add(setAnswer);
    listeners.set(id, set);

    if (!settled(answers.get(id))) {
      ask(id, wait);
    }

    return () => {
      set.delete(setAnswer);
    };
  }, [enabled, id, wait]);

  // ask again when told to, for as long as something is showing it
  useEffect(() => {
    if (!id || !enabled || settled(answer) || !answer?.retryAfter) {
      return undefined;
    }

    const timer = setTimeout(() => ask(id, wait), answer.retryAfter * 1_000);

    return () => clearTimeout(timer);
  }, [answer, enabled, id, wait]);

  return answer;
};

export const forgetAnalysis = (id) => answers.delete(id);
