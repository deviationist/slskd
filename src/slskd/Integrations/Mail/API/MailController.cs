// <copyright file="MailController.cs" company="JP Dillingham">
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

namespace slskd.Integrations.Mail.API;

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
///     Mail.
/// </summary>
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("0")]
[ApiController]
[Produces("application/json")]
[Consumes("application/json")]
public class MailController : ControllerBase
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MailController"/> class.
    /// </summary>
    /// <param name="mailService">The mail service.</param>
    /// <param name="optionsSnapshot">The options snapshot.</param>
    public MailController(MailService mailService, IOptionsSnapshot<Options> optionsSnapshot)
    {
        Mail = mailService;
        OptionsSnapshot = optionsSnapshot;
    }

    private MailService Mail { get; }
    private IOptionsSnapshot<Options> OptionsSnapshot { get; }

    /// <summary>
    ///     Sends a test message.
    /// </summary>
    /// <remarks>
    ///     Mail is the one integration whose configuration cannot be checked by reading it: a credential, a host and a
    ///     recipient can all look right and still not deliver. This sends one message so that the failure arrives while
    ///     an operator is looking at it, rather than the first time something has news.
    /// </remarks>
    /// <param name="to">An optional address to send to, in place of the configured default.</param>
    /// <returns></returns>
    /// <response code="200">The message was sent.</response>
    /// <response code="400">Mail is not enabled, or no recipient is known.</response>
    /// <response code="401">Authentication failed.</response>
    /// <response code="502">The configured adapter refused the message.</response>
    [HttpPost("test")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(502)]
    public async Task<IActionResult> SendTestAsync([FromQuery] string to = null)
    {
        if (!Mail.IsEnabled)
        {
            return BadRequest("Mail is not enabled");
        }

        var options = OptionsSnapshot.Value.Integrations.Mail;

        var mail = new Mail
        {
            To = to,
            Subject = "slskd test message",
            Text = $"This is a test message from slskd, sent through the '{options.Adapter}' adapter.\n\n"
                + "If you are reading it, mail is configured correctly.\n",
        };

        try
        {
            var adapter = await Mail.SendAsync(mail, HttpContext.RequestAborted);

            return Ok(new { adapter, to = string.IsNullOrWhiteSpace(to) ? options.To : to });
        }
        catch (MailException ex)
        {
            // the adapter's own words, because they are the only useful thing here: a refused credential, a host that
            // did not answer, or a binary that is not installed each say so, and none of them are worth paraphrasing
            return StatusCode(502, ex.Message);
        }
    }
}
