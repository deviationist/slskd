/*
 * Whether the browser plays a file's own format, so it can be sent as it is
 * rather than transcoded. Only the browser knows, so it is asked
 * (canPlayType), and its answer is held to the formats the server will send
 * as they are (Codecs.Originals): not AIFF, which no browser plays, and not
 * M4A, which may be ALAC, which Chrome does not.
 *
 * A "maybe" is taken at its word. Where it is wrong the file fails to load,
 * and the player falls back to a transcode for that track.
 */

const TYPES = {
  flac: 'audio/flac',
  mp3: 'audio/mpeg',
  ogg: 'audio/ogg',
  opus: 'audio/ogg; codecs=opus',
  wav: 'audio/wav',
};

export const playsOriginal = (element, format) => {
  const type = TYPES[String(format ?? '').toLowerCase()];

  if (!type || typeof element?.canPlayType !== 'function') {
    return false;
  }

  try {
    return element.canPlayType(type) !== '';
  } catch {
    return false;
  }
};
