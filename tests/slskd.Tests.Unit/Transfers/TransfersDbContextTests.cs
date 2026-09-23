namespace slskd.Tests.Unit.Transfers;

using System;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using slskd.Transfers;
using Soulseek;
using Xunit;
using Transfer = slskd.Transfers.Transfer;

public class TransfersDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<TransfersDbContext> _options;

    public TransfersDbContextTests()
    {
        // one open connection is the database: an in-memory SQLite database lives exactly as long as it does
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<TransfersDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new TransfersDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void Reads_Every_Timestamp_Back_As_Utc()
    {
        // the API serializes a Utc DateTime with a trailing Z and an Unspecified one without, and a browser reads
        // the latter as local time. if some of these come back Utc and some do not, subtracting one from another
        // in the browser -- how long a download waited, say -- is off by the reader's offset from UTC
        var at = new DateTime(2026, 9, 17, 0, 0, 49, DateTimeKind.Utc);
        var id = Guid.NewGuid();

        using (var context = new TransfersDbContext(_options))
        {
            context.Transfers.Add(new Transfer
            {
                Id = id,
                Username = "alice",
                Direction = TransferDirection.Download,
                Filename = "@@a\\track.mp3",
                State = TransferStates.Completed | TransferStates.Succeeded,
                RequestedAt = at,
                EnqueuedAt = at.AddSeconds(1),
                StartedAt = at.AddSeconds(2),
                EndedAt = at.AddSeconds(3),
                NextAttemptAt = at.AddSeconds(4),
            });

            context.SaveChanges();
        }

        // a fresh context, so what is asserted is what came out of the database rather than the tracked instance
        using (var context = new TransfersDbContext(_options))
        {
            var transfer = context.Transfers.Find(id);

            Assert.Equal(DateTimeKind.Utc, transfer.RequestedAt.Kind);
            Assert.Equal(DateTimeKind.Utc, transfer.EnqueuedAt.Value.Kind);
            Assert.Equal(DateTimeKind.Utc, transfer.StartedAt.Value.Kind);
            Assert.Equal(DateTimeKind.Utc, transfer.EndedAt.Value.Kind);
            Assert.Equal(DateTimeKind.Utc, transfer.NextAttemptAt.Value.Kind);

            // the kind is stamped, not converted: the instant itself must not move
            Assert.Equal(at, transfer.RequestedAt);
            Assert.Equal(at.AddSeconds(4), transfer.NextAttemptAt.Value);
        }
    }

    [Fact]
    public void Leaves_Unset_Timestamps_Null()
    {
        var id = Guid.NewGuid();

        using (var context = new TransfersDbContext(_options))
        {
            context.Transfers.Add(new Transfer
            {
                Id = id,
                Username = "alice",
                Direction = TransferDirection.Download,
                Filename = "@@a\\track.mp3",
                State = TransferStates.Queued | TransferStates.Remotely,
                RequestedAt = DateTime.UtcNow,
            });

            context.SaveChanges();
        }

        using (var context = new TransfersDbContext(_options))
        {
            var transfer = context.Transfers.Find(id);

            Assert.Null(transfer.EnqueuedAt);
            Assert.Null(transfer.StartedAt);
            Assert.Null(transfer.EndedAt);
            Assert.Null(transfer.NextAttemptAt);
        }
    }
}
