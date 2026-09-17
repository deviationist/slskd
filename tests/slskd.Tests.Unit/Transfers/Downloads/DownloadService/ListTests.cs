namespace slskd.Tests.Unit.Transfers.Downloads;

using System;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using slskd.Transfers;
using slskd.Transfers.Downloads;
using Soulseek;
using Xunit;

/// <summary>
///     Tests for the origin a listed download reports.
/// </summary>
/// <remarks>
///     Against a real SQLite database rather than a mocked context, because the thing most likely to go wrong
///     here is the query failing to translate: a left join that LINQ accepts and the provider refuses throws at
///     runtime, on a page that is polled, and a mocked context would never notice.
/// </remarks>
public class ListTests
{
    [Fact]
    public void Reports_The_Search_A_Download_Came_From()
    {
        var fixture = new Fixture();
        var batchId = Guid.NewGuid();
        var searchId = Guid.NewGuid();

        fixture.InsertBatch(batchId, searchId, "aphex twin");
        fixture.InsertDownload("alice", "@@a\\track.mp3", batchId);

        var listed = Assert.Single(fixture.Service.List());

        Assert.Equal(searchId, listed.SearchId);
        Assert.Equal("aphex twin", listed.SearchText);
    }

    [Fact]
    public void Says_Nothing_About_A_Download_That_Came_From_No_Search()
    {
        // a browse, a retry, or anything enqueued through the API: there is no origin to report, and the
        // absence has to read differently from "came from a search that has since gone"
        var fixture = new Fixture();

        fixture.InsertDownload("alice", "@@a\\track.mp3", batchId: null);

        var listed = Assert.Single(fixture.Service.List());

        Assert.Null(listed.SearchId);
        Assert.Null(listed.SearchText);
    }

    [Fact]
    public void Reports_A_Batch_That_Was_Not_Made_From_A_Search()
    {
        // the browse page enqueues batches too, with no search behind them
        var fixture = new Fixture();
        var batchId = Guid.NewGuid();

        fixture.InsertBatch(batchId, searchId: null, searchText: null);
        fixture.InsertDownload("alice", "@@a\\track.mp3", batchId);

        var listed = Assert.Single(fixture.Service.List());

        Assert.Null(listed.SearchId);
        Assert.Null(listed.SearchText);
    }

    [Fact]
    public void Still_Names_The_Search_When_Only_Its_Id_Is_Recorded()
    {
        // batches enqueued before the text was recorded: the link still works while the search exists
        var fixture = new Fixture();
        var batchId = Guid.NewGuid();
        var searchId = Guid.NewGuid();

        fixture.InsertBatch(batchId, searchId, searchText: null);
        fixture.InsertDownload("alice", "@@a\\track.mp3", batchId);

        var listed = Assert.Single(fixture.Service.List());

        Assert.Equal(searchId, listed.SearchId);
        Assert.Null(listed.SearchText);
    }

    [Fact]
    public void Lists_Every_Download_Whether_Or_Not_It_Has_A_Batch()
    {
        // the join is a left join; an inner one drops the rows with no batch, which is most of them
        var fixture = new Fixture();
        var withBatch = Guid.NewGuid();
        var withoutSearch = Guid.NewGuid();

        fixture.InsertBatch(withBatch, Guid.NewGuid(), "aphex twin");
        fixture.InsertBatch(withoutSearch, searchId: null, searchText: null);
        fixture.InsertDownload("alice", "@@a\\one.mp3", withBatch);
        fixture.InsertDownload("bob", "@@b\\two.mp3", batchId: null);
        fixture.InsertDownload("carol", "@@c\\three.mp3", withoutSearch);

        var listed = fixture.Service.List();

        Assert.Equal(3, listed.Count);
        Assert.Single(listed, t => t.SearchText == "aphex twin");
    }

    [Fact]
    public void A_Transfer_Cannot_Name_A_Batch_That_Does_Not_Exist()
    {
        // the schema's own guarantee, and the reason the left join can only miss on a null BatchId rather
        // than on a batch that has been deleted out from under a transfer
        var fixture = new Fixture();

        var ex = Record.Exception(() =>
            fixture.InsertDownload("alice", "@@a\\track.mp3", batchId: Guid.NewGuid()));

        Assert.IsType<DbUpdateException>(ex);
    }

    [Fact]
    public void Leaves_An_Upload_Out_Of_It()
    {
        var fixture = new Fixture();

        fixture.InsertDownload("alice", "@@a\\track.mp3", batchId: null, direction: TransferDirection.Upload);

        Assert.Empty(fixture.Service.List());
    }

    private class Fixture
    {
        private readonly SqliteConnection anchor;
        private readonly DbContextOptions<TransfersDbContext> options;

        public Fixture()
        {
            var name = $"list_{Guid.NewGuid():N}";
            var connectionString = $"Data Source={name};Mode=Memory;Cache=Shared";

            // keep the shared-cache in-memory database alive for the life of this fixture
            anchor = new SqliteConnection(connectionString);
            anchor.Open();

            options = new DbContextOptionsBuilder<TransfersDbContext>()
                .UseSqlite(connectionString)
                .Options;

            using var context = new TransfersDbContext(options);
            context.Database.EnsureCreated();

            var factory = new Mock<IDbContextFactory<TransfersDbContext>>();
            factory.Setup(f => f.CreateDbContext()).Returns(() => new TransfersDbContext(options));

            Service = new DownloadService(
                new Mock<IBatchService>().Object,
                new Mock<slskd.Search.ISearchService>().Object,
                new TestOptionsMonitor<Options>(new Options()),
                null,
                factory.Object,
                null,
                null,
                null,
                null);
        }

        public DownloadService Service { get; }

        public void InsertBatch(Guid id, Guid? searchId, string searchText)
        {
            using var context = new TransfersDbContext(options);

            context.Batches.Add(new Batch
            {
                Id = id,
                SearchId = searchId,
                SearchText = searchText,
                Username = "alice",
                Direction = TransferDirection.Download,
            });

            context.SaveChanges();
        }

        public void InsertDownload(string username, string filename, Guid? batchId, TransferDirection direction = TransferDirection.Download)
        {
            using var context = new TransfersDbContext(options);

            context.Transfers.Add(new slskd.Transfers.Transfer
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                Username = username,
                Direction = direction,
                Filename = filename,
                Size = 1_000,
                State = TransferStates.Completed | TransferStates.Succeeded,
                RequestedAt = DateTime.UtcNow,
            });

            context.SaveChanges();
        }
    }
}
