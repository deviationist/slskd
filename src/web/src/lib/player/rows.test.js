import { extensionOf, isPlayable, trackOf } from './rows';

const row = (over = {}) => ({
  direction: 'Download',
  filename: '@@abc\\Music\\Album\\01 Track.aiff',
  id: '0b9a6c7e-1d2f-4a3b-8c9d-0e1f2a3b4c5d',
  state: 'Completed, Succeeded',
  username: 'somebody',
  ...over,
});

describe('which rows play', () => {
  it('offers a finished download of audio', () => {
    expect(isPlayable(row())).toBe(true);
    expect(isPlayable(row({ filename: 'x/y/Z.FLAC' }))).toBe(true);
  });

  it.each([
    ['still running', { state: 'InProgress' }],
    ['failed', { state: 'Completed, Errored' }],
    ['an upload', { direction: 'Upload' }],
    ['a cover', { filename: 'Album\\cover.jpg' }],
    ['no extension', { filename: 'Album\\README' }],
    ['no id', { id: undefined }],
  ])('offers nothing for a row that is %s', (_, over) => {
    expect(isPlayable(row(over))).toBe(false);
  });

  it('reads the extension of a Windows path and a Unix one alike', () => {
    expect(extensionOf('C:\\a.b\\track.Mp3')).toBe('mp3');
    expect(extensionOf('a.b/track')).toBe('');
    expect(extensionOf('.hidden')).toBe('');
  });

  it('holds the row by its id, titled by its file name', () => {
    expect(trackOf(row())).toEqual({
      artist: null,
      // so the player can ask the browser whether it plays the file as it is
      format: 'aiff',
      id: row().id,
      title: '01 Track.aiff',
      username: 'somebody',
    });
  });
});
