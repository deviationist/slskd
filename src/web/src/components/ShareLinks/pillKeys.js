// held on their own these are the start of something else, not typing
const MODIFIERS = new Set(['Alt', 'Control', 'Meta', 'Shift']);

/**
 * What a key does to the recipients field, as the To field in Gmail does it:
 * the arrows walk from the text onto the pills and along them, Backspace and
 * Delete remove the one picked out, and typing goes back to the text.
 *
 * Kept apart from the component as a function of what is on screen to what
 * to do, so that every key can be checked without a browser. The caller still
 * does the doing -- removing, editing, preventing the key's default.
 * @param {object} state
 * @param {string} state.key - The key, as KeyboardEvent.key.
 * @param {number|null} state.picked - The index of the pill picked out, or null.
 * @param {number} state.count - How many pills there are.
 * @param {boolean} state.atStart - Whether the caret is at the start of the text, with nothing selected.
 * @param {boolean} state.empty - Whether there is no text.
 * @returns {object|null} `{ picked }` to move the pick, `{ remove }` to remove
 *   that pill (and go back to the text), `{ edit }` to put that pill back into
 *   the text, or null for a key that is the text's to handle.
 */
const pillKeys = ({ atStart, count, empty, key, picked }) => {
  if (count === 0 || MODIFIERS.has(key)) {
    return null;
  }

  if (picked === null) {
    // from the text onto the pills: only from its very start, so that the
    // arrows still move through what is being typed
    if (key === 'ArrowLeft' && atStart) {
      return { picked: count - 1 };
    }

    // Backspace in an empty field picks out the last pill first, and the next
    // press removes it: one press removing it would cost a whole address to a
    // key held a moment too long
    if (key === 'Backspace' && empty) {
      return { picked: count - 1 };
    }

    return null;
  }

  switch (key) {
    case 'ArrowLeft':
      return { picked: Math.max(0, picked - 1) };
    case 'ArrowRight':
      // past the last pill is back to the text
      return { picked: picked + 1 < count ? picked + 1 : null };
    case 'Home':
      return { picked: 0 };
    case 'End':
    case 'Escape':
      return { picked: null };
    case 'Backspace':
    case 'Delete':
      return { remove: picked };
    case 'Enter':
      return { edit: picked };
    default:
      // anything typed goes to the text, and is the end of the pick
      return { picked: null, passThrough: true };
  }
};

export default pillKeys;
