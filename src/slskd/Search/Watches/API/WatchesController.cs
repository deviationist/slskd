// <copyright file="WatchesController.cs" company="JP Dillingham">
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

namespace slskd.Search.Watches.API;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
///     A watch, as asked for.
/// </summary>
public class WatchRequest
{
    /// <summary>
    ///     Gets or sets a value indicating whether the watch runs.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the recurrence rule, in RFC 5545 form.
    /// </summary>
    [Required]
    public string Rrule { get; set; }

    /// <summary>
    ///     Gets or sets the IANA time zone the rule is read in. Defaults to the configured zone.
    /// </summary>
    public string TimeZone { get; set; }

    /// <summary>
    ///     Gets or sets the address to notify. Defaults to the configured address.
    /// </summary>
    public string NotifyEmail { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether locked files count.
    /// </summary>
    public bool IncludeLocked { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether a response must have a free slot to count.
    /// </summary>
    public bool RequireFreeSlot { get; set; }

    /// <summary>
    ///     Gets or sets the filter string, in the language the results page uses.
    /// </summary>
    public string Filter { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether new files are queued for download as they are found.
    /// </summary>
    public bool AutoDownload { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether what this search has already found should be recorded without
    ///     being reported.
    /// </summary>
    /// <remarks>
    ///     Only meaningful when the watch is created; a watch that has been running has a memory already.
    /// </remarks>
    public bool SeedFromCurrentResults { get; set; }
}

/// <summary>
///     A file or peer to ignore, as asked for.
/// </summary>
public class IgnoreRequest
{
    /// <summary>
    ///     Gets or sets what to match on: Name or User.
    /// </summary>
    public IgnoreKind Kind { get; set; }

    /// <summary>
    ///     Gets or sets the filename or username to match.
    /// </summary>
    [Required]
    public string Value { get; set; }

    /// <summary>
    ///     Gets or sets a note about why.
    /// </summary>
    public string Note { get; set; }
}

/// <summary>
///     Search watches.
/// </summary>
[Route("api/v{version:apiVersion}/searches/{id}/watch")]
[ApiVersion("0")]
[ApiController]
[Produces("application/json")]
[Consumes("application/json")]
public class WatchesController : ControllerBase
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WatchesController"/> class.
    /// </summary>
    public WatchesController(WatchService watchService, ISearchService searchService, IOptionsSnapshot<Options> optionsSnapshot)
    {
        Watches = watchService;
        Searches = searchService;
        OptionsSnapshot = optionsSnapshot;
    }

    private WatchService Watches { get; }
    private ISearchService Searches { get; }
    private IOptionsSnapshot<Options> OptionsSnapshot { get; }

    /// <summary>
    ///     Lists every watch.
    /// </summary>
    /// <remarks>
    ///     Absolute route: the searches list needs to know which of its rows are watched, and asking per row would be
    ///     one request per search on every render.
    /// </remarks>
    /// <returns></returns>
    /// <response code="200">The request completed successfully.</response>
    [HttpGet("/api/v{version:apiVersion}/watches")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(List<Watch>), 200)]
    public async Task<IActionResult> List()
        => Ok(await Watches.ListAsync());

    /// <summary>
    ///     Lists everything no watch will report.
    /// </summary>
    /// <returns></returns>
    /// <response code="200">The request completed successfully.</response>
    [HttpGet("/api/v{version:apiVersion}/watches/ignores")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(List<Ignore>), 200)]
    public async Task<IActionResult> ListIgnores()
        => Ok(await Watches.ListIgnoresAsync());

    /// <summary>
    ///     Stops every watch reporting a filename, or a peer.
    /// </summary>
    /// <remarks>
    ///     A filename rather than a path: the same release sits on dozens of peers under dozens of paths, and each
    ///     copy is new to a watch. Matching the name is what stops one rejected release arriving forty times.
    /// </remarks>
    /// <param name="request">What to ignore.</param>
    /// <returns></returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="400">The specified value is not valid.</response>
    [HttpPost("/api/v{version:apiVersion}/watches/ignores")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(Ignore), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> AddIgnore([FromBody] IgnoreRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Value))
        {
            return BadRequest("A value to ignore is required");
        }

        // stored as the name alone even when a whole path is sent, so that what was ignored is what is matched --
        // an ignore that reads back as a path nobody will ever match again is worse than an error
        var value = request.Kind == IgnoreKind.Name
            ? IgnoreSet.NameOf(request.Value.Trim())
            : request.Value.Trim();

        return Ok(await Watches.AddIgnoreAsync(new Ignore
        {
            Kind = request.Kind,
            Value = value,
            Note = request.Note,
        }));
    }

