namespace slskd.Tests.Unit.Messaging;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using slskd.Messaging;
using Soulseek;
using Xunit;

public class ConversationListTests
{
    [Fact]
    public async Task Carries_The_Newest_Message_Of_Each_Conversation_Either_Way()
    {
        // the list is what the Chat page orders by and previews from, so the newest has to win whichever side sent it
        var fixture = new Fixture();
        var start = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

        fixture.Conversation("alice");
        fixture.Message("alice", "hi", MessageDirection.In, start);
        fixture.Message("alice", "hello back", MessageDirection.Out, start.AddMinutes(5));
        fixture.Conversation("bob");
        fixture.Message("bob", "later", MessageDirection.In, start.AddMinutes(10));
        fixture.Message("bob", "earlier", MessageDirection.In, start.AddMinutes(1));

        var list = (await fixture.Service.ListAsync(c => c.IsActive)).ToDictionary(c => c.Username);

        Assert.Equal("hello back", list["alice"].LastMessage.Message);
        Assert.Equal(MessageDirection.Out, list["alice"].LastMessage.Direction);
        Assert.Equal("later", list["bob"].LastMessage.Message);
    }

    [Fact]
    public async Task Leaves_It_Empty_For_A_Conversation_With_No_Messages()
    {
        var fixture = new Fixture();

        fixture.Conversation("carol");

        var carol = (await fixture.Service.ListAsync(c => c.IsActive)).Single();

        Assert.Null(carol.LastMessage);
    }

    private class Fixture
    {
        private readonly SqliteConnection anchor;
        private readonly DbContextOptions<MessagingDbContext> options;

        public Fixture()
        {
            var connectionString = $"Data Source=conversations_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

            // keep the shared-cache in-memory database alive for the life of this fixture
            anchor = new SqliteConnection(connectionString);
            anchor.Open();

            options = new DbContextOptionsBuilder<MessagingDbContext>()
                .UseSqlite(connectionString)
                .Options;

            using var context = new MessagingDbContext(options);
            context.Database.EnsureCreated();

            var factory = new Mock<IDbContextFactory<MessagingDbContext>>();
            factory.Setup(f => f.CreateDbContext())
                .Returns(() => new MessagingDbContext(options));

            Service = new ConversationService(new Mock<ISoulseekClient>().Object, eventBus: null, factory.Object, new ManagedState<State>());
        }

        public ConversationService Service { get; }

        public void Conversation(string username)
        {
            using var context = new MessagingDbContext(options);
            context.Conversations.Add(new Conversation { Username = username });
            context.SaveChanges();
        }

        public void Message(string username, string text, MessageDirection direction, DateTime timestamp)
        {
            using var context = new MessagingDbContext(options);
            context.PrivateMessages.Add(new PrivateMessage
            {
                Username = username,
                Message = text,
                Direction = direction,
                Timestamp = timestamp,
                Id = Random.Shared.Next(),
            });
            context.SaveChanges();
        }
    }
}
