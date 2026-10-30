// <copyright file="TransfersDbContext.cs" company="JP Dillingham">
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

namespace slskd.Transfers
{
    using System;
    using System.Text.Json;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
    using slskd.Transfers.ShareLinks;

    public class TransfersDbContext : DbContext
    {
        public TransfersDbContext(DbContextOptions<TransfersDbContext> options)
            : base(options)
        {
        }

        public DbSet<Batch> Batches { get; set; }
        public DbSet<Transfer> Transfers { get; set; }
        public DbSet<ShareLink> ShareLinks { get; set; }
        public DbSet<ShareLinkFile> ShareLinkFiles { get; set; }
        public DbSet<ShareLinkAccess> ShareLinkAccesses { get; set; }
        public DbSet<ShareLinkRecipient> ShareLinkRecipients { get; set; }
        public DbSet<slskd.Player.StoredAnalysis> Analyses { get; set; }
        public DbSet<slskd.Player.DownloadAnalysis> DownloadAnalyses { get; set; }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            // this is absolutely NOT IDEAL and will accellerate the move away from EF
            // we have to do this to store both the integer and the string value in the database
            // well, we don't *HAVE* to, but storing the string allows external apps to understand transfer history
            // and not need to work out the flags. storing the integer allows us to use bitwise operations in queries,
            // which is the main driver for this.
            foreach (var entry in ChangeTracker.Entries<Transfer>())
            {
                if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    entry.Entity.StateDescription = entry.Entity.State.ToString();
#pragma warning restore CS0618 // Type or member is obsolete
                }
            }

            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureTransfers(modelBuilder);
            ConfigureBatches(modelBuilder);
            ConfigureShareLinks(modelBuilder);
            ConfigureAnalyses(modelBuilder);
        }

        private void ConfigureAnalyses(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<slskd.Player.StoredAnalysis>()
                .Property(e => e.StoredAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            // a new version of the analyser drops everything made by the others
            modelBuilder
                .Entity<slskd.Player.StoredAnalysis>()
                .HasIndex(a => a.Version)
                .HasDatabaseName("IDX_Analyses_Version");

            modelBuilder
                .Entity<slskd.Player.DownloadAnalysis>()
                .HasIndex(d => d.AnalysisId)
                .HasDatabaseName("IDX_DownloadAnalyses_AnalysisId");
        }

        private void ConfigureShareLinks(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<ShareLink>()
                .Property(e => e.CreatedAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            modelBuilder
                .Entity<ShareLink>()
                .Property(e => e.ExpiresAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            modelBuilder
                .Entity<ShareLink>()
                .HasMany(l => l.Files)
                .WithOne()
                .HasForeignKey(f => f.ShareLinkId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder
                .Entity<ShareLink>()
                .HasMany(l => l.Accesses)
                .WithOne()
                .HasForeignKey(a => a.ShareLinkId)
                .OnDelete(DeleteBehavior.Cascade);

            // the sweep starts with this lookup
            modelBuilder
                .Entity<ShareLink>()
                .HasIndex(l => l.ExpiresAt)
                .HasDatabaseName("IDX_ShareLinks_ExpiresAt");

            modelBuilder
                .Entity<ShareLinkFile>()
                .HasIndex(f => f.ShareLinkId)
                .HasDatabaseName("IDX_ShareLinkFiles_ShareLinkId");

            modelBuilder
                .Entity<ShareLinkAccess>()
                .Property(e => e.CreatedAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            modelBuilder
                .Entity<ShareLinkAccess>()
                .Property(e => e.SentAt)
                .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

            modelBuilder
                .Entity<ShareLinkAccess>()
                .Property(e => e.RevokedAt)
                .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

            modelBuilder
                .Entity<ShareLinkAccess>()
                .Property(e => e.LastDownloadedAt)
                .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

            // and every public request with this one
            modelBuilder
                .Entity<ShareLinkAccess>()
                .HasIndex(a => a.Token)
                .IsUnique()
                .HasDatabaseName("IDX_ShareLinkAccesses_Token");

            modelBuilder
                .Entity<ShareLinkAccess>()
                .HasIndex(a => a.ShareLinkId)
                .HasDatabaseName("IDX_ShareLinkAccesses_ShareLinkId");

            modelBuilder
                .Entity<ShareLinkRecipient>()
                .Property(e => e.FirstSentAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            modelBuilder
                .Entity<ShareLinkRecipient>()
                .Property(e => e.LastSentAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        }

        private void ConfigureTransfers(ModelBuilder modelBuilder)
        {
            // SQLite has no date type and keeps no DateTimeKind, so every timestamp comes back Unspecified and is
            // serialized without a zone -- which a browser reads as *local* time. every instant on a transfer is
            // written as UTC, so every one of them is read back as UTC; covering some and not others leaves the
            // API serving a mix, and the difference between two of them is then off by the reader's UTC offset.
            modelBuilder
                .Entity<Transfer>()
                .Property(e => e.RequestedAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            modelBuilder
                .Entity<Transfer>()
                .Property(e => e.EnqueuedAt)
                .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

            modelBuilder
                .Entity<Transfer>()
                .Property(e => e.StartedAt)
                .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

            modelBuilder
                .Entity<Transfer>()
                .Property(e => e.EndedAt)
                .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

            modelBuilder
                .Entity<Transfer>()
                .Property(e => e.NextAttemptAt)
                .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

            modelBuilder
                .Entity<Transfer>()
                .Property(d => d.Direction)
                .HasConversion(new EnumToStringConverter<Soulseek.TransferDirection>());

            modelBuilder
                .Entity<Transfer>()
                .HasIndex(t => t.Direction)
                .HasDatabaseName("IDX_Transfers_Direction");

            modelBuilder
                .Entity<Transfer>()
                .HasIndex(t => t.State)
                .HasDatabaseName("IDX_Transfers_State");

            modelBuilder
                .Entity<Transfer>()
                .HasIndex(t => t.Removed)
                .HasDatabaseName("IDX_Transfers_Removed");

            modelBuilder
                .Entity<Transfer>()
                .Property(e => e.Attempts)
                .HasDefaultValue(0); // force EF to match the migration

            modelBuilder
                .Entity<Transfer>()
                .HasIndex(t => t.BatchId)
                .HasDatabaseName("IDX_Transfers_BatchId");

            // covers the check for existing records when enqueueing uploads and downloads
            modelBuilder
                .Entity<Transfer>()
                .HasIndex(t => new { t.Username, t.Filename })
                .HasDatabaseName("IDX_Transfers_UsernameFilename");

            // covers the GetUserStatistics method that backs limit checks
            // check every so often with EXPLAIN to make sure it's being shown as a covering index
            modelBuilder
                .Entity<Transfer>()
                .HasIndex(e => new { e.Username, e.Direction, e.EndedAt, e.StartedAt, e.State, e.Size })
                .HasDatabaseName("IDX_Transfers_UserUploadStatistics");
        }

        private void ConfigureBatches(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<Batch>()
                .Property(e => e.CreatedAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            modelBuilder
                .Entity<Batch>()
                .HasMany(b => b.Transfers)
                .WithOne()
                .HasForeignKey(t => t.BatchId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder
                .Entity<Batch>()
                .Property(b => b.Options)
                .HasConversion(
                    convertToProviderExpression: v => JsonSerializer.Serialize(v, new JsonSerializerOptions().WithStandardOptions()),
                    convertFromProviderExpression: v => JsonSerializer.Deserialize<BatchOptions>(v, new JsonSerializerOptions().WithStandardOptions()))
                .HasColumnType("TEXT");

            modelBuilder
                .Entity<Batch>()
                .HasIndex(b => b.SearchId)
                .HasDatabaseName("IDX_Batches_SearchId");
        }
    }
}
