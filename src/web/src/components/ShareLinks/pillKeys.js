// held on their own these are the start of something else, not typing
const MODIFIERS = new Set(['Alt', 'Control', 'Meta', 'Shift']);

/**
 * The pills a selection covers, lowest first.
 * @param {object|null} selection - `{ anchor, focus }`, or null.
 * @returns {number[]} The indices.
 */
export const selectedIndices = (selection) => {
  if (!selection) {
    return [];
  }

  const low = Math.min(selection.anchor, selection.focus);
  const high = Math.max(selection.anchor, selection.focus);

  return Array.from({ length: high - low + 1 }, (_, n) => low + n);
};

const one = (index) => ({ select: { anchor: index, focus: index } });

const extend = (selection, focus) => ({
  select: { anchor: selection.anchor, focus },
});

// from the text, with nothing selected: only from its very start, so that
// the arrows still move through what is being typed
const fromText = ({ atStart, empty, key, last, shift }) => {
  if (key === 'ArrowLeft' && atStart) {
    return one(last);
  }

  if (key === 'Home' && shift && atStart) {
    return { select: { anchor: last, focus: 0 } };
  }

  // Backspace in an empty field picks out the last pill first, and the next
  // press removes it: one press removing it would cost a whole address to a
  // key held a moment too long
  if (key === 'Backspace' && empty) {
    return one(last);
  }

  return null;
};

// with pills selected
const onSelection = ({ key, last, selection, shift }) => {
  const indices = selectedIndices(selection);
  const low = indices[0];
  const high = indices[indices.length - 1];

  switch (key) {
    case 'ArrowLeft':
      return shift
        ? extend(selection, Math.max(0, selection.focus - 1))
        : one(Math.max(0, low - 1));
    case 'ArrowRight':
      if (shift) {
        return extend(selection, Math.min(last, selection.focus + 1));
      }

      // past the last pill is back to the text
      return high < last ? one(high + 1) : { select: null };
    case 'Home':
      return shift ? extend(selection, 0) : one(0);
    case 'End':
      return shift ? extend(selection, last) : { select: null };
    case 'Escape':
      return { select: null };
    case 'Backspace':
    case 'Delete':
      return { remove: indices };
    case 'Enter':
      // one pill to edit; with several, Enter does nothing rather than guess
      // which, and is kept from submitting the form
      return low === high ? { edit: low } : { select: selection };
    default:
      // anything typed goes to the text, and is the end of the selection
      return { passThrough: true, select: null };
  }
};

/**
 * What a key does to the recipients field, as the To field in Gmail does it.
 *
 * The pills picked out are a selection, an anchor and a focus as in text: the
 * arrows walk from the start of the text onto the pills and along them,
 * Shift with them stretches the selection, Ctrl/Cmd+A in an empty field takes
 * every pill, Backspace and Delete remove what is selected, Enter edits a
 * single pill, and typing goes back to the text. Copying and cutting are the
 * component's, through the clipboard; a key held with Ctrl or Cmd leaves the
 * selection as it is, so that they find it there.
 *
 * Kept apart from the component as a function of what is on screen to what
 * to do, so that every key can be checked without a browser. The caller still
 * does the doing -- removing, editing, preventing the key's default.
 * @param {object} state
 * @param {string} state.key - The key, as KeyboardEvent.key.
 * @param {boolean} [state.shift] - Whether Shift is held.
 * @param {boolean} [state.mod] - Whether Ctrl or Cmd is held.
 * @param {object|null} state.selection - `{ anchor, focus }`, or null for none.
 * @param {number} state.count - How many pills there are.
 * @param {boolean} state.atStart - Whether the caret is at the start of the text, with nothing selected.
 * @param {boolean} state.empty - Whether there is no text.
 * @returns {object|null} `{ select }` for the selection now (null to go back
 *   to the text), `{ remove }` for the indices to remove (and go back to the
 *   text), `{ edit }` for a pill to put back into the text, or null for a key
 *   that is the text's to handle; with `passThrough`, the key goes to the text
 *   as well.
 */
const pillKeys = ({
  atStart,
  count,
  empty,
  key,
  mod = false,
  selection,
  shift = false,
}) => {
  if (count === 0 || MODIFIERS.has(key)) {
    return null;
  }

  // every pill, but only where there is no text for it to mean instead
  if (mod && key.toLowerCase() === 'a') {
    return empty ? { select: { anchor: 0, focus: count - 1 } } : null;
  }

  // copy, cut, paste and the like: theirs to handle, and the selection stays
  // for them to act on
  if (mod) {
    return null;
  }

  const last = count - 1;

  return selection === null
    ? fromText({ atStart, empty, key, last, shift })
    : onSelection({ key, last, selection, shift });
};

export default pillKeys;
