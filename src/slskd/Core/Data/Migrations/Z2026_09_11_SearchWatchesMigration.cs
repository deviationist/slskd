// <copyright file="Z2026_09_11_SearchWatchesMigration.cs" company="JP Dillingham">
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
namespace slskd.Migrations;

using System;
using Microsoft.Data.Sqlite;
using Serilog;

/// <summary>
///     Creates the tables a search watch needs, in the Search database.
/// </summary>
/// <remarks>
///     A migration rather than a model change alone: EnsureCreated does nothing to a database that already exists, so
///     adding a DbSet creates the table for new installations only and leaves every existing one without it.
/// </remarks>
public class Z2026_09_11_SearchWatchesMigration : IMigration
{
    public Z2026_09_11_SearchWatchesMigration(ConnectionStringDictionary connectionStrings)
    {
        ConnectionString = connectionStrings[Database.Search];
    }

    private ILogger Log { get; } = Serilog.Log.ForContext<Z2026_09_11_SearchWatchesMigration>();
    private string ConnectionString { get; }

    public bool NeedsToBeApplied()
    {
        var schema = SchemaInspector.GetDatabaseSchema(ConnectionString);

        // all four are created together, so the presence of any one of them means this has run. checking all four
        // anyway costs nothing and covers a migration interrupted between statements, which the transaction should
        // prevent but which a restored backup could still present
        return !schema.ContainsKey("Watches")
            || !schema.ContainsKey("WatchFiles")
            || !schema.ContainsKey("WatchRuns")
            || !schema.ContainsKey("WatchNotifications");
    }

    public void Apply()
    {
        if (!NeedsToBeApplied())
        {
            Log.Information("> Migration {Name} is not necessary or has already been applied", nameof(Z2026_09_11_SearchWatchesMigration));
            return;
        }

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            void Exec(string sql)
            {
                using var command = new SqliteCommand(sql, connection, transaction);
                command.ExecuteNonQuery();
            }

            Log.Information("> Creating the search watch tables...");

            Exec(@"
            CREATE TABLE IF NOT EXISTS Watches (
                SearchId TEXT NOT NULL CONSTRAINT PK_Watches PRIMARY KEY,
                Enabled INTEGER NOT NULL,
                Rrule TEXT,
                TimeZone TEXT,
                Anchor TEXT NOT NULL,
                NextRunAt TEXT,
                LastRunAt TEXT,
                NotifyEmail TEXT,
                IncludeLocked INTEGER NOT NULL,
                RequireFreeSlot INTEGER NOT NULL,
                Filter TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );");

            Exec(@"
            CREATE TABLE IF NOT EXISTS WatchFiles (
                Id TEXT NOT NULL CONSTRAINT PK_WatchFiles PRIMARY KEY,
                SearchId TEXT NOT NULL,
                Username TEXT NOT NULL,
                Filename TEXT NOT NULL,
                Size INTEGER NOT NULL,
                FirstSeenAt TEXT NOT NULL,
                NotifiedAt TEXT,
                Seeded INTEGER NOT NULL
            );");

            // the memory is read once per file per run and written only for new ones; this index is what keeps a
            // watch with thousands of remembered files from costing a table scan every time it runs
            Exec(@"CREATE UNIQUE INDEX IF NOT EXISTS IDX_WatchFiles_Identity ON WatchFiles (SearchId, Username, Filename);");

            Exec(@"
            CREATE TABLE IF NOT EXISTS WatchRuns (
                Id TEXT NOT NULL CONSTRAINT PK_WatchRuns PRIMARY KEY,
                SearchId TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                EndedAt TEXT,
                ResponseCount INTEGER NOT NULL,
                FileCount INTEGER NOT NULL,
                MatchCount INTEGER NOT NULL,
                NewCount INTEGER NOT NULL,
                Outcome INTEGER NOT NULL,
                Error TEXT
            );");

            Exec(@"CREATE INDEX IF NOT EXISTS IDX_WatchRuns_SearchId_StartedAt ON WatchRuns (SearchId, StartedAt);");

            Exec(@"
            CREATE TABLE IF NOT EXISTS WatchNotifications (
                Id TEXT NOT NULL CONSTRAINT PK_WatchNotifications PRIMARY KEY,
                SearchId TEXT NOT NULL,
                SentAt TEXT NOT NULL,
                Adapter TEXT,
                Recipient TEXT,
                Subject TEXT,
                FileCount INTEGER NOT NULL,
                FilesJson TEXT,
                Sent INTEGER NOT NULL,
                Error TEXT
            );");

            Exec(@"CREATE INDEX IF NOT EXISTS IDX_WatchNotifications_SearchId_SentAt ON WatchNotifications (SearchId, SentAt);");

            transaction.Commit();
            Log.Information("> Done!");
        }
        catch (Exception)
        {
            transaction.Rollback();
            throw;
        }
    }
}
