// <copyright file="AnalysisProvider.cs" company="JP Dillingham">
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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

/// <summary>
///     Where an analysis stands, as the browser is told it.
/// </summary>
public enum AnalysisState
{
    /// <summary>
    ///     No provider is configured; the analysis UI is not drawn.
    /// </summary>
    Off,

    /// <summary>
    ///     The provider answered; the document is attached.
    /// </summary>
    Ready,

    /// <summary>
    ///     The provider is still working; ask again after <see cref="AnalysisAnswer.RetryAfter"/> seconds.
    /// </summary>
    Pending,

    /// <summary>
    ///     There is nothing to show for this file, and asking again will not change that.
    /// </summary>
    None,

    /// <summary>
    ///     The provider could not answer just now.
    /// </summary>
    Unavailable,
}

/// <summary>
///     One answer about one file.
/// </summary>
/// <param name="State">Where it stands.</param>
/// <param name="Analysis">The provider's document, when ready.</param>
/// <param name="RetryAfter">Seconds to wait before asking again, when pending.</param>
/// <param name="Reason">Why there is nothing, for the log and the panel.</param>
public record AnalysisAnswer(AnalysisState State, JsonElement? Analysis = null, int? RetryAfter = null, string Reason = null);

/// <summary>
///     Asks the configured analysis provider about a file, under the contract in docs/player-analysis-provider.md.
/// </summary>
/// <remarks>
///     <para>
///         Server to server: the browser only ever talks to slskd, so the provider's token stays in slskd's config and
///         there is one origin. Answers are kept per file identity (path, size, mtime), so a re-download is asked about
///         afresh and a file nobody touched is asked about once.
///     </para>
///     <para>
///         A negative answer is kept too, for a while, so a page of rows the provider cannot see does not become a
///         request per row per poll. One request per file at a time, and at most <c>concurrency</c> in all.
///     </para>
/// </remarks>
public class AnalysisProvider
{
    /// <summary>
    ///     The contract version this client speaks.
    /// </summary>
    public const int Contract = 1;

    // how soon a caller that did not wait is told to ask again
    private const int PollAfter = 3;

