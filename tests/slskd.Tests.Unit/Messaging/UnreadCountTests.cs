namespace slskd.Tests.Unit.Messaging;

using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using slskd.Events;
using slskd.Messaging;
using Soulseek;
using Xunit;

/// <summary>
///     The unread count the menu shows: what the Chat page would call unread, from everyone, kept in the application
///     state as it changes.
/// </summary>
public sealed class UnreadCountTests : IDisposable
{
    private readonly SqliteConnection anchor;
    private readonly DbContextOptions<MessagingDbContext> options;

    public UnreadCountTests()
    {
        var connectionString = $"Data Source=unread_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        // keep the shared-cache in-memory database alive for the life of the test
        anchor = new SqliteConnection(connectionString);
        anchor.Open();

        options = new DbContextOptionsBuilder<MessagingDbContext>().UseSqlite(connectionString).Options;

        using var context = new MessagingDbContext(options);
        context.Database.EnsureCreated();
    }

    private ManagedState<State> State { get; } = new();

    public void Dispose() => anchor.Dispose();

    [Fact]
    public void What_was_left_unread_is_counted_at_startup()
    {
        Stored("alice", acknowledged: false);
        Stored("alice", acknowledged: false);
        Stored("bob", acknowledged: false);
        Stored("carol", acknowledged: true);

        Service();

        Assert.Equal((3, 2), Unread());
    }

    [Fact]
    public async Task A_message_arriving_is_counted_from_whoever_it_is_from()
    {
        var service = Service();
        Assert.Equal((0, 0), Unread());

        await service.HandleMessageAsync("alice", Message("alice", 1));
        await service.HandleMessageAsync("bob", Message("bob", 2));
        await service.HandleMessageAsync("bob", Message("bob", 3));

        Assert.Equal((3, 2), Unread());
        Assert.Equal(
            [new UnreadConversationState("alice", 1), new UnreadConversationState("bob", 2)],
            State.CurrentValue.Conversations.ByUser);
    }

    [Fact]
    public async Task A_username_comes_out_exactly_as_it_went_in()
    {
        // a dictionary keyed by username would have had its keys camel-cased by the serializer on the way out
        var service = Service();
        await service.HandleMessageAsync("Alice_DJ", Message("Alice_DJ", 1));

        Assert.Equal("Alice_DJ", Assert.Single(State.CurrentValue.Conversations.ByUser).Username);
        Assert.Contains("\"username\":\"Alice_DJ\"", System.Text.Json.JsonSerializer.Serialize(
            State.CurrentValue.Conversations,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task A_replayed_message_is_not_counted_twice()
    {
        // the server replays unacknowledged messages on login; the same message must stay one message
        var service = Service();
        await service.HandleMessageAsync("alice", Message("alice", 1));

        var replayed = Message("alice", 1);
        replayed.WasReplayed = true;
        await service.HandleMessageAsync("alice", replayed);

        Assert.Equal((1, 1), Unread());
    }

    [Fact]
    public async Task Reading_a_conversation_takes_only_its_messages_off_the_count()
    {
        var service = Service();
        await service.HandleMessageAsync("alice", Message("alice", 1));
        await service.HandleMessageAsync("bob", Message("bob", 2));

        await service.AcknowledgeAsync("alice");

        Assert.Equal((1, 1), Unread());
        Assert.Equal([new UnreadConversationState("bob", 1)], State.CurrentValue.Conversations.ByUser);

        await service.AcknowledgeAsync("bob");

        Assert.Equal((0, 0), Unread());
    }

    [Fact]
    public async Task Removing_a_conversation_clears_its_unread_messages()
    {
        var service = Service();
        await service.HandleMessageAsync("alice", Message("alice", 1));

        await service.RemoveAsync("alice");

        Assert.Equal((0, 0), Unread());
    }

    [Fact]
    public async Task What_we_send_is_never_unread()
    {
        var service = Service();

        await service.SendMessageAsync("alice", "hi");

        Assert.Equal((0, 0), Unread());
    }

    private (int Messages, int Conversations) Unread()
        => (State.CurrentValue.Conversations.UnAcknowledgedMessages, State.CurrentValue.Conversations.UnAcknowledgedConversations);

    private ConversationService Service()
    {
        var factory = new Mock<IDbContextFactory<MessagingDbContext>>();
        factory.Setup(f => f.CreateDbContext()).Returns(() => new MessagingDbContext(options));

        var events = new Mock<EventBus>(MockBehavior.Loose, new object[] { null });

        return new ConversationService(new Mock<ISoulseekClient>().Object, events.Object, factory.Object, State);
    }

    private static PrivateMessage Message(string username, int id) => new()
    {
        Id = id,
        Username = username,
        Message = "hello",
        Direction = MessageDirection.In,
        Timestamp = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc).AddSeconds(id),
    };

    private void Stored(string username, bool acknowledged)
    {
        using var context = new MessagingDbContext(options);
        context.PrivateMessages.Add(new PrivateMessage
        {
            Id = Random.Shared.Next(),
            Username = username,
            Message = "hello",
            Direction = MessageDirection.In,
            Timestamp = DateTime.UtcNow,
            IsAcknowledged = acknowledged,
        });
        context.SaveChanges();
    }
}
