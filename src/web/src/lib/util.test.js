import * as utils from './util';

describe('formatBytesAsUnit', () => {
  it('converts bytes to specified unit', () => {
    expect(utils.formatBytesAsUnit(1_234_567, 'MB', 2)).toBe(1.18);
  });
});

describe('formatBytes', () => {
  it('returns 0 B for values under one byte', () => {
    expect(utils.formatBytes(0.5)).toBe('0 B');
  });

  it('formats byte values for one and above', () => {
    expect(utils.formatBytes(1)).toBe('1 B');
    expect(utils.formatBytes(1_024)).toBe('1 KB');
  });
});

describe('date and time formatting', () => {
  // 15:04 UTC: a time that reads differently on a 12- and a 24-hour clock
  const at = '2026-09-14T15:04:00Z';

  it('formats in the runtime locale rather than a named one', () => {
    expect(utils.formatDayTime(at)).toBe(
      new Date(at).toLocaleString(undefined, utils.DAY_TIME_OPTIONS),
    );
  });

  it('leaves the choice of clock to the locale', () => {
    // the bug this replaces: chat and room timestamps were built with
    // `new Intl.DateTimeFormat('en', ...)`, so a reader whose locale uses a
    // 24-hour clock was shown AM/PM anyway. these two must disagree, or
    // something in the options is pinning the clock instead of the locale
    const us = new Date(at).toLocaleString('en-US', utils.DAY_TIME_OPTIONS);
    const gb = new Date(at).toLocaleString('en-GB', utils.DAY_TIME_OPTIONS);

    expect(us).not.toBe(gb);
    expect(us).toMatch(/PM/u);
    expect(gb).not.toMatch(/PM/u);
  });

  it('gives a time on its own, and a full date and time', () => {
    expect(utils.formatTime(at)).toBe(new Date(at).toLocaleTimeString());
    expect(utils.formatDate(at)).toBe(new Date(at).toLocaleString());
  });
});
