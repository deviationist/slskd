# Player — plan

slskd gets its own audio player for finished downloads, with an opt-in
**analysis provider**: an external service that slskd asks, through a written
contract, what is wrong with a track. media-bridge is the first provider.

Today both halves are a guest. media-bridge's `web/slskd-preview.js` (3,000
lines) and `.css` (1,300) are injected into slskd's page by a `sub_filter` on
xavi's nginx, and the audio, waveform and analysis come from media-bridge's
`/preview/*` routes. That worked on a desktop. Once the fork became usable on a
phone it stopped working: the guest cannot see the host's layout, so it guesses.
It measures only `.footer.ui.menu`, which on a phone is `display: none` when
its slot is empty, so the bar lands at `bottom: 0`, `z-index: 9000`, over the
bottom menu (`1003`).

Vocabulary: the **player** is the transport and everything drawn with it; the
**dock** is where it lives in slskd's layout; the **provider** is the external
analysis service; the **contract** is the HTTP + JSON shape between slskd and a
provider; an **analysis** is one provider answer for one file.

## Decisions this rests on

1. **The player is slskd's.** It is built into slskd's React, so the layout,
   the bottom menu, the theme and the stacking order are its own rather than
   reverse-engineered. Generic enough to offer upstream: nothing in it knows
   about media-bridge, Plex or Rekordbox.
2. **slskd serves its own audio.** The transcode, the waveform peaks and the
   embedded cover are replicated from media-bridge's `preview.py` in slskd's
   backend, with ffmpeg added to the image. Playback does not depend on any
   other service.
3. **Analysis is not replicated.** `quality.py` and `sametrack.py` stay in
   media-bridge as the single analyser. Their thresholds were calibrated
   against measured ground truth through ffmpeg + numpy; a C# port would need
   every one re-measured, and media-bridge needs the analyser for its own
   library anyway.
4. **The analysis UI is slskd's, the data is the provider's.** slskd draws the
   row badge, the lanes and the findings panel from a documented,
   self-describing JSON shape. Nothing is injected to show an analysis.
5. **Off by default.** No provider configured, no analysis UI. The player
   works without one.

**Not moving**: media-bridge's own React app keeps its player, and keeps
playing **both** kinds of source it plays today: Plex library tracks and
files sitting in slskd's download folders. Its `/preview/*` routes (audio,
peaks, art, tags, analyse) therefore stay, and stay on the xavi vhost, because
that app is served through it. What leaves media-bridge is only the copy of
the player injected into slskd's page. Ingest and Compare (`slskd-ingest.js`, `slskd-menu.js`)
stay injected, because they are media-bridge's business, and are pointed at
the player's public hook instead of at `slskd-preview.js` (see *Interop*).

## Layout

How players sit beside a bottom tab bar elsewhere (Spotify, SoundCloud,
YouTube Music, Apple Music): a mini player docked directly above the tabs,
tapped or swiped up into a full-screen player; on wider screens a full-width
bar along the bottom. Nobody makes the player a tab: it is a state, not a
destination, and pausing has to be one tap from anywhere.

| Width | Shape |
|---|---|
| Phone (the fork's `max-width: 767px` / landscape `max-height: 500px`) | **Mini bar** joined to the top of `MobileNav`, as one surface: cover, title, play/pause, a progress line along its top edge. Tap or swipe up → **drawer** (as media-bridge's app: cover, title, waveform with scrubbing, lanes, clock, meter, transport). Swipe down, the chevron, the backdrop or Escape folds it |
| Tablet and desktop | **Today's bar**, same layout, docked above the footer. With the touch fixes, because a tablet is a touch screen at desktop width |

Touch rules for every shape, all of which the injected player breaks today:

- Seeking is pointer events with capture: tap seeks, drag scrubs, audio
  follows every 250 ms (`SCRUB_STEP_MS`), release seeks exactly.
  `touch-action: none` on the waveform.
- The hover cursor and its time label are mouse only (`pointerType`); a
  phone's synthetic mouse events after a tap must not leave it stuck.
- Nothing is reachable only by hover: no hover-only spinner give-way, no
  `tr:hover` reveals, no reading that lives only in a `title`.
- Targets at least 44 px on touch layouts.
- Safe areas: `env(safe-area-inset-*)` on every edge the player touches.

### The dock and the stacking order

`App.jsx` renders an element for the player, and owns its position:

- Above `.footer.ui.menu` on desktop, above `.mobile-nav` on a phone, inside
  the safe area.
- slskd measures the dock (ResizeObserver) and publishes `--player-dock-h` on
  `:root`; `.app-content-body`'s bottom padding adds it to what it already
  reserves for the nav and footer. That replaces the injected `reflow()`,
  the spacer and the padding re-asserted on every tick.
