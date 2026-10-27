// <copyright file="Codecs.cs" company="JP Dillingham">
//           ▄▄▄▄     ▄▄▄▄     ▄▄▄▄
//     ▄▄▄▄▄▄█  █▄▄▄▄▄█  █▄▄▄▄▄█  █
//     █__ --█  █__ --█    ◄█  -  █
//     █▄▄▄▄▄█▄▄█▄▄▄▄▄█▄▄█▄▄█▄▄▄▄▄█
//   ┍━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ ━━━━ ━  ━┉   ┉     ┉
//   │ Copyright (c) JP Dillingham.
//   │
//   │ This program is free software: you can redistribute it and/or modify
//   │ it under the terms of the GNU Affero General Public License as published
//   │ by the Free Software Foundation, version 3.
//   │
//   │ This program is distributed in the hope that it will be useful,
//   │ but WITHOUT ANY WARRANTY; without even the implied warranty of
//   │ MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//   │ GNU Affero General Public License for more details.
//   │
//   │ You should have received a copy of the GNU Affero General Public License
//   │ along with this program.  If not, see https://www.gnu.org/licenses/.
//   │
//   │ This program is distributed with Additional Terms pursuant to Section 7
//   │ of the AGPLv3.  See the LICENSE file in the root directory of this
//   │ project for the complete terms and conditions.
//   │
//   │ https://slskd.org
//   │
//   ├╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌ ╌ ╌╌╌╌ ╌
//   │ SPDX-FileCopyrightText: JP Dillingham
//   │ SPDX-License-Identifier: AGPL-3.0-only
//   ╰───────────────────────────────────────────╶──── ─ ─── ─  ── ──┈  ┈
// </copyright>

namespace slskd.Player;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
///     One way of making a file a browser can play.
/// </summary>
/// <param name="Name">The name a client asks for it by.</param>
/// <param name="Suffix">The cache file's extension.</param>
/// <param name="ContentType">The response's content type.</param>
/// <param name="Arguments">The ffmpeg output arguments.</param>
public record Codec(string Name, string Suffix, string ContentType, IReadOnlyList<string> Arguments);

/// <summary>
///     The encodings the player serves, and which files are sent as they are.
/// </summary>
/// <remarks>
///     <para>
///         Ported from media-bridge's <c>preview.py</c>, where each choice was measured. Most finished downloads here
///         are AIFF, which Chrome and Firefox do not play, so the file is transcoded rather than served.
///     </para>
///     <para>
///         Opus is the default: on a 5-minute AIFF it took 1.85 s at <c>-compression_level 0</c> against 6.8 s for
///         MP3 and 7.5 s for native AAC, and it is the smallest. AAC is what WebKit asks for: Safari plays Opus, but
///         its Web Audio tap reads silence from it, which kills the player's meter on an iPhone. <c>-aac_coder
///         fast</c> because the default took 13.8 s for a 5:21 AIFF against 3.5 s.
///     </para>
/// </remarks>
public static class Codecs
{
    /// <summary>
    ///     The codec used when none is asked for.
    /// </summary>
    public const string Default = "opus";

    /// <summary>
    ///     How much of a source is transcoded. Anything longer is a DJ set or a rip of a whole side.
    /// </summary>
    public const int MaxSeconds = 3600;

    /// <summary>
    ///     The largest MP3 sent as it is. Re-encoding a lossy file is generation loss for no gain, and an MP3 that
    ///     arrived from a peer is already small.
    /// </summary>
    public const long PassthroughMaxBytes = 25L * 1024 * 1024;

    /// <summary>
    ///     The encodings, by name.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Codec> ByName = new Dictionary<string, Codec>(StringComparer.Ordinal)
    {
        ["opus"] = new("opus", ".webm", "audio/webm", new[] { "-c:a", "libopus", "-b:a", "128k", "-compression_level", "0", "-f", "webm" }),
        ["aac"] = new("aac", ".m4a", "audio/mp4", new[] { "-c:a", "aac", "-aac_coder", "fast", "-b:a", "128k", "-movflags", "+faststart", "-f", "mp4" }),
        ["mp3"] = new("mp3", ".mp3", "audio/mpeg", new[] { "-c:a", "libmp3lame", "-b:a", "128k", "-f", "mp3" }),
    };

    /// <summary>
    ///     The extensions the player will try to play. Wider than the analysis set: playing is only a decode, and
    ///     ffmpeg reads all of these.
    /// </summary>
    public static readonly IReadOnlySet<string> Playable = new HashSet<string>(
        new[] { ".mp3", ".aiff", ".aif", ".flac", ".wav", ".m4a", ".alac", ".aac", ".ogg", ".opus", ".wma", ".ape", ".wv" },
        StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> Passthrough = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
    };

    /// <summary>
    ///     The codec for a name a client sent, or null when it is not one of ours. A name outside the table is refused
    ///     rather than defaulted: it becomes an ffmpeg argument list and a file name.
    /// </summary>
    /// <param name="name">The name, or empty for the default.</param>
    /// <returns>The codec, or null.</returns>
    public static Codec Find(string name)
        => ByName.TryGetValue(string.IsNullOrEmpty(name) ? Default : name, out var codec) ? codec : null;

    /// <summary>
    ///     The content type a source is served under unchanged, or null when it must be transcoded.
    /// </summary>
    /// <param name="filename">The source.</param>
    /// <param name="size">Its size in bytes.</param>
    /// <returns>The content type, or null.</returns>
    public static string PassthroughType(string filename, long size)
        => size <= PassthroughMaxBytes && Passthrough.TryGetValue(Path.GetExtension(filename), out var type) ? type : null;
}
