# This fork

`deviationist/slskd`, a fork of [`slskd/slskd`](https://github.com/slskd/slskd)
kept for one reason: we run slskd as the front of a music ingress chain, and a
couple of things that chain needs are not upstream yet. The fork exists to fit
that setup first; contributing back is what happens once it does.

Same shape as the [onetagger fork](https://github.com/deviationist/onetagger),
deliberately — one convention across every fork on this fleet.

## Branches

**`main` is this fork's version of slskd** — upstream plus everything below.
Clone it, build it, and you get the additions; that is the point of the branch,
and it is what our images are built from. It is never rewound to a plain copy
of upstream.

| Branch | What it is |
|---|---|
| `master` | A plain mirror of `upstream/master`. Nothing of ours is ever committed here. |
| `main` | Upstream plus our work. The branch to build and to run. |
| `pr/<type>-<name>` | Branched from **`upstream/master`**, so its diff carries nothing of ours and a PR can be opened from it as-is. |
| `feature/<name>` | New capability, based on `main`. |
| `fix/<name>` | A correction to existing behaviour, based on `main`. |
| `docs/<name>` | Documentation, based on `main`. |

The prefix answers *can this be sent upstream?*; for `pr/` branches the next
token (`pr/fix-…`, `pr/feat-…`) answers *what is it?*. Only `pr/` makes a claim
about the base, and it is a checkable one —
`git rev-list --count upstream/master..<branch>` should be a handful of commits,
not dozens.

The distinction is capability, not intent. Plenty of `feature/` work would be
welcome upstream; it simply cannot be *sent* from a branch whose diff against
`upstream/master` is a pile of unrelated work. Which it is depends on the base,
and the base is chosen before the first line is written.

A `pr/` branch is **not** an alternative to shipping a change here: it is
branched from `upstream/master` and then merged into `main` like anything else,
so the one branch is both a ready-to-send PR and part of this fork's build.
That only works if the base is chosen *before* the change is written — start it
on `main` and it will grow to depend on whatever else is already there, after
which it cannot be lifted out without being rewritten.

**One `pr/` branch is based on another rather than on `upstream/master`:**
`pr/feat-transfers-default-sort` sits on `pr/feat-sort-transfers`, because it
configures the thing that branch adds and is meaningless without it. The rule
it bends is a means to an end — a diff against `upstream/master` that carries
nothing unrelated — and that still holds: the two together are one feature and
answer one issue. A second `pr/` branch on top of a first is fine whenever it
is the *same* piece of work; it is a base on `main` that is not.

## How a change gets in

**Through a pull request into `main`, merged when the operator says so.** Not
by a local `git merge` that happens to have been run first — the PR is the
gate, and a change that is already on `main` before it is reviewed has skipped
the only step that was asked for.

```sh
git checkout -b pr/feat-thing upstream/master   # base: upstream, never main
# ...build it, test it, commit...
git push -u origin pr/feat-thing
gh pr create --repo deviationist/slskd --base main --head pr/feat-thing --draft
# ...operator reviews, says all good...
gh pr ready  <n> --repo deviationist/slskd
gh pr merge  <n> --repo deviationist/slskd --merge
git checkout main && git pull                   # then build and deploy from main
```

**The base is `main`, not `master`.** `master` is the mirror; a PR merged there
puts our commits on it and breaks `--ff-only` permanently. Basing the *branch*
on `upstream/master` is what keeps its diff clean — and because the merge base
is `upstream/master` either way, a PR into `main` still shows that feature
alone even once `main` carries several others. There is no tradeoff to make
here; an earlier PR on this fork was based on `master` for a diff it would have
got anyway, and could then only be closed rather than merged.

Deploying before the merge is fine and often necessary — the image has to be
built to be tested. Merging before the review is not.

## Taking upstream's changes

```sh
git fetch upstream --tags
git checkout master && git merge --ff-only upstream/master && git push
git checkout main   && git merge master
```

`master` is fast-forward only, so a merge that is not a fast-forward means
something of ours was committed there by mistake — the failure is the point.
Upstream comes into `main` by merging, never rebasing: nothing upstream is
removed, so `main` stays a superset rather than a divergence, and the merge
commits record when each upstream state was taken.

## What this fork adds

| Branch | What it does | Upstream |
|---|---|---|
| `pr/feat-delete-files-on-remove` | Makes *Remove* on the Downloads page delete the file too, behind a new `transfers.download.delete_file_on_removal` option — the option is the whole decision, there is no second button and no per-request flag. Covers a cancelled download's partial as well as a completed file, and clears the folders the deletion empties. Records `Transfer.LocalFilename` — where the bytes are *now* — because a completed file's path is unreproducible once `MoveFile` has renamed it around a collision. | [#1361](https://github.com/slskd/slskd/issues/1361), open |
| `pr/feat-sort-transfers` | Puts a sort control on the Transfers header, so the newest user card and folder can be at the top rather than the bottom. Ordered on `requestedAt`, which is set once and never moves — the list re-fetches every second, so `startedAt`/`endedAt` would have cards climbing out from under the pointer. A group takes its *newest* transfer's instant under Newest and its *oldest* under Oldest, so a folder still taking delivery climbs back up and Oldest still reproduces the API's order. Files inside a folder keep filename order, since they were all enqueued in one gesture and ordering them by time only reverses an album. | [#1798](https://github.com/slskd/slskd/issues/1798), open |
| `pr/feat-transfers-default-sort` | Makes the order the list *starts* in configurable: `transfers.upload.default_sort` and `transfers.download.default_sort`, each `newest` or `oldest`, validated against the same two names the sort understands. Based on `pr/feat-sort-transfers` rather than `upstream/master` — see *Branches*. A default rather than an instruction: the control on the page still overrides it for the browser it was used in, and only a browser that has never touched the control follows the configured value. Which is also the thing to know when a change to it appears to do nothing. | [#1798](https://github.com/slskd/slskd/issues/1798), open |

| `feature/download-completed-file` | Puts a download button on the completed rows of the Downloads page, which hands the file the download produced back to the *browser*, behind a new `remote_file_retrieval` option — its own decision, separate from `remote_file_management`, because reading a file out is not the same grant as deleting one. The file is named by the **download's id**: its path is resolved server-side from `Transfer.LocalFilename` and no path is ever accepted from the caller, so there is no path for a request to traverse. The resolved path is still opened through the file service, which allows the downloads and incomplete directories and nothing else — symlinks resolved and re-checked, since opening a link follows it. *Download Selected* on a user card streams the selection as one **stored** zip — never deflated, never staged on disk — after a pre-flight that names anything already gone; a file that goes missing mid-stream is skipped into a `MISSING.txt` rather than failing an archive already most of the way to the browser. That one is fetched by *navigating* to it so the browser streams it to disk instead of holding an album in the memory of a tab, which means it cannot carry an Authorization header: it is authorized by a single-use, 60-second, id-bound ticket instead. | Not contributable as-is: it reads `Transfer.LocalFilename`, which is this fork's column. Based on `main` for that reason — see *Branches*. |
| `pr/fix-files-controller-error-codes` | Makes the Files API answer **403** where it answered 500, and **400** where a route parameter cannot be decoded. Its handlers caught `SecurityException` while the file service throws `UnauthorizedException`, which does not derive from it — so those catch blocks were unreachable and a forbidden path fell through to the generic error handler. `SecurityException` can still legitimately arise, so both are caught rather than one swapped for the other. The decode has a second door: valid base64 that decodes to a NUL character passes the decode and then throws from `Path.GetFullPath`. Changes what is *reported*, never what is allowed. | Fixes upstream behaviour; diff against `upstream/master` is exactly this. |
| `fix/archive-async-central-directory` | Finishes the archive. `ZipArchive` streams entry contents through `WriteAsync` but writes the format's bookkeeping — each entry's data descriptor, the central directory — with **synchronous** writes, which Kestrel disallows on the response body. Every file's bytes reached the browser and the request then died before the central directory, leaving a file that is not a zip (*"Error 79 — Inappropriate file type or format"* on macOS). The .NET 10 async API is not a way out: neither `DirectToArchiveWriterStream` nor the stream from `OpenAsync` implements `DisposeAsync`, so `await using` falls back to the synchronous path. Also: a single selected file is fetched as a file rather than an archive of one, and a row whose file has gone is struck out and says why. | Ours — fixes `feature/download-completed-file`. |
| `pr/fix-html-middleware-binary-corruption` | Stops the HTML middleware corrupting every binary response. Both middlewares engage on the **request's** `Accept` header, and a browser sends `text/html` on any navigation — including one that downloads a file — after which they read the body into a string and write it back, replacing every byte that is not valid UTF-8 with U+FFFD. Measured: a 113,302,889-byte archive arrived as 203,464,131 bytes. They now decide from the *response's* content type. Second bug in the same file: `UseHTMLInjection` accepted `excludedRoutes` and never passed it to `UseMiddleware`, so the container supplied an empty collection instead and `/api` and `/swagger` were never actually excluded. | Both are upstream bugs, but mostly *latent* there: stock slskd serves no file downloads, so the corruption needs a browser navigation to a binary response — which upstream only has by opening a static asset directly in a tab. The dropped parameter is present today and costs waste rather than damage (an `/api` response asked for with an HTML `Accept` is buffered and round-tripped through a string; JSON survives intact). Worth sending on as a trap removed for the next binary endpoint, not as a live break. |
| `fix/testable-retrieval-decisions` | Moves the four rules that decide what a download row offers out of the components and into `lib/transfers.js`, with tests. This project has no component-test setup, so a rule written in a component cannot be tested at all — and two of these were written out twice, once in each component. | Ours. |
| `feature/remove-from-row` | Puts a remove control on every removable row of the Transfers page, so one transfer can be cleared without first ticking it and aiming at the selection's button. It *is* that button over a selection of one — the same request, the same clearing of the selection, the same summary toast — offered on exactly the rows `isStateRemovable` accepts, which is also why it can never abort a transfer still running. Where the removal would delete a file it asks first and names the path it is about to delete; where it would not, it asks nothing, because a dialog raised over a harmless action is one that gets dismissed unread. Both decisions live in `planRowRemoval` in `lib/transfers.js`, with tests. | Not contributable as-is: the confirmation exists because of `transfers.download.delete_file_on_removal` and is worded from it, and the reporting it reuses is this fork's `summariseDeletions`. Based on `main` for that reason — see *Branches*. |

On the first of those: upstream's own *Remove* only clears the transfer record,
and always has. The capability to delete a downloaded file exists — the files API, behind
`remote_file_management` — but it lives in a separate browser under System, so
getting rid of a download and its file is two operations in two places.

### Search watches

A search that re-runs on a schedule, keeps what is new since its last run, and
mails it. Built over 2026-09-12/13; the plan and the reasons the shape changed
while it was built are in `PLAN-search-watches.md`. Configured under
`searches.watches.*` — `enabled`, `minimum_interval`, `gap`, `limit`,
`download_limit` and `timezone`.

| Branch | What it does | Upstream |
|---|---|---|
| `pr/feat-mail-integration` | Gives slskd a way to send mail at all: an adapter interface with an SMTP implementation, `mail.*` options, and both SMTP fields marked `[Secret]` so they do not come back out through the options API. Its own branch because sending mail is a capability the rest of the fork happens to be the first user of, not part of watching a search. | Nothing upstream asks for it directly; it is the prerequisite for anything that reports on its own. |
| `pr/feat-search-watches` | The feature: schema and migration, the watch service and scheduler, a C# port of the UI's result filter, the event, the API and the UI. A watch stores an RRULE and **the zone it was created in** — so an existing watch keeps the zone it had until it is saved again, and changing `searches.watches.timezone` governs new ones only. What counts as *new* is recorded per watch, so a run reports what the previous one did not see rather than everything it finds. | No upstream issue asks for this exactly. [#1315](https://github.com/slskd/slskd/issues/1315) is adjacent — people already schedule searches from outside and complain it clogs the Search screen, which is an argument for re-running in place rather than accumulating rows. |
| `feature/watch-existing-search`, `feature/watch-from-dashboard`, `feature/watch-modal-editable-search` | The three other doors into the same modal: a search already in the list can be made recurring without re-running it, the dashboard's search bar can start one, and the phrase can be corrected while the watch is being created rather than after. | Ours. |
| `feature/watch-auto-enqueue`, `feature/seed-when-the-search-has-results` | A watch can queue what it finds instead of waiting to be read, capped by `download_limit` — the cap is the whole safety argument, since an unattended watch on a common phrase would otherwise fill a disk while its operator read the mail about it. Seeding takes the results the search **already has**, which is what a watch created alongside its own search needs; seeding from what it *had* found meant seeding from nothing. | Ours. |
| `feature/global-ignore-list`, `feature/ignore-from-email` | A file can be ignored by every watch rather than only the one that found it, and the mail that reported a file carries a link that ignores it — the decision is made where it is read. | Ours. |
| `feature/watch-mail-table` | The mail a watch sends carries an HTML body as well as a plain one, and by default it is a **table** — one row per file, with the name, its folder under it, the peer, size, bitrate, length and the ignore link as columns. The question being asked of this mail is whether anything in it is worth having, which is answered by scanning a column rather than by reading a block per file. Rows are ordered by **name** first, so the three peers holding one track are three adjacent rows to compare rather than three places in the table. `mail.format` picks the layout (`table` or `list`); the plain body stays a block per file either way, because it is the fallback and it carries a link per file on its own line — and it is all the sendmail adapter sends, which cannot do multipart. Composition moved out of `WatchService` into `Notification`, where it is tested. | Ours. |
| `feature/next-run-time-element`, `fix/watch-timestamps-serialize-as-utc` | "next run in 12 h" is a `<time>` carrying the real instant, so the moment is readable rather than inferred. The second is why it could be trusted: the API served watch timestamps with no zone, because SQLite loses `DateTimeKind`, so a browser read UTC as local and every watch was two hours out. Fixed with a model-wide value converter in `SearchDbContext` rather than per-column. | Ours; the converter is the sort of thing upstream would want if it stored instants this way. |

Nine `fix/watch-*` branches follow these and are not itemised — form fields that
rendered as labels only, a modal that lost focus to the searches page behind it,
a close icon outside its header, native selects, a watch accepted with no
phrase, the file casing in a notification. They are in the log.

### The table view

The search results and the downloads, as one row per file with the peer as a
column. Built 2026-09-13/14.

**The card view stays.** Its value is per-folder context — the folder headers
and *Search for Additional Files in This Directory* — and grouping is
incompatible with ordering, which is what the table is for. Neither is a
migration path away from the other, and the asymmetry between them is the
design rather than a gap. Fold Results is hidden in table view for that reason.

| Branch | What it does | Upstream |
|---|---|---|
| `feature/flat-file-list` | The list itself: `lib/searches.js` flattens the grouped responses to rows, the toggle is remembered (`slskd-search-flat-results`), and the checkboxes, download, delete and info controls all carry over. Selection across a flat list needed a real indeterminate master, since "all" and "some" are different answers once every file is in one list. | Ours. |
| `feature/virtualise-flat-list` | `@tanstack/react-virtual` in place of *Show 100 more* — the results were always fully loaded, so the pagination was hiding what was already in memory. **The window is not the scroll container:** this app scrolls `div.ui.segment.pushable.app`, so `useWindowVirtualizer` renders a list that goes blank on scroll. `scrollParentOf` in `lib/util.js` finds the real one. The card view keeps its *Show More*, which pages *responses* rather than files. | Ours, though the virtualiser is generic. |
| `feature/sortable-columns`, `fix/quiet-sort-affordance` | Ordering from the column headers, written to the query string so an ordering is a link. Length and Size sort on the underlying seconds and bytes, not their formatted text; File, Path, User and Attributes sort alphabetically, which for Attributes is really *grouping* — the point is to bring the same encodings together. The sort marker shows under the pointer and on the active column only; a marker on every header was noise. | Ours. |
| `feature/peer-columns-and-visibility` | Upload speed, free upload slots and queue length as sortable columns, and a picker deciding which columns show (`slskd-search-columns`, `slskd-transfers-columns-<direction>`). All on by default except those three. The picker is a `Popup`, not a `Dropdown`: Semantic's dropdown closes from a native document listener that React's `stopPropagation` cannot reach, which swallowed every tick. | Ours. |
| `feature/mark-downloaded-results`, `feature/retrieve-or-redownload` | A search result says whether it is downloading or already downloaded, and a row for a file we already have offers to **fetch it to the browser** rather than download it a second time — unless the file has since gone from disk, in which case it offers the download again and says why. The decision is `rowActionOf` in `lib/searches.js`, with tests. | Not contributable as-is: it reads this fork's `Transfer.LocalFilename` and depends on `remote_file_retrieval`. |
| `feature/wide-layout-and-folder` | A *Wide* toggle (`slskd-wide`) taking the view to 2400px, and the file's **full folder path** in a column renamed Path — the width is what makes the path affordable. | Ours. |
| `feature/downloads-table-view`, `feature/row-state-attribute`, `feature/player-in-table-view` | The same table on the Downloads page: virtualised, sortable, filterable (space-separated AND terms, `-term` excludes, matching peer, full path and state), with the selection actions gated on the same `isStateRetryable`/`isStateCancellable`/`isStateRemovable`/`isRetrievable` rules the card view uses. Rows are tagged `data-filename`, `data-username` and `data-state` — **the scripts injected into this page must read identity from those attributes**, because the column picker can hide the cells they used to read. The bulk bar is portalled into `#footer-action-slot`: `.pushable.app` carries a transform, which breaks `position: fixed` for everything under it. | Ours. |

### Time, locale, and the rest

| Branch | What it does | Upstream |
|---|---|---|
| `pr/fix-hardcoded-locale-timestamps` | Chat and room timestamps were formatted `en-US` regardless of the reader. | Fixes upstream behaviour. |
| `feature/24-hour-timestamps`, `feature/day-first-dates` | Every clock in the UI reads 00-23 (`hourCycle: 'h23'`, which unlike `hour12: false` cannot produce a 24:07), and the locale is a setting — `web.locale`, `en-GB` by default. Date order is not a separate knob because Intl derives order, separators and month names together from the locale; blank follows the browser's *language*, which is how an operator on a 24-hour OS still gets AM/PM. | Ours, on top of the fix above. |
| `feature/expose-toast-to-injected-scripts` | Hands the toast API to the scripts injected into the page, so an injected control reports the way the app does. | Ours — for `media-bridge`. |
| `pr/fix-grouped-button-border`, `pr/fix-popup-dark-theme`, `pr/fix-footer-favicon-url-base`, `pr/fix-search-detail-header-height` | Four small upstream fixes: the first button of a group losing its left border in the dark theme, the popup unreadable in it, the footer logo resolved against the current route rather than the url base, and a search detail header that would not grow with its phrase. | All four fix upstream behaviour; each diff is exactly that. |
| `feature/explain-hide-result`, `feature/confirm-removal-with-enter`, `feature/confirm-bulk-removal` | The red cross on a result card says what it does; a removal confirms with Enter as it already cancels with Escape; a selection asks before it deletes files, as a single row already did. | Ours, the last one because the prompt is worded from `delete_file_on_removal`. |

## Our image

Built from `main` and pushed to the homelab registry:

```sh
docker build --build-arg VERSION=<upstream version>.65534-homelab \
  -t registry.ichiva.no/slskd:homelab .
docker push registry.ichiva.no/slskd:homelab
```

The `.65534-` version suffix is upstream's own convention for a local build.
The quim stack pulls that tag; see `~/docker-root/slskd/docker-compose.yml`.

## Sending something upstream

Open it as a **draft** (`gh pr create --draft`) and let the operator be the one
who marks it ready. That is a fleet-wide rule for fork → upstream PRs, and it
exists so the title, description and diff can be read as a unit before any
maintainer is notified.
