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
            "-i", source,
            "-t", Codecs.MaxSeconds.ToString(CultureInfo.InvariantCulture),
            "-vn", "-ac", "1", "-ar", Waveform.PeakRate.ToString(CultureInfo.InvariantCulture), "-f", "s16le", "-",
        }, Timeout, capture: true, cancellationToken);

        return stdout;
    }

    /// <inheritdoc/>
    public Task TranscodeAsync(string source, string destination, Codec codec, CancellationToken cancellationToken = default)
        => RunAsync(Ffmpeg, new[]
        {
            "-v", "error", "-nostdin", "-y",
            "-i", source,
            "-t", Codecs.MaxSeconds.ToString(CultureInfo.InvariantCulture),

            // no video: these files carry cover art, and the WebM muxer will not take it
            "-vn",
        }.Concat(codec.Arguments).Append(destination), Timeout, capture: false, cancellationToken);

    /// <inheritdoc/>
    public async Task<double?> ProbeDurationAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var (_, stdout) = await RunAsync(Ffprobe, new[]
            {
                "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", path,
            }, ProbeTimeout, capture: true, cancellationToken);

            var text = System.Text.Encoding.UTF8.GetString(stdout).Trim();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0 ? seconds : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<(int ExitCode, byte[] Stdout)> RunAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, bool capture, CancellationToken cancellationToken)
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
            : process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, cts.Token);
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
