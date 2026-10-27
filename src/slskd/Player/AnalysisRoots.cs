// <copyright file="AnalysisRoots.cs" company="JP Dillingham">
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
///     The folders slskd will ask an analysis provider about, and how a file in one is named to it.
/// </summary>
/// <remarks>
///     <para>
///         A file is named to a provider as a root's name plus a path relative to that root, never as an absolute
///         path: the two services may mount one folder in two different places, and only the provider knows where its
///         own mounts are. The provider holds the same names mapped to its folders, and that table is its allow-list.
///     </para>
///     <para>
///         The checks here are a courtesy -- a bad name never leaves slskd -- and not the provider's protection, which
///         it makes for itself. They are the same rules, so a request slskd sends is one a correct provider accepts.
///     </para>
/// </remarks>
public static class AnalysisRoots
{
    /// <summary>
    ///     A root's name: a plain word, so a name cannot carry a path.
    /// </summary>
    public static readonly Regex Name = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    /// <summary>
    ///     The extensions a provider may be asked about. The same six the music chain ingests.
    /// </summary>
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(new[] { ".mp3", ".aiff", ".aif", ".flac", ".wav", ".m4a" }, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     What is wrong with a table of roots, if anything.
    /// </summary>
    /// <param name="roots">Name to folder.</param>
    /// <returns>One message per problem; none for a good table.</returns>
    public static IEnumerable<string> Problems(IDictionary<string, string> roots)
    {
        var seen = new List<(string Name, string Folder)>();

        foreach (var (name, folder) in roots ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(name) || !Name.IsMatch(name))
            {
                yield return $"Player analysis root '{name}' is not a plain name (letters, digits, - and _)";
                continue;
            }

            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
            {
                yield return $"Player analysis root '{name}' must name an absolute folder";
                continue;
            }

            var full = Normalise(folder);

            foreach (var (otherName, otherFolder) in seen)
            {
                // one folder inside another gives one file two names, and lets the containment check pass for a folder
                // only one of the names was meant to reach
                if (Within(full, otherFolder) || Within(otherFolder, full))
                {
                    yield return $"Player analysis roots '{name}' and '{otherName}' overlap";
                }
            }

            seen.Add((name, full));
        }
    }

    /// <summary>
    ///     Whether <paramref name="relative"/> is a path the contract allows, as written.
    /// </summary>
    /// <remarks>
    ///     Refused rather than repaired: <c>a/../b</c> names a different file from the one written. A backslash is a
    ///     separator on Windows and nowhere else, so two layers could disagree about where it splits; a segment starting
    ///     with a dot covers <c>.</c>, <c>..</c> and every dotfile.
    /// </remarks>
    /// <param name="relative">The path, with forward slashes.</param>
    /// <returns>A value indicating whether the path is clean.</returns>
    public static bool IsClean(string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\0') || relative.Contains('\\'))
        {
            return false;
        }

        if (relative.StartsWith('/') || Regex.IsMatch(relative, "^[A-Za-z]:"))
        {
            return false;
        }

        return relative.Split('/').All(segment => segment.Length > 0 && segment[0] != '.');
    }

    /// <summary>
    ///     Names a file to the provider: the root containing it, and the path below that root.
    /// </summary>
    /// <param name="roots">Name to folder, as configured.</param>
    /// <param name="filename">The file's absolute, resolved path.</param>
    /// <returns>The pair, or null when no root holds the file or the path is not one the contract allows.</returns>
    public static (string Root, string File)? Locate(IDictionary<string, string> roots, string filename)
    {
        if (roots is null || string.IsNullOrWhiteSpace(filename) || !Path.IsPathFullyQualified(filename))
        {
            return null;
        }

        if (!Extensions.Contains(Path.GetExtension(filename)))
        {
            return null;
        }

        var full = Path.GetFullPath(filename);

        foreach (var (name, folder) in roots)
        {
            if (!Name.IsMatch(name ?? string.Empty) || string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            var root = Normalise(folder);

            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                var relative = full[(root.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
                return IsClean(relative) ? (name, relative) : null;
            }
        }

        return null;
    }

    private static string Normalise(string folder)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

    private static bool Within(string path, string root)
        => path == root || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
