import React from 'react';
import { Modal } from 'semantic-ui-react';

/**
 * Asks before a Clear removes a screenful of searches.
 *
 * It names what is kept as well as what goes. A button called Clear that
 * leaves things behind owes an explanation of which things, and the two
 * reasons are different enough to be worth telling apart: a watched search is
 * kept because deleting it would orphan its schedule, and a running one
 * because it cannot be deleted yet at all.
 * @param {object} params
 * @param {boolean} params.busy - Whether the removals are under way.
 * @param {Function} params.onCancel - Closes without removing anything.
 * @param {Function} params.onConfirm - Starts the removals.
 * @param {{prompt: string, kept: string|undefined}} params.plan - What to say.
 * @returns {object} The dialog.
 */
const ClearSearchesModal = ({ busy, onCancel, onConfirm, plan }) => (
  <Modal
    actions={[
      'Cancel',
      {
        content: 'Clear',
        key: 'clear',
        loading: busy,
        negative: true,
        onClick: onConfirm,
      },
    ]}
    centered
    content={
      <Modal.Content>
        <p>{plan.prompt}</p>
        {plan.kept && <p>{plan.kept}</p>}
      </Modal.Content>
    }
    header="Clear searches"
    onClose={onCancel}
    open
    size="tiny"
  />
);

export default ClearSearchesModal;
