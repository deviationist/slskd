// <copyright file="AnalysisStore.cs" company="JP Dillingham">
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
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using slskd.Transfers;

/// <summary>
///     One analysis, stored under its own id.
/// </summary>
/// <remarks>
///     A provider that gives an id gives one made of what was analysed and how: a checksum of the audio alone, and the
///     analyser's version -- <c>"&lt;checksum&gt;-v5"</c>. Two downloads of the same audio then share one analysis, and
///     a new version of the analyser is a new id, so nothing it made under the old rules passes for its answer now.
/// </remarks>
public class StoredAnalysis
{
    /// <summary>
    ///     Gets or sets the provider's id for the analysis, or one made for it where the provider gave none.
    /// </summary>
    [Key]
    public string Id { get; set; }

    /// <summary>
    ///     Gets or sets the provider's version, as the document gives it.
    /// </summary>
    [Required]
    public string Version { get; set; }

    /// <summary>
    ///     Gets or sets the verdict's level, stored apart so a list can draw its badges without the documents.
    /// </summary>
    [Required]
    public string Level { get; set; }

    /// <summary>
    ///     Gets or sets the verdict's headline.
    /// </summary>
    [Required]
    public string Headline { get; set; }

    /// <summary>
    ///     Gets or sets the provider's document, as JSON.
    /// </summary>
    [Required]
    public string Document { get; set; }

    /// <summary>
    ///     Gets or sets when it was stored.
    /// </summary>
    public DateTime StoredAt { get; set; }
}

/// <summary>
///     Which analysis a download has.
/// </summary>
public class DownloadAnalysis
{
    /// <summary>
    ///     Gets or sets the download's id.
    /// </summary>
    [Key]
    public Guid TransferId { get; set; }

    /// <summary>
    ///     Gets or sets the id of its <see cref="StoredAnalysis"/>.
    /// </summary>
    [Required]
    public string AnalysisId { get; set; }
}

/// <summary>
///     The verdict a row's badge shows.
/// </summary>
/// <param name="Level">The level, which colours the badge.</param>
/// <param name="Headline">The headline, which is its text.</param>
public record AnalysisVerdict(string Level, string Headline);

