// <copyright file="SearchDbContextTests.cs" company="JP Dillingham">
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
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using slskd.Search;
using slskd.Search.Watches;
using Xunit;

public class SearchDbContextTests : IDisposable
{
    private readonly SearchDbContext context;

    public SearchDbContextTests()
    {
        // a real SQLite file, not the in-memory provider: the bug is SQLite's own lack of a date type, and the
        // in-memory provider keeps the CLR value verbatim and so cannot reproduce it
        var options = new DbContextOptionsBuilder<SearchDbContext>()
            .UseSqlite($"Data Source={Path}")
            .Options;

        context = new SearchDbContext(options);
        context.Database.EnsureCreated();
    }

    private string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"slskd-utc-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        context.Dispose();
        System.IO.File.Delete(Path);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Watch_timestamps_come_back_as_utc()
    {
        // SQLite keeps no DateTimeKind. Without a converter these read back Unspecified, and are then serialised
        // with no trailing Z -- which a browser reads as local time, drawing a watch hours away from when it runs.
        var id = Guid.NewGuid();
        var at = new DateTime(2026, 9, 14, 1, 0, 21, DateTimeKind.Utc);

        context.Watches.Add(new Watch
        {
            SearchId = id,
            Enabled = true,
            Rrule = "FREQ=DAILY;BYHOUR=3;BYMINUTE=0",
            TimeZone = "Europe/Oslo",
            Anchor = at,
            NextRunAt = at,
            CreatedAt = at,
            UpdatedAt = at,
        });

        context.SaveChanges();
        context.ChangeTracker.Clear();

        var read = context.Watches.AsNoTracking().Single(w => w.SearchId == id);

        Assert.Equal(DateTimeKind.Utc, read.Anchor.Kind);
        Assert.Equal(DateTimeKind.Utc, read.NextRunAt.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, read.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, read.UpdatedAt.Kind);
    }

    [Fact]
    public void Watch_timestamps_serialise_with_a_zone()
    {
        // the assertion that matches what the browser actually receives: no Z means local time to `new Date(...)`
        var id = Guid.NewGuid();
        var at = new DateTime(2026, 9, 14, 1, 0, 21, DateTimeKind.Utc);

        context.Watches.Add(new Watch
        {
            SearchId = id,
            Enabled = true,
            Rrule = "FREQ=DAILY;BYHOUR=3;BYMINUTE=0",
            TimeZone = "Europe/Oslo",
            Anchor = at,
            NextRunAt = at,
            CreatedAt = at,
            UpdatedAt = at,
        });

        context.SaveChanges();
        context.ChangeTracker.Clear();

        var read = context.Watches.AsNoTracking().Single(w => w.SearchId == id);
        var json = JsonSerializer.Serialize(read);

        Assert.Contains("2026-09-14T01:00:21Z", json);
    }
}
