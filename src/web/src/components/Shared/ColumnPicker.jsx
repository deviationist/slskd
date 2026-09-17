import { moveColumn, withColumn } from '../../lib/tables';
import React from 'react';
import { Button, Checkbox, Icon } from 'semantic-ui-react';

/**
 * Which columns a table shows, and in what order.
 *
 * The order is the reason this is a component rather than a list of
 * checkboxes: a checkbox says whether a column is there, and nothing says
 * where. Arrows rather than dragging, because a drag needs a pointer and this
 * list is read on a phone as often as anywhere else.
 *
 * Shown columns come first, in the order they are drawn, so the list reads as
 * the table does. The rest follow under a rule: they have no position to
 * change until they are switched on, and arrows against them would be arrows
 * that do nothing.
 * @param {object} params
 * @param {object[]} params.all - Every column the table has.
 * @param {string[]} params.columns - The keys shown, in order.
 * @param {Function} params.onChange - Given the new list of keys.
 * @returns {object} The picker.
 */
const ColumnPicker = ({ all = [], columns = [], onChange }) => {
  const shown = columns
    .map((key) => all.find((col) => col.key === key))
    .filter(Boolean);
  const hidden = all.filter((col) => !columns.includes(col.key));

  const move = (key, by) => onChange(moveColumn({ by, columns, key }));
  const toggle = (key, on) => onChange(withColumn({ all, columns, key, on }));

  return (
    <div className="flatlist-columns-menu">
      {shown.map((col, index) => (
        <div
          className="flatlist-columns-row"
          key={col.key}
        >
          <Checkbox
            checked
            label={col.label}
            onChange={() => toggle(col.key, false)}
          />
          <Button.Group
            basic
            size="mini"
          >
            <Button
              aria-label={`Move ${col.label} left`}
              disabled={index === 0}
              icon
              onClick={() => move(col.key, -1)}
              type="button"
            >
              <Icon name="chevron left" />
            </Button>
            <Button
              aria-label={`Move ${col.label} right`}
              disabled={index === shown.length - 1}
              icon
              onClick={() => move(col.key, 1)}
              type="button"
            >
              <Icon name="chevron right" />
            </Button>
          </Button.Group>
        </div>
      ))}
      {hidden.length > 0 && (
        <div className="flatlist-columns-hidden">
          {shown.length > 0 && <div className="flatlist-columns-divider" />}
          {hidden.map((col) => (
            <div
              className="flatlist-columns-row"
              key={col.key}
            >
              <Checkbox
                checked={false}
                label={col.label}
                onChange={() => toggle(col.key, true)}
              />
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

export default ColumnPicker;
