// <copyright file="AutoDownload.cs" company="JP Dillingham">
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
using System.Linq;

/// <summary>
///     Chooses which of a watch's new files to queue, and from whom.
/// </summary>
public static class AutoDownload
{
    /// <summary>
    ///     Chooses one peer per filename, best first, up to <paramref name="limit"/> files.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One per <em>filename</em>, and that is the whole of it. The same release sits on dozens of peers, so
    ///         queueing every new match would fetch the same track twenty times over -- the same fact that makes a
    ///         global ignore match on the name rather than the path.
    ///     </para>
    ///     <para>
    ///         Best is a free upload slot first, then the faster peer, then the shorter queue. A file is worth having
    ///         from whoever will actually send it, and a free slot is the difference between a download and a place
    ///         in a queue nobody is moving through.
    ///     </para>
    ///     <para>
    ///         The limit is not a nicety. A watch on a broad phrase finds hundreds of new files every run, and a
    ///         feature that quietly queued all of them would fill a disk while its operator read the mail about it.
    ///     </para>
    /// </remarks>
    /// <param name="matches">The new matches.</param>
    /// <param name="limit">The most files to queue.</param>
    /// <returns>The chosen matches.</returns>
    public static List<WatchService.Match> Choose(IEnumerable<WatchService.Match> matches, int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        return (matches ?? [])
            .Where(match => !string.IsNullOrWhiteSpace(match.Filename))
            .GroupBy(match => IgnoreSet.NameOf(match.Filename), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(match => match.HasFreeUploadSlot)
                .ThenByDescending(match => match.UploadSpeed)
                .ThenBy(match => match.QueueLength)
                .First())
            .Take(limit)
            .ToList();
    }
}
