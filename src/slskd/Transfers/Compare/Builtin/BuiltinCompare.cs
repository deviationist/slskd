// <copyright file="BuiltinCompare.cs" company="JP Dillingham">
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

namespace slskd.Transfers.Compare.Builtin;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using slskd.Player;

/// <summary>
///     slskd's own Compare: which of several files are the same track, judged from the audio.
/// </summary>
/// <remarks>
///     The built-in answer to the compare contract (docs/transfers-compare-provider.md), in the very document a
///     provider sends, so the dialog draws it the same whichever answered.
/// </remarks>
public class BuiltinCompare
{
    /// <summary>
    ///     The comparer's version, which goes out as the document's <c>provider.version</c>.
    /// </summary>
    /// <remarks>
    ///     <b>Bump it whenever any answer could differ for the same files</b> -- a new threshold, a feature computed
    ///     differently, a change of wording. Nothing about a comparison is stored -- an answer is held
    ///     only minutes, for the dialog to collect -- so a new version takes effect with the restart that deploys it.
    /// </remarks>
    public const string Version = "slskd-compare-v1";

    /// <summary>
    ///     Initializes a new instance of the <see cref="BuiltinCompare"/> class.
    /// </summary>
    /// <param name="mediaTool">ffmpeg.</param>
    public BuiltinCompare(IMediaTool mediaTool)
    {
        MediaTool = mediaTool;
    }

    private IMediaTool MediaTool { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<BuiltinCompare>();


    /// <summary>
    ///     Compares <paramref name="files"/>.
    /// </summary>
    /// <param name="files">The files, in the order the document's indices follow.</param>
    /// <param name="cancellationToken">A token to cancel the comparison.</param>
    /// <returns>The comparison, in the contract's shape.</returns>
    public async Task<JsonElement> CompareAsync(IReadOnlyList<FileInfo> files, CancellationToken cancellationToken = default)
    {
        var compared = new List<ComparedFile>();

        // a file that cannot be read is reported and left out of the pairs rather than failing the lot: one broken
        // download in a search's worth of copies is the ordinary case
        foreach (var file in files)
        {
            try
            {
                compared.Add(new ComparedFile(await FeaturesAsync(file, cancellationToken)));
            }
            catch (Exception ex) when (ex is MediaToolException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                Log.Debug("Cannot compare {File}: {Message}", file.Name, ex.Message);
                compared.Add(new ComparedFile(null));
            }
        }

        return Compare(compared);
    }

    /// <summary>
    ///     Scores every pair of readable files and groups those that are one track, into the document.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <returns>The document.</returns>
    public static JsonElement Compare(IReadOnlyList<ComparedFile> files)
    {
        var pairs = new List<ComparedPair>();
        var parent = Enumerable.Range(0, files.Count).ToArray();

        int Root(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        for (var i = 0; i < files.Count; i++)
        {
            for (var j = i + 1; j < files.Count; j++)
            {
                if (!files[i].Readable || !files[j].Readable)
                {
                    continue;
                }

                var match = SameTrack.Match(files[i].Features, files[j].Features);
                pairs.Add(new ComparedPair(i, j, match));

                if (match.Verdict == "same")
                {
                    parent[Root(j)] = Root(i);
                }
            }
        }

        var groups = Enumerable.Range(0, files.Count)
            .Where(i => files[i].Readable)
            .GroupBy(Root)
            .Select(g => g.ToList())
            .OrderBy(g => -g.Count)
            .ThenBy(g => g[0])
            .ToList();

        return CompareDocument.Build(files, pairs, groups);
    }

    private async Task<Features> FeaturesAsync(FileInfo file, CancellationToken cancellationToken)
    {
        var builder = new SameTrack.Builder();

        using (var sink = new MonoSink(builder))
        {
            await MediaTool.DecodeToAsync(file.FullName, SameTrack.Rate, 1, "f32le", sink, cancellationToken);
        }

        return builder.Finish();
    }

    /// <summary>
    ///     Mono 32-bit float PCM as ffmpeg writes it, sample by sample into a <see cref="SameTrack.Builder"/>.
    /// </summary>
    private sealed class MonoSink(SameTrack.Builder builder) : Stream
    {
        private readonly byte[] carry = new byte[4];
        private int carried;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (carried > 0)
            {
                var take = Math.Min(4 - carried, buffer.Length);
                buffer[..take].CopyTo(carry.AsSpan(carried));
                carried += take;
                buffer = buffer[take..];

                if (carried < 4)
                {
                    return;
                }

                builder.Add(BinaryPrimitives.ReadSingleLittleEndian(carry));
                carried = 0;
            }

            while (buffer.Length >= 4)
            {
                builder.Add(BinaryPrimitives.ReadSingleLittleEndian(buffer));
                buffer = buffer[4..];
            }

            buffer.CopyTo(carry);
            carried = buffer.Length;
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
    }
}
