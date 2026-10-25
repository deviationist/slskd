import { urlBase } from '../config';
import * as navigation from '../lib/navigation';
import React, { useEffect, useState } from 'react';
import { useHistory, useLocation } from 'react-router-dom';
import { Icon } from 'semantic-ui-react';

/**
 * A title bar along the top edge, for the installed iPhone app, which has no
 * back of its own: Back on the left, where iOS keeps it, and what the page is.
 *
 * Back is up, not history: shown only on a page that is one thing out of a
 * list or a lookup -- a search's results, a user, a browse -- and going to that
 * list. Between the sections themselves the tabs are the way to move, as in
 * any iOS app, so a top-level page has no Back however it was reached. The
 * title is centred on the bar rather than beside Back, so nothing moves.
 *
 * It keeps its own account of the history from the moment the app starts, so
 * that a page opened from its list goes back to it the way it came, and the
 * list is as it was left (see navigation.backTarget).
 *
 * Only drawn in the installed app, and only at the widths and heights App.css
 * calls a phone, where it is the page's top edge; App.css makes the room for it.
 * @param {object} params
 * @param {Function} params.onNavigate - Called before going anywhere: closes the menu.
 */
const TopBar = ({ onNavigate }) => {
  const history = useHistory();
  const location = useLocation();
  const [stack, setStack] = useState(() =>
    navigation.initialStack(history.location),
  );

  // the room for the bar is made by App.css under this class, so that a page
  // without the bar keeps its top edge
  useEffect(() => {
    document.documentElement.classList.add('has-top-bar');
    return () => document.documentElement.classList.remove('has-top-bar');
  }, []);

  useEffect(
    () =>
      history.listen((next, action) =>
        setStack((current) =>
          navigation.nextStack(current, { action, location: next }),
        ),
      ),
    [history],
  );

  const target = navigation.backTarget(stack, location.pathname, urlBase);

  const handleBack = () => {
    onNavigate();

    if (target.mode === 'back') {
      history.goBack();
    } else {
      // up replaces rather than adds, so that going up and then back does not
      // return to the page that was just left
      history.replace(target.to);
    }
  };

  return (
    <header className="top-bar">
      {target && (
        <button
          className="top-bar-back"
          onClick={handleBack}
          type="button"
        >
          <Icon name="chevron left" />
          <span>Back</span>
        </button>
      )}
      <h1 className="top-bar-title">
        {navigation.titleOf(location.pathname, urlBase)}
      </h1>
    </header>
  );
};

export default TopBar;
