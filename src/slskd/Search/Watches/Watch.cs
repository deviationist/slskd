// <copyright file="Watch.cs" company="JP Dillingham">
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

namespace slskd.Search.Watches;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
///     A standing search: one that is re-run on a schedule, and which reports files it has not reported before.
/// </summary>
/// <remarks>
///     <para>
///         Keyed on the id of the search it watches, and that is not incidental. The set of files already reported is
///         kept per search, so a run cannot create a new search row -- a new id would reset the memory every time.
///         A watch therefore re-runs its search <em>in place</em>: same row, same id, same url, new token.
///     </para>
///     <para>
///         Two watches over the same text are two independent memories, and the second will report a file the first
///         already mentioned. That is intended: a new watch is a new question.
///     </para>
/// </remarks>
public record Watch
{
    /// <summary>
    ///     Gets the id of the search this watches.
    /// </summary>
    [Key]
    public Guid SearchId { get; init; }

    /// <summary>
    ///     Gets or sets a value indicating whether this watch runs.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the recurrence rule, in RFC 5545 form, without a DTSTART.
    /// </summary>
    public string Rrule { get; set; }

    /// <summary>
    ///     Gets or sets the IANA time zone the rule is interpreted in.
    /// </summary>
    /// <remarks>
    ///     Without this, "every day at 03:00" means a different instant in summer than in winter -- and the server
    ///     almost certainly does not keep the operator's clock.
    /// </remarks>
    public string TimeZone { get; set; }

    /// <summary>
    ///     Gets or sets the instant the recurrence is anchored to.
    /// </summary>
    public DateTime Anchor { get; set; }

    /// <summary>
    ///     Gets or sets the instant this watch is next due, persisted so a restart does not shift the schedule.
    /// </summary>
    public DateTime? NextRunAt { get; set; }

    /// <summary>
    ///     Gets or sets the instant this watch last ran.
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>
    ///     Gets or sets the address to notify, or null to use the configured default.
    /// </summary>
    public string NotifyEmail { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether locked files count as hits.
    /// </summary>
    public bool IncludeLocked { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether a response must have a free upload slot to count.
    /// </summary>
    public bool RequireFreeSlot { get; set; }

    /// <summary>
    ///     Gets or sets the filter string, in the same language the results page uses.
    /// </summary>
    public string Filter { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether new files are queued for download as they are found.
    /// </summary>
    /// <remarks>
    ///     For the case this feature exists for: a rare track whose only holder is rarely online. Waiting until the
    ///     mail is read can mean waiting until they have gone again.
    /// </remarks>
    public bool AutoDownload { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether this watch still owes itself a seeding.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Set when a watch is created asking to be seeded over a search that has not finished yet -- which is
    ///         every watch created together with its search, because the search is started and the watch written
    ///         within the same breath. Seeding then reads a search that has found nothing, records nothing, and the
    ///         first scheduled run reports the entire haul as though it were new.
    ///     </para>
    ///     <para>
    ///         The next run seeds from what the search holds by then -- the original haul -- before re-running it,
    ///         and clears this. The run still reports whatever is genuinely new, so the seeding costs no run.
    ///     </para>
    /// </remarks>
    public bool SeedPending { get; set; }

    /// <summary>
    ///     Gets or sets the instant this watch was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Decides whether a watch being saved must seed later rather than now.
    /// </summary>
    /// <remarks>
    ///     Seeding reads what the search holds, so it can only work once the search holds something. A watch created
    ///     together with its search is written within a breath of the search starting, and so asks a search that has
    ///     found nothing -- which is the ordinary path through the searches page, not an edge case.
    /// </remarks>
    /// <param name="isNew">Whether this watch is being created rather than edited.</param>
    /// <param name="seedRequested">Whether the caller asked not to be told what the search has already found.</param>
    /// <param name="searchIsComplete">Whether the search has finished, and so has something to seed from.</param>
    /// <returns>A value indicating whether seeding must wait for the first run.</returns>
    public static bool SeedingMustWait(bool isNew, bool seedRequested, bool searchIsComplete)
        => isNew && seedRequested && !searchIsComplete;

    /// <summary>
    ///     Gets or sets the instant this watch was last changed.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
///     A file a watch has seen.
/// </summary>
/// <remarks>
///     Identity is the username and the remote path. A peer re-encoding a file does not make it a new one; you were
///     told about that path from that user already.
/// </remarks>
public record WatchFile
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid SearchId { get; init; }

    public string Username { get; init; }

    public string Filename { get; init; }

    public long Size { get; init; }

    public DateTime FirstSeenAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    ///     Gets the instant this file was reported, or null if it was seeded rather than reported.
    /// </summary>
    public DateTime? NotifiedAt { get; init; }

    /// <summary>
    ///     Gets a value indicating whether this file was recorded without being reported.
    /// </summary>
    /// <remarks>
    ///     The "do not tell me what this first run finds" switch. Seeding the memory rather than emptying it is what
    ///     makes the first notification the first <em>new</em> thing, rather than the whole of what was already there.
    /// </remarks>
    public bool Seeded { get; init; }
}

/// <summary>
///     What happened on one execution of a watch.
/// </summary>
/// <remarks>
///     Kept instead of retaining each run's responses, which would grow the search database without bound.
/// </remarks>
public record WatchRun
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid SearchId { get; init; }

    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    public DateTime? EndedAt { get; set; }

    public int ResponseCount { get; set; }

    public int FileCount { get; set; }

    public int MatchCount { get; set; }

    public int NewCount { get; set; }

    /// <summary>
    ///     Gets or sets the number of files queued for download.
    /// </summary>
    public int EnqueuedCount { get; set; }

    /// <summary>
    ///     Gets or sets the number of matches a global ignore removed.
    /// </summary>
    /// <remarks>
    ///     Recorded so that a watch which suddenly reports nothing can be told apart from one whose results are all
    ///     being ignored. A suppression nobody can see is the failure this feature invites.
    /// </remarks>
    public int IgnoredCount { get; set; }

    public WatchRunOutcome Outcome { get; set; }

    public string Error { get; set; }
}

/// <summary>
///     How a run ended.
/// </summary>
public enum WatchRunOutcome
{
    /// <summary>
    ///     The run is still in progress.
    /// </summary>
    Running = 0,

    /// <summary>
    ///     The run completed.
    /// </summary>
    Completed = 1,

    /// <summary>
    ///     The run failed.
    /// </summary>
    Errored = 2,

    /// <summary>
    ///     The run was not attempted, because the server could not be searched.
    /// </summary>
    Skipped = 3,
}

/// <summary>
///     A notification a watch sent, or tried to.
/// </summary>
public record WatchNotification
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid SearchId { get; init; }

    public DateTime SentAt { get; init; } = DateTime.UtcNow;

    public string Adapter { get; init; }

    public string Recipient { get; init; }

    public string Subject { get; init; }

    public int FileCount { get; init; }

    /// <summary>
    ///     Gets the files reported, as JSON.
    /// </summary>
    public string FilesJson { get; init; }

    public bool Sent { get; init; }

    /// <summary>
    ///     Gets the reason a send failed, if it did.
    /// </summary>
    /// <remarks>
    ///     Kept so that a watch whose mail has been bouncing for a month says so on its own page, rather than only in
    ///     the application log where nobody is looking.
    /// </remarks>
    public string Error { get; init; }
}
