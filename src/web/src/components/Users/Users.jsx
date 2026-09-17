import './Users.css';
import { activeUserInfoKey, urlBase } from '../../config';
import * as users from '../../lib/users';
import PlaceholderSegment from '../Shared/PlaceholderSegment';
import User from './User';
import React, { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useHistory, useLocation, useParams } from 'react-router-dom';
import { Icon, Input, Item, Loader, Message, Segment } from 'semantic-ui-react';

/**
 * The username a route parameter names.
 *
 * A malformed escape throws rather than returning the text it could not
 * decode, and the parameter comes from whatever is in the address bar -- so a
 * bad one leaves the page empty rather than breaking the render.
 * @param {string} parameter - The raw route parameter.
 * @returns {string|undefined} The username.
 */
const usernameFrom = (parameter) => {
  if (!parameter) {
    return undefined;
  }

  try {
    return decodeURIComponent(parameter);
  } catch {
    return undefined;
  }
};

/**
 * One of `Promise.allSettled`'s results, as `describeLookup` wants it.
 *
 * The API's own words where it refused -- `data` is the message it sent, and
 * a request that never reached it has only the axios error to offer.
 * @param {object} result - The settled result.
 * @returns {{ok: boolean, data?: object, reason?: string}} The outcome.
 */
const settled = (result) =>
  result.status === 'fulfilled'
    ? { data: result.value.data, ok: true }
    : {
        ok: false,
        reason:
          result.reason?.response?.data ??
          result.reason?.message ??
          'see the log',
      };

const Users = () => {
  const location = useLocation();
  const history = useHistory();
  const { username: usernameParameter } = useParams();
  const inputRef = useRef();
  const [user, setUser] = useState();
  const [note, setNote] = useState();
  const [usernameInput, setUsernameInput] = useState();

  /*
   * Which user is shown lives in the route rather than in state, so the page
   * can be linked to -- from the User column of either table, and from
   * anywhere else that has a username in its hand. localStorage keeps the last
   * one for a bare /users, which is what the menu leads to.
   */
  const selectedUsername = usernameFrom(usernameParameter);
  // eslint-disable-next-line react/hook-use-state
  const [{ error, fetching }, setStatus] = useState({
    error: undefined,
    fetching: false,
  });

  const setInputText = (text) => {
    inputRef.current.inputRef.current.value = text;
  };

  const setInputFocus = () => {
    inputRef.current.focus();
  };

  const show = (username) => {
    if (username) {
      history.push(users.userPath(username));
    }
  };

  const clear = () => {
    localStorage.removeItem(activeUserInfoKey);
    setUser(undefined);
    setNote(undefined);
    setInputText('');
    setInputFocus();
    history.push(`${urlBase}/users`);
  };

  const keyUp = (event) => (event.key === 'Escape' ? clear() : '');

  useLayoutEffect(() => {
    document.removeEventListener('keyup', keyUp, false);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    document.addEventListener('keyup', keyUp, false);

    // an address naming a user is the whole instruction; there is nothing to
    // remember or restore
    if (selectedUsername) {
      return;
    }

    const storedUsername =
      location.state?.user || localStorage.getItem(activeUserInfoKey);

    // into the address rather than into state, so that the user being shown is
    // always the user the address names -- and `replace`, because arriving at
    // /users and going back to where it sent you should not require two
    // presses of the back button
    if (storedUsername) {
      history.replace(users.userPath(storedUsername));
    }
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  /*
   * The box follows the address, which changes under this component when a
   * link elsewhere names a user and when the back button retraces one.
   */
  useEffect(() => {
    if (selectedUsername) {
      setInputText(selectedUsername);
      return;
    }

    setUser(undefined);
    setNote(undefined);
    setInputText('');
  }, [selectedUsername]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    const fetchUser = async () => {
      if (!selectedUsername) {
        return;
      }

      setStatus({ error: undefined, fetching: true });
      setNote(undefined);

      /*
       * Settled rather than all: the three requests do not fail together, and
       * the two that fail for an offline user are the two the server cannot
       * answer on their behalf. `Promise.all` threw away the one that
       * succeeded -- the one that knew the user was offline -- and reported
       * the lookup as broken.
       */
      const [info, status, endpoint] = await Promise.allSettled([
        users.getInfo({ username: selectedUsername }),
        users.getStatus({ username: selectedUsername }),
        users.getEndpoint({ username: selectedUsername }),
      ]);

      const outcome = users.describeLookup({
        endpoint: settled(endpoint),
        info: settled(info),
        status: settled(status),
        username: selectedUsername,
      });

      if (outcome.error) {
        setUser(undefined);
        setStatus({ error: outcome.error, fetching: false });
        return;
      }

      localStorage.setItem(activeUserInfoKey, selectedUsername);
      setUser(outcome.user);
      setNote(outcome.note);
      setStatus({ error: undefined, fetching: false });
    };

    fetchUser();
  }, [selectedUsername]);

  return (
    <div className="users-container">
      <Segment
        className="users-segment"
        raised
      >
        <div className="users-segment-icon">
          <Icon
            name="users"
            size="big"
          />
        </div>
        <Input
          action={
            !fetching &&
            (user == null
              ? {
                  icon: 'search',
                  onClick: () => show(usernameInput),
                }
              : { color: 'red', icon: 'x', onClick: clear })
          }
          className="users-input"
          disabled={fetching}
          input={
            <input
              data-lpignore="true"
              disabled={Boolean(user) || fetching}
              placeholder="Username"
              type="search"
            />
          }
          loading={fetching}
          onChange={(event) => setUsernameInput(event.target.value)}
          onKeyUp={(event) =>
            event.key === 'Enter' ? show(usernameInput) : ''
          }
          placeholder="Username"
          ref={inputRef}
          size="big"
        />
      </Segment>
      {fetching ? (
        <Loader
          active
          className="search-loader"
          inline="centered"
          size="big"
        />
      ) : (
        <div>
          {error ? (
            <Message
              content={error}
              negative
            />
          ) : user == null ? (
            <PlaceholderSegment
              caption="No user info to display"
              icon="users"
            />
          ) : (
            <>
              <Segment
                className="users-user"
                raised
              >
                <Item.Group>
                  <User {...user} />
                </Item.Group>
              </Segment>
              {note && (
                <Message
                  content={note.body}
                  header={note.heading}
                  info
                />
              )}
            </>
          )}
        </div>
      )}
    </div>
  );
};

export default Users;
