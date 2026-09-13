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
  // rendered in UTC where the assertion names an exact string, so it does not
  // depend on the zone the test runner happens to be in
  const afternoon = '2026-09-14T15:04:00Z';
  const justAfterMidnight = '2026-09-14T00:30:00Z';

  const inUtc = (options) =>
    new Date(afternoon).toLocaleString(utils.LOCALE, {
      ...options,
      timeZone: 'UTC',
    });

  it('writes the day first and the clock as 00-23', () => {
    // the 14th, deliberately: a day past 12 cannot be read as a month, so this
    // fails rather than passes ambiguously if the order goes back to US
    expect(inUtc(utils.DATE_TIME_OPTIONS)).toBe('14/09/2026, 15:04:00');
  });

  it('does not follow the browser', () => {
    // the reversal worth having a test for: date order and clock both come
    // from the locale, so following the browser means accepting whatever its
    // *language* implies -- 9/14/2026, 3:04 PM on an en-US one
    expect(utils.LOCALE).toBe('en-GB');
    expect(utils.formatDate(afternoon)).toBe(
      new Date(afternoon).toLocaleString(utils.LOCALE, utils.DATE_TIME_OPTIONS),
    );
    expect(utils.formatDate(afternoon)).not.toMatch(/[AP]M/u);
  });

  it('keeps month names in English', () => {
    // en-GB rather than nb-NO, which gives the same day-first order and would
    // also put 'sep.' in an otherwise English UI.
    //
    // 'Sept', not 'Sep': en-GB abbreviates September to four letters where
    // en-US uses three, and September is the only month it does this to. It
    // shows up on the graph's axis ticks and nowhere else.
    expect(inUtc(utils.DAY_MONTH_OPTIONS)).toBe('14 Sept');
  });

  it('writes midnight as 00, not 24', () => {
    // 'h23' rather than `hour12: false`, which selects the h24 cycle in some
    // locales and renders the hour after midnight as 24:30
    const out = new Date(justAfterMidnight).toLocaleTimeString(utils.LOCALE, {
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

  it('renders every shape through the one locale', () => {
    // a formatter that forgot the locale argument falls back to the browser's,
    // which is the bug this file exists to prevent
    expect(utils.formatTime(afternoon)).toBe(
      new Date(afternoon).toLocaleTimeString(utils.LOCALE, utils.TIME_OPTIONS),
    );
    expect(utils.formatDayTime(afternoon)).toBe(
      new Date(afternoon).toLocaleString(utils.LOCALE, utils.DAY_TIME_OPTIONS),
    );
    expect(utils.formatHourMinute(afternoon)).toBe(
      new Date(afternoon).toLocaleTimeString(
        utils.LOCALE,
        utils.HOUR_MINUTE_OPTIONS,
      ),
    );
    expect(utils.formatDayMonth(afternoon)).toBe(
      new Date(afternoon).toLocaleDateString(
        utils.LOCALE,
        utils.DAY_MONTH_OPTIONS,
      ),
    );
  });
});
