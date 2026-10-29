# Compare — plan

slskd gets its own **Compare**: tick some finished downloads, press Compare,
and be told which of them are the same track, judged from the audio rather than
the names. As with the player's analysis, slskd does not judge anything itself.
It asks a **compare provider** through a written contract and draws the answer.
media-bridge is the first provider.

Today Compare is a guest. media-bridge's `web/slskd-ingest.js` injects the button
and dialog into slskd's page through a `sub_filter` on xavi's nginx, and the
browser calls `/x-pipeline/preview/compare` on that vhost, which nginx proxies to
media-bridge with the pipeline token. The script has to decorate both the card
rows and the table view, from `data-*` attributes it hopes stay put. It is the
same arrangement the player left on 2026-10-27, for the same reasons.

## Decisions this rests on

1. **The button and the dialog are slskd's.** They are built into slskd's
   React, on the Downloads page, for both the card view and the table view.
   Nothing in them knows about media-bridge.
2. **The engine is not replicated.** `sametrack.py` stays in media-bridge. Its
   thresholds were calibrated on 14 copies of one remix through ffmpeg and
   numpy. media-bridge also needs it for itself: its own app's Compare, and the
   Ingress duplicate check (`DUPLICATE_AUDIO=1`).
3. **The document describes itself.** The provider sends the verdict, the
   headline and the per-file notes as text, as the analysis contract does with
   findings. slskd draws them without knowing what a match score of 0.95 means.
   That moves the rules that are written twice in JS today (`verdictOf`,
   `differences`, `CLOSE_MATCH`) into Python once. The numbers are sent too, for
   anyone who wants them; slskd does not read them.
4. **Server to server.** The browser talks only to slskd. slskd resolves the
   ticked transfers to files and asks the provider. The provider's token stays
   in slskd's config, and the browser no longer goes through `/x-pipeline`.
5. **Opt-in, on both sides.** In slskd, `transfers.compare.enabled` defaults to
   `false`, and the button is drawn only when it is on *and* a provider is
   configured. In media-bridge, `/contract/v1/compare` shares the analysis
   token and roots but answers 503 unless `COMPARE_PROVIDER=1`: being asked
   about one file at a time is not consent to a dozen decodes per request.
6. **At most 10 files** per comparison (TODO item 3). slskd enforces it, and the
   button says so before it is pressed. media-bridge keeps its own ceiling of 12
   for its own app; the contract lets a provider refuse more than it takes.
7. **The injected Compare is removed** once slskd's is live (phase 5). **The
   injection itself stays**: `slskd-ingest.js` keeps Send to ingress (the
   dialog, the rename, clearing the row), and `slskd-menu.js` keeps the nav links
   and the System → Media bridge tab. Only Compare's section goes, plus the
   favicon repoint in `slskd-menu.js`, which has been dead since upstream's own
   fix (`bcbb9839`, 2026-09-09).

## The compare contract (v1)

Written up for providers in `docs/transfers-compare-provider.md`, with a JSON
Schema beside it, in the same form as `docs/player-analysis-provider.md`.

```
POST {url}/v1/compare
Authorization: Bearer <token>
Content-Type: application/json

{ "files": [ { "root": "downloads", "file": "complete/peer/Album/01 A.aiff",
               "size": 51234567, "mtime": 1761730000000 }, … ] }
```

Files are named as in the analysis contract: a root name plus a path relative to
it, never an absolute path, with size and mtime so the provider can tell it is
looking at the same file. There are 2 to `max` files.

| Status | Means | slskd does |
|---|---|---|
| 200 | The comparison, below | Keeps it for this exact set of files, and shows it |
| 202 + `Retry-After` | Still being computed | Asks again with the same body after that long |
| 400 | Fewer than 2 files, or more than the provider takes | Says so in the dialog |
| 409 | A file's size or mtime disagree | Asks once more after a moment, then gives up (as in the analysis contract) |
| 503 | The provider is not configured for this | "Compare unavailable" |
| other | A provider fault | "Compare unavailable"; backs off |

**A file the provider will not or cannot read does not fail the request.** In a
search's worth of copies, one broken download is the ordinary case. Such a file
comes back as `readable: false`, with the same words whether it was refused,
missing or undecodable, so the answer reveals nothing about what exists. The
analysis contract's path rules apply to each file unchanged (`contract.resolve`).

A cold comparison decodes every file at about 4 seconds each, so ten files take
40 seconds. The provider may answer that synchronously, and media-bridge does,
as its analysis route does. 202 is in the contract for a provider that would
rather not. Either way the browser never waits on the provider: it polls slskd,
which waits for the provider up to `timeout`. Once the features are cached, a
repeat answers at once.

