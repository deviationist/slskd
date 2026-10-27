import { useEffect, useRef } from 'react';

/*
 * Pull down at the top of a page and let go to refetch what it shows, in the
 * installed app.
 *
 * By hand, because the installed app has no gesture of its own: iOS standalone
 * offers only the rubber-band bounce, and a browser tab -- which has a native
 * one -- is left to it (see `isStandalone`).
 *
 * Refreshing is in place, not a reload. Each page says what refetching it means
 * with `usePullRefresh` (or `registerPullRefresh` from a class), and a pull runs
 * every one registered: a page can be more than one source -- the search list
 * with a search open over it -- and all of them are on screen. A page that
 * registers nothing has no pull at all, rather than one that reloads the app.
 *
 * The approach is hospex-watcher's (components/pull-to-refresh.tsx there),
 * where the timing was learned the hard way: the gesture is claimed on the
 * FIRST touchmove that carries any distance, or not at all. WebKit hands a
 * gesture to the compositor as soon as it reads it as a scroll, and every
 * touchmove after that is uncancelable -- spending a few pixels on working out
 * the axis is exactly the window in which it is lost.
 *
 * What differs here is the scroller. On a phone the document does not scroll;
 * `.pushable` does, and lists and sheets scroll inside that. So "at the top"
 * means every scroller between the finger and the page is at its top -- a pull
 * inside a chat history that is scrolled down is that history's to scroll.
 *
 * The dial is a plain element written to directly, outside React: a drag
 * repaints every frame, and nothing about it needs rendering.
 */

// how far the dial travels before letting go means "refresh"; dial pixels, so
// the finger goes further than this (see damp)
const TRIGGER = 64;

// the curve's asymptote: pull as hard as you like, the dial stops here
const MAX = 140;

// where the dial waits while the page refetches, and for how long at least:
// off the local network an answer can land in 40ms, which reads as a flicker
// rather than as an answer
const REST = 52;
const MIN_SPIN_MS = 450;

// finger travel before a move counts at all: a noise filter, not a dead zone
const AXIS = 3;

const damp = (travel) => MAX * (1 - Math.exp(-travel / MAX));

const sources = new Set();

/**
 * Says what a pull refetches while the caller is on screen.
 * @param {Function} refresh - Refetches; may return a promise, which the dial spins until.
 * @returns {Function} Unregisters it.
 */
export const registerPullRefresh = (refresh) => {
  sources.add(refresh);
  return () => sources.delete(refresh);
};

/**
 * `registerPullRefresh` for a function component, for as long as it is mounted.
 * Always calls the latest `refresh`, so it can close over state freely.
 * @param {Function} refresh - Refetches; may return a promise.
 */
export const usePullRefresh = (refresh) => {
  const latest = useRef(refresh);
  latest.current = refresh;

  useEffect(() => registerPullRefresh(() => latest.current()), []);
};

/**
 * Whether this is the installed app: iOS from the home screen, or any browser's
 * standalone display mode.
 * @returns {boolean} True in an installed app.
 */
export const isStandalone = () =>
  window.navigator?.standalone === true ||
  (window.matchMedia?.('(display-mode: standalone)').matches ?? false);

const scrolls = (element) =>
  /auto|scroll/u.test(getComputedStyle(element).overflowY) &&
  element.scrollHeight > element.clientHeight;

// every scroller from the touch up to the page is at its top
const atTop = (target) => {
  for (
    let element = target;
    element && element !== document.body;
    element = element.parentElement
  ) {
    if (scrolls(element) && element.scrollTop > 1) return false;
  }

  return window.scrollY <= 1;
};

// something is open over the page: the phone menu, a Semantic modal (its
// dimmer marks the body), or a sheet portaled to the body such as chat's
// conversation list
const covered = () =>
  document.documentElement.classList.contains('nav-open') ||
  document.body.classList.contains('dimmed') ||
  document.querySelector('.conversation-sheet-backdrop') !== null;

// every source at once; a failed refetch is its page's to report, the dial's
// job is only to stop
const refreshAll = () =>
  Promise.allSettled([...sources].map((refresh) => refresh()));