- The drawer folds when More opens (`:root.nav-open`), and More stays usable
  while the player is open.

One table of z-indices in `App.css`, written down, so no layer picks a number
like 9000 again: page < footer < dock < mobile nav < nav panel < drawer
backdrop < drawer < toasts < modals and lightbox.

## Backend: serving audio

Replicates `media-bridge/src/media_bridge/preview.py` and the `/preview/*`
routes. The rules below are each there because the media-bridge version was
wrong in production first; they carry over as tests.

### Addressing

By **transfer id**. `Transfer.LocalFilename` is where the bytes are, recorded
when the file was moved into place, so slskd never has to reconstruct a path
from the peer's remote name (which media-bridge does today in
`resolve_preview_source`, and sometimes has to walk the tree for). Only
`Succeeded` downloads whose `LocalFilename` exists and is inside
`Directories.Downloads` (proved after resolving symlinks) are playable.
Anything else is 404; a transfer that is not yet complete is 409, so a caller
retries a 409 and gives up on a 404 (the same meaning media-bridge's routes use).

### Routes

Under `api/v0/player`, behind slskd's normal auth.

| Route | Does |
|---|---|
| `GET {id}/audio?codec=opus\|aac` | A browser-playable copy, honouring a single byte range |
| `GET {id}/peaks` | `{ duration, peaks[1600] }` |
| `GET {id}/art` | The embedded front cover, ETag'd; 404 when there is none |
| `GET {id}/tags` | Title, artist, album, for the bar |
| `GET {id}/analysis` | The provider's answer (see below); 404 when no provider is configured |

### Audio

- **Transcode, do not serve the original.** Most of `complete/` is AIFF, which
  Chrome and Firefox do not play. Opus/WebM by default at
  `-b:a 128k -compression_level 0` (1.85 s for a 5-minute AIFF where MP3 took
  6.8 s and native AAC 7.5 s).
- **AAC for WebKit** (`?codec=aac`, `-aac_coder fast`, `+faststart`, MP4):
  Safari plays Opus but its Web Audio tap reads silence from it, which kills
  the meter on an iPhone. The client chooses by engine.
- **`-vn` always**: these AIFFs carry a cover stream and the WebM mux fails on it.
- **`-t 3600`** (`MAX_SECONDS`): longer is a DJ set or a side rip.
- **MP3 up to 25 MB passes through** untouched: re-encoding lossy is
  generation loss for no gain.
- **A finished encode is verified in both directions** before it is
  published: the output's header duration (ffprobe) must match
  `min(source, MAX_SECONDS)` within 1 s. Too short is a truncation; *too
  long* is an encode that never finalised, because ffmpeg writes `-t`'s value
  into the WebM header up front. On 2026-09-07 a 6:17 track played to 5:27
  and showed `60:00 / 6:17`, served from a half-written cache entry that a
  one-sided check would have waved through.
- **A receipt per codec** (`{key}.audio.{codec}.json`) records the check, so a
  hit costs a read and not a probe. No receipt means build again, which also
  heals entries from older code.
- **Range** via `PhysicalFile(..., enableRangeProcessing: true)`, which
  handles 206/416, and `ETag` from the file identity.

### Peaks

ffmpeg to mono s16le at 8 kHz, 1600 buckets, each the loudest sample over
32768 (not 32767: −32768 is a legal sample), rounded to three places.
`duration` from the sample count, not a header: a truncated download
disagrees with its own header.

### Cache

- `{AppDirectory}/data/player-cache`, capped at 2 GB, least recently used
  evicted (a hit touches the mtime).
- Key = sha256 of resolved path + size + mtime, so a re-download under the
  same name is never served a stale waveform.
- One build at a time **per artifact** (a `SemaphoreSlim` keyed per
  `key:peaks` and `key:audio:codec`), never per file: the browser asks for
  peaks and audio together, and a shared lock makes the waveform wait out the
  transcode.
- Written staged-then-renamed, so a full disk never leaves a partial that
  passes for an entry.
- ffmpeg timeout 300 s (the source can be a cold NFS read), ffprobe 30 s.

### Image and options

- `ffmpeg` added to the `apt-get install` in the `Dockerfile` (noble's 6.1
  has libopus and the native AAC encoder).
- `player.enabled` (default true), `player.ffmpeg` and `player.ffprobe`
  (paths; default from `PATH`), `player.cache.directory`,
  `player.cache.capacity`. Documented in `docs/config.md`.
- Cover and tags through **TagLibSharp**, which slskd already ships. Check
  against the library before relying on it: media-bridge reads covers with
  mutagen because it needs the APIC *picture type* (front cover vs back or
  logo) and must not demux an 80 MB AIFF over NFS to get it. TagLib exposes
  `IPicture.Type`; that it reads AIFF's ID3 chunk without a full read needs
  measuring.

## The analysis contract (v1)

Server to server: **slskd's backend calls the provider**, and the browser
talks only to slskd. The token stays in slskd's config, there is one origin,
and slskd can cache and push. (Calling the provider from the browser made
sense only while the audio came from the provider too.)

### Request

```
GET {provider.url}/v1/analysis?root=<name>&file=<relative path>&size=<bytes>&mtime=<unix ms>
Authorization: Bearer <provider.token>
```

A file is named as a **root name plus a path relative to that root**, never as
an absolute path: `root=downloads&file=complete/Some User/Some Album/01
Track.aiff`. `size` and `mtime` let the provider refuse to answer about a
different file than the one slskd means.

### Path translation lives in the provider

slskd and the provider mount the same folders in different places (slskd's
container sees `/app/downloads`; media-bridge sees `/mnt/downloads/slsk`). The
mapping lives **in the provider**, as one table from root names to its own
folders, and that table is also the allow-list:

```
# media-bridge .env -- comma-separated, like RELOCATE_ROOTS
ANALYSIS_ROOTS=downloads=/mnt/downloads/slsk/complete,shared=/mnt/downloads/slsk-share
```

**Several roots, one folder per name.** A name covering two folders would let
one relative path match a file in each, and the provider would be choosing
which one slskd meant. Validated when the setting is read:

- Names unique, `[A-Za-z0-9_-]+` only, so a name cannot carry a path.
- Each folder exists and is a directory, compared **after resolving
  symlinks**.
- **No folder inside another.** Overlap gives one file two names and two
  cache entries, and lets a containment check pass for a folder only one of
  the names was meant to reach.
- Any failure turns the route off (503) and logs why. Never a partial table.

Why there and not in slskd:

- **The provider is the security boundary.** It is the one reading its disk,
  so it cannot take a caller's word for where a path points. A mapping kept in
  slskd would be a mapping the provider has to trust.
- **The allow-list and the translation are the same fact.** A folder is
  reachable *because* it has a name in the table; a name not in the table is
  refused. Keeping them in one place means they cannot disagree.
- **slskd should not know a provider's mounts.** A second provider, or
  media-bridge moving hosts, changes only the provider's config.

slskd's half is small: `player.analysis.provider.roots` maps the same names to
*its* directories (`downloads: /app/downloads`, and `shared: /app/shared` if
shares are ever playable), validated the same way, and slskd turns a
transfer's `LocalFilename` into the root containing it + relative path. Roots
cannot overlap, so there is only ever one. It never sends a path for a file
it does not hold as a transfer.

### The provider's checks, in order

All of them run before the filesystem is touched, apart from the last two.
Every refusal before step 5 is the same 403, so the answer never reveals
whether a file exists.

1. **`root` is a name in `ANALYSIS_ROOTS`.** Unset = the route is off (503).
2. **`file` is a clean relative path**, refused rather than repaired:
   - not empty, not absolute (no leading `/`), and no drive letter;
   - no NUL, no backslash (a separator on Windows and nowhere else, so
     different layers would disagree about what it splits);
   - no `..` and no `.` segment, and no empty segment (`a//b`). **Refuse, do
     not normalise and accept**: `normpath("a/../../etc")` gives a different
     file from the one written, and a caller who sent `..` meant something
     the contract does not allow;
   - no segment starting with `.` (dotfiles are infrastructure, such as the
     `.mounted` NFS marker and `.name.part` upload partials).
3. **The extension is a known audio format**: `INGESTABLE_EXTS`
   (`.mp3 .aiff .aif .flac .wav .m4a`), case-insensitive. It is the same set the
   upload route and the chain use, so it is not a seventh copy to maintain.
4. **Lexical containment**: `normpath(root + file)` is inside the root.
   Belt-and-braces after step 2, and what protects a future change to it.
5. **Containment after symlinks**: `resolve(strict=True)` is inside the
   root's resolved path, so a link planted inside the folder pointing out of
   it is refused. 404 if the file does not exist.
6. **A regular file**, and `size`/`mtime` agree (409 otherwise; see below).

No Unicode normalisation: NFC and NFD forms of a name can be two different
files on disk, so the path is used byte for byte as sent.

slskd applies steps 2 and 3 before sending as well, so a bad name never
leaves it. That is a courtesy, not the check.

### Responses

| Status | Means | slskd does |
|---|---|---|
| 200 | An analysis, below | Cache by file identity, push to the page |
| 202 + `Retry-After` | Being computed | Ask again after that long |
| 404 | The provider cannot see the file | Show no analysis; do not retry |
| 409 | `size`/`mtime` disagree with what it sees | Retry once after a short wait (NFS attribute cache), then give up |
| other | Provider fault | A muted "analysis unavailable" in the panel; back off |

### Body

Self-describing: slskd draws all of it without knowing what any code means.
Wording and thresholds stay in the provider, next to each other.

```jsonc
{
  "contract": 1,
  "provider": { "name": "media-bridge", "version": "<schema or build id>" },
  // The row badge and the bar's sub-line.
  "verdict": { "level": "ok" | "warn" | "bad", "headline": "Lossy source (16 kHz cut-off)" },
  // The panel's top line. Free text, so the provider owns the vocabulary.
  "summary": "AIFF · 16-bit · 44.1 kHz · stereo · 1411 kbps · 4:55",
  // The panel rows, worst first.
  "findings": [
    {
      "id": "lowpass",
      "label": "Frequency cut-off",
      "level": "bad",
      "value": "16.1 kHz",
      // Text runs, and runs that are seek points, so slskd does not parse
      // times out of prose (the injected player does, in `splitTimes`).
      "note": [ { "text": "A lossy source shows a shelf; first heard at " },
                { "text": "1:12", "seek": 72.0 }, { "text": "." } ]
    }
  ],
  // Optional. Where a finding sits in the track: weights 0..100 in equal
  // buckets across the decoded length. Never sent empty -- a lane present is
  // the signal it is worth drawing. Label and level come from the finding.
  "lanes": [ { "finding": "clipping", "buckets": [0, 0, 40, 100, ...] } ]
}
```

`contract` is bumped only for a change slskd cannot ignore. Fields slskd does
not know are ignored, so a provider can add more without coordinating.

### slskd's side

- Options: `player.analysis.provider.url`, `.token`, `.timeout` (default 60 s:
  a cold analysis decodes the whole file), `.concurrency` (default 2).
  Unset `url` = no analysis UI at all.
- A queue with the concurrency limit, an in-memory cache keyed by the file
  identity, and a **SignalR push** when an analysis lands. The injected
  player polls every finished row with backoff (`want`/`pump`); the native
  one asks once per file and is told.
- Asked for: the playing track, then finished rows as they appear on screen.

### media-bridge's side

- A new route, `GET /contract/v1/analysis`, built from `quality.analyse` and
  `lanes`. Token-gated, **not on the xavi vhost** (server to server only),
  listed in `ROUTES` with that reason.
- `root` + `file` resolved through `ANALYSIS_ROOTS` and the checks above.
  Steps 4–6 reuse `resolve_source`, which already does lexical-then-symlink
  containment; steps 1–3 are new and get one test per refusal.
  `ANALYSIS_ROOTS` goes into `.env.example`.
- The `measured` → `summary` and `report` → `findings` mapping happens here,
  so slskd never sees media-bridge's internal codes. Times in notes become seek
  runs. A test holds the output to the contract's JSON schema.
- Network: the `soulseek` container has to reach `10.99.0.2:36914`. That
  crosses quim's host INPUT from the docker bridge, so it needs a rule; check
  `firewall.md` before writing it. The alternative is `media-bridge.ichiva.no`
  through xavi, which would put the route on the vhost.

## Frontend

**media-bridge's player is the starting point**: `web/app/src/features/plex/`
`player.tsx` and the modules beside it. It works well, it already has the
three shapes, and most of what the injected player gets wrong on a phone it
already gets right. Port it, and change only what slskd's stack forces.

- `src/components/Player/`, mirroring media-bridge's split:
  - **The provider and context** (`PlayerProvider`, `usePlayer`): current
    track, playing, loading, position, ended, failure, `toggle`/`seek`/`stop`,
    and one `<audio>` that lives for the whole session. Keyed by transfer id
    instead of a Plex track or host path.
  - **`PlayerBar`, with its three modes as they are**: `wide`, `mini` and
    `full` from one component; `leading(mode)` drawn twice so the mini bar
    is already in place under a folding drawer; the fold played backwards,
    on a timer as well as `animationend`; the drawer following the finger
    (native non-passive `touchmove`, so iOS cannot rubber-band under it); the
    mouse drag from the handle only; body scroll lock while it is open.
  - **Scrubbing** (`onScrubStart`/`Move`/`End`, `SCRUB_STEP_MS`, seek on
    release only if moved), **the cursor** (mouse only), **the wave-loading
    shimmer**, **the clock clamped to the decoded length**, **the replay
    glyph**, and **the loading spinner that gives way on hover and focus**.
  - **Settings, cover and lightbox**: `Settings.tsx`, `Cover.tsx`, `Lanes.tsx`.
- **Copied as they are** (types stripped, tests kept): `meter.ts`, `lanes.ts`,
  `audioGraph.ts` (the AudioContext started *inside* the press of play, which
  iOS requires; AAC for WebKit), `mediaSession.ts`, and from `player.tsx`
  `outline()`, `clock`, `typingIn`, `activatesOnSpace`, the keyboard map (space,
  ←/→ 10 s, 0–9 tenths, Escape innermost-first), and the constants with their
  comments (`FOLD_MS`, `FOLD_PULL`, `GRAB_CLICK`, `CLEARANCE`).
- **Changed because slskd's stack is different**:
  - Tailwind classes → a `Player.css` on slskd's theme tokens; the `index.css`
    rules for `.player.mini` / `.player.full` / `.pbackdrop` move over nearly
    verbatim.
  - TanStack Query → a small fetch hook for peaks and tags; analyses arrive by
    SignalR.
  - React 16: no `inert` prop (set it on the ref); router v5 hooks.
  - The `COMPACT_PLAYER` breakpoint → the fork's phone media query, read from
    the root rather than repeated.
  - The mini bar sits in the dock above `MobileNav` rather than at `bottom: 0`.
- **Left out**: prev/next across a list, `openDetail`, last-played.
  These are Plex-library features.

When something is fixed in one copy after the port, it should be fixed in the
other. A line at the top of each file names its twin.
- Row play buttons in the transfer card rows and in `FlatTransferList`, from
  React, so `data-mbp` and the cell-reading rules stop being a contract with
  a guest.
- Remembered state, per the media-bridge rules: volume yes, **mute no**
  (the injected player remembers mute and should not); the current track and
  position in `sessionStorage`, restored paused, never autoplayed.
- Settings (artwork, meter, cursor, lanes, volume control) move into slskd.

## Interop: the hook the injected scripts use

Compare in `slskd-ingest.js` plays tracks today by dispatching `mbp:play`
with a remote name. The native player exposes:

- `window.slskd.player.play(transferId)` and `.pause()`, `.current()`;
- `slskd:player` events (`change`, `play`, `pause`) on `document`.

The injected scripts use those. `slskd-preview.js` checks for
`window.slskd.player` and builds nothing if it is there, so old and new can be
deployed in any order during the move.

## Tests

- Backend (xUnit, `tests/slskd.Tests.Unit`): verification in both directions
  (short, never-finalised, unreadable source accepted); receipt per codec;
  passthrough limit; peak bucketing, including −32768; per-artifact locking
  (peaks not blocked by a running transcode); containment (a symlink out of
  the downloads directory refused); 404 vs 409; the provider client's status
  handling and backoff.
- Frontend (Jest): the ported modules with their media-bridge tests; mode
  selection by width; scrub stepping and the release-seeks-only-if-moved
  rule; the keyboard map; the dock height reaching the page padding.
- Contract: one JSON schema file, checked by both repos' tests.

## Phasing

1. **Contract.** The schema and `docs/player-analysis-provider.md` in slskd;
   media-bridge's `/contract/v1/analysis` with its tests. Ships on its own
   and changes nothing visible.
2. **Backend.** ffmpeg in the image, the player routes, the cache. Testable
   with curl, nothing in the UI yet.
3. **Player.** The dock, the bar, the mini bar and drawer, row buttons,
   settings, the public hook. `slskd-preview.js` steps aside when it sees it.
4. **Analysis UI.** Provider client, SignalR push, row badges, lanes, the
   findings panel.
5. **media-bridge cleanup.** Remove the player half of `slskd-preview.js` and
   `.css`; point Compare at the hook; update xavi's `sub_filter` for the
   removed asset; update media-bridge's `AGENTS.md`. The `/preview/*` routes
   and their vhost entries **stay**, because media-bridge's own player still
   plays slskd downloads through them.

## Open questions

- Whether TagLibSharp reads AIFF covers cheaply enough (see *Image and options*).
- Which network path the provider call takes (see *media-bridge's side*).
- Whether shares (`/app/shared`) become playable too. Not in v1: they are not
  transfers and have no id.

## Not in this plan

- Prev/next or a queue. The transfer list is not a playlist; add it if
  auditioning in sequence turns out to be wanted.
- Playing search results before downloading. That is streaming from a peer.
- The phone-layout Liquid Glass collapse (the tab bar and mini bar sharing one
  row on scroll). Worth trying once the mini bar exists.
