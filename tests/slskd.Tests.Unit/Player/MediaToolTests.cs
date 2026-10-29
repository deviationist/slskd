namespace slskd.Tests.Unit.Player
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using slskd.Player;
    using Xunit;

    /// <summary>
    ///     The real ffmpeg, where there is one. The build image has none, so these pass without running there; the
    ///     runtime image does, and they are the check that the arguments ffmpeg is given are ones it accepts.
    /// </summary>
    public class MediaToolTests : IDisposable
    {
        public MediaToolTests()
        {
            Temp = Path.Combine(Path.GetTempPath(), $"slskd.test.{Guid.NewGuid()}");
            Directory.CreateDirectory(Temp);
        }

        private string Temp { get; }

        public void Dispose() => Directory.Delete(Temp, recursive: true);

        [Fact]
        public void The_input_guard_names_one_protocol_and_no_format_that_reads_other_files()
        {
            var guard = MediaTool.InputGuard;
            var protocols = guard[guard.ToList().IndexOf("-protocol_whitelist") + 1];
            var formats = guard[guard.ToList().IndexOf("-format_whitelist") + 1].Split(',');

            Assert.Equal("file", protocols);
            Assert.DoesNotContain(formats, f => f is "dash" or "hls" or "concat" or "image2" or "data" or "tee");
        }

        [Fact]
        public async Task A_file_named_as_audio_that_is_really_a_manifest_is_not_followed()
        {
            if (!Has("ffmpeg") || !Has("ffprobe"))
            {
                return;
            }

            // a DASH manifest under an audio name, its BaseURL a real track somewhere else: ffmpeg left to detect the
            // format itself decodes that other file, which the player would then stream
            var elsewhere = Path.Combine(Temp, "elsewhere.m4a");
            Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=3", "-c:a", "aac", elsewhere);
            var manifest = Path.Combine(Temp, "innocent.mp3");
            File.WriteAllText(manifest,
                "<?xml version=\"1.0\"?><MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" type=\"static\" mediaPresentationDuration=\"PT3S\""
                + " minBufferTime=\"PT1S\" profiles=\"urn:mpeg:dash:profile:isoff-on-demand:2011\"><Period><AdaptationSet mimeType=\"audio/mp4\">"
                + $"<Representation id=\"a\" bandwidth=\"128000\" codecs=\"mp4a.40.2\"><BaseURL>{elsewhere}</BaseURL></Representation>"
                + "</AdaptationSet></Period></MPD>");

            var options = new slskd.Options();
            var tool = new MediaTool(new TestOptionsMonitor<slskd.Options>(options));

            await Assert.ThrowsAnyAsync<Exception>(() => tool.DecodeAsync(manifest));
            await Assert.ThrowsAnyAsync<Exception>(() => tool.TranscodeAsync(manifest, Path.Combine(Temp, "out.webm"), Codecs.Find("opus")));
            Assert.Null(await tool.ProbeDurationAsync(manifest));

            // and the real file it pointed at still decodes, so the guard shut out the trick and not the format
            Assert.NotEmpty(await tool.DecodeAsync(elsewhere));
        }

        [Fact]
        public async Task An_aiff_with_a_cover_becomes_a_checked_opus_and_aac_and_a_waveform()
        {
            if (!Has("ffmpeg") || !Has("ffprobe"))
            {
                return;
            }

            var source = Path.Combine(Temp, "tone.aiff");
            var cover = Path.Combine(Temp, "cover.png");

            // a cover stream as well as audio, because that is what these files carry and the WebM muxer refuses one
            Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "color=c=red:s=32x32:d=1", "-frames:v", "1", cover);
            Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=3", "-i", cover,
                "-map", "0:a", "-map", "1:v", "-c:a", "pcm_s16be", "-c:v", "png", "-disposition:v", "attached_pic", source);

            var options = new slskd.Options
            {
                Player = new slskd.Options.PlayerOptions
                {
                    Cache = new slskd.Options.PlayerOptions.PlayerCacheOptions { Directory = Path.Combine(Temp, "cache") },
                },
            };
            var service = new PlayerService(new TestOptionsMonitor<slskd.Options>(options), new MediaTool(new TestOptionsMonitor<slskd.Options>(options)));

            var peaks = await service.PeaksAsync(new FileInfo(source));
            Assert.Equal(3.0, peaks.Duration, precision: 2);
            // lavfi's sine is at 1/8 of full scale by default
            Assert.InRange(peaks.Peaks.Max(), 0.1, 0.15);

            foreach (var name in new[] { "opus", "aac" })
            {
                var (path, _) = await service.AudioAsync(new FileInfo(source), Codecs.Find(name));
                Assert.True(new FileInfo(path).Length > 0);
            }
        }

        private static bool Has(string executable)
        {
            try
            {
                Run(executable, "-version");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void Run(string executable, params string[] arguments)
        {
            var info = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(error);
            }
        }
    }
}
