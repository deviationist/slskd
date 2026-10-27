/*
 * The output meter's arithmetic, its tap on the audio, and its drawing.
 *
 * Twin: media-bridge web/app/src/features/plex/meter.ts. A fix to one belongs
 * in the other.
 *
 * It must not decide whether the file clips. What it reads is the transcoded
 * stream, and a lossy decoder reconstructs a waveform that need not stay inside
 * the source's peak envelope: a file peaking at -0.21 dBFS with zero samples at
 * full scale came back from Opus at +1.44 dBFS with 19,508 of them. So the red
 * zone means loud, and whether the file clips is the analysis's answer, from
 * the original samples.
 *
 * A linear dB scale is unreadable at the top: -60..0 mapped straight to height
 * puts -6 dB at 90% and leaves red the top 1.7%, under a pixel.
 */

export const METER_AMBER = -6;
export const METER_RED = -1;
export const METER_FLOOR = -60;

// green the bottom 70%, amber the next 18%, red the top 12%. the bottom is the
// part compressed, which costs nothing: nobody judges -55 against -45 dB on a
// bar forty pixels tall
const SCALE = [
  [-60, 0],
  [-40, 0.16],
  [-24, 0.4],
  [-12, 0.6],
  [-6, 0.7],
  [-3, 0.8],
  [-1, 0.88],
  [0, 1],
];

// how fast a held peak falls, in dB per frame at 60 fps: about 40 dB/s
export const METER_FALL = 0.7;

// how long the clip light stays bright after the playhead crosses clipping; a
// light that only blinked would say nothing to anyone who glanced away
export const CLIP_BLINK_MS = 180;

export const METER_GREEN = '#199c66';
export const METER_AMBER_C = '#c9930f';
export const METER_RED_C = '#db2828';
export const METER_HOLD_GREEN = '#8fdcb8';
export const METER_HOLD_RED = '#ff4d4d';

export const dbfs = (value) =>
  value > 0 ? Math.max(METER_FLOOR, 20 * Math.log10(value)) : METER_FLOOR;

export const meterY = (db) => {
  if (db <= SCALE[0][0]) {
    return 0;
  }

  for (let index = 1; index < SCALE.length; index++) {
    const [loDb, loY] = SCALE[index - 1];
    const [hiDb, hiY] = SCALE[index];

    if (db <= hiDb) {
      return loY + (hiY - loY) * ((db - loDb) / (hiDb - loDb));
    }
  }

  return 1;
};

// a held peak that falls back rather than snapping: the eye cannot follow a bar
// that tracks every frame, and the number worth seeing is the recent maximum
export const holdPeak = (db, held) =>
  db >= held ? db : Math.max(db, held - METER_FALL);

/*
 * Split the element's output into two analysers and put it back together.
 *
 * Every node sits on the path to the destination on purpose: a branch that
 * dead-ends is not guaranteed to be pulled, and an analyser never pulled reads
 * silence for ever. Null rather than throwing: the likeliest failure is an
 * element that already has a source (there can only be one), and a missing
 * meter is a blemish where audio routed into a graph that then threw is
 * silence.
 */
export const tapAudio = (element, context) => {
  try {
    const source = context.createMediaElementSource(element);
    const split = context.createChannelSplitter(2);
    const merge = context.createChannelMerger(2);
    const analysers = [context.createAnalyser(), context.createAnalyser()];

    source.connect(split);

    for (const [index, tap] of analysers.entries()) {
      // 1024 samples is ~21 ms, longer than a 60 fps frame, so consecutive
      // reads overlap and no peak falls between them
      tap.fftSize = 1_024;
      split.connect(tap, index);
      tap.connect(merge, 0, index);
    }

    merge.connect(context.destination);

    return { analysers, buffer: new Float32Array(1_024) };
  } catch {
    return null;
  }
};

// the peak of what is coming out, per channel, 0..1
export const readLevels = (taps) =>
  taps.analysers.map((tap) => {
    tap.getFloatTimeDomainData(taps.buffer);
    let peak = 0;

    for (const value of taps.buffer) {
      peak = Math.max(peak, Math.abs(value));
    }

    return peak;
  });

// where the playhead is in a lane, or 0
export const laneAt = (lane, fraction) => {
  if (!lane?.length) {
    return 0;
  }

  const index = Math.floor(
    Math.max(0, Math.min(0.999_999, fraction)) * lane.length,
  );

  return lane[index] ?? 0;
};

/*
 * What the clip light says: 'here' just after the playhead crossed clipping,
 * 'somewhere' if the file clips anywhere, 'none' if it does not. From the
 * analysis, never from the meter's own reading (see the top of this file).
 */
export const clipState = (lane, sinceMs) => {
  if (!lane?.length) {
    return 'none';
  }

  return sinceMs < CLIP_BLINK_MS ? 'here' : 'somewhere';
};

/*
 * Paint the meter: a ladder, green to the amber threshold, amber to the red,
 * red above it, a peak-hold line, and a clip light on top. `colours` carries
 * the theme's two neutrals (the empty bar and the zone rules), which must read
 * on a white bar and a black one alike.
 */
export const drawMeter = (context, width, height, paint, colours = {}) => {
  const empty = colours.empty ?? 'rgba(255,255,255,.06)';
  const dark = colours.light ?? 'rgba(255,255,255,.07)';
  const rule = colours.rule ?? 'rgba(22,24,29,.85)';
  const led = 7;
  const gap = 3;
  const top = led + gap;
  const barHeight = height - top;
  const barWidth = 13;
  const barGap = 4;
  const x0 = Math.floor((width - (barWidth * 2 + barGap)) / 2);

  context.clearRect(0, 0, width, height);

  context.fillStyle =
    paint.clip === 'here'
      ? METER_HOLD_RED
      : paint.clip === 'somewhere'
        ? 'rgba(219,40,40,.42)'
        : dark;
  context.fillRect(x0, 0, barWidth * 2 + barGap, led);

  const amberY = top + barHeight * (1 - meterY(METER_AMBER));
  const redY = top + barHeight * (1 - meterY(METER_RED));

  for (let index = 0; index < 2; index++) {
    const x = x0 + index * (barWidth + barGap);

    context.fillStyle = empty;
    context.fillRect(x, top, barWidth, barHeight);

    const level = top + barHeight * (1 - meterY(paint.levels[index]));

    // three fills rather than a gradient: the boundary between amber and red
    // is a threshold, and a gradient would blur the line the meter exists for
    context.fillStyle = METER_GREEN;
    context.fillRect(
      x,
      Math.max(level, amberY),
      barWidth,
      top + barHeight - Math.max(level, amberY),
    );

    if (level < amberY) {
      context.fillStyle = METER_AMBER_C;
      context.fillRect(
        x,
        Math.max(level, redY),
        barWidth,
        amberY - Math.max(level, redY),
      );
    }

    if (level < redY) {
      context.fillStyle = METER_RED_C;
      context.fillRect(x, level, barWidth, redY - level);
    }

    const held = paint.holds[index];

    if (held > METER_FLOOR) {
      const hold = top + barHeight * (1 - meterY(held));
      context.fillStyle =
        held >= METER_RED
          ? METER_HOLD_RED
          : held >= METER_AMBER
            ? METER_AMBER_C
            : METER_HOLD_GREEN;
      context.fillRect(x, Math.min(top + barHeight - 2, hold), barWidth, 2);
    }
  }

  // the zone boundaries over the bars, so the scale reads in silence too
  context.fillStyle = rule;
  context.fillRect(x0, amberY, barWidth * 2 + barGap, 1);
  context.fillRect(x0, redY, barWidth * 2 + barGap, 1);
};
