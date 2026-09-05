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
| `pr/feat-delete-files-on-remove` | A second action beside *Remove* on the Downloads page that also deletes the file from disk, gated by the existing `remote_file_management` option. Records `Transfer.LocalFilename` when a download is moved out of the incomplete directory, because that is the only moment the final path is known. | [#1361](https://github.com/slskd/slskd/issues/1361), open |

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
