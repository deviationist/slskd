# Search watches — plan

A **watch** turns a one-off search into a standing one: slskd re-runs it on a
schedule and emails when a file appears that it has not told you about before.
The case it exists for is the rare track whose only holder is offline when you
look — you search, find nothing or find only a 192 kbps rip, and then wait
weeks for the one person who has the FLAC to come online. Today that means
remembering to search again. A watch remembers for you.

Vocabulary: the feature is **search watches**; one instance is a **watch**;
each execution is a **run**; a file that clears the criteria and has not been
reported before is a **hit**.

## The shape of it, as the operator described it

- A third button beside ＋ and 🔍 on `/searches` opens a **create modal**:
  the search criteria, the recurrence, whether locked results count, the
  notification address (blank = the configured default), and — only here, at
  creation — **"don't notify me about what this first run finds"**.
- The search then appears in the list **flagged as a watch**, so it reads as
  ongoing rather than finished.
- Opening it gives a **watch panel**: enable/disable, edit the recurrence,
  the address and the criteria, and a **log of emails sent**, in a modal.

Two decisions from the Q&A that the rest of this document rests on:

1. **There is no ignore list.** The "skip the first run's results" checkbox at
   creation *is* the ignore mechanism, and the criteria are the granular
   control. Everything reported is recorded as reported, so you hear about
   each file once.
2. **That record is keyed on the search's own UUID** — the one already in the
   URL (`/searches/c4a7ece4-…`). Two watches for the same track are two
   independent memories, and the second will happily tell you about a file the
   first already mentioned. That is intended: a new watch is a fresh question.

## Decision: a watch re-runs *in place*

Keying the reported-files set on the search UUID settles a design question that
would otherwise be open. A run cannot create a new `Search` row, because its
new id would reset the memory. So:

**One `Search` row, one id, for the life of the watch.** Each run gets a new
Soulseek token, overwrites `Responses`, and moves `StartedAt`/`EndedAt` and
`State`. The list row stays put, the URL stays valid, and the hub's existing
`update` broadcast means an open browser watches the re-run happen live.

Consequences to handle, none of them optional:

- `Search.Token` and `Search.StartedAt` are `init`-only (`Search/Types/Search.cs`).
  They need setters, or the run has to delete and re-insert under the same id.
  Setters are the smaller change and keep the row's identity obvious.
- **Search retention will delete your watch.** `retention.search` defaults to
  10080 minutes — seven days — and `PruneAsync` removes completed searches
  older than that. A watch that runs weekly is *always* older than its last
  run by the time the pruner looks. `PruneAsync` must exclude searches that
  carry a watch. This is the single most likely way to ship this feature and
  have it quietly disappear a month later.
- Deleting the search deletes the watch, its memory and its notification log,
  by cascade.
- History is kept as **run records**, not as retained responses: one small row
  per run (started, ended, responses, files, matches, new, outcome). Keeping
  every run's `ResponsesJson` would grow the search DB without bound.

## Data model

Four tables, all in the existing **search** database.

```
Watch                  PK SearchId (FK Searches.Id, cascade)
  Enabled              bool
  Rrule                text        RFC 5545 RRULE, no DTSTART
  TimeZone             text        IANA, e.g. Europe/Oslo
  Anchor               datetime    DTSTART, UTC — set at creation
  NextRunAt            datetime    UTC, persisted so restarts don't drift
  LastRunAt            datetime?
  NotifyEmail          text?       null = fall back to config default
  IncludeLocked        bool        "notify about locked tracks/folders"
  RequireFreeSlot      bool        mirrors "Hide Results with No Free Slots"
  Filter               text        the filter-box string, verbatim
  Formats              text?       optional extension allowlist — see below
  CreatedAt/UpdatedAt  datetime

WatchFile              PK Id; UNIQUE (SearchId, Username, Filename)
  SearchId, Username, Filename, Size
  FirstSeenAt          datetime
  NotifiedAt           datetime?   null + Seeded = suppressed first run
  Seeded               bool

WatchRun               PK Id; INDEX (SearchId, StartedAt)
  SearchId, StartedAt, EndedAt
  ResponseCount, FileCount, MatchCount, NewCount
  Outcome              enum  Completed | Errored | Cancelled | Skipped
  Error                text?

WatchNotification      PK Id; INDEX (SearchId, SentAt)
  SearchId, SentAt, Adapter, Recipient, Subject
  FileCount, FilesJson
  Status               enum  Sent | Failed
  Error                text?
```

