import {
  activatesOnSpace,
  clock,
  fractionAt,
  keyAction,
  modeFor,
  outline,
  SCRUB_STEP_MS,
  scrubSeekDue,
  SEEK_STEP,
  seekOnRelease,
  shownTime,
  typingIn,
} from './transport';

const element = (tagName, over = {}) => ({
  getAttribute: () => null,
  tagName,
  ...over,
});

const press = (key, over = {}) => ({ key, target: element('BODY'), ...over });

describe('clock', () => {
  it('reads minutes and padded seconds, and nonsense as zero', () => {
    expect(clock(0)).toBe('0:00');
    expect(clock(377.4)).toBe('6:17');
    expect(clock(Number.NaN)).toBe('0:00');
    expect(clock(-5)).toBe('0:00');
  });
});

describe('shownTime', () => {
  it('never says a time past the end of the track it is in', () => {
    expect(shownTime(3_600, 377)).toBe(377);
  });

  it('does not freeze the clock before the length is known', () => {
    expect(shownTime(12, 0)).toBe(12);
  });
});

describe('outline', () => {
  it('is one closed path mirrored about the centre line', () => {
    const path = outline([0, 1], 2, 10);
    expect(path.startsWith('M')).toBe(true);
    expect(path.endsWith('Z')).toBe(true);
    expect(path).toContain(',0.0');
    expect(path).toContain(',10.0');
  });

  it('is nothing for no peaks', () => {
    expect(outline([])).toBe('');
  });

  it('samples down to the width rather than drawing every bucket', () => {
    const path = outline(
      Array.from({ length: 1_600 }, () => 0.5),
      100,
      10,
    );
    expect(path.split('L').length).toBeLessThan(250);
  });
});

describe('what a key press is for', () => {
  it('does not take keys from something being typed into', () => {
    expect(typingIn(element('INPUT'))).toBe(true);
    expect(typingIn(element('DIV', { isContentEditable: true }))).toBe(true);
    expect(typingIn(element('BUTTON'))).toBe(false);
    expect(
      keyAction(press('ArrowRight', { target: element('INPUT') }), {
        at: 0,
        length: 100,
      }),
    ).toBeNull();
  });

  it('leaves space to a focused button, which would act on it anyway', () => {
    // two actions from one press -- and one button within reach deletes a file
    expect(activatesOnSpace(element('BUTTON'))).toBe(true);
    expect(
      activatesOnSpace(element('DIV', { getAttribute: () => 'button' })),
    ).toBe(true);
    expect(
      keyAction(press(' ', { target: element('BUTTON') }), {
        at: 0,
        length: 1,
      }),
    ).toBeNull();
    expect(keyAction(press(' '), { at: 0, length: 1 })).toEqual({
      type: 'toggle',
    });
  });

  it('moves the arrows SEEK_STEP, never before the start or past the end', () => {
    expect(keyAction(press('ArrowRight'), { at: 5, length: 100 })).toEqual({
      to: 5 + SEEK_STEP,
      type: 'seek',
    });
    expect(keyAction(press('ArrowLeft'), { at: 5, length: 100 })).toEqual({
      to: 0,
      type: 'seek',
    });
    expect(keyAction(press('ArrowRight'), { at: 95, length: 100 })).toEqual({
      to: 100,
      type: 'seek',
    });
  });

  it('jumps to tenths with the digits, but only with a length to take a tenth of', () => {
    expect(keyAction(press('3'), { at: 0, length: 200 })).toEqual({
      to: 60,
      type: 'seek',
    });
    expect(keyAction(press('3'), { at: 0, length: 0 })).toBeNull();
  });

  it('leaves modified keys to the browser and the OS', () => {
    expect(
      keyAction(press('ArrowLeft', { metaKey: true }), { at: 5, length: 100 }),
    ).toBeNull();
    expect(
      keyAction(press('ArrowLeft', { ctrlKey: true }), { at: 5, length: 100 }),
    ).toBeNull();
  });

  it('reports Escape for the caller to decide, but not from a search box', () => {
    expect(keyAction(press('Escape'), { at: 0, length: 1 })).toEqual({
      type: 'escape',
    });
    expect(
      keyAction(press('Escape', { target: element('INPUT') }), {
        at: 0,
        length: 1,
      }),
    ).toBeNull();
  });
});

describe('scrubbing', () => {
  it('reads where along the waveform a pointer is, clamped to it', () => {
    const box = { left: 100, width: 200 };
    expect(fractionAt(200, box)).toBe(0.5);
    expect(fractionAt(0, box)).toBe(0);
    expect(fractionAt(999, box)).toBe(1);
    expect(fractionAt(5, { left: 0, width: 0 })).toBe(0);
  });

  it('sends the audio along in steps, not on every move', () => {
    expect(scrubSeekDue(1_000 + SCRUB_STEP_MS - 1, 1_000)).toBe(false);
    expect(scrubSeekDue(1_000 + SCRUB_STEP_MS, 1_000)).toBe(true);
  });

  it('does not seek again on the release of a click', () => {
    // it re-buffered for nothing and stuttered on every click
    expect(seekOnRelease(0.5, 0.5, 300)).toBe(false);
    expect(seekOnRelease(0.6, 0.5, 300)).toBe(true);
    expect(seekOnRelease(0.5, null, 300)).toBe(true);
  });
});

describe('modeFor', () => {
  it('is the bar off a phone, and a mini bar or a drawer on one', () => {
    expect(modeFor(false, true)).toBe('wide');
    expect(modeFor(true, false)).toBe('mini');
    expect(modeFor(true, true)).toBe('full');
  });
});
