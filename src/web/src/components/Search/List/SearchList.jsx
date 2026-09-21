import ErrorSegment from '../../Shared/ErrorSegment';
import Switch from '../../Shared/Switch';
import SearchListRow from './SearchListRow';
import React from 'react';
import { Button, Card, Icon, Loader, Popup, Table } from 'semantic-ui-react';

const SearchList = ({
  clearable = 0,
  clearing = false,
  connecting = false,
  error = undefined,
  onClear = () => {},
  onRemove = () => {},
  onStop = () => {},
  searches = {},
  watches = {},
}) => {
  return (
    <Card
      className="search-list-card"
      raised
    >
      <Card.Content>
        {/*
          Above the list rather than beside the search box: it is about what is
          in the list, and the box above is about adding to it. Disabled with
          nothing to take, which is also how a list of nothing but watched
          searches says so.
        */}
        <div className="search-list-actions">
          <Popup
            content={
              clearable > 0
                ? 'Remove the finished searches, keeping the ones being watched'
                : 'Nothing to clear: every search here is being watched or is still running'
            }
            position="left center"
            trigger={
              <span>
                <Button
                  basic
                  compact
                  disabled={clearable === 0 || clearing}
                  icon="trash alternate"
                  loading={clearing}
                  onClick={onClear}
                  size="tiny"
                >
                  <Icon name="trash alternate" />
                  {`Clear ${clearable > 0 ? clearable : ''}`.trim()}
                </Button>
              </span>
            }
          />
        </div>
        <div className="search-list-wrapper">
          <Switch
            connecting={
              connecting && (
                <Loader
                  active
                  inline="centered"
                  size="small"
                />
              )
            }
            error={error && <ErrorSegment caption={error} />}
          >
            <Table
              className="unstackable"
              size="large"
            >
              <Table.Header>
                <Table.Row>
                  <Table.HeaderCell className="search-list-action">
                    <Icon name="info circle" />
                  </Table.HeaderCell>
                  <Table.HeaderCell className="search-list-phrase">
                    Search
                  </Table.HeaderCell>
                  <Table.HeaderCell className="search-list-files">
                    Files
                  </Table.HeaderCell>
                  <Table.HeaderCell className="search-list-locked">
                    Locked
                  </Table.HeaderCell>
                  <Table.HeaderCell className="search-list-responses">
                    Responses
                  </Table.HeaderCell>
                  <Table.HeaderCell className="search-list-started">
                    Ended
                  </Table.HeaderCell>
                  <Table.HeaderCell className="search-list-action" />
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {Object.values(searches)
                  .sort((a, b) => new Date(b.startedAt) - new Date(a.startedAt))
                  .map((search) => (
                    <SearchListRow
                      key={search.id}
                      onRemove={onRemove}
                      onStop={onStop}
                      search={search}
                      watch={watches[search.id]}
                    />
                  ))}
              </Table.Body>
            </Table>
          </Switch>
        </div>
      </Card.Content>
    </Card>
  );
};

export default SearchList;
