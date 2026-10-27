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
