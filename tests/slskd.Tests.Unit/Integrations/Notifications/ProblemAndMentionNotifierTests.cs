namespace slskd.Tests.Unit.Integrations.Notifications
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Moq;
    using slskd.Events;
    using slskd.Integrations.Notifications;
    using slskd.Messaging;
    using Soulseek;
    using Xunit;

    public class ProblemAndMentionNotifierTests
    {
        private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        private ManagedState<State> State { get; } = new();
        private FakeChannel Ntfy { get; } = new();

        [Fact]
        public async Task A_connection_down_longer_than_the_threshold_is_notified_once_then_its_return()
        {
            var notifier = Problems();

            await notifier.CheckAsync(Connection(watchdog: true, loggedIn: false), T0);
            await notifier.CheckAsync(Connection(watchdog: true, loggedIn: false), T0.AddMinutes(9));
            Assert.Empty(Ntfy.Sent);

            await notifier.CheckAsync(Connection(watchdog: true, loggedIn: false), T0.AddMinutes(10));
            await notifier.CheckAsync(Connection(watchdog: true, loggedIn: false), T0.AddMinutes(30));
            await notifier.CheckAsync(Connection(watchdog: true, loggedIn: true), T0.AddMinutes(31));

            Assert.Equal(
                ["slskd lost its Soulseek connection", "slskd is back on Soulseek"],
                Ntfy.Sent.Select(n => n.Title));
            Assert.Equal(NotificationPriority.High, Ntfy.Sent[0].Priority);
            Assert.Contains("31 minutes", Ntfy.Sent[1].Text);
        }

        [Fact]
        public async Task A_blip_shorter_than_the_threshold_says_nothing_either_way()
        {
            var notifier = Problems();

            await notifier.CheckAsync(Connection(watchdog: true, loggedIn: false), T0);
            await notifier.CheckAsync(Connection(watchdog: true, loggedIn: true), T0.AddMinutes(3));

            Assert.Empty(Ntfy.Sent);
        }

        [Fact]
        public async Task A_disconnect_the_operator_asked_for_is_not_a_problem()
        {
            // the watchdog is off when the operator disconnects on purpose
            var notifier = Problems();

            await notifier.CheckAsync(Connection(watchdog: false, loggedIn: false), T0);
            await notifier.CheckAsync(Connection(watchdog: false, loggedIn: false), T0.AddHours(2));

            Assert.Empty(Ntfy.Sent);
        }

        [Fact]
        public async Task A_failed_scan_is_notified_once_per_failure()
        {
            var notifier = Problems();
            var ok = Connection(watchdog: true, loggedIn: true);

            await notifier.CheckAsync(ok with { Shares = new ShareState { Faulted = true } }, T0);
            await notifier.CheckAsync(ok with { Shares = new ShareState { Faulted = true } }, T0.AddMinutes(1));
            await notifier.CheckAsync(ok with { Shares = new ShareState { Faulted = false } }, T0.AddMinutes(2));
            await notifier.CheckAsync(ok with { Shares = new ShareState { Faulted = true } }, T0.AddMinutes(3));

            Assert.Equal(2, Ntfy.Sent.Count(n => n.Event == NotificationEvent.ShareScanFailed));
        }

        [Theory]
        [InlineData("hey kai, got that track?", "Kai", true)]
        [InlineData("KAI!", "kai", true)]
        [InlineData("kaiser is here", "kai", false)]
        [InlineData("ask dj.kai+ about it", "dj.kai+", true)]
        [InlineData("nothing to see", "kai", false)]
        public void A_mention_is_the_username_as_a_word_of_its_own(string text, string username, bool expected)
            => Assert.Equal(expected, RoomMentionNotifier.Mentions(text, username));

        [Fact]
        public async Task A_mention_is_notified_and_the_room_then_stays_quiet_for_a_while()
        {
            State.SetValue(s => s with { User = new UserState { Username = "kai" } });
            var notifier = Mentions();

            await notifier.HandleAsync(Room("techno", "alice", "kai you around?"), T0);
            await notifier.HandleAsync(Room("techno", "bob", "kai!!"), T0.AddMinutes(5));
            await notifier.HandleAsync(Room("house", "carol", "hi kai"), T0.AddMinutes(5));
            await notifier.HandleAsync(Room("techno", "dave", "kai?"), T0.AddMinutes(11));

            Assert.Equal(
                ["alice mentioned you in techno", "carol mentioned you in house", "dave mentioned you in techno"],
                Ntfy.Sent.Select(n => n.Title));
        }

        [Fact]
        public async Task Our_own_messages_and_ones_that_do_not_mention_us_are_not_notified()
        {
            State.SetValue(s => s with { User = new UserState { Username = "kai" } });
            var notifier = Mentions();

            await notifier.HandleAsync(Room("techno", "kai", "kai here"), T0);
            await notifier.HandleAsync(Room("techno", "alice", "anyone have this?"), T0);

            Assert.Empty(Ntfy.Sent);
        }

        private static State Connection(bool watchdog, bool loggedIn) => new()
        {
            ConnectionWatchdog = new ServerConnectionWatchdogState { IsEnabled = watchdog },
            Server = new ServerState { State = loggedIn ? SoulseekClientStates.Connected | SoulseekClientStates.LoggedIn : SoulseekClientStates.Disconnected },
        };

        private static RoomMessageReceivedEvent Room(string room, string username, string text)
            => new() { Message = new RoomMessage { RoomName = room, Username = username, Message = text, Direction = MessageDirection.In } };

        private TestOptionsMonitor<slskd.Options> Options() => new(new slskd.Options
        {
            Integrations = new slskd.Options.IntegrationsOptions
            {
                Notifications = new slskd.Options.IntegrationsOptions.NotificationsOptions
                {
                    Events = new slskd.Options.IntegrationsOptions.NotificationEventsOptions
                    {
                        RoomMention = new() { Enabled = true },
                        ConnectionLost = new() { Enabled = true, After = 10 },
                        ShareScanFailed = new() { Enabled = true },
                    },
                },
            },
        });

        private ProblemNotifier Problems()
        {
            var options = Options();
            return new ProblemNotifier(State, new NotificationService([Ntfy], options), options);
        }

        private RoomMentionNotifier Mentions()
        {
            var options = Options();
            return new RoomMentionNotifier(new Mock<EventBus>(MockBehavior.Loose, new object[] { null }).Object, State, new NotificationService([Ntfy], options), options);
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
