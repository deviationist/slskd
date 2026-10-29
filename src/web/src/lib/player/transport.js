/*
 * The player's rules that do not need a DOM: the waveform's shape, the clock,
 * the keyboard, scrubbing, and which shape the player takes.
 */

// how far the arrow keys move; ten seconds is short enough to land on a
// section and long enough that crossing a six-minute track is not forty presses
export const SEEK_STEP = 10;

// how long the drawer takes to fold; the CSS animation says the same
export const FOLD_MS = 200;

// how far the drawer has to be pulled before letting go folds it
export const FOLD_PULL = 80;

// how far a mouse may move on the handle and still have clicked it
export const GRAB_CLICK = 4;

// how often a scrub sends the audio to the finger: long enough for a moment of
// each place to be heard, short enough that the sound keeps up. a seek per
// move would be sixty a second and none of them would get far enough to sound
export const SCRUB_STEP_MS = 250;

// how far a release may be from the last place the audio was sent and still be
// that place. a click is a press and a release at the same spot, and seeking
// again on the release re-buffered for nothing -- it stuttered on every click
export const SCRUB_RELEASE_S = 0.05;

// the viewBox the waveform is drawn in
export const WAVE_WIDTH = 900;
export const WAVE_HEIGHT = 56;

export const clock = (seconds) => {
  const value = Number.isFinite(seconds) && seconds >= 0 ? seconds : 0;

  return `${Math.floor(value / 60)}:${String(Math.floor(value % 60)).padStart(2, '0')}`;
};

/*
 * The waveform as one path, not sixteen hundred bars: the peaks come at 1600
 * buckets, finer than the bar is wide, and an element each would be a thousand
 * nodes redrawn on every resize. Sampled to the width and mirrored about the
 * centre line.
 */
export const outline = (peaks, width = WAVE_WIDTH, height = WAVE_HEIGHT) => {
  if (!peaks?.length) {
    return '';
  }

  const mid = height / 2;
  const step = Math.max(1, Math.floor(peaks.length / width));
  const buckets = Math.ceil(peaks.length / step);
  const top = [];
  const bottom = [];

  for (let index = 0, x = 0; index < peaks.length; x++, index += step) {
    let peak = 0;

    for (
      let inner = index;
      inner < Math.min(index + step, peaks.length);
      inner++
    ) {
      peak = Math.max(peak, peaks[inner] ?? 0);
    }

    const px = ((x / buckets) * width).toFixed(1);
    top.push(`${px},${(mid - peak * mid).toFixed(1)}`);
    bottom.unshift(`${px},${(mid + peak * mid).toFixed(1)}`);
  }

  return `M${top.join('L')}L${bottom.join('L')}Z`;
};

// whether a key press belongs to something being typed into. a focused button
// is not typing, which is what lets the arrows work while play has focus
export const typingIn = (element) => {
  if (!element) {
    return false;
  }

  return (
    ['INPUT', 'SELECT', 'TEXTAREA'].includes(element.tagName) ||
    element.isContentEditable === true
  );
};

// whether space would already do something where it is: the browser activates
// a focused button on space, so a player that also toggled would fire two
// actions from one press
export const activatesOnSpace = (element) => {
  if (!element) {
    return false;
  }

  return (
    ['A', 'BUTTON', 'INPUT', 'SUMMARY'].includes(element.tagName) ||
    element.getAttribute?.('role') === 'button'
  );
};

/*
 * What a key press asks of the player, or null for nothing.
 *
 * Space plays and pauses, the arrows move SEEK_STEP, 0-9 jump to tenths, and
 * Escape is the caller's to decide (innermost thing first). Nothing at all
 * while something is being typed into. Modifiers belong to
 * the browser and the OS: ctrl+arrow is a word jump, cmd+arrow is history.
 */
export const keyAction = (
  { key, code, ctrlKey, metaKey, altKey, target },
  { at, length },
) => {
  if (ctrlKey || metaKey || altKey) {
    return null;
  }

  // before Escape too: pressed in a search box it means "clear this", and the
  // inputs here do not say they took it
  if (typingIn(target)) {
    return null;
  }

  if (key === 'Escape') {
    return { type: 'escape' };
  }

  if (key === ' ' || code === 'Space') {
    return activatesOnSpace(target) ? null : { type: 'toggle' };
  }

  if (key === 'ArrowLeft' || key === 'ArrowRight') {
    const to = at + (key === 'ArrowRight' ? SEEK_STEP : -SEEK_STEP);

    return {
      to: Math.max(0, length ? Math.min(to, length) : to),
      type: 'seek',
    };
  }

  // needs a length: without one there is no tenth, and seeking to NaN silently
  // stops playback
  if (/^\d$/u.test(key) && length) {
    return { to: (Number(key) / 10) * length, type: 'seek' };
  }

  return null;
};

// where along a box a pointer is, 0..1
export const fractionAt = (clientX, box) =>
  box.width > 0
    ? Math.min(1, Math.max(0, (clientX - box.left) / box.width))
    : 0;

// whether a scrub's move should send the audio there now
export const scrubSeekDue = (now, lastSeekAt) =>
  now - lastSeekAt >= SCRUB_STEP_MS;

// whether letting go should seek again, or is the second half of a click
export const seekOnRelease = (fraction, lastSentFraction, length) =>
  lastSentFraction == null ||
  Math.abs(fraction - lastSentFraction) * length > SCRUB_RELEASE_S;

/*
 * Which shape the player takes. Wide is the bar; on a phone there is no room for
 * a waveform beside a title beside a meter -- squeezed into one row each came
 * out a sliver -- so it is a mini bar that says what is playing, opening into a
 * drawer with room for everything.
 */
export const modeFor = (phone, expanded) => {
  if (!phone) {
    return 'wide';
  }

  return expanded ? 'full' : 'mini';
};

// the playhead clamped to the track, so a clock can never read past the end of
// the very track it is in. only with a length: clamping to an unknown 0 would
// freeze the clock over audible playback
export const shownTime = (at, length) =>
  length > 0 ? Math.min(at, length) : at;
