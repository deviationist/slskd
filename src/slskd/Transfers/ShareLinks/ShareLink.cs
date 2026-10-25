// <copyright file="ShareLink.cs" company="JP Dillingham">
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

namespace slskd.Transfers.ShareLinks;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

/// <summary>
///     A link that lets someone without an account download copies of some finished downloads, until it expires.
/// </summary>
/// <remarks>
///     <para>
///         The link owns its files. They are copies, made when the link is created, in a folder of the link's own, so
///         the link and what it serves have one lifetime: removing the download they came from -- or deleting its
///         file -- does not break the link, and expiring the link removes exactly what it put there and nothing else.
///     </para>
///     <para>
///         The token is kept as it is rather than hashed, because the list of links offers each one to be copied
///         again. A hash would protect it from someone who can read this database, and that person can already read
///         the files it guards.
///     </para>
/// </remarks>
public class ShareLink
{
    /// <summary>
    ///     Gets or sets the id, which also names the link's folder.
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    ///     Gets or sets the secret the link's address carries.
    /// </summary>
    [Required]
    [JsonIgnore]
    public string Token { get; set; }

    /// <summary>
    ///     Gets or sets what the link is called, on its page and in the list.
    /// </summary>
    [Required]
    public string Title { get; set; }

    /// <summary>
    ///     Gets or sets when the link was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    ///     Gets or sets when the link stops working and its files are deleted.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    ///     Gets or sets the addresses the link was mailed to, one per line. Recorded for the list, not used again.
    /// </summary>
    public string Recipients { get; set; }

    /// <summary>
    ///     Gets or sets how many times something was downloaded through the link -- a file or the archive, each once.
    /// </summary>
    public int DownloadCount { get; set; }

    /// <summary>
    ///     Gets or sets when something was last downloaded through the link.
    /// </summary>
    public DateTime? LastDownloadedAt { get; set; }

    /// <summary>
    ///     Gets or sets the files the link serves.
    /// </summary>
    public List<ShareLinkFile> Files { get; set; } = [];
}

/// <summary>
///     One file a <see cref="ShareLink"/> serves: a copy of a finished download, in the link's folder.
/// </summary>
public class ShareLinkFile
{
    /// <summary>
    ///     Gets or sets the id.
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    ///     Gets or sets the link this belongs to.
    /// </summary>
    [JsonIgnore]
    public Guid ShareLinkId { get; set; }

    /// <summary>
    ///     Gets or sets where in the link's list this file is, from 0. Also how the link's page asks for it.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    ///     Gets or sets the name the file is offered under: its folder and filename, as an archive entry would be.
    /// </summary>
    [Required]
    public string Name { get; set; }

    /// <summary>
    ///     Gets or sets where the copy is on disk.
    /// </summary>
    [Required]
    [JsonIgnore]
    public string Path { get; set; }

    /// <summary>
    ///     Gets or sets the size of the copy, in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    ///     Gets or sets the download the copy was made from, which may since have been removed.
    /// </summary>
    public Guid? TransferId { get; set; }

    /// <summary>
    ///     Gets or sets the peer the download came from.
    /// </summary>
    public string Username { get; set; }
}
