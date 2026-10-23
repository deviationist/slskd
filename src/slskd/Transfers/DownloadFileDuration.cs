// <copyright file="DownloadFileDuration.cs" company="JP Dillingham">
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

namespace slskd.Transfers;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

/// <summary>
///     Answers how long the audio in a completed download's file plays for.
/// </summary>
/// <remarks>
///     <para>
///         Read from the file, not taken from the search result it was found in. A peer's reported length is a
///         claim about a file on their disk, and is missing or wrong often enough that a column of them would need
///         checking against the file anyway -- the comparison this is for is between candidates already downloaded.
///         It also covers downloads that never came from a search.
///     </para>
///     <para>
///         Never measured on the request that asks. The transfer list is polled once a second and the downloads
///         directory may be a network mount: a question that is not answered yet starts a measurement and is
///         answered with null, and the poll after the measurement finishes gets the number. A file's length does not
///         change, so an answer is kept for as long as the process runs -- including "not audio", so a non-audio
///         file is opened once rather than once a second.
///     </para>
///     <para>
///         Keyed on the path <em>and</em> the size, because the overwrite strategy can put a different file at the
///         same path, and a different file almost never has the same number of bytes.
///     </para>
/// </remarks>
public class DownloadFileDuration
{
    /// <summary>
    ///     How many files are read at once. Reading a header is cheap; reading a hundred at once off a network
    ///     mount on the first poll after a restart is not.
    /// </summary>
    private const int Concurrency = 2;

    private ConcurrentDictionary<(string Filename, long Size), int?> Measured { get; } = new();
    private ConcurrentDictionary<(string Filename, long Size), Lazy<Task>> Measuring { get; } = new();
    private SemaphoreSlim Limiter { get; } = new SemaphoreSlim(Concurrency, Concurrency);
    private ILogger Log { get; } = Serilog.Log.ForContext<DownloadFileDuration>();

    /// <summary>
    ///     Gets the length of the audio in the file at the specified <paramref name="filename"/>, in whole seconds,
    ///     or null if it is not known yet, not audio, or no filename was recorded.
    /// </summary>
    /// <remarks>Returns at once. A file not yet measured is measured in the background.</remarks>
    /// <param name="filename">The fully qualified filename.</param>
    /// <param name="size">The size of the file, in bytes.</param>
    /// <returns>The length in seconds, or null.</returns>
    public int? Of(string filename, long size)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return null;
        }

        if (Measured.TryGetValue((filename, size), out var length))
        {
            return length;
        }

        _ = MeasureAsync(filename, size);

        return null;
    }

    /// <summary>
    ///     Measures the file at the specified <paramref name="filename"/>, or waits for the measurement already
    ///     under way, and remembers the answer.
    /// </summary>
    /// <param name="filename">The fully qualified filename.</param>
    /// <param name="size">The size of the file, in bytes.</param>
    /// <returns>The operation context.</returns>
    public Task MeasureAsync(string filename, long size)
    {
        var key = (filename, size);

        if (Measured.ContainsKey(key))
        {
            return Task.CompletedTask;
        }

        /*
            Lazy, so the measurement starts only once it is the entry in the dictionary. Started from the factory,
            a quick one could finish -- and remove its entry -- before GetOrAdd had stored it, leaving a finished
            task stored for good and the file never asked about again.
        */
        Lazy<Task> measurement = null;
        measurement = new Lazy<Task>(() => Task.Run(async () =>
        {
            await Limiter.WaitAsync();

            try
            {
                if (TryRead(filename, out var length))
                {
                    Measured[key] = length;
                }
            }
            finally
            {
                Limiter.Release();
                Measuring.TryRemove(KeyValuePair.Create(key, measurement));
            }
        }));

        return Measuring.GetOrAdd(key, measurement).Value;
    }

    /// <summary>
    ///     Reads the length of the file, saying whether the answer is one worth keeping.
    /// </summary>
    /// <remarks>
    ///     A file TagLib cannot make sense of is an answer -- "not audio" -- and is kept. A file that could not be
    ///     <em>opened</em> is not: the downloads directory may be a network mount that is briefly away, and keeping
    ///     that as "not audio" would blank the cell until the next restart.
    /// </remarks>
    /// <param name="filename">The fully qualified filename.</param>
    /// <param name="length">The length in seconds, or null for a file that is not audio.</param>
    /// <returns>Whether <paramref name="length"/> should be remembered.</returns>
    private bool TryRead(string filename, out int? length)
    {
        length = null;

        try
        {
            using var file = TagLib.File.Create(filename, TagLib.ReadStyle.Average | TagLib.ReadStyle.PictureLazy);
            var seconds = file.Properties?.Duration.TotalSeconds ?? 0;

            // a zero is TagLib finding a container it knows with no audio stream it could time, which is not
            // a length of nothing; both that and a format it does not know at all are "not audio" here
            length = seconds > 0 ? (int)Math.Round(seconds) : null;
            return true;
        }
        catch (Exception ex) when (ex is TagLib.UnsupportedFormatException or TagLib.CorruptFileException)
        {
            // an image, a cue sheet, a log: most of what arrives alongside an album is not audio, and that
            // is not worth more than a debug line
            Log.Debug("{Filename} has no length to read: {Message}", filename, ex.Message);
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug("Could not read the length of {Filename}, will ask again: {Message}", filename, ex.Message);
            return false;
        }
    }
}
