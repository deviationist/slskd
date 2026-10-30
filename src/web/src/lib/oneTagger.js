import api from './api';
import { extensionOf, PLAYABLE } from './player/rows';
import * as transfers from './transfers';

/**
 * OneTagger, running as a service of its own: a finished download opened in
 * its Tag Editor, and a link to it in the menu. Both are off until the server
 * says otherwise (integrations.oneTagger).
 */

const settings = (options) => options?.integrations?.oneTagger;

/**
 * Whether downloads can be opened in OneTagger.
 */
export const isEnabled = (options) =>
  settings(options)?.enabled === true && Boolean(settings(options)?.url);

/**
 * OneTagger's address for the menu, or null when the menu leaves it out.
 */
export const menuUrl = (options) =>
  settings(options)?.menu === true && settings(options)?.url
    ? settings(options).url.replace(/\/+$/u, '')
    : null;

/**
 * Whether a download can be opened: finished, its file recorded and not known
 * to be gone, and audio -- the file the server resolves, deciding again for
 * itself.
 */
export const isOpenable = (file) =>
  transfers.isRetrievable(file) &&
  file.localFileExists !== false &&
  PLAYABLE.has(extensionOf(file.filename));

/**
 * Opens a download in OneTagger's Tag Editor, in a new tab.
 *
 * The tab is opened before the address is known and pointed at it after: a
 * browser lets a click open a window, but not a promise that settles later,
 * so asking first would have the tab blocked as a popup.
 */
export const open = async (id) => {
  const tab = window.open('', '_blank');

  try {
    const { url } = (
      await api.get(`/onetagger/downloads/${encodeURIComponent(id)}`)
    ).data;

    if (tab) {
      tab.opener = null;
      tab.location.href = url;
    } else {
      window.open(url, '_blank', 'noopener');
    }
  } catch (error) {
    tab?.close();
    throw error;
  }
};

/**
 * OneTagger's mark: the one path of its logo (client/src/assets/logo.svg,
 * Color-Fill-1) that holds the ring, the tag and the dot, cropped to its
 * bounding box, so it can take the colour of whatever it sits in.
 */
export const MARK = {
  path: 'M173.089,222.88c-22.812,5.07-47.23,3.021-68.74-6.21 c-27.01-11.33-49.08-33.73-60.1-60.86c-10.59-25.93-11.2-55.97-1.062-82.13c1.551,0.97,3.102,1.94,4.722,2.8 c1.028,6.34-1.86,12.3-2.802,18.45c-3.31,19.34-1.29,39.65,6.262,57.8c10.29,25.29,30.858,46.17,56,56.831 c21.778,9.359,46.77,11.021,69.59,4.551c24.729-6.801,46.568-23.199,60.33-44.812c10.83-16.8,16.568-36.82,16.358-56.8 c0.04-19.9-5.898-39.75-16.75-56.42C221.4,32.02,195.73,14.7,167.5,9.69c-18.852-3.31-38.79-1.83-56.562,5.49 c7.96,8.65,16.62,16.63,24.802,25.08c8.63,9.04,17.55,17.79,26.38,26.62c4.3,4.37,9.5,8.17,12.18,13.85 c3.26,6.41,3.13,13.8,3.06,20.81c-0.5,10.62,0.9,21.3-0.229,31.9c-0.45,5.35-5.44,9.59-10.73,9.63 c-9.62,0.29-19.26-0.08-28.898,0.08c-7.062,0.01-14.49,0.61-21.05-2.54c-6.79-2.73-11.512-8.49-16.562-13.49 c-10.51-10.49-20.95-21.06-31.56-31.45c-6.552-7.03-13.29-13.9-20.512-20.25c-2.92-2.83-5.979-5.65-7.988-9.22 c-2.24-4.5-0.9-10.23,2.83-13.52C57.36,38.03,72.04,23.36,86.68,8.66c2.3-2.39,5.43-4.13,8.83-3.97 c3.88-0.21,7.05,2.32,9.91,4.6c23.45-9.54,50.08-11.01,74.398-3.96c25.25,7.26,47.682,23.72,62.19,45.62 c12.52,18.62,19.13,41.12,18.77,63.55c-0.1,25.3-9.26,50.42-25.35,69.932C219.7,203.702,197.409,217.55,173.089,222.88z M136.64,114.47c0.102,6.38,6.43,11.86,12.771,10.94c6.479-0.59,11.47-7.38,9.92-13.76c-1.121-6.35-8.281-10.67-14.441-8.68 C140,104.39,136.33,109.35,136.64,114.47z',
  viewBox: '35.9 0.9 224.9 224.7',
};
