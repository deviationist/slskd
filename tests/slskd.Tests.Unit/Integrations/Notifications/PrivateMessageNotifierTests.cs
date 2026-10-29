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

    public class PrivateMessageNotifierTests
    {
        private ManagedState<State> State { get; } = new();
        private FakeChannel Ntfy { get; } = new();

        [Fact]
        public async Task A_message_still_unread_after_the_delay_is_notified()
        {
            var notifier = Notifier();
            Unread(("alice", 1));

            await notifier.HandleAsync(Received("alice", "hei"));

            var sent = Assert.Single(Ntfy.Sent);
            Assert.Equal("Message from alice", sent.Title);
            Assert.Equal("hei", sent.Text);
            Assert.Equal(NotificationEvent.PrivateMessage, sent.Event);
        }

        [Fact]
        public async Task A_message_read_before_the_delay_ends_is_not()
        {
            // the operator was in the conversation: the Chat page read it as it arrived
            var notifier = Notifier();
            Unread();

            await notifier.HandleAsync(Received("alice", "hei"));

            Assert.Empty(Ntfy.Sent);
        }

        [Fact]
        public async Task A_conversation_is_notified_once_until_it_is_read_then_again()
        {
            var notifier = Notifier();
            Unread(("alice", 1));
            await notifier.HandleAsync(Received("alice", "one"));

            Unread(("alice", 3));
            await notifier.HandleAsync(Received("alice", "two"));
            await notifier.HandleAsync(Received("alice", "three"));
            Assert.Single(Ntfy.Sent);

            Unread();                     // read
            Unread(("alice", 1));
            await notifier.HandleAsync(Received("alice", "four"));

            Assert.Equal(["one", "four"], Ntfy.Sent.Select(n => n.Text));
        }

        [Fact]
        public async Task What_was_unread_at_startup_is_not_notified_again_when_the_server_replays_it()
        {
            Unread(("alice", 2));
            var notifier = Notifier();

            await notifier.HandleAsync(Received("alice", "from before", replayed: true));
            Unread(("alice", 2), ("bob", 1));
            await notifier.HandleAsync(Received("bob", "wrote while it was down", replayed: true));

            Assert.Equal(["Message from bob"], Ntfy.Sent.Select(n => n.Title));
        }

        [Fact]
        public async Task What_we_send_and_a_disabled_event_notify_nothing()
        {
            Unread(("alice", 1));

            await Notifier().HandleAsync(Received("alice", "mine", direction: MessageDirection.Out));
            await Notifier(enabled: false).HandleAsync(Received("alice", "hei"));

            Assert.Empty(Ntfy.Sent);
        }

        [Fact]
        public void The_title_counts_what_is_unread_the_preview_is_trimmed_and_it_links_to_chat()
        {
            var notification = Notifier(baseUrl: "https://slskd.example/").Compose("alice", new string('x', 400), unread: 3);

            Assert.Equal("3 messages from alice", notification.Title);
            Assert.Equal(301, notification.Text.Length);
            Assert.EndsWith("…", notification.Text);
            Assert.Equal("https://slskd.example/chat", notification.Url);
            Assert.Null(Notifier().Compose("alice", "hi", 1).Url);
        }

        private void Unread(params (string Username, int Count)[] byUser)
            => State.SetValue(s => s with
            {
                Conversations = new ConversationsState
                {
                    ByUser = byUser.Select(u => new UnreadConversationState(u.Username, u.Count)).ToList(),
                    UnAcknowledgedMessages = byUser.Sum(u => u.Count),
                    UnAcknowledgedConversations = byUser.Length,
                },
            });

        private static PrivateMessageReceivedEvent Received(string username, string text, bool replayed = false, MessageDirection direction = MessageDirection.In)
            => new() { Message = new PrivateMessage { Username = username, Message = text, WasReplayed = replayed, Direction = direction } };

        private PrivateMessageNotifier Notifier(bool enabled = true, string baseUrl = null)
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
                            PrivateMessage = new slskd.Options.IntegrationsOptions.PrivateMessageNotificationOptions { Enabled = enabled, Delay = 0 },
                        },
                    },
                },
            });

            var events = new Mock<EventBus>(MockBehavior.Loose, new object[] { null });

            return new PrivateMessageNotifier(events.Object, Mock.Of<IConversationService>(), State, new NotificationService([Ntfy], options), options);
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