    /// <summary>
    ///     Stops ignoring something.
    /// </summary>
    /// <param name="ignoreId">The id of the ignore.</param>
    /// <returns></returns>
    /// <response code="204">The request completed successfully.</response>
    [HttpDelete("/api/v{version:apiVersion}/watches/ignores/{ignoreId}")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteIgnore([FromRoute] Guid ignoreId)
    {
        await Watches.DeleteIgnoreAsync(ignoreId);
        return NoContent();
    }

    /// <summary>
    ///     Gets the watch on the specified search.
    /// </summary>
    /// <param name="id">The id of the search.</param>
    /// <returns></returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="404">There is no watch on the specified search.</response>
    [HttpGet]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(Watch), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get([FromRoute] Guid id)
    {
        var watch = await Watches.FindAsync(id);

        return watch is null ? NotFound() : Ok(watch);
    }

    /// <summary>
    ///     Creates or replaces the watch on the specified search.
    /// </summary>
    /// <param name="id">The id of the search.</param>
    /// <param name="request">The watch to save.</param>
    /// <returns></returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="400">The specified watch is not valid.</response>
    /// <response code="404">The specified search does not exist.</response>
    [HttpPut]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(Watch), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Put([FromRoute] Guid id, [FromBody] WatchRequest request)
    {
        var search = await Searches.FindAsync(s => s.Id == id);

        if (search is null)
        {
            return NotFound();
        }

        var options = OptionsSnapshot.Value.Searches.Watches;
        var existing = await Watches.FindAsync(id);

        var timeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? options.TimeZone : request.TimeZone;

        if (!Recurrence.IsKnownTimeZone(timeZone))
        {
            return BadRequest($"'{timeZone}' is not a time zone this system knows");
        }

        if (!Recurrence.TryValidate(request.Rrule, out var error))
        {
            return BadRequest(error);
        }

        var watch = new Watch
        {
            SearchId = id,
            Enabled = request.Enabled,
            Rrule = request.Rrule,
            TimeZone = timeZone,
            Anchor = existing?.Anchor ?? DateTime.UtcNow,
            NotifyEmail = request.NotifyEmail,
            IncludeLocked = request.IncludeLocked,
            RequireFreeSlot = request.RequireFreeSlot,
            Filter = request.Filter,
            AutoDownload = request.AutoDownload,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
        };

        // a rule is refused for searching too often when it is *set*, rather than quietly slowed down later: an
        // operator who asked for every ten minutes should be told no, not left wondering why it runs hourly
        var shortest = Recurrence.ShortestInterval(watch.Rrule, watch.Anchor, watch.TimeZone);

        if (shortest.HasValue && shortest.Value < TimeSpan.FromMinutes(options.MinimumInterval))
        {
            return BadRequest($"That recurrence would search every {shortest.Value.TotalMinutes:0} minutes; the shortest allowed is {options.MinimumInterval}");
        }

        if (existing is null && request.Enabled)
        {
            var enabled = (await Watches.ListAsync()).Count(w => w.Enabled);

            if (enabled >= options.Limit)
            {
                return BadRequest($"There are already {enabled} enabled watches, and the limit is {options.Limit}");
            }
        }

        var saved = await Watches.UpsertAsync(watch);

        // seeding reads what this search *currently* holds, so it only means anything at creation -- a watch that has
        // been running has a memory already, and re-seeding it would silence files it has not reported
        if (existing is null && request.SeedFromCurrentResults)
        {
            var seeded = await Watches.SeedAsync(saved);
            return Ok(new { watch = saved, seeded });
        }

        return Ok(new { watch = saved, seeded = 0 });
    }

    /// <summary>
    ///     Deletes the watch on the specified search, and everything it remembered.
    /// </summary>
    /// <param name="id">The id of the search.</param>
    /// <returns></returns>
    /// <response code="204">The request completed successfully.</response>
    [HttpDelete]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        await Watches.DeleteAsync(id);
        return NoContent();
    }

    /// <summary>
    ///     Runs the watch on the specified search now.
    /// </summary>
    /// <param name="id">The id of the search.</param>
    /// <returns></returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="404">There is no watch on the specified search.</response>
    [HttpPost("run")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(WatchRun), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Run([FromRoute] Guid id)
    {
        var watch = await Watches.FindAsync(id);

        if (watch is null)
        {
            return NotFound();
        }

        return Ok(await Watches.RunAsync(watch));
    }

    /// <summary>
    ///     Lists the runs of the watch on the specified search.
    /// </summary>
    /// <param name="id">The id of the search.</param>
    /// <returns></returns>
    /// <response code="200">The request completed successfully.</response>
    [HttpGet("runs")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(List<WatchRun>), 200)]
    public async Task<IActionResult> Runs([FromRoute] Guid id)
        => Ok(await Watches.ListRunsAsync(id));

    /// <summary>
    ///     Lists the notifications the watch on the specified search has sent, or tried to.
    /// </summary>
    /// <param name="id">The id of the search.</param>
    /// <returns></returns>
    /// <response code="200">The request completed successfully.</response>
    [HttpGet("notifications")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(List<WatchNotification>), 200)]
    public async Task<IActionResult> Notifications([FromRoute] Guid id)
        => Ok(await Watches.ListNotificationsAsync(id));
}