/// <summary>
///     Every analysis made of a download, by slskd's own analyser or a provider, stored for good in the Transfers
///     database.
/// </summary>
/// <remarks>
///     <para>
///         Stored by download rather than by file, because a download is one file whose audio never changes: retagging,
///         renaming or moving it changes nothing an analysis looked at. Its id is the one key that needs no look at the
///         disk, which is what lets the downloads list -- served every second or so, of hundreds of rows, over a network
///         filesystem -- carry each row's verdict, read from the database with the rows.
///     </para>
///     <para>
///         Only a finished analysis is stored. "Nothing to show" and "the provider is down" are answers about now,
///         and stay with <see cref="AnalysisProvider"/>, which forgets them.
///     </para>
///     <para>
///         Stored until the analyser changes. A document made by a version other than the one answering now is deleted
///         as soon as that version is known -- at once for slskd's own analyser, on its first answer for a provider --
///         and the operator can delete the lot from the settings.
///     </para>
///     <para>
///         And stored only for downloads still listed: daily, and on first use, what is stored for a download since
///         removed -- by hand, with its file, or by retention -- is deleted, and so is any analysis no download is left
///         to use. Removing a download is a soft delete, so the record stays and a join tells the two apart.
///     </para>
/// </remarks>
public class AnalysisStore
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AnalysisStore"/> class.
    /// </summary>
    /// <param name="contextFactory">The transfers database context factory.</param>
    /// <param name="optionsMonitor">The options, which say whether slskd's own analyser is the one answering.</param>
    public AnalysisStore(IDbContextFactory<TransfersDbContext> contextFactory, Microsoft.Extensions.Options.IOptionsMonitor<Options> optionsMonitor = null)
    {
        ContextFactory = contextFactory;
        OptionsMonitor = optionsMonitor;

        // a change of source -- to the built-in analyser, or away from it -- is looked at again on next use
        optionsMonitor?.OnChange((_, _) =>
        {
            lock (Lock)
            {
                Prepared = false;
            }
        });

        // no daily tick on the clock: checked hourly, swept once a day has passed. a removed download is not listed,
        // so its analysis sitting there until then costs a few KB and shows nowhere
        Clock.EveryHour += (_, _) => Task.Run(() =>
        {
            try
            {
                if (DateTime.UtcNow - LastSwept >= SweepEvery)
                {
                    Sweep();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Could not sweep the stored analyses: {Message}", ex.Message);
            }
        });
    }

    private IDbContextFactory<TransfersDbContext> ContextFactory { get; }
    private Microsoft.Extensions.Options.IOptionsMonitor<Options> OptionsMonitor { get; }

    // the version answers are made under now, when slskd knows it without asking: its own analyser's
    private string Expected => OptionsMonitor?.CurrentValue.Player.Analysis.UsesBuiltin == true ? Builtin.BuiltinAnalyser.Version : null;
    private ILogger Log { get; } = Serilog.Log.ForContext<AnalysisStore>();
    private object Lock { get; } = new();

    private static readonly TimeSpan SweepEvery = TimeSpan.FromDays(1);

    // whether the first-use work -- the sweep, and dropping what another version made -- is done for the source now
    private bool Prepared { get; set; }
    private bool Swept { get; set; }
    private DateTime LastSwept { get; set; } = DateTime.MinValue;

    // the version answers were last seen made under
    private string Version { get; set; }

    /// <summary>
    ///     Gets the number of downloads with an analysis stored.
    /// </summary>
    public int Count
    {
        get
        {
            lock (Lock)
            {
                using var context = Open();
                return context.DownloadAnalyses.Count();
            }
        }
    }

    /// <summary>
    ///     The verdict of each of <paramref name="downloads"/> that has one stored.
    /// </summary>
    /// <param name="downloads">The downloads' ids.</param>
    /// <returns>The verdicts, by download.</returns>
    public IReadOnlyDictionary<Guid, AnalysisVerdict> VerdictsOf(IEnumerable<Guid> downloads)
    {
        var ids = downloads.ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<Guid, AnalysisVerdict>();
        }

        lock (Lock)
        {
            using var context = Open();

            return context.DownloadAnalyses
                .Where(d => ids.Contains(d.TransferId))
                .Join(context.Analyses, d => d.AnalysisId, a => a.Id, (d, a) => new { d.TransferId, a.Level, a.Headline })
                .AsEnumerable()
                .ToDictionary(r => r.TransferId, r => new AnalysisVerdict(r.Level, r.Headline));
        }
    }

    /// <summary>
    ///     The document stored for <paramref name="download"/>, if one is.
    /// </summary>
    /// <param name="download">The download's id.</param>
    /// <param name="document">The document.</param>
    /// <returns>Whether one is stored.</returns>
    public bool TryGet(Guid download, out JsonElement document)
    {
        document = default;

        lock (Lock)
        {
            using var context = Open();

            var json = context.DownloadAnalyses
                .Where(d => d.TransferId == download)
                .Join(context.Analyses, d => d.AnalysisId, a => a.Id, (_, a) => a.Document)
                .FirstOrDefault();

            if (json is null)
            {
                return false;
            }

            using var parsed = JsonDocument.Parse(json);
            document = parsed.RootElement.Clone();
            return true;
        }
    }

    /// <summary>
    ///     Stores <paramref name="document"/> as the analysis of <paramref name="download"/>.
    /// </summary>
    /// <param name="download">The download's id.</param>
    /// <param name="document">The analyser's document, already checked against the contract.</param>
    public virtual void Save(Guid download, JsonElement document)
    {
        var version = StringAt(document, "provider", "version") ?? string.Empty;
        var id = StringAt(document, "id") ?? $"{download}-{version}";
        var level = StringAt(document, "verdict", "level") ?? "ok";
        var headline = StringAt(document, "verdict", "headline") ?? string.Empty;

        lock (Lock)
        {
            using var context = Open();

            // the analyser has moved on: what it made under other rules is no longer its answer
            if (Version is not null && Version != version)
            {
                DropOtherVersions(context, version);
            }

            Version = version;

            if (!context.Analyses.Any(a => a.Id == id))
            {
                context.Analyses.Add(new StoredAnalysis
                {
                    Id = id,
                    Version = version,
                    Level = level,
                    Headline = headline,
                    Document = document.GetRawText(),
                    StoredAt = DateTime.UtcNow,
                });
            }

            var link = context.DownloadAnalyses.FirstOrDefault(d => d.TransferId == download);

            if (link is null)
            {
                context.DownloadAnalyses.Add(new DownloadAnalysis { TransferId = download, AnalysisId = id });
            }
            else
            {
                link.AnalysisId = id;
            }

            context.SaveChanges();
        }
    }

    /// <summary>
    ///     Deletes every stored analysis, for when the analyser has changed in a way its version does not say.
    /// </summary>
    /// <returns>The number of downloads whose analysis was deleted.</returns>
    public int Clear()
    {
        lock (Lock)
        {
            using var context = ContextFactory.CreateDbContext();

            var count = context.DownloadAnalyses.ExecuteDelete();
            context.Analyses.ExecuteDelete();

            Version = null;

            Log.Information("Deleted the stored analyses: {Count} downloads will be analysed again when next shown", count);
            return count;
        }
    }

    /// <summary>
    ///     Deletes what is stored for downloads no longer listed, and every analysis no download is left to use.
    /// </summary>
    /// <returns>The number of downloads whose analysis was deleted.</returns>
    public int Sweep()
    {
        lock (Lock)
        {
            LastSwept = DateTime.UtcNow;
            Swept = true;

            using var context = ContextFactory.CreateDbContext();

            var dropped = context.DownloadAnalyses
                .Where(d => !context.Transfers.Any(t => t.Id == d.TransferId && !t.Removed))
                .ExecuteDelete();

            var unused = context.Analyses
                .Where(a => !context.DownloadAnalyses.Any(d => d.AnalysisId == a.Id))
                .ExecuteDelete();

            if (dropped > 0 || unused > 0)
            {
                Log.Information("Deleted the analysis of {Count} removed downloads, and {Unused} analyses nothing uses", dropped, unused);
            }

            return dropped;
        }
    }

    // deletes every analysis not made under `version`, and the downloads' links to them
    private void DropOtherVersions(TransfersDbContext context, string version)
    {
        var stale = context.Analyses.Where(a => a.Version != version).Select(a => a.Id).ToList();

        if (stale.Count == 0)
        {
            return;
        }

        context.DownloadAnalyses.Where(d => stale.Contains(d.AnalysisId)).ExecuteDelete();
        context.Analyses.Where(a => stale.Contains(a.Id)).ExecuteDelete();

        Log.Information("Analyses are now made by {Version}; deleted {Count} made by another", version, stale.Count);
    }

    private static string StringAt(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    // a context, with the first-use work done: called under the lock
    private TransfersDbContext Open()
    {
        if (!Prepared)
        {
            Prepared = true;

            // whatever was removed while slskd was not running
            if (!Swept)
            {
                Sweep();
            }

            using var context = ContextFactory.CreateDbContext();

            Version = context.Analyses.OrderByDescending(a => a.StoredAt).Select(a => a.Version).FirstOrDefault();

            // slskd's own analyser has a version slskd knows, so what any other made -- an older version of it, or
            // another source -- is dropped now, rather than when the first new answer happens to arrive
            if (Expected is { } expected)
            {
                DropOtherVersions(context, expected);
                Version = expected;
            }
        }

        return ContextFactory.CreateDbContext();
    }
}
