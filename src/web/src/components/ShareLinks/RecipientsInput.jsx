import * as shareLinks from '../../lib/shareLinks';
import React, { useRef } from 'react';
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

/**
 * Addresses as pills: type one and press Enter (or a comma, a semicolon, a
 * space, Tab) and it becomes a pill of its own, checked as it is made. A pill
 * can be put back into the field to fix, or removed. Pasting several at once
 * makes a pill of each.
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

  const commit = (text = draft) =>
    onChange({
      draft: '',
      recipients: shareLinks.addRecipients(recipients, text),
    });

  const handleKeyDown = (event) => {
    if (
      SEPARATORS.has(event.key) ||
      (event.key === 'Tab' && draft.trim() !== '')
    ) {
      // Enter in particular: inside a form it would otherwise submit it
      event.preventDefault();
      commit();
    }
  };

  const handlePaste = (event) => {
    const text = event.clipboardData?.getData('text') ?? '';

    // a single address pasted is left to be typed onto, like any other text
    if (/[\s,;]/u.test(text.trim())) {
      event.preventDefault();
      commit(`${draft} ${text}`);
    }
  };

  const handleEdit = (value) => {
    // whatever was being typed is kept, as a pill, rather than overwritten
    onChange({
      draft: value,
      recipients: shareLinks
        .addRecipients(recipients, draft)
        .filter((recipient) => recipient !== value),
    });

    input.current?.focus();
  };

  const handleRemove = (value) =>
    onChange({
      draft,
      recipients: recipients.filter((recipient) => recipient !== value),
    });

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
      {recipients.map((value) => {
        const valid = shareLinks.isRecipient(value);

        return (
          <Label
            className="recipients-input-pill"
            color={valid ? undefined : 'red'}
            key={value}
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
        autoComplete="email"
        // eslint-disable-next-line jsx-a11y/no-autofocus -- only where the dialog opens for this field alone
        autoFocus={autoFocus}
        disabled={disabled}
        id={id}
        // text with the email keyboard rather than type="email", which brings
        // the browser's own validation: it would block the form on a draft
        // that is about to become a pill, and treats spaces unevenly
        inputMode="email"
        onBlur={() => draft.trim() && commit()}
        onChange={(event) =>
          onChange({ draft: event.target.value, recipients })
        }
        onKeyDown={handleKeyDown}
        onPaste={handlePaste}
        placeholder={recipients.length === 0 ? placeholder : undefined}
        ref={input}
        type="text"
        value={draft}
      />
    </div>
  );
};

export default RecipientsInput;
