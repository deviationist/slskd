import { urlBase } from '../config';

// the service worker behind the offline page (public/sw.js). Production builds only, and only on a secure origin,
// which is the only place a browser allows one. Each load also asks the worker to fetch the offline page again,
// which is how a changed message reaches an installed app.
export const registerServiceWorker = async () => {
  if (
    // eslint-disable-next-line n/no-process-env
    process.env.NODE_ENV !== 'production' ||
    !('serviceWorker' in navigator) ||
    !window.isSecureContext
  ) {
    return;
  }

  try {
    await navigator.serviceWorker.register(`${urlBase}/sw.js`, {
      scope: `${urlBase}/`,
    });

    const registration = await navigator.serviceWorker.ready;
    registration.active?.postMessage('refresh-offline-page');
  } catch {
    // without a worker the app works exactly as it did; only the offline page is missing
  }
};
