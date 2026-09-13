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
  // rendered in UTC so the assertions do not depend on the runner's zone
  const afternoon = '2026-09-14T15:04:00Z';
  const justAfterMidnight = '2026-09-14T00:30:00Z';

  const render = (locale, options) =>
    new Date(afternoon).toLocaleString(locale, { ...options, timeZone: 'UTC' });

  it('shows a 24-hour clock whichever locale the reader has', () => {
    // the point of pinning it: a browser takes AM/PM from its language, which
    // is not the reader's clock setting and is not reachable from the app
    expect(render('en-US', utils.DATE_TIME_OPTIONS)).toContain('15:04');
    expect(render('en-US', utils.DATE_TIME_OPTIONS)).not.toMatch(/[AP]M/u);
    expect(render('en-GB', utils.DATE_TIME_OPTIONS)).not.toMatch(/[AP]M/u);
  });

  it('leaves everything but the clock to the locale', () => {
    // en-US writes the month first and en-GB the day: pinning the clock must
    // not have quietly pinned the rest of the format with it
    expect(render('en-US', utils.DATE_TIME_OPTIONS)).not.toBe(
      render('en-GB', utils.DATE_TIME_OPTIONS),
    );
  });

  it('writes midnight as 00, not 24', () => {
    // 'h23' rather than `hour12: false`, which selects the h24 cycle in some
    // locales and renders the hour after midnight as 24:30
    const out = new Date(justAfterMidnight).toLocaleTimeString('en-GB', {
      ...utils.TIME_OPTIONS,
      timeZone: 'UTC',
    });

    expect(out).toMatch(/^00:30/u);
  });

  it('pins the same clock in every shape a moment is rendered in', () => {
    const shapes = [
      utils.DATE_TIME_OPTIONS,
      utils.TIME_OPTIONS,
      utils.DAY_TIME_OPTIONS,
      utils.HOUR_MINUTE_OPTIONS,
    ];

    for (const options of shapes) {
      expect(options.hourCycle).toBe(utils.HOUR_CYCLE);

      // `hour12` and `hourCycle` are mutually exclusive and hour12 wins
      // silently, so its presence would undo the line above without failing
      expect(options).not.toHaveProperty('hour12');
    }
  });

  it('formats in the runtime locale rather than a named one', () => {
    expect(utils.formatDate(afternoon)).toBe(
      new Date(afternoon).toLocaleString(undefined, utils.DATE_TIME_OPTIONS),
    );
    expect(utils.formatTime(afternoon)).toBe(
      new Date(afternoon).toLocaleTimeString(undefined, utils.TIME_OPTIONS),
    );
  });
});
