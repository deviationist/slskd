import {
  isStateCancellable,
  isStateRetryable,
  SORT_OPTIONS,
} from '../../lib/transfers';
import { Div } from '../Shared';
import ShrinkableDropdownButton from '../Shared/ShrinkableDropdownButton';
import React, { useMemo, useState } from 'react';
import { Checkbox, Dropdown, Icon, Input, Segment } from 'semantic-ui-react';

/*
 * Where the action buttons give up their labels for their icons.
 *
 * Upstream's 715px was measured when this header held an icon and three
 * buttons. It now also holds a filter and the Table View toggle, and the
 * buttons at full width are 752px of the 770px a 800px window leaves --
 * so by the time this fired the row had already overflowed the segment.
 *
 * At this width the header wraps instead: the buttons take a line of their
 * own, where they have the room to keep their labels. Below it they would not
 * fit even on a line of their own, so the labels go and the tooltips carry
 * them.
 */
const SHRINK_BUTTONS = '(max-width: 800px)';

const getRetryableFiles = ({ files, retryOption }) => {
  switch (retryOption) {
    case 'Errored':
      return files.filter((file) =>
        [
          'Completed, TimedOut',
          'Completed, Errored',
          'Completed, Rejected',
        ].includes(file.state),
      );
    case 'Cancelled':
      return files.filter((file) => file.state === 'Completed, Cancelled');
    case 'All':
      return files.filter((file) => isStateRetryable(file.state));
    default:
      return [];
  }
};

const getCancellableFiles = ({ cancelOption, files }) => {
  switch (cancelOption) {
    case 'All':
      return files.filter((file) => isStateCancellable(file.state));
    case 'Queued':
      return files.filter((file) =>
        ['Queued, Locally', 'Queued, Remotely'].includes(file.state),
      );
    case 'In Progress':
      return files.filter((file) => file.state === 'InProgress');
    default:
      return [];
  }
};

const getRemovableFiles = ({ files, removeOption }) => {
  switch (removeOption) {
    case 'Succeeded':
      return files.filter((file) => file.state === 'Completed, Succeeded');
    case 'Errored':
      return files.filter((file) =>
        [
          'Completed, TimedOut',
          'Completed, Errored',
          'Completed, Rejected',
        ].includes(file.state),
      );
    case 'Cancelled':
      return files.filter((file) => file.state === 'Completed, Cancelled');
    case 'Completed':
      return files.filter((file) => file.state.includes('Completed'));
    default:
      return [];
  }
};

