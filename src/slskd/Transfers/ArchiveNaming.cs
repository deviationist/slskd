// <copyright file="ArchiveNaming.cs" company="JP Dillingham">
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
using System.IO;
using System.Linq;

/// <summary>
///     Names for the entries and the file of an archive of downloaded files.
/// </summary>
/// <remarks>
///     Separated from the code that writes the archive because naming is where the decisions are, and because a pure
///     function over a list of remote filenames can be tested without a filesystem, a transfer, or a response.
/// </remarks>
public static class ArchiveNaming
{
    /// <summary>
    ///     The name given to the file listing entries that were skipped.
    /// </summary>
    public const string MissingEntryName = "MISSING.txt";

    /// <summary>
    ///     Returns the entry name to use for each of the specified <paramref name="remoteFilenames"/>, in the order
    ///     they were given.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each entry keeps the name of the folder it came from, so that the '01 - Intro.flac' of one album and the
    ///         '01 - Intro.flac' of another are two entries rather than one. Keeping only the *last* folder rather than
    ///         the whole remote path is deliberate: the rest of a Soulseek path is the sharer's own directory layout,
    ///         which is noise in an archive, and can include a share prefix like '@@abcde'.
    ///     </para>
    ///     <para>
    ///         Names that still collide after that -- two folders named 'CD1', most obviously -- are numbered rather
    ///         than allowed to overwrite each other. Comparison is case-insensitive, because the archive will very
    ///         likely be extracted on a filesystem that is.
    ///     </para>
    ///     <para>
    ///         Every segment is sanitized: these names were chosen by a remote peer, and an entry name containing a
    ///         traversal segment would be a trap laid for whatever extracts the archive.
    ///     </para>
    /// </remarks>
    /// <param name="remoteFilenames">The remote filenames of the files to be archived.</param>
    /// <returns>The entry name for each file, in the order the filenames were given.</returns>
    public static string[] EntryNamesFor(IEnumerable<string> remoteFilenames)
    {
        ArgumentNullException.ThrowIfNull(remoteFilenames);

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // never let a file take the name the skipped-entry list will be written under
            MissingEntryName,
        };

        return remoteFilenames
            .Select(filename => Deduplicate(EntryNameFor(filename), taken))
            .ToArray();
    }

    /// <summary>
    ///     Returns the name to give the archive file itself.
    /// </summary>
    /// <remarks>
    ///     An archive of one folder is named for that folder, which is the name an operator will recognise in a
    ///     Downloads directory. A selection spanning several folders has no such name, so it falls back to who it came
    ///     from and when, which at least sorts and disambiguates.
    /// </remarks>
    /// <param name="username">The username the files were downloaded from.</param>
    /// <param name="remoteFilenames">The remote filenames of the files to be archived.</param>
    /// <param name="timestampUtc">The current time, used when there is no single folder to name the archive for.</param>
    /// <returns>The name of the archive file, including the extension.</returns>
    public static string ArchiveNameFor(string username, IEnumerable<string> remoteFilenames, DateTime timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(remoteFilenames);

        var folders = remoteFilenames
            .Select(filename => FolderOf(filename))
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2) // only whether there is exactly one matters
            .ToArray();

        if (folders.Length == 1)
        {
            return $"{folders[0]}.zip";
        }

        var who = FileSafety.SanitizePathSegment(username ?? string.Empty);

        if (string.IsNullOrWhiteSpace(who))
        {
            who = "downloads";
        }

        return $"{who}-{timestampUtc:yyyyMMdd-HHmmss}.zip";
    }

    /// <summary>
    ///     Returns the entry name for a single file, before any deduplication.
    /// </summary>
    /// <param name="remoteFilename">The remote filename.</param>
    /// <returns>The entry name.</returns>
    private static string EntryNameFor(string remoteFilename)
    {
        var segments = Segments(remoteFilename);

        var file = segments.Length > 0 ? FileSafety.SanitizePathSegment(segments[^1]) : string.Empty;

        if (string.IsNullOrWhiteSpace(file))
        {
            // a remote filename made entirely of separators or invalid characters. it still has to land somewhere,
            // and an entry with no name at all is not something to hand to an extractor
            file = "file";
        }

        var folder = segments.Length > 1 ? FileSafety.SanitizePathSegment(segments[^2]) : string.Empty;

        // zip entry names are separated with a forward slash on every platform, by specification
        return string.IsNullOrWhiteSpace(folder) ? file : $"{folder}/{file}";
    }

    /// <summary>
    ///     Returns the sanitized name of the folder the specified <paramref name="remoteFilename"/> sits in.
    /// </summary>
    /// <param name="remoteFilename">The remote filename.</param>
    /// <returns>The folder name, or an empty string if there isn't one.</returns>
    private static string FolderOf(string remoteFilename)
    {
        var segments = Segments(remoteFilename);

        return segments.Length > 1 ? FileSafety.SanitizePathSegment(segments[^2]) : string.Empty;
    }

    /// <summary>
    ///     Splits the specified <paramref name="remoteFilename"/> into segments, on either separator.
    /// </summary>
    /// <remarks>
    ///     Either, and not the one the local OS uses: these paths come from remote peers, and a Soulseek path is
    ///     conventionally separated with backslashes regardless of what either end is running.
    /// </remarks>
    /// <param name="remoteFilename">The remote filename.</param>
    /// <returns>The segments.</returns>
    private static string[] Segments(string remoteFilename)
        => (remoteFilename ?? string.Empty)
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    ///     Returns the specified <paramref name="name"/>, numbered if something has already taken it, and records the
    ///     result as taken.
    /// </summary>
    /// <param name="name">The desired entry name.</param>
    /// <param name="taken">The names already in use.</param>
    /// <returns>A name nothing else in the archive is using.</returns>
    private static string Deduplicate(string name, HashSet<string> taken)
    {
        if (taken.Add(name))
        {
            return name;
        }

        var directory = name.Contains('/') ? name[..(name.LastIndexOf('/') + 1)] : string.Empty;
        var file = name[directory.Length..];
        var stem = Path.GetFileNameWithoutExtension(file);
        var extension = Path.GetExtension(file);

        for (var n = 2; ; n++)
        {
            var candidate = $"{directory}{stem} ({n}){extension}";

            if (taken.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
