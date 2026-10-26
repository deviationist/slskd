import pillKeys, { selectedIndices } from './pillKeys';

const sel = (anchor, focus = anchor) => ({ anchor, focus });

const press = (overrides) =>
  pillKeys({
    atStart: true,
    count: 3,
    empty: true,
    key: 'ArrowLeft',
    selection: null,
    ...overrides,
  });

describe('selectedIndices', () => {
  it('covers anchor to focus, whichever way round', () => {
    expect(selectedIndices(sel(2, 0))).toEqual([0, 1, 2]);
    expect(selectedIndices(sel(1))).toEqual([1]);
    expect(selectedIndices(null)).toEqual([]);
  });
});

describe('pillKeys', () => {
  it('walks from the text onto the last pill, then left along them', () => {
    expect(press({ key: 'ArrowLeft' })).toEqual({ select: sel(2) });
    expect(press({ key: 'ArrowLeft', selection: sel(2) })).toEqual({
      select: sel(1),
    });
  });

  it('stops at the first pill', () => {
    expect(press({ key: 'ArrowLeft', selection: sel(0) })).toEqual({
      select: sel(0),
    });
  });

  it('walks right, and past the last pill back to the text', () => {
    expect(press({ key: 'ArrowRight', selection: sel(1) })).toEqual({
      select: sel(2),
    });
    expect(press({ key: 'ArrowRight', selection: sel(2) })).toEqual({
      select: null,
    });
  });

  it('leaves the arrows to the text unless the caret is at its start', () => {
    expect(
      press({ atStart: false, empty: false, key: 'ArrowLeft' }),
    ).toBeNull();
    expect(press({ key: 'ArrowRight' })).toBeNull();
  });

  it('stretches the selection with Shift, from where it started', () => {
    expect(press({ key: 'ArrowLeft', shift: true, selection: sel(2) })).toEqual(
      { select: sel(2, 1) },
    );
    expect(
      press({ key: 'ArrowLeft', shift: true, selection: sel(2, 1) }),
    ).toEqual({ select: sel(2, 0) });
    expect(
      press({ key: 'ArrowRight', shift: true, selection: sel(2, 0) }),
    ).toEqual({ select: sel(2, 1) });
  });

  it('does not stretch past either end', () => {
    expect(
      press({ key: 'ArrowLeft', shift: true, selection: sel(2, 0) }),
    ).toEqual({ select: sel(2, 0) });
    expect(
      press({ key: 'ArrowRight', shift: true, selection: sel(2) }),
    ).toEqual({ select: sel(2) });
  });

  it('stretches to either end with Shift+Home and Shift+End', () => {
    expect(press({ key: 'Home', shift: true, selection: sel(2) })).toEqual({
      select: sel(2, 0),
    });
    expect(press({ key: 'End', shift: true, selection: sel(0) })).toEqual({
      select: sel(0, 2),
    });
    expect(press({ key: 'Home', shift: true })).toEqual({ select: sel(2, 0) });
  });

  it('collapses a selection to one pill on a plain arrow', () => {
    expect(press({ key: 'ArrowLeft', selection: sel(2, 1) })).toEqual({
      select: sel(0),
    });
    expect(press({ key: 'ArrowRight', selection: sel(0, 1) })).toEqual({
      select: sel(2),
    });
  });

  it('takes every pill on Ctrl/Cmd+A in an empty field, and leaves it to the text otherwise', () => {
    expect(press({ key: 'a', mod: true })).toEqual({ select: sel(0, 2) });
    expect(press({ empty: false, key: 'a', mod: true })).toBeNull();
  });

  it('keeps the selection for copy, cut and paste', () => {
    expect(press({ key: 'c', mod: true, selection: sel(0, 1) })).toBeNull();
    expect(press({ key: 'x', mod: true, selection: sel(0, 1) })).toBeNull();
  });

  it('picks the last pill on Backspace in an empty field, and removes it on the next', () => {
    expect(press({ key: 'Backspace' })).toEqual({ select: sel(2) });
    expect(press({ key: 'Backspace', selection: sel(2) })).toEqual({
      remove: [2],
    });
  });

  it('leaves Backspace to the text while there is text', () => {
    expect(press({ empty: false, key: 'Backspace' })).toBeNull();
  });

  it('removes everything selected, with Backspace or Delete', () => {
    expect(press({ key: 'Backspace', selection: sel(2, 0) })).toEqual({
      remove: [0, 1, 2],
    });
    expect(press({ key: 'Delete', selection: sel(1) })).toEqual({
      remove: [1],
    });
  });

  it('edits one pill on Enter, and does nothing with several', () => {
    expect(press({ key: 'Enter', selection: sel(1) })).toEqual({ edit: 1 });
    expect(press({ key: 'Enter', selection: sel(0, 1) })).toEqual({
      select: sel(0, 1),
    });
  });

  it('jumps to the first pill, and End or Escape lets go', () => {
    expect(press({ key: 'Home', selection: sel(2) })).toEqual({
      select: sel(0),
    });
    expect(press({ key: 'End', selection: sel(0) })).toEqual({ select: null });
    expect(press({ key: 'Escape', selection: sel(0, 2) })).toEqual({
      select: null,
    });
  });

  it('lets anything typed through to the text, and lets go of the selection', () => {
    expect(press({ key: 'a', selection: sel(0) })).toEqual({
      passThrough: true,
      select: null,
    });
  });

  it('keeps the selection while a modifier is held on its own', () => {
    expect(press({ key: 'Shift', selection: sel(0) })).toBeNull();
  });

  it('does nothing with no pills', () => {
    expect(press({ count: 0, key: 'Backspace' })).toBeNull();
  });
});