const TransfersHeader = ({
  cancelling = false,
  direction,
  filter = '',
  flat,
  onCancelAll,
  onFilterChange,
  onFlatChange,
  onRemoveAll,
  onRetryAll,
  onSortChange,
  removing = false,
  retrying = false,
  server = { isConnected: true },
  sort,
  transfers,
}) => {
  const [removeOption, setRemoveOption] = useState('Succeeded');
  const [cancelOption, setCancelOption] = useState('All');
  const [retryOption, setRetryOption] = useState('Errored');

  const files = useMemo(() => {
    return transfers
      .reduce((accumulator, username) => {
        const allUserFiles = username.directories.reduce(
          (directoryAccumulator, directory) => {
            return directoryAccumulator.concat(directory.files);
          },
          [],
        );

        return accumulator.concat(allUserFiles);
      }, [])
      .filter((file) => file.direction.toLowerCase() === direction);
  }, [direction, transfers]);

  const empty = files.length === 0;
  const working = retrying || cancelling || removing;

  return (
    <Segment
      className="transfers-header-segment"
      raised
    >
      <div className="transfers-segment-icon">
        <Icon
          name={direction}
          size="big"
        />
      </div>
      <Div
        className="transfers-header-sort"
        hidden={empty || flat}
      >
        <Dropdown
          button
          className="icon"
          /* the arrow follows the order, so the button says which way the list
             runs without having to be opened */
          icon={sort === 'oldest' ? 'sort amount up' : 'sort amount down'}
          labeled
          onChange={(_, data) => onSortChange(data.value)}
          options={SORT_OPTIONS}
          text={SORT_OPTIONS.find((option) => option.value === sort)?.text}
          value={sort}
        />
      </Div>
      <Div
        className="transfers-header-filter"
        hidden={empty}
      >
        {/*
         * Matched against the peer, the whole remote path and the state, so
         * `errored` finds the failures and a username finds one peer's queue
         * without either needing a control of its own. Applied before the
         * grouping, so both views answer the same question.
         */}
        <Input
          action={
            Boolean(filter) && {
              color: 'red',
              icon: 'x',
              onClick: () => onFilterChange(''),
            }
          }
          label={{ content: 'Filter', icon: 'filter' }}
          onChange={(_, data) => onFilterChange(data.value)}
          placeholder="flac -bob errored"
          value={filter}
        />
      </Div>
      {/*
       * A break, so that the view toggle and the actions take a line of their
       * own on a narrow window. An empty full-width flex item is how a flex
       * container is told where to wrap -- there is no `break-before` for flex
       * items -- and it is display:none above the width where it is wanted.
       */}
      <Div
        className="transfers-header-break"
        hidden={empty}
      />
      <Div
        className="transfers-header-view"
        hidden={empty}
      >
        {/*
         * A second way to read the same transfers: one row per file, with the
         * peer and the folder as columns, sortable. The card view answers
         * "what is this peer sending me"; this answers "what is in the
         * queue". Remembered per direction, like the sort.
         *
         * Beside the actions rather than beside the sort, because it is the
         * control an operator reaches for, and the sort it replaces is hidden
         * while it is on. Its own element rather than the first of them, so
         * that on a line of their own it can sit at one end and they at the
         * other.
         */}
        <Checkbox
          checked={flat}
          className="transfers-header-flat"
          label="Table View"
          onChange={() => onFlatChange(!flat)}
          toggle
        />
      </Div>
      <Div
        className="transfers-header-buttons"
        hidden={empty}
      >
        <ShrinkableDropdownButton
          color="green"
          disabled={working || empty || !server.isConnected}
          hidden={direction === 'upload'}
          icon="redo"
          loading={retrying}
          mediaQuery={SHRINK_BUTTONS}
          onChange={(_, data) => setRetryOption(data.value)}
          onClick={() => onRetryAll(getRetryableFiles({ files, retryOption }))}
          options={[
            { key: 'errored', text: 'Errored', value: 'Errored' },
            { key: 'cancelled', text: 'Cancelled', value: 'Cancelled' },
            { key: 'all', text: 'All', value: 'All' },
          ]}
        >
          {`Retry ${retryOption === 'All' ? retryOption : `All ${retryOption}`}`}
        </ShrinkableDropdownButton>
        <ShrinkableDropdownButton
          color="red"
          disabled={working || empty}
          icon="x"
          loading={cancelling}
          mediaQuery={SHRINK_BUTTONS}
          onChange={(_, data) => setCancelOption(data.value)}
          onClick={() =>
            onCancelAll(getCancellableFiles({ cancelOption, files }))
          }
          options={[
            { key: 'all', text: 'All', value: 'All' },
            { key: 'queued', text: 'Queued', value: 'Queued' },
            { key: 'inProgress', text: 'In Progress', value: 'In Progress' },
          ]}
        >
          {`Cancel ${cancelOption === 'All' ? cancelOption : `All ${cancelOption}`}`}
        </ShrinkableDropdownButton>
        <ShrinkableDropdownButton
          disabled={working || empty}
          icon="trash alternate"
          loading={removing}
          mediaQuery={SHRINK_BUTTONS}
          onChange={(_, data) => setRemoveOption(data.value)}
          onClick={() =>
            onRemoveAll(getRemovableFiles({ files, removeOption }))
          }
          options={[
            { key: 'succeeded', text: 'Succeeded', value: 'Succeeded' },
            { key: 'errored', text: 'Errored', value: 'Errored' },
            { key: 'cancelled', text: 'Cancelled', value: 'Cancelled' },
            { key: 'completed', text: 'Completed', value: 'Completed' },
          ]}
        >
          {`Remove All ${removeOption}`}
        </ShrinkableDropdownButton>
      </Div>
    </Segment>
  );
};

export default TransfersHeader;
