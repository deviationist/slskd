// <copyright file="AnalysisCache.cs" company="JP Dillingham">
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
///     One analysis a provider made, kept under its own id.
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
    ///     Gets or sets the verdict's level, kept apart so a list can draw its badges without the documents.
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
    ///     Gets or sets when it was kept.
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
///     Every analysis a provider has made of a download, kept for good.
/// </summary>
/// <remarks>
///     <para>
///         Kept by download rather than by file, because a download is one file whose audio never changes: retagging,
///         renaming or moving it changes nothing an analysis looked at. Its id is the one key that needs no look at the
///         disk, which is what lets the downloads list -- served every second or so, of hundreds of rows, over a network
///         filesystem -- carry each row's verdict.
///     </para>
///     <para>
///         Only a finished analysis is kept here. "Nothing to show" and "the provider is down" are answers about now,
///         and stay with <see cref="AnalysisProvider"/>, which forgets them.
///     </para>
///     <para>
///         Kept until the analyser changes. A document made by a version other than the provider's now is dropped as
///         soon as the provider is seen to have moved on, and the operator can drop the lot from the settings.
///     </para>
///     <para>
///         And kept only for downloads still listed: every five minutes, and on first use, what is kept for a download
///         since removed -- by hand, with its file, or by retention -- is dropped, and so is any analysis no download
///         is left to use. Removing a download is a soft delete, so the record stays and a join tells the two apart.
///     </para>
/// </remarks>
public class AnalysisCache
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AnalysisCache"/> class.
    /// </summary>
    /// <param name="contextFactory">The transfers database context factory.</param>
    public AnalysisCache(IDbContextFactory<TransfersDbContext> contextFactory)
    {
        ContextFactory = contextFactory;

        Clock.EveryFiveMinutes += (_, _) => Task.Run(() =>
        {
            try
            {
                Sweep();
            }
            catch (Exception ex)
            {
                Log.Warning("Could not sweep the analysis cache: {Message}", ex.Message);
            }
        });
    }

    private IDbContextFactory<TransfersDbContext> ContextFactory { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<AnalysisCache>();
    private object Lock { get; } = new();

    // every download's verdict, read once and kept up with: what a list asks for, every poll
    private Dictionary<Guid, AnalysisVerdict> Verdicts { get; set; }

    private bool Swept { get; set; }

    // the version the provider was last seen to speak
    private string Version { get; set; }

    /// <summary>
    ///     Gets the number of downloads with an analysis kept.
    /// </summary>
    public int Count
    {
        get
        {
            lock (Lock)
            {
                return Load().Count;
            }
        }
    }

    /// <summary>
    ///     The verdict of each of <paramref name="downloads"/> that has one kept.
    /// </summary>
    /// <param name="downloads">The downloads' ids.</param>
    /// <returns>The verdicts, by download.</returns>
    public IReadOnlyDictionary<Guid, AnalysisVerdict> VerdictsOf(IEnumerable<Guid> downloads)
    {
        lock (Lock)
        {
            var verdicts = Load();
            var found = new Dictionary<Guid, AnalysisVerdict>();

            foreach (var id in downloads)
            {
                if (verdicts.TryGetValue(id, out var verdict))
                {
                    found[id] = verdict;
                }
            }

            return found;
        }
    }

    /// <summary>
    ///     The document kept for <paramref name="download"/>, if one is.
    /// </summary>
    /// <param name="download">The download's id.</param>
    /// <param name="document">The document.</param>
    /// <returns>Whether one is kept.</returns>
    public bool TryGet(Guid download, out JsonElement document)
    {
        document = default;

        lock (Lock)
        {
            if (!Load().ContainsKey(download))
            {
                return false;
            }

            using var context = ContextFactory.CreateDbContext();

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
    ///     Keeps <paramref name="document"/> as the analysis of <paramref name="download"/>.
    /// </summary>
    /// <param name="download">The download's id.</param>
    /// <param name="document">The provider's document, already checked against the contract.</param>
    public virtual void Remember(Guid download, JsonElement document)
    {
        var version = StringAt(document, "provider", "version") ?? string.Empty;
        var id = StringAt(document, "id") ?? $"{download}-{version}";
        var level = StringAt(document, "verdict", "level") ?? "ok";
        var headline = StringAt(document, "verdict", "headline") ?? string.Empty;

        lock (Lock)
        {
            var verdicts = Load();

            using var context = ContextFactory.CreateDbContext();

            // the provider has moved on: what it made under other rules is no longer its answer
            if (Version is not null && Version != version)
            {
                var stale = context.Analyses.Where(a => a.Version != version).Select(a => a.Id).ToList();

                if (stale.Count > 0)
                {
                    var orphaned = context.DownloadAnalyses.Where(d => stale.Contains(d.AnalysisId)).Select(d => d.TransferId).ToList();

                    context.DownloadAnalyses.Where(d => stale.Contains(d.AnalysisId)).ExecuteDelete();
                    context.Analyses.Where(a => stale.Contains(a.Id)).ExecuteDelete();

                    foreach (var transferId in orphaned)
                    {
                        verdicts.Remove(transferId);
                    }

                    Log.Information("The analysis provider is now {Version}, was {Previous}; dropped {Count} analyses made before", version, Version, stale.Count);
                }
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

            verdicts[download] = new AnalysisVerdict(level, headline);
        }
    }

    /// <summary>
    ///     Drops every analysis kept, for when the analyser has changed in a way its version does not say.
    /// </summary>
    /// <returns>The number of downloads whose analysis was dropped.</returns>
    public int Clear()
    {
        lock (Lock)
        {
            using var context = ContextFactory.CreateDbContext();

            var count = context.DownloadAnalyses.ExecuteDelete();
            context.Analyses.ExecuteDelete();

            Verdicts = new Dictionary<Guid, AnalysisVerdict>();
            Version = null;

            Log.Information("Cleared the analysis cache: {Count} downloads will be analysed again when next shown", count);
            return count;
        }
    }

    /// <summary>
    ///     Drops what is kept for downloads no longer listed, and every analysis no download is left to use.
    /// </summary>
    /// <returns>The number of downloads whose analysis was dropped.</returns>
    public int Sweep()
    {
        lock (Lock)
        {
            using var context = ContextFactory.CreateDbContext();

            var dropped = context.DownloadAnalyses
                .Where(d => !context.Transfers.Any(t => t.Id == d.TransferId && !t.Removed))
                .ExecuteDelete();

            var unused = context.Analyses
                .Where(a => !context.DownloadAnalyses.Any(d => d.AnalysisId == a.Id))
                .ExecuteDelete();

            if (dropped > 0 || unused > 0)
            {
                // read again on next use rather than edited here: a sweep is rare, and the list is cheap to read
                Verdicts = null;
                Log.Information("Dropped the analysis of {Count} removed downloads, and {Unused} analyses nothing uses", dropped, unused);
            }

            return dropped;
        }
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

    private Dictionary<Guid, AnalysisVerdict> Load()
    {
        if (Verdicts is not null)
        {
            return Verdicts;
        }

        // whatever was removed while slskd was not running
        if (!Swept)
        {
            Swept = true;
            Sweep();
        }

        using var context = ContextFactory.CreateDbContext();

        Verdicts = context.DownloadAnalyses
            .Join(context.Analyses, d => d.AnalysisId, a => a.Id, (d, a) => new { d.TransferId, a.Level, a.Headline })
            .AsEnumerable()
            .ToDictionary(r => r.TransferId, r => new AnalysisVerdict(r.Level, r.Headline));

        Version = context.Analyses.OrderByDescending(a => a.StoredAt).Select(a => a.Version).FirstOrDefault();

        return Verdicts;
    }
}