A file's identity is **username + remote path**. Attributes changing (a peer
re-encodes and the size shifts) does not make it a new file; you were told
about that path from that user already.

`EnsureCreated()` (`Program.cs:1510`) does nothing to a database that already
exists, so these tables need a migration — `Z2026_09_XX_SearchWatchesMigration`,
following `Core/Data/Migrations/README.md` and the fork's own
`Z2026_09_05_TransferLocalFilenameMigration`. Idempotent, schema-inspecting,
transactional.

## Scheduling

**RRULE, with a GUI that hides it.** The operator picks frequency, interval,
time of day and days of week from a form; the form produces
`FREQ=DAILY;INTERVAL=1;BYHOUR=3;BYMINUTE=0`, shows the sentence it means, and
stores the rule.

- Server: **Ical.Net** (MIT) parses and evaluates next-occurrence.
- Client: the **`rrule`** npm package builds the string and renders `toText()`
  underneath the form, so the modal always shows what it will do in English.
- Both sides must agree; the client only *composes and describes*, the server
  is the only thing that ever decides a run is due.

**Timezone is not optional here.** The container runs UTC, the operator does
not. "Every day at 03:00" with no zone is 05:00 Oslo in summer and 04:00 in
winter. Each watch stores an IANA zone, defaulting to a new
`searches.watches.timezone` option, falling back to UTC. DST is Ical.Net's
problem, which is a good reason to use it rather than hand-rolled arithmetic.

**The throttle is the real constraint.** ~240 searches in 15 minutes took this
account off the server for 20 minutes — `Disconnected`, reconnect-looping, and
immune to every local fix until it healed itself. Nothing in this feature may
make that reachable:

- One run at a time across all watches, ever. A `SemaphoreSlim(1)` around the
  runner, not per-watch locking.
- A configurable gap after each run completes (`gap_seconds`, default 10).
- A minimum effective interval the API rejects below (`minimum_interval`,
  default 60 minutes). `FREQ=SECONDLY` and `FREQ=MINUTELY` are refused
  outright, and a rule whose next two occurrences are closer together than the
  floor is refused with the reason.
- Jittered start (±10% of the interval, capped) so ten daily watches created in
  one sitting don't fire as a burst at 03:00.
- **Skip, don't queue, when the client is not logged in.** The runner checks
  `Client.State` and records a `Skipped` run; the occurrence is not banked.
- **Catch-up is one run, not N.** An app that was down for a week owes you one
  search, not 168.

The tick comes from `Clock.EveryMinute` (`Core/Clock.cs`), which
`Application.cs:227` already subscribes to for exactly this kind of work.
A minute of granularity is plenty for a floor of an hour.

## What counts as a hit

The criteria are **the controls that already exist on the search detail page**,
persisted — not a new language. From `SearchDetail.jsx`: *Hide Locked Results*,
*Hide Results with No Free Slots*, and the *Filter* box. (*Fold Results* is
presentation only and is not stored.) Today all three are ephemeral React
state; the watch is the first thing to give them a life beyond the tab.

That has a consequence worth stating plainly: **the filter language has to be
ported to C#.** The parser and evaluator live in `src/web/src/lib/searches.js`
(`parseFiltersFromString`, `filterResponse`) and the email must apply exactly
what the page applies, or the mail and the screen will disagree about the same
search. Plan for a shared corpus of test vectors — one JSON file, read by both
`searches.test.js` and the new xUnit tests — because two implementations of one
grammar drift the moment nothing forces them together.

Three quirks in the current implementation that the port must reproduce
deliberately rather than accidentally fix:

