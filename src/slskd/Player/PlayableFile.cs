// <copyright file="PlayableFile.cs" company="JP Dillingham">
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
using slskd.Files;
using slskd.Transfers;

/// <summary>
///     Why a download cannot be played.
/// </summary>
public enum Unplayable
{
    /// <summary>
    ///     There is no such download.
    /// </summary>
    NoSuchDownload,

    /// <summary>
    ///     The download has not finished; asking again shortly is right.
    /// </summary>
    NotYet,

    /// <summary>
    ///     The download finished but its file is gone, or it never recorded one.
    /// </summary>
    Gone,

    /// <summary>
    ///     The recorded file is outside the downloads directory, or is not audio.
    /// </summary>
    Refused,
}

/// <summary>
///     Which file a download plays, if any.
/// </summary>
/// <remarks>
///     <para>
///         By the transfer's recorded <see cref="Transfer.LocalFilename"/>, never by a name the browser sends. That path
///         is where the bytes went when the file was moved into place, so nothing here reconstructs it from the peer's
///         remote name, which would sometimes mean walking the tree to find it.
///     </para>
///     <para>
///         Containment is checked twice, as it is everywhere a path is opened: lexically, before the filesystem is
///         asked, and again after symlinks are followed, which is the only check that catches a link inside the
///         downloads directory pointing out of it. Followed at every level of the path (<see cref="RealPath"/>), not
///         only the last: a link to a folder on the way leaves the path as written inside the directory.
///     </para>
/// </remarks>
public static class PlayableFile
{
    /// <summary>
    ///     Resolves the file a download plays.
    /// </summary>
    /// <param name="download">The download, or null.</param>
    /// <param name="downloadsDirectory">The configured downloads directory.</param>
    /// <param name="files">The file service, for following symlinks.</param>
    /// <param name="reason">Why it cannot be played, when it cannot.</param>
    /// <returns>The resolved file, or null.</returns>
    public static FileInfo Resolve(Transfer download, string downloadsDirectory, FileService files, out Unplayable reason)
    {
        reason = Unplayable.NoSuchDownload;

        if (download is null || download.Direction != Soulseek.TransferDirection.Download)
        {
            return null;
        }

        if (!TransferStateCategories.Successful.Contains(download.State))
        {
            // while it runs, the recorded path is the partial file in the incomplete directory, and that is not the
            // file the finished download will be
            reason = download.State.HasFlag(Soulseek.TransferStates.Completed) ? Unplayable.Gone : Unplayable.NotYet;
            return null;
        }

        var filename = download.LocalFilename;
        reason = Unplayable.Gone;

        if (string.IsNullOrWhiteSpace(filename))
        {
            return null;
        }

        reason = Unplayable.Refused;

        if (string.IsNullOrWhiteSpace(downloadsDirectory) || !Path.IsPathFullyQualified(filename)
            || Path.GetFullPath(filename) != filename || FileSafety.ContainsTraversalSegments(filename))
        {
            return null;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(downloadsDirectory));

        if (!Within(filename, root) || !Codecs.Playable.Contains(Path.GetExtension(filename)))
        {
            return null;
        }

        FileInfo resolved;

        try
        {
            // every link on the way followed first; what is left for the file service to translate is its errors
            resolved = files.ResolveFileInfo(RealPath(filename));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnauthorizedException or ArgumentException)
        {
            reason = Unplayable.Gone;
            return null;
        }

        if (!resolved.Exists)
        {
            reason = Unplayable.Gone;
            return null;
        }

        // the root resolved the same way, so a downloads directory that is itself a link, or reached through one,
        // still contains its own files
        string realRoot;

        try
        {
            realRoot = Path.TrimEndingDirectorySeparator(RealPath(root));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = Unplayable.Gone;
            return null;
        }

        if (!Within(resolved.FullName, realRoot) || !Codecs.Playable.Contains(resolved.Extension))
        {
            reason = Unplayable.Refused;
            return null;
        }

        return resolved;
    }

    /// <summary>
    ///     The path with every symlink on it followed, at every level, as realpath(3) gives it.
    /// </summary>
    /// <remarks>
    ///     .NET's own link resolution reads the link at the path's last component only, which is the file, so a
    ///     folder link on the way was never followed -- <c>downloads/d -> /etc</c> made <c>downloads/d/x.flac</c> a
    ///     path that read as inside. A component that does not exist is kept as written, and the caller's existence
    ///     check says so.
    /// </remarks>
    /// <param name="path">An absolute path.</param>
    /// <param name="depth">How many links have been followed to get here.</param>
    /// <returns>The resolved path.</returns>
    /// <exception cref="IOException">Thrown when links loop, or nest more deeply than the kernel itself allows.</exception>
    internal static string RealPath(string path, int depth = 0)
    {
        // the kernel's own limit (MAXSYMLINKS) on following links in one lookup
        if (depth > 40)
        {
            throw new IOException("Too many levels of symbolic links");
        }

        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full) ?? string.Empty;

        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, part);
            var link = new FileInfo(next).LinkTarget;

            if (link is not null)
            {
                // relative to the folder the link sits in, and possibly through further links of its own
                next = RealPath(Path.IsPathRooted(link) ? link : Path.Combine(current, link), depth + 1);
            }

            current = next;
        }

        return current;
    }

    private static bool Within(string path, string root)
        => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
