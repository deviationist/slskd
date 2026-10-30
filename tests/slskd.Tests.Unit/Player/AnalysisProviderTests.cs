namespace slskd.Tests.Unit.Player
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Moq;
    using slskd.Player;
    using Xunit;

    public class AnalysisProviderTests : IDisposable
    {
        private const string Document = "{\"contract\":1,\"provider\":{\"name\":\"t\",\"version\":\"1\"},\"verdict\":{\"level\":\"ok\",\"headline\":\"fine\"},\"summary\":\"\",\"findings\":[],\"lanes\":[]}";

        public AnalysisProviderTests()
        {
            Temp = Path.Combine(Path.GetTempPath(), $"slskd.test.{Guid.NewGuid()}");
            Downloads = Path.Combine(Temp, "downloads");
            Directory.CreateDirectory(Path.Combine(Downloads, "complete", "Some User"));
            Track = new FileInfo(Path.Combine(Downloads, "complete", "Some User", "01 Track.aiff"));
            File.WriteAllBytes(Track.FullName, new byte[] { 1, 2, 3 });
        }

        private string Temp { get; }
        private string Downloads { get; }
        private FileInfo Track { get; }
        private List<HttpRequestMessage> Requests { get; } = new();
        private Queue<HttpResponseMessage> Responses { get; } = new();

        public void Dispose() => Directory.Delete(Temp, recursive: true);

        [Fact]
        public async Task With_no_provider_the_answer_is_off_and_nothing_is_asked()
        {
            var answer = await Provider(url: null).GetAsync(Track);

            Assert.Equal(AnalysisState.Off, answer.State);
            Assert.Empty(Requests);
        }

        [Fact]
        public async Task The_file_is_named_by_root_and_relative_path_with_its_size_mtime_and_the_token()
        {
            Responses.Enqueue(Json(HttpStatusCode.OK, Document));

            var answer = await Provider().GetAsync(Track);

            Assert.Equal(AnalysisState.Ready, answer.State);
            var request = Assert.Single(Requests);
            var mtime = new DateTimeOffset(Track.LastWriteTimeUtc).ToUnixTimeMilliseconds();
            Assert.Equal(
                $"http://provider.invalid/contract/v1/analysis?root=downloads&file=complete%2FSome%20User%2F01%20Track.aiff&size=3&mtime={mtime}",
                request.RequestUri.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
            Assert.Equal("provider-secret", request.Headers.Authorization.Parameter);
        }

        [Fact]
        public async Task An_answer_is_kept_for_the_file_as_it_is()
        {
            Responses.Enqueue(Json(HttpStatusCode.OK, Document));
            var provider = Provider();

            await provider.GetAsync(Track);
            var again = await provider.GetAsync(Track);

            Assert.Equal(AnalysisState.Ready, again.State);
            Assert.Single(Requests);
        }

        [Fact]
        public async Task A_file_in_no_configured_root_is_never_sent()
        {
            var outside = new FileInfo(Path.Combine(Temp, "elsewhere.flac"));
            File.WriteAllBytes(outside.FullName, new byte[] { 1 });

            var answer = await Provider().GetAsync(outside);

            Assert.Equal(AnalysisState.None, answer.State);
            Assert.Empty(Requests);
        }

        [Fact]
        public async Task A_conflict_is_asked_about_once_more_then_given_up()
        {
            Responses.Enqueue(Json(HttpStatusCode.Conflict, "{}"));
            Responses.Enqueue(Json(HttpStatusCode.Conflict, "{}"));

            var answer = await Provider().GetAsync(Track);

            Assert.Equal(AnalysisState.None, answer.State);
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

            var first = await provider.GetAsync(Track);
            var second = await provider.GetAsync(Track);

            Assert.Equal((AnalysisState.Pending, 7), (first.State, first.RetryAfter));
            Assert.Equal(AnalysisState.Ready, second.State);
        }

        [Fact]
        public async Task A_provider_speaking_another_contract_is_not_believed()
        {
            Responses.Enqueue(Json(HttpStatusCode.OK, Document.Replace("\"contract\":1", "\"contract\":2")));

            var answer = await Provider().GetAsync(Track);

            Assert.Equal(AnalysisState.Unavailable, answer.State);
            Assert.Null(answer.Analysis);
        }

        [Fact]
        public async Task A_caller_that_will_not_wait_is_told_pending_and_the_answer_arrives_for_the_next()
        {
            var gate = new TaskCompletionSource();
            Responses.Enqueue(Json(HttpStatusCode.OK, Document));
            var provider = Provider(hold: gate.Task);

            var first = await provider.GetAsync(Track, wait: false);
            gate.SetResult();
            var second = await provider.GetAsync(Track);

            Assert.Equal(AnalysisState.Pending, first.State);
            Assert.NotNull(first.RetryAfter);
            Assert.Equal(AnalysisState.Ready, second.State);
            Assert.Single(Requests);
        }

        [Fact]
        public async Task A_finished_analysis_is_kept_for_its_download_even_by_a_caller_that_left()
        {
            var gate = new TaskCompletionSource();
            var kept = new List<(Guid, string)>();
            var download = Guid.NewGuid();
            Responses.Enqueue(Json(HttpStatusCode.OK, Document));
            var provider = Provider(hold: gate.Task, keep: (id, document) => kept.Add((id, document.GetProperty("verdict").GetProperty("headline").GetString())));

            await provider.GetAsync(Track, wait: false, download: download);
            gate.SetResult();
            await provider.GetAsync(Track);

            Assert.Equal([(download, "fine")], kept);
        }

        [Fact]
        public async Task Nothing_but_a_finished_analysis_is_kept()
        {
            var kept = 0;
            Responses.Enqueue(Json(HttpStatusCode.NotFound, "{}"));

            await Provider(keep: (_, _) => kept++).GetAsync(Track, download: Guid.NewGuid());

            Assert.Equal(0, kept);
        }

        [Fact]
        public async Task Analysis_is_off_until_a_source_is_chosen()
        {
            // a provider set up but not chosen: opt-in means nothing is asked until source says so
            var options = new slskd.Options.PlayerOptions.PlayerAnalysisOptions
            {
                Provider = new slskd.Options.PlayerOptions.PlayerAnalysisProviderOptions { Url = "http://provider.invalid/contract", Token = "t" },
            };

            Assert.Equal("off", options.Source);
            Assert.False(options.Enabled);
            Assert.Equal(AnalysisState.Off, (await Provider(source: "off").GetAsync(Track)).State);
            Assert.Empty(Requests);
        }

        [Fact]
        public async Task The_builtin_analyser_answers_without_asking_anyone_and_wherever_the_file_is()
        {
            // outside every root: the roots name folders to a provider elsewhere, and slskd's own analyser needs none
            var outside = new FileInfo(Path.Combine(Temp, "elsewhere.flac"));
            File.WriteAllBytes(outside.FullName, new byte[] { 1 });
            var tool = new FakeMediaTool
            {
                Probe = """{ "streams": [ { "codec_name": "flac", "sample_rate": "44100", "channels": 1, "bits_per_raw_sample": "16" } ], "format": { "duration": "1" } }""",
                DecodedPcm = Enumerable.Range(0, 44100).SelectMany(n => BitConverter.GetBytes((short)(8000 * Math.Sin(n / 7.0)))).ToArray(),
            };

            var answer = await Provider(source: "builtin", builtin: new slskd.Player.Builtin.BuiltinAnalyser(tool)).GetAsync(outside);

            Assert.Equal(AnalysisState.Ready, answer.State);
            Assert.Equal("slskd", answer.Analysis.Value.GetProperty("provider").GetProperty("name").GetString());
            Assert.Empty(Requests);
        }

        [Fact]
        public async Task A_file_the_builtin_analyser_cannot_decode_has_nothing_to_show()
        {
            var answer = await Provider(source: "builtin", builtin: new slskd.Player.Builtin.BuiltinAnalyser(new FakeMediaTool { Probe = "{}" })).GetAsync(Track);

            Assert.Equal(AnalysisState.Unavailable, answer.State);
        }

        [Theory]
        [InlineData("off", true)]
        [InlineData("builtin", true)]
        [InlineData("provider", false)]
        [InlineData("both", false)]
        public void A_source_is_one_of_three_and_provider_needs_a_url(string source, bool valid)
        {
            var options = new slskd.Options.PlayerOptions.PlayerAnalysisOptions { Source = source };

            Assert.Equal(valid, !options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)).Any());
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden, AnalysisState.None)]
        [InlineData(HttpStatusCode.NotFound, AnalysisState.None)]
        [InlineData(HttpStatusCode.ServiceUnavailable, AnalysisState.Unavailable)]
        [InlineData(HttpStatusCode.BadGateway, AnalysisState.Unavailable)]
        public void Each_status_means_what_the_contract_says(HttpStatusCode status, AnalysisState state)
            => Assert.Equal(state, AnalysisProvider.Classify(status, null).State);

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private AnalysisProvider Provider(string url = "http://provider.invalid/contract", Task hold = null, Action<Guid, System.Text.Json.JsonElement> keep = null, string source = null, slskd.Player.Builtin.BuiltinAnalyser builtin = null)
        {
            var handler = new Handler(async request =>
            {
                Requests.Add(request);

                if (hold is not null)
                {
                    await hold;
                }

                return Responses.Count > 0 ? Responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });

            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));

            return new AnalysisProvider(factory.Object, new TestOptionsMonitor<slskd.Options>(new slskd.Options
            {
                Player = new slskd.Options.PlayerOptions
                {
                    Analysis = new slskd.Options.PlayerOptions.PlayerAnalysisOptions
                    {
                        Source = source ?? (url is null ? "off" : "provider"),
                        Provider = new slskd.Options.PlayerOptions.PlayerAnalysisProviderOptions
                        {
                            Url = url,
                            Token = "provider-secret",
                            Roots = new Dictionary<string, string> { ["downloads"] = Downloads },
                        },
                    },
                },
            }), keep is null ? null : new KeepingStore(keep), builtin);
        }

        // the cache, standing in for the database: what was kept, as it was handed over
        private sealed class KeepingStore(Action<Guid, System.Text.Json.JsonElement> keep) : AnalysisStore(null)
        {
            public override void Save(Guid download, System.Text.Json.JsonElement document) => keep(download, document);
        }

        private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => respond(request);
        }
    }
}
