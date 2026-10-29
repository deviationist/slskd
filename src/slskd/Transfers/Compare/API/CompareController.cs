// <copyright file="CompareController.cs" company="JP Dillingham">
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

namespace slskd.Transfers.Compare.API;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using slskd.Files;
using slskd.Player;

/// <summary>
///     Downloads to compare.
/// </summary>
public class CompareRequest
{
    /// <summary>
    ///     Gets or sets the downloads, by id, in the order the answer's indices follow.
    /// </summary>
    [Required]
    public List<Guid> Ids { get; set; }
}

/// <summary>
///     Whether Compare is offered, for the page to decide whether to draw it.
/// </summary>
/// <param name="Enabled">Whether it is enabled and a provider is configured.</param>
/// <param name="MaximumFiles">The most downloads one comparison may hold.</param>
public record CompareStatusResponse(bool Enabled, int MaximumFiles);

/// <summary>
///     A comparison, as the browser is told it.
/// </summary>
public record CompareResponse
{
    /// <summary>
    ///     Gets the state: <c>off</c>, <c>ready</c>, <c>pending</c>, <c>refused</c> or <c>unavailable</c>.
    /// </summary>
    public string State { get; init; }

    /// <summary>
    ///     Gets the downloads compared, in the order the document's indices follow.
    /// </summary>
    public List<Guid> Ids { get; init; }

    /// <summary>
    ///     Gets the provider's document, when ready. Its shape is docs/transfers-compare-provider.schema.json.
    /// </summary>
    public JsonElement? Compare { get; init; }

    /// <summary>
    ///     Gets the seconds to wait before asking again, when pending.
    /// </summary>
    public int? RetryAfter { get; init; }

    /// <summary>
    ///     Gets why there is nothing to show, when there is not.
    /// </summary>
    public string Reason { get; init; }

    /// <summary>
    ///     The response for an answer.
    /// </summary>
    /// <param name="answer">The answer.</param>
    /// <param name="ids">The downloads compared.</param>
    /// <returns>The response.</returns>
    public static CompareResponse From(CompareAnswer answer, List<Guid> ids) => new()
    {
        State = answer.State.ToString().ToLowerInvariant(),
        Ids = ids,
        Compare = answer.Document,
        RetryAfter = answer.RetryAfter,
        Reason = answer.Reason,
    };
}

/// <summary>
///     Compare: which of several finished downloads are the same track, as the configured provider judges it.
/// </summary>
/// <remarks>
///     Downloads are named by id, and each file is the one the download recorded (see <see cref="PlayableFile"/>), so
///     the browser never gets to name a path -- the same rule the player's routes keep.
/// </remarks>
[Route("api/v{version:apiVersion}/compare")]
[ApiVersion("0")]
[ApiController]
[Produces("application/json")]
public class CompareController : ControllerBase
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CompareController"/> class.
    /// </summary>
    /// <param name="transferService">The transfer service.</param>
    /// <param name="fileService">The file service.</param>
    /// <param name="compareProvider">The compare provider client.</param>
    /// <param name="optionsMonitor">The options monitor.</param>
    public CompareController(
        TransferService transferService,
        FileService fileService,
        CompareProvider compareProvider,
        IOptionsMonitor<Options> optionsMonitor)
    {
        Transfers = transferService;
        Files = fileService;
        Provider = compareProvider;
        OptionsMonitor = optionsMonitor;
    }

    private TransferService Transfers { get; }
    private FileService Files { get; }
    private CompareProvider Provider { get; }
    private IOptionsMonitor<Options> OptionsMonitor { get; }

    /// <summary>
    ///     Whether Compare is offered.
    /// </summary>
    /// <returns>Whether it is, and how many downloads it takes at once.</returns>
    /// <response code="200">The request completed successfully.</response>
    [HttpGet]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(CompareStatusResponse), 200)]
    public IActionResult GetStatus()
    {
        return Ok(new CompareStatusResponse(!Program.IsRelayAgent && Provider.Enabled, CompareProvider.MaximumFiles));
    }

    /// <summary>
    ///     Compares finished downloads.
    /// </summary>
    /// <param name="request">The downloads.</param>
    /// <param name="wait">False to be told <c>pending</c> at once rather than wait for the provider.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The answer: its state, and the provider's document when it is ready.</returns>
    /// <response code="200">The request completed successfully; see <c>state</c>.</response>
    /// <response code="400">The downloads cannot be compared as asked; the body says why.</response>
    /// <response code="404">Compare is not available on a relay agent.</response>
    [HttpPost]
    [Authorize(Policy = AuthPolicy.Any)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CompareResponse), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> CompareAsync([FromBody] CompareRequest request, [FromQuery] bool wait = true, CancellationToken cancellationToken = default)
    {
        if (Program.IsRelayAgent)
        {
            return NotFound();
        }

        // a body that did not bind -- not JSON, or ids that are not a list of ids -- arrives as null rather than as a
        // model error, and dereferencing it answered 500. it is the caller's mistake, so it is a 400, in words that
        // say what was wanted rather than what the parser choked on
        if (request?.Ids is null)
        {
            return BadRequest("Send the downloads to compare as { \"ids\": [ ... ] }");
        }

        var ids = request.Ids.Distinct().ToList();

        if (!Provider.Enabled)
        {
            return Ok(CompareResponse.From(new CompareAnswer(CompareState.Off), ids));
        }

        if (ids.Count < 2 || ids.Count > CompareProvider.MaximumFiles)
        {
            return BadRequest($"Compare takes 2 to {CompareProvider.MaximumFiles} downloads");
        }

        var downloads = OptionsMonitor.CurrentValue.Directories.Downloads;
        var files = new List<FileInfo>();
        var problems = new List<string>();

        foreach (var id in ids)
        {
            var download = Transfers.Downloads.Find(t => t.Id == id);
            var file = PlayableFile.Resolve(download, downloads, Files, out var reason);

            if (file is null)
            {
                problems.Add(reason switch
                {
                    Unplayable.NotYet => $"{id} has not finished yet",
                    Unplayable.Refused => $"{id} is not a file Compare will open",
                    Unplayable.NoSuchDownload => $"{id} is not a download",
                    _ => $"{id} no longer has its file",
                });
            }
            else
            {
                files.Add(file);
            }
        }

        if (problems.Count > 0)
        {
            return BadRequest("Only finished downloads that still have their file can be compared: " + string.Join("; ", problems));
        }

        return Ok(CompareResponse.From(await Provider.GetAsync(files, wait, cancellationToken), ids));
    }
}
