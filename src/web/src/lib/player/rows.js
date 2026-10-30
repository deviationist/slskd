/*
 * Which transfer rows the player offers to play.
 *
 * A finished download only: while one runs, its file is a partial in the
 * incomplete directory, and that is not the track. By extension too, so a
 * cover, a cue sheet or a log arriving with an album gets no button that could
 * only fail. The server decides again for itself (PlayableFile.cs); this is so
 * the page does not offer what the server will refuse.
 */

// the same set as Codecs.Playable on the server
export const PLAYABLE = new Set([
  'aac',
  'aif',
  'aiff',
  'alac',
  'ape',
  'flac',
  'm4a',
  'mp3',
  'ogg',
  'opus',
  'wav',
  'wma',
  'wv',
]);

// the extension of a remote filename, which is a Windows path as often as not
export const extensionOf = (filename) => {
  const name = String(filename ?? '')
    .split(/[/\\]/u)
    .pop();
  const dot = name.lastIndexOf('.');

  return dot > 0 ? name.slice(dot + 1).toLowerCase() : '';
};

export const fileNameOf = (filename) =>
  String(filename ?? '')
    .split(/[/\\]/u)
    .pop();

export const isPlayable = (file) =>
  Boolean(file?.id) &&
  file.direction === 'Download' &&
  file.state === 'Completed, Succeeded' &&
  PLAYABLE.has(extensionOf(file.filename));

// what the player holds for a row
export const trackOf = (file) => ({
  artist: null,
  format: extensionOf(file.filename),
  id: file.id,
  title: fileNameOf(file.filename),
  username: file.username ?? '',
});
