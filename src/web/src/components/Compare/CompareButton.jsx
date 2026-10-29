import * as compareLibrary from '../../lib/compare';
import React from 'react';
import { Button, Popup } from 'semantic-ui-react';

/**
 * Compare, for whatever is ticked -- across every card, not only the one the
 * button sits in, since copies of one track from different peers are exactly
 * what gets compared.
 *
 * Never `disabled`: Semantic gives a disabled button `pointer-events: none`,
 * so its tooltip, which is the only place that says *why* it is off, could
 * never be seen. Off, it is dimmed and a press does nothing.
 * @param {object} params
 * @param {string} [params.content] - The label.
 * @param {Function} params.onCompareRequested - Opens the dialog with the rows to compare, and the ticked rows left out.
 * @param {object[]} params.ticked - Every row ticked on the page.
 * @returns {object} The button.
 */
const CompareButton = ({ content = 'Compare', onCompareRequested, ticked }) => {
  const why = compareLibrary.compareWhy(ticked);

  return (
    <Popup
      content={why.tip}
      position="top center"
      trigger={
        <Button
          aria-disabled={!why.on}
          className={why.on ? 'compare-button' : 'compare-button compare-off'}
          color="violet"
          content={content}
          icon="clone outline"
          onClick={() => {
            if (why.on) onCompareRequested(why.picks, why.left);
          }}
        />
      }
    />
  );
};

export default CompareButton;
