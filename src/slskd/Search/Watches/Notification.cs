// <copyright file="Notification.cs" company="JP Dillingham">
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
namespace slskd.Search.Watches;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

/// <summary>
///     The mail a watch sends.
/// </summary>
/// <remarks>
///     <para>
///         Composition lives here rather than in the service that sends it, because what a notification says is a
///         decision and decisions are worth testing. The service arranges the run; this writes it down.
///     </para>
///     <para>
///         Two bodies come out of one call. The plain one is the message -- it is what an adapter that cannot carry
///         multipart sends, and it is the fallback every client falls back to -- and the HTML one is what is actually
///         read. They say the same things about the same files; only the arrangement differs.
///     </para>
/// </remarks>
public static class Notification
{
    /// <summary>
    ///     The layouts the HTML body can be composed in.
    /// </summary>
    /// <remarks>
    ///     A table is the default because the question being asked of this mail is "is there anything here worth
    ///     having", and that is a question about scanning a column, not about reading paragraphs.
    /// </remarks>
    public const string TableLayout = "table";

    /// <summary>
    ///     The layout that mirrors the plain body: one block per file rather than one row.
    /// </summary>
    public const string ListLayout = "list";

    private const string TextColour = "#1b1c1d";
    private const string MutedColour = "#767676";
    private const string FaintColour = "#9a9a9a";
    private const string RuleColour = "#ececec";
    private const string HeadRuleColour = "#d4d4d5";
    private const string StripeColour = "#fafafa";
    private const string FontStack = "-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif";

    /// <summary>
    ///     Returns the subject for a run that found the specified number of files.
    /// </summary>
    /// <param name="searchText">The phrase being watched.</param>
    /// <param name="count">The number of files found.</param>
    /// <returns>The subject.</returns>
    public static string SubjectFor(string searchText, int count)
        => $"slskd: {count} new result{(count == 1 ? string.Empty : "s")} for '{searchText}'";

    /// <summary>
    ///     Composes the notification for a run.
    /// </summary>
    /// <param name="searchText">The phrase being watched.</param>
    /// <param name="searchId">The id of the search, for the links back into the application.</param>
    /// <param name="files">The files the run found.</param>
    /// <param name="enqueued">The number of those files queued for download.</param>
    /// <param name="baseUrl">The publicly reachable base url, or null when none is configured.</param>
    /// <param name="layout">The layout for the HTML body; <see cref="TableLayout"/> or <see cref="ListLayout"/>.</param>
    /// <param name="maximumListed">The most files to name.</param>
    /// <returns>The composed notification.</returns>
    public static Composed Compose(
        string searchText,
        Guid searchId,
        IEnumerable<WatchService.Match> files,
        int enqueued,
        string baseUrl,
        string layout,
        int maximumListed)
    {
        ArgumentNullException.ThrowIfNull(files);

        var all = files.ToList();

        // by name first, so the three peers holding one track are three adjacent rows rather than three places in
        // the table -- which is the comparison a reader is actually making. the full path breaks that up, since it
        // begins with a share name and a folder that differ per peer
        var listed = all
            .OrderBy(file => NameOf(file.Filename), StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Username, StringComparer.OrdinalIgnoreCase)
            .Take(maximumListed)
            .ToList();

        var url = baseUrl?.TrimEnd('/');
        var omitted = all.Count - listed.Count;

        return new Composed
        {
            Subject = SubjectFor(searchText, all.Count),
            Text = ComposeText(searchText, searchId, all.Count, listed, enqueued, url, omitted),
            Html = ComposeHtml(searchText, searchId, all.Count, listed, enqueued, url, omitted, layout),
        };
    }

    /// <summary>
    ///     Returns the filename part of a remote path.
    /// </summary>
    private static string NameOf(string path) => IgnoreSet.NameOf(path) ?? string.Empty;

    /// <summary>
    ///     Returns the folder part of a remote path, or an empty string when it has none.
    /// </summary>
    /// <remarks>
    ///     Shown under the name rather than beside it: a peer's path is most of a line on its own, and the name is
    ///     the part being scanned for. The folder still has to be there -- it is how one copy of a track is told
    ///     from another.
    /// </remarks>
    private static string FolderOf(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var cut = path.LastIndexOfAny(['\\', '/']);

        return cut < 0 ? string.Empty : path[..cut];
    }

    private static string Megabytes(long size) => $"{size / 1024 / 1024} MB";

    private static string BitRate(int? bitRate) => bitRate.HasValue ? $"{bitRate} kbps" : string.Empty;

    private static string Duration(int? length) => length.HasValue ? $"{length / 60}:{length % 60:00}" : string.Empty;

