import React from 'react';

/**
 * Which key of the sort a column is, drawn beside its header label.
 *
 * Only where there is more than one -- `sortStateOf` decides that and hands
 * back nothing when the sort has a single key, since "1" against the only
 * sorted column answers a question nobody asked. Two arrows with no numbers
 * beside them, on the other hand, say which columns are sorted but not which
 * one wins.
 * @param {object} params
 * @param {number} params.rank - The key's position, 1-based, or nothing.
 * @returns {object} The marker, or nothing.
 */
const SortRank = ({ rank }) =>
  rank ? <span className="flatlist-sort-rank">{rank}</span> : null;

export default SortRank;
