import { urlBase } from '../config';

// a production build names its bundle after its contents (static/js/main.<hash>.js), so a page running one name
// while the server hands out another is a page running an old build
const BUNDLE = /static\/js\/(main\.[\da-z]+\.js)/u;

// often enough that an open tab notices a deploy within minutes, rarely enough to cost nothing; coming back to the
// app (an installed one is usually resumed, not reopened) checks at once as well
const CHECK_INTERVAL = 5 * 60_000;

/**
 * The bundle named in a page's HTML.
 * @param {string} html - An index.html, or any text holding a script reference.
 * @returns {string|null} The bundle's file name, or null when there is none.
 */
export const bundleIn = (html) => BUNDLE.exec(html ?? '')?.[1] ?? null;

/**
 * The bundle this page is running, from its own script tag.
 * @returns {string|null} The file name, or null in a development build, which has no hashed bundle.
 */
export const runningBundle = () =>
  bundleIn(
    [...document.querySelectorAll('script[src]')]
      .map((script) => script.getAttribute('src'))
      .join('\n'),
  );

/**
 * The bundle the server hands out now, read from a fresh copy of index.html.
 *
 * Only a real answer counts: a proxy's login page, an error or no network at all is not news of a new build, and
 * reading one as such would offer an update that reloads into the same page.
 * @returns {Promise<string|null>} The file name, or null when it could not be told.
 */
export const servedBundle = async () => {
  try {
    const response = await fetch(`${urlBase}/`, { cache: 'no-store' });

    if (!response.ok || response.redirected) {
      return null;
    }

    return bundleIn(await response.text());
  } catch {
    return null;
  }
};

/**
 * Calls `onUpdate` once, when the server starts handing out a different build from the one this page runs.
 * @param {Function} onUpdate - Called with no arguments when a new build is out.
 * @returns {object} `check`, to look now (after the hub reconnects, say: a deploy restarts slskd), and `stop`.
 */
export const watchForUpdate = (onUpdate) => {
  const running = runningBundle();

  if (running === null) {
    return { check: () => {}, stop: () => {} };
  }

  // stopping is aborting: the listener and the timer both go with it
  const controller = new AbortController();
  const stop = () => controller.abort();

  // visibilitychange, focus and pageshow tend to arrive together when the app
  // comes back, and one look is enough for all three
  let lastCheck = 0;

  const check = async () => {
    if (Date.now() - lastCheck < 2_000) return;
    lastCheck = Date.now();

    const served = await servedBundle();

    if (!controller.signal.aborted && served !== null && served !== running) {
      stop();
      onUpdate();
    }
  };

  // Whenever the app is opened or comes back. Opening usually loads the newest
  // page anyway, but an installed app on iOS is as often resumed from memory as
  // loaded, and that is a page that may be any age: so a look at start, and
  // again on each of the ways a page can return -- shown again (iOS switching
  // back to it), focused again (a desktop window), or restored from the
  // back-forward cache.
  const { signal } = controller;
  document.addEventListener(
    'visibilitychange',
    () => document.visibilityState === 'visible' && check(),
    { signal },
  );
  window.addEventListener('focus', check, { signal });
  window.addEventListener('pageshow', check, { signal });
  check();

  const timer = window.setInterval(check, CHECK_INTERVAL);
  controller.signal.addEventListener('abort', () =>
    window.clearInterval(timer),
  );

  return { check, stop };
};

/**
 * Loads the new build. Nothing of the app is cached by the service worker, and index.html is served `no-cache`, so a
 * reload is all an update is.
 */
export const applyUpdate = () => window.location.reload();
