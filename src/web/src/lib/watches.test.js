import * as watches from './watches';

describe('rruleFor / presetFor', () => {
  it('builds a rule for a preset that takes no hour', () => {
    expect(watches.rruleFor({ key: 'every6' })).toBe('FREQ=HOURLY;INTERVAL=6');
  });

  it('builds a rule for a preset that takes an hour', () => {
    expect(watches.rruleFor({ hour: 9, key: 'daily' })).toBe(
      'FREQ=DAILY;BYHOUR=9;BYMINUTE=0',
    );
  });

  it('knows nothing of a preset that does not exist', () => {
    expect(watches.rruleFor({ key: 'fortnightly' })).toBeUndefined();
  });

  it.each(watches.PRESETS.map((p) => [p.key]))(
    'reads back the %s preset it just wrote',
    (key) => {
      // the interface stores a rule, not a choice, so editing a watch has to
      // recover the control it came from -- for all of them, not just the first
      const rrule = watches.rruleFor({ hour: 9, key });

      expect(watches.presetFor(rrule).key).toBe(key);
    },
  );

  it.each([0, 3, 9, 23])('reads back the hour %i it just wrote', (hour) => {
    expect(
      watches.presetFor(watches.rruleFor({ hour, key: 'daily' })).hour,
    ).toBe(hour);
  });

  it('does not pretend a rule it cannot express is one of its own', () => {
    // the server accepts any valid rule; one set through the API must not read
    // back as a preset that would quietly rewrite it
    expect(watches.presetFor('FREQ=MONTHLY;BYMONTHDAY=1')).toBeUndefined();
    expect(watches.presetFor('')).toBeUndefined();
    expect(watches.presetFor(undefined)).toBeUndefined();
  });
});

describe('describeRecurrence', () => {
  it('describes a preset without an hour', () => {
    expect(watches.describeRecurrence('FREQ=HOURLY;INTERVAL=6')).toBe(
      'Every 6 hours',
    );
  });

  it('describes a preset with its hour, zero padded', () => {
    expect(watches.describeRecurrence('FREQ=DAILY;BYHOUR=3;BYMINUTE=0')).toBe(
      'Once a day, at 03:00',
    );
  });

  it('shows a rule it does not recognise rather than guessing', () => {
    expect(watches.describeRecurrence('FREQ=MONTHLY')).toBe(
      'Custom: FREQ=MONTHLY',
    );
  });

  it('says so when there is no schedule at all', () => {
    expect(watches.describeRecurrence(undefined)).toBe('No schedule');
  });
});

const at = (iso) => ({ enabled: true, nextRunAt: iso });

describe('describeNextRun', () => {
  const now = new Date('2026-09-11T12:00:00Z');

  it('says a paused watch is paused, whatever it is scheduled for', () => {
    expect(
      watches.describeNextRun({
        now,
        watch: { enabled: false, nextRunAt: '2026-09-11T13:00:00Z' },
      }),
    ).toBe('Paused');
  });

  it('counts minutes under an hour', () => {
    expect(
      watches.describeNextRun({ now, watch: at('2026-09-11T12:30:00Z') }),
    ).toBe('in 30 min');
  });

  it('counts hours under two days', () => {
    expect(
      watches.describeNextRun({ now, watch: at('2026-09-12T09:00:00Z') }),
    ).toBe('in 21 h');
  });

  it('counts days beyond that', () => {
    expect(
      watches.describeNextRun({ now, watch: at('2026-09-15T12:00:00Z') }),
    ).toBe('in 4 days');
  });

  it('does not report a due watch as late', () => {
    // it runs on the next tick, or when the server comes back; neither is a
    // fault, and calling it overdue would invite someone to go fixing it
    expect(
      watches.describeNextRun({ now, watch: at('2026-09-11T11:00:00Z') }),
    ).toBe('Due now');
  });

  it('says when a watch has no schedule', () => {
    expect(watches.describeNextRun({ now, watch: { enabled: true } })).toBe(
      'Not scheduled',
    );
  });
});

describe('watchBadge', () => {
  it('says nothing about a search that is not watched', () => {
    expect(watches.watchBadge({ watch: undefined })).toBeUndefined();
  });

  it('marks a watching search', () => {
    expect(watches.watchBadge({ watch: { enabled: true } }).label).toBe(
      'Watching',
    );
  });

  it('marks a paused one', () => {
    expect(watches.watchBadge({ watch: { enabled: false } }).label).toBe(
      'Paused',
    );
  });

  it('lets a failing send outrank everything else', () => {
    // a watch whose mail is bouncing looks exactly like one that has found
    // nothing, which is the confusion worth spending the badge on
    const badge = watches.watchBadge({
      notifications: [{ sent: false }],
      watch: { enabled: true },
    });

    expect(badge.label).toBe('Mail failing');
    expect(badge.color).toBe('red');
  });

  it('reads only the newest notification', () => {
    const badge = watches.watchBadge({
      notifications: [{ sent: true }, { sent: false }],
      watch: { enabled: true },
    });

    expect(badge.label).toBe('Watching');
  });
});

describe('validateDraft', () => {
  const searchText = 'aphex twin';

  it('accepts a draft with a preset and no address', () => {
    expect(
      watches.validateDraft({ hour: 3, key: 'daily', searchText }).ok,
    ).toBe(true);
  });

  it('refuses one with no schedule', () => {
    expect(watches.validateDraft({ searchText }).ok).toBe(false);
  });

  it('refuses one with no phrase to search for', () => {
    // the phrase can be edited here while a watch is being created, so it can
    // be emptied here; the refusal is the server's own wording, the same one
    // the search buttons answer with
    const result = watches.validateDraft({ key: 'daily', searchText: '  ' });

    expect(result.ok).toBe(false);
    expect(result.reason).toContain('SearchText');
  });

  it('accepts a blank address, which means the configured one', () => {
    expect(
      watches.validateDraft({ key: 'daily', notifyEmail: '   ', searchText })
        .ok,
    ).toBe(true);
  });

  it('refuses an address that is not one', () => {
    // a watch that cannot deliver reports nothing and says nothing, so a typo
    // here is worth catching before it is saved
    const result = watches.validateDraft({
      key: 'daily',
      notifyEmail: 'someone@',
      searchText,
    });

    expect(result.ok).toBe(false);
    expect(result.reason).toMatch(/email/iu);
  });
});

describe('filesFrom', () => {
  const two = JSON.stringify([
    { filename: 'a.flac', size: 1, username: 'one' },
    { filename: 'b.flac', size: 2, username: 'two' },
  ]);

  it('reads the files a notification recorded', () => {
    const read = watches.filesFrom({ fileCount: 2, filesJson: two });

    expect(read.files).toHaveLength(2);
    expect(read.truncated).toBe(false);
  });

  it('is honest when the record holds fewer than were reported', () => {
    // a mail that reported 1471 files records a couple of hundred; the log has
    // to say so rather than implying the rest were never sent
    const read = watches.filesFrom({ fileCount: 1_471, filesJson: two });

    expect(read.shown).toBe(2);
    expect(read.total).toBe(1_471);
    expect(read.truncated).toBe(true);
  });

  it('reads nothing rather than throwing on a record it cannot parse', () => {
    // one unreadable row must not take the rest of the log down with it
    expect(watches.filesFrom({ filesJson: 'not json' }).files).toEqual([]);
    expect(watches.filesFrom({}).files).toEqual([]);
    expect(watches.filesFrom(undefined).files).toEqual([]);
  });
});
