// <copyright file="BatchInheritance.cs" company="JP Dillingham">
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

namespace slskd.Transfers.Downloads;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
///     Which batch a newly enqueued download belongs to.
/// </summary>
public static class BatchInheritance
{
    /// <summary>
    ///     Resolves the batch for an enqueue of <paramref name="filename"/> from one peer.
    /// </summary>
    /// <remarks>
    ///     An enqueue that names no batch inherits the one held by the record it supersedes. A retried
    ///     download is the same download -- it fulfils the search it was started from just as the first
    ///     attempt did -- and the rows most worth tracing to their origin are the ones that had to be
    ///     retried. Without this, retrying a download is how it forgets where it came from.
    ///
    ///     A batch given by the caller always wins: that is the caller saying which enqueue this belongs
    ///     to, and nothing here knows better.
    /// </remarks>
    /// <param name="supplied">The batch the caller named, if it named one.</param>
    /// <param name="superseded">This peer's existing records, which the new one will supersede.</param>
    /// <param name="filename">The remote filename being enqueued.</param>
    /// <returns>The batch id to record against the new transfer, or null where there is none.</returns>
    public static Guid? Resolve(Guid? supplied, IEnumerable<Transfer> superseded, string filename)
    {
        if (supplied.HasValue)
        {
            return supplied;
        }

        // the most recent, because a file enqueued more than once has been superseded more than once, and
        // the batch it belonged to last is the one it belongs to now
        return (superseded ?? [])
            .Where(t => t.Filename == filename && t.BatchId.HasValue)
            .OrderByDescending(t => t.RequestedAt)
            .Select(t => t.BatchId)
            .FirstOrDefault();
    }
}
