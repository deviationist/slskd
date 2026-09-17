namespace slskd.Tests.Unit.Transfers.Downloads;

using System;
using System.Collections.Generic;
using slskd.Transfers;
using slskd.Transfers.Downloads;
using Xunit;

public class BatchInheritanceTests
{
    private static readonly Guid Supplied = Guid.NewGuid();
    private static readonly Guid Existing = Guid.NewGuid();

    [Fact]
    public void A_Supplied_Batch_Always_Wins()
    {
        var superseded = new List<Transfer>
        {
            Record("track.mp3", Existing, DateTime.UtcNow),
        };

        Assert.Equal(Supplied, BatchInheritance.Resolve(Supplied, superseded, "track.mp3"));
    }

    [Fact]
    public void An_Enqueue_With_No_Batch_Inherits_The_One_It_Supersedes()
    {
        // the retry: a new record for a file that already had one, and the same download as far as the
        // search it was started from is concerned
        var superseded = new List<Transfer>
        {
            Record("track.mp3", Existing, DateTime.UtcNow),
        };

        Assert.Equal(Existing, BatchInheritance.Resolve(supplied: null, superseded, "track.mp3"));
    }

    [Fact]
    public void Inherits_From_The_Same_File_Only()
    {
        // the records are this peer's whole queue, not this file's history
        var superseded = new List<Transfer>
        {
            Record("something else.mp3", Existing, DateTime.UtcNow),
        };

        Assert.Null(BatchInheritance.Resolve(supplied: null, superseded, "track.mp3"));
    }

    [Fact]
    public void Takes_The_Most_Recent_Of_Several()
    {
        var newest = Guid.NewGuid();
        var superseded = new List<Transfer>
        {
            Record("track.mp3", Existing, DateTime.UtcNow.AddDays(-2)),
            Record("track.mp3", newest, DateTime.UtcNow),
        };

        Assert.Equal(newest, BatchInheritance.Resolve(supplied: null, superseded, "track.mp3"));
    }

    [Fact]
    public void Looks_Past_A_Record_That_Has_No_Batch()
    {
        // enqueued from a search, then again through the API: the API's record is the newer one and has
        // no batch, but the origin recorded for this file has not stopped being true
        var superseded = new List<Transfer>
        {
            Record("track.mp3", Existing, DateTime.UtcNow.AddDays(-2)),
            Record("track.mp3", batchId: null, DateTime.UtcNow),
        };

        Assert.Equal(Existing, BatchInheritance.Resolve(supplied: null, superseded, "track.mp3"));
    }

    [Fact]
    public void Has_Nothing_To_Inherit_From_Nothing()
    {
        Assert.Null(BatchInheritance.Resolve(supplied: null, superseded: [], "track.mp3"));
        Assert.Null(BatchInheritance.Resolve(supplied: null, superseded: null, "track.mp3"));
    }

    private static Transfer Record(string filename, Guid? batchId, DateTime requestedAt) => new()
    {
        Id = Guid.NewGuid(),
        BatchId = batchId,
        Username = "alice",
        Filename = filename,
        RequestedAt = requestedAt,
    };
}
