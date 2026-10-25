import pillKeys from './pillKeys';

const at = (overrides) =>
  pillKeys({
    atStart: true,
    count: 2,
    empty: true,
    key: 'ArrowLeft',
    picked: null,
    ...overrides,
  });

describe('pillKeys', () => {
  it('walks from the text onto the last pill, then left along them', () => {
    expect(at({ key: 'ArrowLeft' })).toEqual({ picked: 1 });
    expect(at({ key: 'ArrowLeft', picked: 1 })).toEqual({ picked: 0 });
  });

  it('stops at the first pill', () => {
    expect(at({ key: 'ArrowLeft', picked: 0 })).toEqual({ picked: 0 });
  });

  it('walks right, and past the last pill back to the text', () => {
    expect(at({ key: 'ArrowRight', picked: 0 })).toEqual({ picked: 1 });
    expect(at({ key: 'ArrowRight', picked: 1 })).toEqual({ picked: null });
  });

  it('leaves the arrows to the text unless the caret is at its start', () => {
    expect(at({ atStart: false, empty: false, key: 'ArrowLeft' })).toBeNull();
    expect(at({ key: 'ArrowRight' })).toBeNull();
  });

  it('picks the last pill on Backspace in an empty field, and removes it on the next', () => {
    expect(at({ key: 'Backspace' })).toEqual({ picked: 1 });
    expect(at({ key: 'Backspace', picked: 1 })).toEqual({ remove: 1 });
  });

  it('leaves Backspace to the text while there is text', () => {
    expect(at({ empty: false, key: 'Backspace' })).toBeNull();
  });

  it('removes a picked pill anywhere along them, with Backspace or Delete', () => {
    expect(at({ key: 'Backspace', picked: 0 })).toEqual({ remove: 0 });
    expect(at({ key: 'Delete', picked: 0 })).toEqual({ remove: 0 });
  });

  it('puts a picked pill back into the text on Enter', () => {
    expect(at({ key: 'Enter', picked: 0 })).toEqual({ edit: 0 });
  });

  it('jumps to either end, and Escape lets go', () => {
    expect(at({ key: 'Home', picked: 1 })).toEqual({ picked: 0 });
    expect(at({ key: 'End', picked: 0 })).toEqual({ picked: null });
    expect(at({ key: 'Escape', picked: 0 })).toEqual({ picked: null });
  });

  it('lets anything typed through to the text, and lets go of the pick', () => {
    expect(at({ key: 'a', picked: 0 })).toEqual({
      passThrough: true,
      picked: null,
    });
  });

  it('keeps the pick while a modifier is held on its own', () => {
    expect(at({ key: 'Shift', picked: 0 })).toBeNull();
  });

  it('does nothing with no pills', () => {
    expect(at({ count: 0, key: 'Backspace' })).toBeNull();
  });
});
