// <copyright file="DownloadTicketService.cs" company="JP Dillingham">
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

namespace slskd.Transfers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

/// <summary>
///     The outcome of an attempt to redeem a download ticket.
/// </summary>
public enum TicketRedemption
{
    /// <summary>
    ///     The ticket was valid, and has now been spent.
    /// </summary>
    Valid,

    /// <summary>
    ///     The ticket was issued, but too long ago.
    /// </summary>
    Expired,

    /// <summary>
    ///     The ticket was never issued, has already been spent, or was presented for something other than what it was
    ///     issued for.
    /// </summary>
    Invalid,
}

/// <summary>
///     Issues and redeems short-lived, single-use tickets authorizing one archive download.
/// </summary>
/// <remarks>
///     <para>
///         These exist because a browser cannot be asked to put an Authorization header on a navigation, and a
///         navigation is what lets it stream a large archive to disk with its own download manager rather than holding
///         it in the memory of a tab. The ticket is the credential for that one navigation.
///     </para>
///     <para>
///         It is bound to the exact username and set of transfer ids it was issued for, so a ticket that leaks cannot
///         be replayed against anything else; it is spent on first redemption; and it is valid for a minute, which is
///         long enough for a click and short enough that a ticket sitting in a log or a browser history is already
///         dead.
///     </para>
///     <para>
///         Nothing here is persisted. A restart invalidates every outstanding ticket, which is the correct behaviour
///         for a credential whose whole life is one click.
///     </para>
/// </remarks>
public class DownloadTicketService
{
    /// <summary>
    ///     The length of time for which an issued ticket may be redeemed, unless another is specified.
    /// </summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     How long a spent or expired ticket is remembered beyond its lifetime.
    /// </summary>
    /// <remarks>
    ///     Only so that a ticket presented late can be reported as *expired* rather than as unrecognised. Without it
    ///     the two are the same answer, and an operator whose download failed cannot tell "you were too slow" from
    ///     "something is wrong".
    /// </remarks>
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    private readonly object syncRoot = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="DownloadTicketService"/> class.
    /// </summary>
    /// <param name="lifetime">
    ///     An optional override for how long an issued ticket may be redeemed, for testing.
    /// </param>
    public DownloadTicketService(TimeSpan? lifetime = null)
    {
        Lifetime = lifetime ?? DefaultLifetime;
        Cache = new MemoryCache(new MemoryCacheOptions());
    }

    private TimeSpan Lifetime { get; }
    private IMemoryCache Cache { get; }

    /// <summary>
    ///     Issues a ticket authorizing a single download of the files produced by the specified transfers.
    /// </summary>
    /// <param name="username">The username the transfers belong to.</param>
    /// <param name="ids">The ids of the transfers.</param>
    /// <returns>The ticket, and the instant at which it stops being redeemable.</returns>
    public (string Ticket, DateTime ExpiresAtUtc) Issue(string username, IEnumerable<Guid> ids)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username, nameof(username));
        ArgumentNullException.ThrowIfNull(ids);

        // 256 bits from a cryptographic source. this is a bearer credential travelling in a URL; it has to be
        // unguessable, and it must not be derived from anything about the request
        var ticket = Base64Url(RandomNumberGenerator.GetBytes(32));
        var expiresAtUtc = DateTime.UtcNow.Add(Lifetime);

        var entry = new Ticket
        {
            Username = username,

            // distinct, but in the order they were given: the order decides the order of the entries in the archive,
            // and an archive whose contents shuffle between two identical requests is a worse thing to debug
            Ids = ids.Distinct().ToArray(),
            ExpiresAtUtc = expiresAtUtc,
        };

        lock (syncRoot)
        {
            Cache.Set(ticket, entry, Lifetime.Add(Grace));
        }

        return (ticket, expiresAtUtc);
    }

    /// <summary>
    ///     Redeems the specified <paramref name="ticket"/>, spending it.
    /// </summary>
    /// <remarks>
    ///     A ticket is spent whatever the outcome, so a redemption cannot be retried and cannot be raced. The ids come
    ///     back from the ticket rather than from the caller: they are what the ticket authorizes, and reading them from
    ///     the request instead would make the binding decorative.
    /// </remarks>
    /// <param name="ticket">The ticket to redeem.</param>
    /// <param name="username">The username the ticket must have been issued for.</param>
    /// <param name="ids">The ids the ticket authorizes, when it is valid.</param>
    /// <returns>The outcome of the redemption.</returns>
    public TicketRedemption Redeem(string ticket, string username, out IReadOnlyList<Guid> ids)
    {
        ids = null;

        if (string.IsNullOrWhiteSpace(ticket))
        {
            return TicketRedemption.Invalid;
        }

        Ticket entry;

        // take and remove under one lock. TryGetValue followed by Remove is not atomic, and two simultaneous
        // redemptions of one ticket would otherwise both succeed -- which is the one thing single-use has to prevent
        lock (syncRoot)
        {
            if (!Cache.TryGetValue(ticket, out entry) || entry is null)
            {
                return TicketRedemption.Invalid;
            }

            Cache.Remove(ticket);
        }

        if (DateTime.UtcNow > entry.ExpiresAtUtc)
        {
            return TicketRedemption.Expired;
        }

        if (!string.Equals(entry.Username, username, StringComparison.Ordinal))
        {
            return TicketRedemption.Invalid;
        }

        ids = entry.Ids;
        return TicketRedemption.Valid;
    }

    /// <summary>
    ///     Encodes the specified <paramref name="bytes"/> in the URL-safe Base64 alphabet.
    /// </summary>
    /// <param name="bytes">The bytes to encode.</param>
    /// <returns>The encoded string.</returns>
    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    ///     What a ticket authorizes.
    /// </summary>
    private sealed class Ticket
    {
        public string Username { get; init; }

        public Guid[] Ids { get; init; }

        public DateTime ExpiresAtUtc { get; init; }
    }
}
