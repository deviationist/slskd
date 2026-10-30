namespace slskd.Tests.Unit.Transfers.Compare
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using slskd.Tests.Unit.Player;
    using slskd.Transfers.Compare;
    using slskd.Transfers.Compare.Builtin;
    using Xunit;

    /// <summary>
    ///     slskd's own Compare, against music built here: a track with its own rhythm in each band, and copies of it
    ///     delayed, turned down, cut short, slowed or swapped for another, so the right answer is known in advance.
    /// </summary>
    public class BuiltinCompareTests
    {
        private const int Rate = SameTrack.Rate;

        [Fact]
        public void A_copy_that_starts_later_and_quieter_is_the_same_track_and_says_how_it_differs()
        {
            var track = Track(60, seed: 1);
            var copy = Delay(track, seconds: 1.2).Select(s => s * 0.5f).ToArray();

            var m = SameTrack.Match(Features(track), Features(copy));

            Assert.Equal("same", m.Verdict);
            Assert.True(m.Score >= CompareDocument.CloseMatch, $"score {m.Score}");
            Assert.InRange(m.OffsetSeconds, 1.1, 1.3);
            Assert.InRange(m.GainDb, -6.5, -5.5);
        }

        [Fact]
        public void Two_different_tracks_are_different()
        {
            var m = SameTrack.Match(Features(Track(60, seed: 1)), Features(Track(60, seed: 2)));

            Assert.Equal("different", m.Verdict);
            Assert.True(m.Share < SameTrack.PartlyShare);
        }

        [Fact]
        public void An_edit_sharing_half_its_length_is_partly_the_same()
        {
            var track = Track(80, seed: 1);
            var other = Track(80, seed: 2);
            var edit = track.Take(40 * Rate).Concat(other.Skip(40 * Rate)).ToArray();

            var m = SameTrack.Match(Features(track), Features(edit));

            Assert.Equal("partly", m.Verdict);
        }

        [Fact]
        public void A_copy_running_slow_is_the_same_track_with_a_drift()
        {
            // a tenth of a percent slow, as a vinyl rip can be: 80 ms over the track
            var track = Track(80, seed: 1);
            var slow = Resample(track, 1.001);

            var m = SameTrack.Match(Features(track), Features(slow));

            Assert.Equal("same", m.Verdict);
            Assert.InRange(m.DriftMs, 50, 120);
        }

        [Fact]
        public void A_file_too_short_to_compare_cannot_be_read()
        {
            var builder = new SameTrack.Builder();

            foreach (var sample in Track(5, seed: 1))
            {
                builder.Add(sample);
            }

            Assert.Throws<InvalidOperationException>(() => builder.Finish());
        }

        [Fact]
        public void Two_copies_of_one_track_get_a_yes_and_the_differences_in_words()
        {
            var track = Track(60, seed: 1);
            var document = BuiltinCompare.Compare([new ComparedFile(Features(track)), new ComparedFile(Features(Delay(track, 1.2)))]);
            var verdict = document.GetProperty("verdict");

            Assert.Equal(CompareProvider.Contract, document.GetProperty("contract").GetInt32());
            Assert.Equal("slskd", document.GetProperty("provider").GetProperty("name").GetString());
            Assert.Equal(BuiltinCompare.Version, document.GetProperty("provider").GetProperty("version").GetString());
            Assert.Equal("same", verdict.GetProperty("level").GetString());
            Assert.Equal("Yes — the same track", verdict.GetProperty("headline").GetString());
            Assert.Contains("starts 1.2 s later", verdict.GetProperty("detail").GetString());
            Assert.Equal(2, document.GetProperty("files").GetArrayLength());
            Assert.Single(document.GetProperty("pairs").EnumerateArray());
        }

        [Fact]
        public void Three_files_two_alike_are_grouped_and_an_unreadable_one_is_said_so()
        {
            var track = Track(60, seed: 1);
            var document = BuiltinCompare.Compare(
            [
                new ComparedFile(Features(track)),
                new ComparedFile(Features(Track(60, seed: 2))),
                new ComparedFile(Features(Delay(track, 0.5))),
                new ComparedFile(null),
            ]);

            Assert.Equal("mixed", document.GetProperty("verdict").GetProperty("level").GetString());
            Assert.Equal("2 of 3 are the same track", document.GetProperty("verdict").GetProperty("headline").GetString());
            Assert.Equal([0, 2], document.GetProperty("groups")[0].GetProperty("files").EnumerateArray().Select(e => e.GetInt32()));
            Assert.Equal([1], document.GetProperty("alone").EnumerateArray().Select(e => e.GetInt32()));
            Assert.False(document.GetProperty("files")[3].GetProperty("readable").GetBoolean());
            Assert.Equal(CompareDocument.Unreadable, document.GetProperty("files")[3].GetProperty("note").GetString());

            // unreadable files are in no pair: three readable make three pairs
            Assert.Equal(3, document.GetProperty("pairs").GetArrayLength());
        }

        [Fact]
        public void Fewer_than_two_readable_files_cannot_be_compared()
        {
            var document = BuiltinCompare.Compare([new ComparedFile(Features(Track(30, seed: 1))), new ComparedFile(null)]);

            Assert.Equal("unknown", document.GetProperty("verdict").GetProperty("level").GetString());
        }

        [Fact]
        public async Task Files_are_decoded_as_mono_at_the_comparers_rate_in_uneven_pieces()
        {
            var track = Track(30, seed: 1);
            var pcm = track.SelectMany(BitConverter.GetBytes).ToArray();
            var tool = new FakeMediaTool { DecodedPcm = pcm, Chunk = 1001 };
            var a = new FileInfo(Path.GetTempFileName());
            var b = new FileInfo(Path.GetTempFileName());

            try
            {
                var document = await new BuiltinCompare(tool).CompareAsync([a, b]);

                Assert.Equal("same", document.GetProperty("verdict").GetProperty("level").GetString());
                Assert.Contains($"decode:f32le:1:{Rate}", tool.Calls);
            }
            finally
            {
                a.Delete();
                b.Delete();
            }
        }

        [Theory]
        [InlineData(null, false, "off")]
        [InlineData(null, true, "provider")]
        [InlineData("builtin", false, "builtin")]
        [InlineData("off", true, "off")]
        public void The_source_in_effect_keeps_what_enabled_meant_before_there_was_a_choice(string source, bool enabled, string effective)
            => Assert.Equal(effective, new slskd.Options.TransfersOptions.CompareOptions { Source = source, Enabled = enabled }.EffectiveSource);

        private static Features Features(float[] samples)
        {
            var builder = new SameTrack.Builder();

            foreach (var sample in samples)
            {
                builder.Add(sample);
            }

            return builder.Finish();
        }

        // music, as far as the comparer cares: four parts in different registers, each with its own rhythm, so every
        // band moves in its own way, and a quiet start
        private static float[] Track(int seconds, int seed)
        {
            var random = new Random(seed);
            var parts = new[] { 90.0, 400.0, 1500.0, 4500.0 }.Select(freq => (Freq: freq * (0.9 + (random.NextDouble() * 0.2)), Beat: 0.15 + (random.NextDouble() * 0.4))).ToArray();
            var envelopes = parts.Select(_ => Enumerable.Range(0, (seconds * 10) + 2).Select(__ => random.NextDouble()).ToArray()).ToArray();
            var noise = new Random(seed + 1000);
            var wave = new float[seconds * Rate];

            for (var n = 0; n < wave.Length; n++)
            {
                var t = n / (double)Rate;
                var step = (int)(t * 10);
                double sum = 0;

                for (var p = 0; p < parts.Length; p++)
                {
                    // the part's level changes every tenth of a second, and pulses at its own beat
                    var level = envelopes[p][step] * (0.6 + (0.4 * Math.Sin(2 * Math.PI * t / parts[p].Beat)));
                    sum += level * Math.Sin(2 * Math.PI * parts[p].Freq * t);
                }

                wave[n] = (float)((sum * 0.2) + ((noise.NextDouble() - 0.5) * 0.01));
            }

            return wave;
        }

        private static float[] Delay(float[] samples, double seconds)
            => new float[(int)(seconds * Rate)].Concat(samples).ToArray();

        private static float[] Resample(float[] samples, double slower)
        {
            var length = (int)(samples.Length * slower);
            var result = new float[length];

            for (var i = 0; i < length; i++)
            {
                var at = i / slower;
                var lo = (int)at;
                var hi = Math.Min(lo + 1, samples.Length - 1);
                result[i] = (float)(samples[lo] + ((samples[hi] - samples[lo]) * (at - lo)));
            }

            return result;
        }
    }
}
