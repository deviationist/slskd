import { urlBase } from '../config';
import React, { useRef } from 'react';
import { NavLink, useHistory } from 'react-router-dom';
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

/*
 * Acting on the finger lifting rather than on the click that follows it.
 *
 * iOS decides for itself whether a tap becomes a click, and while the page
 * is still drawing it can decide that it does not: it takes the tap for a
 * hover, and the click only comes on a second one -- or, under a page busy
 * drawing, after a wait. Pointer events are not subject to that, so the bar
 * moves on `pointerup` from a finger that has not travelled, and the click
 * that may or may not arrive afterwards is swallowed so the tap does not count
 * twice. A mouse, a pen and the keyboard still go through the click.
 */
const SLOP = 10;
const SWALLOW_MS = 800;

const useTap = (action) => {
  const down = useRef(null);
  const tapped = useRef(0);

  return {
    onClick: (event) => {
      if (Date.now() - tapped.current < SWALLOW_MS) {
        event.preventDefault();
        return;
      }

      action(event);
    },
    onPointerDown: (event) => {
      down.current =
        event.pointerType === 'touch'
          ? { x: event.clientX, y: event.clientY }
          : null;
    },
    onPointerUp: (event) => {
      const start = down.current;
      down.current = null;

      if (
        !start ||
        event.pointerType !== 'touch' ||
        Math.abs(event.clientX - start.x) > SLOP ||
        Math.abs(event.clientY - start.y) > SLOP
      ) {
        return;
      }

      tapped.current = Date.now();
      action(event);
    },
  };
};

const NavItem = ({ icon, label, onClose, to }) => {
  const history = useHistory();
  const tap = useTap((event) => {
    onClose();

    // a click is the link's own to follow; a tap has to be followed by hand
    if (event.type === 'pointerup') history.push(to);
  });

  return (
    <NavLink
      activeClassName="active"
      className="mobile-nav-item"
      to={to}
      {...tap}
    >
      <Icon name={icon} />
      <span>{label}</span>
    </NavLink>
  );
};

const MoreButton = ({ alert, onToggle, open }) => {
  const tap = useTap(() => onToggle());

  return (
    <button
      aria-expanded={open}
      className={`mobile-nav-item mobile-nav-toggle${open ? ' active' : ''}`}
      type="button"
      {...tap}
    >
      <span className="mobile-nav-more">
        {/* three bars that fold into a cross, rather than one icon swapped
            for another: the change is the point, so it is shown happening */}
        <span
          aria-hidden="true"
          className="mobile-nav-burger"
        >
          <span />
          <span />
          <span />
        </span>
        {alert && !open && (
          <span
            className={`mobile-nav-dot mobile-nav-dot-${alert}`}
            title="Something under More wants attention"
          />
        )}
      </span>
      <span>{open ? 'Close' : 'More'}</span>
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
      {/* the four in a box of their own, which draws the bar's top edge: while
          the menu is open it stops short of More and turns down beside it */}
      {!agent && (
        <div className="mobile-nav-primary">
          {PRIMARY.map(({ icon, label, path }) => (
            <NavItem
              icon={icon}
              key={path}
              label={label}
              onClose={onClose}
              to={`${urlBase}/${path}`}
            />
          ))}
        </div>
      )}
      <MoreButton
        alert={alert}
        onToggle={onToggle}
        open={open}
      />
    </nav>
  </>
);

export default MobileNav;
