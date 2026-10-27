# Player: analysis provider

slskd's player can show what an external service found wrong with a track: a
badge on the row, marks under the waveform where a problem sits, and a panel of
findings. slskd does no analysis itself. It asks a **provider**, over HTTP, and
draws the answer. With no provider configured none of this is shown, and the
player works the same.

This page is the contract a provider implements. The response's shape is also
written as a JSON Schema in [`player-analysis-provider.schema.json`](player-analysis-provider.schema.json),
which a provider can test itself against.

## Configuration

```yaml
player:
  analysis:
    provider:
      url: http://10.99.0.2:36914/contract   # the provider's base; slskd appends /v1/analysis
      token: <bearer token>
      timeout: 60          # seconds; a cold analysis decodes the whole file
      concurrency: 2       # requests in flight at once
      roots:
        downloads: /app/downloads
```

`roots` names the folders slskd will ask about. The **provider** holds the same
names, mapped to *its* mounts of those folders: the two services may see one
folder under two different paths, and only the provider knows where its own
are. A name is a plain word (`[A-Za-z0-9_-]+`), each name maps to exactly one
folder, and no folder may be inside another.

## Request

```
GET {url}/v1/analysis?root=<name>&file=<relative path>&size=<bytes>&mtime=<unix ms>
Authorization: Bearer <token>
```

- `file` is relative to the named root, with `/` as the separator, as the file
  sits on disk: `complete/Some User/Some Album/01 Track.aiff`.
- `size` and `mtime` describe the file as slskd sees it, so the provider can
  refuse to answer about a different one.
- slskd only asks about files it holds as completed downloads, and only for
  the audio extensions `.mp3 .aiff .aif .flac .wav .m4a`.

## Responses

| Status | Means | slskd does |
|---|---|---|
| 200 | An analysis, below | Keeps it for this file (path, size, mtime) and shows it |
| 202 + `Retry-After` | Still being computed | Asks again after that many seconds |
| 403 | Not a file the provider will analyse | Shows nothing; does not ask again |
| 404 | The provider cannot see the file | Shows nothing; does not ask again |
| 409 | `size`/`mtime` disagree with what the provider sees | Asks once more after a short wait (a network filesystem's attribute cache), then gives up |
| 503 | The provider is not configured for this | Shows nothing; asks again later |
| other | A provider fault | "Analysis unavailable" in the panel; backs off |

## Body

```jsonc
{
  "contract": 1,
  "provider": { "name": "media-bridge", "version": "analysis-v5" },
  "verdict": { "level": "bad", "headline": "lossy? 18.9 kHz" },
  "summary": "MP3 · 44.1 kHz · stereo · 219 kbps · 7:12",
  "findings": [
    {
      "id": "clipping",
      "label": "Clipping",
      "level": "warn",
      "value": [ { "text": "150 runs, longest 4 at " }, { "text": "6:51", "seek": 411 } ],
      "note":  [ { "text": "Consecutive samples stuck at maximum…" } ]
    }
  ],
  "lanes": [ { "finding": "clipping", "buckets": [0, 0, 40, 100, 0] } ]
}
```

- **`verdict`** is the row's badge and the line under the player's title.
  `level` is `ok`, `warn` or `bad` throughout.
- **`summary`** is free text for the top of the findings panel.
- **`findings`** are the panel's rows, worst first. `value` and `note` are
  text as **runs**; a run with `seek` (seconds) is drawn as a time the listener
  can click to jump to. slskd does not look for times in plain text.
- **`lanes`** are optional marks under the waveform: weights from 0 to 100 in
  equal buckets across the track's decoded length, drawn in the named
  finding's colour with its label. A provider never sends an empty lane.
- Unknown fields are ignored, so a provider may send more. `contract` changes
  only for a change a client cannot ignore.

## What a provider must refuse

A provider is reading its own disk on behalf of another service, so the path
it is given must be treated as hostile. The reference implementation
(media-bridge's `contract.py`) refuses, before touching the filesystem and with
the same 403 every time so the answer reveals nothing about what exists:

- a root name it does not hold;
- an empty or absolute path, a drive letter, a backslash or a NUL;
- any `.`, `..` or empty segment, or any segment starting with `.`. Refused,
  not normalised: `a/../b` names a different file from the one sent;
- an extension that is not audio;
- a path that leaves the root, judged as written and again after following
  symlinks.
