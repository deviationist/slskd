// <copyright file="DownloadFileDurationTests.cs" company="JP Dillingham">
//           ▄▄▄▄     ▄▄▄▄     ▄▄▄▄
//     ▄▄▄▄▄▄█  █▄▄▄▄▄█  █▄▄▄▄▄█  █
//     █__ --█  █__ --█    ◄█  -  █
//     █▄▄▄▄▄█▄▄█▄▄▄▄▄█▄▄█▄▄█▄▄▄▄▄█
//   ┍━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ ━━━━ ━  ━┉   ┉     ┉
//   │ Copyright (c) JP Dillingham.
//   │
//   │ This program is free software: you can redistribute it and/or modify
//   │ it under the terms of the GNU Affero General Public License as published
//   │ by the Free Software Foundation, version 3.
//   │
//   │ This program is distributed in the hope that it will be useful,
//   │ but WITHOUT ANY WARRANTY; without even the implied warranty of
//   │ MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//   │ GNU Affero General Public License for more details.
//   │
//   │ You should have received a copy of the GNU Affero General Public License
//   │ along with this program.  If not, see https://www.gnu.org/licenses/.
//   │
//   │ This program is distributed with Additional Terms pursuant to Section 7
//   │ of the AGPLv3.  See the LICENSE file in the root directory of this
//   │ project for the complete terms and conditions.
//   │
//   │ https://slskd.org
//   │
//   ├╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌ ╌ ╌╌╌╌ ╌
//   │ SPDX-FileCopyrightText: JP Dillingham
//   │ SPDX-License-Identifier: AGPL-3.0-only
//   ╰───────────────────────────────────────────╶──── ─ ─── ─  ── ──┈  ┈
// </copyright>

namespace slskd.Tests.Unit.Transfers;

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using slskd.Transfers;
using Xunit;

public class DownloadFileDurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Download_With_No_Recorded_Path_Has_No_Length(string filename)
    {
        Assert.Null(new DownloadFileDuration().Of(filename, 0));
    }

    [Fact]
    public async Task An_Audio_File_Is_Measured_In_The_Background_And_Answered_On_The_Next_Ask()
    {
        using var file = TemporaryFile.Wav(seconds: 2);
        var duration = new DownloadFileDuration();

        // the first ask never waits on the file: the list is polled once a second, from a network mount
        Assert.Null(duration.Of(file.Name, file.Size));

        await duration.MeasureAsync(file.Name, file.Size);

        Assert.Equal(2, duration.Of(file.Name, file.Size));
    }

    [Fact]
    public async Task A_File_That_Is_Not_Audio_Has_No_Length()
    {
        using var file = TemporaryFile.Text("not audio");
        var duration = new DownloadFileDuration();

        await duration.MeasureAsync(file.Name, file.Size);

        Assert.Null(duration.Of(file.Name, file.Size));
    }

    [Fact]
    public async Task A_File_That_Is_Not_Audio_Is_Not_Read_Again()
    {
        // named as audio and holding none: TagLib opens it and finds nothing it can time
        var bytes = TemporaryFile.WavBytes(seconds: 2);
        var name = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
        var duration = new DownloadFileDuration();

        try
        {
            await File.WriteAllBytesAsync(name, new byte[bytes.Length]);
            await duration.MeasureAsync(name, bytes.Length);
            Assert.Null(duration.Of(name, bytes.Length));

            // real audio of the same size at the same path still gets the remembered answer, which is how we
            // know the file was not opened a second time -- the point, since the list asks once a second
            await File.WriteAllBytesAsync(name, bytes);
            await duration.MeasureAsync(name, bytes.Length);

            Assert.Null(duration.Of(name, bytes.Length));
        }
        finally
        {
            File.Delete(name);
        }
    }

    [Fact]
    public async Task A_File_That_Could_Not_Be_Opened_Is_Asked_About_Again()
    {
        // a mount that was briefly away is not a file that is not audio, and keeping it as one would blank the
        // cell until the next restart
        var name = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
        var duration = new DownloadFileDuration();

        // not followed by an Of(): asking starts a measurement of its own, and this one would then wait on that
        await duration.MeasureAsync(name, 32_044);

        try
        {
            await File.WriteAllBytesAsync(name, TemporaryFile.WavBytes(seconds: 2));
            await duration.MeasureAsync(name, 32_044);

            Assert.Equal(2, duration.Of(name, 32_044));
        }
        finally
        {
            File.Delete(name);
        }
    }

    [Fact]
    public async Task A_Different_File_At_The_Same_Path_Is_Measured_Afresh()
    {
        using var file = TemporaryFile.Wav(seconds: 2);
        var duration = new DownloadFileDuration();

        await duration.MeasureAsync(file.Name, file.Size);

        // the overwrite strategy can put another file at the same path; the size is what tells them apart
        await File.WriteAllBytesAsync(file.Name, TemporaryFile.WavBytes(seconds: 3));
        var size = new FileInfo(file.Name).Length;

        await duration.MeasureAsync(file.Name, size);

        Assert.Equal(3, duration.Of(file.Name, size));
    }

    private sealed class TemporaryFile : IDisposable
    {
        private TemporaryFile(string extension, byte[] contents)
        {
            Name = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
            File.WriteAllBytes(Name, contents);
            Size = contents.Length;
        }

        public string Name { get; }
        public long Size { get; }

        public static TemporaryFile Wav(int seconds) => new(".wav", WavBytes(seconds));

        public static TemporaryFile Text(string text) => new(".txt", Encoding.UTF8.GetBytes(text));

        /// <summary>
        ///     A PCM WAV of silence: 8 kHz, mono, 16-bit, so 16,000 bytes a second after a 44-byte header.
        /// </summary>
        public static byte[] WavBytes(int seconds)
        {
            const int rate = 8_000;
            const short channels = 1;
            const short bits = 16;
            var data = rate * channels * (bits / 8) * seconds;

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + data);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(rate);
            writer.Write(rate * channels * (bits / 8));
            writer.Write((short)(channels * (bits / 8)));
            writer.Write(bits);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(data);
            writer.Write(new byte[data]);
            writer.Flush();

            return stream.ToArray();
        }

        public void Dispose()
        {
            File.Delete(Name);
        }
    }
}
