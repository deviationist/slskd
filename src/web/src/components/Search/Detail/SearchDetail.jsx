import {
  filterResponse,
  flattenResponses,
  getResponses,
  indexDownloads,
  parseFiltersFromString,
} from '../../../lib/searches';
import { getAll as getTransfers } from '../../../lib/transfers';
import { sleep } from '../../../lib/util';
import * as watchLibrary from '../../../lib/watches';
import ErrorSegment from '../../Shared/ErrorSegment';
import LoaderSegment from '../../Shared/LoaderSegment';
import Switch from '../../Shared/Switch';
import FlatFileList from '../FlatFileList';
import Response from '../Response';
import WatchModal from '../WatchModal';
import SearchDetailHeader from './SearchDetailHeader';
import WatchPanel from './WatchPanel';
import React, { useEffect, useMemo, useState } from 'react';
import { toast } from 'react-toastify';
import { Button, Checkbox, Dropdown, Input, Segment } from 'semantic-ui-react';

const sortDropdownOptions = [
  {
    key: 'uploadSpeed',
    text: 'Upload Speed (Fastest to Slowest)',
    value: 'uploadSpeed',
  },
  {
    key: 'queueLength',
    text: 'Queue Depth (Least to Most)',
    value: 'queueLength',
  },
];

/**
 * The footer under the grouped results: more to show, or why some are not.
 *
 * Its own component because the page it sits on is at the linter's complexity
 * ceiling, and a nested ternary in the middle of a long render is the first
 * thing worth lifting out of one.
 * @param {object} params
 * @param {number} params.filteredCount - Results hidden by the filters.
 * @param {Function} params.onShowMore - Reveals the next page.
 * @param {number} params.remainingCount - Results not yet drawn.
 * @returns {object} The footer, or nothing when there is neither to report.
 */
const ShowMore = ({ filteredCount, onShowMore, remainingCount }) => {
  if (remainingCount > 0) {
    return (
      <Button
        className="showmore-button"
        fluid
        onClick={onShowMore}
        primary
        size="large"
      >
        Show {remainingCount > 5 ? 5 : remainingCount} More Results{' '}
        {`(${remainingCount} remaining, ${filteredCount} hidden by filter(s))`}
      </Button>
    );
  }

  if (filteredCount > 0) {
    return (
      <Button
        className="showmore-button"
        disabled
        fluid
        size="large"
      >{`All results shown. ${filteredCount} results hidden by filter(s)`}</Button>
    );
  }

  return null;
};

/*
 * Whether the results are drawn as one row per file or as one card per user.
 *
 * Remembered, unlike the filter toggles beside it: those describe *this*
 * search and are reasonably forgotten when the page reloads, while this is a
 * preference about how the operator reads results at all. Per browser, since
 * that is where a display preference belongs.
 *
 * Guarded on both sides because localStorage throws outright in a browser with
 * site data blocked, and the read runs on first render -- an exception there
 * would cost the whole page rather than a preference.
 */
const FLAT_STORAGE_KEY = 'slskd-search-flat-results';

