// <copyright file="SearchDbContext.cs" company="JP Dillingham">
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

namespace slskd.Search
{
    using System;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

    public class SearchDbContext : DbContext
    {
        public SearchDbContext(DbContextOptions<SearchDbContext> options)
            : base(options)
        {
        }

        public DbSet<Search> Searches { get; set; }

        public DbSet<slskd.Search.Watches.Watch> Watches { get; set; }

        public DbSet<slskd.Search.Watches.WatchFile> WatchFiles { get; set; }

        public DbSet<slskd.Search.Watches.WatchRun> WatchRuns { get; set; }

        public DbSet<slskd.Search.Watches.WatchNotification> WatchNotifications { get; set; }

        public DbSet<slskd.Search.Watches.Ignore> Ignores { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<Search>()
                .Property(e => e.StartedAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            modelBuilder
                .Entity<slskd.Search.Watches.WatchFile>()
                .HasIndex(e => new { e.SearchId, e.Username, e.Filename })
                .IsUnique();

            modelBuilder
                .Entity<slskd.Search.Watches.WatchRun>()
                .HasIndex(e => new { e.SearchId, e.StartedAt });

            modelBuilder
                .Entity<slskd.Search.Watches.WatchNotification>()
                .HasIndex(e => new { e.SearchId, e.SentAt });

            modelBuilder
                .Entity<Search>()
                .Property(e => e.EndedAt)
                .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

            // SQLite has no date type and keeps no DateTimeKind, so everything written as UTC comes back
            // Unspecified -- and System.Text.Json then serialises it with no trailing Z. A browser reads that as
            // *local* time, so a watch due 01:00Z was drawn two hours early in Oslo, in the relative text as well
            // as the timestamp behind it. The two Search columns above were converted one at a time and the watch
            // tables, added later, were not; this covers every DateTime in the model so the next column added
            // cannot miss it either. Read-side only -- the stored values are already UTC and are written unchanged,
            // so there is nothing to migrate.
            ApplyUtcKind(modelBuilder);
        }

        /// <summary>
        ///     Stamps <see cref="DateTimeKind.Utc"/> on every DateTime read back from the database.
        /// </summary>
        /// <param name="modelBuilder">The model being built.</param>
        private static void ApplyUtcKind(ModelBuilder modelBuilder)
        {
            var utc = new ValueConverter<DateTime, DateTime>(
                v => v,
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            var nullableUtc = new ValueConverter<DateTime?, DateTime?>(
                v => v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime))
                    {
                        property.SetValueConverter(utc);
                    }
                    else if (property.ClrType == typeof(DateTime?))
                    {
                        property.SetValueConverter(nullableUtc);
                    }
                }
            }
        }
    }
}