/**
 * Installs the gesture on the document.
 * @returns {Function} Removes the gesture and its dial.
 */
export const installPullToRefresh = () => {
  const dial = document.createElement('div');
  dial.className = 'pull-to-refresh';
  dial.setAttribute('aria-hidden', 'true');
  const icon = document.createElement('i');
  icon.className = 'sync alternate icon';
  dial.append(icon);
  document.body.append(dial);

  let gesture = null;
  let busy = false;

  // `ease` is for the moments the dial moves on its own (springing back,
  // settling at REST); during a drag a transition would lag the finger
  const paint = (distance, ease) => {
    const progress = Math.min(1, distance / TRIGGER);
    const transition = ease
      ? 'transform 240ms cubic-bezier(0.22, 1, 0.36, 1), opacity 240ms ease-out'
      : 'none';
    dial.style.transition = transition;
    dial.style.transform = `translate3d(-50%, ${distance}px, 0) scale(${0.7 + 0.3 * progress})`;
    dial.style.opacity = String(progress);
    dial.dataset.armed = distance >= TRIGGER ? 'true' : 'false';
    icon.style.transition = transition;
    icon.style.transform = `rotate(${progress * 180}deg)`;
  };

  // hang the dial from under the title bar, measured per gesture since the
  // bar's height includes the safe area
  const anchor = () => {
    const bar = document.querySelector('.top-bar');
    const bottom = bar ? bar.getBoundingClientRect().bottom : 0;
    dial.style.top = `${Math.round(bottom) - 36}px`;
  };

  const start = (event) => {
    gesture = null;
    if (busy || sources.size === 0) return;
    if (event.touches.length !== 1 || covered()) return;
    if (!atTop(event.target)) return;
    const touch = event.touches[0];
    gesture = { distance: 0, own: null, x: touch.clientX, y: touch.clientY };
    anchor();
  };

  const move = (event) => {
    const current = gesture;
    if (!current || busy) return;

    // a second finger is a pinch, not a pull
    if (event.touches.length !== 1) {
      gesture = null;
      if (current.own) paint(0, true);
      return;
    }

    const touch = event.touches[0];
    const dy = touch.clientY - current.y;
    const dx = touch.clientX - current.x;

    if (current.own === null) {
      if (Math.abs(dy) < AXIS && Math.abs(dx) < AXIS) return;

      // decided on this one move and never revisited (see the top of the
      // file): down at least as much as sideways is ours; up is the page's
      // scroll, sideways is a strip that scrolls sideways
      current.own = dy > 0 && Math.abs(dy) >= Math.abs(dx);
      if (!current.own) {
        gesture = null;
        return;
      }
    }

    // if the compositor beat us to it the move is uncancelable; the dial still
    // follows and the release still refreshes, only the bounce underneath stays
    if (event.cancelable) event.preventDefault();
    current.distance = damp(Math.max(0, dy - AXIS));
    paint(current.distance, false);
  };

  const end = () => {
    const current = gesture;
    gesture = null;
    if (!current?.own || busy) return;

    if (current.distance < TRIGGER) {
      paint(0, true);
      return;
    }

    busy = true;
    icon.classList.add('loading');
    paint(REST, true);

    (async () => {
      const started = Date.now();
      await refreshAll();

      const left = MIN_SPIN_MS - (Date.now() - started);
      if (left > 0) {
        await new Promise((resolve) => {
          setTimeout(resolve, left);
        });
      }

      busy = false;
      icon.classList.remove('loading');
      paint(0, true);
    })();
  };

  // on the document, so a pull that starts on the title bar counts as well;
  // non-passive touchmove, since claiming the gesture means preventing it
  document.addEventListener('touchstart', start, { passive: true });
  document.addEventListener('touchmove', move, { passive: false });
  document.addEventListener('touchend', end, { passive: true });
  document.addEventListener('touchcancel', end, { passive: true });

  return () => {
    document.removeEventListener('touchstart', start);
    document.removeEventListener('touchmove', move);
    document.removeEventListener('touchend', end);
    document.removeEventListener('touchcancel', end);
    dial.remove();
  };
};
