namespace slskd.Tests.Unit.Integrations.Notifications
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Moq;
    using slskd.Integrations.Mail;
    using slskd.Integrations.Notifications;
    using Xunit;

    public class NotificationServiceTests
    {
        [Fact]
        public async Task An_event_that_is_not_enabled_goes_nowhere()
        {
            var ntfy = new FakeChannel("ntfy");
            var service = Service([ntfy], events: new() { PrivateMessage = new() { Enabled = false } });

            var results = await service.NotifyAsync(Note(NotificationEvent.PrivateMessage));

            Assert.Empty(results);
            Assert.Empty(ntfy.Sent);
            Assert.False(service.IsEnabled(NotificationEvent.PrivateMessage));
        }

        [Fact]
        public async Task An_event_that_names_no_channels_goes_to_every_enabled_one()
        {
            var mail = new FakeChannel("mail");
            var ntfy = new FakeChannel("ntfy");
            var off = new FakeChannel("other", enabled: false);
            var service = Service([mail, ntfy, off], events: new() { RoomMention = new() { Enabled = true } });

            var results = await service.NotifyAsync(Note(NotificationEvent.RoomMention));

            Assert.Single(mail.Sent);
            Assert.Single(ntfy.Sent);
            Assert.Empty(off.Sent);
            Assert.Equal(["mail", "ntfy"], results.Where(r => r.Sent).Select(r => r.Channel));
            Assert.Contains(results, r => r.Channel == "other" && !r.Sent && r.Error == "other is not configured");
        }

        [Fact]
        public async Task An_event_that_names_channels_goes_to_those_only()
        {
            var mail = new FakeChannel("mail");
            var ntfy = new FakeChannel("ntfy");
            var service = Service([mail, ntfy], events: new() { PrivateMessage = new() { Enabled = true, Channels = ["NTFY"] } });

            await service.NotifyAsync(Note(NotificationEvent.PrivateMessage));

            Assert.Empty(mail.Sent);
            Assert.Single(ntfy.Sent);
        }

        [Fact]
        public async Task A_failing_channel_does_not_stop_the_others_and_nothing_is_thrown()
        {
            var mail = new FakeChannel("mail") { Fail = true };
            var ntfy = new FakeChannel("ntfy");
            var service = Service([mail, ntfy], events: new() { ShareScanFailed = new() { Enabled = true } });

            var results = await service.NotifyAsync(Note(NotificationEvent.ShareScanFailed));

            Assert.Equal([("mail", false, "boom"), ("ntfy", true, null)], results.Select(r => (r.Channel, r.Sent, r.Error)));
            Assert.Single(ntfy.Sent);
        }

        [Fact]
        public void Watch_results_are_on_by_default_so_existing_watches_keep_mailing_and_nothing_else_is()
        {
            var events = new slskd.Options.IntegrationsOptions.NotificationEventsOptions();

            Assert.True(events.WatchResults.Enabled);
            Assert.False(events.PrivateMessage.Enabled);
            Assert.False(events.RoomMention.Enabled);
            Assert.False(events.ConnectionLost.Enabled);
            Assert.False(events.ShareScanFailed.Enabled);
        }

        [Fact]
        public async Task A_test_goes_to_the_channel_asked_for_whatever_the_events_say()
        {
            var mail = new FakeChannel("mail");
            var ntfy = new FakeChannel("ntfy");
            var service = Service([mail, ntfy], events: new());

            var results = await service.TestAsync("ntfy");

            Assert.Empty(mail.Sent);
            Assert.Equal(NotificationEvent.Test, Assert.Single(ntfy.Sent).Event);
            Assert.True(Assert.Single(results).Sent);
        }

        [Fact]
        public void A_channel_that_does_not_exist_is_a_configuration_error()
        {
            var options = new slskd.Options.IntegrationsOptions.NotificationEventOptions { Enabled = true, Channels = ["pushbullet"] };

            var problems = options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)).ToList();

            Assert.Contains(problems, p => p.ErrorMessage.Contains("'pushbullet' is not a notification channel"));
        }

        [Fact]
        public async Task Ntfy_is_published_as_json_with_the_token_and_any_text()
        {
            string body = null;
            HttpRequestMessage request = null;
            var channel = Ntfy(async r =>
            {
                request = r;
                body = await r.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            await channel.SendAsync(new Notification
            {
                Event = NotificationEvent.PrivateMessage,
                Title = "Aurora's Embrace — 2 copies",
                Text = "hei på deg",
                Url = "https://slskd.example/chat",
                Priority = NotificationPriority.High,
                Tags = ["speech_balloon"],
            });

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://ntfy.example/", request.RequestUri.AbsoluteUri);
            Assert.Equal(("Bearer", "ntfy-secret"), (request.Headers.Authorization.Scheme, request.Headers.Authorization.Parameter));

            var json = JsonDocument.Parse(body).RootElement;
            Assert.Equal("slskd", json.GetProperty("topic").GetString());
            Assert.Equal("Aurora's Embrace — 2 copies", json.GetProperty("title").GetString());
            Assert.Equal("hei på deg", json.GetProperty("message").GetString());
            Assert.Equal(4, json.GetProperty("priority").GetInt32());
            Assert.Equal("speech_balloon", json.GetProperty("tags")[0].GetString());
            Assert.Equal("https://slskd.example/chat", json.GetProperty("click").GetString());
        }

        [Fact]
        public async Task A_refusal_from_ntfy_is_an_error_that_carries_the_status_and_not_the_body()
        {
            var channel = Ntfy(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"error\":\"topic slskd, token ntfy-secret denied\"}", Encoding.UTF8, "application/json"),
            }));

            var ex = await Assert.ThrowsAsync<NotificationException>(() => channel.SendAsync(Note(NotificationEvent.Test)));

            Assert.Contains("403", ex.Message);
            Assert.DoesNotContain("ntfy-secret", ex.Message);
        }

        [Theory]
        [InlineData(NotificationPriority.Low, 2)]
        [InlineData(NotificationPriority.Default, 3)]
        [InlineData(NotificationPriority.High, 4)]
        public void Priorities_map_onto_ntfy_without_its_silent_or_urgent_extremes(NotificationPriority priority, int expected)
            => Assert.Equal(expected, NtfyNotificationChannel.PriorityOf(priority));

        [Fact]
        public async Task Mail_goes_to_the_notifications_own_address_and_carries_the_link_in_a_plain_body()
        {
            var adapter = new FakeMailAdapter();
            var channel = new MailNotificationChannel(new MailService([adapter], Monitor(new slskd.Options
            {
                Integrations = new slskd.Options.IntegrationsOptions
                {
                    Mail = new slskd.Options.IntegrationsOptions.MailOptions { Enabled = true, Adapter = "fake", From = "me@example.com", To = "default@example.com" },
                },
            })));

            await channel.SendAsync(Note(NotificationEvent.WatchResults) with { MailTo = "watch@example.com", Url = "https://slskd.example/searches/1" });

            var (mail, to) = Assert.Single(adapter.Sent);
            Assert.Equal("watch@example.com", to);
            Assert.Equal("Title", mail.Subject);
            Assert.Equal("Text\n\nhttps://slskd.example/searches/1", mail.Text);
        }

        private static Notification Note(NotificationEvent e) => new() { Event = e, Title = "Title", Text = "Text" };

        private static NotificationService Service(List<INotificationChannel> channels, slskd.Options.IntegrationsOptions.NotificationEventsOptions events)
            => new(channels, Monitor(new slskd.Options
            {
                Integrations = new slskd.Options.IntegrationsOptions
                {
                    Notifications = new slskd.Options.IntegrationsOptions.NotificationsOptions { Events = events },
                },
            }));

        private static NtfyNotificationChannel Ntfy(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(new Handler(respond)));

            return new NtfyNotificationChannel(factory.Object, Monitor(new slskd.Options
            {
                Integrations = new slskd.Options.IntegrationsOptions
                {
                    Notifications = new slskd.Options.IntegrationsOptions.NotificationsOptions
                    {
                        Ntfy = new slskd.Options.IntegrationsOptions.NtfyOptions { Url = "https://ntfy.example/", Topic = "slskd", Token = "ntfy-secret" },
                    },
                },
            }));
        }

        private static TestOptionsMonitor<slskd.Options> Monitor(slskd.Options options) => new(options);

        private sealed class FakeChannel(string name, bool enabled = true) : INotificationChannel
        {
            public string Name => name;
            public bool IsEnabled => enabled;
            public bool Fail { get; set; }
            public List<Notification> Sent { get; } = [];

            public Task SendAsync(Notification notification, CancellationToken cancellationToken = default)
            {
                if (Fail)
                {
                    throw new NotificationException("boom");
                }

                Sent.Add(notification);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeMailAdapter : IMailAdapter
        {
            public string Name => "fake";
            public List<(Mail Mail, string To)> Sent { get; } = [];

            public Task SendAsync(Mail mail, string from, string to, CancellationToken cancellationToken = default)
            {
                Sent.Add((mail, to));
                return Task.CompletedTask;
            }
        }

        private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => respond(request);
        }
    }
}
