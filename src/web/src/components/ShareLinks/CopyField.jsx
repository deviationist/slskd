import React, { useState } from 'react';
import { Input } from 'semantic-ui-react';

/**
 * A read-only field holding a link, with a button that copies it.
 *
 * The field is there as well as the button because the clipboard is not
 * always available -- it needs a secure origin, and some browsers ask first --
 * and a link that can only be copied by a button that failed cannot be had
 * at all. Selecting the field on focus makes the manual way one gesture.
 * @param {object} params
 * @param {string} params.value - The link.
 */
const CopyField = ({ value }) => {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 2_000);
    } catch {
      // the field is still there to copy from by hand
    }
  };

  return (
    <Input
      action={{
        color: copied ? 'green' : undefined,
        content: copied ? 'Copied' : 'Copy',
        icon: copied ? 'check' : 'copy',
        onClick: handleCopy,
        type: 'button',
      }}
      fluid
      onFocus={(event) => event.target.select()}
      readOnly
      value={value}
    />
  );
};

export default CopyField;
