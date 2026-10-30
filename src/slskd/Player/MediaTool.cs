// <copyright file="MediaTool.cs" company="JP Dillingham">
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

using Microsoft.Extensions.Options;

namespace slskd.Player;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
///     What the player needs from ffmpeg and ffprobe.
/// </summary>
public interface IMediaTool
{
    /// <summary>
    ///     Decodes a source to mono 16-bit little-endian PCM at <see cref="Waveform.PeakRate"/>.
    /// </summary>
    /// <param name="source">The source file.</param>
    /// <param name="cancellationToken">A token to cancel the decode.</param>
    /// <returns>The PCM.</returns>
    Task<byte[]> DecodeAsync(string source, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Transcodes a source to <paramref name="destination"/> with <paramref name="codec"/>.
    /// </summary>
    /// <param name="source">The source file.</param>
    /// <param name="destination">Where to write the result.</param>
    /// <param name="codec">The encoding.</param>
    /// <param name="cancellationToken">A token to cancel the transcode.</param>
    /// <returns>The operation context.</returns>
    Task TranscodeAsync(string source, string destination, Codec codec, CancellationToken cancellationToken = default);

    /// <summary>
    ///     The duration a container's header claims, or null if it will not say.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">A token to cancel the probe.</param>
    /// <returns>Seconds, or null.</returns>
    Task<double?> ProbeDurationAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    ///     What ffprobe says of the first audio stream and the container, as its JSON.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">A token to cancel the probe.</param>
    /// <returns>The JSON.</returns>
    Task<string> ProbeAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Decodes a source to raw PCM, written to <paramref name="destination"/> as it is decoded rather than held.
    /// </summary>
    /// <param name="source">The source file.</param>
    /// <param name="rate">The sample rate to decode at.</param>
    /// <param name="channels">The channels to decode to.</param>
    /// <param name="format">The sample format: <c>s16le</c>, <c>s32le</c> or <c>f32le</c>.</param>
    /// <param name="destination">Where the PCM goes.</param>
    /// <param name="cancellationToken">A token to cancel the decode.</param>
    /// <returns>The operation context.</returns>
    Task DecodeToAsync(string source, int rate, int channels, string format, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    ///     A SHA-256 of the first audio stream's coded packets: the audio alone, so tags, artwork, a rename or a move
    ///     leave it unchanged.
    /// </summary>
    /// <param name="source">The source file.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The checksum, as 64 hex digits.</returns>
    Task<string> HashAudioAsync(string source, CancellationToken cancellationToken = default);
}

/// <summary>
///     ffmpeg and ffprobe as child processes.
/// </summary>
public class MediaTool : IMediaTool
{
    /// <summary>
    ///     How long ffmpeg gets before it is taken to be wedged. Generous: the source may be a cold read over a network
    ///     filesystem.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(300);

    /// <summary>
    ///     How long a probe gets. It reads a header, not a file.
    /// </summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Input options for every ffmpeg and ffprobe that opens a file, placed before it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Everything before this -- <see cref="PlayableFile"/>, the extension in <see cref="Codecs.Playable"/> -- is
    ///         about the file's name; ffmpeg judges its bytes. A file called <c>X.mp3</c> that is really a DASH manifest is
    ///         opened by the <c>dash</c> demuxer, and its BaseURL sends ffmpeg to any local media file, outside the
    ///         downloads directory -- and the player then streams it. Reproduced on 2026-10-29 with this image's ffmpeg
    ///         6.1.1. A peer chooses both the name and the bytes, so the name alone settles nothing.
    ///     </para>
    ///     <para>
    ///         Pinned to the demuxers for <see cref="Codecs.Playable"/> (<c>mov</c> covers m4a and ALAC, <c>asf</c> is
    ///         WMA, <c>w64</c> a large WAV), plus <c>matroska</c> for the player's own WebM transcodes, which
    ///         <see cref="ProbeDurationAsync"/> checks; and to the <c>file</c> protocol, so nothing a file names can reach
    ///         anything else. A file in none of these formats is refused rather than interpreted.
    ///     </para>
    /// </remarks>
    public static readonly IReadOnlyList<string> InputGuard =
    [
        "-protocol_whitelist", "file",
        "-format_whitelist", "mp3,flac,aiff,wav,w64,mov,aac,ogg,ape,asf,wv,matroska",
    ];

    /// <summary>
    ///     Initializes a new instance of the <see cref="MediaTool"/> class.
    /// </summary>
    /// <param name="optionsMonitor">The options monitor.</param>
    public MediaTool(IOptionsMonitor<Options> optionsMonitor)
    {
        OptionsMonitor = optionsMonitor;
    }

    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private string Ffmpeg => OptionsMonitor.CurrentValue.Player.Ffmpeg;
    private string Ffprobe => OptionsMonitor.CurrentValue.Player.Ffprobe;

    /// <inheritdoc/>
    public async Task<byte[]> DecodeAsync(string source, CancellationToken cancellationToken = default)
    {
        var (_, stdout) = await RunAsync(Ffmpeg, new[]
        {
            "-v", "error", "-nostdin",
        }.Concat(InputGuard).Concat(new[]
        {
            "-i", source,
            "-t", Codecs.MaxSeconds.ToString(CultureInfo.InvariantCulture),
            "-vn", "-ac", "1", "-ar", Waveform.PeakRate.ToString(CultureInfo.InvariantCulture), "-f", "s16le", "-",
        }), Timeout, capture: true, cancellationToken);

        return stdout;
    }

    /// <inheritdoc/>
    public Task TranscodeAsync(string source, string destination, Codec codec, CancellationToken cancellationToken = default)
        => RunAsync(Ffmpeg, new[]
        {
            "-v", "error", "-nostdin", "-y",
        }.Concat(InputGuard).Concat(new[]
        {
            "-i", source,
            "-t", Codecs.MaxSeconds.ToString(CultureInfo.InvariantCulture),

            // no video: these files carry cover art, and the WebM muxer will not take it
            "-vn",
        }).Concat(codec.Arguments).Append(destination), Timeout, capture: false, cancellationToken);

    /// <inheritdoc/>
    public async Task<double?> ProbeDurationAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var (_, stdout) = await RunAsync(Ffprobe, new[]
            {
                "-v", "error",
            }.Concat(InputGuard).Concat(new[]
            {
                "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", path,
            }), ProbeTimeout, capture: true, cancellationToken);

            var text = System.Text.Encoding.UTF8.GetString(stdout).Trim();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0 ? seconds : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<string> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        var (_, stdout) = await RunAsync(Ffprobe, new[]
        {
            "-v", "error",
        }.Concat(InputGuard).Concat(new[]
        {
            "-select_streams", "a:0",
            "-show_entries", "stream=codec_name,sample_rate,channels,bits_per_raw_sample,bits_per_sample,bit_rate",
            "-show_entries", "format=duration,bit_rate,format_name",
            "-of", "json", path,
        }), ProbeTimeout, capture: true, cancellationToken);

        return System.Text.Encoding.UTF8.GetString(stdout);
    }

    /// <inheritdoc/>
    public Task DecodeToAsync(string source, int rate, int channels, string format, Stream destination, CancellationToken cancellationToken = default)
        => RunAsync(Ffmpeg, new[]
        {
            "-v", "error", "-nostdin",
        }.Concat(InputGuard).Concat(new[]
        {
            "-i", source,
            "-t", Codecs.MaxSeconds.ToString(CultureInfo.InvariantCulture),
            "-vn", "-ac", channels.ToString(CultureInfo.InvariantCulture), "-ar", rate.ToString(CultureInfo.InvariantCulture),
            "-f", format, "-",
        }), Timeout, capture: false, cancellationToken, sink: destination);

    /// <inheritdoc/>
    public async Task<string> HashAudioAsync(string source, CancellationToken cancellationToken = default)
    {
        // the coded packets, not the decoded samples: two encodes of one recording share a decoded checksum and not
        // their bitrate, so one would be taken for the other
        var (_, stdout) = await RunAsync(Ffmpeg, new[]
        {
            "-v", "error", "-nostdin",
        }.Concat(InputGuard).Concat(new[]
        {
            "-i", source, "-map", "0:a:0", "-c", "copy", "-f", "hash", "-hash", "sha256", "-",
        }), Timeout, capture: true, cancellationToken);

        var text = System.Text.Encoding.ASCII.GetString(stdout).Trim();
        var digest = text.StartsWith("SHA256=", StringComparison.Ordinal) ? text["SHA256=".Length..] : string.Empty;

        if (digest.Length != 64)
        {
            throw new MediaToolException($"ffmpeg gave an unexpected hash: '{text}'");
        }

        return digest;
    }

    private static async Task<(int ExitCode, byte[] Stdout)> RunAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, bool capture, CancellationToken cancellationToken, Stream sink = null)
    {
        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // an argument list, never a command line: a filename is passed as one argument whatever it contains
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = info };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new MediaToolException($"cannot start {executable}: {ex.Message}", ex);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var output = new MemoryStream();
        var stdout = capture
            ? process.StandardOutput.BaseStream.CopyToAsync(output, cts.Token)
            : process.StandardOutput.BaseStream.CopyToAsync(sink ?? Stream.Null, cts.Token);
        var stderr = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(cts.Token));
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // it may have exited between the timeout and the kill
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new MediaToolException($"{Path.GetFileName(executable)} did not finish within {timeout.TotalSeconds:0} s");
        }

        if (process.ExitCode != 0)
        {
            var last = (await stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            throw new MediaToolException(last ?? $"{Path.GetFileName(executable)} exited {process.ExitCode}");
        }

        return (process.ExitCode, output.ToArray());
    }
}

/// <summary>
///     ffmpeg or ffprobe failed, with the last thing it said.
/// </summary>
public class MediaToolException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MediaToolException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="inner">The inner exception.</param>
    public MediaToolException(string message, Exception inner = null)
        : base(message, inner)
    {
    }
}
