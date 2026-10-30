// <copyright file="OneTaggerLink.cs" company="JP Dillingham">
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

namespace slskd.Integrations.OneTagger;

using System;
using System.IO;

/// <summary>
///     The address that opens a download in OneTagger's Tag Editor.
/// </summary>
/// <remarks>
///     OneTagger reads its view from the URL, after its hash router: <c>#/tageditor?path=&lt;folder&gt;&amp;file=&lt;file&gt;</c>
///     opens the Tag Editor on the folder with the file loaded, both as OneTagger sees them. A OneTagger that does not
///     read them opens on the Tag Editor as it last was, which is still the right page.
/// </remarks>
public static class OneTaggerLink
{
    /// <summary>
    ///     OneTagger's own address, for the menu.
    /// </summary>
    /// <param name="options">The OneTagger options.</param>
    /// <returns>The address, with no trailing slash.</returns>
    public static string Home(Options.IntegrationsOptions.OneTaggerOptions options)
        => options.Url?.TrimEnd('/');

    /// <summary>
    ///     The address that opens <paramref name="file"/> in the Tag Editor.
    /// </summary>
    /// <param name="options">The OneTagger options.</param>
    /// <param name="downloadsDirectory">slskd's downloads directory.</param>
    /// <param name="file">The download's file, resolved, as slskd sees it.</param>
    /// <returns>The address, or null when the file is not under the downloads directory.</returns>
    public static string For(Options.IntegrationsOptions.OneTaggerOptions options, string downloadsDirectory, string file)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(downloadsDirectory));
        var full = Path.GetFullPath(file);

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        // below the root, in OneTagger's terms: the separator of the system OneTagger runs on, as its root is written
        var theirs = string.IsNullOrWhiteSpace(options.Downloads) ? root : options.Downloads.TrimEnd('/', '\\');
        var separator = theirs.Contains('\\') && !theirs.Contains('/') ? '\\' : '/';
        var below = full[(root.Length + 1)..].Split(Path.DirectorySeparatorChar);

        var path = theirs + separator + string.Join(separator, below);
        var folder = below.Length == 1 ? theirs : theirs + separator + string.Join(separator, below[..^1]);

        return $"{Home(options)}/#/tageditor?path={Uri.EscapeDataString(folder)}&file={Uri.EscapeDataString(path)}";
    }
}
