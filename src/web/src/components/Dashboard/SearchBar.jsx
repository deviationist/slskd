import '../Search/Search.css';
import { urlBase } from '../../config';
import * as library from '../../lib/searches';
import * as watchLibrary from '../../lib/watches';
import WatchModal from '../Search/WatchModal';
import React, { useEffect, useRef, useState } from 'react';
import { Link, useHistory } from 'react-router-dom';
import { toast } from 'react-toastify';
import { Button, Icon, Input, Popup, Segment } from 'semantic-ui-react';
import { v4 as uuidv4 } from 'uuid';

const SearchBar = ({ server } = {}) => {
  const [creating, setCreating] = useState(false);
  const [watchDraft, setWatchDraft] = useState(undefined);

  const inputRef = useRef();
  const history = useHistory();

  const create = async ({ navigate = false } = {}) => {
    const ref = inputRef?.current?.inputRef?.current;
    const searchText = ref.value;
    const id = uuidv4();

    try {
      setCreating(true);
      await library.create({ id, searchText });

      ref.value = '';
      ref.focus();

      setCreating(false);

      if (navigate) {
        history.push(`${urlBase}/searches/${id}`);
      } else {
        const label =
          searchText.length > 30 ? `${searchText.slice(0, 15)}...` : searchText;
        toast.info(
          <span>
            Search for &lsquo;{label}&rsquo; started.{' '}
            <Link to={`${urlBase}/searches/${id}`}>View results</Link>
          </span>,
        );
      }
    } catch (createError) {
      console.error(createError);
      toast.error(
        createError?.response?.data ?? createError?.message ?? createError,
      );
      setCreating(false);
    }
  };

  const createWatch = async (watch) => {
    const ref = inputRef?.current?.inputRef?.current;
    const searchText = watchDraft?.searchText ?? ref?.value;

    try {
      const { id } = await watchLibrary.createWatchedSearch({
        searchText,
        watch,
      });

      setWatchDraft(undefined);

      try {
        ref.value = '';
      } catch {
        // the input is not mounted; nothing to clear
      }

      toast.success(
        <span>
          Watching &lsquo;{searchText}&rsquo;.{' '}
          <Link to={`${urlBase}/searches/${id}`}>View</Link>
        </span>,
      );
    } catch (watchError) {
      console.error(watchError);
      toast.error(
        watchError?.response?.data ?? watchError?.message ?? watchError,
      );
    }
  };

  useEffect(() => {
    inputRef?.current?.inputRef?.current?.focus();
  }, []);

  return (
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
              disabled={creating || !server?.isConnected}
              icon="plus"
              onClick={create}
            />
            <Popup
              content="Re-run this search on a schedule and email me what is new"
              position="bottom center"
              trigger={
                <Button
                  disabled={creating || !server?.isConnected}
                  icon="clock outline"
                  onClick={() => {
                    const searchText =
                      inputRef?.current?.inputRef?.current?.value ?? '';
                    const validation = library.validateSearchText(searchText);

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
              disabled={creating || !server?.isConnected}
              icon="search"
              onClick={() => create({ navigate: true })}
            />
          </>
        }
        className="search-input"
        disabled={creating || !server?.isConnected}
        input={
          <input
            data-lpignore="true"
            placeholder={
              server?.isConnected
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
      {watchDraft && (
        <WatchModal
          onClose={() => setWatchDraft(undefined)}
          onSave={createWatch}
          onSearchTextChange={(searchText) => {
            setWatchDraft({ searchText });

            // the field underneath is uncontrolled and read from a ref, so it
            // has to be written to directly
            const input = inputRef?.current?.inputRef?.current;

            if (input) {
              input.value = searchText;
            }
          }}
          open
          searchText={watchDraft.searchText}
        />
      )}
    </Segment>
  );
};

export default SearchBar;
