import * as shareLinks from '../../lib/shareLinks';
import { formatDayMonth } from '../../lib/util';
import pillKeys, { selectedIndices } from './pillKeys';
import React, { useEffect, useRef, useState } from 'react';
import { Icon, Label } from 'semantic-ui-react';

// what ends an address as it is typed. Tab only when there is something to
// end, so that an empty field still lets focus move on
const SEPARATORS = new Set(['Enter', ',', ';', ' ']);

/**
 * What is wrong with the addresses, if anything, under the field. The pills
 * themselves show which: this says what to do about it.
 */
export const RecipientProblems = ({ invalid, tooMany }) => {
  if (invalid.length > 0) {
    return (
      <p className="share-dialog-note share-dialog-unreachable">
        {invalid.length === 1
          ? 'Fix or remove the address marked in red.'
          : `Fix or remove the ${invalid.length} addresses marked in red.`}
      </p>
    );
  }

  if (tooMany) {
    return (
      <p className="share-dialog-note share-dialog-unreachable">
        At most {shareLinks.MAXIMUM_RECIPIENTS} addresses at once.
      </p>
    );
  }

  return null;
};

const writeClipboard = async (text) => {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    // refused or unavailable: nothing to fall back on from a key already
    // handled, and the addresses are still there to copy another way
  }
};

/**
 * Whether a dialog's close is an Escape that only meant to let go of a pill,
 * or to put away the suggestions.
 *
 * Semantic's Modal closes on Escape from a listener on the document in the
 * capture phase, which runs before anything on the field can stop it. So the
 * field says on itself, as `data-pill-picked`, that a pill is picked out, and
 * a dialog holding one asks this before closing: with a pill picked, Escape
 * lets go of it and the dialog stays; with none, Escape closes as it always has.
 * Suggestions on show are said the same way, as `data-suggesting`, and the
 * first Escape puts them away.
 * @param {Event} [event] - What the dialog was asked to close by.
 * @returns {boolean} Whether to stay open.
 */
export const isPickEscape = (event) =>
  event?.type === 'keydown' &&
  event.key === 'Escape' &&
  (event.target?.dataset?.pillPicked === 'true' ||
    event.target?.dataset?.suggesting === 'true');

/**
 * Addresses as pills: type one and press Enter (or a comma, a semicolon, a
 * space, Tab) and it becomes a pill of its own, checked as it is made. A pill
 * can be put back into the field to fix, or removed. Pasting several at once
 * makes a pill of each.
 *
 * The keyboard does what Gmail's To field does (see pillKeys): Left from the
 * start of the text picks out the last pill and walks left along them, Right
 * walks back, and Shift with either stretches the selection; Ctrl/Cmd+A in an
 * empty field selects every pill. Backspace or Delete removes what is
 * selected, Ctrl/Cmd+C copies it as addresses separated by commas and
 * Ctrl/Cmd+X cuts it, and Enter puts a single pill back into the text.
 * Backspace in an empty field picks out the last pill first and removes it on
 * the second press. A click picks a pill out too, Shift+click stretches the
 * selection to it, and a double click edits it. The caret never leaves the
 * text, so typing carries on from wherever the selection was let go.
 *
 * As an address is typed, the addresses shares have been mailed to before
 * that match it are offered under the field (see suggestRecipients). Up and
 * Down move through them, Enter or Tab takes the highlighted one -- the first,
 * until another is chosen -- and Escape puts them away. Shift+Delete, or the
 * cross beside one, forgets an address so it is not offered again. A comma,
 * a semicolon or a space still ends what was typed as typed.
 *
 * Controlled from both ends -- the pills and what is still being typed -- so
 * that the owner can count an address typed and not yet ended as one of the
 * recipients. Leaving it out would mail everyone except the address someone
 * typed last and clicked Share straight after.
 * @param {object} params
 * @param {boolean} params.autoFocus - Whether the field takes focus when shown.
 * @param {boolean} params.disabled - Whether the field takes input.
 * @param {string} params.draft - What is typed and not yet a pill.
 * @param {string} params.id - The input's id, for its label.
 * @param {Function} params.onChange - Called with the new pills and draft.
 * @param {string} params.placeholder - Shown when there is nothing at all.
 * @param {string[]} params.recipients - The pills.
 */
