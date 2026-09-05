// <copyright file="RemovalResult.cs" company="JP Dillingham">
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
    /// <summary>
    ///     The outcome of removing a download: what happened to the record, and what happened to the file.
    /// </summary>
    /// <remarks>
    ///     Returned rather than folded into the status code because "the file is gone", "there was no
    ///     file" and "the file could not be deleted" are three different answers, and a caller that has
    ///     just asked for something irreversible should be told which one it got.
    ///
    ///     Two independent facts, deliberately. The removal and the deletion can each succeed or fail on
    ///     their own, and a caller that derived one from the other would be guessing -- which is exactly
    ///     what reporting "removed, but the file is still there" on the strength of a precondition would
    ///     be.
    /// </remarks>
    public record RemovalResult
    {
        /// <summary>
        ///     Gets a value indicating whether the record of the download was removed.
        /// </summary>
        /// <remarks>
        ///     Reported by <c>Remove()</c> itself rather than assumed from the state read a moment
        ///     earlier: that read is a snapshot, cancellation can change the state after it, and the
        ///     filter <c>Remove()</c> applies is its own.
        /// </remarks>
        public bool Removed { get; init; }

        /// <summary>
        ///     Gets a value indicating whether a file was deleted.
        /// </summary>
        public bool Deleted { get; init; }

        /// <summary>
        ///     Gets the fully qualified path of the file, or null if no local file was recorded for the download.
        /// </summary>
        public string Filename { get; init; }

        /// <summary>
        ///     Gets the reason the file could not be deleted, or null if there was nothing to delete or the
        ///     deletion succeeded.
        /// </summary>
        /// <remarks>
        ///     Set only for a deletion that was attempted and failed. Whether the record went is
        ///     <see cref="Removed"/>'s business, not this one's.
        /// </remarks>
        public string Error { get; init; }
    }
}
