// <copyright file="OfflinePage.cs" company="JP Dillingham">
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

namespace slskd;

using System.Net;

/// <summary>
///     Renders the page the web app's service worker shows when slskd cannot be reached.
/// </summary>
/// <remarks>
///     The page itself is <c>pwa/offline.template.html</c> in the web content; this only puts the configured message
///     into it. Rendered on the server rather than in the page because the worker keeps one copy and shows it with no
///     server to ask -- the message has to be in the copy.
/// </remarks>
public static class OfflinePage
{
    /// <summary>
    ///     The path the rendered page is served at, relative to the URL base.
    /// </summary>
    public const string Path = "/pwa/offline.html";

    /// <summary>
    ///     The name of the template in the web content's <c>pwa</c> folder.
    /// </summary>
    public const string TemplateFileName = "offline.template.html";

    /// <summary>
    ///     The message shown when none is configured. Generic on purpose: slskd knows nothing of the network it is
    ///     deployed on, so anything more specific is the operator's to say.
    /// </summary>
    public const string DefaultMessage = "slskd can't be reached right now. Check your connection and try again.";

    private const string Placeholder = "{{message}}";

    /// <summary>
    ///     Renders <paramref name="template"/> with <paramref name="message"/>, or the default message when it is blank.
    /// </summary>
    /// <param name="template">The page template.</param>
    /// <param name="message">The configured message, as plain text.</param>
    /// <returns>The page.</returns>
    public static string Render(string template, string message)
    {
        var text = string.IsNullOrWhiteSpace(message) ? DefaultMessage : message.Trim();

        // plain text: encoded, so markup in the option is shown rather than run. line breaks survive as they are,
        // and the page's white-space: pre-line draws them
        return template.Replace(Placeholder, WebUtility.HtmlEncode(text.ReplaceLineEndings("\n")));
    }
}
