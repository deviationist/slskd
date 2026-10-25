// <copyright file="ShareLinkMail.cs" company="JP Dillingham">
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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using slskd.Integrations.Mail;

/// <summary>
///     Composes the mail that hands someone a share link.
/// </summary>
/// <remarks>
///     Written for a reader who has never heard of slskd: what they have been sent, how to get it, and until when.
///     Nothing in it says where the files came from beyond the link itself.
/// </remarks>
public static class ShareLinkMail
{
    /// <summary>
    ///     The most files the mail lists by name; the rest are counted.
    /// </summary>
    public const int MaximumFilesListed = 25;

    private const string TextColour = "#1b1c1d";
    private const string MutedColour = "#767676";
    private const string ButtonColour = "#2185d0";
    private const string FontStack = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    /// <summary>
    ///     Composes the mail for the specified link, without a recipient.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="url">The link's address.</param>
    /// <param name="options">The options, for the zone and locale the expiry is written in.</param>
    /// <returns>The mail.</returns>
    public static Mail Compose(ShareLink link, string url, Options options)
    {
        var count = link.Files.Count;
        var size = link.Files.Sum(f => f.Size);
        var expires = FormatExpiry(link.ExpiresAt, options);
        var what = count == 1 ? "a file" : $"{count} files";

        var subject = $"Shared with you: {link.Title}";

        var listed = link.Files.OrderBy(f => f.Position).Take(MaximumFilesListed).ToList();
        var unlisted = count - listed.Count;

        var text = new StringBuilder();
        text.AppendLine($"Someone has shared {what} with you ({((double)size).SizeSuffix()}).");
        text.AppendLine();
        text.AppendLine(link.Title);

        foreach (var file in listed)
        {
            text.AppendLine($"  {file.Name}");
        }

        if (unlisted > 0)
        {
            text.AppendLine($"  ... and {unlisted} more");
        }

        text.AppendLine();
        text.AppendLine($"Download: {url}");
        text.AppendLine();
        text.AppendLine($"The link works until {expires}. After that the files are deleted.");

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head>");
        html.AppendLine($"<body style=\"margin:0;padding:16px;background:#ffffff;color:{TextColour};font-family:{FontStack};font-size:14px;line-height:1.45\">");
        html.AppendLine($"<p style=\"margin:0 0 12px\">Someone has shared {Encode(what)} with you ({Encode(((double)size).SizeSuffix())}).</p>");
        html.AppendLine($"<p style=\"margin:0 0 4px;font-size:16px;font-weight:bold\">{Encode(link.Title)}</p>");
        html.AppendLine($"<ul style=\"margin:0 0 16px;padding-left:20px;color:{MutedColour}\">");

        foreach (var file in listed)
        {
            html.AppendLine($"<li>{Encode(file.Name)}</li>");
        }

        if (unlisted > 0)
        {
            html.AppendLine($"<li>and {unlisted} more</li>");
        }

        html.AppendLine("</ul>");
        html.AppendLine($"<p style=\"margin:0 0 16px\"><a href=\"{Encode(url)}\" style=\"display:inline-block;background:{ButtonColour};color:#ffffff;text-decoration:none;font-weight:bold;padding:10px 20px;border-radius:4px\">Download</a></p>");
        html.AppendLine($"<p style=\"margin:0 0 4px;color:{MutedColour};font-size:12px\">Or open this address: <a href=\"{Encode(url)}\" style=\"color:{MutedColour}\">{Encode(url)}</a></p>");
        html.AppendLine($"<p style=\"margin:0;color:{MutedColour};font-size:12px\">The link works until {Encode(expires)}. After that the files are deleted.</p>");
        html.AppendLine("</body></html>");

        return new Mail { Subject = subject, Text = text.ToString(), Html = html.ToString() };
    }

    /// <summary>
    ///     Writes the specified instant in the zone search watches are scheduled in, and the UI's locale.
    /// </summary>
    /// <remarks>
    ///     The watch zone because it is the one setting that says which zone this instance's operator lives in, and
    ///     the person a link is sent to most likely shares it. Named in the text either way, so a reader elsewhere is
    ///     not misled.
    /// </remarks>
    /// <param name="utc">The instant.</param>
    /// <param name="options">The options.</param>
    /// <returns>The formatted instant.</returns>
    public static string FormatExpiry(DateTime utc, Options options)
    {
        TimeZoneInfo zone;

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(options.Searches.Watches.TimeZone ?? "Etc/UTC");
        }
        catch (Exception)
        {
            zone = TimeZoneInfo.Utc;
        }

        CultureInfo culture;

        try
        {
            culture = string.IsNullOrWhiteSpace(options.Web.Locale) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(options.Web.Locale);
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.InvariantCulture;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

        return $"{local.ToString("dddd d MMMM yyyy, HH:mm", culture)} ({zone.Id})";
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