    private static string IgnoreUrl(string baseUrl, Guid searchId, string filename)
        => $"{baseUrl}/searches/{searchId}?ignore={Uri.EscapeDataString(NameOf(filename))}";

    private static string SearchUrl(string baseUrl, Guid searchId)
        => $"{baseUrl}/searches/{searchId}";

    private static string Encode(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string ComposeText(
        string searchText,
        Guid searchId,
        int count,
        List<WatchService.Match> listed,
        int enqueued,
        string baseUrl,
        int omitted)
    {
        var body = new StringBuilder();

        body.AppendLine($"A watch on '{searchText}' found {count} file(s) it has not reported before.");
        body.AppendLine();

        foreach (var file in listed)
        {
            var attributes = new[] { Megabytes(file.Size), BitRate(file.BitRate), Duration(file.Length) }
                .Where(part => !string.IsNullOrEmpty(part));

            body.AppendLine($"  {file.Username}");
            body.AppendLine($"    {file.Filename}");
            body.AppendLine($"    {string.Join(", ", attributes)}");

            // a link to stop hearing about this one, which is the thing an operator wants at the moment they are
            // reading about a file they do not want. it opens the search and asks before it does anything: the link
            // carries no authority of its own, so following it is safe for anything that prefetches links in mail
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                body.AppendLine($"    Never report this again: {IgnoreUrl(baseUrl, searchId, file.Filename)}");
            }

            body.AppendLine();
        }

        if (enqueued > 0)
        {
            body.AppendLine($"{enqueued} of these have been queued for download.");
            body.AppendLine();
        }

        if (omitted > 0)
        {
            body.AppendLine($"...and {omitted} more.");
            body.AppendLine();
            body.AppendLine("A search this broad will report hundreds of files on every run, because the network");
            body.AppendLine("answers with whichever peers happen to reply and that set differs each time. A watch");
            body.AppendLine("is at its best on a search narrow enough that its results are stable.");
            body.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            body.AppendLine(SearchUrl(baseUrl, searchId));
        }

        return body.ToString();
    }

    /// <remarks>
    ///     Styles are written on every element rather than in a stylesheet, and the layout is a table rather than
    ///     anything that positions: a mail client is free to drop a &lt;style&gt; block and several do. There is no
    ///     image, no script and no external request, so nothing here asks a client to load anything.
    /// </remarks>
    private static string ComposeHtml(
        string searchText,
        Guid searchId,
        int count,
        List<WatchService.Match> listed,
        int enqueued,
        string baseUrl,
        int omitted,
        string layout)
    {
        var html = new StringBuilder();

        html.AppendLine("<!doctype html>");
        html.AppendLine("<html><head><meta charset=\"utf-8\"><meta name=\"color-scheme\" content=\"light\"></head>");
        html.AppendLine($"<body style=\"margin:0;padding:16px;background:#ffffff;color:{TextColour};font-family:{FontStack};font-size:14px;line-height:1.45\">");

        html.AppendLine($"<p style=\"margin:0 0 14px 0\">A watch on <strong>{Encode(searchText)}</strong> found {count} file{(count == 1 ? string.Empty : "s")} it has not reported before.</p>");

        if (listed.Count > 0)
        {
            // anything that is not the list is the table, the option having already been validated against the two
            // names -- a layout that cannot be recognised is not worth failing a notification over
            html.AppendLine(ListLayout.Equals(layout, StringComparison.OrdinalIgnoreCase)
                ? RenderList(listed, searchId, baseUrl)
                : RenderTable(listed, searchId, baseUrl));
        }

        if (enqueued > 0)
        {
            html.AppendLine($"<p style=\"margin:14px 0 0 0\">{enqueued} of these {(enqueued == 1 ? "has" : "have")} been queued for download.</p>");
        }

        if (omitted > 0)
        {
            html.AppendLine($"<p style=\"margin:14px 0 0 0\">...and {omitted} more.</p>");
            html.AppendLine($"<p style=\"margin:6px 0 0 0;color:{MutedColour};font-size:12px\">A search this broad will report hundreds of files on every run, because the network answers with whichever peers happen to reply and that set differs each time. A watch is at its best on a search narrow enough that its results are stable.</p>");
        }

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            var url = Encode(SearchUrl(baseUrl, searchId));
            html.AppendLine($"<p style=\"margin:18px 0 0 0\"><a href=\"{url}\" style=\"color:#2185d0\">Open this search</a></p>");
        }

        html.AppendLine("</body></html>");

