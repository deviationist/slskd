// <copyright file="DownloadFileAvailabilityTests.cs" company="JP Dillingham">
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

namespace slskd.Tests.Unit.Transfers;

using System.IO;
using slskd.Transfers;
using Xunit;

public class DownloadFileAvailabilityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Download_With_No_Recorded_Path_Is_Not_An_Answer(string filename)
    {
        // null rather than false: this application never knew where the file went, which is not the same
        // as knowing it has gone, and a row that says "missing" on a guess is worse than one that says nothing
        Assert.Null(new DownloadFileAvailability().Exists(filename));
    }

    [Fact]
    public void A_File_That_Is_There_Exists()
    {
        using var file = new TemporaryFile();

        Assert.True(new DownloadFileAvailability().Exists(file.Name));
    }

    [Fact]
    public void A_File_That_Is_Not_There_Does_Not_Exist()
    {
        Assert.False(new DownloadFileAvailability().Exists(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())));
    }

    [Fact]
    public void An_Answer_Is_Cached_Rather_Than_Asked_Of_The_Filesystem_Each_Time()
    {
        var availability = new DownloadFileAvailability();
        var file = new TemporaryFile();

        Assert.True(availability.Exists(file.Name));

        file.Dispose();

        // the point of the cache, and its cost: the transfer list is polled once a second, and this answer is
        // allowed to be up to half a minute behind the filesystem
        Assert.True(availability.Exists(file.Name));
    }

    [Fact]
    public void A_Forgotten_Answer_Is_Asked_Again()
    {
        var availability = new DownloadFileAvailability();
        var file = new TemporaryFile();

        Assert.True(availability.Exists(file.Name));

        file.Dispose();
        availability.Forget(file.Name);

        Assert.False(availability.Exists(file.Name));
    }

    [Fact]
    public void Forgetting_Nothing_Is_Not_An_Error()
    {
        var availability = new DownloadFileAvailability();

        availability.Forget(null);
        availability.Forget("   ");
        availability.Forget(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
    }

    private sealed class TemporaryFile : System.IDisposable
    {
        public TemporaryFile()
        {
            Name = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            File.WriteAllText(Name, "slskd");
        }

        public string Name { get; }

        public void Dispose()
        {
            File.Delete(Name);
        }
    }
}
