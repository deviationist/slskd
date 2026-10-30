namespace slskd.Tests.Unit.Player
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using slskd.Player;
    using slskd.Player.Builtin;
    using Xunit;

    /// <summary>
    ///     slskd's own analyser, against signals built here so the right answer is known in advance: content up to a
    ///     known cutoff over a quiet broadband floor, a waveform with its top sliced off, a hole cut into the music, and
    ///     so on -- the ground truth the thresholds were calibrated with, synthesised rather than decoded.
    /// </summary>
    public class BuiltinAnalyserTests
    {
        private const int Rate = 44100;

        [Fact]
        public void Content_to_the_top_is_full_bandwidth_and_clean()
        {
            var m = Measure(Noise(10, seed: 1), Noise(10, seed: 2), Facts("flac"));

            Assert.True(m.CutoffHz >= 20000);
            Assert.Equal(("ok", "clean"), Report.Verdict(m, Report.Findings(m)));
        }

        [Fact]
        public void A_lossless_file_with_a_wall_reads_as_lossy_before_it_was_rewrapped()
        {
            var left = Tones(10, upTo: 16000, seed: 1);
            var m = Measure(left, Tones(10, upTo: 16000, seed: 2), Facts("flac"));
            var findings = Report.Findings(m);
            var bandwidth = findings.Single(f => f.Code == "bandwidth");

            Assert.InRange(m.CutoffHz.Value, 15900, 16300);
            Assert.True(m.CliffDb >= Measurer.WallDb);
            Assert.Equal("bad", bandwidth.Level);
            Assert.StartsWith("wall at 16.", bandwidth.Value);
            Assert.Contains("a 128 kbps encoder", bandwidth.Note);
            Assert.Contains("claims to be lossless", bandwidth.Note);
            Assert.StartsWith("lossy? 16.", Report.Verdict(m, findings).Headline);
        }

        [Fact]
        public void The_same_wall_in_a_lossy_file_is_only_what_the_format_does()
        {
            var m = Measure(Tones(10, upTo: 16000, seed: 1), Tones(10, upTo: 16000, seed: 2), Facts("mp3", bits: null));

            Assert.Equal("warn", Report.Findings(m).Single(f => f.Code == "bandwidth").Level);
        }

        [Fact]
        public void A_sliced_off_waveform_is_clipping_and_says_where()
        {
            // a sine driven past full scale and cut off there, as an over-loud master is
            var wave = Enumerable.Range(0, 10 * Rate).Select(n => (float)Math.Clamp(1.4 * Math.Sin(2 * Math.PI * 220 * n / Rate), -1, 1)).ToArray();
            var m = Measure(wave, Shift(wave), Facts("mp3", bits: null));
            var clipping = Report.Findings(m).Single(f => f.Code == "clipping");

            Assert.Equal("bad", clipping.Level);
            Assert.True(m.LongestRun >= 10);
            Assert.Contains("longest", clipping.Value);
            Assert.True(m.Lanes.ContainsKey("clipping"));
            Assert.Equal(("bad", "clipping"), Report.Verdict(m, Report.Findings(m)));
        }

        [Fact]
        public void A_hole_in_the_music_is_a_dropout_and_silence_at_the_ends_is_not()
        {
            var left = Noise(20, seed: 1);
            var right = Noise(20, seed: 2);

            // a tenth of a second gone at 0:10, and the first and last half second silent, as tracks are cut
            foreach (var channel in new[] { left, right })
            {
                Array.Clear(channel, 10 * Rate, Rate / 10);
                Array.Clear(channel, 0, Rate / 2);
                Array.Clear(channel, channel.Length - (Rate / 2), Rate / 2);
            }

            var m = Measure(left, right, Facts("flac"));
            var dropouts = Report.Findings(m).Single(f => f.Code == "dropouts");

            Assert.Equal(1, m.Dropouts);
            Assert.Equal(10.0, m.DropoutAtSeconds);
            Assert.Equal("1 gap, longest 0.10s at 0:10", dropouts.Value);
            Assert.Equal("warn", dropouts.Level);
        }

        [Fact]
        public void Identical_channels_are_mono_in_a_stereo_file()
        {
            var wave = Noise(10, seed: 1);
            var m = Measure(wave, wave, Facts("flac"));

            Assert.True(m.DualMono);
            Assert.Equal("mono in a stereo file", Report.Findings(m).Single(f => f.Code == "channels").Value);
        }

        [Fact]
        public void Sixteen_bits_shifted_into_twenty_four_is_padding()
        {
            var left = Noise(5, seed: 1);
            var right = Noise(5, seed: 2);
            var facts = Facts("flac", bits: 24);
            var measurer = new Measurer(facts, 2, Math.Pow(2, 31));

            // decoded as 32-bit, as a file declaring more than 16 is: 16-bit content, the low bits never set
            for (var n = 0; n < left.Length; n++)
            {
                long l = (long)(short)(left[n] * 32767) << 16;
                long r = (long)(short)(right[n] * 32767) << 16;
                measurer.Add((float)(l / Math.Pow(2, 31)), (float)(r / Math.Pow(2, 31)), l, r);
            }

            var m = measurer.Finish();

            Assert.Equal(16, m.MeasuredBits);
            Assert.Equal("16 used of 24 declared", Report.Findings(m).Single(f => f.Code == "bits").Value);
        }

        [Fact]
        public void A_high_rate_with_nothing_above_what_48_kHz_holds_is_upsampled()
        {
            const int rate = 96000;
            var left = Tones(3, upTo: 23500, seed: 1, rate: rate);
            var m = Measure(left, Tones(3, upTo: 23500, seed: 2, rate: rate), Facts("flac") with { SampleRate = rate });
            var samplerate = Report.Findings(m).Single(f => f.Code == "samplerate");

            Assert.True(m.UltrasonicDb >= Measurer.UltrasonicEmptyDb);
            Assert.Equal("96 kHz, upsampled", samplerate.Value);
            Assert.Equal("warn", samplerate.Level);
        }

        [Fact]
        public void Times_in_a_finding_become_seek_points()
        {
            var runs = Report.Runs("Play from 3:12 to hear it, not 1:2.");

            Assert.Equal(["Play from ", "3:12", " to hear it, not 1:2."], runs.Select(r => (string)r["text"]));
            Assert.Equal(192.0, runs[1]["seek"]);
        }

        [Fact]
        public void The_summary_names_pcm_by_its_container_and_skips_what_does_not_apply()
        {
            Assert.Equal("AIFF · 16-bit · 44.1 kHz · stereo · 1411 kbps · 4:55",
                Report.Summary(new ContainerFacts("pcm_s16be", "aiff", 44100, 2, 16, 1411200, 295.2)));
            Assert.Equal("MP3 · 44.1 kHz · mono · 320 kbps",
                Report.Summary(new ContainerFacts("mp3", "mp3", 44100, 1, null, 320000, null)));
        }

        [Fact]
        public void The_container_is_read_from_what_ffprobe_writes()
        {
            var facts = BuiltinAnalyser.Facts("""
                { "streams": [ { "codec_name": "pcm_s16be", "sample_rate": "44100", "channels": 2, "bits_per_sample": 0, "bit_rate": "1411200" } ],
                  "format": { "duration": "295.200000", "bit_rate": "1412000", "format_name": "aiff" } }
                """);

            Assert.Equal(new ContainerFacts("pcm_s16be", "aiff", 44100, 2, 16, 1411200, 295.2), facts);
        }

        [Fact]
        public void A_flacs_bitrate_counts_its_frames_not_its_tags()
        {
            // no stream bitrate, as FLAC reports none: the frames over the length, not the container's figure
            var facts = BuiltinAnalyser.Facts(
                """{ "streams": [ { "codec_name": "flac", "sample_rate": "44100", "channels": 2, "bits_per_raw_sample": "16" } ], "format": { "duration": "10", "bit_rate": "999999" } }""",
                flacAudioBytes: 1_000_000);

            Assert.Equal(800_000, facts.BitRate);
        }

        [Fact]
        public void A_flacs_metadata_blocks_are_left_out_of_its_audio()
        {
            var path = Path.GetTempFileName();

            try
            {
                // fLaC, a 34-byte STREAMINFO, a last 100-byte PADDING block, then 1000 bytes of frames
                var bytes = new List<byte>("fLaC"u8.ToArray());
                bytes.AddRange([0x00, 0x00, 0x00, 34]);
                bytes.AddRange(new byte[34]);
                bytes.AddRange([0x81, 0x00, 0x00, 100]);
                bytes.AddRange(new byte[100]);
                bytes.AddRange(new byte[1000]);
                File.WriteAllBytes(path, bytes.ToArray());

                Assert.Equal(1000, BuiltinAnalyser.FlacAudioBytes(new FileInfo(path)));

                File.WriteAllBytes(path, new byte[200]);
                Assert.Null(BuiltinAnalyser.FlacAudioBytes(new FileInfo(path)));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task A_file_is_answered_in_the_contracts_shape_under_its_audio_and_the_analysers_version()
        {
            var left = Tones(6, upTo: 16000, seed: 1);
            var right = Tones(6, upTo: 16000, seed: 2);
            var pcm = new byte[left.Length * 4];

            for (var n = 0; n < left.Length; n++)
            {
                // rounded, as an encoder does: truncating adds distortion above the cutoff that no real file has
                BitConverter.TryWriteBytes(pcm.AsSpan(n * 4), (short)Math.Clamp(Math.Round(left[n] * 32767), -32768, 32767));
                BitConverter.TryWriteBytes(pcm.AsSpan((n * 4) + 2), (short)Math.Clamp(Math.Round(right[n] * 32767), -32768, 32767));
            }

            var tool = new FakeMediaTool
            {
                Probe = """{ "streams": [ { "codec_name": "flac", "sample_rate": "44100", "channels": 2, "bits_per_raw_sample": "16" } ], "format": { "duration": "6", "format_name": "flac" } }""",
                DecodedPcm = pcm,
                Chunk = 1001,
            };

            var file = new FileInfo(Path.GetTempFileName());

            try
            {
                var document = await new BuiltinAnalyser(tool).AnalyseAsync(file);

                Assert.Equal(AnalysisProvider.Contract, document.GetProperty("contract").GetInt32());
                Assert.Equal($"{new string('c', 64)}-{BuiltinAnalyser.Version}", document.GetProperty("id").GetString());
                Assert.Equal("slskd", document.GetProperty("provider").GetProperty("name").GetString());
                Assert.Equal(BuiltinAnalyser.Version, document.GetProperty("provider").GetProperty("version").GetString());
                Assert.Equal("bad", document.GetProperty("verdict").GetProperty("level").GetString());
                Assert.Equal(8, document.GetProperty("findings").GetArrayLength());
                Assert.Equal("bandwidth", document.GetProperty("findings")[0].GetProperty("id").GetString());
                Assert.Contains("decode:s16le:2:44100", tool.Calls);
            }
            finally
            {
                file.Delete();
            }
        }

        private static Measurements Measure(float[] left, float[] right, ContainerFacts facts)
        {
            var measurer = new Measurer(facts, 2, 32768);

            for (var n = 0; n < left.Length; n++)
            {
                measurer.Add(left[n], right[n], (long)(left[n] * 32767), (long)(right[n] * 32767));
            }

            return measurer.Finish();
        }

        private static ContainerFacts Facts(string codec, int? bits = 16)
            => new(codec, codec, Rate, 2, bits, codec == "mp3" ? 320000 : 900000, 10);

        // broadband, as real content and a real noise floor both are
        private static float[] Noise(int seconds, int seed, double level = 0.2, int rate = Rate)
        {
            var random = new Random(seed);
            return Enumerable.Range(0, seconds * rate).Select(_ => (float)(Gaussian(random) * level)).ToArray();
        }

        // content up to a cutoff and none above it, over a floor 90 dB down: what a lossy encoder leaves. dense for the
        // last few kHz, as music is -- sparse tones leave the bands between them at the floor, and no wall reads as one
        private static float[] Tones(int seconds, double upTo, int seed, int rate = Rate)
        {
            var random = new Random(seed);
            var tones = Enumerable.Range(0, 150).Select(i => upTo - (i * 40.0))
                .Concat(Enumerable.Range(1, 20).Select(i => i * 250.0))
                .Select(freq => (Freq: freq, Phase: random.NextDouble() * 2 * Math.PI))
                .ToArray();
            var floor = Noise(seconds, seed + 100, level: 3e-5, rate: rate);
            var amplitude = 0.25 / Math.Sqrt(tones.Length);
            var wave = new float[seconds * rate];

            for (var n = 0; n < wave.Length; n++)
            {
                var t = n / (double)rate;
                double sum = 0;

                foreach (var (freq, phase) in tones)
                {
                    sum += Math.Sin((2 * Math.PI * freq * t) + phase);
                }

                wave[n] = (float)((sum * amplitude) + floor[n]);
            }

            return wave;
        }

        private static float[] Shift(float[] wave) => wave.Skip(37).Concat(wave.Take(37)).ToArray();

        private static double Gaussian(Random random)
            => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
    }
}
