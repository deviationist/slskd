// <copyright file="PlayerService.cs" company="JP Dillingham">
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
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

/// <summary>
///     A waveform: the peaks to draw, and the decoded length they span.
/// </summary>
/// <param name="Duration">Seconds of audio decoded.</param>
/// <param name="Peaks">Per-bucket peak amplitude, 0..1.</param>
public record WaveformPeaks(double Duration, double[] Peaks);

/// <summary>
///     The waveform and the playable copy of a finished download, each built once and cached.
/// </summary>
/// <remarks>
///     <para>
///         Cached under <c>player.cache.directory</c>, keyed on the file's
///         identity -- resolved path, size and modification time -- so a re-download under the same name is never
///         served a stale entry.
///     </para>
///     <para>
///         One build at a time <i>per artifact</i>, never per file: the browser asks for the waveform and the audio
///         together, and a shared lock would make the waveform wait out the transcode -- 2.3 s of nothing rather than a
///         waveform at 0.45 s and sound at 1.9 s.
///     </para>
/// </remarks>
public class PlayerService
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PlayerService"/> class.
    /// </summary>
    /// <param name="optionsMonitor">The options monitor.</param>
    /// <param name="mediaTool">ffmpeg and ffprobe.</param>
    public PlayerService(IOptionsMonitor<Options> optionsMonitor, IMediaTool mediaTool)
    {
        OptionsMonitor = optionsMonitor;
        MediaTool = mediaTool;

        Clock.EveryHour += (_, _) => Task.Run(ExpireNow);
    }

    /// <summary>
    ///     Gets the directory the cache lives in.
    /// </summary>
    public string CacheDirectory
    {
        get
        {
            var configured = OptionsMonitor.CurrentValue.Player.Cache.Directory;
            return string.IsNullOrWhiteSpace(configured) ? Path.Combine(Program.DataDirectory, "player-cache") : configured;
        }
    }

    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private IMediaTool MediaTool { get; }
    private ConcurrentDictionary<string, SemaphoreSlim> Locks { get; } = new();
    private ILogger Log { get; } = Serilog.Log.ForContext<PlayerService>();
    private long Capacity => OptionsMonitor.CurrentValue.Player.Cache.Capacity * 1024L * 1024L;

    /// <summary>
    ///     A key for this file as it is now.
    /// </summary>
    /// <param name="source">The file.</param>
    /// <returns>The key.</returns>
    public static string Identity(FileInfo source)
    {
        source.Refresh();
        var raw = $"{source.FullName}|{source.Length}|{source.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..32].ToLowerInvariant();
    }

    /// <summary>
    ///     Drops the least recently used entries once the cache is over <paramref name="capacity"/>.
    /// </summary>
    /// <remarks>
    ///     Failures are logged and ignored: a cache that cannot be trimmed is a disk-space problem, not a reason to fail
    ///     the request that filled it.
    /// </remarks>
    /// <param name="root">The cache directory.</param>
    /// <param name="capacity">The size to trim to, in bytes.</param>
    public static void Evict(string root, long capacity)
    {
        FileInfo[] entries;

        try
        {
            entries = new DirectoryInfo(root).GetFiles().Where(f => !f.Name.StartsWith('.')).ToArray();
        }
        catch (IOException)
        {
            return;
        }

        var total = entries.Sum(f => f.Length);

        foreach (var entry in entries.OrderBy(f => f.LastWriteTimeUtc))
        {
            if (total <= capacity)
            {
                break;
            }

            try
            {
                total -= entry.Length;
                entry.Delete();
            }
            catch (IOException)
            {
                // logged by nobody: trimming is best effort
            }
        }
    }

    /// <summary>
    ///     Drops the transcodes unused for longer than <paramref name="unused"/>, with their receipts.
    /// </summary>
    /// <remarks>
    ///     Nearly all of the cache is transcodes, and they are the cheapest thing in it to make again: a few seconds
    ///     on the next play. Every hit touches its entry, so the age is time since last use. A transcode of a file
    ///     that has since gone -- moved by the pipeline, deleted by hand -- is reached by nothing, and this is what
    ///     clears it. Waveforms are left to the capacity.
    /// </remarks>
    /// <param name="root">The cache directory.</param>
    /// <param name="unused">How long a transcode may go unused.</param>
    /// <param name="now">The time now.</param>
    /// <returns>The number of transcodes dropped.</returns>
    public static int Expire(string root, TimeSpan unused, DateTime now)
    {
        FileInfo[] entries;

        try
        {
            entries = new DirectoryInfo(root).GetFiles();
        }
        catch (IOException)
        {
            return 0;
        }

        var dropped = 0;

        foreach (var codec in Codecs.ByName.Values)
        {
            foreach (var entry in entries.Where(f => IsKeyed(f.Name, codec.Suffix) && now - f.LastWriteTimeUtc > unused))
            {
                var key = entry.Name[..^codec.Suffix.Length];
                TryDelete(entry.FullName);
                TryDelete(Path.Combine(root, $"{key}.audio.{codec.Name}.json"));
                dropped++;
            }
        }

        return dropped;
    }

    /// <summary>
    ///     Drops everything cached for <paramref name="source"/>, before it is deleted: once it is gone, its identity
    ///     -- path, size and modification time -- can no longer be worked out, and nothing could find these again.
    /// </summary>
    /// <param name="source">The file about to be deleted.</param>
    /// <returns>The number of entries dropped.</returns>
    public int Forget(FileInfo source)
    {
        if (!source.Exists)
        {
            return 0;
        }

        var key = Identity(source);
        var dropped = 0;

        try
        {
            foreach (var entry in new DirectoryInfo(CacheDirectory).GetFiles($"{key}.*"))
            {
                TryDelete(entry.FullName);
                dropped++;
            }
        }
        catch (IOException ex)
        {
            Log.Warning("Cannot drop the cached copies of {File}: {Message}", source.Name, ex.Message);
        }

        return dropped;
    }

    /// <summary>
    ///     The waveform of <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The file.</param>
    /// <param name="cancellationToken">A token to cancel the build.</param>
    /// <returns>The peaks.</returns>
    public async Task<WaveformPeaks> PeaksAsync(FileInfo source, CancellationToken cancellationToken = default)
    {
        var root = EnsureRoot();
        var key = Identity(source);
        var destination = Path.Combine(root, $"{key}.peaks.json");

        using (await LockAsync($"{key}:peaks", cancellationToken))
        {
            var cached = ReadJson<WaveformPeaks>(destination);

            if (cached is not null)
            {
                Touch(destination);
                return cached;
            }

            var samples = Waveform.Samples(await MediaTool.DecodeAsync(source.FullName, cancellationToken));
            var peaks = new WaveformPeaks(Waveform.Duration(samples.Length), Waveform.Bucket(samples));

            try
            {
                WriteStaged(destination, stream => JsonSerializer.Serialize(stream, peaks));
                Evict(root, Capacity);
            }
            catch (IOException ex)
            {
                // a cache that cannot be written still has an answer to return
                Log.Warning("Cannot cache the waveform for {File}: {Message}", source.Name, ex.Message);
            }

            return peaks;
        }
    }

    /// <summary>
    ///     A file a browser can play, and its content type: the source itself when that is already true and small, a
    ///     checked transcode otherwise.
    /// </summary>
    /// <param name="source">The file.</param>
    /// <param name="codec">The encoding the client asked for.</param>
    /// <param name="cancellationToken">A token to cancel the build.</param>
    /// <returns>The path and the content type.</returns>
    public async Task<(string Path, string ContentType)> AudioAsync(FileInfo source, Codec codec, CancellationToken cancellationToken = default)
    {
        var passthrough = Codecs.PassthroughType(source.Name, source.Length);

        if (passthrough is not null)
        {
            return (source.FullName, passthrough);
        }

        var root = EnsureRoot();
        var key = Identity(source);
        var destination = Path.Combine(root, key + codec.Suffix);
        var receipt = Path.Combine(root, $"{key}.audio.{codec.Name}.json");

        using (await LockAsync($"{key}:audio:{codec.Name}", cancellationToken))
        {
            // the receipt, not just the file. a half-written transcode is a real file of a real size and passes every
            // test a stat can make; what it does not have is a record that its length was checked against the source
            var kbps = codec.BitrateAt(OptionsMonitor.CurrentValue.Player.Bitrate);
            var kept = ReadJson<Receipt>(receipt);

            // at the bitrate asked for now: a transcode made at another is made again, which is how a change of the
            // setting reaches what is already cached
            if (File.Exists(destination) && new FileInfo(destination).Length > 0 && kept?.Codec == codec.Name && kept.Bitrate == kbps)
            {
                Touch(destination);
                return (destination, codec.ContentType);
            }

            var staged = StagedName(destination);

            try
            {
                await MediaTool.TranscodeAsync(source.FullName, staged, codec with { Arguments = codec.ArgumentsAt(kbps) }, cancellationToken);

                // before the rename, so a bad encode is never published. ffmpeg's exit code is not enough on its own --
                // the 2026-09-07 file was committed with one
                var got = await MediaTool.ProbeDurationAsync(staged, cancellationToken);
                var want = await MediaTool.ProbeDurationAsync(source.FullName, cancellationToken);

                if (want is null)
                {
                    Log.Warning("Cannot read a duration for {File}; accepting its transcode unchecked", source.Name);
                }

                var refusal = Waveform.Refusal(got, want);

                if (refusal is not null)
                {
                    throw new MediaToolException(refusal);
                }

                File.Move(staged, destination, overwrite: true);
            }
            finally
            {
                TryDelete(staged);
            }

            // after the rename: the receipt vouches for a file already in place. the other way round it would vouch for
            // one that might never arrive
            try
            {
                WriteStaged(receipt, stream => JsonSerializer.Serialize(stream, new Receipt(codec.Name, kbps)));
            }
            catch (IOException ex)
            {
                // the transcode is good; only the shortcut is lost, so the next play rebuilds it
                Log.Warning("Cannot record the transcode receipt for {File}: {Message}", source.Name, ex.Message);
            }

            Evict(root, Capacity);
            return (destination, codec.ContentType);
        }
    }

    // an entry named by an identity: 32 hex digits, then the suffix
    private static bool IsKeyed(string name, string suffix)
        => name.Length == 32 + suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal)
            && name[..32].All(Uri.IsHexDigit);

    private void ExpireNow()
    {
        var days = OptionsMonitor.CurrentValue.Player.Cache.Ttl;

        if (days <= 0 || !Directory.Exists(CacheDirectory))
        {
            return;
        }

        try
        {
            var dropped = Expire(CacheDirectory, TimeSpan.FromDays(days), DateTime.UtcNow);

            if (dropped > 0)
            {
                Log.Information("Dropped {Count} transcodes unused for {Days} days", dropped, days);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Could not expire the player cache: {Message}", ex.Message);
        }
    }

    private static T ReadJson<T>(string path)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
        {
            return null;
        }
    }

    // a dotfile beside the destination, so eviction never counts it and a crash part-way leaves nothing that passes for
    // an entry. `staged` is load-bearing, not tidiness: ffmpeg writes as it encodes, and a full disk part-way through is
    // the likeliest failure there is
    private static string StagedName(string destination)
        => Path.Combine(Path.GetDirectoryName(destination), $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.part{Path.GetExtension(destination)}");

    private static void WriteStaged(string destination, Action<Stream> write)
    {
        var staged = StagedName(destination);

        try
        {
            using (var stream = File.Create(staged))
            {
                write(stream);
            }

            File.Move(staged, destination, overwrite: true);
        }
        finally
        {
            TryDelete(staged);
        }
    }

    private static void Touch(string path)
    {
        // so eviction reads "least recently used", not "oldest built"
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (IOException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private string EnsureRoot()
    {
        var root = CacheDirectory;
        Directory.CreateDirectory(root);
        return root;
    }

    private async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Release(semaphore);
    }

    private sealed record Receipt(string Codec, int Bitrate);

    private sealed class Release(SemaphoreSlim semaphore) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
