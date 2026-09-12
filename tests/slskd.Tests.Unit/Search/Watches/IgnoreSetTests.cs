// <copyright file="IgnoreSetTests.cs" company="JP Dillingham">
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
using slskd.Search.Watches;
using Xunit;

public class IgnoreSetTests
{
    [Theory]
    [InlineData(@"@@weeyv\complete\Aphex Twin\01 Xtal.flac", "01 Xtal.flac")]
    [InlineData("/home/someone/music/01 Xtal.flac", "01 Xtal.flac")]
    [InlineData("01 Xtal.flac", "01 Xtal.flac")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void A_Name_Is_Cut_From_Either_Kind_Of_Path(string path, string expected)
    {
        // peers send Windows paths and a few send unix ones; a name that matched only one separator would ignore a
        // file from some peers and not from others, which reads as the ignore simply not working
        Assert.Equal(expected, IgnoreSet.NameOf(path));
    }

    [Fact]
    public void A_Name_Is_Ignored_Whatever_Path_Or_Peer_It_Arrives_From()
    {
        // the whole point of matching the name: one release sits on dozens of peers under dozens of paths, and each
        // copy is a different file to a watch's memory
        var set = new IgnoreSet([new Ignore { Kind = IgnoreKind.Name, Value = "01 Xtal.flac" }]);

        Assert.True(set.Ignores("someone", @"music\Aphex Twin\01 Xtal.flac"));
        Assert.True(set.Ignores("someone-else", @"D:\shared\rips\01 Xtal.flac"));
        Assert.False(set.Ignores("someone", @"music\Aphex Twin\02 Tha.flac"));
    }

    [Fact]
    public void A_Peer_Is_Ignored_Whatever_They_Offer()
    {
        var set = new IgnoreSet([new Ignore { Kind = IgnoreKind.User, Value = "transcoder" }]);

        Assert.True(set.Ignores("transcoder", "anything.flac"));
        Assert.False(set.Ignores("someone", "anything.flac"));
    }

    [Fact]
    public void Case_Does_Not_Decide_It()
    {
        // the network is full of the same release spelled several ways, and an ignore that missed on capitalisation
        // would look like it had not been saved
        var set = new IgnoreSet(
        [
            new Ignore { Kind = IgnoreKind.Name, Value = "01 XTAL.FLAC" },
            new Ignore { Kind = IgnoreKind.User, Value = "Transcoder" },
        ]);

        Assert.True(set.Ignores("nobody", @"x\01 xtal.flac"));
        Assert.True(set.Ignores("TRANSCODER", "anything.flac"));
    }

    [Fact]
    public void An_Empty_Set_Ignores_Nothing()
    {
        var set = new IgnoreSet([]);

        Assert.True(set.IsEmpty);
        Assert.False(set.Ignores("someone", "anything.flac"));
    }

    [Fact]
    public void An_Ignore_With_No_Value_Is_Not_A_Rule_That_Matches_Everything()
    {
        // a blank value must not become a set containing the empty string, which a missing username or filename
        // would then match -- one bad row silencing every watch
        var set = new IgnoreSet(
        [
            new Ignore { Kind = IgnoreKind.Name, Value = null },
            new Ignore { Kind = IgnoreKind.User, Value = "   " },
        ]);

        Assert.True(set.IsEmpty);
        Assert.False(set.Ignores(null, null));
        Assert.False(set.Ignores("someone", "anything.flac"));
    }
}
