import './Users.css';
import { activeUserInfoKey, urlBase } from '../../config';
import * as users from '../../lib/users';
import PlaceholderSegment from '../Shared/PlaceholderSegment';
import User from './User';
import React, { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useHistory, useLocation, useParams } from 'react-router-dom';
import { Icon, Input, Item, Loader, Segment } from 'semantic-ui-react';

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

const Users = () => {
  const location = useLocation();
  const history = useHistory();
  const { username: usernameParameter } = useParams();
  const inputRef = useRef();
  const [user, setUser] = useState();
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
    setInputText('');
  }, [selectedUsername]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    const fetchUser = async () => {
      if (!selectedUsername) {
        return;
      }

      setStatus({ error: undefined, fetching: true });

      try {
        const [info, status, endpoint] = await Promise.all([
          users.getInfo({ username: selectedUsername }),
          users.getStatus({ username: selectedUsername }),
          users.getEndpoint({ username: selectedUsername }),
        ]);

        localStorage.setItem(activeUserInfoKey, selectedUsername);
        setUser({ ...info.data, ...status.data, ...endpoint.data });
        setStatus({ error: undefined, fetching: false });
      } catch (fetchError) {
        setStatus({ error: fetchError, fetching: false });
      }
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
            <span>Failed to retrieve information for {selectedUsername}</span>
          ) : user == null ? (
            <PlaceholderSegment
              caption="No user info to display"
              icon="users"
            />
          ) : (
            <Segment
              className="users-user"
              raised
            >
              <Item.Group>
                <User {...user} />
              </Item.Group>
            </Segment>
          )}
        </div>
      )}
    </div>
  );
};

export default Users;
