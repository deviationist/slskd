// <copyright file="CompareProvider.cs" company="JP Dillingham">
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

namespace slskd.Transfers.Compare;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using slskd.Player;

/// <summary>
///     Where a comparison stands, as the browser is told it.
/// </summary>
public enum CompareState
{
    /// <summary>
    ///     Compare is not enabled, or no provider is configured; the button is not drawn.
    /// </summary>
    Off,

    /// <summary>
    ///     The provider answered; the document is attached.
    /// </summary>
    Ready,

    /// <summary>
    ///     The provider is still working; ask again after <see cref="CompareAnswer.RetryAfter"/> seconds.
    /// </summary>
    Pending,

    /// <summary>
    ///     The provider will not compare these, and asking again will not change that; <see cref="CompareAnswer.Reason"/> says why.
    /// </summary>
    Refused,

    /// <summary>
    ///     The provider could not answer just now.
    /// </summary>
    Unavailable,
}

/// <summary>
///     One answer about one set of files.
/// </summary>
/// <param name="State">Where it stands.</param>
/// <param name="Document">The provider's document, when ready.</param>
/// <param name="RetryAfter">Seconds to wait before asking again, when pending.</param>
/// <param name="Reason">Why there is nothing, for the log and the dialog.</param>
public record CompareAnswer(CompareState State, JsonElement? Document = null, int? RetryAfter = null, string Reason = null);

/// <summary>
///     Asks the configured compare provider which of several files are the same track, under the contract in
///     docs/transfers-compare-provider.md.
/// </summary>
/// <remarks>
///     <para>
///         Server to server, as <see cref="AnalysisProvider"/> is: the browser only ever talks to slskd, so the
///         provider's token stays in slskd's config. Answers are kept per list of file identities (path, size, mtime)
///         in the order sent -- the document's indices are into that order, so the same files in another order are a
///         different question as far as the cache is concerned.
///     </para>
///     <para>
///         One comparison goes to the provider at a time. Each may be a dozen decodes, and a second one asked for
///         meanwhile is usually the same one asked again, which <see cref="InFlight"/> answers without a request.
///     </para>
/// </remarks>
public class CompareProvider
{
    /// <summary>
    ///     The contract version this client speaks.
    /// </summary>
    public const int Contract = 1;

    /// <summary>
    ///     The most files one comparison may hold. Every pair is scored, so this bounds the work at 45 pairs.
    /// </summary>
    public const int MaximumFiles = 10;

    /// <summary>
    ///     What the browser is told when the provider refuses a comparison. Fixed rather than the provider's own words,
    ///     which go to the log: those are another service's text, and whatever it one day puts in an error -- a path, a
    ///     setting -- would otherwise go straight to the page.
    /// </summary>
    public const string RefusedByProvider = "The compare provider will not compare these files; the server log says why";

    /// <summary>
    ///     The most comparisons waiting for, or being answered by, the provider at once. A new one past this is told
    ///     to try again rather than queued: each can be a dozen decodes, and a queue would only hold work for callers
    ///     who have stopped waiting.
    /// </summary>
    public const int MaximumInFlight = 3;

    // how soon a caller that did not wait is told to ask again
    private const int PollAfter = 3;