        return html.ToString();
    }

    private static string RenderTable(List<WatchService.Match> listed, Guid searchId, string baseUrl)
    {
        var linked = !string.IsNullOrWhiteSpace(baseUrl);

        var head = $"padding:6px 8px;border-bottom:2px solid {HeadRuleColour};color:{MutedColour};font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.04em";
        var cell = $"padding:7px 8px;border-bottom:1px solid {RuleColour};vertical-align:top";
        var number = $"{cell};text-align:right;white-space:nowrap;color:{MutedColour}";

        var table = new StringBuilder();

        table.AppendLine("<table cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"border-collapse:collapse;width:100%;font-size:13px\">");
        table.AppendLine("<thead><tr>");
        table.AppendLine($"<th align=\"left\" style=\"{head}\">Track</th>");
        table.AppendLine($"<th align=\"left\" style=\"{head}\">Peer</th>");
        table.AppendLine($"<th align=\"right\" style=\"{head};text-align:right\">Size</th>");
        table.AppendLine($"<th align=\"right\" style=\"{head};text-align:right\">Bitrate</th>");
        table.AppendLine($"<th align=\"right\" style=\"{head};text-align:right\">Length</th>");

        if (linked)
        {
            table.AppendLine($"<th style=\"{head}\"></th>");
        }

        table.AppendLine("</tr></thead>");
        table.AppendLine("<tbody>");

        var row = 0;

        foreach (var file in listed)
        {
            // zebra striping, because the thing being done to this table is reading across a row of a file whose name
            // is the widest column by a distance
            var stripe = row++ % 2 == 1 ? $" background:{StripeColour};" : string.Empty;
            var folder = FolderOf(file.Filename);

            table.AppendLine("<tr>");
            table.Append($"<td style=\"{cell};{stripe}word-break:break-word\">");
            table.Append($"<div style=\"font-weight:600\">{Encode(NameOf(file.Filename))}</div>");

            if (!string.IsNullOrEmpty(folder))
            {
                table.Append($"<div style=\"color:{FaintColour};font-size:11px;margin-top:2px\">{Encode(folder)}</div>");
            }

            table.AppendLine("</td>");
            table.AppendLine($"<td style=\"{cell};{stripe}color:{MutedColour};word-break:break-word\">{Encode(file.Username)}</td>");
            table.AppendLine($"<td style=\"{number};{stripe}\">{Encode(Megabytes(file.Size))}</td>");
            table.AppendLine($"<td style=\"{number};{stripe}\">{Encode(BitRate(file.BitRate))}</td>");
            table.AppendLine($"<td style=\"{number};{stripe}\">{Encode(Duration(file.Length))}</td>");

            if (linked)
            {
                var url = Encode(IgnoreUrl(baseUrl, searchId, file.Filename));

                table.AppendLine($"<td style=\"{cell};{stripe}white-space:nowrap\"><a href=\"{url}\" title=\"Never report this file again\" style=\"color:{MutedColour};font-size:11px\">ignore</a></td>");
            }

            table.AppendLine("</tr>");
        }

        table.AppendLine("</tbody></table>");

        return table.ToString();
    }

    private static string RenderList(List<WatchService.Match> listed, Guid searchId, string baseUrl)
    {
        var list = new StringBuilder();

        foreach (var file in listed)
        {
            var folder = FolderOf(file.Filename);

            var attributes = new[] { Encode(file.Username), Encode(Megabytes(file.Size)), Encode(BitRate(file.BitRate)), Encode(Duration(file.Length)) }
                .Where(part => !string.IsNullOrEmpty(part))
                .ToList();

            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                var url = Encode(IgnoreUrl(baseUrl, searchId, file.Filename));

                attributes.Add($"<a href=\"{url}\" title=\"Never report this file again\" style=\"color:{MutedColour}\">ignore</a>");
            }

            list.AppendLine($"<div style=\"margin:0 0 14px 0;word-break:break-word\">");
            list.AppendLine($"<div style=\"font-weight:600\">{Encode(NameOf(file.Filename))}</div>");

            if (!string.IsNullOrEmpty(folder))
            {
                list.AppendLine($"<div style=\"color:{FaintColour};font-size:12px;margin-top:2px\">{Encode(folder)}</div>");
            }

            list.AppendLine($"<div style=\"color:{MutedColour};font-size:12px;margin-top:2px\">{string.Join(" &middot; ", attributes)}</div>");
            list.AppendLine("</div>");
        }

        return list.ToString();
    }

    /// <summary>
    ///     A composed notification.
    /// </summary>
    public record Composed
    {
        /// <summary>
        ///     Gets the subject.
        /// </summary>
        public required string Subject { get; init; }

        /// <summary>
        ///     Gets the plain text body.
        /// </summary>
        public required string Text { get; init; }

        /// <summary>
        ///     Gets the HTML body.
        /// </summary>
        public required string Html { get; init; }
    }
}
