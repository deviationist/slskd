// <copyright file="StreamTickets.cs" company="JP Dillingham">
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
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

/// <summary>
///     Short-lived permission to stream one download's audio and cover.
/// </summary>
/// <remarks>
///     <para>
///         An <c>&lt;audio&gt;</c> or <c>&lt;img&gt;</c> element cannot send an Authorization header, and putting the
///         session token in the URL would leave it in every proxy's access log. A ticket is what goes in the URL
///         instead: it is bound to one transfer, so a leaked one plays that file for a while and reaches nothing else.
///     </para>
///     <para>
///         Reusable within its lifetime, unlike the archive's <see cref="slskd.Transfers.DownloadTicketService"/>:
///         playback is many ranged requests, one per seek. Sliding, because a track paused for half an hour must still
///         seek when it is resumed, and capped, so a ticket is never permanent.
///     </para>
/// </remarks>
public class StreamTickets
{
    /// <summary>
    ///     How long a ticket survives without being used.
    /// </summary>
    public static readonly TimeSpan Idle = TimeSpan.FromHours(1);

    /// <summary>
    ///     How long a ticket survives however much it is used.
    /// </summary>
    public static readonly TimeSpan Cap = TimeSpan.FromHours(12);

    /// <summary>
    ///     Initializes a new instance of the <see cref="StreamTickets"/> class.
    /// </summary>
    /// <param name="idle">An optional override of <see cref="Idle"/>, for testing.</param>
    /// <param name="cap">An optional override of <see cref="Cap"/>, for testing.</param>
    public StreamTickets(TimeSpan? idle = null, TimeSpan? cap = null)
    {
        IdleLifetime = idle ?? Idle;
        CapLifetime = cap ?? Cap;
    }

    private TimeSpan IdleLifetime { get; }
    private TimeSpan CapLifetime { get; }
    private IMemoryCache Cache { get; } = new MemoryCache(new MemoryCacheOptions());

    /// <summary>
    ///     Issues a ticket for one download.
    /// </summary>
    /// <param name="id">The download's id.</param>
    /// <returns>The ticket.</returns>
    public string Issue(Guid id)
    {
        var ticket = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Cache.Set(ticket, id, new MemoryCacheEntryOptions
        {
            SlidingExpiration = IdleLifetime,
            AbsoluteExpirationRelativeToNow = CapLifetime,
        });

        return ticket;
    }

    /// <summary>
    ///     Whether <paramref name="ticket"/> was issued for <paramref name="id"/> and is still live. Using it extends it.
    /// </summary>
    /// <param name="ticket">The ticket.</param>
    /// <param name="id">The download it is presented for.</param>
    /// <returns>A value indicating whether it admits.</returns>
    public bool Admits(string ticket, Guid id)
        => !string.IsNullOrEmpty(ticket) && Cache.TryGetValue(ticket, out Guid issuedFor) && issuedFor == id;
}
