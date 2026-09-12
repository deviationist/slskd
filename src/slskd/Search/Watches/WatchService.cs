// <copyright file="WatchService.cs" company="JP Dillingham">
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

namespace slskd.Search.Watches;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using slskd.Integrations.Mail;
using slskd.Transfers;
using ISoulseekClient = Soulseek.ISoulseekClient;
using SearchOptions = Soulseek.SearchOptions;
using SearchQuery = Soulseek.SearchQuery;
using SearchScope = Soulseek.SearchScope;
using SearchStates = Soulseek.SearchStates;
using SoulseekClientStates = Soulseek.SoulseekClientStates;

/// <summary>
///     Runs standing searches, and reports files they have not reported before.
/// </summary>
public class WatchService
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WatchService"/> class.
    /// </summary>
    public WatchService(
        ISearchService searchService,
        TransferService transferService,
        IDbContextFactory<SearchDbContext> contextFactory,
        ISoulseekClient soulseekClient,
        MailService mailService,
        IOptionsMonitor<Options> optionsMonitor)
    {
        Searches = searchService;
        Transfers = transferService;
        ContextFactory = contextFactory;
        Client = soulseekClient;
        Mail = mailService;
        OptionsMonitor = optionsMonitor;

        Clock.EveryMinute += (_, _) => _ = RunDueAsync();
    }

    private ISearchService Searches { get; }
    private TransferService Transfers { get; }
    private IDbContextFactory<SearchDbContext> ContextFactory { get; }
    private ISoulseekClient Client { get; }
    private MailService Mail { get; }
    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<WatchService>();

    /// <summary>
    ///     One search at a time, across every watch. The server counts searches from a client and does not care
    ///     which watch asked, so this is global rather than per watch.
    /// </summary>
    private SemaphoreSlim Gate { get; } = new(1, 1);

    private Options.SearchesOptions.WatchesOptions WatchOptions => OptionsMonitor.CurrentValue.Searches.Watches;

    /// <summary>
    ///     Lists everything no watch should report.
    /// </summary>
    /// <returns>The ignores.</returns>
    public async Task<List<Ignore>> ListIgnoresAsync()
    {
        using var context = ContextFactory.CreateDbContext();
        return await context.Ignores.AsNoTracking().OrderByDescending(i => i.CreatedAt).ToListAsync();
    }

    /// <summary>
    ///     Adds an ignore, or returns the one that already covers it.
    /// </summary>
    /// <param name="ignore">The ignore to add.</param>
    /// <returns>The stored ignore.</returns>
    public async Task<Ignore> AddIgnoreAsync(Ignore ignore)
    {
        using var context = ContextFactory.CreateDbContext();

        var existing = await context.Ignores
            .FirstOrDefaultAsync(i => i.Kind == ignore.Kind && i.Value == ignore.Value);

        if (existing is not null)
        {
            return existing;
        }

        context.Ignores.Add(ignore);
        await context.SaveChangesAsync();

        Log.Information("Ignoring {Kind} '{Value}' in every watch", ignore.Kind, ignore.Value);

        return ignore;
    }

    /// <summary>
    ///     Removes an ignore.
    /// </summary>
    /// <param name="id">The id of the ignore.</param>
    /// <returns>The operation context.</returns>
    public async Task DeleteIgnoreAsync(Guid id)
    {
        using var context = ContextFactory.CreateDbContext();
        await context.Ignores.Where(i => i.Id == id).ExecuteDeleteAsync();
    }

    private async Task<IgnoreSet> IgnoresAsync()
    {
        using var context = ContextFactory.CreateDbContext();
        return new IgnoreSet(await context.Ignores.AsNoTracking().ToListAsync());
    }

    /// <summary>
    ///     Lists every watch.
    /// </summary>
    /// <returns>The watches.</returns>
    public async Task<List<Watch>> ListAsync()
    {
        using var context = ContextFactory.CreateDbContext();
        return await context.Watches.AsNoTracking().ToListAsync();
    }

    /// <summary>
    ///     Finds the watch on the specified search, if there is one.
    /// </summary>
    /// <param name="searchId">The id of the search.</param>
    /// <returns>The watch, or null.</returns>
    public async Task<Watch> FindAsync(Guid searchId)
    {
        using var context = ContextFactory.CreateDbContext();
        return await context.Watches.AsNoTracking().FirstOrDefaultAsync(w => w.SearchId == searchId);
    }

    /// <summary>
    ///     Creates or replaces a watch.
    /// </summary>
    /// <param name="watch">The watch to save.</param>
    /// <returns>The saved watch.</returns>
    public async Task<Watch> UpsertAsync(Watch watch)
    {
        using var context = ContextFactory.CreateDbContext();

        var existing = await context.Watches.FirstOrDefaultAsync(w => w.SearchId == watch.SearchId);

        watch.UpdatedAt = DateTime.UtcNow;
        watch.NextRunAt = watch.Enabled
            ? Recurrence.Next(watch.Rrule, watch.Anchor, watch.TimeZone, DateTime.UtcNow)
            : null;

        if (existing is null)
        {
            context.Watches.Add(watch);
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(watch);
        }

        await context.SaveChangesAsync();

        Log.Information("Watch on search {SearchId} saved; next run {NextRunAt}", watch.SearchId, watch.NextRunAt);

        return watch;
    }

    /// <summary>
    ///     Deletes the watch on the specified search, and everything it remembered.
    /// </summary>
    /// <param name="searchId">The id of the search.</param>
    /// <returns>The operation context.</returns>
    public async Task DeleteAsync(Guid searchId)
    {
        using var context = ContextFactory.CreateDbContext();

        await context.Watches.Where(w => w.SearchId == searchId).ExecuteDeleteAsync();
        await context.WatchFiles.Where(f => f.SearchId == searchId).ExecuteDeleteAsync();
        await context.WatchRuns.Where(r => r.SearchId == searchId).ExecuteDeleteAsync();
        await context.WatchNotifications.Where(n => n.SearchId == searchId).ExecuteDeleteAsync();
    }

    /// <summary>
    ///     Lists a watch's runs, newest first.
    /// </summary>
    /// <param name="searchId">The id of the search.</param>
    /// <param name="limit">The most to return.</param>
    /// <returns>The runs.</returns>
    public async Task<List<WatchRun>> ListRunsAsync(Guid searchId, int limit = 50)
    {
        using var context = ContextFactory.CreateDbContext();
        return await context.WatchRuns.AsNoTracking()
            .Where(r => r.SearchId == searchId)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    ///     Lists a watch's notifications, newest first.
    /// </summary>
    /// <param name="searchId">The id of the search.</param>
    /// <param name="limit">The most to return.</param>
    /// <returns>The notifications.</returns>
    public async Task<List<WatchNotification>> ListNotificationsAsync(Guid searchId, int limit = 50)
    {
        using var context = ContextFactory.CreateDbContext();
        return await context.WatchNotifications.AsNoTracking()
            .Where(n => n.SearchId == searchId)
            .OrderByDescending(n => n.SentAt)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    ///     Records every file the specified search currently matches as already reported, without reporting any of it.
    /// </summary>
    /// <remarks>
    ///     The "do not tell me what this first run found" switch. Seeding rather than emptying is what makes the first
    ///     notification the first <em>new</em> thing rather than the whole of what was already there.
    /// </remarks>
    /// <param name="watch">The watch to seed.</param>
    /// <returns>The number of files recorded.</returns>
    public async Task<int> SeedAsync(Watch watch)
    {
        var search = await Searches.FindAsync(s => s.Id == watch.SearchId, includeResponses: true);

        if (search is null)
        {
            return 0;
        }

        var matches = Matches(watch, search);

        return await RecordAsync(watch.SearchId, matches, seeded: true);
    }

    /// <summary>
    ///     Runs every watch that is due.
    /// </summary>
    /// <returns>The operation context.</returns>
    public async Task RunDueAsync()
    {
        if (!WatchOptions.Enabled)
        {
            return;
        }

        // if a run is already in progress, do nothing: the next tick will find whatever is still due. waiting on the
        // gate here would queue a minute's worth of ticks behind a long search
        if (!Gate.Wait(0))
        {
            return;
        }

        try
        {
            var now = DateTime.UtcNow;

            List<Watch> due;

            using (var context = ContextFactory.CreateDbContext())
            {
                due = await context.Watches.AsNoTracking()
                    .Where(w => w.Enabled && w.NextRunAt != null && w.NextRunAt <= now)
                    .OrderBy(w => w.NextRunAt)
                    .ToListAsync();
            }

            if (due.Count == 0)
            {
                return;
            }

            if (!Client.State.HasFlag(SoulseekClientStates.LoggedIn))
            {
                // left due rather than banked, and deliberately not recorded: a run record per minute for as long as
                // the server is unreachable is noise, and the occurrence is not lost -- it runs once on reconnect
                Log.Debug("{Count} watch(es) are due but the server is not connected; leaving them due", due.Count);
                return;
            }

            foreach (var watch in due)
            {
                try
                {
                    await RunAsync(watch);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Watch on search {SearchId} failed: {Message}", watch.SearchId, ex.Message);
                }

                await Task.Delay(TimeSpan.FromSeconds(WatchOptions.Gap));
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    ///     Runs the specified <paramref name="watch"/> now, whether or not it is due.
    /// </summary>
    /// <param name="watch">The watch to run.</param>
    /// <returns>The run.</returns>
    public async Task<WatchRun> RunAsync(Watch watch)
    {
        var run = new WatchRun { SearchId = watch.SearchId, Outcome = WatchRunOutcome.Running };

        if (!Client.State.HasFlag(SoulseekClientStates.LoggedIn))
        {
            run.Outcome = WatchRunOutcome.Skipped;
            run.EndedAt = DateTime.UtcNow;
            run.Error = "The server is not connected";

            await SaveRunAsync(run);
            return run;
        }

        var search = await Searches.FindAsync(s => s.Id == watch.SearchId);

        if (search is null)
        {
            run.Outcome = WatchRunOutcome.Errored;
            run.EndedAt = DateTime.UtcNow;
            run.Error = "The search this watch belongs to no longer exists";

            await SaveRunAsync(run);
            return run;
        }

        try
        {
            Log.Information("Running watch on search {SearchId}: '{Text}'", watch.SearchId, search.SearchText);

            // in place: same id, same row, same url. the memory of what has been reported is keyed on that id
            await Searches.StartAsync(
                id: watch.SearchId,
                query: SearchQuery.FromText(search.SearchText),
                scope: SearchScope.Network,
                options: new SearchOptions(),
                replaceExisting: true);

            var completed = await AwaitCompletionAsync(watch.SearchId);

            if (completed is null)
            {
                run.Outcome = WatchRunOutcome.Errored;
                run.Error = "The search did not complete";
            }
            else
            {
                run.ResponseCount = completed.ResponseCount;
                run.FileCount = completed.FileCount;

                var matches = Matches(watch, completed);
                run.MatchCount = matches.Count;

                // before the memory, not after: an ignored file is never marked as reported, so removing the ignore
                // later reports it as new rather than never
                var ignores = await IgnoresAsync();
                var kept = matches.Where(m => !ignores.Ignores(m.Username, m.Filename)).ToList();

                run.IgnoredCount = matches.Count - kept.Count;
                matches = kept;

                var added = await RecordAsync(watch.SearchId, matches, seeded: false);
                run.NewCount = added;

                var isNew = matches.Where(m => m.IsNew).ToList();

                if (watch.AutoDownload && isNew.Count > 0)
                {
                    run.EnqueuedCount = await EnqueueAsync(isNew);
                }

                if (added > 0)
                {
                    await NotifyAsync(watch, completed.SearchText, isNew, run.EnqueuedCount);
                }

                run.Outcome = WatchRunOutcome.Completed;
            }
        }
        catch (Exception ex)
        {
            run.Outcome = WatchRunOutcome.Errored;
            run.Error = ex.Message;
            Log.Error(ex, "Watch on search {SearchId} failed: {Message}", watch.SearchId, ex.Message);
        }

        run.EndedAt = DateTime.UtcNow;
        await SaveRunAsync(run);
        await RescheduleAsync(watch);

        return run;
    }

    /// <summary>
    ///     Returns the files of <paramref name="search"/> that <paramref name="watch"/> cares about.
    /// </summary>
    /// <param name="watch">The watch whose criteria to apply.</param>
    /// <param name="search">The search whose responses to read.</param>
    /// <returns>The matching files.</returns>
    public static List<Match> Matches(Watch watch, Search search)
    {
        var filter = SearchFilters.Parse(watch.Filter);
        var matches = new List<Match>();

        foreach (var response in search.Responses ?? [])
        {
            if (watch.RequireFreeSlot && !response.HasFreeUploadSlot)
            {
                continue;
            }

            var filtered = SearchFilters.Apply(filter, response);

            var files = filtered.Files.AsEnumerable();

            if (watch.IncludeLocked)
            {
                files = files.Concat(filtered.LockedFiles);
            }

            foreach (var file in files)
            {
                matches.Add(new Match
                {
                    Username = response.Username,
                    HasFreeUploadSlot = response.HasFreeUploadSlot,
                    UploadSpeed = response.UploadSpeed,
                    QueueLength = response.QueueLength,
                    Filename = file.Filename,
                    Size = file.Size,
                    BitRate = file.BitRate,
                    Length = file.Length,
                });
            }
        }

        return matches;
    }

    private async Task<Search> AwaitCompletionAsync(Guid searchId)
    {
        // the search runs on a background task and updates its own row; there is no completion to await, so the row
        // is the only thing to watch. four minutes is comfortably longer than any search this application will run
        var deadline = DateTime.UtcNow.AddMinutes(4);

        while (DateTime.UtcNow < deadline)
        {
            var search = await Searches.FindAsync(s => s.Id == searchId);

            if (search is not null && search.State.HasFlag(SearchStates.Completed))
            {
                return await Searches.FindAsync(s => s.Id == searchId, includeResponses: true);
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return null;
    }

    private async Task<int> RecordAsync(Guid searchId, List<Match> matches, bool seeded)
    {
        if (matches.Count == 0)
        {
            return 0;
        }

        using var context = ContextFactory.CreateDbContext();

        var known = await context.WatchFiles
            .Where(f => f.SearchId == searchId)
            .Select(f => new { f.Username, f.Filename })
            .ToListAsync();

        var seen = known.Select(k => (k.Username, k.Filename)).ToHashSet();
        var added = 0;

        foreach (var match in matches)
        {
            if (!seen.Add((match.Username, match.Filename)))
            {
                continue;
            }

            match.IsNew = true;
            added++;

            context.WatchFiles.Add(new WatchFile
            {
                SearchId = searchId,
                Username = match.Username,
                Filename = match.Filename,
                Size = match.Size,
                NotifiedAt = seeded ? null : DateTime.UtcNow,
                Seeded = seeded,
            });
        }

        await context.SaveChangesAsync();

        return added;
    }

    /// <summary>
    ///     The most files a notification lists, and the most it records.
    /// </summary>
    /// <remarks>
    ///     A broad search finds hundreds of files it has never reported on every run, and not because anything new
    ///     appeared: the network answers a search with whichever peers happen to reply, and the set differs every
    ///     time. Measured on a popular query, two runs a minute apart returned 252 and 250 responses with 1471 files
    ///     between them that the first run had not seen. A mail listing all of them is unreadable and a record of all
    ///     of them is a third of a megabyte per run, so both are capped and the true count is kept alongside.
    /// </remarks>
    private const int MaximumFilesListed = 50;
    private const int MaximumFilesRecorded = 200;

    /// <summary>
    ///     How the recorded file list is written.
    /// </summary>
    /// <remarks>
    ///     camelCase, because this is read by the web client, and everything else this application sends it is
    ///     camelCase. Written with the serializer's defaults it was PascalCase, so every field of every row read as
    ///     undefined -- a size of "NaN undefined" beside two empty columns.
    /// </remarks>
    private static readonly JsonSerializerOptions RecordedFileOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    ///     Queues the chosen files for download, and returns how many were accepted.
    /// </summary>
    /// <remarks>
    ///     A failure here is recorded and not thrown: the point of a watch is to say what it found, and a peer that
    ///     went offline between answering and being asked must not cost the notification that would have told you.
    /// </remarks>
    private async Task<int> EnqueueAsync(List<Match> isNew)
    {
        var chosen = AutoDownload.Choose(isNew, WatchOptions.DownloadLimit);
        var enqueued = 0;

        foreach (var group in chosen.GroupBy(match => match.Username))
        {
            try
            {
                var files = group.Select(match => (match.Filename, match.Size));
                var (accepted, failed) = await Transfers.Downloads.EnqueueAsync(group.Key, files);

                enqueued += accepted.Count;

                foreach (var (filename, message) in failed)
                {
                    Log.Warning("A watch could not queue '{File}' from {User}: {Message}", filename, group.Key, message);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "A watch could not queue {Count} file(s) from {User}: {Message}", group.Count(), group.Key, ex.Message);
            }
        }

        if (enqueued > 0)
        {
            Log.Information("A watch queued {Count} file(s) for download", enqueued);
        }

        return enqueued;
    }

    private async Task NotifyAsync(Watch watch, string searchText, List<Match> files, int enqueued)
    {
        if (files.Count == 0)
        {
            return;
        }

        var options = OptionsMonitor.CurrentValue.Integrations.Mail;
        var subject = $"slskd: {files.Count} new result{(files.Count == 1 ? string.Empty : "s")} for '{searchText}'";

        var body = new StringBuilder();
        body.AppendLine($"A watch on '{searchText}' found {files.Count} file(s) it has not reported before.");
        body.AppendLine();

        var listed = files.OrderBy(f => f.Username).ThenBy(f => f.Filename).Take(MaximumFilesListed).ToList();

        var baseUrl = options.BaseUrl?.TrimEnd('/');

        foreach (var file in listed)
        {
            body.AppendLine($"  {file.Username}");
            body.AppendLine($"    {file.Filename}");
            body.AppendLine($"    {file.Size / 1024 / 1024} MB{(file.BitRate.HasValue ? $", {file.BitRate} kbps" : string.Empty)}{(file.Length.HasValue ? $", {file.Length / 60}:{file.Length % 60:00}" : string.Empty)}");

            // a link to stop hearing about this one, which is the thing an operator wants at the moment they are
            // reading about a file they do not want. it opens the search and asks before it does anything: the link
            // carries no authority of its own, so following it is safe for anything that prefetches links in mail
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                var name = Uri.EscapeDataString(IgnoreSet.NameOf(file.Filename) ?? string.Empty);
                body.AppendLine($"    Never report this again: {baseUrl}/searches/{watch.SearchId}?ignore={name}");
            }

            body.AppendLine();
        }

        if (enqueued > 0)
        {
            body.AppendLine($"{enqueued} of these have been queued for download.");
            body.AppendLine();
        }

        if (files.Count > listed.Count)
        {
            body.AppendLine($"...and {files.Count - listed.Count} more.");
            body.AppendLine();
            body.AppendLine("A search this broad will report hundreds of files on every run, because the network");
            body.AppendLine("answers with whichever peers happen to reply and that set differs each time. A watch");
            body.AppendLine("is at its best on a search narrow enough that its results are stable.");
            body.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            body.AppendLine($"{options.BaseUrl.TrimEnd('/')}/searches/{watch.SearchId}");
        }

        var mail = new Mail
        {
            To = watch.NotifyEmail,
            Subject = subject,
            Text = body.ToString(),
        };

        string adapter = null;
        string error = null;

        try
        {
            adapter = await Mail.SendAsync(mail);
        }
        catch (Exception ex)
        {
            // recorded rather than thrown: a watch whose mail has been failing for a month should say so on its own
            // page, and a send that failed must not lose the run that produced it
            error = ex.Message;
            Log.Error(ex, "Failed to notify for watch on search {SearchId}: {Message}", watch.SearchId, ex.Message);
        }

        using var context = ContextFactory.CreateDbContext();

        context.WatchNotifications.Add(new WatchNotification
        {
            SearchId = watch.SearchId,
            Adapter = adapter,
            Recipient = string.IsNullOrWhiteSpace(watch.NotifyEmail) ? options.To : watch.NotifyEmail,
            Subject = subject,
            FileCount = files.Count,
            FilesJson = JsonSerializer.Serialize(files.Take(MaximumFilesRecorded), RecordedFileOptions),
            Sent = error is null,
            Error = error,
        });

        await context.SaveChangesAsync();
    }

    private async Task RescheduleAsync(Watch watch)
    {
        using var context = ContextFactory.CreateDbContext();

        var stored = await context.Watches.FirstOrDefaultAsync(w => w.SearchId == watch.SearchId);

        if (stored is null)
        {
            return;
        }

        stored.LastRunAt = DateTime.UtcNow;

        // computed from *now* rather than from the occurrence that was missed, so an application that was down for a
        // week owes one search rather than a hundred and sixty-eight
        stored.NextRunAt = stored.Enabled
            ? Recurrence.Next(stored.Rrule, stored.Anchor, stored.TimeZone, DateTime.UtcNow)
            : null;

        await context.SaveChangesAsync();
    }

    private async Task SaveRunAsync(WatchRun run)
    {
        using var context = ContextFactory.CreateDbContext();
        context.WatchRuns.Add(run);
        await context.SaveChangesAsync();
    }

    /// <summary>
    ///     A file a watch matched.
    /// </summary>
    public class Match
    {
        public string Username { get; init; }

        /// <summary>
        ///     Gets a value indicating whether the peer had a free upload slot when it answered.
        /// </summary>
        public bool HasFreeUploadSlot { get; init; }

        /// <summary>
        ///     Gets the peer's upload speed, as reported with the response.
        /// </summary>
        public int UploadSpeed { get; init; }

        /// <summary>
        ///     Gets the peer's queue length, as reported with the response.
        /// </summary>
        public long QueueLength { get; init; }

        public string Filename { get; init; }

        public long Size { get; init; }

        public int? BitRate { get; init; }

        public int? Length { get; init; }

        /// <summary>
        ///     Gets or sets a value indicating whether this file had not been recorded before this run.
        /// </summary>
        public bool IsNew { get; set; }
    }
}
