import * as utils from './util';

describe('getFileExtension', () => {
  it('reads the extension off a remote path', () => {
    expect(utils.getFileExtension('@@music\\Artist\\01 Track.FLAC')).toBe(
      'flac',
    );
    expect(utils.getFileExtension('/home/x/y/track.mp3')).toBe('mp3');
  });

  it('reads it from the name, never from the folder', () => {
    // a peer's folder routinely carries a dot -- and a format in brackets that
    // the file itself may not agree with
    expect(utils.getFileExtension('@@x\\Album (1998) [FLAC]\\track')).toBe('');
    expect(utils.getFileExtension('/x/v1.5/track.aiff')).toBe('aiff');
  });

  it('has nothing to say about a name without one', () => {
    expect(utils.getFileExtension('track')).toBe('');
    expect(utils.getFileExtension('track.')).toBe('');
    expect(utils.getFileExtension('.sync')).toBe('');
    expect(utils.getFileExtension('')).toBe('');
    expect(utils.getFileExtension(undefined)).toBe('');
  });

  it('takes the last dot, not the first', () => {
    expect(utils.getFileExtension('01. Artist - Track.mp3')).toBe('mp3');
  });
});

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
    new Date(afternoon).toLocaleString(utils.locale(), {
      ...options,
      timeZone: 'UTC',
    });

  // the locale is module state, so a test that sets it must put it back
  afterEach(() => utils.setLocale(undefined));

  it('writes the day first and the clock as 00-23', () => {
    // the 14th, deliberately: a day past 12 cannot be read as a month, so this
    // fails rather than passes ambiguously if the order goes back to US
    expect(inUtc(utils.DATE_TIME_OPTIONS)).toBe('14/09/2026, 15:04:00');
  });

  it('does not follow the browser unless told to', () => {
    // the reversal worth having a test for: date order and clock both come
    // from the locale, so following the browser means accepting whatever its
    // *language* implies -- 9/14/2026, 3:04 PM on an en-US one
    expect(utils.locale()).toBe(utils.DEFAULT_LOCALE);
    expect(utils.formatDate(afternoon)).toBe(
      new Date(afternoon).toLocaleString(
        utils.locale(),
        utils.DATE_TIME_OPTIONS,
      ),
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
    const out = new Date(justAfterMidnight).toLocaleTimeString(utils.locale(), {
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

  it('takes the locale the deployment configured', () => {
    utils.setLocale('en-US');

    expect(
      new Date(afternoon).toLocaleString(utils.locale(), {
        ...utils.DATE_TIME_OPTIONS,
        timeZone: 'UTC',
      }),
    ).toBe('9/14/2026, 15:04:00');
  });

  it('keeps the 24-hour clock whatever locale is configured', () => {
    // the clock is applied on top of the locale rather than inherited from it,
    // so choosing US date order does not bring AM/PM back with it
    utils.setLocale('en-US');

    expect(utils.formatDate(afternoon)).not.toMatch(/[AP]M/u);
  });

  it('follows the browser when the setting is deliberately blank', () => {
    // blank is a choice, and a different one from unset: undefined is how Intl
    // is told to use the browser's locale, so it cannot also mean "not yet
    // configured" -- which is what the resolver exists to keep apart
    utils.setLocale('');

    expect(utils.locale()).toBeUndefined();
  });

  it('renders every shape through the one locale', () => {
    // a formatter that forgot the locale argument falls back to the browser's,
    // which is the bug this file exists to prevent
    expect(utils.formatTime(afternoon)).toBe(
      new Date(afternoon).toLocaleTimeString(
        utils.locale(),
        utils.TIME_OPTIONS,
      ),
    );
    expect(utils.formatDayTime(afternoon)).toBe(
      new Date(afternoon).toLocaleString(
        utils.locale(),
        utils.DAY_TIME_OPTIONS,
      ),
    );
    expect(utils.formatHourMinute(afternoon)).toBe(
      new Date(afternoon).toLocaleTimeString(
        utils.locale(),
        utils.HOUR_MINUTE_OPTIONS,
      ),
    );
    expect(utils.formatDayMonth(afternoon)).toBe(
      new Date(afternoon).toLocaleDateString(
        utils.locale(),
        utils.DAY_MONTH_OPTIONS,
      ),
    );
  });
});

describe('formatDuration', () => {
  it.each([
    [0, '0s'],
    [45, '45s'],
    [45.9, '45s'],
    [60, '1m 00s'],
    [187, '3m 07s'],
    [3_599, '59m 59s'],
    [3_600, '1h 00m'],
    [7_500, '2h 05m'],
    [86_399, '23h 59m'],
    [86_400, '1d 00h'],
    [273_600, '3d 04h'],
  ])('%s seconds is %s', (seconds, expected) => {
    expect(utils.formatDuration(seconds)).toBe(expected);
  });

  it.each([
    ['negative', -1],
    ['not a number', Number.NaN],
    ['missing', undefined],
    ['null', null],
  ])('is blank for a duration that is %s', (_, seconds) => {
    expect(utils.formatDuration(seconds)).toBe('');
  });
});

describe('formatWhen', () => {
  // written without a zone, so they are the runner's local time -- which is
  // what "today" is measured in
  it('gives only the time for something that happened today', () => {
    const now = Date.parse('2026-09-23T18:00:00');
    const at = new Date('2026-09-23T14:02:33');

    expect(utils.formatWhen(at, now)).toBe(utils.formatTime(at));
  });

  it('gives the day as well for anything earlier', () => {
    const now = Date.parse('2026-09-23T00:05:00');
    const at = new Date('2026-09-22T23:58:00');

    expect(utils.formatWhen(at, now)).toBe(utils.formatDayTime(at));
  });
});
