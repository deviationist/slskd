import { formatTime } from '../../../lib/util';
import * as watchLibrary from '../../../lib/watches';
import SearchStatusIcon from '../SearchStatusIcon';
import SearchActionIcon from './SearchActionIcon';
import React, { useState } from 'react';
import { Link, useRouteMatch } from 'react-router-dom';
import { Icon, Label, Popup, Table } from 'semantic-ui-react';

const SearchListRow = ({ onRemove, onStop, search, watch = undefined }) => {
  const [working, setWorking] = useState(false);
  const match = useRouteMatch();
  const badge = watchLibrary.watchBadge({ watch });

  const invoke = async (function_) => {
    setWorking(true);

    try {
      await function_();
    } catch (error) {
      console.error(error);
    } finally {
      setWorking(false);
    }
  };

  return (
    <Table.Row
      disabled={working}
      style={{ cursor: working ? 'wait' : undefined }}
    >
      <Table.Cell>
        <SearchStatusIcon state={search.state} />
      </Table.Cell>
      <Table.Cell className="search-list-phrase-cell">
        <Link to={`${match.url}/${search.id}`}>{search.searchText}</Link>
        {badge && (
          <Popup
            content={`${watchLibrary.describeRecurrence(watch.rrule)} — next run ${watchLibrary.describeNextRun({ watch })}`}
            position="right center"
            trigger={
              <Label
                color={badge.color}
                size="tiny"
              >
                <Icon name={badge.icon} />
                {badge.label}
              </Label>
            }
          />
        )}
      </Table.Cell>
      <Table.Cell>{search.fileCount}</Table.Cell>
      <Table.Cell>
        <Icon
          color="yellow"
          name="lock"
          size="small"
        />
        {search.lockedFileCount}
      </Table.Cell>
      <Table.Cell>{search.responseCount}</Table.Cell>
      <Table.Cell>
        {search.endedAt ? formatTime(search.endedAt) : '-'}
      </Table.Cell>
      <Table.Cell>
        <SearchActionIcon
          loading={working}
          onRemove={() => invoke(() => onRemove(search))}
          onStop={() => invoke(() => onStop(search))}
          search={search}
          style={{ cursor: 'pointer' }}
        />
      </Table.Cell>
    </Table.Row>
  );
};

export default SearchListRow;
