// <copyright file="BuiltinAnalyser.cs" company="JP Dillingham">
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

namespace slskd.Player.Builtin;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
///     slskd's own analyser: what a downloaded file is, as opposed to what it says it is.
/// </summary>
/// <remarks>
///     <para>
///         The built-in answer to the analysis contract (docs/player-analysis-provider.md), for an instance with no
///         provider of its own: it answers in the same document a provider does, so everything downstream -- the badge,
///         the panel, the lanes, the cache -- is the same whichever answered. Ported from media-bridge's analyser at its
///         schema 6, and kept independently from then on.
///     </para>
///     <para>
///         Everything comes out of one decode, streamed through <see cref="Measurer"/>.
///     </para>
/// </remarks>
public partial class BuiltinAnalyser
{
    /// <summary>
    ///     The analyser's version, which goes out as the document's <c>provider.version</c> and ends its id.
    /// </summary>
    /// <remarks>
    ///     <b>Bump it whenever any answer could differ for the same audio</b> -- a new check, a retired one, a rewritten
    ///     threshold, a number measured differently, a change of wording. Everything kept under another version is then
    ///     dropped (see <see cref="AnalysisCache"/>), so no answer made by the old rules is shown as this one's: the
    ///     operator would otherwise see the new findings on new downloads and the old ones on everything already there.
    /// </remarks>
    public const string Version = "slskd-v1";

    /// <summary>
    ///     Initializes a new instance of the <see cref="BuiltinAnalyser"/> class.
    /// </summary>
    /// <param name="mediaTool">ffmpeg and ffprobe.</param>
    public BuiltinAnalyser(IMediaTool mediaTool)
    {
        MediaTool = mediaTool;
    }

    private IMediaTool MediaTool { get; }

    /// <summary>
    ///     Analyses <paramref name="file"/>.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="cancellationToken">A token to cancel the analysis.</param>
    /// <returns>The analysis, in the contract's shape.</returns>
    public async Task<JsonElement> AnalyseAsync(FileInfo file, CancellationToken cancellationToken = default)
    {
        var facts = Facts(await MediaTool.ProbeAsync(file.FullName, cancellationToken), FlacAudioBytes(file));
        var channels = Math.Min(2, Math.Max(1, facts.Channels));
        var lossy = !Measurer.LosslessCodecs.Contains(facts.Codec ?? string.Empty);

        // a lossy file can hold peaks above full scale, which an integer decode would cut off -- and then measure as
        // clipping of its own making. a lossless one cannot, and keeps the integers its bit-depth check needs
        var format = lossy ? "f32le" : facts.DeclaredBits > 16 ? "s32le" : "s16le";
        var full = format switch { "f32le" => 1.0, "s32le" => Math.Pow(2, 31), _ => Math.Pow(2, 15) };

        var measurer = new Measurer(facts, channels, full);

        using (var sink = new PcmSink(measurer, format, channels))
        {
            await MediaTool.DecodeToAsync(file.FullName, facts.SampleRate, channels, format, sink, cancellationToken);
        }

        var measurements = measurer.Finish();
        var checksum = await MediaTool.HashAudioAsync(file.FullName, cancellationToken);

        return Report.Document(measurements, $"{checksum}-{Version}");
    }

    /// <summary>
    ///     The container's facts, from ffprobe's JSON.
    /// </summary>
    /// <param name="json">What ffprobe wrote.</param>
    /// <param name="flacAudioBytes">A FLAC's audio frames in bytes, if it is one.</param>
    /// <returns>The facts.</returns>
    public static ContainerFacts Facts(string json, long? flacAudioBytes = null)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = document.RootElement;

        var stream = root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array && streams.GetArrayLength() > 0
            ? streams[0]
            : default;
        var format = root.TryGetProperty("format", out var f) ? f : default;

        var codec = Text(stream, "codec_name") ?? string.Empty;

        // ffprobe is inconsistent about where a PCM depth appears; three sources, ending with the codec name, which
        // for PCM *is* the depth
        var declared = Whole(stream, "bits_per_raw_sample") is { } raw && raw != 0 ? raw
            : Whole(stream, "bits_per_sample") is { } bps && bps != 0 ? bps
            : PcmDepth().Match(codec) is { Success: true } m ? int.Parse(m.Groups[1].Value) : (long?)null;

        var duration = Real(format, "duration");
        var streamRate = Whole(stream, "bit_rate");
        long? bitRate = streamRate is { } s && s != 0 ? s
            : codec == "flac" && flacAudioBytes is { } bytes && bytes != 0 && duration is { } d && d != 0 ? Py.RoundToLong(bytes * 8 / d)
            : Whole(format, "bit_rate");

        var container = (Text(format, "format_name") ?? string.Empty).Split(',')[0];