```jsonc
{
  "contract": 1,
  "provider": { "name": "media-bridge", "version": "sametrack-v1" },
  "verdict": {
    "level": "same",          // same | likely | mixed | partly | different | unknown
    "headline": "Yes — the same track",
    "detail": "98% match · 2.1 s longer · starts 0.4 s later"
  },
  "files": [                  // in request order
    { "readable": true, "music_s": 431.2 },
    { "readable": false, "note": "could not be read" }
  ],
  "groups": [                 // files that are one track, largest first
    { "files": [0, 2], "notes": ["the others are compared with this one", "98% match · 2.1 s longer"] }
  ],
  "alone": [3],               // readable, and the same as nothing else here
  "partly": [ { "a": 1, "b": 3, "note": "62% of their length matches, then they part ways" } ],
  "pairs": [ { "a": 0, "b": 2, "verdict": "same", "match": 0.98, "share": 1.0,
               "offset_s": 0.4, "drift_ms": 0, "gain_db": -1.2 } ]
}
```

`level` has its own six values rather than the analysis contract's
`ok`/`warn`/`bad`: "same, beyond doubt", "very likely the same", "some of them
are", "partly" and "different" are each a different answer, and slskd chooses
the colours. Unknown fields are ignored, as in the analysis contract.

## slskd's side

- **Options:** `transfers.compare.enabled` (default `false`) and
  `transfers.compare.provider` with `url`, `token`, `timeout` and `roots`, the
  same shape as `player.analysis.provider`. On quim they get the
  same values: media-bridge's provider token and roots are read-only on both
  routes, so one set of secrets serves both.
- **`CompareProvider`**, modelled on `AnalysisProvider`: one request in flight
  per set of files, answers kept per set (each file's path, size and mtime,
  sorted), and negative answers kept briefly.
- **`POST /api/v0/transfers/downloads/compare`** with `{ ids }` and an optional
  `wait`. It accepts only the user's own finished downloads whose files still
  exist, the same check as `isShareable`/`isRetrievable`. It answers with the
  provider's document plus each file's transfer id, or `pending` with a
  `retryAfter`.
- **The UI:** a Compare button beside the selection's other actions on
  Downloads. It is off, with a reason in its tooltip, when fewer than 2 finished
  downloads are ticked or more than 10. The dialog opens at once and fills in
  when the answer lands (the injected one learned that "Checking…" for half a
  minute reads as stuck). Each file in it has a play button through the player.
- **Tests:** status mapping, keying and caching (xUnit); the dialog drawn from
  fixture documents (Jest); the request built by the client checked against the
  schema.

## media-bridge's side

- `/contract/v1/compare` beside `/contract/v1/analysis`, behind the same bearer
  token and `ANALYSIS_ROOTS`, each file resolved by `contract.resolve`.
- The verdict and notes are made in Python (`contract.compare_document`), ported
  from `verdictOf`/`differences` with their tests. media-bridge's own app can
  then use the same text and drop its copy of `differences()` in `Compare.tsx`.
- Synchronous, like the analysis route: no worker and no 202. `sametrack`'s
  feature cache makes a repeat immediate.
- Off unless `COMPARE_PROVIDER=1`, on top of the analysis token and roots.

## Phasing

1. **Contract.** The doc and schema in slskd, then media-bridge's
   `/contract/v1/compare` with its tests. It ships on its own and changes
   nothing visible.
2. **slskd backend.** Options, `CompareProvider` and the route. Testable with
   curl.
3. **slskd UI.** The button, the dialog and the 10-file cap.
4. **Deploy and configure** on quim: build, set `transfers.compare.provider`,
   check it against the live provider.
5. **media-bridge cleanup.** Remove Compare from `slskd-ingest.js` (button,
   dialog, styles, `COMPARE_MAX`), leaving Send to ingress as it is. Remove the
   dead favicon repoint from `slskd-menu.js`. Drop the 300-second `proxy_read_timeout` on
   xavi's `slskd.ichiva.no` `/x-pipeline/` if nothing else there needs it.
   Update media-bridge's AGENTS.md: Compare joins the player among what slskd
   does itself. `/preview/compare` stays, because media-bridge's own app uses
   it.

## Decided

- **Two provider sections**, not one shared: `player.analysis.provider` and
  `transfers.compare.provider`, configured with the same values on quim, so each
  feature turns on and off on its own (2026-10-29).
- **media-bridge's own app keeps its Compare as it is** for now. Switching it to
  the provider's text, which would remove the last duplicate of the rules in
  `Compare.tsx`, is deferred (2026-10-29).
