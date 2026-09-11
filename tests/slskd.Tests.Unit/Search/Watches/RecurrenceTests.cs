// <copyright file="RecurrenceTests.cs" company="JP Dillingham">
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
using slskd.Search;
using slskd.Search.Watches;
using Xunit;

public class RecurrenceTests
{
    private static readonly DateTime Anchor = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_Daily_Rule_Keeps_Its_Local_Hour_Across_A_Dst_Change()
    {
        // the whole reason a watch stores a zone rather than an offset. 03:00 in Oslo is 02:00Z in winter and 01:00Z
        // in summer, and an operator who asked for 03:00 meant 03:00 where they are, on both sides of the change
        const string Rule = "FREQ=DAILY;BYHOUR=3;BYMINUTE=0;BYSECOND=0";

        var winter = Recurrence.Next(Rule, Anchor, "Europe/Oslo", new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc));
        var summer = Recurrence.Next(Rule, Anchor, "Europe/Oslo", new DateTime(2026, 3, 30, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(2, winter!.Value.Hour);
        Assert.Equal(1, summer!.Value.Hour);
    }

    [Fact]
    public void The_Next_Occurrence_Is_Strictly_After_The_Moment_Asked_About()
    {
        // otherwise a watch that has just run finds itself due again immediately, and runs in a loop
        const string Rule = "FREQ=HOURLY;INTERVAL=1";

        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var next = Recurrence.Next(Rule, now, "Etc/UTC", now);

        Assert.True(next > now, $"expected an occurrence after {now:u}, got {next:u}");
    }

    [Theory]
    [InlineData("FREQ=HOURLY;INTERVAL=1", 60)]
    [InlineData("FREQ=HOURLY;INTERVAL=6", 360)]
    [InlineData("FREQ=DAILY", 1440)]
    public void The_Shortest_Interval_Is_Measured_Rather_Than_Read(string rule, int expectedMinutes)
    {
        var shortest = Recurrence.ShortestInterval(rule, Anchor, "Etc/UTC");

        Assert.Equal(expectedMinutes, (int)shortest!.Value.TotalMinutes);
    }

    [Fact]
    public void A_Weekly_Rule_On_Consecutive_Days_Is_Measured_By_Its_Shortest_Gap()
    {
        // a rule naming three days has gaps of one day and of five; the floor cares about the short one, which is
        // why this is measured across several occurrences rather than between the first two
        var shortest = Recurrence.ShortestInterval("FREQ=WEEKLY;BYDAY=MO,TU,SA", Anchor, "Etc/UTC");

        Assert.Equal(TimeSpan.FromDays(1), shortest);
    }

    [Theory]
    [InlineData("FREQ=SECONDLY")]
    [InlineData("FREQ=MINUTELY;INTERVAL=5")]
    [InlineData("freq=minutely")]
    public void A_Rule_That_Would_Search_Constantly_Is_Refused_Outright(string rule)
    {
        // refused by name rather than left to the interval floor: the floor is a number an operator can lower, and
        // these are never a reasonable thing to ask a peer-to-peer network for
        Assert.False(Recurrence.TryValidate(rule, out var error));
        Assert.Contains("hourly", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("FREQ=NONSENSE")]
    public void A_Rule_That_Cannot_Be_Read_Is_Refused(string rule)
    {
        Assert.False(Recurrence.TryValidate(rule, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("FREQ=DAILY;BYHOUR=3")]
    [InlineData("FREQ=HOURLY;INTERVAL=6")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,TH;BYHOUR=9")]
    public void A_Rule_The_Presets_Produce_Is_Accepted(string rule)
    {
        Assert.True(Recurrence.TryValidate(rule, out _));
        Assert.NotNull(Recurrence.Next(rule, Anchor, "Europe/Oslo", DateTime.UtcNow));
    }

    [Theory]
    [InlineData("Europe/Oslo", true)]
    [InlineData("Etc/UTC", true)]
    [InlineData("Mars/Olympus_Mons", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_Time_Zone_Is_Checked_Against_The_System(string zone, bool known)
    {
        Assert.Equal(known, Recurrence.IsKnownTimeZone(zone));
    }
}

public class WatchMatchTests
{
    [Fact]
    public void Only_Files_Passing_The_Filter_Are_Matched()
    {
        var search = SearchWith(Response("someone", freeSlot: true,
            File("Artist/track.flac", bitDepth: 16, sampleRate: 44100),
            File("Artist/track.mp3", bitRate: 320)));

        var matches = WatchService.Matches(Watch(filter: "islossless"), search);

        Assert.Single(matches);
        Assert.EndsWith(".flac", matches[0].Filename);
    }

    [Fact]
    public void Locked_Files_Are_Left_Out_Unless_Asked_For()
    {
        var response = new Response
        {
            Username = "someone",
            HasFreeUploadSlot = true,
            FileCount = 1,
            Files = [File("Artist/open.flac", bitDepth: 16, sampleRate: 44100)],
            LockedFileCount = 1,
            LockedFiles = [File("Artist/locked.flac", bitDepth: 16, sampleRate: 44100)],
        };

        Assert.Single(WatchService.Matches(Watch(), SearchWith(response)));
        Assert.Equal(2, WatchService.Matches(Watch(includeLocked: true), SearchWith(response)).Count);
    }

    [Fact]
    public void A_Peer_With_No_Free_Slot_Is_Skipped_When_That_Is_Asked_For()
    {
        var search = SearchWith(Response("busy", freeSlot: false, File("Artist/track.flac")));

        Assert.Single(WatchService.Matches(Watch(), search));
        Assert.Empty(WatchService.Matches(Watch(requireFreeSlot: true), search));
    }

    [Fact]
    public void A_Search_With_No_Responses_Matches_Nothing()
    {
        Assert.Empty(WatchService.Matches(Watch(), new slskd.Search.Search { SearchText = "x" }));
    }

    private static Watch Watch(string filter = "", bool includeLocked = false, bool requireFreeSlot = false)
        => new() { SearchId = Guid.NewGuid(), Filter = filter, IncludeLocked = includeLocked, RequireFreeSlot = requireFreeSlot };

    private static slskd.Search.Search SearchWith(params Response[] responses)
        => new() { SearchText = "x", Responses = responses };

    private static Response Response(string username, bool freeSlot, params slskd.Search.File[] files)
        => new() { Username = username, HasFreeUploadSlot = freeSlot, FileCount = files.Length, Files = files };

    private static slskd.Search.File File(string filename, int? bitRate = null, int? bitDepth = null, int? sampleRate = null)
        => new() { Filename = filename, Size = 1_000_000, BitRate = bitRate, BitDepth = bitDepth, SampleRate = sampleRate };
}
