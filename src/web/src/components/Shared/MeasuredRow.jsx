import React from 'react';

/**
 * A table row the virtualiser can measure, for `Table.Row`'s `as`.
 *
 * Semantic's `Table.Row` is a function component and passes no ref down to
 * its `<tr>`, so the measuring ref travels as an ordinary prop and is attached
 * here. Wrapping the row in Semantic's `Ref` would do the same through
 * `findDOMNode`, which is deprecated.
 * @param {object} params
 * @param {Function} params.measureRef - The virtualiser's `measureElement`.
 * @returns {object} The row.
 */
const MeasuredRow = ({ measureRef, ...props }) => (
  <tr
    ref={measureRef}
    {...props}
  />
);

export default MeasuredRow;
