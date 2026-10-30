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
using System.Linq;

/// <summary>
///     One way of making a file a browser can play.
/// </summary>
/// <param name="Name">The name a client asks for it by.</param>
/// <param name="Suffix">The cache file's extension.</param>
/// <param name="ContentType">The response's content type.</param>
/// <param name="Arguments">The ffmpeg output arguments, with <see cref="Codecs.Bitrate"/> where the bitrate goes.</param>
/// <param name="MaxBitrate">The highest bitrate the encoder takes, in kbps.</param>
public record Codec(string Name, string Suffix, string ContentType, IReadOnlyList<string> Arguments, int MaxBitrate = 512)
{
    /// <summary>
    ///     The ffmpeg output arguments at <paramref name="kbps"/>, held to what the encoder takes.
    /// </summary>
    /// <param name="kbps">The bitrate.</param>
    /// <returns>The arguments.</returns>
    public IReadOnlyList<string> ArgumentsAt(int kbps)
        => Arguments.Select(a => a == Codecs.Bitrate ? $"{BitrateAt(kbps)}k" : a).ToList();

    /// <summary>
    ///     The bitrate a transcode at <paramref name="kbps"/> is actually made at.
    /// </summary>
    /// <param name="kbps">The bitrate asked for.</param>
    /// <returns>The bitrate, in kbps.</returns>
    public int BitrateAt(int kbps) => Math.Min(kbps, MaxBitrate);
}

/// <summary>
///     The encodings the player serves, and which files are sent as they are.
/// </summary>
/// <remarks>
///     <para>
///         Each choice was measured. Many finished downloads
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
    ///     Where the bitrate goes in a codec's arguments; see <see cref="Codec.ArgumentsAt"/>.
    /// </summary>
    public const string Bitrate = "{bitrate}";

    /// <summary>
    ///     The encodings, by name.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Codec> ByName = new Dictionary<string, Codec>(StringComparer.Ordinal)
    {
        ["opus"] = new("opus", ".webm", "audio/webm", new[] { "-c:a", "libopus", "-b:a", Bitrate, "-compression_level", "0", "-f", "webm" }, MaxBitrate: 510),
        ["aac"] = new("aac", ".m4a", "audio/mp4", new[] { "-c:a", "aac", "-aac_coder", "fast", "-b:a", Bitrate, "-movflags", "+faststart", "-f", "mp4" }),
        ["mp3"] = new("mp3", ".mp3", "audio/mpeg", new[] { "-c:a", "libmp3lame", "-b:a", Bitrate, "-f", "mp3" }, MaxBitrate: 320),
    };

    /// <summary>
    ///     The files a browser may be sent as they are, when it says it can play them, and their content types.
    /// </summary>
    /// <remarks>
    ///     Asked for by the player, never assumed: only the browser knows what it plays, so the player asks it, and
    ///     falls back to a transcode when a file it was sure of will not play after all. Not AIFF, which no browser
    ///     plays, and not M4A, which may be ALAC, which Chrome does not.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> Originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".flac"] = "audio/flac",
        [".wav"] = "audio/wav",
        [".mp3"] = "audio/mpeg",
        [".ogg"] = "audio/ogg",
        [".opus"] = "audio/ogg",
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
