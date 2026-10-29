# Transfers: compare provider

slskd can say which of several finished downloads are the same track, judged
from the audio rather than the names: "Darkness (Chris Cargo Mix)" and
"Darkness (Chris Cargo Remix)" may be one recording renamed by whoever shared
it, or two different things. slskd does no comparing itself. It asks a
**provider**, over HTTP, and draws the answer. The feature is **off by
default**. It is on only when it is enabled *and* a provider is configured;
otherwise the Compare button is not drawn.

This page is the contract a provider implements. The response's shape is also
written as a JSON Schema in [`transfers-compare-provider.schema.json`](transfers-compare-provider.schema.json),
which a provider can test itself against. It follows the
[analysis provider](player-analysis-provider.md) contract closely: files are
named the same way, and a provider may implement both behind one token.

## Configuration

```yaml
transfers:
  compare:
    enabled: true            # off unless set
    provider:
      url: http://analysis.example:8080/contract   # the provider's base; slskd appends /v1/compare
      token: <bearer token>
      timeout: 300           # seconds; a cold comparison decodes every file
      roots:
        downloads: /app/downloads
```

`roots` works as in the analysis contract: names slskd uses, which the provider
maps to *its* mounts of the same folders. A name is a plain word
(`[A-Za-z0-9_-]+`), each name maps to exactly one folder, and no folder may be
inside another.

## Request

```
POST {url}/v1/compare
Authorization: Bearer <token>
Content-Type: application/json

{
  "files": [
    { "root": "downloads", "file": "complete/Some User/Album/01 Track.aiff",
      "size": 51234567, "mtime": 1761730000000 },
    { "root": "downloads", "file": "complete/Other User/Album/01 Track (Remix).mp3",
      "size": 11893221, "mtime": 1761731000000 }
  ]
}
```

- Between 2 and 10 files. 10 is slskd's limit; a provider may take fewer and
  refuses more than it takes with a 400.
- Each `file` is relative to its named root, with `/` as the separator, as the
  file sits on disk.
- `size` and `mtime` (Unix milliseconds) describe the file as slskd sees it, so
  the provider can refuse to answer about a different one.
- slskd only sends files it holds as completed downloads, with the audio
  extensions `.mp3 .aiff .aif .flac .wav .m4a`.

## Responses

| Status | Means | slskd does |
|---|---|---|
| 200 | A comparison, below | Keeps it for this exact set of files, and shows it |
| 202 + `Retry-After` | Still being computed | Asks again with the same body after that many seconds |
| 400 | Fewer than 2 files, or more than the provider takes | Says so in the dialog |
| 409 | A file's `size`/`mtime` disagree with what the provider sees | Asks once more after a short wait (a network filesystem's attribute cache), then gives up |
| 503 | The provider is not configured for this | "Compare unavailable" |
| other | A provider fault | "Compare unavailable"; backs off |

A provider may answer synchronously and never send 202; the reference
implementation does. slskd does not hold the browser's request open meanwhile:
the browser polls slskd, and slskd waits for the provider up to `timeout`.

**A file the provider will not or cannot read does not fail the request.** In a
search's worth of copies, one broken download is the ordinary case. Such a file
comes back as `readable: false` with a `note`, in the same words whether it was
refused, missing or undecodable.

## Body

```jsonc
{
  "contract": 1,
  "provider": { "name": "example-analyzer", "version": "1.4.0" },
  "verdict": {
    "level": "mixed",
    "headline": "2 of 3 are the same track",
    "detail": "Grouped below; the rest match nothing else here."
  },
  "files": [
    { "readable": true, "music_s": 515.6, "lead_s": 0.4, "tail_s": 1.2 },
    { "readable": true, "music_s": 300.1, "lead_s": 0.0, "tail_s": 0.8 },
    { "readable": true, "music_s": 517.6, "lead_s": 2.4, "tail_s": 1.1 }
  ],
  "groups": [
    { "files": [0, 2], "notes": ["the others are compared with this one",
                                 "99% match · 2.0 s longer · starts 2.0 s later"] }
  ],
  "alone": [1],
  "partly": [],
  "pairs": [ { "a": 0, "b": 2, "verdict": "same", "match": 0.99, "share": 1.0,
               "offset_s": 2.0, "drift_ms": 0, "gain_db": 0.0 } ]
}
```

Every index is into `files`, which is in the order the files were sent.

- **`verdict`** is the dialog's banner. `level` is one of:
  - `same`: the same recording, beyond doubt;
  - `likely`: the same, but a looser match (a different master, a processed rip);
  - `mixed`: some are the same and some are not;
  - `partly`: they share long stretches and then part ways (an edit, an extended mix, a mashup);
  - `different`: none are the same;
  - `unknown`: fewer than two could be read.
- **`groups`** are the files that are one track, two or more each, largest
  first. `notes` has one entry per member and says how it differs from the
  first, which is the one the others are compared with.
- **`alone`** are the readable files that match nothing else sent.
- **`partly`** are pairs, not groups: two files can share their first half and
  nothing else, which is not a track the two of them make.
- **`pairs`** are optional: the measurements behind the words. slskd draws the
  words and does not read these.
- The provider sends text, not codes, so a client draws the answer without
  knowing what a score means. Unknown fields are ignored, so a provider may send
  more. `contract` changes only for a change a client cannot ignore.

## What a provider must refuse

Exactly what the analysis contract's provider refuses, file by file: a root name
it does not hold; an empty or absolute path, a drive letter, a backslash or a
NUL; any `.`, `..` or empty segment, or any segment starting with `.`; an
extension that is not audio; a path that leaves the root, judged as written and
again after following symlinks. A provider should apply the same checks to
each file, and report a refused file as `readable: false` rather than refusing
the request, with the same note as a missing one.
