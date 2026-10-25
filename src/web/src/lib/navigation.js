/**
 * Going back, for where the browser offers no way to.
 *
 * A page added to an iPhone's home screen opens without Safari around it: no
 * back button, and no swipe back either. A search's results, a user, a browse
 * of someone's files -- each one thing out of a list or a lookup -- then had
 * no way back to that list short of knowing which tab it lives under.
 *
 * Back here is up, not history: it is shown only on a page with a parent, and
 * it goes to that parent. Between the sections themselves the tabs are the
 * way to move, as in any iOS app. The app keeps its own account of where it
 * has been only to go up the way it came when it came from there, so that the
 * list is as it was left -- scrolled, filtered -- rather than drawn again.
 */

// what a location with no key is called: the browser's history gives the
// entry the app was opened on none, and gives it none again when it is
// returned to
const INITIAL = 'initial';

/**
 * Whether this is the installed iPhone or iPad app, the one place with
 * neither a back button nor a back gesture. `navigator.standalone` is set by
 * iOS for a page opened from the home screen and by nothing else; Android
 * keeps its system back, and a browser its own button.
 */
export const isStandaloneIos = () =>
  typeof window !== 'undefined' && window.navigator?.standalone === true;

const entryOf = (location) => ({
  key: location?.key ?? INITIAL,
  pathname: location?.pathname ?? '',
});

/**
 * The entries of the app's own history, oldest first, after a navigation:
 * each a location's key and path.
 *
 * The browser does not say which way a pop went, so it is worked out from the
 * key: the entry before this one means back, any other entry already seen
 * means back that far, and one never seen starts the account again rather than
 * guess. Only the last case is a loss, and only of the way back past it.
 * @param {object[]} stack - The entries so far.
 * @param {object} navigation
 * @param {string} navigation.action - PUSH, REPLACE or POP.
 * @param {object} navigation.location - The location arrived at.
 * @returns {object[]} The entries now.
 */
export const nextStack = (stack, { action, location }) => {
  const entry = entryOf(location);

  if (action === 'PUSH') {
    return [...stack, entry];
  }

  if (action === 'REPLACE') {
    return [...stack.slice(0, -1), entry];
  }

  const at = stack.map((item) => item.key).lastIndexOf(entry.key);

  return at === -1 ? [entry] : stack.slice(0, at + 1);
};

/**
 * The account to start from, for the location the app was opened on.
 * @param {object} location - The location.
 * @returns {object[]} The entries.
 */
export const initialStack = (location) => [entryOf(location)];

// the pages that show one thing out of a list or a lookup, and the page that
// is the list or the lookup: up from a search is its list, from a user is
// Users, from a share of someone's files is Browse
const PARENTS = [
  [/^\/searches\/[^/]+\/?$/u, '/searches'],
  [/^\/users\/[^/]+\/?$/u, '/users'],
  [/^\/browse\/[^/]+\/?$/u, '/browse'],
];

const prefixOf = (base) =>
  base && base !== '/' ? base.replace(/\/$/u, '') : '';

const withoutBase = (pathname, prefix) =>
  prefix && pathname.startsWith(prefix)
    ? pathname.slice(prefix.length)
    : pathname;

/**
 * Where up is from the specified page, for when there is no back: the app was
 * opened on it, or reopened, and has no history to go back through.
 * @param {string} pathname - The page's path.
 * @param {string} base - The URL base the app is served under.
 * @returns {string|null} The parent's path, or null for a page without one.
 */
export const parentOf = (pathname, base = '') => {
  const prefix = prefixOf(base);
  const path = withoutBase(pathname, prefix);

  const parent = PARENTS.find(([pattern]) => pattern.test(path));

  return parent ? `${prefix}${parent[1]}` : null;
};

// what each page is called, by the first part of its path: the names the menu
// gives them, so that the bar says what was tapped
const TITLES = {
  browse: 'Browse',
  chat: 'Chat',
  dashboard: 'Dashboard',
  downloads: 'Downloads',
  rooms: 'Rooms',
  searches: 'Search',
  shared: 'Shared',
  system: 'System',
  uploads: 'Uploads',
  users: 'Users',
};

const decode = (segment) => {
  try {
    return decodeURIComponent(segment);
  } catch {
    // a segment that is not valid percent-encoding is shown as it is
    return segment;
  }
};

/**
 * What to call the specified page in a title bar: its name in the menu, or,
 * for a user or a browse of one, whose.
 * @param {string} pathname - The page's path.
 * @param {string} base - The URL base the app is served under.
 * @returns {string} The title, or an empty string for a page with no name.
 */
export const titleOf = (pathname, base = '') => {
  const [page, item] = withoutBase(pathname, prefixOf(base))
    .split('/')
    .filter(Boolean);

  if (item && (page === 'users' || page === 'browse')) {
    return decode(item);
  }

  return TITLES[page] ?? '';
};

const samePath = (a, b) => a.replace(/\/+$/u, '') === b.replace(/\/+$/u, '');

/**
 * What Back does on the specified page: nothing, for a page with no parent --
 * it is not shown there -- and otherwise goes up to the parent. `back` when the
 * page was reached from that parent, so stepping back in history returns to
 * the list as it was left; `up` to the parent's path otherwise, replacing the
 * page rather than adding to the history, so that Back then up does not loop.
 * @param {object[]} stack - The app's history, as nextStack keeps it.
 * @param {string} pathname - The page's path.
 * @param {string} base - The URL base the app is served under.
 * @returns {object|null} `{ mode: 'back' }`, `{ mode: 'up', to }`, or null.
 */
export const backTarget = (stack, pathname, base = '') => {
  const parent = parentOf(pathname, base);

  if (!parent) {
    return null;
  }

  const previous = stack.length > 1 ? stack[stack.length - 2] : null;

  return previous && samePath(previous.pathname, parent)
    ? { mode: 'back' }
    : { mode: 'up', to: parent };
};
