// <copyright file="Z2026_10_29_ShareLinkRecipientsMigration.cs" company="JP Dillingham">
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
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Serilog;

/// <summary>
///     Creates the table of addresses shares have been mailed to, in the Transfers database, and fills it from the
///     links that are still there.
/// </summary>
/// <remarks>
///     Filled from what is left rather than started empty, so that suggestions work from the first share after the
///     upgrade. Shares that have already expired took their addresses with them; that is what this table is for.
/// </remarks>
public class Z2026_10_29_ShareLinkRecipientsMigration : IMigration
{
    public Z2026_10_29_ShareLinkRecipientsMigration(ConnectionStringDictionary connectionStrings)
    {
        ConnectionString = connectionStrings[Database.Transfers];
    }

    private ILogger Log { get; } = Serilog.Log.ForContext<Z2026_10_29_ShareLinkRecipientsMigration>();
    private string ConnectionString { get; }

    public bool NeedsToBeApplied()
    {
        var schema = SchemaInspector.GetDatabaseSchema(ConnectionString);

        return !schema.ContainsKey("ShareLinkRecipients");
    }

    public void Apply()
    {
        if (!NeedsToBeApplied())
        {
            Log.Information("> Migration {Name} is not necessary or has already been applied", nameof(Z2026_10_29_ShareLinkRecipientsMigration));
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

            Log.Information("> Creating the share link recipients table...");

            Exec(@"
            CREATE TABLE IF NOT EXISTS ShareLinkRecipients (
                Key TEXT NOT NULL CONSTRAINT PK_ShareLinkRecipients PRIMARY KEY,
                Address TEXT NOT NULL,
                FirstSentAt TEXT NOT NULL,
                LastSentAt TEXT NOT NULL,
                SendCount INTEGER NOT NULL
            );");

            // grouped here rather than in SQL: SQLite's lower() folds ASCII alone, and the key has to be the one the
            // service makes, with ToLowerInvariant, or an address with an accent in it would be remembered twice
            var found = new Dictionary<string, (string Address, string First, string Last, int Count)>();

            using (var select = new SqliteCommand(
                "SELECT Recipient, SentAt FROM ShareLinkAccesses WHERE Recipient IS NOT NULL AND SentAt IS NOT NULL ORDER BY SentAt",
                connection,
                transaction))
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    var address = reader.GetString(0);
                    var sentAt = reader.GetString(1);
                    var key = address.ToLowerInvariant();

                    // in order of sending, so the last spelling and time seen are the latest
                    found[key] = found.TryGetValue(key, out var seen)
                        ? (address, seen.First, sentAt, seen.Count + 1)
                        : (address, sentAt, sentAt, 1);
                }
            }

            foreach (var (key, recipient) in found)
            {
                using var insert = new SqliteCommand(
                    "INSERT OR IGNORE INTO ShareLinkRecipients (Key, Address, FirstSentAt, LastSentAt, SendCount) VALUES ($key, $address, $first, $last, $count)",
                    connection,
                    transaction);

                insert.Parameters.AddWithValue("$key", key);
                insert.Parameters.AddWithValue("$address", recipient.Address);
                insert.Parameters.AddWithValue("$first", recipient.First);
                insert.Parameters.AddWithValue("$last", recipient.Last);
                insert.Parameters.AddWithValue("$count", recipient.Count);
                insert.ExecuteNonQuery();
            }

            Log.Information("> Remembered {Count} address(es) from existing share links", found.Count);

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
