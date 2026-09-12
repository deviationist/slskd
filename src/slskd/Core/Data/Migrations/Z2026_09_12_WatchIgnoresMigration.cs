// <copyright file="Z2026_09_12_WatchIgnoresMigration.cs" company="JP Dillingham">
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
using System.Linq;
using Microsoft.Data.Sqlite;
using Serilog;

/// <summary>
///     Creates the Ignores table in the Search database, and gives WatchRuns somewhere to record what they removed.
/// </summary>
public class Z2026_09_12_WatchIgnoresMigration : IMigration
{
    public Z2026_09_12_WatchIgnoresMigration(ConnectionStringDictionary connectionStrings)
    {
        ConnectionString = connectionStrings[Database.Search];
    }

    private ILogger Log { get; } = Serilog.Log.ForContext<Z2026_09_12_WatchIgnoresMigration>();
    private string ConnectionString { get; }

    public bool NeedsToBeApplied()
    {
        var schema = SchemaInspector.GetDatabaseSchema(ConnectionString);

        if (!schema.ContainsKey("Ignores"))
        {
            return true;
        }

        // the column matters as much as the table: a run inserts it, so a database with one and not the other fails
        // on the next run rather than at startup
        return !schema.TryGetValue("WatchRuns", out var runs)
            || !runs.Any(column => column.Name.Equals("IgnoredCount", StringComparison.OrdinalIgnoreCase));
    }

    public void Apply()
    {
        if (!NeedsToBeApplied())
        {
            Log.Information("> Migration {Name} is not necessary or has already been applied", nameof(Z2026_09_12_WatchIgnoresMigration));
            return;
        }

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            using var command = new SqliteCommand(@"
            CREATE TABLE IF NOT EXISTS Ignores (
                Id TEXT NOT NULL CONSTRAINT PK_Ignores PRIMARY KEY,
                Kind INTEGER NOT NULL,
                Value TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                Note TEXT
            );", connection, transaction);

            Log.Information("> Creating the Ignores table...");
            command.ExecuteNonQuery();

            using var column = new SqliteCommand(
                @"ALTER TABLE WatchRuns ADD COLUMN IgnoredCount INTEGER NOT NULL DEFAULT 0;",
                connection,
                transaction);

            try
            {
                Log.Information("> Adding IgnoredCount to WatchRuns...");
                column.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
            {
                // already there; this migration has to be safe to run against a database that has had half of it
                Log.Information("> WatchRuns already has IgnoredCount");
            }

            using var index = new SqliteCommand(
                @"CREATE UNIQUE INDEX IF NOT EXISTS IDX_Ignores_Kind_Value ON Ignores (Kind, Value);",
                connection,
                transaction);

            index.ExecuteNonQuery();

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