const readStoredFlat = () => {
  try {
    return window.localStorage.getItem(FLAT_STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
};

const storeFlat = (flat) => {
  try {
    window.localStorage.setItem(FLAT_STORAGE_KEY, String(flat));
  } catch {
    // a preference that cannot be saved is still a preference for this tab
  }
};

const SearchDetail = ({
  creating,
  disabled,
  onCreate,
  onRemove,
  onStop,
  removing,
  search,
  stopping,
}) => {
  const { fileCount, id, isComplete, lockedFileCount, responseCount, state } =
    search;

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(undefined);

  // the watch on this search, owned here because both the header and the panel
  // below it need the same answer -- the header to decide whether to offer one,
  // the panel to show the one that exists
  const [watch, setWatch] = useState(undefined);
  const [watching, setWatching] = useState(false);

  const [results, setResults] = useState([]);

  // filters and sorting options
  const [hiddenResults, setHiddenResults] = useState([]);
  const [resultSort, setResultSort] = useState('uploadSpeed');
  const [hideLocked, setHideLocked] = useState(true);
  const [hideNoFreeSlots, setHideNoFreeSlots] = useState(false);
  const [foldResults, setFoldResults] = useState(false);
  const [flatResults, setFlatResults] = useState(readStoredFlat);
  const [downloads, setDownloads] = useState(() => indexDownloads());
  const [resultFilters, setResultFilters] = useState('');
  const [displayCount, setDisplayCount] = useState(5);

  // when the search transitions from !isComplete -> isComplete,
  // fetch the results from the server
  useEffect(() => {
    const get = async () => {
      try {
        setLoading(true);

        // the results may not be ready yet.  this is very rare, but
        // if it happens the search will complete with no results.
        await sleep(500);

        const responses = await getResponses({ id });
        setResults(responses);
        setLoading(false);
      } catch (getError) {
        setError(getError);
        setLoading(false);
      }
    };

    if (isComplete) {
      get();
    }
  }, [id, isComplete]);

  // apply sorting and filters.  this can take a while for larger result
  // sets, so memoize it.
  const sortedAndFilteredResults = useMemo(() => {
    const sortOptions = {
      queueLength: { field: 'queueLength', order: 'asc' },
      uploadSpeed: { field: 'uploadSpeed', order: 'desc' },
    };

    const { field, order } = sortOptions[resultSort];

    const filters = parseFiltersFromString(resultFilters);

    return results
      .filter((r) => !hiddenResults.includes(r.username))
      .map((r) => {
        if (hideLocked) {
          return { ...r, lockedFileCount: 0, lockedFiles: [] };
        }

        return r;
      })
      .map((response) => filterResponse({ filters, response }))
      .filter((r) => r.fileCount + r.lockedFileCount > 0)
      .filter((r) => !(hideNoFreeSlots && !r.hasFreeUploadSlot))
      .sort((a, b) => {
        if (order === 'asc') {
          return a[field] - b[field];
        }

        return b[field] - a[field];
      });
  }, [
    hiddenResults,
    hideLocked,
    hideNoFreeSlots,
    resultFilters,
    resultSort,
    results,
  ]);

  // when a user uses the action buttons, we will *probably* re-use this component,
  // but with a new search ID.  clear everything to prepare for the transition
  const reset = () => {
    setLoading(false);
    setError(undefined);
    setResults([]);
    setHiddenResults([]);
    setDisplayCount(5);
  };

  const create = async ({ navigate, search: searchForCreate }) => {
    reset();
    onCreate({ navigate, search: searchForCreate });
  };

  const remove = async () => {
    reset();
    onRemove(search);
  };

  // from the filtered responses, never from `results`: every filter on this
  // page -- the filter box, hide-locked, hide-no-free-slots, and hiding a user
  // -- works on responses, so flattening afterwards inherits all four
  const flatRows = useMemo(
    () => flattenResponses({ responses: sortedAndFilteredResults }),
    [sortedAndFilteredResults],
  );

  const filteredCount = results?.length - sortedAndFilteredResults.length;
  const remainingCount = sortedAndFilteredResults.length - displayCount;
  /*
   * What has been downloaded, so a result can say that it already has been.
   *
   * Polled rather than pushed: there is no transfers hub, and the transfers
   * page itself polls this endpoint every second. Five is the interval for a
   * secondary signal on another page -- enough that a row lights up shortly
   * after its download is enqueued from here, cheap enough not to matter.
   *
   * Only while the flat list is showing. The grouped view does not mark
   * anything, so polling behind it would be a request a second for nothing.
   */
  useEffect(() => {
    if (!flatResults) {
      return undefined;
    }

    let cancelled = false;

    const poll = async () => {
      try {
        const users = await getTransfers({ direction: 'download' });

        if (!cancelled) {
          setDownloads(indexDownloads(users));
        }
      } catch {
        // a failed poll leaves the last answer in place: marks going stale for
        // five seconds is a better outcome than the list losing them entirely
      }
    };

    poll();

    const interval = window.setInterval(poll, 5_000);

    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [flatResults]);

  const loadWatch = async () => {
    try {
      const found = await watchLibrary.get({ id });

      setWatch(found);
      return found;
    } catch {
      // a 404 is the ordinary case: most searches are not watched
      setWatch(undefined);
      return undefined;
    }
  };

  useEffect(() => {
    loadWatch();
  }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

  const loaded = !removing && !creating && !loading && results;

  if (error) {
    return <ErrorSegment caption={error?.message ?? error} />;
  }

  return (
    <>
      <SearchDetailHeader
        creating={creating}
        disabled={disabled}
        loaded={loaded}
        loading={loading}
        onCreate={create}
        onRemove={remove}
        onStop={onStop}
        onWatch={() => setWatching(true)}
        removing={removing}
        search={search}
        stopping={stopping}
        watch={watch}
      />
      <Switch
        loading={loading && <LoaderSegment />}
        searching={
          !isComplete && (
            <LoaderSegment>
              {state === 'InProgress'
                ? `Found ${fileCount} files ${
                    lockedFileCount > 0
                      ? `(plus ${lockedFileCount} locked) `
                      : ''
                  }from ${responseCount} users`
                : 'Loading results...'}
            </LoaderSegment>
          )
        }
      >
        <WatchPanel
          onWatchChanged={loadWatch}
          searchId={search.id}
          searchText={search.searchText}
          watch={watch}
        />
        {loaded && (
          <Segment
            className="search-options"
            raised
          >
            {/*
             * The dropdown sorts whole peers, which is the only thing that
             * can be sorted when the results are one card each. The flat list
             * sorts files, from its own column headers -- so it is hidden
             * there rather than left as a second control answering a
             * different question about the same list.
             *
             * It still runs underneath: with no column chosen the rows arrive
             * in the order it put the peers in, which is the same default the
             * list has always had.
             */}
            {!flatResults && (
              <Dropdown
                button
                className="search-options-sort icon"
                floating
                icon="sort"
                labeled
                onChange={(_event, { value }) => setResultSort(value)}
                options={sortDropdownOptions}
                text={
                  sortDropdownOptions.find((o) => o.value === resultSort).text
                }
              />
            )}
            <div className="search-option-toggles">
              <Checkbox
                checked={hideLocked}
                className="search-options-hide-locked"
                label="Hide Locked Results"
                onChange={() => setHideLocked(!hideLocked)}
                toggle
              />
              <Checkbox
                checked={hideNoFreeSlots}
                className="search-options-hide-no-slots"
                label="Hide Results with No Free Slots"
                onChange={() => setHideNoFreeSlots(!hideNoFreeSlots)}
                toggle
              />
              <Checkbox
                checked={foldResults}
                className="search-options-fold-results"
                // folding is a property of a per-user card, and the flat list
                // has none. left visible rather than hidden so the controls do
                // not move around under the pointer when the view changes
                disabled={flatResults}
                label="Fold Results"
                onChange={() => setFoldResults(!foldResults)}
                toggle
              />
              <Checkbox
                checked={flatResults}
                className="search-options-flat-results"
                label="Table View"
                onChange={() => {
                  setFlatResults(!flatResults);
                  storeFlat(!flatResults);
                }}
                toggle
              />
            </div>
            <Input
              action={
                Boolean(resultFilters) && {
                  color: 'red',
                  icon: 'x',
                  onClick: () => setResultFilters(''),
                }
              }
              className="search-filter"
              label={{ content: 'Filter', icon: 'filter' }}
              onChange={(_event, data) => setResultFilters(data.value)}
              placeholder="
                lackluster container -bothersome iscbr|isvbr islossless|islossy 
                minbitrate:320 minbitdepth:24 minfilesize:10 minfilesinfolder:8 minlength:5000
              "
              value={resultFilters}
            />
          </Segment>
        )}
        {loaded && flatResults && (
          <FlatFileList
            disabled={disabled}
            downloads={downloads}
            rows={flatRows}
          />
        )}
        {loaded &&
          !flatResults &&
          sortedAndFilteredResults.slice(0, displayCount).map((r) => (
            <Response
              disabled={disabled}
              isInitiallyFolded={foldResults}
              key={r.username}
              onHide={() => setHiddenResults([...hiddenResults, r.username])}
              response={r}
            />
          ))}
        {loaded && !flatResults && (
          <ShowMore
            filteredCount={filteredCount}
            onShowMore={() => setDisplayCount(displayCount + 5)}
            remainingCount={remainingCount}
          />
        )}
      </Switch>
      {watching && (
        <WatchModal
          onClose={() => setWatching(false)}
          onSave={async (created) => {
            try {
              await watchLibrary.put({ id, watch: created });
              await loadWatch();
              setWatching(false);
              toast.success(`Watching '${search.searchText}'`);
            } catch (watchError) {
              console.error(watchError);
              toast.error(
                watchError?.response?.data ?? watchError?.message ?? watchError,
              );
            }
          }}
          open
          searchText={search.searchText}
          // the search already exists and the watch's memory will be keyed on
          // its id, so the phrase is settled -- but this is still a new watch,
          // which is what keeps the seeding switch on offer
          searchTextFixed
        />
      )}
    </>
  );
};

export default SearchDetail;
