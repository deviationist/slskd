namespace slskd.Tests.Unit.Integrations.Notifications
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Moq;
    using slskd.Events;
    using slskd.Integrations.Notifications;
    using slskd.Messaging;
    using Xunit;

    public class RoomMessageNotifierTests
    {
        private ManagedState<State> State { get; } = new();
        private FakeChannel Ntfy { get; } = new();

        public RoomMessageNotifierTests()
        {
            State.SetValue(s => s with { User = new UserState { Username = "kai" } });
        }

        [Fact]
        public async Task A_room_still_unread_after_the_delay_is_notified()
        {
            var notifier = Notifier();
            Unread(("techno", 1));

            await notifier.HandleAsync(Received("techno", "alice", "anyone have this?"));

            var sent = Assert.Single(Ntfy.Sent);
            Assert.Equal("New message in techno", sent.Title);
            Assert.Equal("alice: anyone have this?", sent.Text);
            Assert.Equal(NotificationEvent.RoomMessage, sent.Event);
        }

        [Fact]
        public async Task A_room_read_before_the_delay_ends_is_not()
        {
            // the operator had the room open: the Rooms page read it as it arrived
            var notifier = Notifier();
            Unread();

            await notifier.HandleAsync(Received("techno", "alice", "hei"));

            Assert.Empty(Ntfy.Sent);
        }

        [Fact]
        public async Task A_busy_room_is_notified_once_until_it_is_read_then_again()
        {
            var notifier = Notifier();
            Unread(("techno", 1));
            await notifier.HandleAsync(Received("techno", "alice", "one"));

            Unread(("techno", 40));
            for (var i = 0; i < 39; i++)
            {
                await notifier.HandleAsync(Received("techno", "bob", $"line {i}"));
            }

            Assert.Single(Ntfy.Sent);

            Unread();                     // read
            Unread(("techno", 1));
            await notifier.HandleAsync(Received("techno", "carol", "back again"));

            Assert.Equal(["alice: one", "carol: back again"], Ntfy.Sent.Select(n => n.Text));
        }

        [Fact]
        public async Task Each_room_is_notified_on_its_own()
        {
            var notifier = Notifier();
            Unread(("techno", 1), ("ambient", 1));

            await notifier.HandleAsync(Received("techno", "alice", "one"));
            await notifier.HandleAsync(Received("ambient", "bob", "two"));

            Assert.Equal(["New message in techno", "New message in ambient"], Ntfy.Sent.Select(n => n.Title));
        }

        [Fact]
        public async Task Our_own_messages_and_a_disabled_event_notify_nothing()
        {
            Unread(("techno", 1));

            await Notifier().HandleAsync(Received("techno", "kai", "mine"));
            await Notifier().HandleAsync(Received("techno", "kai", "mine", MessageDirection.Out));
            await Notifier(enabled: false).HandleAsync(Received("techno", "alice", "hei"));

            Assert.Empty(Ntfy.Sent);
        }

        [Fact]
        public async Task Only_the_rooms_named_are_notified_when_rooms_are_named()
        {
            Unread(("techno", 1), ("ambient", 1));
            var notifier = Notifier(rooms: ["Techno"]);

            await notifier.HandleAsync(Received("ambient", "bob", "not this one"));
            await notifier.HandleAsync(Received("techno", "alice", "this one"));

            Assert.Equal(["alice: this one"], Ntfy.Sent.Select(n => n.Text));
        }

        [Fact]
        public async Task A_mention_is_left_to_the_mention_notifier_when_that_is_on()
        {
            Unread(("techno", 1));

            await Notifier(mentions: true).HandleAsync(Received("techno", "alice", "kai, you there?"));
            Assert.Empty(Ntfy.Sent);

            await Notifier(mentions: false).HandleAsync(Received("techno", "alice", "kai, you there?"));
            Assert.Single(Ntfy.Sent);
        }

        [Fact]
        public void The_title_counts_what_is_unread_the_preview_is_trimmed_and_it_links_to_rooms()
        {
            var notification = Notifier(baseUrl: "https://slskd.example/").Compose("techno", "alice", new string('x', 400), unread: 7);

            Assert.Equal("7 new messages in techno", notification.Title);
            Assert.Equal(301, notification.Text.Length);
            Assert.StartsWith("alice: ", notification.Text);
            Assert.EndsWith("…", notification.Text);
            Assert.Equal("https://slskd.example/rooms", notification.Url);
            Assert.Null(Notifier().Compose("techno", "alice", "hi", 1).Url);
        }

        private void Unread(params (string Room, int Count)[] byRoom)
            => State.SetValue(s => s with
            {
                RoomsUnread = new RoomsUnreadState
                {
                    ByRoom = byRoom.Select(r => new UnreadRoomState(r.Room, r.Count)).ToList(),
                    UnreadMessages = byRoom.Sum(r => r.Count),
                    UnreadRooms = byRoom.Length,
                },
            });

        private static RoomMessageReceivedEvent Received(string room, string username, string text, MessageDirection direction = MessageDirection.In)
            => new() { Message = new RoomMessage { RoomName = room, Username = username, Message = text, Direction = direction } };

        private RoomMessageNotifier Notifier(bool enabled = true, bool mentions = false, string[] rooms = null, string baseUrl = null)
        {
            var options = new TestOptionsMonitor<slskd.Options>(new slskd.Options
            {
                Integrations = new slskd.Options.IntegrationsOptions
                {
                    Mail = new slskd.Options.IntegrationsOptions.MailOptions { BaseUrl = baseUrl },
                    Notifications = new slskd.Options.IntegrationsOptions.NotificationsOptions
                    {
                        Events = new slskd.Options.IntegrationsOptions.NotificationEventsOptions
                        {
                            RoomMessage = new slskd.Options.IntegrationsOptions.RoomMessageNotificationOptions { Enabled = enabled, Delay = 0, Rooms = rooms ?? [] },
                            RoomMention = new() { Enabled = mentions },
                        },
                    },
                },
            });

            var events = new Mock<EventBus>(MockBehavior.Loose, new object[] { null });

            return new RoomMessageNotifier(events.Object, State, new NotificationService([Ntfy], options), options);
        }

        private sealed class FakeChannel : INotificationChannel
        {
            public string Name => "ntfy";
            public bool IsEnabled => true;
            public List<Notification> Sent { get; } = [];

            public Task SendAsync(Notification notification, CancellationToken cancellationToken = default)
            {
                Sent.Add(notification);
                return Task.CompletedTask;
            }
        }
    }
}