1. **`minfilesize` is compared against bytes**, so the placeholder's
   `minfilesize:10` filters nothing at all. `minlength` is in seconds, so
   `minlength:5000` means 83 minutes. Both read as if they meant MB and
   milliseconds.
2. **`minfilesinfolder` empties `files` but leaves `fileCount` untouched**, so
   the caller's `fileCount + lockedFileCount > 0` test still passes and the
   response renders with no files rather than disappearing.
3. **`islossless` is a metadata heuristic, not a format check** — it requires
   both `sampleRate` and `bitDepth` to be present. A peer that reports no
   attributes fails it, *including for a real FLAC*. `islossy` is the
   complement, so an attribute-less file counts as lossy.

Point 3 matters for the exact case this feature is for. Worse: **include terms
are ANDed** (`include.filter(…).length !== include.length`), so the filter box
cannot express "flac **or** aiff **or** wav". There is no way to write a format
allowlist in the language as it stands.

→ **Recommendation, needs a yes/no:** add one optional field to the watch, a
**format allowlist** (checkboxes: FLAC / AIFF / WAV / MP3 / other), matched on
the filename extension and ORed, ANDed with whatever the filter box says. One
field, no new syntax, and it makes "tell me when this appears in any lossless
format" expressible and robust against peers with no attributes. Without it,
the feature's headline use case rests on a heuristic that silently drops files.

## Notification

The runner raises a **`SearchWatchHit` event** on the existing bus
(`Events/Types/Events.cs`) carrying the watch, the run and the new files.
Mail is then one subscriber among several — `WebhookService` and
`ScriptService` pick it up for free, which is also the MQTT path to
media-bridge without slskd needing to know that exists.

A new `Integrations/Mail/` with an adapter interface and three
implementations, selected by config:

| Adapter | How | Notes |
|---|---|---|
| `smtp` | MailKit (MIT) | Works from inside the container; points straight at `smtp-relay.brevo.com:587`, the credential the hosts already use. .NET's built-in `SmtpClient` is the alternative and is the one Microsoft tells you not to use in new code. |
| `brevo` | HTTP API | No mail library at all; the path banko already uses. Brevo API keys are IP-restricted by default and that check is currently **off** on the account, because nothing maintains an allowlist against a changing WAN IP. |
| `sendmail` | pipe to a binary | What media-bridge does. **Dead in our own deployment**: there is no sendmail in the slskd image and it runs `cap_drop: ALL` as uid 4009. Built for bare-metal installs, and it is the cheapest of the three. |

Mail details that are easy to get wrong:

- The SMTP password and the Brevo key must carry **`[Secret]`**
  (`Common/Redactor.cs`), or they are served to any browser that opens the
  Options page.
- Emails need a link back to the search, which means slskd has to be told its
  own public origin — it does not know it. New option
  `integrations.mail.base_url`, e.g. `https://slsk.ichiva.no`. Without it the
  mail can only name the search, not link to it.
- Multipart plain + HTML. The plain part is the one that has to be readable.
- Send failures are logged to `WatchNotification` with the error and surfaced
  in the panel. A watch whose mail has been bouncing for a month should say so
  on its own page, not only in the application log.
- Retries via the existing `Retry.Do`, then give up — never block the runner.

## Config surface

```yaml
searches:
  watches:
    enabled: true
    minimum_interval: 60    # minutes; the API refuses anything tighter
    gap_seconds: 10         # quiet time between runs, globally
    timezone: Europe/Oslo   # default for new watches
    max_enabled: 25

integrations:
  mail:
    adapter: smtp           # smtp | brevo | sendmail
    from: slskd@ichiva.no
    to: secret.registry@pm.me   # default recipient; a watch may override
    base_url: https://slsk.ichiva.no
    smtp:
      host: smtp-relay.brevo.com
      port: 587
      encryption: starttls
      username: ~
      password: ~           # [Secret]
    brevo:
      api_key: ~            # [Secret]
    sendmail:
      path: /usr/sbin/sendmail
```

Plus `docs/config.md` and `config/slskd.example.yml`, which is where anyone
running this will actually look.

## API