const RecipientsInput = ({
  autoFocus = false,
  disabled = false,
  draft,
  id,
  onChange,
  placeholder,
  recipients,
}) => {
  const input = useRef(null);

  // every address mailed before, fetched once; a field that cannot be used
  // does not ask. Suggestions are a convenience, so failing to get them
  // leaves the field as it was without them rather than saying anything
  const [history, setHistory] = useState([]);

  useEffect(() => {
    if (disabled) return undefined;

    let current = true;

    const load = async () => {
      try {
        const entries = await shareLinks.recipientHistory();
        if (current) setHistory(entries ?? []);
      } catch {
        // without them, as before there were any
      }
    };

    load();

    return () => {
      current = false;
    };
  }, [disabled]);

  const [focused, setFocused] = useState(false);
  // put away with Escape until the text changes again
  const [dismissed, setDismissed] = useState(false);
  const [highlight, setHighlight] = useState(0);

  const suggestions = shareLinks.suggestRecipients({
    draft,
    history,
    recipients,
  });
  const suggesting = focused && !dismissed && suggestions.length > 0;
  const highlighted = Math.min(highlight, suggestions.length - 1);
  const suggestionsId = `${id}-suggestions`;

  // the pills the keyboard or a click has picked out, as an anchor and a
  // focus as in text, or null; let go of if the pills have since changed
  // under it
  const [chosen, setChosen] = useState(null);
  const selection =
    chosen && Math.max(chosen.anchor, chosen.focus) < recipients.length
      ? chosen
      : null;
  const picked = selectedIndices(selection);
  const pickedText = () => picked.map((index) => recipients[index]).join(', ');

  const commit = (text = draft) =>
    onChange({
      draft: '',
      recipients: shareLinks.addRecipients(recipients, text),
    });

  // a suggestion taken stands for what was typed, which goes
  const take = (entry) => {
    setChosen(null);
    setHighlight(0);
    onChange({
      draft: '',
      recipients: shareLinks.addRecipients(recipients, entry.address),
    });
    input.current?.focus();
  };

  const forget = async (entry) => {
    setHistory((entries) =>
      entries.filter((candidate) => candidate.address !== entry.address),
    );

    try {
      await shareLinks.forgetRecipient({ address: entry.address });
    } catch {
      // still not offered for as long as the field is open, which is what
      // was asked for here; the next dialog will offer it again
    }
  };

  // the suggestions' keys, ahead of the pills'. Whether one was used
  const handleSuggestionKey = (event) => {
    if (!suggesting) return false;

    const count = suggestions.length;

    switch (event.key) {
      case 'ArrowDown':
        setHighlight((highlighted + 1) % count);
        break;
      case 'ArrowUp':
        setHighlight((highlighted - 1 + count) % count);
        break;
      case 'Enter':
      case 'Tab':
        take(suggestions[highlighted]);
        break;
      case 'Escape':
        setDismissed(true);
        break;
      case 'Delete':
        if (!event.shiftKey) return false;
        forget(suggestions[highlighted]);
        break;
      default:
        return false;
    }

    event.preventDefault();
    return true;
  };

  const handlePaste = (event) => {
    const text = event.clipboardData?.getData('text') ?? '';
    const pasted = shareLinks.parseRecipients(text);

    // a paste of several makes a pill of each, along with whatever was typed;
    // a paste of one address into an empty field is one finished address, and
    // becomes its pill at once, as in Gmail. Pasted into the middle of
    // something being typed, or not an address, it is text to carry on with
    if (
      pasted.length > 1 ||
      (draft === '' && pasted.length === 1 && shareLinks.isRecipient(pasted[0]))
    ) {
      event.preventDefault();
      commit(`${draft} ${text}`);
    }
  };

  const handleEdit = (value) => {
    setChosen(null);

    // whatever was being typed is kept, as a pill, rather than overwritten
    onChange({
      draft: value,
      recipients: shareLinks
        .addRecipients(recipients, draft)
        .filter((recipient) => recipient !== value),
    });

    input.current?.focus();
  };

  // back to the text afterwards, as after Backspace on a pill in Gmail
  const removeIndices = (indices) => {
    setChosen(null);
    onChange({
      draft,
      recipients: recipients.filter((_, index) => !indices.includes(index)),
    });
    input.current?.focus();
  };

  const handleRemove = (value) => removeIndices([recipients.indexOf(value)]);

  // the selected pills as text, for the clipboard; cut takes them out as well
  const handleClipboard = (event, cut) => {
    if (picked.length === 0) return;
    event.preventDefault();
    event.clipboardData.setData('text/plain', pickedText());
    if (cut) removeIndices(picked);
  };

  const handleKeyDown = (event) => {
    if (handleSuggestionKey(event)) return;

    const mod = event.ctrlKey || event.metaKey;
    const letter = event.key.toLowerCase();

    // Ctrl/Cmd+C and +X on selected pills. Written straight to the clipboard
    // where it can be, since not every browser raises a copy event from a
    // field with no text selected; where it cannot, the key is left to the
    // browser and the copy event below does it
    if (
      mod &&
      picked.length > 0 &&
      (letter === 'c' || letter === 'x') &&
      navigator.clipboard?.writeText
    ) {
      event.preventDefault();
      writeClipboard(pickedText());
      if (letter === 'x') removeIndices(picked);
      return;
    }

    const { selectionEnd, selectionStart } = event.target;
    const action = pillKeys({
      atStart: selectionStart === 0 && selectionEnd === 0,
      count: recipients.length,
      empty: draft === '',
      key: event.key,
      mod,
      selection,
      shift: event.shiftKey,
    });

    if (action && !action.passThrough) {
      event.preventDefault();

      if ('remove' in action) {
        removeIndices(action.remove);
      } else if ('edit' in action) {
        handleEdit(recipients[action.edit]);
      } else {
        setChosen(action.select);
      }

      return;
    }

    if (action?.passThrough) {
      setChosen(null);
    }

    if (
      SEPARATORS.has(event.key) ||
      (event.key === 'Tab' && draft.trim() !== '')
    ) {
      // Enter in particular: inside a form it would otherwise submit it
      event.preventDefault();
      commit();
    }
  };

  return (
    // a click anywhere in the box, not only on the bit of input left after
    // the pills, puts the cursor in it
    // eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-static-element-interactions
    <div
      className={`recipients-input${disabled ? ' disabled' : ''}`}
      onClick={(event) => {
        if (event.target === event.currentTarget) input.current?.focus();
      }}
    >
      {recipients.map((value, index) => {
        const valid = shareLinks.isRecipient(value);
        const isPicked = picked.includes(index);

        return (
          <Label
            className={`recipients-input-pill${isPicked ? ' picked' : ''}`}
            color={valid ? undefined : 'red'}
            id={`${id}-pill-${index}`}
            key={value}
            onClick={(event) => {
              // a click on the pill itself, not on its edit or remove button
              if (event.target.closest('button')) return;
              setChosen(
                event.shiftKey && selection
                  ? { anchor: selection.anchor, focus: index }
                  : { anchor: index, focus: index },
              );
              input.current?.focus();
            }}
            onDoubleClick={(event) => {
              if (event.target.closest('button')) return;
              handleEdit(value);
            }}
            // the text keeps the focus through a press on a pill: its blur
            // lets go of the selection, which a Shift+click must still find
            onMouseDown={(event) => {
              if (!event.target.closest('button')) event.preventDefault();
            }}
            title={valid ? value : `Not an email address: ${value}`}
          >
            {!valid && <Icon name="warning circle" />}
            <span className="recipients-input-address">{value}</span>
            <button
              aria-label={`Edit ${value}`}
              disabled={disabled}
              onClick={() => handleEdit(value)}
              type="button"
            >
              <Icon
                fitted
                name="pencil"
              />
            </button>
            <button
              aria-label={`Remove ${value}`}
              disabled={disabled}
              onClick={() => handleRemove(value)}
              type="button"
            >
              <Icon
                fitted
                name="close"
              />
            </button>
          </Label>
        );
      })}
      <input
        aria-activedescendant={
          suggesting ? `${suggestionsId}-${highlighted}` : undefined
        }
        aria-autocomplete="list"
        aria-controls={suggestionsId}
        aria-expanded={suggesting}
        // the browser's own would draw over ours, and knows only the
        // addresses typed into forms on this device
        autoComplete="off"
        // eslint-disable-next-line jsx-a11y/no-autofocus -- only where the dialog opens for this field alone
        autoFocus={autoFocus}
        // read by isPickEscape, before this field's own handlers run
        data-pill-picked={selection ? 'true' : undefined}
        data-suggesting={suggesting ? 'true' : undefined}
        disabled={disabled}
        id={id}
        // text with the email keyboard rather than type="email", which brings
        // the browser's own validation: it would block the form on a draft
        // that is about to become a pill, and treats spaces unevenly
        inputMode="email"
        onBlur={() => {
          setChosen(null);
          setFocused(false);
          if (draft.trim()) commit();
        }}
        onChange={(event) => {
          setDismissed(false);
          setHighlight(0);
          onChange({ draft: event.target.value, recipients });
        }}
        onCopy={(event) => handleClipboard(event, false)}
        onCut={(event) => handleClipboard(event, true)}
        onFocus={() => setFocused(true)}
        onKeyDown={handleKeyDown}
        onPaste={handlePaste}
        placeholder={recipients.length === 0 ? placeholder : undefined}
        ref={input}
        role="combobox"
        type="text"
        value={draft}
      />
      {suggesting && (
        // pressed rather than clicked, and held from taking the focus, so the
        // field is not left -- which would end the draft as typed -- first
        <ul
          className="recipients-input-suggestions"
          id={suggestionsId}
          onMouseDown={(event) => event.preventDefault()}
          role="listbox"
        >
          {suggestions.map((entry, index) => (
            // eslint-disable-next-line jsx-a11y/click-events-have-key-events -- the field's own keys choose, and the focus never leaves it
            <li
              aria-selected={index === highlighted}
              className={index === highlighted ? 'highlighted' : undefined}
              id={`${suggestionsId}-${index}`}
              key={entry.address}
              onClick={(event) => {
                if (event.target.closest('button')) return;
                take(entry);
              }}
              onMouseEnter={() => setHighlight(index)}
              role="option"
            >
              <span className="recipients-input-address">{entry.address}</span>
              {entry.lastSentAt && (
                <span className="recipients-input-suggestion-when">
                  {formatDayMonth(entry.lastSentAt)}
                </span>
              )}
              <button
                aria-label={`Forget ${entry.address}`}
                onClick={() => forget(entry)}
                tabIndex={-1}
                title="Stop suggesting this address"
                type="button"
              >
                <Icon
                  fitted
                  name="close"
                />
              </button>
            </li>
          ))}
        </ul>
      )}
      {/* the pick is drawn, and the caret stays in the text, so a screen
          reader is told in words which pill the next key will act on */}
      <span
        aria-live="polite"
        className="recipients-input-status"
      >
        {picked.length === 1 &&
          `${recipients[picked[0]]} selected. Backspace removes it, Enter edits it.`}
        {picked.length > 1 &&
          `${picked.length} addresses selected. Backspace removes them.`}
      </span>
    </div>
  );
};

export default RecipientsInput;
