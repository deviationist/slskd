import {
  arrange,
  clock,
  compareWhy,
  isComparable,
  isEnabled,
  MAXIMUM_FILES,
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
  it('wants Compare enabled and a provider', () => {
    const on = {
      transfers: { compare: { enabled: true, provider: { url: 'http://p' } } },
    };

    expect(isEnabled(on)).toBe(true);
    expect(
      isEnabled({
        transfers: { compare: { enabled: true, provider: { url: null } } },
      }),
    ).toBe(false);
    expect(
      isEnabled({
        transfers: {
          compare: { enabled: false, provider: { url: 'http://p' } },
        },
      }),
    ).toBe(false);
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

  it('compares only the rows it can, in the order ticked', () => {
    const why = compareWhy([done('b.flac'), done('cover.jpg'), done('a.flac')]);

    expect(why.on).toBe(true);
    expect(why.picks.map((f) => f.id)).toEqual(['b.flac', 'a.flac']);
    expect(why.tip).toBe('Check whether these 2 are the same track');
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