```
GET    /api/v0/searches/{id}/watch                 the watch, or 404
PUT    /api/v0/searches/{id}/watch                 create or replace
PATCH  /api/v0/searches/{id}/watch                 enable/disable, edit
DELETE /api/v0/searches/{id}/watch                 stop watching, keep search
GET    /api/v0/searches/{id}/watch/runs            run history, paged
GET    /api/v0/searches/{id}/watch/notifications   the email log, paged
POST   /api/v0/searches/{id}/watch/run             run now (respects the lock)
POST   /api/v0/mail/test                           prove the adapter works
```

`GET /api/v0/searches` gains the watch summary per row (enabled, next run,
last notified) so the list can badge without N+1 requests.

## Frontend

- **`Searches.jsx`** — a third action button (`icon="eye"` or `"clock"`) beside
  ＋ and 🔍, opening `WatchModal`.
- **`WatchModal`** — search text, the three persisted criteria controls reusing
  the same components as the detail page, the RRULE builder, the notification
  address, and the creation-only *"skip this run's results"* switch. It creates
  the search **and** the watch; the first run starts immediately, seeding the
  memory when that switch is on.
- **`SearchListRow.jsx`** — a badge, and next-run text.
- **`SearchDetail.jsx`** — a watch panel above the results: enabled toggle,
  the rule in English, next/last run, *Run now*, *Edit*, and *Emails sent…*.
- **`WatchNotificationsModal`** — the log: when, to whom, how many files,
  status, expandable to the file list. The operator asked for this explicitly
  and it is also the only place a failing adapter becomes visible.
- **`lib/watches.js`** alongside `lib/searches.js`.

## Tests

- **Filter parity** — the shared vector corpus, run from both sides. The point
  is that a change to one implementation fails the other's suite.
- **RRULE** — next-occurrence across a DST boundary in `Europe/Oslo`, the
  floor rejections, and the one-not-N catch-up after a long outage.
- **Scheduler** — single-flight under concurrent due watches, the gap, the
  skip-when-disconnected path, and that a skipped occurrence is not banked.
- **Memory** — a file reported once is not reported twice; seeding suppresses
  the first run without emptying it; two watches over the same text each report
  independently.
- **Adapters** — against a fake SMTP server and a stubbed HTTP handler;
  assert `[Secret]` fields never appear in the serialised options.
- **Migration** — idempotent across repeated startup, on a populated DB.

## Phasing

Per `FORK.md`, the base is chosen before the first line is written. Nothing
here depends on the fork's existing work, so this can be built where it can
still be sent upstream — two stacked `pr/` branches, the second on the first,
which `FORK.md` already permits when they are one piece of work:

1. **`pr/feat-mail-integration`** — the mailer, three adapters, options,
   redaction, `/api/v0/mail/test`, docs. Independently useful: slskd has
   Pushbullet today and no email at all.
2. **`pr/feat-search-watches`** — schema and migration, the watch service and
   scheduler, the C# filter port, the event, the API, the UI.

Each goes to a draft PR into `main`; the operator marks ready and merges.
Build and deploy from `main` afterwards — `docker build --build-arg VERSION=…`,
push to `registry.ichiva.no`, then the compose env for the mail settings.

No upstream issue asks for this exactly. [#1315](https://github.com/slskd/slskd/issues/1315)
is adjacent — people already schedule searches from outside and complain that
it clogs the Search screen, which is an argument for re-running in place rather
than accumulating rows.

## Not in this plan

- **A global ignore list** — "never tell me about this file again, in any
  watch". Deliberately deferred: the per-watch memory covers the described
  workflow, and a global list is a second, cross-cutting store with its own UI
  and its own retention question. It slots in cleanly later as a rule consulted
  before `WatchFile` — the hit pipeline should be written with that seam in mind.
- **Auto-enqueue on hit** — grabbing the file while the peer is online rather
  than when you read the mail. Tempting, and the runner already holds
  everything needed; left out because "download without me looking" deserves
  its own decision about scoring and disk.
- **Cross-watch digests**, folder-level notification, and anything that turns
  this into a subscription manager.
