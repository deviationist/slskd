// <copyright file="NotificationTests.cs" company="JP Dillingham">
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
namespace slskd.Tests.Unit.Search.Watches;

using System;
using System.Collections.Generic;
using System.Linq;
using slskd.Search.Watches;
using Xunit;

public class NotificationTests
{
    private static readonly Guid SearchId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Composes_Both_Bodies()
    {
        // the plain one is the message and the HTML one is what is read; an adapter gets to choose, and a client
        // that will not render HTML must still receive something complete
        var composed = Compose([Match("alice", @"@share\Album\01 Xtal.flac")]);

        Assert.Contains("01 Xtal.flac", composed.Text);
        Assert.Contains("01 Xtal.flac", composed.Html);
        Assert.StartsWith("<!doctype html>", composed.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_Subject_Counts_The_Files_And_Says_Result_Once_For_One()
    {
        Assert.Equal("slskd: 1 new result for 'aphex'", Notification.SubjectFor("aphex", 1));
        Assert.Equal("slskd: 4 new results for 'aphex'", Notification.SubjectFor("aphex", 4));
    }

    [Fact]
    public void The_Default_Layout_Is_A_Table()
    {
        var composed = Compose([Match("alice", @"@share\Album\01 Xtal.flac")], layout: null);

        Assert.Contains("<table", composed.Html);
        Assert.Contains("<th", composed.Html);
    }

    [Fact]
    public void Every_Column_Has_A_Header()
    {
        var composed = Compose([Match("alice", @"@share\Album\01 Xtal.flac")]);

        foreach (var column in new[] { "Track", "Peer", "Size", "Bitrate", "Length" })
        {
            Assert.Contains($">{column}</th>", composed.Html);
        }
    }

    [Fact]
    public void A_Row_Carries_What_The_Plain_Body_Says_About_A_File()
    {
        var composed = Compose([Match("alice", @"@share\Album\01 Xtal.flac", size: 41_943_040, bitRate: 1411, length: 293)]);

        Assert.Contains("40 MB", composed.Html);
        Assert.Contains("1411 kbps", composed.Html);
        Assert.Contains("4:53", composed.Html);
        Assert.Contains("alice", composed.Html);

        // and the plain body still says all of it, on its one line
        Assert.Contains("40 MB, 1411 kbps, 4:53", composed.Text);
    }

    [Fact]
    public void The_Name_Is_The_Row_And_The_Folder_Is_Under_It()
    {
        // a peer's path is most of a line on its own, and the name is the part being scanned for
        var composed = Compose([Match("alice", @"@share\Aphex Twin\Selected Ambient Works\01 Xtal.flac")]);

        Assert.Contains(">01 Xtal.flac</div>", composed.Html);
        Assert.Contains(@"@share\Aphex Twin\Selected Ambient Works", composed.Html);

        // the plain body keeps the whole path, since it has no second line to put it on
        Assert.Contains(@"@share\Aphex Twin\Selected Ambient Works\01 Xtal.flac", composed.Text);
    }

    [Fact]
    public void An_Attribute_No_Peer_Reported_Leaves_An_Empty_Cell_Rather_Than_A_Zero()
    {
        // a bitrate of zero is a claim; an empty cell is the truth, which is that nobody said
        var composed = Compose([Match("alice", "x.flac", bitRate: null, length: null)]);

        Assert.DoesNotContain("0 kbps", composed.Html);
        Assert.DoesNotContain("0:00", composed.Html);
    }

    [Fact]
    public void One_Track_From_Three_Peers_Is_Three_Adjacent_Rows()
    {
        // the comparison being made is between copies of one track, and ordering by the full path would scatter them:
        // a path begins with a share name and a folder that differ per peer
        var composed = Compose(
        [
            Match("carol", @"@c\misc\02 Tha.flac"),
            Match("alice", @"@a\Album\01 Xtal.flac"),
            Match("bob", @"@b\Other\01 Xtal.flac"),
        ]);

        var first = composed.Html.IndexOf("alice", StringComparison.Ordinal);
        var second = composed.Html.IndexOf("bob", StringComparison.Ordinal);
        var third = composed.Html.IndexOf("carol", StringComparison.Ordinal);

        Assert.True(first < second && second < third, "the two copies of one track are not adjacent");
    }

    [Fact]
    public void Each_Row_Carries_The_Link_That_Ignores_It()
    {
        var composed = Compose([Match("alice", @"@share\Album\01 Xtal.flac")]);

        Assert.Contains($"/searches/{SearchId}?ignore=01%20Xtal.flac", composed.Html);
        Assert.Contains($"/searches/{SearchId}?ignore=01%20Xtal.flac", composed.Text);
    }

    [Fact]
    public void Without_A_Base_Url_There_Is_No_Link_Column_And_No_Broken_Link()
    {
        // the application cannot work out what it is reached at; an unconfigured base url must cost the link, not
        // produce one that goes nowhere
        var composed = Compose([Match("alice", @"@share\Album\01 Xtal.flac")], baseUrl: null);

        Assert.DoesNotContain("<a href", composed.Html);
        Assert.DoesNotContain("Never report this again", composed.Text);
    }

    [Fact]
    public void A_Trailing_Slash_On_The_Base_Url_Does_Not_Double()
    {
        var composed = Compose([Match("alice", "x.flac")], baseUrl: "https://slsk.example.com/");

        Assert.DoesNotContain("com//searches", composed.Html);
        Assert.DoesNotContain("com//searches", composed.Text);
    }

    [Fact]
    public void A_Filename_That_Is_Markup_Is_Escaped()
    {
        // peers name their own files, and one of them is a name with a bracket in it. the plain body is unaffected
        var composed = Compose([Match("alice", @"@share\<script>alert(1)</script>.flac")]);

        Assert.DoesNotContain("<script>", composed.Html);
        Assert.Contains("&lt;script&gt;", composed.Html);
    }

    [Fact]
    public void A_Peer_Named_As_Markup_Is_Escaped_Too()
    {
        var composed = Compose([Match("<b>alice</b>", "x.flac")]);

        Assert.DoesNotContain("<b>alice</b>", composed.Html);
        Assert.Contains("&lt;b&gt;alice&lt;/b&gt;", composed.Html);
    }

    [Fact]
    public void The_List_Layout_Renders_Blocks_Rather_Than_Rows()
    {
        var composed = Compose([Match("alice", @"@share\Album\01 Xtal.flac")], layout: Notification.ListLayout);

        Assert.DoesNotContain("<table", composed.Html);
        Assert.Contains("01 Xtal.flac", composed.Html);
        Assert.Contains("alice", composed.Html);
    }

    [Fact]
    public void The_Layout_Is_Read_Without_Regard_To_Case()
    {
        var composed = Compose([Match("alice", "x.flac")], layout: "LIST");

        Assert.DoesNotContain("<table", composed.Html);
    }

    [Fact]
    public void A_Layout_Nobody_Recognises_Is_A_Table_Rather_Than_A_Failure()
    {
        // the option is validated where it is set; a notification is not the place to discover it was not
        var composed = Compose([Match("alice", "x.flac")], layout: "spreadsheet");

        Assert.Contains("<table", composed.Html);
    }

    [Fact]
    public void Both_Bodies_Say_What_Was_Queued()
    {
        var composed = Compose([Match("alice", "x.flac")], enqueued: 3);

        Assert.Contains("3 of these have been queued for download.", composed.Text);
        Assert.Contains("3 of these have been queued for download.", composed.Html);
    }

    [Fact]
    public void A_Run_That_Queued_Nothing_Says_Nothing_About_Queueing()
    {
        var composed = Compose([Match("alice", "x.flac")], enqueued: 0);

        Assert.DoesNotContain("queued for download", composed.Text);
        Assert.DoesNotContain("queued for download", composed.Html);
    }

    [Fact]
    public void More_Files_Than_Will_Be_Listed_Are_Counted_And_Explained()
    {
        var files = Enumerable.Range(0, 10).Select(i => Match($"peer{i}", $"{i}.flac")).ToList();

        var composed = Compose(files, maximumListed: 4);

        Assert.Contains("...and 6 more.", composed.Text);
        Assert.Contains("...and 6 more.", composed.Html);
        Assert.Contains("A search this broad", composed.Text);
        Assert.Contains("A search this broad", composed.Html);

        // the count in the subject is what the run found, not what it listed
        Assert.Equal("slskd: 10 new results for 'aphex'", composed.Subject);
    }

    [Fact]
    public void A_Run_Inside_The_Limit_Is_Not_Told_Its_Search_Is_Too_Broad()
    {
        var composed = Compose([Match("alice", "x.flac")], maximumListed: 50);

        Assert.DoesNotContain("more.", composed.Text);
        Assert.DoesNotContain("A search this broad", composed.Html);
    }

    [Fact]
    public void Nothing_In_The_Html_Asks_A_Client_To_Load_Anything()
    {
        // a remote image in mail is a read receipt, and a mail client that blocks one leaves a hole where the table
        // should be. there is no image, no script, and no stylesheet here
        var composed = Compose([Match("alice", @"@share\Album\01 Xtal.flac")]);

        Assert.DoesNotContain("<img", composed.Html);
        Assert.DoesNotContain("<script", composed.Html);
        Assert.DoesNotContain("<link", composed.Html);
    }

    private static Notification.Composed Compose(
        IEnumerable<WatchService.Match> files,
        int enqueued = 0,
        string baseUrl = "https://slsk.example.com",
        string layout = Notification.TableLayout,
        int maximumListed = 50)
        => Notification.Compose(
            searchText: "aphex",
            searchId: SearchId,
            files: files,
            enqueued: enqueued,
            baseUrl: baseUrl,
            layout: layout,
            maximumListed: maximumListed);

    private static WatchService.Match Match(
        string username,
        string filename,
        long size = 1024 * 1024,
        int? bitRate = 320,
        int? length = 240)
        => new()
        {
            Username = username,
            Filename = filename,
            Size = size,
            BitRate = bitRate,
            Length = length,
        };
}
