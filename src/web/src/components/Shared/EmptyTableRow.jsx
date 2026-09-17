import React from 'react';
import { Table } from 'semantic-ui-react';

/**
 * One row, across every column, saying why there are none.
 *
 * A table that draws its header and then nothing reads as one still loading,
 * or as a fault. This keeps the header, the sort and the column picker where
 * they are -- which is the point: the filter that emptied the table is one of
 * the controls still on screen, and replacing the whole table with a notice
 * takes it away at the moment it is needed.
 * @param {object} params
 * @param {number} params.columns - How many columns the table is drawing.
 * @param {string} params.children - What to say.
 * @returns {object} The row.
 */
const EmptyTableRow = ({ children, columns }) => (
  <Table.Row>
    <Table.Cell
      className="flatlist-empty"
      colSpan={columns}
    >
      {children}
    </Table.Cell>
  </Table.Row>
);

export default EmptyTableRow;
