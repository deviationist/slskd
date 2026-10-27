// <copyright file="PlayerController.cs" company="JP Dillingham">
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

namespace slskd.Player.API;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Serilog;
using slskd.Files;
using slskd.Transfers;

/// <summary>
///     The player: a finished download's waveform, its audio, its cover and tags, and what an analysis provider found.
/// </summary>
/// <remarks>
///     <para>
///         Every route names a download by its id, and the file is the one the download recorded (see
///         <see cref="PlayableFile"/>), so the browser never gets to name a path.
///     </para>
///     <para>
///         The audio and the cover are fetched by an <c>&lt;audio&gt;</c> and an <c>&lt;img&gt;</c>, which cannot send
///         an Authorization header, so those two are admitted by a <see cref="StreamTickets">stream ticket</see> in the
///         query string instead; the ticket is issued to an authenticated caller and admits those two routes only.
///     </para>
///     <para>
///         A 404 means the file has gone for good and the caller should stop asking. A 409 means the download has not
///         finished yet, and asking again shortly is right.
///     </para>
///     <para>
///         The audio and art routes declare no <c>[Produces]</c> of their own: a file result is not negotiated, but an
///         error is, and with only audio types to choose from a 403 or a 400 came back as 406.
///     </para>
/// </remarks>
[Route("api/v{version:apiVersion}/player")]
[ApiVersion("0")]
[Produces("application/json")]
public class PlayerController : ControllerBase
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PlayerController"/> class.
    /// </summary>
    /// <param name="transferService">The transfer service.</param>
    /// <param name="fileService">The file service.</param>
    /// <param name="playerService">The player service.</param>
    /// <param name="analysisProvider">The analysis provider client.</param>
    /// <param name="tickets">The stream tickets.</param>
    /// <param name="optionsMonitor">The options monitor.</param>
    public PlayerController(
        TransferService transferService,
        FileService fileService,
        PlayerService playerService,
        AnalysisProvider analysisProvider,
        StreamTickets tickets,
        IOptionsMonitor<Options> optionsMonitor)
    {
        Transfers = transferService;
        Files = fileService;
        Player = playerService;
        Analysis = analysisProvider;
        Tickets = tickets;
        OptionsMonitor = optionsMonitor;
    }

    private TransferService Transfers { get; }
    private FileService Files { get; }
    private PlayerService Player { get; }
    private AnalysisProvider Analysis { get; }
    private StreamTickets Tickets { get; }
    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<PlayerController>();

    /// <summary>
    ///     Issues a stream ticket for the audio and art routes.
    /// </summary>
    /// <returns>The ticket.</returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="404">The player is off.</response>
    [HttpPost("ticket")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(StreamTicketResponse), 200)]
    public IActionResult CreateTicket()
    {
        if (Program.IsRelayAgent || !OptionsMonitor.CurrentValue.Player.Enabled)
        {
            return NotFound();
        }

        return Ok(new StreamTicketResponse { Ticket = Tickets.Issue(), Analysis = Analysis.Configured });
    }

    /// <summary>
    ///     The audio, as a file a browser can play, honouring byte ranges.
    /// </summary>
    /// <param name="id">The download's id.</param>
    /// <param name="ticket">A stream ticket.</param>
    /// <param name="codec"><c>opus</c> (the default) or <c>aac</c>, which WebKit needs for its meter to read anything.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The audio.</returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="206">A range of the audio.</response>
    /// <response code="400">The codec is not one this serves.</response>
    /// <response code="403">The ticket was not issued here, or has expired.</response>
    /// <response code="502">The transcode failed.</response>
    [HttpGet("{id}/audio")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAudioAsync([FromRoute] string id, [FromQuery] string ticket, [FromQuery] string codec, CancellationToken cancellationToken)
    {
        var chosen = Codecs.Find(codec);

        if (chosen is null)
        {
            // a name outside the table becomes neither an ffmpeg argument list nor a file name
            return BadRequest($"Unknown codec '{codec}'");
        }

        var (guid, file, refusal) = Resolve(id, ticket, requireTicket: true);

        if (refusal is not null)
        {
            return refusal;
        }

        try
        {
            var (path, contentType) = await Player.AudioAsync(file, chosen, cancellationToken);
            var info = new FileInfo(path);

            Response.Headers.CacheControl = "private, max-age=300";

            return PhysicalFile(
                path,
                contentType,
                lastModified: info.LastWriteTimeUtc,
                entityTag: new EntityTagHeaderValue("\"" + PlayerService.Identity(file) + "\""),
                enableRangeProcessing: true);
        }
        catch (MediaToolException ex)
        {
            Log.Warning("Cannot make download {Id} playable: {Message}", guid, ex.Message);
            return StatusCode(502, ex.Message);
        }
    }

    /// <summary>
    ///     The waveform: per-bucket peaks and the decoded length they span.
    /// </summary>
    /// <param name="id">The download's id.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The waveform.</returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="502">The decode failed.</response>
    [HttpGet("{id}/peaks")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(WaveformPeaks), 200)]
    public async Task<IActionResult> GetPeaksAsync([FromRoute] string id, CancellationToken cancellationToken)
    {
        var (guid, file, refusal) = Resolve(id);

        if (refusal is not null)
        {
            return refusal;
        }

        try
        {
            return Ok(await Player.PeaksAsync(file, cancellationToken));
        }
        catch (MediaToolException ex)
        {
            Log.Warning("Cannot draw the waveform of download {Id}: {Message}", guid, ex.Message);
            return StatusCode(502, ex.Message);
        }
    }

    /// <summary>
    ///     The title, artist and album the file carries.
    /// </summary>
    /// <param name="id">The download's id.</param>
    /// <returns>The tags; every field null when the file carries none.</returns>
    /// <response code="200">The request completed successfully.</response>
    [HttpGet("{id}/tags")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(TrackTags), 200)]
    public IActionResult GetTags([FromRoute] string id)
    {
        var (_, file, refusal) = Resolve(id);

        if (refusal is not null)
        {
            return refusal;
        }

        return Ok(EmbeddedTags.Read(file.FullName) ?? new TrackTags(null, null, null));
    }

    /// <summary>
    ///     The cover embedded in the file itself.
    /// </summary>
    /// <param name="id">The download's id.</param>
    /// <param name="ticket">A stream ticket.</param>
    /// <returns>The image.</returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="304">The cover has not changed.</response>
    /// <response code="404">The file carries no cover. Not an error: the player draws an empty frame.</response>
    [HttpGet("{id}/art")]
    [AllowAnonymous]
    public IActionResult GetArt([FromRoute] string id, [FromQuery] string ticket)
    {
        var (_, file, refusal) = Resolve(id, ticket, requireTicket: true);

        if (refusal is not null)
        {
            return refusal;
        }

        var cover = EmbeddedTags.ReadCover(file.FullName);

        if (cover is null)
        {
            return NotFound();
        }

        if (Request.Headers.IfNoneMatch.ToString() == cover.ETag)
        {
            return StatusCode(304);
        }

        Response.Headers.ETag = cover.ETag;
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(cover.Data, cover.MimeType);
    }

    /// <summary>
    ///     What the configured analysis provider found, if one is configured.
    /// </summary>
    /// <param name="id">The download's id.</param>
    /// <param name="wait">False to be told <c>pending</c> at once rather than wait for the provider.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The answer: its state, and the provider's document when it is ready.</returns>
    /// <response code="200">The request completed successfully; see <c>state</c>.</response>
    [HttpGet("{id}/analysis")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(AnalysisResponse), 200)]
    public async Task<IActionResult> GetAnalysisAsync([FromRoute] string id, [FromQuery] bool wait = true, CancellationToken cancellationToken = default)
    {
        if (!Analysis.Configured)
        {
            return Ok(AnalysisResponse.From(new AnalysisAnswer(AnalysisState.Off)));
        }

        var (_, file, refusal) = Resolve(id);

        if (refusal is not null)
        {
            return refusal;
        }

        return Ok(AnalysisResponse.From(await Analysis.GetAsync(file, wait, cancellationToken)));
    }

    private (Guid Id, FileInfo File, IActionResult Refusal) Resolve(string id, string ticket = null, bool requireTicket = false)
    {
        if (Program.IsRelayAgent || !OptionsMonitor.CurrentValue.Player.Enabled)
        {
            return (default, null, NotFound());
        }

        if (!Guid.TryParse(id, out var guid))
        {
            return (default, null, BadRequest("Invalid id"));
        }

        // a route reached with a ticket is admitted by that ticket alone; it is the only credential an audio element
        // can carry. checked before the download is looked up, so a bad ticket learns nothing about which ids exist
        if (requireTicket && !Tickets.Admits(ticket))
        {
            return (guid, null, StatusCode(403, "The ticket is not one this server issued, or it has expired"));
        }

        var download = Transfers.Downloads.Find(t => t.Id == guid);
        var file = PlayableFile.Resolve(download, OptionsMonitor.CurrentValue.Directories.Downloads, Files, out var reason);

        if (file is not null)
        {
            return (guid, file, null);
        }

        IActionResult refusal = reason switch
        {
            Unplayable.NotYet => Conflict("The download has not finished yet"),
            Unplayable.Refused => StatusCode(403, "Not a file the player will open"),
            _ => NotFound(),
        };

        if (reason == Unplayable.Refused)
        {
            Log.Warning("Refused to play download {Id}: its recorded file is outside the downloads directory or is not audio", guid);
        }

        return (guid, null, refusal);
    }
}
