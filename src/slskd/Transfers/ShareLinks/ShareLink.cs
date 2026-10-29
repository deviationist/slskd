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
///     Copies of some finished downloads that people without an account can download, through links, until it expires.
/// </summary>
/// <remarks>
///     <para>
///         The share owns its files. They are copies, made when the share is created, in a folder of its own, so the
///         share and what it serves have one lifetime: removing the download they came from -- or deleting its file --
///         does not break it, and expiring the share removes exactly what it put there and nothing else.
///     </para>
///     <para>
///         Who can download is a matter of <see cref="Accesses"/>, each a link of its own: one general link to hand out
///         however you like, and one per address it was mailed to. A link cannot be taken back from someone who has
///         it, so a token per recipient is what makes it possible to cut off one of them without the others -- and to
///         see which of them has downloaded anything.
///     </para>
/// </remarks>
public class ShareLink
{
    /// <summary>
    ///     Gets or sets the id, which also names the share's folder.
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    ///     Gets or sets what the share is called, on its page, in its mail and in the list.
    /// </summary>
    [Required]
    public string Title { get; set; }

    /// <summary>
    ///     Gets or sets when the share was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    ///     Gets or sets when every link to the share stops working and its files are deleted.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    ///     Gets or sets the files the share serves.
    /// </summary>
    public List<ShareLinkFile> Files { get; set; } = [];

    /// <summary>
    ///     Gets or sets the links to the share, revoked ones included: they are the record of who was given it.
    /// </summary>
    public List<ShareLinkAccess> Accesses { get; set; } = [];
}

/// <summary>
///     One link to a <see cref="ShareLink"/>: a token, and who it was given to.
/// </summary>
/// <remarks>
///     The token is kept as it is rather than hashed, because the list offers each link to be copied again. A hash
///     would protect it from someone who can read this database, and that person can already read the files it guards.
/// </remarks>
public class ShareLinkAccess
{
    /// <summary>
    ///     Gets or sets the id.
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    ///     Gets or sets the share this opens.
    /// </summary>
    [JsonIgnore]
    public Guid ShareLinkId { get; set; }

    /// <summary>
    ///     Gets or sets the secret the link's address carries.
    /// </summary>
    [Required]
    [JsonIgnore]
    public string Token { get; set; }

    /// <summary>
    ///     Gets or sets the address this link was made for, or null for the general link.
    /// </summary>
    public string Recipient { get; set; }

    /// <summary>
    ///     Gets or sets when the link was made.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    ///     Gets or sets when the link was last mailed to its recipient successfully.
    /// </summary>
    public DateTime? SentAt { get; set; }

    /// <summary>
    ///     Gets or sets why the last attempt to mail it failed, if it did.
    /// </summary>
    public string MailError { get; set; }

    /// <summary>
    ///     Gets or sets when the link was revoked. A revoked link opens nothing, and is kept as the record that it
    ///     existed.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    ///     Gets or sets how many times something was downloaded through this link -- a file or the archive, each once.
    /// </summary>
    public int DownloadCount { get; set; }

    /// <summary>
    ///     Gets or sets when something was last downloaded through this link.
    /// </summary>
    public DateTime? LastDownloadedAt { get; set; }

    /// <summary>
    ///     Gets a value indicating whether this is the general link rather than one made for an address.
    /// </summary>
    [JsonIgnore]
    public bool IsGeneral => Recipient is null;
}

/// <summary>
///     One file a <see cref="ShareLink"/> serves: a copy of a finished download, in the share's folder.
/// </summary>
public class ShareLinkFile
{
    /// <summary>
    ///     Gets or sets the id.
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    ///     Gets or sets the share this belongs to.
    /// </summary>
    [JsonIgnore]
    public Guid ShareLinkId { get; set; }

    /// <summary>
    ///     Gets or sets where in the share's list this file is, from 0. Also how the share's page asks for it.
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

/// <summary>
///     An address a share has been mailed to, remembered so that the next one can be offered it as it is typed.
/// </summary>
/// <remarks>
///     Kept apart from <see cref="ShareLinkAccess"/> because those go with their share when it expires, and the point
///     of this is to remember who was shared with long after what was shared is gone. Only an address a mail actually
///     went to is kept: a typo that bounced at the relay is not worth suggesting again.
/// </remarks>
public class ShareLinkRecipient
{
    /// <summary>
    ///     Gets or sets the address in lower case, which is what makes two spellings of it one address -- as the
    ///     server compares recipients everywhere else.
    /// </summary>
    [Key]
    [JsonIgnore]
    public string Key { get; set; }

    /// <summary>
    ///     Gets or sets the address as it was last typed.
    /// </summary>
    [Required]
    public string Address { get; set; }

    /// <summary>
    ///     Gets or sets when a share was first mailed to it.
    /// </summary>
    public DateTime FirstSentAt { get; set; }

    /// <summary>
    ///     Gets or sets when a share was last mailed to it.
    /// </summary>
    public DateTime LastSentAt { get; set; }

    /// <summary>
    ///     Gets or sets how many mails have gone to it.
    /// </summary>
    public int SendCount { get; set; }
}
