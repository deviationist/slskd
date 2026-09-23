import { timestampParts } from '../../lib/util';
import React from 'react';

/**
 * A date and time, drawn the same way everywhere in the UI.
 *
 * Always a `<time>` carrying the exact instant, with the full date and time as
 * its tooltip. The text depends on `variant`:
 *
 * - `short` (the default), for tables and lists: the time alone if it is
 *   today, the day and time if it is earlier this year, and the year as well
 *   if it is older than that.
 * - `full`, for a detail view or anywhere the complete date should show
 *   regardless: always the whole date and time.
 *
 * With nothing to show it renders `placeholder`, which is empty by default:
 * pass `-` where a missing value means something -- a search that has not
 * ended -- and leave it blank where it only means there is nothing to say.
 * @param {object} props
 * @param {string|number|Date} props.at - The timestamp.
 * @param {string} [props.variant] - 'short' or 'full'.
 * @param {string} [props.placeholder] - What to show when there is no instant.
 * @param {number} [props.now] - The present, in ms; a list can pass one so every row measures "today" alike.
 * @param {string} [props.className] - Passed to the element.
 * @returns {object} The timestamp.
 */
const Timestamp = ({
  at,
  className,
  now = Date.now(),
  placeholder = '',
  variant = 'short',
}) => {
  const parts = timestampParts({ at, now, variant });

  if (parts === null) {
    return placeholder;
  }

  return (
    <time
      className={className}
      dateTime={parts.dateTime}
      title={parts.title}
    >
      {parts.text}
    </time>
  );
};

export default Timestamp;
