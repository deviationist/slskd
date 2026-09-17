import { SORT_HINT_TEXT } from '../../lib/tables';
import React from 'react';
import { Icon } from 'semantic-ui-react';

/**
 * How to sort by more than one column, said where it can be found.
 *
 * Above the table rather than on the headers. The headers carry the same
 * sentence as a `title`, which is a tooltip nobody meets unless they already
 * suspect there is something to hover for -- and a feature reached only by a
 * modifier key is one nobody suspects. This is the line that answers "it just
 * switches columns instead of adding one", which is what a plain click is
 * supposed to do and gives no hint that anything else is on offer.
 * @returns {object} The hint.
 */
const SortHint = () => (
  <span className="flatlist-hint">
    <Icon name="info circle" />
    {SORT_HINT_TEXT}
  </span>
);

export default SortHint;
