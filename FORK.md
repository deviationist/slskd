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

Upstream's own *Remove* only clears the transfer record, and always has. The
capability to delete a downloaded file exists — the files API, behind
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
