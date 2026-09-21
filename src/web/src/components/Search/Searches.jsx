import './Search.css';
import { createSearchHubConnection } from '../../lib/hubFactory';
import * as library from '../../lib/searches';
import * as watchLibrary from '../../lib/watches';
import ErrorSegment from '../Shared/ErrorSegment';
import LoaderSegment from '../Shared/LoaderSegment';
import PlaceholderSegment from '../Shared/PlaceholderSegment';
import SearchDetail from './Detail/SearchDetail';
import ClearSearchesModal from './List/ClearSearchesModal';
import SearchList from './List/SearchList';
import WatchModal from './WatchModal';
import React, { useEffect, useRef, useState } from 'react';
import { useHistory, useParams, useRouteMatch } from 'react-router-dom';
import { toast } from 'react-toastify';
import { Button, Icon, Input, Popup, Segment } from 'semantic-ui-react';
import { v4 as uuidv4 } from 'uuid';

const Searches = ({ server } = {}) => {
  const [connecting, setConnecting] = useState(true);
  const [error, setError] = useState(undefined);
  const [searches, setSearches] = useState({});

  const [removing, setRemoving] = useState(false);
  const [stopping, setStopping] = useState(false);
  const [creating, setCreating] = useState(false);
  const [watchDraft, setWatchDraft] = useState(undefined);
  const [watches, setWatches] = useState({});
  const [clearing, setClearing] = useState(false);
  const [confirmingClear, setConfirmingClear] = useState(false);

  const inputRef = useRef();

  const { id: searchId } = useParams();
  const history = useHistory();
  const match = useRouteMatch();

  const onConnecting = () => {
    setConnecting(true);
  };

  const onConnected = () => {
    setConnecting(false);
    setError(undefined);
  };

  const onConnectionError = (connectionError) => {
    setConnecting(false);
    setError(connectionError);
  };

  const onUpdate = (update) => {
    setSearches(update);
    onConnected();
  };

  useEffect(() => {
    onConnecting();

    const searchHub = createSearchHubConnection();

    searchHub.on('list', (searchesEvent) => {
      onUpdate(
        searchesEvent.reduce((accumulator, search) => {
          accumulator[search.id] = search;
          return accumulator;
        }, {}),
      );
      onConnected();
    });

    searchHub.on('update', (search) => {
      onUpdate((old) => ({ ...old, [search.id]: search }));
    });

    searchHub.on('delete', (search) => {
      onUpdate((old) => {
        delete old[search.id];
        return { ...old };
      });
    });

    searchHub.on('create', (search) => {
      onUpdate((old) => ({ ...old, [search.id]: search }));
    });

    searchHub.onreconnecting((connectionError) =>
      onConnectionError(connectionError?.message ?? 'Disconnected'),
    );
    searchHub.onreconnected(() => onConnected());
    searchHub.onclose((connectionError) =>
      onConnectionError(connectionError?.message ?? 'Disconnected'),
    );

    const connect = async () => {
      try {
        onConnecting();
        await searchHub.start();
      } catch (connectionError) {
        toast.error(connectionError?.message ?? 'Failed to connect');
        onConnectionError(connectionError?.message ?? 'Failed to connect');
      }
    };

    connect();

    const loadWatches = async () => {
      try {
        const all = await watchLibrary.getAll();

        setWatches(
          all.reduce((accumulator, watch) => {
            accumulator[watch.searchId] = watch;
            return accumulator;
          }, {}),
        );
      } catch (watchError) {
        // a list that cannot be badged is still a usable list
        console.error(watchError);
      }
    };

    loadWatches();

    return () => {
      searchHub.stop();
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // create a new search, and optionally navigate to it to display the details
  // we do this if the user clicks the search icon, or repeats an existing search
  const create = async ({ navigate = false, search } = {}) => {
    const ref = inputRef?.current?.inputRef?.current;
    const searchText = search || ref.value;
    const id = uuidv4();

    try {
      setCreating(true);
      await library.create({ id, searchText });

      try {
        ref.value = '';
        ref.focus();
      } catch {
        // we are probably repeating an existing search; the input isn't mounted.  no-op.
      }

      setCreating(false);

      if (navigate) {
        history.push(`${match.url.replace(`/${searchId}`, '')}/${id}`);
      }
    } catch (createError) {
      console.error(createError);
      toast.error(
        createError?.response?.data ?? createError?.message ?? createError,
      );
      setCreating(false);
    }
  };

  // create the search, then watch it. a watch is an extension of a search rather
  // than a thing of its own -- what it has already reported is keyed on the
  // search's id -- so there is nothing to watch until the search exists
  const createWatch = async (watch) => {
    const ref = inputRef?.current?.inputRef?.current;
    const searchText = watchDraft?.searchText ?? ref?.value;

    try {
      const { id, watch: saved } = await watchLibrary.createWatchedSearch({
        searchText,
        watch,
      });

      setWatches((old) => ({ ...old, [id]: saved }));
      setWatchDraft(undefined);

      try {
        ref.value = '';
      } catch {
        // the input is not mounted; nothing to clear
      }

      toast.success(`Watching '${searchText}'`);
    } catch (watchError) {
      console.error(watchError);
      toast.error(
        watchError?.response?.data ?? watchError?.message ?? watchError,
      );
    }
  };

  // the detail page owns the watch it is showing, and the list badges searches
  // from this map -- so the two have to be kept level, or a watch added or
  // removed in there is invisible here until the page is reloaded
  const onWatchChanged = ({ searchId: id, watch }) => {
    setWatches((old) => {
      if (!watch) {
        delete old[id];
        return { ...old };
      }

      return { ...old, [id]: watch };
    });
  };

  // delete a search
  const remove = async (search) => {
    try {
      setRemoving(true);

      await library.remove({ id: search.id });
      setSearches((old) => {
        delete old[search.id];
        return { ...old };
      });

      setRemoving(false);
    } catch (error_) {
      console.error(error_);
      toast.error(error?.response?.data ?? error?.message ?? error);
      setRemoving(false);
    }
  };

  /*
   * Clear the searches that are finished and not being watched.
   *
   * One request each rather than a bulk endpoint, because there is not one --
   * and the hub reports each deletion as it lands, so the list empties as it
   * goes rather than in a jump at the end. Failures are counted and named
   * once: a peer's search that will not delete is not a reason to stop
   * deleting the other forty.
   */
  const clear = async () => {
    const doomed = library.clearableSearches({ searches, watches });

    setClearing(true);
    setConfirmingClear(false);

    const failed = [];

    for (const search of doomed) {
      try {
        // eslint-disable-next-line no-await-in-loop
        await library.remove({ id: search.id });
        setSearches((old) => {
          delete old[search.id];
          return { ...old };
        });
      } catch (clearError) {
        console.error(clearError);
        failed.push(search.searchText);
      }
    }

    setClearing(false);

    if (failed.length > 0) {
      toast.error(
        `Could not remove ${failed.length} of ${doomed.length}: ${failed.slice(0, 3).join(', ')}`,
      );
      return;
    }

    toast.success(
      `Removed ${doomed.length} search${doomed.length === 1 ? '' : 'es'}`,
    );
  };

  // stop an in-progress search
  const stop = async (search) => {
    try {
      setStopping(true);
      await library.stop({ id: search.id });
      setStopping(false);
    } catch (stoppingError) {
      console.error(stoppingError);
      toast.error(
        stoppingError?.response?.data ??
          stoppingError?.message ??
          stoppingError,
      );
      setStopping(false);
    }
  };

  if (connecting) {
    return <LoaderSegment />;
  }

  if (error) {
    return <ErrorSegment caption={error?.message ?? error} />;
  }

  // if searchId is not null, there's an id in the route.
  // display the details for the search, if there is one
  if (searchId) {
    if (searches[searchId]) {
      return (
        <SearchDetail
          creating={creating}
          disabled={!server?.isConnected}
          onCreate={create}
          onRemove={remove}
          onStop={stop}
          onWatchChanged={onWatchChanged}
          removing={removing}
          search={searches[searchId]}
          stopping={stopping}
        />
      );
    }

    // if the searchId doesn't match a search we know about, chop
    // the id off of the url and force navigation back to the list
    history.replace(match.url.replace(`/${searchId}`, ''));
  }

  // not while a modal is open. this runs on *every* render, and the hub pushes
  // search updates continuously -- so an open modal had focus pulled out from
  // under it several times a second, which sent typing to the field behind it
  // and closed any menu the moment it was opened
  if (!watchDraft) {
    inputRef?.current?.inputRef?.current?.focus();
  }

  return (
    <>
      <Segment
        className="search-segment"
        raised
      >
        <div className="search-segment-icon">
          <Icon
            name="search"
            size="big"
          />
        </div>
        <Input
          action={
            <>
              <Button
                disabled={creating || !server.isConnected}
                icon="plus"
                onClick={create}
              />
              <Popup
                content="Re-run this search on a schedule and email me what is new"
                position="bottom center"
                trigger={
                  <Button
                    disabled={creating || !server.isConnected}
                    icon="clock outline"
                    onClick={() => {
                      const searchText =
                        inputRef?.current?.inputRef?.current?.value ?? '';
                      const validation = library.validateSearchText(searchText);

                      // refused here rather than at the end of the modal: this
                      // button is the only one of the three that does not reach
                      // the server, and a modal that cannot be saved is a worse
                      // way to learn there is nothing to search for
                      if (!validation.ok) {
                        toast.error(validation.reason);
                        return;
                      }

                      setWatchDraft({ searchText });
                    }}
                  />
                }
              />
              <Button
                disabled={creating || !server.isConnected}
                icon="search"
                onClick={() => create({ navigate: true })}
              />
            </>
          }
          className="search-input"
          disabled={creating || !server.isConnected}
          input={
            <input
              data-lpignore="true"
              placeholder={
                server.isConnected
                  ? 'Search phrase'
                  : 'Connect to server to perform a search'
              }
              type="search"
            />
          }
          loading={creating}
          onKeyUp={(keyUpEvent) => (keyUpEvent.key === 'Enter' ? create() : '')}
          placeholder="Search phrase"
          ref={inputRef}
          size="big"
        />
      </Segment>
      {Object.keys(searches).length === 0 ? (
        <PlaceholderSegment
          caption="No searches to display"
          icon="search"
        />
      ) : (
        <SearchList
          clearable={library.clearableSearches({ searches, watches }).length}
          clearing={clearing}
          connecting={connecting}
          error={error}
          onClear={() => setConfirmingClear(true)}
          onRemove={remove}
          onStop={stop}
          searches={searches}
          watches={watches}
        />
      )}
      {confirmingClear && (
        <ClearSearchesModal
          busy={clearing}
          onCancel={() => setConfirmingClear(false)}
          onConfirm={clear}
          plan={library.describeClear({
            clearable: library.clearableSearches({ searches, watches }),
            searches,
            watches,
          })}
        />
      )}
      {watchDraft && (
        <WatchModal
          onClose={() => setWatchDraft(undefined)}
          onSave={createWatch}
          onSearchTextChange={(searchText) => {
            setWatchDraft({ searchText });

            // the field underneath is uncontrolled and read from a ref, so it
            // has to be written to directly; leaving the two to disagree would
            // mean closing the modal silently reverted what was typed in it
            const input = inputRef?.current?.inputRef?.current;

            if (input) {
              input.value = searchText;
            }
          }}
          open
          searchText={watchDraft.searchText}
        />
      )}
    </>
  );
};

export default Searches;
