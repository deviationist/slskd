import { labelSide, laneOf, lanesFor, markOpacity, runs } from './lanes';

// twin: media-bridge web/app/test/lanes.test.ts

const finding = (id, over = {}) => ({
  id,
  label: id,
  level: 'bad',
  note: [],
  value: [],
  ...over,
});

describe('runs', () => {
  it('collapses consecutive lit buckets into one mark', () => {
    expect(runs([0, 50, 50, 0, 0, 100, 0, 0])).toEqual([
      { from: 1 / 8, to: 3 / 8, weight: 50 },
      { from: 5 / 8, to: 6 / 8, weight: 100 },
    ]);
  });

  it('carries the mean weight, so a dense passage reads darker', () => {
    expect(runs([100, 20])[0].weight).toBe(60);
  });

  it('closes a run that reaches the end', () => {
    // an off-by-one here would drop a problem at the very end of a track
    expect(runs([0, 0, 7])).toEqual([{ from: 2 / 3, to: 1, weight: 7 }]);
  });

  it('gives no marks for nothing lit', () => {
    expect(runs([0, 0])).toEqual([]);
    expect(runs([])).toEqual([]);
  });
});

describe('labelSide', () => {
  it('puts the label at the emptier end, ties to the left', () => {
    expect(labelSide([100, 100, 100, 0, 0, 0, 0, 0, 0, 0])).toBe('right');
    expect(labelSide([0, 0, 0, 0])).toBe('left');
    expect(labelSide([])).toBe('left');
  });
});

describe('markOpacity', () => {
  it('is floored well above nothing', () => {
    expect(markOpacity(0)).toBe(0.4);
    expect(markOpacity(100)).toBe(1);
  });
});

describe('lanesFor', () => {
  it('ties each lane to its finding, in the order the provider sent', () => {
    const lanes = lanesFor({
      findings: [finding('clipping'), finding('dropouts', { level: 'warn' })],
      lanes: [
        { buckets: [0, 1], finding: 'dropouts' },
        { buckets: [1, 0], finding: 'clipping' },
      ],
    });

    expect(lanes.map((lane) => lane.finding.id)).toEqual([
      'dropouts',
      'clipping',
    ]);
    expect(lanes[0].finding.level).toBe('warn');
  });

  it('drops a lane naming no finding, and one that is empty', () => {
    expect(
      lanesFor({
        findings: [finding('clipping')],
        lanes: [
          { buckets: [1], finding: 'orphan' },
          { buckets: [0, 0], finding: 'clipping' },
        ],
      }),
    ).toEqual([]);
  });

  it('answers for no analysis at all', () => {
    expect(lanesFor(undefined)).toEqual([]);
    expect(lanesFor({ findings: [] })).toEqual([]);
  });

  it('finds one finding’s buckets for the clip light', () => {
    const analysis = { lanes: [{ buckets: [0, 9], finding: 'clipping' }] };
    expect(laneOf(analysis, 'clipping')).toEqual([0, 9]);
    expect(laneOf(analysis, 'dropouts')).toBeUndefined();
  });
});
