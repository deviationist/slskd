// <copyright file="OneTaggerController.cs" company="JP Dillingham">
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

namespace slskd.Integrations.OneTagger.API;

using System;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using slskd.Files;
using slskd.Player;
using slskd.Transfers;

/// <summary>
///     The address that opens a download in OneTagger.
/// </summary>
/// <param name="Url">The address.</param>
public record OneTaggerLinkResponse(string Url);

/// <summary>
///     OneTagger: a finished download opened in its Tag Editor.
/// </summary>
/// <remarks>
///     The download is named by id and its file is the one it recorded (see <see cref="PlayableFile"/>), so the browser
///     never names a path; it is handed one in OneTagger's terms, to open.
/// </remarks>
[Route("api/v{version:apiVersion}/onetagger")]
[ApiVersion("0")]
[ApiController]
[Produces("application/json")]
public class OneTaggerController : ControllerBase
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="OneTaggerController"/> class.
    /// </summary>
    /// <param name="transferService">The transfer service.</param>
    /// <param name="fileService">The file service.</param>
    /// <param name="optionsMonitor">The options monitor.</param>
    public OneTaggerController(TransferService transferService, FileService fileService, IOptionsMonitor<slskd.Options> optionsMonitor)
    {
        Transfers = transferService;
        Files = fileService;
        OptionsMonitor = optionsMonitor;
    }

    private TransferService Transfers { get; }
    private FileService Files { get; }
    private IOptionsMonitor<slskd.Options> OptionsMonitor { get; }

    /// <summary>
    ///     The address that opens a finished download in OneTagger's Tag Editor.
    /// </summary>
    /// <param name="id">The download's id.</param>
    /// <returns>The address.</returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="404">OneTagger is off, or there is no such download, or its file is gone.</response>
    /// <response code="409">The download has not finished.</response>
    [HttpGet("downloads/{id}")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(OneTaggerLinkResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public IActionResult GetLink([FromRoute] string id)
    {
        var options = OptionsMonitor.CurrentValue;

        if (Program.IsRelayAgent || !options.Integrations.OneTagger.Enabled)
        {
            return NotFound();
        }

        if (!Guid.TryParse(id, out var guid))
        {
            return BadRequest("Invalid id");
        }

        var download = Transfers.Downloads.Find(t => t.Id == guid);
        var file = PlayableFile.Resolve(download, options.Directories.Downloads, Files, out var reason);

        if (file is null)
        {
            return reason == Unplayable.NotYet ? Conflict("The download has not finished yet") : NotFound();
        }

        var url = OneTaggerLink.For(options.Integrations.OneTagger, options.Directories.Downloads, file.FullName);

        return url is null ? NotFound() : Ok(new OneTaggerLinkResponse(url));
    }
}
