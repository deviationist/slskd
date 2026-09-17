namespace slskd.Tests.Unit.Search;

using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using slskd.Search;
using Xunit;

/// <summary>
///     Tests for whether a search still exists.
/// </summary>
/// <remarks>
///     Against a real SQLite database: the value of this is that it asks the database rather than trusting a
///     record that cannot know, and a mocked context would be testing the mock.
/// </remarks>
public class SearchExistenceTests
{
    [Fact]
    public async Task Finds_A_Search_That_Exists()
    {
        var fixture = new Fixture();
        var id = fixture.Insert();

        Assert.True(await fixture.Existence.ExistsAsync(id));
    }

    [Fact]
    public async Task Does_Not_Find_One_That_Never_Existed()
    {
        var fixture = new Fixture();

        fixture.Insert();

        Assert.False(await fixture.Existence.ExistsAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Answers_From_An_Empty_Database()
    {
        var fixture = new Fixture();

        Assert.False(await fixture.Existence.ExistsAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Forgets_A_Deleted_Search_Once_Told_To()
    {
        // the cache is what makes this affordable once a second; `Forget` is what keeps it from being wrong for
        // longer than its window
        var fixture = new Fixture();
        var id = fixture.Insert();

        Assert.True(await fixture.Existence.ExistsAsync(id));

        fixture.Delete(id);
        fixture.Existence.Forget();

        Assert.False(await fixture.Existence.ExistsAsync(id));
    }

    [Fact]
    public async Task Holds_Its_Answer_Until_Then()
    {
        // the deliberate trade: one indexed read every few seconds rather than one per row per poll, at the cost
        // of an answer that can be up to that window stale
        var fixture = new Fixture();
        var id = fixture.Insert();

        Assert.True(await fixture.Existence.ExistsAsync(id));

        fixture.Delete(id);

        Assert.True(await fixture.Existence.ExistsAsync(id));
    }

    private class Fixture
    {
        private readonly SqliteConnection anchor;
        private readonly DbContextOptions<SearchDbContext> options;

        public Fixture()
        {
            var connectionString = $"Data Source=exists_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

            // keep the shared-cache in-memory database alive for the life of this fixture
            anchor = new SqliteConnection(connectionString);
            anchor.Open();

            options = new DbContextOptionsBuilder<SearchDbContext>()
                .UseSqlite(connectionString)
                .Options;

            using var context = new SearchDbContext(options);
            context.Database.EnsureCreated();

            var factory = new Mock<IDbContextFactory<SearchDbContext>>();
            factory.Setup(f => f.CreateDbContextAsync(default))
                .ReturnsAsync(() => new SearchDbContext(options));

            Existence = new SearchExistence(factory.Object);
        }

        public SearchExistence Existence { get; }

        public Guid Insert()
        {
            using var context = new SearchDbContext(options);
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

        public void Delete(Guid id)
        {
            using var context = new SearchDbContext(options);

            context.Searches.Remove(context.Searches.Find(id));
            context.SaveChanges();
        }
    }
}