    private static readonly TimeSpan UnavailableFor = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ConflictRetry = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Initializes a new instance of the <see cref="CompareProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="optionsMonitor">The options monitor.</param>
    /// <param name="builtin">slskd's own comparer, for <c>transfers.compare.source: builtin</c>.</param>
    public CompareProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<Options> optionsMonitor, Builtin.BuiltinCompare builtin = null)
    {
        HttpClientFactory = httpClientFactory;
        OptionsMonitor = optionsMonitor;
        Builtin = builtin;
    }

    /// <summary>
    ///     Gets a value indicating whether Compare is offered: slskd's own comparer, or a configured provider, as
    ///     <c>transfers.compare.source</c> says. Off by default.
    /// </summary>
    public bool Enabled => UsesBuiltin || Settings.UsesProvider;

    private bool UsesBuiltin => Settings.UsesBuiltin && Builtin is not null;
    private Builtin.BuiltinCompare Builtin { get; }

    private IHttpClientFactory HttpClientFactory { get; }
    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private Options.TransfersOptions.CompareOptions Settings => OptionsMonitor.CurrentValue.Transfers.Compare;
    private IMemoryCache Answers { get; } = new MemoryCache(new MemoryCacheOptions { SizeLimit = 500 });
    private ConcurrentDictionary<string, Lazy<Task<CompareAnswer>>> InFlight { get; } = new();
    private SemaphoreSlim Limit { get; } = new(1, 1);
    private ILogger Log { get; } = Serilog.Log.ForContext<CompareProvider>();

    /// <summary>
    ///     How a provider's HTTP answer maps onto what the browser is told, and how long it is kept.
    /// </summary>
    /// <param name="status">The status code.</param>
    /// <returns>The state, and how long to keep it (null: do not keep).</returns>
    public static (CompareState State, TimeSpan? KeepFor) Classify(HttpStatusCode status)
        => status switch
        {
            HttpStatusCode.OK => (CompareState.Ready, TimeSpan.FromDays(1)),
            HttpStatusCode.Accepted => (CompareState.Pending, null),

            // the provider is busy with other comparisons: the same as still working on this one, from where the
            // browser stands -- ask again after the time it gives, and keep nothing
            HttpStatusCode.TooManyRequests => (CompareState.Pending, null),

            // the request itself is wrong for this provider -- too many files, too few -- so the same one will be
            // refused again; not kept, because a change of the provider's limit should be seen at once
            HttpStatusCode.BadRequest => (CompareState.Refused, null),
            _ => (CompareState.Unavailable, UnavailableFor),
        };

    /// <summary>
    ///     Names each file to the provider, or says which cannot be named.
    /// </summary>
    /// <param name="files">The resolved files.</param>
    /// <returns>The pairs in the files' order, and the files no root holds.</returns>
    public (List<(string Root, string File)> Located, List<FileInfo> Outside) Locate(IReadOnlyList<FileInfo> files)
    {
        var located = new List<(string Root, string File)>();
        var outside = new List<FileInfo>();

        foreach (var file in files)
        {
            var pair = AnalysisRoots.Locate(Settings.Provider.Roots, file.FullName);

            if (pair is null)
            {
                outside.Add(file);
            }
            else
            {
                located.Add(pair.Value);
            }
        }

        return (located, outside);
    }

    /// <summary>
    ///     The comparison of <paramref name="files"/>, from the cache or the provider.
    /// </summary>
    /// <param name="files">The resolved files, in the order the document's indices will follow. Two to <see cref="MaximumFiles"/>.</param>
    /// <param name="wait">
    ///     Whether to wait for the provider. Without, an answer not yet known is <see cref="CompareState.Pending"/> at
    ///     once and the request carries on, so the browser holds no connection open while files are decoded.
    /// </param>
    /// <param name="cancellationToken">A token to stop waiting. The request itself carries on for whoever asks next.</param>
    /// <returns>The answer.</returns>
    public async Task<CompareAnswer> GetAsync(IReadOnlyList<FileInfo> files, bool wait = true, CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return new CompareAnswer(CompareState.Off);
        }

        if (files.Count < 2 || files.Count > MaximumFiles)
        {
            return new CompareAnswer(CompareState.Refused, Reason: $"Compare takes 2 to {MaximumFiles} files");
        }

        var builtin = UsesBuiltin;
        List<(string Root, string File)> located = null;

        // the roots name folders to a provider elsewhere; slskd's own comparer reads the files where they are
        if (!builtin)
        {
            (located, var outside) = Locate(files);

            if (outside.Count > 0)
            {
                return new CompareAnswer(CompareState.Refused, Reason: "not in a folder the provider is asked about: " + string.Join(", ", outside.Select(f => f.Name)));
            }
        }

        foreach (var file in files)
        {
            file.Refresh();
        }

        // by source too, so switching between them is never answered from what the other said
        var key = (builtin ? "builtin\n" : "provider\n") + string.Join('\n', files.Select(f => $"{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}"));

        if (Answers.TryGetValue(key, out CompareAnswer cached))
        {
            return cached;
        }

        if (!InFlight.ContainsKey(key) && InFlight.Count >= MaximumInFlight)
        {
            return new CompareAnswer(CompareState.Unavailable, Reason: $"slskd is already comparing {MaximumInFlight} sets of files; try again shortly");
        }

        var lazy = InFlight.GetOrAdd(key, _ => new Lazy<Task<CompareAnswer>>(() => AskAndKeepAsync(key, located, files)));
        var task = lazy.Value;

        if (!wait && !task.IsCompleted)
        {
            return new CompareAnswer(CompareState.Pending, RetryAfter: PollAfter);
        }

        return await task.WaitAsync(cancellationToken);
    }

    private static async Task<string> ReasonOf(HttpResponseMessage response)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

            // FastAPI's { "detail": "..." }, or any provider's { "error": "..." }
            foreach (var name in new[] { "detail", "error", "message" })
            {
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // not JSON; the status says enough
        }

        return "the provider will not compare these";
    }

    private async Task<CompareAnswer> AskAndKeepAsync(string key, List<(string Root, string File)> located, IReadOnlyList<FileInfo> files)
    {
        try
        {
            var (answer, keepFor) = located is null ? await CompareHereAsync(files) : await AskAsync(located, files);

            if (keepFor is not null)
            {
                Answers.Set(key, answer, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = keepFor });
            }

            return answer;
        }
        finally
        {
            // by the request itself rather than by whoever was waiting, who may have stopped waiting
            InFlight.TryRemove(key, out _);
        }
    }

    private async Task<(CompareAnswer Answer, TimeSpan? KeepFor)> CompareHereAsync(IReadOnlyList<FileInfo> files)
    {
        await Limit.WaitAsync();

        try
        {
            return (new CompareAnswer(CompareState.Ready, await Builtin.CompareAsync(files)), TimeSpan.FromDays(1));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warning("The built-in comparer failed for {Count} file(s): {Message}", files.Count, ex.Message);
            return (new CompareAnswer(CompareState.Unavailable, Reason: "the comparison failed"), UnavailableFor);
        }
        finally
        {
            Limit.Release();
        }
    }

    private async Task<(CompareAnswer Answer, TimeSpan? KeepFor)> AskAsync(List<(string Root, string File)> located, IReadOnlyList<FileInfo> files)
    {
        var settings = Settings.Provider;

        await Limit.WaitAsync();

        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var response = await SendAsync(settings, located, files);

                if (response is null)
                {
                    return (new CompareAnswer(CompareState.Unavailable, Reason: "the provider did not answer"), UnavailableFor);
                }

                // a file's size or mtime disagree with the provider's view of it. usually a network filesystem's
                // attribute cache rather than a different file, so once more after a moment, then give up
                if (response.StatusCode == HttpStatusCode.Conflict && attempt == 0)
                {
                    await Task.Delay(ConflictRetry);
                    continue;
                }

                var (state, keepFor) = Classify(response.StatusCode);

                if (state == CompareState.Pending)
                {
                    var retryAfter = (int?)response.Headers.RetryAfter?.Delta?.TotalSeconds;
                    return (new CompareAnswer(state, RetryAfter: Math.Clamp(retryAfter ?? 5, 1, 300)), null);
                }

                if (state == CompareState.Refused)
                {
                    Log.Warning("Compare provider refused {Count} file(s): {Reason}", files.Count, await ReasonOf(response));
                    return (new CompareAnswer(state, Reason: RefusedByProvider), keepFor);
                }

                if (state != CompareState.Ready)
                {
                    Log.Warning("Compare provider answered {Status} for {Count} file(s)", (int)response.StatusCode, files.Count);
                    return (new CompareAnswer(state, Reason: $"the provider answered {(int)response.StatusCode}"), keepFor);
                }

                using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("contract", out var contract)
                    || !contract.TryGetInt32(out var version)
                    || version != Contract)
                {
                    Log.Warning("Compare provider speaks a different contract than {Contract}; ignoring its answers", Contract);
                    return (new CompareAnswer(CompareState.Unavailable, Reason: "the provider speaks a different contract"), UnavailableFor);
                }

                // a document whose files do not line up with what was sent would put one file's verdict on another
                if (!root.TryGetProperty("files", out var listed) || listed.ValueKind != JsonValueKind.Array || listed.GetArrayLength() != files.Count)
                {
                    Log.Warning("Compare provider answered about a different number of files than the {Count} it was sent", files.Count);
                    return (new CompareAnswer(CompareState.Unavailable, Reason: "the provider's answer does not match the files sent"), UnavailableFor);
                }

                return (new CompareAnswer(CompareState.Ready, root.Clone()), keepFor);
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException)
        {
            Log.Warning("Compare provider failed for {Count} file(s): {Message}", files.Count, ex.Message);
            return (new CompareAnswer(CompareState.Unavailable, Reason: "the provider failed"), UnavailableFor);
        }
        finally
        {
            Limit.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Options.TransfersOptions.CompareProviderOptions settings, List<(string Root, string File)> located, IReadOnlyList<FileInfo> files)
    {
        var body = new
        {
            files = located.Select((pair, index) => new
            {
                root = pair.Root,
                file = pair.File,
                size = files[index].Length,
                mtime = new DateTimeOffset(files[index].LastWriteTimeUtc).ToUnixTimeMilliseconds(),
            }),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Url.TrimEnd('/') + "/v1/compare")
        {
            Content = JsonContent.Create(body),
        };

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
            Log.Warning("Compare provider did not answer for {Count} file(s): {Message}", files.Count, ex.Message);
            return null;
        }
    }
}
