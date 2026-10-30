import {
  arrange,
  clock,
  compareWhy,
  isComparable,
  isEnabled,
  MAXIMUM_FILES,
  rowsOf,
} from './compare';

const done = (name, extra = {}) => ({
  direction: 'Download',
  filename: `@@peer\\Album\\${name}`,
  id: name,
  localFilename: `/downloads/Album/${name}`,
  state: 'Completed, Succeeded',
  ...extra,
});

describe('isEnabled', () => {
  it('follows the source the server says is in effect', () => {
    const compare = (settings) =>
      isEnabled({ transfers: { compare: settings } });

    expect(compare({ usesBuiltin: true, usesProvider: false })).toBe(true);
    expect(compare({ usesBuiltin: false, usesProvider: true })).toBe(true);
    // a provider named but not chosen, or chosen with no address: off
    expect(compare({ usesBuiltin: false, usesProvider: false })).toBe(false);
    // options arrive over the hub; before they do, nothing is offered
    expect(isEnabled(undefined)).toBe(false);
  });
});

describe('isComparable', () => {
  it('takes a finished audio download that still has its file', () => {
    expect(isComparable(done('01 Mix.flac'))).toBe(true);
  });

  it('refuses what the server would refuse', () => {
    expect(isComparable(done('cover.jpg'))).toBe(false);
    expect(isComparable(done('01.flac', { state: 'InProgress' }))).toBe(false);
    expect(isComparable(done('01.flac', { localFilename: null }))).toBe(false);
    expect(isComparable(done('01.flac', { localFileExists: false }))).toBe(
      false,
    );
    expect(isComparable(done('01.flac', { direction: 'Upload' }))).toBe(false);
  });
});

describe('compareWhy', () => {
  it('asks for two or more', () => {
    expect(compareWhy([done('a.flac')])).toMatchObject({ on: false });
    expect(compareWhy([done('a.flac')]).tip).toMatch(/^Tick two or more/u);
  });

  it('counts what has not finished', () => {
    expect(
      compareWhy([done('a.flac'), done('b.flac', { state: 'InProgress' })]).tip,
    ).toBe(
      '2 ticked, but only 1 has finished downloading — only finished downloads can be compared',
    );
  });

  it('counts what is finished but cannot be compared', () => {
    expect(compareWhy([done('a.flac'), done('cover.jpg')]).tip).toBe(
      '2 finished, but only 1 is an audio file still on disk',
    );
  });

  it('holds to the limit', () => {
    const many = Array.from({ length: MAXIMUM_FILES + 1 }, (_, i) =>
      done(`${i}.flac`),
    );

    expect(compareWhy(many)).toMatchObject({
      on: false,
      tip: `At most ${MAXIMUM_FILES} at a time — ${MAXIMUM_FILES + 1} are ticked`,
    });
  });

  it('says what it leaves out rather than comparing fewer in silence', () => {
    // the case that was reported: two compared, a third ticked that is still
    // queued, and Compare pressed again -- which compared the same two, and
    // said nothing about the third
    const queued = done('c.flac', {
      localFilename: null,
      state: 'Queued, Remotely',
    });
    const why = compareWhy([done('a.flac'), done('b.flac'), queued]);

    expect(why.on).toBe(true);
    expect(why.picks.map((f) => f.id)).toEqual(['a.flac', 'b.flac']);
    expect(why.left).toEqual([
      { file: queued, reason: 'has not finished downloading' },
    ]);
    expect(why.tip).toBe(
      'Check whether these 2 are the same track — 1 more ticked is left out: it has not finished downloading',
    );
  });

  it('names each reason a row is left out', () => {
    const why = compareWhy([
      done('a.flac'),
      done('b.flac'),
      done('gone.flac', { localFileExists: false }),
      done('cover.jpg'),
      done('c.flac', { state: 'InProgress' }),
    ]);

    expect(why.left.map((l) => l.reason)).toEqual([
      'no longer has its file',
      'is not an audio file',
      'has not finished downloading',
    ]);
    expect(why.tip).toBe(
      'Check whether these 2 are the same track — 3 more ticked are left out',
    );
  });

  it('leaves nothing out when everything ticked can be compared', () => {
    expect(compareWhy([done('a.flac'), done('b.flac')]).left).toEqual([]);
  });

  it('compares only the rows it can, in the order ticked', () => {
    const why = compareWhy([done('b.flac'), done('cover.jpg'), done('a.flac')]);

    expect(why.on).toBe(true);
    expect(why.picks.map((f) => f.id)).toEqual(['b.flac', 'a.flac']);
    expect(why.tip).toBe(
      'Check whether these 2 are the same track — 1 more ticked is left out: it is not an audio file',
    );
  });
});

describe('arrange', () => {
  const picks = ['a', 'b', 'c', 'd'].map((name) => done(`${name}.flac`));
  const document = {
    alone: [2],
    files: [
      { music_s: 515.6, readable: true },
      { music_s: 517.6, readable: true },
      { music_s: 300, readable: true },
      { note: 'could not be read', readable: false },
    ],
    groups: [
      {
        files: [0, 1],
        notes: [
          'the others are compared with this one',
          '99% match · 2.0 s longer',
        ],
      },
    ],
    partly: [{ a: 0, b: 2, note: 'share 45% of their length' }],
    verdict: {
      detail: 'Grouped below',
      headline: '2 of 3 are the same track',
      level: 'mixed',
    },
  };

  it('puts each index back on the row it means', () => {
    const arranged = arrange(document, picks);

    expect(arranged.verdict.level).toBe('mixed');
    expect(
      arranged.groups[0].map((m) => [m.file.id, m.note, m.musicSeconds]),
    ).toEqual([
      ['a.flac', 'the others are compared with this one', 515.6],
      ['b.flac', '99% match · 2.0 s longer', 517.6],
    ]);
    expect(arranged.alone.map((m) => m.file.id)).toEqual(['c.flac']);
    expect(arranged.partly.map((p) => [p.a.id, p.b.id])).toEqual([
      ['a.flac', 'c.flac'],
    ]);
    expect(arranged.unreadable).toEqual([
      { file: picks[3], note: 'could not be read' },
    ]);
  });

  it('draws nothing it was not sent', () => {
    expect(arrange({}, picks)).toMatchObject({
      alone: [],
      groups: [],
      partly: [],
      unreadable: [],
    });
  });
});

describe('clock', () => {
  it('reads a length as minutes and seconds, and a missing one as nothing', () => {
    expect(clock(576)).toBe('9:36');
    expect(clock(61.4)).toBe('1:01');
    expect(clock(null)).toBe('');
    expect(clock(0)).toBe('');
  });
});

describe('rowsOf', () => {
  const picks = [done('a.flac'), done('b.flac'), done('c.flac')];

  it('follows the order the server answered in, not the order sent', () => {
    expect(rowsOf(['c.flac', 'a.flac'], picks).map((row) => row.id)).toEqual([
      'c.flac',
      'a.flac',
    ]);
  });

  it('drops what the server did not compare, so no index lands on the wrong row', () => {
    expect(rowsOf(['a.flac', 'missing'], picks).map((row) => row.id)).toEqual([
      'a.flac',
    ]);
  });

  it('falls back to the rows sent when the answer carries no ids', () => {
    expect(rowsOf(undefined, picks)).toBe(picks);
  });
});
