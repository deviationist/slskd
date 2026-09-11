// <copyright file="ArchiveAvailabilityResponse.cs" company="JP Dillingham">
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

namespace slskd.Transfers.API
{
    using System.Collections.Generic;

    /// <summary>
    ///     Which of a set of downloads still have a file that can be archived, and which do not.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Asked before an archive is started, because an archive is streamed: once the first byte is written the
    ///         response is committed, and there is no way to go back and tell the operator that a third of what they
    ///         selected was not there. Better to ask first and let them decide.
    ///     </para>
    ///     <para>
    ///         A file going missing between this answer and the archive being written is still possible and is handled
    ///         separately, by skipping it and listing it inside the archive. This reduces that case; it cannot remove
    ///         it.
    ///     </para>
    /// </remarks>
    public record ArchiveAvailabilityResponse
    {
        /// <summary>
        ///     Gets the downloads whose file was present and readable.
        /// </summary>
        public IEnumerable<ArchiveAvailabilityEntry> Available { get; init; }

        /// <summary>
        ///     Gets the downloads that have no file to archive.
        /// </summary>
        /// <remarks>
        ///     One answer for several causes, deliberately: the transfer is unknown, it did not succeed, no path was
        ///     recorded for it, the file has since been moved or deleted, or the recorded path is no longer somewhere
        ///     this application may read. To the operator deciding whether to continue, these are the same fact.
        /// </remarks>
        public IEnumerable<ArchiveAvailabilityEntry> Missing { get; init; }
    }

    /// <summary>
    ///     One download in an availability answer.
    /// </summary>
    public record ArchiveAvailabilityEntry
    {
        /// <summary>
        ///     Gets the id of the download.
        /// </summary>
        public string Id { get; init; }

        /// <summary>
        ///     Gets the name of the file, for display.
        /// </summary>
        /// <remarks>
        ///     The remote filename's last segment -- what the operator sees in the transfer list. Not the local path,
        ///     which is where the file is on the server and is nobody's business in a confirmation dialog.
        /// </remarks>
        public string Filename { get; init; }
    }
}
