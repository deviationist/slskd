// <copyright file="SeedingTests.cs" company="JP Dillingham">
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

using slskd.Search.Watches;
using Xunit;

public class SeedingTests
{
    [Fact]
    public void Waits_when_the_search_has_not_finished()
    {
        // the ordinary path through the searches page: the search is started and the watch written in the same
        // breath, so seeding now would read a search that has found nothing and record nothing
        Assert.True(Watch.SeedingMustWait(isNew: true, seedRequested: true, searchIsComplete: false));
    }

    [Fact]
    public void Does_not_wait_when_the_search_already_has_results()
    {
        // watching a search that is already on screen: seeding reads the results and works immediately
        Assert.False(Watch.SeedingMustWait(isNew: true, seedRequested: true, searchIsComplete: true));
    }

    [Fact]
    public void Does_not_wait_when_seeding_was_not_asked_for()
    {
        Assert.False(Watch.SeedingMustWait(isNew: true, seedRequested: false, searchIsComplete: false));
    }

    [Fact]
    public void Never_waits_for_a_watch_that_already_exists()
    {
        // an edit must not arrange a seeding: the watch has a memory by now, and seeding it would mark files as
        // already reported that it has never reported
        Assert.False(Watch.SeedingMustWait(isNew: false, seedRequested: true, searchIsComplete: false));
        Assert.False(Watch.SeedingMustWait(isNew: false, seedRequested: true, searchIsComplete: true));
    }
}
