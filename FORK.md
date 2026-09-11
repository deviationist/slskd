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

On the first of those: upstream's own *Remove* only clears the transfer record,
and always has. The capability to delete a downloaded file exists — the files API, behind
`remote_file_management` — but it lives in a separate browser under System, so
getting rid of a download and its file is two operations in two places.

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
