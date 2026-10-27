// <copyright file="PlayerResponses.cs" company="JP Dillingham">
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

namespace slskd.Player.API;

using System.Text.Json;

/// <summary>
///     A stream ticket.
/// </summary>
public record StreamTicketResponse
{
    /// <summary>
    ///     Gets the ticket, for the <c>ticket</c> query parameter of the audio and art routes.
    /// </summary>
    public string Ticket { get; init; }

    /// <summary>
    ///     Gets a value indicating whether an analysis provider is configured, so the page knows whether to draw any
    ///     analysis at all without asking about a file to find out.
    /// </summary>
    public bool Analysis { get; init; }
}

/// <summary>
///     Where a download's analysis stands.
/// </summary>
public record AnalysisResponse
{
    /// <summary>
    ///     Gets the state: <c>off</c>, <c>ready</c>, <c>pending</c>, <c>none</c> or <c>unavailable</c>.
    /// </summary>
    public string State { get; init; }

    /// <summary>
    ///     Gets the provider's document, when ready. Its shape is docs/player-analysis-provider.schema.json.
    /// </summary>
    public JsonElement? Analysis { get; init; }

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
    /// <returns>The response.</returns>
    public static AnalysisResponse From(AnalysisAnswer answer) => new()
    {
        State = answer.State.ToString().ToLowerInvariant(),
        Analysis = answer.Analysis,
        RetryAfter = answer.RetryAfter,
        Reason = answer.Reason,
    };
}
