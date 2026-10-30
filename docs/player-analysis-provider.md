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
      url: http://analysis.example:8080/contract   # the provider's base; slskd appends /v1/analysis
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
| 200 | An analysis, below | Keeps it for the download for good (see *Caching*) and shows it |
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
  "id": "9f2c…e41a-v5",
  "provider": { "name": "example-analyzer", "version": "analysis-v5" },
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

- **`id`** (optional) names this analysis: a checksum of the **audio alone**
  (tags, artwork and the file's name and dates left out) and the analyser's
  version, as `<checksum>-<version>`. Two files with the same audio analysed
  under the same rules get the same id. See *Caching*.
- **`provider.version`** is the version of the analyser's rules. Change it
  whenever an answer could change for the same audio: a new finding, a
  retired one, a rewritten threshold.
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

## Caching

An analysis is a fact about the audio, and the audio of a download does not
change, so slskd keeps every 200 **for good**, by download, in its transfers
database. It survives restarts, retagging, and the file being moved or
deleted, and the downloads list carries each row's verdict so the badge draws
with the row. Only a download never analysed asks the provider.

- When an answer arrives whose `provider.version` differs from the one
  before, slskd drops everything made under the other versions: the rules
  changed, so those answers are no longer the provider's.
- Downloads whose answers carry the same `id` share one stored analysis.
  Without an `id`, slskd keeps one per download.
- **System > Data > Clear Audio Analysis** (`DELETE /api/v0/player/analysis/cache`)
  drops the lot, for rules that changed without the version saying so.
  `GET` on the same route gives the count.

Only downloads still listed keep theirs. Daily, and at startup,
slskd drops what it kept for a download since removed (by hand, with its file,
or by retention), and any analysis no listed download still uses.

202, 403, 404 and faults are not kept beyond a short while: they are answers
about now.

## What a provider must refuse

A provider is reading its own disk on behalf of another service, so the path
it is given must be treated as hostile. A provider should refuse, before
touching the filesystem and with the same 403 every time so the answer reveals
nothing about what exists:

- a root name it does not hold;
- an empty or absolute path, a drive letter, a backslash or a NUL;
- any `.`, `..` or empty segment, or any segment starting with `.`. Refused,
  not normalised: `a/../b` names a different file from the one sent;
- an extension that is not audio;
- a path that leaves the root, judged as written and again after following
  symlinks.
