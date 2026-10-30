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
        public IReadOnlyList<string> LastArguments { get; private set; }
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
                LastArguments = codec.Arguments;
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

        [Fact]
        public async Task A_transcode_is_made_at_the_bitrate_set()
        {
            await Service.AudioAsync(Source, Codecs.Find("opus"));

            Assert.Equal("320k", Tool.LastArguments[Tool.LastArguments.ToList().IndexOf("-b:a") + 1]);
        }

        [Fact]
        public async Task A_transcode_made_at_another_bitrate_is_made_again()
        {
            // the cache from before the setting: every transcode in it was made at 128k
            await ServiceAt(128).AudioAsync(Source, Codecs.Find("opus"));
            await ServiceAt(128).AudioAsync(Source, Codecs.Find("opus"));
            await ServiceAt(320).AudioAsync(Source, Codecs.Find("opus"));

            Assert.Equal(new[] { "transcode:opus", "transcode:opus" }, Tool.Calls);
        }

        [Fact]
        public void A_bitrate_is_held_to_what_the_encoder_takes()
        {
            Assert.Equal(320, Codecs.Find("mp3").BitrateAt(512));
            Assert.Equal(510, Codecs.Find("opus").BitrateAt(512));
            Assert.Contains("256k", Codecs.Find("aac").ArgumentsAt(256));
            Assert.All(Codecs.ByName.Values, c => Assert.DoesNotContain(Codecs.Bitrate, c.ArgumentsAt(320)));
        }

        [Fact]
        public void Only_formats_every_browser_may_play_are_offered_as_they_are()
        {
            // no browser plays AIFF, and an M4A may be ALAC, which Chrome does not
            Assert.Contains(".flac", Codecs.Originals.Keys);
            Assert.DoesNotContain(".aiff", Codecs.Originals.Keys);
            Assert.DoesNotContain(".aif", Codecs.Originals.Keys);
            Assert.DoesNotContain(".m4a", Codecs.Originals.Keys);
        }

        private PlayerService ServiceAt(int kbps) => new(
            new TestOptionsMonitor<slskd.Options>(new slskd.Options
            {
                Player = new slskd.Options.PlayerOptions
                {
                    Bitrate = kbps,
                    Cache = new slskd.Options.PlayerOptions.PlayerCacheOptions { Directory = Cache },
                },
            }),
            Tool);

        [Fact]
        public async Task A_file_about_to_be_deleted_takes_its_waveform_and_transcodes_with_it()
        {
            await Service.PeaksAsync(Source);
            await Service.AudioAsync(Source, Codecs.Find("opus"));
            await Service.AudioAsync(Source, Codecs.Find("aac"));

            var other = new FileInfo(Path.Combine(Temp, "02 Other.aiff"));
            File.WriteAllBytes(other.FullName, new byte[] { 7 });
            await Service.PeaksAsync(other);

            // a waveform, and two transcodes with their receipts
            Assert.Equal(5, Service.Forget(Source));

            Assert.Equal([$"{PlayerService.Identity(other)}.peaks.json"], Directory.GetFiles(Cache).Select(Path.GetFileName));
        }

        [Fact]
        public void A_file_already_gone_has_nothing_to_forget()
        {
            Source.Delete();

            Assert.Equal(0, Service.Forget(Source));
        }

        [Fact]
        public void A_transcode_unused_for_the_ttl_goes_with_its_receipt_and_waveforms_stay()
        {
            Directory.CreateDirectory(Cache);
            var now = DateTime.UtcNow;
            var stale = new string('a', 32);
            var fresh = new string('b', 32);

            string Entry(string name, int daysUnused)
            {
                var path = Path.Combine(Cache, name);
                File.WriteAllBytes(path, new byte[] { 1 });
                File.SetLastWriteTimeUtc(path, now.AddDays(-daysUnused));
                return path;
            }

            var staleWebm = Entry($"{stale}.webm", 31);
            var staleReceipt = Entry($"{stale}.audio.opus.json", 31);
            var staleM4a = Entry($"{stale}.m4a", 31);
            var freshWebm = Entry($"{fresh}.webm", 29);
            var oldPeaks = Entry($"{stale}.peaks.json", 300);
            var notOurs = Entry("notes.webm", 300);

            Assert.Equal(2, PlayerService.Expire(Cache, TimeSpan.FromDays(30), now));

            Assert.False(File.Exists(staleWebm));
            Assert.False(File.Exists(staleReceipt));
            Assert.False(File.Exists(staleM4a));
            Assert.True(File.Exists(freshWebm));
            Assert.True(File.Exists(oldPeaks));
            Assert.True(File.Exists(notOurs));
        }
    }
}
