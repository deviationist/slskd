import { urlBase } from '../config';
import * as navigation from '../lib/navigation';
import React, { useEffect, useState } from 'react';
import { NavLink, useHistory, useLocation } from 'react-router-dom';
import { Icon } from 'semantic-ui-react';

/*
 * The pages a phone gets one tap away. Everything else -- and everything the
 * scripts injected into this page add to the menu -- is under More.
 *
 * Search and Downloads are the two halves of what this instance is for, and
 * Uploads and Chat are the two that change without being asked: a peer
 * starts taking something, or says something. The dashboard is the landing
 * page, which is a reason to arrive at it, not a reason to go back to it.
 */
const PRIMARY = [
  { icon: 'search', label: 'Search', path: 'searches' },
  { icon: 'download', label: 'Downloads', path: 'downloads' },
  { icon: 'upload', label: 'Uploads', path: 'uploads' },
  { icon: 'comment', label: 'Chat', path: 'chat' },
];

/**
 * Back, for the installed iPhone app, which has no back of its own: to where
 * the app was before, or -- opened straight onto a search, a user, a browse,
 * with nowhere before it -- up to the list that one belongs to. Greyed when
 * there is neither, rather than gone, so the bar does not shift under a thumb.
 *
 * It keeps its own account of the history from the moment the app starts,
 * since the browser will not say whether there is anything to go back to.
 * @param {object} params
 * @param {Function} params.onClose - Closes the menu, as every item here does.
 */
const BackItem = ({ onClose }) => {
  const history = useHistory();
  const location = useLocation();
  const [stack, setStack] = useState(() =>
    navigation.initialStack(history.location),
  );

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

  const handleClick = () => {
    onClose();

    if (canGoBack) {
      history.goBack();
    } else if (parent) {
      // up replaces rather than adds, so that going up and then back does not
      // return to the page that was just left
      history.replace(parent);
    }
  };

  return (
    <button
      className="mobile-nav-item"
      disabled={!canGoBack && !parent}
      onClick={handleClick}
      type="button"
    >
      <Icon name="chevron left" />
      <span>Back</span>
    </button>
  );
};

/**
 * The navigation on a phone: a bar along the bottom edge, where a thumb is,
 * holding the pages used most and a More that opens the full menu.
 *
 * The full menu is not drawn here. It is the same menu the desktop shows
 * across the top, restyled by App.css into a panel above this bar while More
 * is open -- so there is one list of destinations rather than two that drift,
 * and what the scripts injected into the page add to the menu (they find it by
 * its first link to /downloads, which is why this bar comes after it in the
 * document) appears in the panel without either of them knowing.
 *
 * Only drawn at the widths and heights App.css calls a phone; above that it is
 * `display: none`, and the desktop menu is untouched.
 * @param {object} params
 * @param {boolean} params.agent - Whether this instance is a relay agent, which has only System.
 * @param {string|null} params.alert - A colour for the dot on More when something there wants attention.
 * @param {Function} params.onClose - Closes the menu.
 * @param {Function} params.onToggle - Opens or closes the menu.
 * @param {boolean} params.open - Whether the menu is open.
 * @returns {object} The bar, and the backdrop behind the menu while it is open.
 */
const MobileNav = ({ agent, alert, onClose, onToggle, open }) => (
  <>
    {open && (
      // a tap anywhere outside the panel closes it, as a sheet does; not a
      // button, because it is a second way to do what More already does
      // eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-static-element-interactions
      <div
        className="mobile-nav-backdrop"
        onClick={onClose}
      />
    )}
    <nav
      aria-label="Main"
      className="mobile-nav"
    >
      {navigation.isStandaloneIos() && <BackItem onClose={onClose} />}
      {!agent &&
        PRIMARY.map(({ icon, label, path }) => (
          <NavLink
            activeClassName="active"
            className="mobile-nav-item"
            key={path}
            onClick={onClose}
            to={`${urlBase}/${path}`}
          >
            <Icon name={icon} />
            <span>{label}</span>
          </NavLink>
        ))}
      <button
        aria-expanded={open}
        className={`mobile-nav-item${open ? ' active' : ''}`}
        onClick={onToggle}
        type="button"
      >
        <span className="mobile-nav-more">
          <Icon name={open ? 'close' : 'bars'} />
          {alert && !open && (
            <span
              className={`mobile-nav-dot mobile-nav-dot-${alert}`}
              title="Something under More wants attention"
            />
          )}
        </span>
        <span>More</span>
      </button>
    </nav>
  </>
);

export default MobileNav;
