// <copyright file="Z2026_09_12_WatchAutoDownloadMigration.cs" company="JP Dillingham">
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
///     Adds the auto-download option to Watches, and somewhere for a run to record what it queued.
/// </summary>
public class Z2026_09_12_WatchAutoDownloadMigration : IMigration
{
    public Z2026_09_12_WatchAutoDownloadMigration(ConnectionStringDictionary connectionStrings)
    {
        ConnectionString = connectionStrings[Database.Search];
    }

    private ILogger Log { get; } = Serilog.Log.ForContext<Z2026_09_12_WatchAutoDownloadMigration>();
    private string ConnectionString { get; }

    public bool NeedsToBeApplied()
    {
        var schema = SchemaInspector.GetDatabaseSchema(ConnectionString);

        return Missing(schema, "Watches", "AutoDownload") || Missing(schema, "WatchRuns", "EnqueuedCount");
    }

    public void Apply()
    {
        if (!NeedsToBeApplied())
        {
            Log.Information("> Migration {Name} is not necessary or has already been applied", nameof(Z2026_09_12_WatchAutoDownloadMigration));
            return;
        }

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            Add(connection, transaction, "ALTER TABLE Watches ADD COLUMN AutoDownload INTEGER NOT NULL DEFAULT 0;");
            Add(connection, transaction, "ALTER TABLE WatchRuns ADD COLUMN EnqueuedCount INTEGER NOT NULL DEFAULT 0;");

            transaction.Commit();
            Log.Information("> Done!");
        }
        catch (Exception)
        {
            transaction.Rollback();
            throw;
        }
    }

    private static bool Missing(System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<SchemaInspector.ColumnInfo>> schema, string table, string column)
        => !schema.TryGetValue(table, out var columns)
        || !columns.Any(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase));

    private void Add(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = new SqliteCommand(sql, connection, transaction);

        try
        {
            Log.Information("> {Sql}", sql);
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // one of the two may already be there; this has to be safe against a database that has had half of it
            Log.Information("> already applied");
        }
    }
}
