// <copyright file="ShareLinkPagePolicy.cs" company="JP Dillingham">
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
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
///     The Content-Security-Policy the share link page is served with.
/// </summary>
/// <remarks>
///     <para>
///         The page is the one thing this instance shows to people who are not its operator, at whatever public name
///         it is given -- and that name is likely a sibling of the ones the operator signs in at, so a script run on it
///         would be same-site with them. It needs nothing but its own inline script and stylesheet and calls to its
///         own API, so the policy allows exactly those and nothing else: no other script, no plugin, no frame, no form.
///     </para>
///     <para>
///         The inline blocks are allowed by hash, computed from the page as it is served, so the policy cannot fall out
///         of step with the page: a hash written down anywhere else would refuse the page's own script the first time
///         the page changed, and the page would show nothing at all.
///     </para>
/// </remarks>
public static partial class ShareLinkPagePolicy
{
    /// <summary>
    ///     Returns the policy for the specified page.
    /// </summary>
    /// <param name="html">The page, exactly as it is served.</param>
    /// <returns>The policy.</returns>
    public static string For(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        return string.Join("; ", [
            "default-src 'none'",
            $"script-src {HashesOf(ScriptBlock(), html)}",
            $"style-src {HashesOf(StyleBlock(), html)}",
            "img-src data:",
            "connect-src 'self'",
            "base-uri 'none'",
            "form-action 'none'",
            "frame-ancestors 'none'",
        ]);
    }

    private static string HashesOf(Regex block, string html)
    {
        var hashes = block.Matches(html)
            .Select(match => $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(match.Groups[1].Value)))}'")
            .Distinct()
            .ToList();

        // a page with none of the kind allows none of the kind
        return hashes.Count > 0 ? string.Join(' ', hashes) : "'none'";
    }

    // only blocks with no attributes: the page has no other kind, and one with a src would be a request this policy
    // refuses anyway
    [GeneratedRegex("<script>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex ScriptBlock();

    [GeneratedRegex("<style>(.*?)</style>", RegexOptions.Singleline)]
    private static partial Regex StyleBlock();
}
