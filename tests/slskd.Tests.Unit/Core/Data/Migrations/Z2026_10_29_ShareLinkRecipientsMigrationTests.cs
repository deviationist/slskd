// <copyright file="Z2026_10_29_ShareLinkRecipientsMigrationTests.cs" company="JP Dillingham">
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

namespace slskd.Tests.Unit.Core.Data.Migrations;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using slskd.Migrations;
using slskd.Transfers;
using slskd.Transfers.ShareLinks;
using Xunit;

public sealed class Z2026_10_29_ShareLinkRecipientsMigrationTests : IDisposable
{
    private readonly string connectionString = $"Data Source=recipients_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection anchor;
    private readonly DbContextOptions<TransfersDbContext> dbOptions;

    public Z2026_10_29_ShareLinkRecipientsMigrationTests()
    {
        // keep the shared-cache in-memory database alive for the life of the test
        anchor = new SqliteConnection(connectionString);
        anchor.Open();

        dbOptions = new DbContextOptionsBuilder<TransfersDbContext>().UseSqlite(connectionString).Options;

        // a database from before the table: everything the model makes, less the one this migration adds
        using var context = new TransfersDbContext(dbOptions);
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw("DROP TABLE ShareLinkRecipients");
    }

    public void Dispose() => anchor.Dispose();

    [Fact]
    public void Remembers_Every_Address_A_Link_Was_Delivered_To_Once_Ignoring_Case()
    {
        var earlier = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var later = earlier.AddDays(3);

        Seed(
            ("kim@example.com", earlier),
            ("KIM@example.com", later),
            ("lee@example.com", earlier),
            ("never@example.com", null),
            (null, earlier));

        var migration = Migration();
        Assert.True(migration.NeedsToBeApplied());

        migration.Apply();

        Assert.False(migration.NeedsToBeApplied());

        using var context = new TransfersDbContext(dbOptions);
        var recipients = context.ShareLinkRecipients.OrderBy(r => r.Key).ToList();

        Assert.Equal(["kim@example.com", "lee@example.com"], recipients.Select(r => r.Key));

        var kim = recipients[0];
        Assert.Equal("KIM@example.com", kim.Address);
        Assert.Equal(2, kim.SendCount);
        Assert.Equal(earlier, kim.FirstSentAt);
        Assert.Equal(later, kim.LastSentAt);
        Assert.Equal(DateTimeKind.Utc, kim.LastSentAt.Kind);
    }

    [Fact]
    public void Is_Not_Needed_Where_The_Table_Exists()
    {
        using (var context = new TransfersDbContext(dbOptions))
        {
            context.Database.ExecuteSqlRaw("CREATE TABLE ShareLinkRecipients (Key TEXT NOT NULL PRIMARY KEY)");
        }

        Assert.False(Migration().NeedsToBeApplied());
    }

    private Z2026_10_29_ShareLinkRecipientsMigration Migration() =>
        new(new ConnectionStringDictionary(new Dictionary<Database, ConnectionString> { [Database.Transfers] = connectionString }));

    private void Seed(params (string Recipient, DateTime? SentAt)[] accesses)
    {
        using var context = new TransfersDbContext(dbOptions);

        var link = new ShareLink
        {
            Id = Guid.NewGuid(),
            Title = "x",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            Files = [],
            Accesses = [.. accesses.Select(a => new ShareLinkAccess
            {
                Id = Guid.NewGuid(),
                Token = Guid.NewGuid().ToString("N"),
                Recipient = a.Recipient,
                CreatedAt = DateTime.UtcNow,
                SentAt = a.SentAt,
            })],
        };

        context.ShareLinks.Add(link);
        context.SaveChanges();
    }
}
