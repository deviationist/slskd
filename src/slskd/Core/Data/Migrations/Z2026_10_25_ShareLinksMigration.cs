// <copyright file="Z2026_10_25_ShareLinksMigration.cs" company="JP Dillingham">
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
///     Creates the tables share links need, in the Transfers database.
/// </summary>
/// <remarks>
///     A migration rather than a model change alone: EnsureCreated does nothing to a database that already exists, so
///     adding a DbSet creates the tables for new installations only and leaves every existing one without them.
/// </remarks>
public class Z2026_10_25_ShareLinksMigration : IMigration
{
    public Z2026_10_25_ShareLinksMigration(ConnectionStringDictionary connectionStrings)
    {
        ConnectionString = connectionStrings[Database.Transfers];
    }

    private ILogger Log { get; } = Serilog.Log.ForContext<Z2026_10_25_ShareLinksMigration>();
    private string ConnectionString { get; }

    public bool NeedsToBeApplied()
    {
        var schema = SchemaInspector.GetDatabaseSchema(ConnectionString);

        return !schema.ContainsKey("ShareLinks")
            || !schema.ContainsKey("ShareLinkFiles")
            || !schema.ContainsKey("ShareLinkAccesses");
    }

    public void Apply()
    {
        if (!NeedsToBeApplied())
        {
            Log.Information("> Migration {Name} is not necessary or has already been applied", nameof(Z2026_10_25_ShareLinksMigration));
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

            Log.Information("> Creating the share link tables...");

            Exec(@"
            CREATE TABLE IF NOT EXISTS ShareLinks (
                Id TEXT NOT NULL CONSTRAINT PK_ShareLinks PRIMARY KEY,
                Title TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL
            );");

            Exec(@"CREATE INDEX IF NOT EXISTS IDX_ShareLinks_ExpiresAt ON ShareLinks (ExpiresAt);");

            Exec(@"
            CREATE TABLE IF NOT EXISTS ShareLinkFiles (
                Id TEXT NOT NULL CONSTRAINT PK_ShareLinkFiles PRIMARY KEY,
                ShareLinkId TEXT NOT NULL,
                Position INTEGER NOT NULL,
                Name TEXT NOT NULL,
                Path TEXT NOT NULL,
                Size INTEGER NOT NULL,
                TransferId TEXT,
                Username TEXT,
                CONSTRAINT FK_ShareLinkFiles_ShareLinks_ShareLinkId FOREIGN KEY (ShareLinkId) REFERENCES ShareLinks (Id) ON DELETE CASCADE
            );");

            Exec(@"CREATE INDEX IF NOT EXISTS IDX_ShareLinkFiles_ShareLinkId ON ShareLinkFiles (ShareLinkId);");

            Exec(@"
            CREATE TABLE IF NOT EXISTS ShareLinkAccesses (
                Id TEXT NOT NULL CONSTRAINT PK_ShareLinkAccesses PRIMARY KEY,
                ShareLinkId TEXT NOT NULL,
                Token TEXT NOT NULL,
                Recipient TEXT,
                CreatedAt TEXT NOT NULL,
                SentAt TEXT,
                MailError TEXT,
                RevokedAt TEXT,
                DownloadCount INTEGER NOT NULL,
                LastDownloadedAt TEXT,
                CONSTRAINT FK_ShareLinkAccesses_ShareLinks_ShareLinkId FOREIGN KEY (ShareLinkId) REFERENCES ShareLinks (Id) ON DELETE CASCADE
            );");

            Exec(@"CREATE UNIQUE INDEX IF NOT EXISTS IDX_ShareLinkAccesses_Token ON ShareLinkAccesses (Token);");
            Exec(@"CREATE INDEX IF NOT EXISTS IDX_ShareLinkAccesses_ShareLinkId ON ShareLinkAccesses (ShareLinkId);");

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
