// <copyright file="EmbeddedTags.cs" company="JP Dillingham">
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
using System.IO;
using System.Linq;
using System.Security.Cryptography;

/// <summary>
///     What the player shows of a file's own tags.
/// </summary>
/// <param name="Title">The title tag, or null.</param>
/// <param name="Artist">The artists, joined, or null.</param>
/// <param name="Album">The album tag, or null.</param>
public record TrackTags(string Title, string Artist, string Album);

/// <summary>
///     A cover picture taken out of a file's tag block.
/// </summary>
/// <param name="Data">The image bytes.</param>
/// <param name="MimeType">Its content type.</param>
/// <param name="ETag">A strong ETag of the bytes.</param>
public record Cover(byte[] Data, string MimeType, string ETag);

/// <summary>
///     Reading a finished download's own tags and cover. Nothing else has seen these files: there is no library to ask.
/// </summary>
public static class EmbeddedTags
{
    /// <summary>
    ///     The title, artist and album the file carries, or null when it carries none or cannot be read.
    /// </summary>
    /// <param name="filename">The file.</param>
    /// <returns>The tags, or null.</returns>
    public static TrackTags Read(string filename)
    {
        try
        {
            using var file = TagLib.File.Create(filename, TagLib.ReadStyle.PictureLazy);
            var tag = file.Tag;
            var artist = tag.JoinedPerformers;

            return new TrackTags(
                Title: NullIfBlank(tag.Title),
                Artist: NullIfBlank(artist),
                Album: NullIfBlank(tag.Album));
        }
        catch (Exception ex) when (ex is TagLib.UnsupportedFormatException or TagLib.CorruptFileException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    ///     The front cover, or the first picture when none is marked as the front, or null.
    /// </summary>
    /// <remarks>
    ///     By picture type, because a file can carry a back cover, a disc or a label logo as well, and the first picture
    ///     is often not the sleeve.
    /// </remarks>
    /// <param name="filename">The file.</param>
    /// <returns>The cover, or null.</returns>
    public static Cover ReadCover(string filename)
    {
        try
        {
            using var file = TagLib.File.Create(filename, TagLib.ReadStyle.None);
            var pictures = file.Tag.Pictures ?? Array.Empty<TagLib.IPicture>();
            var picture = pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? pictures.FirstOrDefault();

            if (picture?.Data is null || picture.Data.Count == 0)
            {
                return null;
            }

            var data = picture.Data.Data;
            var mime = string.IsNullOrWhiteSpace(picture.MimeType) ? Sniff(data) : picture.MimeType;
            var etag = "\"" + Convert.ToHexString(SHA256.HashData(data))[..32].ToLowerInvariant() + "\"";

            return new Cover(data, mime, etag);
        }
        catch (Exception ex) when (ex is TagLib.UnsupportedFormatException or TagLib.CorruptFileException or IOException)
        {
            return null;
        }
    }

    private static string Sniff(byte[] data)
        => data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 ? "image/png" : "image/jpeg";

    private static string NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
