import { urlBase } from '../config';
import * as navigation from '../lib/navigation';
import React, { useEffect, useState } from 'react';
import { useHistory, useLocation } from 'react-router-dom';
import { Icon } from 'semantic-ui-react';

/**
 * A title bar along the top edge, for the installed iPhone app, which has no
 * back of its own: Back on the left, where iOS keeps it, and what the page is.
 *
 * Back goes to where the app was before, or -- opened straight onto a search,
 * a user, a browse, with nowhere before it -- up to the list that one belongs
 * to. With neither it is not shown, as iOS shows none on a first page; the
 * title is centred on the bar rather than beside it, so nothing moves.
 *
 * It keeps its own account of the history from the moment the app starts,
 * since the browser will not say whether there is anything to go back to.
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
          navigation.nextStack(current, { action, key: next.key }),
        ),
      ),
    [history],
  );

  const canGoBack = stack.length > 1;
  const parent = navigation.parentOf(location.pathname, urlBase);

  const handleBack = () => {
    onNavigate();

    if (canGoBack) {
      history.goBack();
    } else {
      // up replaces rather than adds, so that going up and then back does not
      // return to the page that was just left
      history.replace(parent);
    }
  };

  return (
    <header className="top-bar">
      {(canGoBack || parent) && (
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
