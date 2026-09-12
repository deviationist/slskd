// <copyright file="Ignore.cs" company="JP Dillingham">
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
using System.Linq;

/// <summary>
///     What an ignore matches on.
/// </summary>
public enum IgnoreKind
{
    /// <summary>
    ///     A filename, from any peer, in any watch.
    /// </summary>
    /// <remarks>
    ///     The one that earns its keep. The same rip sits on dozens of peers, and each copy is a different peer and a
    ///     different path -- so each is genuinely new to a watch, and each gets reported. Matching the name is what
    ///     stops one rejected release arriving forty times.
    /// </remarks>
    Name = 0,

    /// <summary>
    ///     Everything from one peer.
    /// </summary>
    User = 1,
}

/// <summary>
///     A file, or a peer, that no watch should ever report.
/// </summary>
/// <remarks>
///     <para>
///         Global, where a watch's own memory is not. There is deliberately no third kind matching a path from a
///         particular peer: a watch already reports any given file from any given peer exactly once, so an ignore of
///         that shape would only ever matter to a <em>second</em> watch over the same ground.
///     </para>
///     <para>
///         Applied before a match is recorded, not after -- so an ignored file is never marked as reported, and
///         removing the ignore later reports it as new rather than never.
///     </para>
/// </remarks>
public record Ignore
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    ///     Gets what this matches on.
    /// </summary>
    public IgnoreKind Kind { get; init; }

    /// <summary>
    ///     Gets the value to match: a filename, or a username.
    /// </summary>
    public string Value { get; init; }

    /// <summary>
    ///     Gets the instant this was added.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    ///     Gets a note about why, if one was given.
    /// </summary>
    public string Note { get; init; }
}

/// <summary>
///     The ignores, as a set that can be asked about a file.
/// </summary>
/// <remarks>
///     Built once per run and asked per match, rather than a query per match. A watch on a broad search produces
///     thousands of matches, and a round trip for each is the difference between a run and an outage.
/// </remarks>
public class IgnoreSet
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="IgnoreSet"/> class.
    /// </summary>
    /// <param name="ignores">The ignores to build from.</param>
    public IgnoreSet(IEnumerable<Ignore> ignores)
    {
        foreach (var ignore in ignores ?? [])
        {
            if (string.IsNullOrWhiteSpace(ignore.Value))
            {
                continue;
            }

            if (ignore.Kind == IgnoreKind.User)
            {
                Users.Add(ignore.Value);
            }
            else
            {
                Names.Add(ignore.Value);
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether anything is ignored at all.
    /// </summary>
    public bool IsEmpty => Names.Count == 0 && Users.Count == 0;

    // peers and filenames are compared without regard to case: the network is full of the same release spelled
    // several ways, and an ignore that missed on capitalisation would look like it had simply not worked
    private HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> Users { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Returns the filename part of a remote path.
    /// </summary>
    /// <remarks>
    ///     Peers send Windows-style paths, and a few send unix ones. Both separators are cut, because a name that
    ///     matched only one of them would ignore a file from some peers and not others.
    /// </remarks>
    /// <param name="path">The remote path.</param>
    /// <returns>The filename.</returns>
    public static string NameOf(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var cut = path.LastIndexOfAny(['\\', '/']);

        return cut < 0 ? path : path[(cut + 1)..];
    }

    /// <summary>
    ///     Returns a value indicating whether a file from the specified peer should never be reported.
    /// </summary>
    /// <param name="username">The peer.</param>
    /// <param name="filename">The remote path.</param>
    /// <returns>Whether it is ignored.</returns>
    public bool Ignores(string username, string filename)
        => Users.Contains(username ?? string.Empty) || Names.Contains(NameOf(filename) ?? string.Empty);
}
