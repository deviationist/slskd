/*
    slskd's service worker. It does one thing: when opening a page fails, it
    shows slskd's own offline page instead of the browser's error. Everything
    else goes to the network untouched -- nothing of the app or the API is
    cached, so there is no stale UI to serve and online behaviour is exactly
    what it is without a worker.

    It lives at the root of the URL base rather than beside the manifest under
    pwa/, because a worker controls only the pages at or below the path it was
    served from.

    The offline page is fetched from the server (which renders the configured
    message into it) on install, and again each time the app asks -- the app
    does on every load -- so a changed message reaches the device on its next
    online visit.
*/

const CACHE = 'slskd-offline-v1';
const OFFLINE_URL = new URL('pwa/offline.html', self.registration.scope).href;

// a proxy in front of slskd answering for it: the server is reachable, slskd behind it is not
const GATEWAY_STATUSES = [502, 503, 504];

const refreshOfflinePage = async () => {
  const response = await fetch(OFFLINE_URL, { cache: 'no-store' });

  // anything but the page itself -- a login redirect from a proxy in front, an error -- keeps the copy already held
  if (
    response.ok &&
    !response.redirected &&
    (response.headers.get('content-type') ?? '').includes('text/html')
  ) {
    const cache = await caches.open(CACHE);
    await cache.put(OFFLINE_URL, response);
  }
};

// offline is when this runs as often as not, and a failed refresh leaves the copy already held, which is the point
const refreshOfflinePageQuietly = async () => {
  try {
    await refreshOfflinePage();
  } catch {
    // keep what is cached
  }
};

const offlineResponse = async (status, original) => {
  const cached = await caches.match(OFFLINE_URL);

  if (!cached) {
    // never cached: the proxy's own error page if there was one, otherwise the browser's
    return original ?? Response.error();
  }

  let html = await cached.text();

  if (status) {
    html = html.replace(
      'data-reason="unreachable"',
      `data-reason="gateway" data-status="${status}"`,
    );
  }

  return new Response(html, {
    headers: { 'Content-Type': 'text/html; charset=utf-8' },
  });
};

self.addEventListener('install', (event) => {
  event.waitUntil(refreshOfflinePageQuietly());
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      const keys = await caches.keys();
      await Promise.all(
        keys.filter((key) => key !== CACHE).map((key) => caches.delete(key)),
      );
      await self.clients.claim();
    })(),
  );
});

self.addEventListener('message', (event) => {
  if (event.data === 'refresh-offline-page') {
    event.waitUntil(refreshOfflinePageQuietly());
  }
});

self.addEventListener('fetch', (event) => {
  const { request } = event;

  if (request.mode !== 'navigate') {
    return;
  }

  // a navigation into the API is a file being downloaded, which is left to the browser rather than streamed
  // through here -- a long archive is the last thing that should depend on a worker staying alive
  const path = new URL(request.url).pathname;
  const api = new URL('api/', self.registration.scope).pathname;

  if (path.startsWith(api)) {
    return;
  }

  event.respondWith(
    (async () => {
      try {
        const response = await fetch(request);

        return GATEWAY_STATUSES.includes(response.status)
          ? offlineResponse(response.status, response)
          : response;
      } catch {
        return offlineResponse();
      }
    })(),
  );
});