    private static readonly TimeSpan NoneFor = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan UnavailableFor = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ConflictRetry = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Initializes a new instance of the <see cref="AnalysisProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="optionsMonitor">The options monitor.</param>
    /// <param name="cache">Where a finished analysis is kept for good, by download; none keeps them in memory only.</param>
    /// <param name="builtin">slskd's own analyser, for <c>player.analysis.source: builtin</c>.</param>
    public AnalysisProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<Options> optionsMonitor, AnalysisCache cache = null, Builtin.BuiltinAnalyser builtin = null)
    {
        HttpClientFactory = httpClientFactory;
        OptionsMonitor = optionsMonitor;
        Cache = cache;
        Builtin = builtin;
    }

    /// <summary>
    ///     Gets a value indicating whether anything analyses: slskd's own analyser or an external provider, as
    ///     <c>player.analysis.source</c> says. Off by default.
    /// </summary>
    public bool Configured => OptionsMonitor.CurrentValue.Player.Analysis.Enabled;

    private bool UsesBuiltin => OptionsMonitor.CurrentValue.Player.Analysis.UsesBuiltin && Builtin is not null;
    private Builtin.BuiltinAnalyser Builtin { get; }

    private AnalysisCache Cache { get; }
    private IHttpClientFactory HttpClientFactory { get; }
    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private Options.PlayerOptions.PlayerAnalysisProviderOptions Settings => OptionsMonitor.CurrentValue.Player.Analysis.Provider;
    private IMemoryCache Answers { get; } = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5000 });
    private ConcurrentDictionary<string, Lazy<Task<AnalysisAnswer>>> InFlight { get; } = new();
    private SemaphoreSlim Limit { get; set; }
    private int LimitSize { get; set; }
    private ILogger Log { get; } = Serilog.Log.ForContext<AnalysisProvider>();

    /// <summary>
    ///     How a provider's HTTP answer maps onto what the browser is told, and how long it is kept.
    /// </summary>
    /// <param name="status">The status code.</param>
    /// <param name="retryAfter">The Retry-After header, in seconds, if any.</param>
    /// <returns>The state, and how long to keep it (null: do not keep).</returns>
    public static (AnalysisState State, TimeSpan? KeepFor) Classify(HttpStatusCode status, int? retryAfter)
        => status switch
        {
            HttpStatusCode.OK => (AnalysisState.Ready, TimeSpan.FromDays(1)),
            HttpStatusCode.Accepted => (AnalysisState.Pending, null),
            HttpStatusCode.Forbidden or HttpStatusCode.NotFound => (AnalysisState.None, NoneFor),
            HttpStatusCode.Conflict => (AnalysisState.None, NoneFor),
            _ => (AnalysisState.Unavailable, UnavailableFor),
        };

    /// <summary>
    ///     The analysis of <paramref name="file"/>, from the cache or the provider.
    /// </summary>
    /// <param name="file">The resolved file.</param>
    /// <param name="wait">
    ///     Whether to wait for the provider. Without, an answer not yet known is <see cref="AnalysisState.Pending"/>
    ///     at once and the request carries on, so a page asking about every row it draws holds no connection open
    ///     while the provider works.
    /// </param>
    /// <param name="download">
    ///     The download the file is, if it is one: a finished analysis is then kept for it for good, by the request
    ///     itself, so it is kept whether or not anyone is still waiting.
    /// </param>
    /// <param name="cancellationToken">A token to stop waiting. The request itself carries on for whoever asks next.</param>
    /// <returns>The answer.</returns>
    public async Task<AnalysisAnswer> GetAsync(FileInfo file, bool wait = true, Guid? download = null, CancellationToken cancellationToken = default)
    {
        if (!Configured)
        {
            return new AnalysisAnswer(AnalysisState.Off);
        }

        var builtin = UsesBuiltin;
        (string Root, string File)? located = null;

        // the roots name folders to a provider elsewhere; slskd's own analyser reads the file where it is
        if (!builtin)
        {
            located = AnalysisRoots.Locate(Settings.Roots, file.FullName);

            if (located is null)
            {
                return new AnalysisAnswer(AnalysisState.None, Reason: "not in a folder the provider is asked about");
            }
        }

        file.Refresh();

        // by source too, so switching between them is never answered from what the other said
        var key = $"{(builtin ? "builtin" : "provider")}|{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";

        if (Answers.TryGetValue(key, out AnalysisAnswer cached))
        {
            // the same file under another download, analysed while this one was not asked about
            if (download is not null && cached.State == AnalysisState.Ready)
            {
                Remember(download.Value, cached);
            }

            return cached;
        }

        var lazy = InFlight.GetOrAdd(key, _ => new Lazy<Task<AnalysisAnswer>>(() => AskAndKeepAsync(key, located, file, download)));
        var task = lazy.Value;

        if (!wait && !task.IsCompleted)
        {
            return new AnalysisAnswer(AnalysisState.Pending, RetryAfter: PollAfter);
        }

        return await task.WaitAsync(cancellationToken);
    }

    private async Task<AnalysisAnswer> AskAndKeepAsync(string key, (string Root, string File)? located, FileInfo file, Guid? download)
    {
        try
        {
            var (answer, keepFor) = located is { } where ? await AskAsync(where, file) : await AnalyseAsync(file);

            if (keepFor is not null)
            {
                Answers.Set(key, answer, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = keepFor });
            }

            if (download is not null && answer.State == AnalysisState.Ready)
            {
                Remember(download.Value, answer);
            }

            return answer;
        }
        finally
        {
            // by the request itself rather than by whoever was waiting, who may have stopped waiting
            InFlight.TryRemove(key, out _);
        }
    }

    private async Task<(AnalysisAnswer Answer, TimeSpan? KeepFor)> AnalyseAsync(FileInfo file)
    {
        var limit = LimitFor(OptionsMonitor.CurrentValue.Player.Analysis.Builtin.Concurrency);

        await limit.WaitAsync();

        try
        {
            var document = await Builtin.AnalyseAsync(file);
            return (new AnalysisAnswer(AnalysisState.Ready, document), TimeSpan.FromDays(1));
        }
        catch (MediaToolException ex)
        {
            // ffmpeg could not read it: a file that is not what it says, or is broken. asking again changes nothing
            Log.Debug("The built-in analyser could not read {File}: {Message}", file.Name, ex.Message);
            return (new AnalysisAnswer(AnalysisState.None, Reason: "the file could not be decoded"), NoneFor);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warning("The built-in analyser failed for {File}: {Message}", file.Name, ex.Message);
            return (new AnalysisAnswer(AnalysisState.Unavailable, Reason: "the analysis failed"), UnavailableFor);
        }
        finally
        {
            limit.Release();
        }
    }

    private async Task<(AnalysisAnswer Answer, TimeSpan? KeepFor)> AskAsync((string Root, string File) located, FileInfo file)
    {
        var settings = Settings;
        var limit = LimitFor(settings.Concurrency);

        await limit.WaitAsync();

        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var response = await SendAsync(settings, located, file);

                if (response is null)
                {
                    return (new AnalysisAnswer(AnalysisState.Unavailable, Reason: "the provider did not answer"), UnavailableFor);
                }

                // the provider's view of the file disagrees with ours. usually a network filesystem's attribute cache
                // rather than a different file, so once more after a moment, then give up
                if (response.StatusCode == HttpStatusCode.Conflict && attempt == 0)
                {
                    await Task.Delay(ConflictRetry);
                    continue;
                }

                var retryAfter = (int?)response.Headers.RetryAfter?.Delta?.TotalSeconds;
                var (state, keepFor) = Classify(response.StatusCode, retryAfter);

                if (state == AnalysisState.Pending)
                {
                    return (new AnalysisAnswer(state, RetryAfter: Math.Clamp(retryAfter ?? 5, 1, 300)), null);
                }

                if (state != AnalysisState.Ready)
                {
                    Log.Debug("Analysis provider answered {Status} for {Root}/{File}", (int)response.StatusCode, located.Root, located.File);
                    return (new AnalysisAnswer(state, Reason: $"the provider answered {(int)response.StatusCode}"), keepFor);
                }

                using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("contract", out var contract)
                    || !contract.TryGetInt32(out var version)
                    || version != Contract)
                {
                    Log.Warning("Analysis provider speaks a different contract than {Contract}; ignoring its answers", Contract);
                    return (new AnalysisAnswer(AnalysisState.Unavailable, Reason: "the provider speaks a different contract"), UnavailableFor);
                }

                return (new AnalysisAnswer(AnalysisState.Ready, root.Clone()), keepFor);
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException)
        {
            Log.Warning("Analysis provider failed for {Root}/{File}: {Message}", located.Root, located.File, ex.Message);
            return (new AnalysisAnswer(AnalysisState.Unavailable, Reason: "the provider failed"), UnavailableFor);
        }
        finally
        {
            limit.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Options.PlayerOptions.PlayerAnalysisProviderOptions settings, (string Root, string File) located, FileInfo file)
    {
        var mtime = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds();
        var url = settings.Url.TrimEnd('/') + "/v1/analysis"
            + "?root=" + Uri.EscapeDataString(located.Root)
            + "&file=" + Uri.EscapeDataString(located.File)
            + "&size=" + file.Length
            + "&mtime=" + mtime;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.Timeout));

        try
        {
            var client = HttpClientFactory.CreateClient();
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            return await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
        {
            Log.Warning("Analysis provider did not answer for {Root}/{File}: {Message}", located.Root, located.File, ex.Message);
            return null;
        }
    }

    private void Remember(Guid download, AnalysisAnswer answer)
    {
        if (Cache is null || answer.Analysis is null)
        {
            return;
        }

        try
        {
            Cache.Remember(download, answer.Analysis.Value);
        }
        catch (Exception ex)
        {
            // a cache that cannot be written costs a request next time, not this answer
            Log.Warning("Could not keep the analysis of download {Id}: {Message}", download, ex.Message);
        }
    }

    private SemaphoreSlim LimitFor(int size)
    {
        // rebuilt when the setting changes; requests already holding the old one finish under it
        lock (InFlight)
        {
            if (Limit is null || LimitSize != size)
            {
                Limit = new SemaphoreSlim(size, size);
                LimitSize = size;
            }

            return Limit;
        }
    }
}
