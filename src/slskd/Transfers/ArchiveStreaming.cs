// <copyright file="ArchiveStreaming.cs" company="JP Dillingham">
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

namespace slskd.Transfers;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Serilog;

/// <summary>
///     One file to put in an archive: the name it goes in under, and how to open it.
/// </summary>
/// <param name="EntryName">The entry's name in the archive.</param>
/// <param name="Open">Opens the file, or says why it cannot be: returns the stream, or null and a reason.</param>
public record ArchiveSource(string EntryName, Func<(Stream Stream, string Reason)> Open);

/// <summary>
///     Streams a zip of files to an HTTP response as it is written, rather than building it first.
/// </summary>
/// <remarks>
///     Shared by every endpoint that hands out an archive, because what it gets right is easy to get wrong and was
///     found the hard way: see the comment on synchronous IO below.
/// </remarks>
public static class ArchiveStreaming
{
    private static ILogger Log { get; } = Serilog.Log.ForContext(typeof(ArchiveStreaming));

    /// <summary>
    ///     Writes <paramref name="sources"/> to the response of <paramref name="context"/> as a store-only zip named
    ///     <paramref name="archiveName"/>.
    /// </summary>
    /// <remarks>
    ///     A file that cannot be opened is skipped rather than failing the archive, and the archive then ends with an
    ///     entry listing what was skipped and why -- by the time a file turns out to be missing, the response has
    ///     started and there is no status code left to report it with.
    /// </remarks>
    /// <param name="context">The request's context.</param>
    /// <param name="archiveName">The name the archive is saved under.</param>
    /// <param name="sources">The files, in the order they go in.</param>
    /// <returns>The operation context.</returns>
    public static async Task WriteAsync(HttpContext context, string archiveName, IReadOnlyList<ArchiveSource> sources)
    {
        var response = context.Response;
        var aborted = context.RequestAborted;

        var contentDisposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("attachment");
        contentDisposition.SetHttpFileName(archiveName);

        response.ContentType = "application/zip";
        response.Headers.ContentDisposition = contentDisposition.ToString();

        // no Content-Length. the size of a store-only archive *is* computable in advance, but only with a writer
        // that controls every byte, and a Content-Length wrong by one is a broken download

        // ask a reverse proxy not to buffer: nginx spools a proxied response to a temporary file by default, which
        // for a streamed archive means waiting for all of it before the browser is handed any of it
        response.Headers["X-Accel-Buffering"] = "no";

        var skipped = new List<string>();

        try
        {
            // ZipArchive cannot write an archive to this response without synchronous writes, and Kestrel
            // disallows those on the response body by default. The bookkeeping parts of the format -- each entry's
            // data descriptor, written when the entry stream is disposed, and the central directory, written when
            // the archive is -- go out through Stream.Write, and neither DirectToArchiveWriterStream nor the entry
            // stream returned by OpenAsync implements DisposeAsync, so `await using` falls back to the synchronous
            // path. Verified against the framework directly on .NET 10.0.12, not inferred.
            //
            // The consequence of not doing this is worse than a failed request: the file *contents* stream fine,
            // so the browser saves every byte of every file and then the request dies before the central directory
            // is written. What lands on disk is a plausible-looking archive with nothing at the end to say what is
            // in it, which macOS reports as "Error 79 - Inappropriate file type or format".
            //
            // Scoped to this request, and only the format's bookkeeping actually travels this way -- the file
            // contents are copied with CopyToAsync and stay asynchronous.
            var bodyControl = context.Features.Get<IHttpBodyControlFeature>();

            if (bodyControl is not null)
            {
                bodyControl.AllowSynchronousIO = true;
            }

            await using var archive = await ZipArchive.CreateAsync(response.Body, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, aborted);

            foreach (var source in sources)
            {
                var (stream, reason) = source.Open();

                if (stream is null)
                {
                    Log.Warning("Skipping '{Entry}' in archive '{Archive}': {Reason}", source.EntryName, archiveName, reason);
                    skipped.Add($"{source.EntryName}{Environment.NewLine}    {reason}");
                    continue;
                }

                await using (stream)
                {
                    var entry = archive.CreateEntry(source.EntryName, CompressionLevel.NoCompression);

                    await using var target = await entry.OpenAsync(aborted);
                    await stream.CopyToAsync(target, aborted);
                }
            }

            // last, because entries are written in the order they are created, and this one can only be written
            // once every other entry has had its turn and either made it or not
            if (skipped.Count > 0)
            {
                var entry = archive.CreateEntry(ArchiveNaming.MissingEntryName, CompressionLevel.NoCompression);

                await using var target = await entry.OpenAsync(aborted);
                await using var writer = new StreamWriter(target);

                await writer.WriteLineAsync($"{skipped.Count} of the {sources.Count} selected file(s) could not be added to this archive.");
                await writer.WriteLineAsync();

                foreach (var line in skipped)
                {
                    await writer.WriteLineAsync(line);
                    await writer.WriteLineAsync();
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex is IOException)
        {
            // the browser went away, or the connection did. there is no response left to put an error into, and
            // nothing here is this application's fault
            Log.Debug("The download of archive '{Archive}' ended early: {Message}", archiveName, ex.Message);
        }
    }
}
