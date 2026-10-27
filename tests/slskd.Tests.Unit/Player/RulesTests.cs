namespace slskd.Tests.Unit.Player
{
    using System.Collections.Generic;
    using System.Linq;
    using slskd.Player;
    using Xunit;

    public class AnalysisRootsTests
    {
        [Fact]
        public void A_good_table_has_no_problems()
        {
            var roots = new Dictionary<string, string> { ["downloads"] = "/app/downloads", ["shared"] = "/app/shared" };
            Assert.Empty(AnalysisRoots.Problems(roots));
        }

        [Theory]
        [InlineData("down/loads")]
        [InlineData("..")]
        [InlineData("a b")]
        [InlineData("")]
        public void A_name_that_is_not_a_plain_word_is_a_problem(string name)
        {
            Assert.Single(AnalysisRoots.Problems(new Dictionary<string, string> { [name] = "/app/downloads" }));
        }

        [Fact]
        public void A_relative_folder_is_a_problem()
        {
            Assert.Single(AnalysisRoots.Problems(new Dictionary<string, string> { ["a"] = "app/downloads" }));
        }

        [Theory]
        [InlineData("/app", "/app/downloads")]
        [InlineData("/app/downloads", "/app")]
        [InlineData("/app/downloads", "/app/downloads/")]
        public void Folders_inside_one_another_overlap_in_either_order(string first, string second)
        {
            var problems = AnalysisRoots.Problems(new Dictionary<string, string> { ["a"] = first, ["b"] = second }).ToList();
            Assert.Contains(problems, p => p.Contains("overlap"));
        }

        [Fact]
        public void A_shared_prefix_is_not_an_overlap()
        {
            Assert.Empty(AnalysisRoots.Problems(new Dictionary<string, string> { ["a"] = "/app/down", ["b"] = "/app/downloads" }));
        }

        [Theory]
        [InlineData("complete/Some User/Album/01 Track.aiff")]
        [InlineData("x.flac")]
        public void A_plain_relative_path_is_clean(string path) => Assert.True(AnalysisRoots.IsClean(path));

        [Theory]
        [InlineData("")]
        [InlineData("../secret/out.flac")]
        [InlineData("a/../b.flac")]
        [InlineData("a/./b.flac")]
        [InlineData("a//b.flac")]
        [InlineData("/etc/x.flac")]
        [InlineData("C:/x.flac")]
        [InlineData("a\\b.flac")]
        [InlineData("a/.hidden.flac")]
        [InlineData("a\0b.flac")]
        public void A_path_with_any_unsafe_part_is_refused_rather_than_repaired(string path)
            => Assert.False(AnalysisRoots.IsClean(path));

        [Fact]
        public void A_file_is_named_by_the_root_holding_it_and_the_path_below()
        {
            var roots = new Dictionary<string, string> { ["downloads"] = "/app/downloads", ["shared"] = "/app/shared" };
            Assert.Equal(("downloads", "complete/U/A/01 T.aiff"), AnalysisRoots.Locate(roots, "/app/downloads/complete/U/A/01 T.aiff"));
            Assert.Equal(("shared", "x.MP3"), AnalysisRoots.Locate(roots, "/app/shared/x.MP3"));
        }

        [Theory]
        [InlineData("/elsewhere/x.flac")]
        [InlineData("/app/downloads-old/x.flac")]
        [InlineData("/app/downloads/cover.jpg")]
        [InlineData("/app/downloads/.partial/x.flac")]
        [InlineData("/app/downloads")]
        public void A_file_outside_every_root_or_not_audio_is_never_named(string filename)
        {
            var roots = new Dictionary<string, string> { ["downloads"] = "/app/downloads" };
            Assert.Null(AnalysisRoots.Locate(roots, filename));
        }
    }

    public class CodecsTests
    {
        [Fact]
        public void No_name_is_the_default() => Assert.Equal(Codecs.Default, Codecs.Find(null).Name);

        [Theory]
        [InlineData("aac", "audio/mp4")]
        [InlineData("opus", "audio/webm")]
        public void A_known_name_is_its_codec(string name, string type) => Assert.Equal(type, Codecs.Find(name).ContentType);

        [Theory]
        [InlineData("../../etc")]
        [InlineData("OPUS")]
        [InlineData("flac")]
        public void A_name_outside_the_table_is_refused_not_defaulted(string name) => Assert.Null(Codecs.Find(name));

        [Fact]
        public void Every_transcode_drops_video_is_not_left_to_the_codec()
        {
            // -vn is passed by the tool for every codec; the codec arguments must not try to carry a video stream
            Assert.All(Codecs.ByName.Values, c => Assert.DoesNotContain("-c:v", c.Arguments));
        }

        [Fact]
        public void A_small_mp3_passes_through_and_a_large_one_does_not()
        {
            Assert.Equal("audio/mpeg", Codecs.PassthroughType("x.mp3", Codecs.PassthroughMaxBytes));
            Assert.Null(Codecs.PassthroughType("x.mp3", Codecs.PassthroughMaxBytes + 1));
        }

        [Theory]
        [InlineData("x.aiff")]
        [InlineData("x.flac")]
        [InlineData("x.wav")]
        public void Anything_else_is_transcoded(string name) => Assert.Null(Codecs.PassthroughType(name, 10));
    }

    public class WaveformTests
    {
        [Fact]
        public void A_bucket_is_its_loudest_sample_not_its_mean()
        {
            var samples = new short[] { 0, 0, 0, 16384, 0, 0, 0, 0 };
            Assert.Equal(new[] { 0.5, 0.0 }, Waveform.Bucket(samples, 2));
        }

        [Fact]
        public void The_most_negative_sample_is_full_scale_and_not_past_it()
        {
            Assert.Equal(new[] { 1.0 }, Waveform.Bucket(new short[] { short.MinValue }, 1));
            Assert.Equal(new[] { 1.0 }, Waveform.Bucket(new short[] { short.MaxValue }, 1));
        }

        [Fact]
        public void Fewer_samples_than_buckets_gives_one_bucket_per_sample()
            => Assert.Equal(3, Waveform.Bucket(new short[] { 1, 2, 3 }, 1600).Length);

        [Fact]
        public void Nothing_decoded_is_no_waveform() => Assert.Empty(Waveform.Bucket(new short[0]));

        [Fact]
        public void Pcm_is_read_little_endian_and_an_odd_byte_dropped()
            => Assert.Equal(new short[] { 0x0201, -1 }, Waveform.Samples(new byte[] { 0x01, 0x02, 0xff, 0xff, 0x09 }));

        [Fact]
        public void The_duration_is_the_sample_count_not_a_header()
            => Assert.Equal(1.5, Waveform.Duration(Waveform.PeakRate * 3 / 2));

        [Fact]
        public void A_transcode_as_long_as_its_source_is_served()
            => Assert.Null(Waveform.Refusal(377.3, 377.354));

        [Fact]
        public void A_transcode_that_stopped_early_is_refused()
            => Assert.Contains("stopped early", Waveform.Refusal(327.0, 377.354));

        [Fact]
        public void A_transcode_whose_header_was_never_rewritten_is_refused()
        {
            // the 2026-09-07 file: 5 MiB of audio under a header claiming -t's placeholder. a "not shorter" check passes it
            Assert.Contains("never finalised", Waveform.Refusal(Codecs.MaxSeconds, 377.354));
        }

        [Fact]
        public void A_transcode_with_no_duration_did_not_finish()
            => Assert.NotNull(Waveform.Refusal(null, 377.0));

        [Fact]
        public void A_source_that_will_not_say_is_accepted_unchecked()
            => Assert.Null(Waveform.Refusal(200.0, null));

        [Fact]
        public void A_source_longer_than_the_cap_is_held_to_the_cap()
        {
            Assert.Null(Waveform.Refusal(Codecs.MaxSeconds, 7200.0));
            Assert.NotNull(Waveform.Refusal(Codecs.MaxSeconds - 10, 7200.0));
        }
    }
}