        return new ContainerFacts(
            Codec: codec.Length > 0 ? codec : null,
            Container: container.Length > 0 ? container : null,
            SampleRate: Whole(stream, "sample_rate") is { } rate && rate != 0 ? (int)rate : 44100,
            Channels: Whole(stream, "channels") is { } ch && ch != 0 ? (int)ch : 2,
            DeclaredBits: (int?)declared,
            BitRate: bitRate,
            Duration: duration);
    }

    /// <summary>
    ///     How many bytes of a FLAC are audio frames: the file, less its metadata blocks and any ID3 tag either side.
    /// </summary>
    /// <remarks>
    ///     A FLAC stream reports no bitrate, and the container's figure counts the tags and the cover art -- adding art
    ///     moved a test file from 109 to 110 kbps.
    /// </remarks>
    /// <param name="file">The file.</param>
    /// <returns>The bytes, or null for anything that does not read as FLAC.</returns>
    public static long? FlacAudioBytes(FileInfo file)
    {
        try
        {
            using var stream = file.OpenRead();
            var size = stream.Length;
            var head = new byte[10];
            long pos = 0;

            if (Read(stream, 0, head) == 10 && head[0] == 'I' && head[1] == 'D' && head[2] == '3')
            {
                var tag = (head[6] << 21) | (head[7] << 14) | (head[8] << 7) | head[9];
                pos = 10 + tag + ((head[5] & 0x10) != 0 ? 10 : 0);
            }

            var marker = new byte[4];

            if (Read(stream, pos, marker) != 4 || marker[0] != 'f' || marker[1] != 'L' || marker[2] != 'a' || marker[3] != 'C')
            {
                return null;
            }

            pos += 4;

            while (true)
            {
                var block = new byte[4];

                if (Read(stream, pos, block) != 4)
                {
                    return null;
                }

                pos += 4 + ((block[1] << 16) | (block[2] << 8) | block[3]);

                if ((block[0] & 0x80) != 0)
                {
                    break;
                }
            }

            var end = size;
            var tail = new byte[3];

            if (size >= 128 && Read(stream, size - 128, tail) == 3 && tail[0] == 'T' && tail[1] == 'A' && tail[2] == 'G')
            {
                end = size - 128;
            }

            return end > pos ? end - pos : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int Read(Stream stream, long position, byte[] into)
    {
        stream.Position = position;
        return stream.ReadAtLeast(into, into.Length, throwOnEndOfStream: false);
    }

    private static string Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    // ffprobe writes most numbers as strings, and "N/A" where it has none
    private static long? Whole(JsonElement element, string name)
        => long.TryParse(Text(element, name), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    private static double? Real(JsonElement element, string name)
        => double.TryParse(Text(element, name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    [GeneratedRegex(@"^pcm_[su](\d+)")]
    private static partial Regex PcmDepth();

    /// <summary>
    ///     PCM as ffmpeg writes it, frame by frame into a <see cref="Measurer"/>.
    /// </summary>
    private sealed class PcmSink : Stream
    {
        public PcmSink(Measurer measurer, string format, int channels)
        {
            Measurer = measurer;
            Format = format;
            Channels = channels;
            Width = format == "s16le" ? 2 : 4;
            Full = format switch { "s32le" => (float)Math.Pow(2, 31), "s16le" => 32768f, _ => 1f };
            Carry = new byte[Width * channels];
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        private Measurer Measurer { get; }
        private string Format { get; }
        private int Channels { get; }
        private int Width { get; }
        private float Full { get; }
        private byte[] Carry { get; }
        private int Carried { get; set; }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var frame = Carry.Length;

            // a frame split across two writes
            if (Carried > 0)
            {
                var take = Math.Min(frame - Carried, buffer.Length);
                buffer[..take].CopyTo(Carry.AsSpan(Carried));
                Carried += take;
                buffer = buffer[take..];

                if (Carried < frame)
                {
                    return;
                }

                AddFrame(Carry);
                Carried = 0;
            }

            while (buffer.Length >= frame)
            {
                AddFrame(buffer[..frame]);
                buffer = buffer[frame..];
            }

            buffer.CopyTo(Carry);
            Carried = buffer.Length;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        private void AddFrame(ReadOnlySpan<byte> frame)
        {
            var (l, li) = Sample(frame);
            var (r, ri) = Channels > 1 ? Sample(frame[Width..]) : (l, li);
            Measurer.Add(l, r, li, ri);
        }

        private (float Value, long Integer) Sample(ReadOnlySpan<byte> bytes)
        {
            switch (Format)
            {
                case "s16le":
                    var s16 = BinaryPrimitives.ReadInt16LittleEndian(bytes);
                    return (s16 / Full, s16);
                case "s32le":
                    var s32 = BinaryPrimitives.ReadInt32LittleEndian(bytes);
                    return ((float)s32 / Full, s32);
                default:
                    return (BinaryPrimitives.ReadSingleLittleEndian(bytes), 0);
            }
        }
    }
}
