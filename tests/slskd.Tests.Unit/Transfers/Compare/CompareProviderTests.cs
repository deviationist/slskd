namespace slskd.Tests.Unit.Transfers.Compare
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Moq;
    using slskd.Transfers.Compare;
    using Xunit;

    public class CompareProviderTests : IDisposable
    {
        private const string Document =
            "{\"contract\":1,\"provider\":{\"name\":\"test\",\"version\":\"1\"},"
            + "\"verdict\":{\"level\":\"same\",\"headline\":\"Yes\",\"detail\":\"\"},"
            + "\"files\":[{\"readable\":true},{\"readable\":true}],\"groups\":[{\"files\":[0,1],\"notes\":[\"\",\"\"]}],"
            + "\"alone\":[],\"partly\":[]}";

        public CompareProviderTests()
        {
            Temp = Path.Combine(Path.GetTempPath(), $"slskd.test.{Guid.NewGuid()}");
            Downloads = Path.Combine(Temp, "downloads");
            Directory.CreateDirectory(Path.Combine(Downloads, "complete", "Some User"));
            A = Write(Path.Combine(Downloads, "complete", "Some User", "01 Mix.aiff"), 3);
            B = Write(Path.Combine(Downloads, "complete", "Some User", "01 Remix.mp3"), 5);
        }

        private string Temp { get; }
        private string Downloads { get; }
        private FileInfo A { get; }
        private FileInfo B { get; }
        private List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();
        private Queue<HttpResponseMessage> Responses { get; } = new();

        public void Dispose() => Directory.Delete(Temp, recursive: true);

        [Fact]
        public async Task Off_unless_enabled_even_with_a_provider()
        {
            var answer = await Provider(enabled: false).GetAsync([A, B]);

            Assert.Equal(CompareState.Off, answer.State);
            Assert.Empty(Requests);
        }

        [Fact]
        public async Task Off_without_a_provider_even_when_enabled()
        {
            var provider = Provider(url: null);

            Assert.False(provider.Enabled);
            Assert.Equal(CompareState.Off, (await provider.GetAsync([A, B])).State);
            Assert.Empty(Requests);
        }

        [Fact]
        public async Task The_files_are_posted_by_root_and_relative_path_in_order_with_their_size_mtime_and_the_token()
        {
            Responses.Enqueue(Json(HttpStatusCode.OK, Document));

            var answer = await Provider().GetAsync([B, A]);

            Assert.Equal(CompareState.Ready, answer.State);
            var (request, body) = Assert.Single(Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://provider.invalid/contract/v1/compare", request.RequestUri.AbsoluteUri);
            Assert.Equal(("Bearer", "provider-secret"), (request.Headers.Authorization.Scheme, request.Headers.Authorization.Parameter));

            var files = JsonDocument.Parse(body).RootElement.GetProperty("files").EnumerateArray().ToList();
            Assert.Equal(["complete/Some User/01 Remix.mp3", "complete/Some User/01 Mix.aiff"], files.Select(f => f.GetProperty("file").GetString()));
            Assert.All(files, f => Assert.Equal("downloads", f.GetProperty("root").GetString()));
            Assert.Equal(5, files[0].GetProperty("size").GetInt64());
            Assert.Equal(new DateTimeOffset(B.LastWriteTimeUtc).ToUnixTimeMilliseconds(), files[0].GetProperty("mtime").GetInt64());
        }

        [Fact]
        public async Task An_answer_is_kept_for_the_files_in_that_order_only()
        {
            Responses.Enqueue(Json(HttpStatusCode.OK, Document));
            Responses.Enqueue(Json(HttpStatusCode.OK, Document));
            var provider = Provider();

            await provider.GetAsync([A, B]);
            await provider.GetAsync([A, B]);
            Assert.Single(Requests);

            // the document's indices follow the order sent, so another order is another question
            await provider.GetAsync([B, A]);
            Assert.Equal(2, Requests.Count);
        }

        [Fact]
        public async Task A_changed_file_is_asked_about_afresh()
        {
            Responses.Enqueue(Json(HttpStatusCode.OK, Document));
            Responses.Enqueue(Json(HttpStatusCode.OK, Document));
            var provider = Provider();

            await provider.GetAsync([A, B]);
            File.WriteAllBytes(A.FullName, new byte[] { 1, 2, 3, 4 });
            await provider.GetAsync([A, B]);

            Assert.Equal(2, Requests.Count);
        }

        [Fact]
        public async Task A_file_in_no_configured_root_is_refused_and_nothing_is_sent()
        {
            var outside = Write(Path.Combine(Temp, "elsewhere.flac"), 1);

            var answer = await Provider().GetAsync([A, outside]);

            Assert.Equal(CompareState.Refused, answer.State);
            Assert.Contains("elsewhere.flac", answer.Reason);
            Assert.Empty(Requests);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(CompareProvider.MaximumFiles + 1)]
        public async Task Two_to_the_maximum_files_or_nothing_is_sent(int count)
        {
            var files = Enumerable.Range(0, count).Select(i => Write(Path.Combine(Downloads, $"{i}.flac"), 1)).ToList();

            var answer = await Provider().GetAsync(files);

            Assert.Equal(CompareState.Refused, answer.State);
            Assert.Empty(Requests);
        }

        [Fact]
        public async Task A_refusal_tells_the_browser_fixed_words_never_the_providers_and_is_not_kept()
        {
            // whatever the provider puts in its error stays in the log: here, a host path it should not have sent
            Responses.Enqueue(Json(HttpStatusCode.BadRequest, "{\"detail\":\"cannot open /mnt/secret/.env\"}"));
            Responses.Enqueue(Json(HttpStatusCode.OK, Document));
            var provider = Provider();

            var first = await provider.GetAsync([A, B]);
            var second = await provider.GetAsync([A, B]);

            Assert.Equal((CompareState.Refused, CompareProvider.RefusedByProvider), (first.State, first.Reason));
            Assert.DoesNotContain("/mnt", first.Reason);
            Assert.Equal(CompareState.Ready, second.State);
        }

        [Fact]
        public async Task A_conflict_is_asked_about_once_more_then_given_up()
        {
            Responses.Enqueue(Json(HttpStatusCode.Conflict, "{}"));
            Responses.Enqueue(Json(HttpStatusCode.Conflict, "{}"));

            var answer = await Provider().GetAsync([A, B]);

            Assert.Equal(CompareState.Unavailable, answer.State);
            Assert.Equal(2, Requests.Count);
        }

        [Fact]
        public async Task A_pending_answer_carries_when_to_ask_again_and_is_not_kept()
        {
            var pending = Json(HttpStatusCode.Accepted, "{}");
            pending.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            Responses.Enqueue(pending);
            Responses.Enqueue(Json(HttpStatusCode.OK, Document));
            var provider = Provider();

            var first = await provider.GetAsync([A, B]);
            var second = await provider.GetAsync([A, B]);

            Assert.Equal((CompareState.Pending, 7), (first.State, first.RetryAfter));
            Assert.Equal(CompareState.Ready, second.State);
        }

        [Fact]
        public async Task A_provider_speaking_another_contract_is_not_believed()
        {
            Responses.Enqueue(Json(HttpStatusCode.OK, Document.Replace("\"contract\":1", "\"contract\":2")));

            var answer = await Provider().GetAsync([A, B]);

            Assert.Equal(CompareState.Unavailable, answer.State);
            Assert.Null(answer.Document);
        }

        [Fact]
        public async Task An_answer_about_a_different_number_of_files_is_not_believed()
        {
            Responses.Enqueue(Json(HttpStatusCode.OK, Document.Replace("{\"readable\":true},{\"readable\":true}", "{\"readable\":true}")));

            var answer = await Provider().GetAsync([A, B]);

            Assert.Equal(CompareState.Unavailable, answer.State);
            Assert.Null(answer.Document);
        }

        [Fact]
        public async Task A_caller_that_will_not_wait_is_told_pending_and_the_answer_arrives_for_the_next()
        {
            var gate = new TaskCompletionSource();
            Responses.Enqueue(Json(HttpStatusCode.OK, Document));
            var provider = Provider(hold: gate.Task);

            var first = await provider.GetAsync([A, B], wait: false);
            gate.SetResult();
            var second = await provider.GetAsync([A, B]);

            Assert.Equal(CompareState.Pending, first.State);
            Assert.NotNull(first.RetryAfter);
            Assert.Equal(CompareState.Ready, second.State);
            Assert.Single(Requests);
        }

        [Fact]
        public async Task A_busy_provider_is_waited_for_as_pending_and_nothing_is_kept()
        {
            var busy = Json(HttpStatusCode.TooManyRequests, "{\"error\":\"2 comparisons are already running\"}");
            busy.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(15));
            Responses.Enqueue(busy);
            Responses.Enqueue(Json(HttpStatusCode.OK, Document));
            var provider = Provider();

            var first = await provider.GetAsync([A, B]);
            var second = await provider.GetAsync([A, B]);

            Assert.Equal((CompareState.Pending, 15), (first.State, first.RetryAfter));
            Assert.Equal(CompareState.Ready, second.State);
            Assert.Equal(2, Requests.Count);
        }

        [Fact]
        public async Task Past_the_limit_a_new_comparison_is_turned_away_and_never_sent()
        {
            var gate = new TaskCompletionSource();
            var provider = Provider(hold: gate.Task);
            var sets = Enumerable.Range(0, CompareProvider.MaximumInFlight + 1)
                .Select(i => new[] { A, Write(Path.Combine(Downloads, $"other-{i}.flac"), 2) })
                .ToList();

            for (var i = 0; i < CompareProvider.MaximumInFlight; i++)
            {
                Responses.Enqueue(Json(HttpStatusCode.OK, Document));
                Assert.Equal(CompareState.Pending, (await provider.GetAsync(sets[i], wait: false)).State);
            }

            var turnedAway = await provider.GetAsync(sets[^1], wait: false);
            var joined = await provider.GetAsync(sets[0], wait: false);

            Assert.Equal(CompareState.Unavailable, turnedAway.State);
            Assert.Contains("already comparing", turnedAway.Reason);
            Assert.Equal(CompareState.Pending, joined.State);

            // one goes to the provider at a time; the limit is on those waiting as well as the one being answered
            gate.SetResult();

            foreach (var set in sets.Take(CompareProvider.MaximumInFlight))
            {
                Assert.Equal(CompareState.Ready, (await provider.GetAsync(set)).State);
            }

            Assert.Equal(CompareProvider.MaximumInFlight, Requests.Count);
        }

        [Theory]
        [InlineData(HttpStatusCode.OK, CompareState.Ready)]
        [InlineData(HttpStatusCode.Accepted, CompareState.Pending)]
        [InlineData(HttpStatusCode.BadRequest, CompareState.Refused)]
        [InlineData(HttpStatusCode.TooManyRequests, CompareState.Pending)]
        [InlineData(HttpStatusCode.ServiceUnavailable, CompareState.Unavailable)]
        [InlineData(HttpStatusCode.BadGateway, CompareState.Unavailable)]
        public void Each_status_means_what_the_contract_says(HttpStatusCode status, CompareState state)
            => Assert.Equal(state, CompareProvider.Classify(status).State);

        private static FileInfo Write(string path, int length)
        {
            File.WriteAllBytes(path, new byte[length]);
            return new FileInfo(path);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private CompareProvider Provider(string url = "http://provider.invalid/contract", bool enabled = true, Task hold = null)
        {
            var handler = new Handler(async request =>
            {
                // read here, while the request is alive: the client disposes it after sending
                Requests.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync()));

                if (hold is not null)
                {
                    await hold;
                }

                return Responses.Count > 0 ? Responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });

            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));

            return new CompareProvider(factory.Object, new TestOptionsMonitor<slskd.Options>(new slskd.Options
            {
                Transfers = new slskd.Options.TransfersOptions
                {
                    Compare = new slskd.Options.TransfersOptions.CompareOptions
                    {
                        Enabled = enabled,
                        Provider = new slskd.Options.TransfersOptions.CompareProviderOptions
                        {
                            Url = url,
                            Token = "provider-secret",
                            Roots = new Dictionary<string, string> { ["downloads"] = Downloads },
                        },
                    },
                },
            }));
        }

        private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => respond(request);
        }
    }
}
