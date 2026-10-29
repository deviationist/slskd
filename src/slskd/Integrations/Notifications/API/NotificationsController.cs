// <copyright file="NotificationsController.cs" company="JP Dillingham">
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

namespace slskd.Integrations.Notifications.API;

using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
///     A channel to send a test notification on, or none for every enabled channel.
/// </summary>
public class NotificationTestRequest
{
    /// <summary>
    ///     Gets or sets the channel's name.
    /// </summary>
    public string Channel { get; set; }
}

/// <summary>
///     Notifications: which channels can send, and a test through them.
/// </summary>
[Route("api/v{version:apiVersion}/notifications")]
[ApiVersion("0")]
[ApiController]
[Produces("application/json")]
public class NotificationsController : ControllerBase
{
    public NotificationsController(NotificationService notifications)
    {
        Notifications = notifications;
    }

    private NotificationService Notifications { get; }

    /// <summary>
    ///     Lists the channels that can send.
    /// </summary>
    /// <returns>Their names.</returns>
    /// <response code="200">The request completed successfully.</response>
    [HttpGet("channels")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), 200)]
    public IActionResult GetChannels() => Ok(Notifications.EnabledChannels);

    /// <summary>
    ///     Sends a test notification, so a configuration can be checked without waiting for something to happen.
    /// </summary>
    /// <param name="request">The channel, or none for every enabled channel.</param>
    /// <returns>What became of it on each channel.</returns>
    /// <response code="200">The request completed; see each channel's result.</response>
    [HttpPost("test")]
    [Authorize(Policy = AuthPolicy.Any)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(List<NotificationResult>), 200)]
    public async Task<IActionResult> TestAsync([FromBody] NotificationTestRequest request)
        => Ok(await Notifications.TestAsync(request?.Channel, HttpContext.RequestAborted));
}
