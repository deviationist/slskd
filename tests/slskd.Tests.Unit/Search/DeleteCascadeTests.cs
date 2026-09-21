namespace slskd.Tests.Unit.Search;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using slskd.Search;
using slskd.Search.Watches;
using Xunit;

/// <summary>
///     Tests for what deleting a search takes with it.
/// </summary>
/// <remarks>
///     Against a real SQLite database: the thing being tested is that four tables in one database are cleared
///     together, which a mocked context would assert about itself rather than about the schema.
/// </remarks>
public class DeleteCascadeTests
{
    [Fact]
    public async Task Deleting_A_Search_Takes_Its_Watch_With_It()
    {
        // the whole bug: nothing cascaded, so the schedule outlived the thing it was scheduled against
        var fixture = new Fixture();
        var id = fixture.InsertSearch();

        fixture.InsertWatch(id);

        Assert.Equal(1, fixture.Count("Watches"));

        await WatchService.DeleteRowsAsync(fixture.Context(), id);

        Assert.Equal(0, fixture.Count("Watches"));
        Assert.Equal(0, fixture.Count("WatchFiles"));
        Assert.Equal(0, fixture.Count("WatchRuns"));
        Assert.Equal(0, fixture.Count("WatchNotifications"));
    }

    [Fact]
    public async Task Everything_A_Watch_Remembered_Goes_With_It()
    {
        // the files it has already reported, the runs and the notifications: left behind, they are rows about a
        // search that does not exist, and the files would suppress reports if the id were ever reused
        var fixture = new Fixture();
        var id = fixture.InsertSearch();

        fixture.InsertWatch(id);
        fixture.InsertWatchFile(id);
        fixture.InsertWatchRun(id);
        fixture.InsertWatchNotification(id);

        await WatchService.DeleteRowsAsync(fixture.Context(), id);

        Assert.Equal(0, fixture.Count("WatchFiles"));
        Assert.Equal(0, fixture.Count("WatchRuns"));
        Assert.Equal(0, fixture.Count("WatchNotifications"));
    }

    [Fact]
    public async Task Another_Search_Keeps_Its_Watch()
    {
        var fixture = new Fixture();
        var doomed = fixture.InsertSearch();
        var kept = fixture.InsertSearch();

        fixture.InsertWatch(doomed);
        fixture.InsertWatch(kept);

        await WatchService.DeleteRowsAsync(fixture.Context(), doomed);

        Assert.Equal(1, fixture.Count("Watches"));
    }

    [Fact]
    public async Task Deleting_A_Search_With_No_Watch_Is_Not_An_Error()
    {
        var fixture = new Fixture();
        var id = fixture.InsertSearch();

        await WatchService.DeleteRowsAsync(fixture.Context(), id);

        Assert.Equal(0, fixture.Count("Watches"));
    }

    private class Fixture
    {
        private readonly SqliteConnection anchor;
        private readonly DbContextOptions<SearchDbContext> options;

        public Fixture()
        {
            var connectionString = $"Data Source=cascade_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

            // keep the shared-cache in-memory database alive for the life of this fixture
            anchor = new SqliteConnection(connectionString);
            anchor.Open();

            options = new DbContextOptionsBuilder<SearchDbContext>()
                .UseSqlite(connectionString)
                .Options;

            using var context = new SearchDbContext(options);
            context.Database.EnsureCreated();
        }

        public SearchDbContext Context() => new(options);

        public int Count(string table)
        {
            using var connection = new SqliteConnection(anchor.ConnectionString);

            connection.Open();

            using var command = connection.CreateCommand();

            command.CommandText = $"SELECT COUNT(*) FROM {table}";

            return Convert.ToInt32(command.ExecuteScalar());
        }

        public Guid InsertSearch()
        {
            using var context = Context();
            var id = Guid.NewGuid();

            context.Searches.Add(new slskd.Search.Search
            {
                Id = id,
                SearchText = "aphex twin",
                Token = Random.Shared.Next(),
            });

            context.SaveChanges();

            return id;
        }

        public void InsertWatch(Guid searchId)
        {
            using var context = Context();

            context.Watches.Add(new Watch
            {
                SearchId = searchId,
                Rrule = "FREQ=DAILY",
                TimeZone = "Etc/UTC",
                Enabled = true,
            });

            context.SaveChanges();
        }

        public void InsertWatchFile(Guid searchId)
        {
            using var context = Context();

            context.WatchFiles.Add(new WatchFile
            {
                SearchId = searchId,
                Username = "alice",
                Filename = "@@a\\track.mp3",
            });

            context.SaveChanges();
        }

        public void InsertWatchRun(Guid searchId)
        {
            using var context = Context();

            context.WatchRuns.Add(new WatchRun
            {
                SearchId = searchId,
                Outcome = WatchRunOutcome.Completed,
            });

            context.SaveChanges();
        }

        public void InsertWatchNotification(Guid searchId)
        {
            using var context = Context();

            context.WatchNotifications.Add(new WatchNotification
            {
                SearchId = searchId,
                SentAt = DateTime.UtcNow,
            });

            context.SaveChanges();
        }
    }
}
