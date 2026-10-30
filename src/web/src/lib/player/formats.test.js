import { playsOriginal } from './formats';

// a browser that plays what it is told it plays
const browser = (plays) => ({
  canPlayType: (type) => (plays.includes(type) ? 'maybe' : ''),
});

describe('playsOriginal', () => {
  it('sends a FLAC as it is to a browser that plays FLAC', () => {
    expect(playsOriginal(browser(['audio/flac']), 'flac')).toBe(true);
    expect(playsOriginal(browser(['audio/flac']), 'FLAC')).toBe(true);
  });

  it('transcodes for a browser that does not', () => {
    expect(playsOriginal(browser([]), 'flac')).toBe(false);
  });

  it('never offers AIFF or M4A as they are, whatever the browser says', () => {
    const anything = { canPlayType: () => 'probably' };

    expect(playsOriginal(anything, 'aiff')).toBe(false);
    expect(playsOriginal(anything, 'aif')).toBe(false);
    expect(playsOriginal(anything, 'm4a')).toBe(false);
  });

  it('transcodes when there is nothing to ask, or no format', () => {
    expect(playsOriginal(null, 'flac')).toBe(false);
    expect(playsOriginal({}, 'flac')).toBe(false);
    expect(playsOriginal(browser(['audio/flac']), '')).toBe(false);
    expect(playsOriginal(browser(['audio/flac']), undefined)).toBe(false);
  });
});
