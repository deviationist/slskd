namespace slskd.Tests.Unit.Player
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using slskd.Player;
    using Xunit;

    /// <summary>
    ///     ffmpeg stood in for: a transcode writes what it is told to, and a probe answers what it is told to.
    /// </summary>
    internal class FakeMediaTool : IMediaTool
    {
        public List<string> Calls { get; } = new();
        public double? SourceDuration { get; set; } = 300;
        public double? OutputDuration { get; set; } = 300;
        public TaskCompletionSource Gate { get; set; }
        public byte[] Pcm { get; set; } = Enumerable.Repeat((byte)0x40, 1000).ToArray();

        public Task<byte[]> DecodeAsync(string source, CancellationToken cancellationToken = default)
        {
            lock (Calls)
            {
                Calls.Add("decode");
            }

            return Task.FromResult(Pcm);
        }

        public async Task TranscodeAsync(string source, string destination, Codec codec, CancellationToken cancellationToken = default)
        {
            lock (Calls)
            {
                Calls.Add("transcode:" + codec.Name);
            }

            if (Gate is not null)
            {
                await Gate.Task;
            }

            await File.WriteAllBytesAsync(destination, new byte[] { 1, 2, 3 }, cancellationToken);
        }

        public Task<double?> ProbeDurationAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(Path.GetFileName(path).StartsWith('.') ? OutputDuration : SourceDuration);
    }

    public class PlayerServiceTests : IDisposable
    {
        public PlayerServiceTests()
        {
            Temp = Path.Combine(Path.GetTempPath(), $"slskd.test.{Guid.NewGuid()}");
            Cache = Path.Combine(Temp, "cache");
            Directory.CreateDirectory(Temp);
            Source = new FileInfo(Path.Combine(Temp, "01 Track.aiff"));
            File.WriteAllBytes(Source.FullName, new byte[] { 9, 9, 9 });

            Tool = new FakeMediaTool();
            Service = new PlayerService(
                new TestOptionsMonitor<slskd.Options>(new slskd.Options
                {
                    Player = new slskd.Options.PlayerOptions
                    {
                        Cache = new slskd.Options.PlayerOptions.PlayerCacheOptions { Directory = Cache },
                    },
                }),
                Tool);
        }

        private string Temp { get; }
        private string Cache { get; }
        private FileInfo Source { get; }
        private FakeMediaTool Tool { get; }
        private PlayerService Service { get; }

        public void Dispose() => Directory.Delete(Temp, recursive: true);

        [Fact]
        public async Task A_waveform_is_built_once_and_then_read_back()
        {
            var first = await Service.PeaksAsync(Source);
            var second = await Service.PeaksAsync(Source);

            Assert.Equal(first.Peaks, second.Peaks);
            Assert.Equal(first.Duration, second.Duration);
            Assert.Equal(new[] { "decode" }, Tool.Calls);
        }

        [Fact]
        public async Task A_changed_file_is_a_new_waveform()
        {
            await Service.PeaksAsync(Source);
            File.WriteAllBytes(Source.FullName, new byte[] { 1, 2, 3, 4, 5 });
            await Service.PeaksAsync(new FileInfo(Source.FullName));

            Assert.Equal(new[] { "decode", "decode" }, Tool.Calls);
        }

        [Fact]
        public async Task A_checked_transcode_is_served_from_the_cache_the_second_time()
        {
            var (path, type) = await Service.AudioAsync(Source, Codecs.Find("opus"));
            var (again, _) = await Service.AudioAsync(Source, Codecs.Find("opus"));

            Assert.Equal(path, again);
            Assert.Equal("audio/webm", type);
            Assert.Equal(new[] { "transcode:opus" }, Tool.Calls);
        }

        [Fact]
        public async Task Each_codec_has_its_own_receipt_so_one_does_not_rebuild_the_other()
        {
            await Service.AudioAsync(Source, Codecs.Find("opus"));
            await Service.AudioAsync(Source, Codecs.Find("aac"));
            await Service.AudioAsync(Source, Codecs.Find("opus"));
            await Service.AudioAsync(Source, Codecs.Find("aac"));

            Assert.Equal(new[] { "transcode:opus", "transcode:aac" }, Tool.Calls);
        }

        [Fact]
        public async Task A_transcode_without_a_receipt_is_built_again()
        {
            var (path, _) = await Service.AudioAsync(Source, Codecs.Find("opus"));
            File.Delete(Directory.GetFiles(Cache, "*.audio.opus.json").Single());

            await Service.AudioAsync(Source, Codecs.Find("opus"));

            Assert.Equal(new[] { "transcode:opus", "transcode:opus" }, Tool.Calls);
            Assert.True(File.Exists(path));
        }

        [Theory]
        [InlineData(250.0)]
        [InlineData(3600.0)]
        [InlineData(null)]
        public async Task A_transcode_that_is_not_finished_is_never_published(double? got)
        {
            Tool.OutputDuration = got;

            await Assert.ThrowsAsync<MediaToolException>(() => Service.AudioAsync(Source, Codecs.Find("opus")));

            // no entry, no receipt, and no staged leftover that could pass for either
            Assert.Empty(Directory.GetFiles(Cache));
        }

        [Fact]
        public async Task A_small_mp3_is_sent_as_it_is()
        {
            var mp3 = new FileInfo(Path.Combine(Temp, "x.mp3"));
            File.WriteAllBytes(mp3.FullName, new byte[] { 1 });

            var (path, type) = await Service.AudioAsync(mp3, Codecs.Find("opus"));

            Assert.Equal(mp3.FullName, path);
            Assert.Equal("audio/mpeg", type);
            Assert.Empty(Tool.Calls);
        }

        [Fact]
        public async Task The_waveform_does_not_wait_for_a_running_transcode()
        {
            Tool.Gate = new TaskCompletionSource();
            var audio = Service.AudioAsync(Source, Codecs.Find("opus"));

            var peaks = Service.PeaksAsync(Source);
            var finished = await Task.WhenAny(peaks, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.Same(peaks, finished);
            Tool.Gate.SetResult();
            await audio;
        }

        [Fact]
        public async Task Two_requests_for_one_transcode_build_it_once()
        {
            Tool.Gate = new TaskCompletionSource();
            var first = Service.AudioAsync(Source, Codecs.Find("opus"));
            var second = Service.AudioAsync(Source, Codecs.Find("opus"));

            Tool.Gate.SetResult();
            await Task.WhenAll(first, second);

            Assert.Equal(new[] { "transcode:opus" }, Tool.Calls);
        }

        [Fact]
        public void Eviction_drops_the_least_recently_used_and_leaves_staged_files_alone()
        {
            Directory.CreateDirectory(Cache);
            var old = Path.Combine(Cache, "old.webm");
            var recent = Path.Combine(Cache, "recent.webm");
            var staged = Path.Combine(Cache, ".building.part.webm");
            File.WriteAllBytes(old, new byte[100]);
            File.WriteAllBytes(recent, new byte[100]);
            File.WriteAllBytes(staged, new byte[100]);
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-2));
            File.SetLastWriteTimeUtc(staged, DateTime.UtcNow.AddHours(-3));

            PlayerService.Evict(Cache, capacity: 150);

            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
            Assert.True(File.Exists(staged));
        }
    }
}
