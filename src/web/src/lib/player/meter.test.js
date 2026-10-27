import {
  CLIP_BLINK_MS,
  clipState,
  dbfs,
  drawMeter,
  holdPeak,
  laneAt,
  METER_AMBER,
  METER_AMBER_C,
  METER_FLOOR,
  METER_GREEN,
  METER_HOLD_RED,
  METER_RED,
  METER_RED_C,
  meterY,
} from './meter';

// twin: media-bridge web/app/test/meter.test.ts

const fakeContext = () => {
  const calls = [];
  let fill = '';

  return {
    calls,
    context: {
      clearRect: () => {},
      fillRect: (x, y, w, h) => calls.push({ fill, h, w, x, y }),
      get fillStyle() {
        return fill;
      },
      set fillStyle(value) {
        fill = value;
      },
    },
  };
};

const paint = (over = {}) => ({
  clip: 'none',
  holds: [METER_FLOOR, METER_FLOOR],
  levels: [METER_FLOOR, METER_FLOOR],
  ...over,
});

describe('the scale', () => {
  it('puts the room at the top, where the decisions are', () => {
    // a straight -60..0 mapping leaves red the top 1.7%
    expect(meterY(METER_AMBER)).toBeCloseTo(0.7, 2);
    expect(meterY(METER_RED)).toBeCloseTo(0.88, 2);
    expect(meterY(0)).toBe(1);
    expect(meterY(METER_FLOOR)).toBe(0);
  });

  it('never goes backwards', () => {
    let last = -1;

    for (let db = -60; db <= 0; db += 0.5) {
      expect(meterY(db)).toBeGreaterThanOrEqual(last);
      last = meterY(db);
    }
  });

  it('reads silence as the floor, not minus infinity', () => {
    expect(dbfs(0)).toBe(METER_FLOOR);
    expect(dbfs(1)).toBe(0);
    expect(dbfs(0.5)).toBeCloseTo(-6.02, 1);
  });
});

describe('the held peak', () => {
  it('is held and then falls back', () => {
    expect(holdPeak(-3, -20)).toBe(-3);
    expect(holdPeak(-40, -3)).toBeLessThan(-3);
    expect(holdPeak(-40, -3)).toBeGreaterThan(-40);
  });

  it('never falls below what is actually there', () => {
    expect(holdPeak(-10, -10.2)).toBe(-10);
  });
});

describe('the clip light', () => {
  it('reads the bucket the playhead is in, clamped to the lane', () => {
    expect(laneAt([0, 0, 90, 0], 0.5)).toBe(90);
    expect(laneAt([5, 9], 1)).toBe(9);
    expect(laneAt([5, 9], -1)).toBe(5);
    expect(laneAt(undefined, 0.5)).toBe(0);
  });

  it('is dark for a file that does not clip, dim for one that does, bright at the moment', () => {
    expect(clipState(undefined, 0)).toBe('none');
    expect(clipState([0, 90], 10_000)).toBe('somewhere');
    expect(clipState([0, 90], CLIP_BLINK_MS - 1)).toBe('here');
  });
});

describe('drawMeter', () => {
  it('paints a quiet signal green and nothing else', () => {
    const { calls, context } = fakeContext();
    drawMeter(context, 34, 56, paint({ levels: [-30, -30] }));
    const fills = calls.map((call) => call.fill);
    expect(fills).toContain(METER_GREEN);
    expect(fills).not.toContain(METER_AMBER_C);
    expect(fills).not.toContain(METER_RED_C);
  });

  it('paints a loud signal in all three zones, not one colour for the lot', () => {
    const { calls, context } = fakeContext();
    drawMeter(context, 34, 56, paint({ levels: [0, 0] }));
    const fills = calls.map((call) => call.fill);
    expect(fills).toEqual(
      expect.arrayContaining([METER_GREEN, METER_AMBER_C, METER_RED_C]),
    );
  });

  it('lights the clip light from the analysis, not from the level', () => {
    // a lossy decode overshoots: driven from the meter it lit on clean files
    const loudButClean = fakeContext();
    drawMeter(loudButClean.context, 34, 56, paint({ levels: [0, 0] }));
    expect(loudButClean.calls[0].fill).not.toBe(METER_HOLD_RED);

    const quietButClips = fakeContext();
    drawMeter(quietButClips.context, 34, 56, paint({ clip: 'here' }));
    expect(quietButClips.calls[0].fill).toBe(METER_HOLD_RED);
  });

  it('draws no hold line in silence, and draws the zone rules always', () => {
    const { calls, context } = fakeContext();
    drawMeter(context, 34, 56, paint(), { rule: 'RULE' });
    expect(calls.filter((call) => call.h === 2)).toHaveLength(0);
    expect(calls.filter((call) => call.fill === 'RULE')).toHaveLength(2);
  });

  it('takes the theme neutrals it is given, so an empty bar reads on a white page', () => {
    const { calls, context } = fakeContext();
    drawMeter(context, 34, 56, paint(), { empty: 'EMPTY', light: 'LIGHT' });
    expect(calls[0].fill).toBe('LIGHT');
    expect(calls.map((call) => call.fill)).toContain('EMPTY');
  });
});
