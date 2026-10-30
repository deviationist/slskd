namespace slskd.Tests.Unit.Player;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using slskd.Migrations;
using slskd.Player;
using slskd.Transfers;
using Xunit;

/// <summary>
///     The analyses kept for good, by download: what lets the downloads list carry each row's verdict, and a restart
///     lose none of them.
/// </summary>
public sealed class AnalysisCacheTests : IDisposable
{
    private readonly string connectionString = $"Data Source=analyses_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection anchor;
    private readonly DbContextOptions<TransfersDbContext> dbOptions;

    public AnalysisCacheTests()
    {
        anchor = new SqliteConnection(connectionString);
        anchor.Open();

        dbOptions = new DbContextOptionsBuilder<TransfersDbContext>().UseSqlite(connectionString).Options;

        using var context = new TransfersDbContext(dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose() => anchor.Dispose();

    [Fact]
    public void A_kept_analysis_is_there_after_a_restart()
    {
        var download = Guid.NewGuid();
        Cache().Remember(download, Document("abc-v5", "analysis-v5", "warn", "Clipped"));

        // a new instance reads it from the database, as slskd does after a restart
        var cache = Cache();

        Assert.True(cache.TryGet(download, out var document));
        Assert.Equal("Clipped", document.GetProperty("verdict").GetProperty("headline").GetString());
        Assert.Equal(new AnalysisVerdict("warn", "Clipped"), cache.VerdictsOf([download])[download]);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Only_the_downloads_with_one_have_a_verdict()
    {
        var analysed = Guid.NewGuid();
        var cache = Cache();
        cache.Remember(analysed, Document("abc-v5", "analysis-v5"));

        var verdicts = cache.VerdictsOf([analysed, Guid.NewGuid()]);

        Assert.Equal([analysed], verdicts.Keys);
        Assert.False(cache.TryGet(Guid.NewGuid(), out _));
    }

    [Fact]
    public void Two_downloads_of_the_same_audio_share_one_analysis()
    {
        var cache = Cache();
        cache.Remember(Guid.NewGuid(), Document("same-audio-v5", "analysis-v5"));
        cache.Remember(Guid.NewGuid(), Document("same-audio-v5", "analysis-v5"));

        using var context = new TransfersDbContext(dbOptions);
        Assert.Single(context.Analyses);
        Assert.Equal(2, context.DownloadAnalyses.Count());
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void A_provider_that_gives_no_id_gets_one_made_per_download()
    {
        var download = Guid.NewGuid();
        Cache().Remember(download, Document(id: null, "analysis-v5"));

        using var context = new TransfersDbContext(dbOptions);
        Assert.Equal($"{download}-analysis-v5", Assert.Single(context.Analyses).Id);
    }

    [Fact]
    public void A_new_version_of_the_analyser_drops_what_the_old_one_made()
    {
        // the rules changed -- what counts as a gap, or as clipping -- and the provider says so with its version
        var old = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        Cache().Remember(old, Document("abc-v5", "analysis-v5"));

        var cache = Cache();
        cache.Remember(fresh, Document("def-v6", "analysis-v6"));

        Assert.False(cache.TryGet(old, out _));
        Assert.True(cache.TryGet(fresh, out _));
        Assert.Equal([fresh], cache.VerdictsOf([old, fresh]).Keys);

        using var context = new TransfersDbContext(dbOptions);
        Assert.Equal(["def-v6"], context.Analyses.Select(a => a.Id));
    }

    [Fact]
    public void Clearing_drops_everything_kept()
    {
        var download = Guid.NewGuid();
        var cache = Cache();
        cache.Remember(download, Document("abc-v5", "analysis-v5"));
        cache.Remember(Guid.NewGuid(), Document("def-v5", "analysis-v5"));

        Assert.Equal(2, cache.Clear());

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet(download, out _));
        Assert.Equal(0, Cache().Count);
    }

    [Fact]
    public void Reanalysing_a_download_points_it_at_the_new_analysis()
    {
        var download = Guid.NewGuid();
        var cache = Cache();
        cache.Remember(download, Document("abc-v5", "analysis-v5", "ok", "Fine"));
        cache.Remember(download, Document("xyz-v5", "analysis-v5", "bad", "Upsampled"));

        Assert.Equal("bad", cache.VerdictsOf([download])[download].Level);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void The_migration_makes_the_tables_on_a_database_from_before_them()
    {
        using (var context = new TransfersDbContext(dbOptions))
        {
            context.Database.ExecuteSqlRaw("DROP TABLE DownloadAnalyses");
            context.Database.ExecuteSqlRaw("DROP TABLE Analyses");
        }

        var migration = new Z2026_10_30_AnalysisCacheMigration(
            new ConnectionStringDictionary(new Dictionary<Database, ConnectionString> { [Database.Transfers] = connectionString }));

        Assert.True(migration.NeedsToBeApplied());
        migration.Apply();
        Assert.False(migration.NeedsToBeApplied());

        var download = Guid.NewGuid();
        Cache().Remember(download, Document("abc-v5", "analysis-v5"));
        Assert.True(Cache().TryGet(download, out _));
    }

    private static JsonElement Document(string id, string version, string level = "ok", string headline = "Fine")
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["contract"] = 1,
            ["id"] = id,
            ["provider"] = new { name = "t", version },
            ["verdict"] = new { level, headline },
        });

        if (id is null)
        {
            json = json.Replace("\"id\":null,", string.Empty);
        }

        using var parsed = JsonDocument.Parse(json);
        return parsed.RootElement.Clone();
    }

    private AnalysisCache Cache()
    {
        var factory = new Mock<IDbContextFactory<TransfersDbContext>>();
        factory.Setup(f => f.CreateDbContext()).Returns(() => new TransfersDbContext(dbOptions));
        return new AnalysisCache(factory.Object);
    }
}
