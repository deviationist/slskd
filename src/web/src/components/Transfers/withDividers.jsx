import React from 'react';
import { Button } from 'semantic-ui-react';

/**
 * Puts Semantic's "or" divider between the buttons a selection actually
 * offers, given every candidate with `false` for the ones it does not.
 *
 * Written once rather than as a condition per pair: with five actions that
 * may each be absent, spelling out when a divider belongs between two of
 * them was most of the selection bar's code, and all of its complexity. Every
 * button passed must carry a key.
 * @param {Array<object|boolean>} buttons - The candidates, in order.
 * @returns {object[]} The offered buttons, with dividers between them.
 */
const withDividers = (buttons) =>
  buttons
    .filter(Boolean)
    .flatMap((button, index) =>
      index === 0 ? [button] : [<Button.Or key={`or-${button.key}`} />, button],
    );

export default withDividers;
