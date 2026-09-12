// <copyright file="AutoDownloadTests.cs" company="JP Dillingham">
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

using System.Collections.Generic;
using System.Linq;
using slskd.Search.Watches;
using Xunit;

public class AutoDownloadTests
{
    [Fact]
    public void One_Peer_Is_Chosen_Per_Filename()
    {
        // the reason this exists: the same release sits on dozens of peers, so queueing every new match would fetch
        // one track twenty times over
        var chosen = AutoDownload.Choose(
        [
            Match("alice", @"a\01 Xtal.flac"),
            Match("bob", @"b\01 Xtal.flac"),
            Match("carol", @"c\01 Xtal.flac"),
        ], limit: 10);

        Assert.Single(chosen);
    }

    [Fact]
    public void A_Free_Slot_Beats_A_Faster_Peer_With_None()
    {
        // a file is worth having from whoever will actually send it; a free slot is the difference between a
        // download and a place in a queue nobody is moving through
        var chosen = AutoDownload.Choose(
        [
            Match("fast", "x.flac", freeSlot: false, speed: 9000),
            Match("free", "x.flac", freeSlot: true, speed: 10),
        ], limit: 10);

        Assert.Equal("free", chosen.Single().Username);
    }

    [Fact]
    public void Speed_Decides_Between_Peers_That_Both_Have_A_Slot()
    {
        var chosen = AutoDownload.Choose(
        [
            Match("slow", "x.flac", freeSlot: true, speed: 10),
            Match("fast", "x.flac", freeSlot: true, speed: 9000),
        ], limit: 10);

        Assert.Equal("fast", chosen.Single().Username);
    }

    [Fact]
    public void The_Shorter_Queue_Decides_A_Tie()
    {
        var chosen = AutoDownload.Choose(
        [
            Match("backed-up", "x.flac", freeSlot: true, speed: 100, queue: 500),
            Match("clear", "x.flac", freeSlot: true, speed: 100, queue: 0),
        ], limit: 10);

        Assert.Equal("clear", chosen.Single().Username);
    }

    [Fact]
    public void No_More_Than_The_Limit_Is_Chosen()
    {
        // a watch on a broad phrase finds hundreds of new files every run, and one left on auto-download would fill
        // a disk while its operator read the mail about it
        var many = Enumerable.Range(0, 50).Select(i => Match("someone", $"{i}.flac")).ToList();

        Assert.Equal(5, AutoDownload.Choose(many, limit: 5).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_Limit_Of_None_Chooses_Nothing(int limit)
    {
        Assert.Empty(AutoDownload.Choose([Match("someone", "x.flac")], limit));
    }

    [Fact]
    public void A_Match_With_No_Filename_Is_Not_Chosen()
    {
        Assert.Empty(AutoDownload.Choose([Match("someone", null), Match("someone", "  ")], limit: 10));
    }

    [Fact]
    public void Nothing_To_Choose_From_Is_Not_An_Error()
    {
        Assert.Empty(AutoDownload.Choose(null, limit: 10));
        Assert.Empty(AutoDownload.Choose([], limit: 10));
    }

    private static WatchService.Match Match(
        string username,
        string filename,
        bool freeSlot = false,
        int speed = 0,
        long queue = 0)
        => new()
        {
            Username = username,
            Filename = filename,
            Size = 1000,
            HasFreeUploadSlot = freeSlot,
            UploadSpeed = speed,
            QueueLength = queue,
        };
}
