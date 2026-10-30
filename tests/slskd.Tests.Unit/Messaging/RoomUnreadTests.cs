namespace slskd.Tests.Unit.Messaging;

using System;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using slskd.Events;
using slskd.Messaging;
using slskd.Users;
using Soulseek;
using Xunit;

/// <summary>
///     What is unread in the rooms joined, the count the Rooms menu item shows: counted as messages arrive, kept in the
///     application state, and cleared when a room is read or left. Messages are simulated, the way the
///     <c>room-simulation</c> flag lets the API do it, or raised as the Soulseek client would raise them.
/// </summary>
public class RoomUnreadTests
{
    private const string Me = "me";

    private ManagedState<State> State { get; } = new();
    private Mock<ISoulseekClient> Client { get; } = new();
    private Mock<IUserService> Users { get; } = new();
    private Mock<EventBus> Events { get; } = new(MockBehavior.Loose, new object[] { null });
    private RoomTracker Tracker { get; } = new(messageLimit: 250);

    public RoomUnreadTests()
    {
        Client.Setup(c => c.Username).Returns(Me);
    }

    [Fact]
    public void Someone_posting_is_counted_by_room()
    {
        var service = Service();

        service.Simulate("techno", "alice", "one");
        service.Simulate("techno", "bob", "two");
        service.Simulate("ambient", "carol", "three");

        Assert.Equal((3, 2), Unread());
        Assert.Equal(
            [new UnreadRoomState("ambient", 1), new UnreadRoomState("techno", 2)],
            State.CurrentValue.RoomsUnread.ByRoom);
    }

    [Fact]
    public void A_message_from_the_server_is_counted_like_a_simulated_one()
    {
        var service = Service();

        Client.Raise(c => c.RoomMessageReceived += null, new RoomMessageReceivedEventArgs("techno", "alice", "hei"));

        Assert.Equal((1, 1), Unread());
        Assert.Equal("hei", Assert.Single(Messages("techno")).Message);
        _ = service;
    }

    [Fact]
    public void What_we_say_is_never_unread()
    {
        // the server sends the operator's own messages back, like anyone else's
        var service = Service();

        var mine = service.Simulate("techno", Me, "mine");

        Assert.Equal((0, 0), Unread());
        Assert.Equal(MessageDirection.Out, mine.Direction);
        Assert.Single(Messages("techno"));
    }

    [Fact]
    public void A_blacklisted_user_is_neither_shown_nor_counted()
    {
        Users.Setup(u => u.IsBlacklisted("spammer", It.IsAny<System.Net.IPAddress>(), It.IsAny<bool>())).Returns(true);
        var service = Service();

        service.Simulate("techno", "spammer", "buy now");

        Assert.Equal((0, 0), Unread());
        Assert.Empty(Messages("techno"));
    }

    [Fact]
    public void Reading_a_room_takes_only_its_messages_off_the_count()
    {
        var service = Service();
        service.Simulate("techno", "alice", "one");
        service.Simulate("ambient", "carol", "two");

        service.MarkRead("techno");

        Assert.Equal((1, 1), Unread());
        Assert.Equal([new UnreadRoomState("ambient", 1)], State.CurrentValue.RoomsUnread.ByRoom);

        service.MarkRead("ambient");
        service.MarkRead("never-heard-of");

        Assert.Equal((0, 0), Unread());
    }

    [Fact]
    public void A_message_after_reading_counts_from_one_again()
    {
        var service = Service();
        service.Simulate("techno", "alice", "one");
        service.Simulate("techno", "alice", "two");
        service.MarkRead("techno");

        service.Simulate("techno", "bob", "three");

        Assert.Equal((1, 1), Unread());
    }

    [Fact]
    public async Task Leaving_a_simulated_room_clears_its_count_and_tells_the_server_nothing()
    {
        var service = Service();
        service.Simulate("practice", "alice", "one");

        await service.LeaveAsync("practice");

        Assert.Equal((0, 0), Unread());
        Client.Verify(c => c.LeaveRoomAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken?>()), Times.Never);
    }

    [Fact]
    public async Task Leaving_a_joined_room_clears_its_count()
    {
        var service = Service();
        Tracker.TryAdd("techno", new Room { Name = "techno" });
        service.Simulate("techno", "alice", "one");

        await service.LeaveAsync("techno");

        Assert.Equal((0, 0), Unread());
        Client.Verify(c => c.LeaveRoomAsync("techno", It.IsAny<System.Threading.CancellationToken?>()), Times.Once);
    }

    [Fact]
    public void The_server_saying_we_left_clears_the_count_too()
    {
        // a leave from elsewhere -- another client, or the server -- arrives as an event, not through LeaveAsync
        var service = Service();
        service.Simulate("techno", "alice", "one");

        Client.Raise(c => c.RoomLeft += null, new RoomLeftEventArgs("techno", Me));

        Assert.Equal((0, 0), Unread());
    }

    [Fact]
    public void A_simulated_room_is_made_marked_and_never_rejoined()
    {
        var service = Service();
        Tracker.TryAdd("techno", new Room { Name = "techno" });

        service.Simulate("practice", "alice", "one");

        Assert.True(Tracker.TryGet("practice", out var practice));
        Assert.True(practice.IsSimulated);

        // on login slskd rejoins what it had joined; a simulated room was never joined, so it is not asked for
        Client.Raise(c => c.LoggedIn += null, EventArgs.Empty);

        Client.Verify(c => c.JoinRoomAsync("techno", It.IsAny<bool>(), It.IsAny<System.Threading.CancellationToken?>()), Times.Once);
        Client.Verify(c => c.JoinRoomAsync("practice", It.IsAny<bool>(), It.IsAny<System.Threading.CancellationToken?>()), Times.Never);
    }

    [Fact]
    public void Simulating_into_a_joined_room_leaves_it_joined()
    {
        var service = Service();
        Tracker.TryAdd("techno", new Room { Name = "techno" });

        service.Simulate("techno", "alice", "one");

        Assert.True(Tracker.TryGet("techno", out var techno));
        Assert.False(techno.IsSimulated);
    }

    [Fact]
    public void The_count_has_moved_by_the_time_the_event_is_raised()
    {
        // the notifier reads the count when it hears the event; it must not find the count from before
        int? countWhenRaised = null;
        Events.Setup(e => e.Raise(It.IsAny<RoomMessageReceivedEvent>()))
            .Callback(() => countWhenRaised = State.CurrentValue.RoomsUnread.UnreadMessages);

        Service().Simulate("techno", "alice", "one");

        Assert.Equal(1, countWhenRaised);
    }

    [Fact]
    public void A_room_name_comes_out_exactly_as_it_went_in()
    {
        Service().Simulate("Deep_House", "alice", "one");

        Assert.Contains("\"roomName\":\"Deep_House\"", System.Text.Json.JsonSerializer.Serialize(
            State.CurrentValue.RoomsUnread,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    }

    private (int Messages, int Rooms) Unread()
        => (State.CurrentValue.RoomsUnread.UnreadMessages, State.CurrentValue.RoomsUnread.UnreadRooms);

    private RoomMessage[] Messages(string room)
        => Tracker.TryGet(room, out var found) ? found.Messages.ToArray() : [];

    private RoomService Service()
        => new(Client.Object, new TestOptionsMonitor<slskd.Options>(new slskd.Options()), State, Tracker, Users.Object, Events.Object);
}
