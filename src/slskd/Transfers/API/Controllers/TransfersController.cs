// <copyright file="TransfersController.cs" company="JP Dillingham">
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

using Microsoft.Extensions.Options;

namespace slskd.Transfers.API
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.ComponentModel.DataAnnotations;
    using System.IO.Compression;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Asp.Versioning;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Http.Features;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.StaticFiles;
    using Serilog;
    using slskd.Files;
    using slskd.Users;
    using Soulseek;

    /// <summary>
    ///     Transfers.
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    public class TransfersController : ControllerBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TransfersController"/> class.
        /// </summary>
        /// <param name="optionsSnapshot"></param>
        /// <param name="userService"></param>
        /// <param name="transferService"></param>
        /// <param name="fileService"></param>
        /// <param name="downloadTicketService"></param>
        /// <param name="downloadFileAvailability"></param>
        /// <param name="searchExistence"></param>
        public TransfersController(
            TransferService transferService,
            IUserService userService,
            FileService fileService,
            DownloadTicketService downloadTicketService,
            DownloadFileAvailability downloadFileAvailability,
            slskd.Search.SearchExistence searchExistence,
            IOptionsSnapshot<Options> optionsSnapshot)
        {
            Transfers = transferService;
            Users = userService;
            Files = fileService;
            Tickets = downloadTicketService;
            FileAvailability = downloadFileAvailability;
            SearchExistence = searchExistence;
            OptionsSnapshot = optionsSnapshot;
        }

        /// <summary>
        ///     Gets the map of file extensions to content types used when serving a downloaded file.
        /// </summary>
        /// <remarks>
        ///     The framework's built-in map doesn't include some of the formats that are most common on the Soulseek
        ///     network, so they are added here. Anything still unmapped is served as 'application/octet-stream'.
        /// </remarks>
        private static FileExtensionContentTypeProvider ContentTypeProvider { get; } = new()
        {
            Mappings =
            {
                [".flac"] = "audio/flac",
                [".opus"] = "audio/ogg",
            },
        };

        private static SemaphoreSlim DownloadRequestLimiter { get; } = new SemaphoreSlim(2, 2);
        private TransferService Transfers { get; }
        private IUserService Users { get; }
        private FileService Files { get; }
        private DownloadTicketService Tickets { get; }
        private DownloadFileAvailability FileAvailability { get; }
        private slskd.Search.SearchExistence SearchExistence { get; }
        private IOptionsSnapshot<Options> OptionsSnapshot { get; }
        private ILogger Log { get; set; } = Serilog.Log.ForContext<TransfersController>();

        /// <summary>
        ///     Cancels the specified download.
        /// </summary>
        /// <param name="username">The username of the download source.</param>
        /// <param name="id">The id of the download.</param>
        /// <param name="remove">A value indicating whether the tracked download should be removed after cancellation.</param>
        /// <param name="deleteFile">A value indicating whether the downloaded file should also be deleted from disk.</param>
        /// <returns></returns>
        /// <remarks>
        ///     Removing a download removes the record of it, and has never touched the file on disk. Passing
        ///     <paramref name="deleteFile"/> deletes the file the download produced as well, and requires the
        ///     transfers.download.delete_file_on_removal option to be enabled.
        ///
        ///     Only a file this application knows it wrote is deleted, at the path recorded for the transfer:
        ///     the finished file if the download completed, or the partial left in the incomplete directory if
        ///     it was cancelled or failed. Both are deleted through the file service and subject to its
        ///     containment checks, which allow those two directories and nothing else.
        ///
        ///     A transfer that is still running is not deleted from under itself -- it must be cancelled
        ///     first, which the UI already requires, since Remove is only offered for terminal transfers.
        ///     Nothing is deleted either for a download that finished before the application began recording
        ///     where the bytes are; that is a null path, not a derivable one.
        /// </remarks>
        /// <response code="200">The download was removed, and the outcome of the file deletion is reported.</response>
        /// <response code="204">The download was cancelled or removed successfully.</response>
        /// <response code="404">The specified download was not found.</response>
        [HttpDelete("downloads/{username}/{id}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(typeof(RemovalResult), 200)]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> CancelDownloadAsync([FromRoute, UrlEncoded, Required] string username, [FromRoute, Required] string id, [FromQuery] bool remove = false)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (!Guid.TryParse(id, out var guid))
            {
                return BadRequest();
            }

            // no query parameter decides this; the option does. removing a download either takes its file
            // with it or it does not, and which of those this instance does is a thing the operator
            // configured once rather than something each caller chooses.
            var deleteFile = remove && OptionsSnapshot.Value.Transfers.Download.DeleteFileOnRemoval;

            try
            {
                // read before removing, while the record is still there to read: it is the only thing
                // that knows where the bytes are.
                // `Transfer` alone is Soulseek.NET's, this file having `using Soulseek`; ours is the record
                slskd.Transfers.Transfer transfer = deleteFile ? Transfers.Downloads.Find(t => t.Id == guid) : null;

                if (deleteFile && transfer is null)
                {
                    return NotFound();
                }

                Transfers.Downloads.TryCancel(guid);

                // a running download has to stop before either half can happen: Remove() only touches
                // terminal transfers, and unlinking a file that is still being written to is either
                // allowed and confusing (POSIX: the writer keeps the inode) or refused outright
                // (Windows: the stream is opened FileShare.None).
                //
                // cancellation is asynchronous -- TryCancel() signals a token and returns -- so this
                // waits for the transfer to actually land in a terminal state rather than racing it.
                // bounded, because a wait that cannot end is worse than one that gives up: if it does
                // give up, Remove() reports removing nothing and the deletion reports why.
                var settled = !deleteFile || TransferStateCategories.Completed.Contains(transfer.State);

                if (deleteFile && !settled)
                {
                    transfer = await WaitForTerminalStateAsync(guid) ?? transfer;
                    settled = TransferStateCategories.Completed.Contains(transfer.State);
                }

                // reported, not assumed. Remove() answers whether it removed anything, and everything
                // above was decided from a snapshot read before the cancellation.
                var removed = remove && Transfers.Downloads.Remove(guid);

                if (!deleteFile)
                {
                    return NoContent();
                }

                // it did not stop, so its file is still being written to and is not ours to unlink.
                // the wait exists to make this case rare; skipping the delete is what makes it safe
                // when the wait is not enough, and saying so is what stops it being silent.
                if (!settled)
                {
                    Log.Warning("Download {Id} was still running after being cancelled; its file was left alone", guid);
                    return Ok(new RemovalResult
                    {
                        Removed = removed,
                        Deleted = false,
                        Filename = transfer.LocalFilename,
                        Error = "the transfer did not stop in time; its file was left alone",
                    });
                }

                return Ok(await DeleteDownloadedFileAsync(transfer) with { Removed = removed });
            }
            catch (NotFoundException)
            {
                return NotFound();
            }
        }

        /// <summary>
        ///     Waits for the download matching <paramref name="id"/> to reach a terminal state.
        /// </summary>
        /// <remarks>
        ///     Returns the transfer as it stands when it gets there, or null if it did not within the
        ///     timeout -- in which case the caller carries on with what it already had and reports the
        ///     outcome honestly rather than pretending the wait succeeded.
        /// </remarks>
        private async Task<slskd.Transfers.Transfer> WaitForTerminalStateAsync(Guid id, int timeoutMilliseconds = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);

                var transfer = Transfers.Downloads.Find(t => t.Id == id);

                if (transfer is null || TransferStateCategories.Completed.Contains(transfer.State))
                {
                    return transfer;
                }
            }

            Log.Warning("Download {Id} did not reach a terminal state within {Timeout}ms of being cancelled", id, timeoutMilliseconds);
            return null;
        }

        /// <summary>
        ///     Deletes the file produced by the specified <paramref name="transfer"/>, if it produced one.
        /// </summary>
        /// <remarks>
        ///     Delegates to the file service rather than deleting directly, so that this inherits the same
        ///     guards as every other deletion: absolute paths only, no traversal segments, and nothing outside
        ///     the configured downloads and incomplete directories. A recorded path that no longer satisfies
        ///     those -- the downloads directory having been reconfigured since, for instance -- is refused
        ///     here exactly as it would be there.
        /// </remarks>
        private async Task<RemovalResult> DeleteDownloadedFileAsync(slskd.Transfers.Transfer transfer)
        {
            var filename = transfer?.LocalFilename;

            if (string.IsNullOrWhiteSpace(filename))
            {
                // two different things hide behind a missing path, and the byte count tells them apart.
                //
                // the path is recorded immediately before the download begins, so a transfer that has
                // no path *and* transferred nothing never reached that point: queued, rejected, timed
                // out, or cancelled while waiting. nothing was ever written anywhere, which is the end
                // state the caller asked for -- so it is a success, the same as a file already gone.
                if (transfer is not null
                    && transfer.BytesTransferred == 0
                    && !TransferStateCategories.Successful.Contains(transfer.State))
                {
                    Log.Debug("Download {Id} never started; there is no file to delete", transfer.Id);
                    return new RemovalResult { Deleted = true, Filename = null, Error = null };
                }

                // bytes were written somewhere this instance did not record -- a download from before
                // it began recording. its file may well be sitting in the downloads directory under a
                // name nobody wrote down, so claiming the end state here would be claiming something
                // never checked.
                Log.Debug("No local file is recorded for download {Id}; nothing to delete", transfer?.Id);
                return new RemovalResult { Deleted = false, Filename = null, Error = null };
            }

            try
            {
                // a file that is already gone is a success, not a failure: what was asked for is that
                // it not be there, and it is not. `File.Delete` not throwing over an empty path is the
                // same rule, and this reports it the same way. failure is reserved for a file that is
                // there, should go, and will not.
                var results = await Files.DeleteFilesAsync(filename);

                return await results[filename].Match(
                    async success => new RemovalResult
                    {
                        Deleted = true,
                        Filename = filename,
                        Error = null,
                        PrunedDirectories = await PruneEmptyDirectoriesAsync(filename),
                    },
                    failure => Task.FromResult(new RemovalResult { Deleted = false, Filename = filename, Error = failure.Message }));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to delete the file for download {Id}: {Message}", transfer.Id, ex.Message);
                return new RemovalResult { Deleted = false, Filename = filename, Error = ex.Message };
            }
        }

        /// <summary>
        ///     Removes the directories the deleted file leaves empty behind it, innermost first.
        /// </summary>
        /// <remarks>
        ///     A download arrives inside the folder the peer named, sometimes nested several deep, and
        ///     deleting the last file out of that structure leaves the structure. Walking up rather than
        ///     deleting one level is the difference between cleaning up and moving the litter one folder
        ///     outwards.
        ///
        ///     It stops at the first directory that still holds something, and at the roots -- which is
        ///     not this method's rule to enforce: `DeleteDirectoriesAsync` refuses the Downloads and
        ///     Incomplete roots itself ("Deletion of application-controlled directory roots is not
        ///     supported"), along with anything outside them, so the walk ends when the shared guard says
        ///     no. A boundary checked in one place cannot drift from a boundary checked in two.
        ///
        ///     Empty means empty: `EnumerateFileSystemEntries` counts everything, so a folder holding a
        ///     cover image or a peer's notelist is left alone. That matches what slskd already does after
        ///     moving a completed file out of the incomplete tree.
        /// </remarks>
        /// <returns>The number of directories removed.</returns>
        private async Task<int> PruneEmptyDirectoriesAsync(string filename)
        {
            var pruned = 0;
            var directory = Path.GetDirectoryName(filename);

            while (!string.IsNullOrWhiteSpace(directory))
            {
                try
                {
                    if (!System.IO.Directory.Exists(directory) || System.IO.Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        break;
                    }

                    var results = await Files.DeleteDirectoriesAsync(directory);

                    if (results[directory].TryPickT1(out var failure, out _))
                    {
                        Log.Debug("Stopped pruning at {Directory}: {Message}", directory, failure.Message);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    // the roots land here, which is how the walk knows where to stop -- and so does
                    // anything the filesystem throws while being asked what is still in a folder.
                    //
                    // this method must not throw, and that is not tidiness: it runs *after* the file
                    // is gone, so an exception escaping here would be caught by the caller and
                    // reported as a deletion that failed. the file would be deleted and the answer
                    // would say it was not.
                    Log.Debug("Stopped pruning at {Directory}: {Message}", directory, ex.Message);
                    break;
                }

                Log.Information("Removed empty directory {Directory}", directory);
                pruned++;
                directory = Path.GetDirectoryName(directory);
            }

            return pruned;
        }

        /// <summary>
        ///     Removes all completed downloads, regardless of whether they failed or succeeded.
        /// </summary>
        /// <returns></returns>
        /// <response code="204">The downloads were removed successfully.</response>
        [HttpDelete("downloads/all/completed")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(204)]
        public IActionResult ClearCompletedDownloads()
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            try
            {
                Transfers.Downloads.Remove(t => !t.Removed && TransferStateCategories.Completed.Contains(t.State));
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to remove completed downloads: {Message}", ex.Message);
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        ///     Cancels the specified upload.
        /// </summary>
        /// <param name="username">The username of the upload destination.</param>
        /// <param name="id">The id of the upload.</param>
        /// <param name="remove">A value indicating whether the tracked upload should be removed after cancellation.</param>
        /// <returns></returns>
        /// <response code="204">The upload was cancelled successfully.</response>
        /// <response code="404">The specified upload was not found.</response>
        [HttpDelete("uploads/{username}/{id}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public IActionResult CancelUpload([FromRoute, UrlEncoded, Required] string username, [FromRoute, Required] string id, [FromQuery] bool remove = false)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (!Guid.TryParse(id, out var guid))
            {
                return BadRequest();
            }

            try
            {
                Transfers.Uploads.TryCancel(guid);

                if (remove)
                {
                    Transfers.Uploads.Remove(guid);
                }

                return NoContent();
            }
            catch (NotFoundException)
            {
                return NotFound();
            }
        }

        /// <summary>
        ///     Removes all completed uploads, regardless of whether they failed or succeeded.
        /// </summary>
        /// <returns></returns>
        /// <response code="204">The uploads were removed successfully.</response>
        [HttpDelete("uploads/all/completed")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(204)]
        public IActionResult ClearCompletedUploads()
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            try
            {
                Transfers.Uploads.Remove(t => !t.Removed && TransferStateCategories.Completed.Contains(t.State));
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to remove completed uploads: {Message}", ex.Message);
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        ///     (Obsolete) Enqueues the specified download.
        /// </summary>
        /// <param name="username">The username of the download source.</param>
        /// <param name="requests">The list of download requests.</param>
        /// <returns></returns>
        /// <response code="201">The download was successfully enqueued.</response>
        /// <response code="403">The download was rejected.</response>
        /// <response code="500">An unexpected error was encountered.</response>
        [Obsolete("Will be phased out in future versions; use batches")]
        [HttpPost("downloads/{username}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(201)]
        [ProducesResponseType(typeof(string), 403)]
        [ProducesResponseType(typeof(string), 500)]
        public async Task<IActionResult> EnqueueAsync([FromRoute, UrlEncoded, Required] string username, [FromBody] IEnumerable<QueueDownloadRequest> requests)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState.GetReadableString());
            }

            if (!requests?.Any() ?? true)
            {
                return BadRequest("At least one file is required");
            }

            if (requests.Any(r => r is null))
            {
                return BadRequest("One or more files in the request are null");
            }

            if (requests.Any(r => FileSafety.ContainsTraversalSegments(r.Filename)))
            {
                return BadRequest("One or more files in the request contain a dangerous path traversal segment");
            }

            if (!DownloadRequestLimiter.Wait(0))
            {
                return StatusCode(429, "Only one concurrent operation is permitted. Wait until the previous request completes");
            }

            try
            {
                var endpoint = await Users.GetIPEndPointAsync(username);

                if (Users.IsBlacklisted(username, endpoint.Address))
                {
                    throw new UserOfflineException($"User {username} appears to be offline");
                }

                var (enqueued, failed) = await Transfers.Downloads.EnqueueAsync(username, requests.Select(r => (r.Filename, r.Size)));

                return StatusCode(201, new { Enqueued = enqueued, Failed = failed });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to enqueue {Count} files for {Username}: {Message}", requests.Count(), username, ex.Message);
                return StatusCode(500, ex.Message);
            }
            finally
            {
                DownloadRequestLimiter.Release();
            }
        }

        /// <summary>
        ///     Enqueues a batch of downloads.
        /// </summary>
        /// <param name="request">The batch details.</param>
        /// <returns></returns>
        /// <response code="201">All downloads were successfully enqueued.</response>
        /// <response code="200">The request succeeded, but all downloads failed to be enqueued.</response>
        /// <response code="207">Some downloads were successfully enqueued, while some failed.</response>
        /// <response code="400">Bad request.</response>
        /// <response code="403">The request was forbidden.</response>
        /// <response code="409">A batch with the same ID already exists.</response>
        /// <response code="429">Request throttled.</response>
        /// <response code="500">An unexpected error was encountered.</response>
        [HttpPost("downloads/batches")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(typeof(EnqueueDownloadBatchResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(EnqueueDownloadBatchResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(EnqueueDownloadBatchResponse), StatusCodes.Status207MultiStatus)]
        [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(string), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(string), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(string), StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> EnqueueBatchAsync([FromBody] EnqueueDownloadBatchRequest request)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState.GetReadableString());
            }

            if (request.Files.Any(r => r is null))
            {
                return BadRequest("One or more files in the request are null");
            }

            if (request.Files.DistinctBy(f => f.Filename).Count() != request.Files.Count)
            {
                return BadRequest("Two or more files in the request are repeated");
            }

            Guid? batchId;
            Guid? searchId;

            try
            {
                batchId = string.IsNullOrWhiteSpace(request.Id) ? null : Guid.Parse(request.Id);
                searchId = string.IsNullOrWhiteSpace(request.SearchId) ? null : Guid.Parse(request.SearchId);
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to parse Guid from enqueue batch input: {Message}", ex.Message);
                return BadRequest("One or more provided identifiers is not a valid GUID/UUID");
            }

            batchId ??= Guid.NewGuid();

            if (!DownloadRequestLimiter.Wait(0))
            {
                return StatusCode(429, "Only one concurrent operation is permitted. Wait until the previous request completes");
            }

            try
            {
                var endpoint = await Users.GetIPEndPointAsync(request.Username);

                if (Users.IsBlacklisted(request.Username, endpoint.Address))
                {
                    throw new UserOfflineException($"User {request.Username} appears to be offline");
                }

                // throws DuplicateException if a record already exists
                await Transfers.Downloads.Batches.CreateAsync(new()
                {
                    Id = batchId.Value,
                    SearchId = searchId,
                    Username = request.Username,
                    Options = new()
                    {
                        Destination = request.Options.Destination,
                    },
                });

                // Transfer records will have been inserted before this returns, unless they were rejected
                // because they were already in progress, in which case they will show up in 'failed'. or maybe they
                // failed with an error. either way this complicates the return code
                var (enqueued, failed) = await Transfers.Downloads.EnqueueAsync(
                    username: request.Username,
                    files: request.Files.Select(r => (r.Filename, r.Size.Value)),
                    batchId: batchId);

                if (failed.Count > 0)
                {
                    Log.Warning("Failed to enqueue {Count} of {Total} files from {Username}; transfers already queued, in progress, or an error occurred (batch Id: {BatchId}).  Failues: {Failures}", failed.Count, request.Files.Count, request.Username, batchId, failed);
                }

                // the returned batch will have whatever Transfers were successfully inserted attached (via Include())
                // failed transfers MAY or MAY NOT have an associated database record. if they do, it should have been
                // properly finalized and marked as a failure
                var batch = await Transfers.Downloads.Batches.FindAsync(b => b.Id == batchId);

                var response = new EnqueueDownloadBatchResponse
                {
                    Batch = batch,
                    Failures = failed.Select(f => new EnqueueDownloadBatchResponseFailure { Filename = f.Filename, Message = f.Message }).ToList(),
                };

                // basically a no-op, but we did create the batch record (and it's useless, but it's there)
                // there's nothing to process asynchronously, so we'll return 200. 204 makes more sense to me,
                // but it doesn't allow a body and without it the caller will never know the id unless they supplied it
                if (response.Failures.Count == request.Files.Count)
                {
                    return StatusCode(StatusCodes.Status200OK, response);
                }

                // if at least one (but not all) failed, we're in a weird state so send the most appropriate status code
                // along with the batch and list of failures; the caller will have to pick through it and decide what to do
                if (response.Failures.Count > 0)
                {
                    return StatusCode(StatusCodes.Status207MultiStatus, response);
                }

                // everything passed and we are now (or will eventually be) downloading asynchronously
                return StatusCode(StatusCodes.Status201Created, response);
            }
            catch (UserOfflineException ex)
            {
                return StatusCode(StatusCodes.Status404NotFound, ex.Message);
            }
            catch (DuplicateException ex)
            {
                Log.Error(ex, "Failed to enqueue {Count} files for {Username}: A Batch with ID {BatchId} already exists", request.Files.Count, request.Username, request.Id);
                return Conflict($"A batch with ID {batchId} already exists");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to enqueue {Count} files for {Username} (batch Id: {BatchId}): {Message}", request.Files.Count, request.Username, batchId, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
            finally
            {
                DownloadRequestLimiter.Release();
            }
        }

        /// <summary>
        ///     Gets all downloads.
        /// </summary>
        /// <returns></returns>
        /// <response code="200">The request completed successfully.</response>
        [HttpGet("downloads")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetDownloadsAsync([FromQuery] bool includeRemoved = false)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            var downloads = Transfers.Downloads.List(includeRemoved: includeRemoved);

            await AnnotateAsync(downloads);

            var response = downloads.GroupBy(t => t.Username).Select(grouping => new UserResponse()
            {
                Username = grouping.Key,
                Directories = grouping.GroupBy(g => g.Filename.DirectoryName()).Select(d => new DirectoryResponse()
                {
                    Directory = d.Key,
                    FileCount = d.Count(),
                    Files = d.ToList(),
                }),
            });

            return Ok(response);
        }

        /// <summary>
        ///     Gets the specified batch and associated transfers.
        /// </summary>
        /// <param name="id">The id of the batch.</param>
        /// <returns></returns>
        /// <response code="400">The specified id is not valid.</response>
        /// <response code="200">The request completed successfully.</response>
        /// <response code="404">The specified batch was not found.</response>
        [HttpGet("downloads/batches/{id}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Get([FromRoute, Required] string id)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (!Guid.TryParse(id, out var guid))
            {
                return BadRequest($"The specified id {id} is not a valid GUID/UUID");
            }

            try
            {
                var found = await Transfers.Downloads.Batches.FindAsync(b => b.Id == guid);

                if (found is null)
                {
                    return NotFound();
                }

                return Ok(found);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get batch with ID {Id}: {Message}", guid, ex.Message);
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        ///     Gets all downloads for the specified username.
        /// </summary>
        /// <param name="username"></param>
        /// <returns></returns>
        /// <response code="200">The request completed successfully.</response>
        [HttpGet("downloads/{username}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(200)]
        public async Task<IActionResult> GetDownloadsAsync([FromRoute, UrlEncoded, Required] string username)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            var downloads = Transfers.Downloads.List(d => d.Username == username);

            if (!downloads.Any())
            {
                return NotFound();
            }

            await AnnotateAsync(downloads);

            var response = new UserResponse()
            {
                Username = username,
                Directories = downloads.GroupBy(g => g.Filename.DirectoryName()).Select(d => new DirectoryResponse()
                {
                    Directory = d.Key,
                    FileCount = d.Count(),
                    Files = d.ToList(),
                }),
            };

            return Ok(response);
        }

        [HttpGet("downloads/{username}/{id}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(typeof(Transfer), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetDownload([FromRoute, UrlEncoded, Required] string username, [FromRoute, Required] string id)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (!Guid.TryParse(id, out var guid))
            {
                return BadRequest();
            }

            var download = Transfers.Downloads.Find(t => t.Id == guid);

            if (download == default)
            {
                return NotFound();
            }

            await AnnotateAsync([download]);

            return Ok(download);
        }

        /// <summary>
        ///     Gets the contents of the file produced by the specified download.
        /// </summary>
        /// <param name="username">The username of the download source.</param>
        /// <param name="id">The id of the download.</param>
        /// <returns></returns>
        /// <remarks>
        ///     <para>
        ///         The download is identified by its id, and the path of the file it produced is resolved here, from the
        ///         path this application recorded when it wrote the file. No part of the path is supplied by the caller,
        ///         so there is no path for a caller to traverse.
        ///     </para>
        ///     <para>
        ///         The recorded path is nonetheless opened through the file service, which allows the configured downloads
        ///         and incomplete directories and nothing else. It was derived from a filename a remote peer chose, so it
        ///         is checked rather than trusted -- and a path recorded before the downloads directory was reconfigured is
        ///         refused here exactly as it would be anywhere else.
        ///     </para>
        ///     <para>
        ///         Only a download that succeeded is served. While one is running its recorded path is the partial file in
        ///         the incomplete directory, and a partial file served under the finished file's name is worse than no file
        ///         at all. A download that finished before this application began recording where the bytes went has no
        ///         recorded path, and is reported as not found rather than guessed at.
        ///     </para>
        /// </remarks>
        /// <response code="200">The request completed successfully.</response>
        /// <response code="400">The specified id is not a valid id.</response>
        /// <response code="403">Retrieval is disabled, or the recorded file is not in an application-controlled directory.</response>
        /// <response code="404">The specified download was not found, did not succeed, or produced no file that still exists.</response>
        [HttpGet("downloads/{username}/{id}/file")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(typeof(FileResult), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public IActionResult GetDownloadedFile([FromRoute, UrlEncoded, Required] string username, [FromRoute, Required] string id)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (!OptionsSnapshot.Value.RemoteFileRetrieval)
            {
                return Forbid();
            }

            if (!Guid.TryParse(id, out var guid))
            {
                return BadRequest();
            }

            var download = Transfers.Downloads.Find(t => t.Id == guid);

            if (download == default)
            {
                return NotFound();
            }

            if (!TransferStateCategories.Successful.Contains(download.State))
            {
                Log.Debug("Download {Id} is in state {State}; only a successful download has a file to serve", guid, download.State);
                return NotFound();
            }

            var filename = download.LocalFilename;

            if (string.IsNullOrWhiteSpace(filename))
            {
                Log.Debug("No local file is recorded for download {Id}; there is nothing to serve", guid);
                return NotFound();
            }

            Log.Information("Serving the file produced by download {Id} from '{File}'", guid, filename);

            try
            {
                // the service resolves symlinks and checks containment before it opens anything; if it returns a
                // Stream, the file is known to reside within an application-controlled directory
                var stream = Files.OpenFile(filename);

                if (!ContentTypeProvider.TryGetContentType(filename, out var contentType))
                {
                    contentType = "application/octet-stream";
                }

                // the framework takes care of Content-Length, Accept-Ranges, and the RFC 6266 encoding of the
                // filename in the Content-Disposition header, and disposes of the Stream once the response has
                // been written. the file is streamed; it is never read into memory here
                return File(
                    fileStream: stream,
                    contentType: contentType,
                    fileDownloadName: Path.GetFileName(filename),
                    enableRangeProcessing: true);
            }
            catch (Exception ex) when (ex is UnauthorizedException || ex is ArgumentException)
            {
                Log.Warning("The file recorded for download {Id} is not one this application may serve: {Message}", guid, ex.Message);
                return Forbid();
            }
            catch (NotFoundException)
            {
                Log.Debug("The file recorded for download {Id} no longer exists at '{File}'", guid, filename);

                // the list may have said this file was there moments ago. it has just been proven wrong, and a cached
                // answer that outlives the proof by half a minute would keep offering a button that cannot work
                FileAvailability.Forget(filename);

                return NotFound();
            }
        }

        /// <summary>
        ///     Reports which of the specified downloads still have a file that can be archived.
        /// </summary>
        /// <param name="username">The username of the download source.</param>
        /// <param name="request">The ids of the downloads to check.</param>
        /// <returns></returns>
        /// <remarks>
        ///     Asked before an archive is started, because an archive is *streamed*: once its first byte is written the
        ///     response is committed, and there is no longer any way to tell the caller that half of what they asked
        ///     for was not there. This is what lets the UI say so while it can still be acted on.
        /// </remarks>
        /// <response code="200">The request completed successfully.</response>
        /// <response code="400">No ids were specified, or one of them is not a valid id.</response>
        /// <response code="403">Retrieval is disabled.</response>
        [HttpPost("downloads/{username}/archive/availability")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(typeof(ArchiveAvailabilityResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(403)]
        public IActionResult GetArchiveAvailability([FromRoute, UrlEncoded, Required] string username, [FromBody] ArchiveRequest request)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (!OptionsSnapshot.Value.RemoteFileRetrieval)
            {
                return Forbid();
            }

            if (!TryParseIds(request, out var ids, out var error))
            {
                return BadRequest(error);
            }

            var available = new List<ArchiveAvailabilityEntry>();
            var missing = new List<ArchiveAvailabilityEntry>();

            foreach (var id in ids)
            {
                var download = Transfers.Downloads.Find(t => t.Id == id);
                var entry = new ArchiveAvailabilityEntry { Id = id.ToString(), Filename = DisplayNameOf(download, id) };

                // opened and closed again rather than merely looked for, so that this answers exactly the question the
                // archive will ask of the same file a moment later -- whether it can be *read*, not only whether
                // something is sitting at that path
                if (TryOpenDownloadedFile(download, out var stream, out _))
                {
                    stream.Dispose();
                    available.Add(entry);
                }
                else
                {
                    missing.Add(entry);
                }
            }

            return Ok(new ArchiveAvailabilityResponse { Available = available, Missing = missing });
        }

        /// <summary>
        ///     Issues a short-lived, single-use ticket authorizing one download of an archive of the specified downloads.
        /// </summary>
        /// <param name="username">The username of the download source.</param>
        /// <param name="request">The ids of the downloads to be archived.</param>
        /// <returns></returns>
        /// <remarks>
        ///     The archive is fetched by navigating to it, so that the browser streams it to disk with its own download
        ///     manager instead of holding the whole thing in the memory of a tab -- and a navigation cannot carry an
        ///     Authorization header. This ticket is the credential for that one navigation. See
        ///     <see cref="DownloadTicketService"/> for what it is bound to and how long it lives.
        /// </remarks>
        /// <response code="200">The request completed successfully.</response>
        /// <response code="400">No ids were specified, or one of them is not a valid id.</response>
        /// <response code="403">Retrieval is disabled.</response>
        [HttpPost("downloads/{username}/archive/ticket")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(typeof(DownloadTicketResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(403)]
        public IActionResult CreateArchiveTicket([FromRoute, UrlEncoded, Required] string username, [FromBody] ArchiveRequest request)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (!OptionsSnapshot.Value.RemoteFileRetrieval)
            {
                return Forbid();
            }

            if (!TryParseIds(request, out var ids, out var error))
            {
                return BadRequest(error);
            }

            var (ticket, expiresAtUtc) = Tickets.Issue(username, ids);

            // the count, never the ticket. it is a bearer credential, and keeping it out of the log is the whole
            // reason it is tolerable to put one in a URL
            Log.Debug("Issued an archive download ticket for {Count} file(s) from {Username}", ids.Length, username);

            return Ok(new DownloadTicketResponse { Ticket = ticket, ExpiresAtUtc = expiresAtUtc });
        }

        /// <summary>
        ///     Gets an archive of the files produced by the downloads a ticket authorizes.
        /// </summary>
        /// <param name="username">The username of the download source.</param>
        /// <param name="ticket">A ticket issued for this set of downloads.</param>
        /// <returns></returns>
        /// <remarks>
        ///     <para>
        ///         <b>Anonymous by design, and authorized by the ticket instead.</b> A browser navigation cannot carry
        ///         an Authorization header, and a navigation is what lets the browser stream a large archive to disk
        ///         rather than into the memory of a tab. The ticket is therefore the credential: 256 random bits,
        ///         single-use, valid for a minute, and bound to the exact username and ids it was issued for, so one
        ///         that leaks cannot be replayed against anything else. It is issued only to an authenticated caller.
        ///     </para>
        ///     <para>
        ///         The archive is streamed and *stored* rather than deflated. Nothing is staged on disk: entries are
        ///         written straight to the response as they are read. Audio does not compress, so deflating would spend
        ///         CPU for nothing -- and storing is also what makes this a pass-through with nothing to wait for.
        ///     </para>
        ///     <para>
        ///         Every path is resolved from what this application recorded and opened through the file service,
        ///         entry by entry, exactly as a single-file retrieval is. A file that has gone missing since the
        ///         availability check is skipped and named in a MISSING.txt written at the end of the archive: a
        ///         response already most of the way to the browser must not be failed over one file.
        ///     </para>
        /// </remarks>
        /// <response code="200">The request completed successfully.</response>
        /// <response code="400">No ticket was specified.</response>
        /// <response code="403">Retrieval is disabled, or the ticket is not one issued for this request.</response>
        /// <response code="410">The ticket has expired.</response>
        [HttpGet("downloads/{username}/archive")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(FileResult), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(403)]
        [ProducesResponseType(410)]
        public async Task<IActionResult> GetArchiveAsync([FromRoute, UrlEncoded, Required] string username, [FromQuery] string ticket)
        {
            // before anything else, and before anything can log this request: the ticket travels in the query string,
            // because a navigation has nowhere else to put it. Serilog's request logging reads the raw target *after*
            // the pipeline has run, so overwriting it here is what keeps the ticket out of this application's own log.
            // (a proxy in front of this still records the URL it was asked for -- which is why a ticket is good for
            // one use and one minute)
            ScrubQueryFromRequestLog();

            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (!OptionsSnapshot.Value.RemoteFileRetrieval)
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(ticket))
            {
                return BadRequest("A ticket is required");
            }

            var redemption = Tickets.Redeem(ticket, username, out var ids);

            if (redemption == TicketRedemption.Expired)
            {
                Log.Debug("An expired archive download ticket was presented for {Username}", username);
                return StatusCode(StatusCodes.Status410Gone, "The download ticket has expired");
            }

            if (redemption != TicketRedemption.Valid)
            {
                Log.Warning("An invalid archive download ticket was presented for {Username}", username);
                return Forbid();
            }

            // ordered by remote filename rather than left in selection order, so that two identical requests produce
            // two identical archives -- which also makes the entry names, and any numbering applied to them, stable
            var downloads = ids
                .Select(id => (Id: id, Download: Transfers.Downloads.Find(t => t.Id == id)))
                .OrderBy(t => t.Download?.Filename ?? t.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var remoteFilenames = downloads.Select(t => t.Download?.Filename ?? t.Id.ToString()).ToList();
            var entryNames = ArchiveNaming.EntryNamesFor(remoteFilenames);
            var archiveName = ArchiveNaming.ArchiveNameFor(username, remoteFilenames, DateTime.UtcNow);

            var contentDisposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("attachment");
            contentDisposition.SetHttpFileName(archiveName);

            Response.ContentType = "application/zip";
            Response.Headers.ContentDisposition = contentDisposition.ToString();

            // no Content-Length. the size of a store-only archive *is* computable in advance, but only with a writer
            // that controls every byte, and a Content-Length wrong by one is a broken download

            // ask a reverse proxy not to buffer: nginx spools a proxied response to a temporary file by default, which
            // for a streamed archive means waiting for all of it before the browser is handed any of it
            Response.Headers["X-Accel-Buffering"] = "no";

            Log.Information("Streaming an archive of {Count} file(s) from {Username} as '{Archive}'", downloads.Count, username, archiveName);

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
                var bodyControl = HttpContext.Features.Get<IHttpBodyControlFeature>();

                if (bodyControl is not null)
                {
                    bodyControl.AllowSynchronousIO = true;
                }

                await using var archive = await ZipArchive.CreateAsync(Response.Body, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, HttpContext.RequestAborted);

                for (var i = 0; i < downloads.Count; i++)
                {
                    var download = downloads[i].Download;

                    if (!TryOpenDownloadedFile(download, out var source, out var reason))
                    {
                        Log.Warning("Skipping '{Entry}' in the archive for {Username}: {Reason}", entryNames[i], username, reason);
                        skipped.Add($"{entryNames[i]}{Environment.NewLine}    {reason}");
                        continue;
                    }

                    await using (source)
                    {
                        var entry = archive.CreateEntry(entryNames[i], CompressionLevel.NoCompression);

                        await using var target = await entry.OpenAsync(HttpContext.RequestAborted);
                        await source.CopyToAsync(target, HttpContext.RequestAborted);
                    }
                }

                // last, because entries are written in the order they are created, and this one can only be written
                // once every other entry has had its turn and either made it or not
                if (skipped.Count > 0)
                {
                    var entry = archive.CreateEntry(ArchiveNaming.MissingEntryName, CompressionLevel.NoCompression);

                    await using var target = await entry.OpenAsync(HttpContext.RequestAborted);
                    await using var writer = new StreamWriter(target);

                    await writer.WriteLineAsync($"{skipped.Count} of the {downloads.Count} selected file(s) could not be added to this archive.");
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
                Log.Debug("The archive download for {Username} ended early: {Message}", username, ex.Message);
            }

            return new EmptyResult();
        }

        /// <summary>
        ///     Gets the download for the specified username matching the specified filename, and requests
        ///     the current place in the remote queue of the specified download.
        /// </summary>
        /// <param name="username">The username of the download source.</param>
        /// <param name="id">The id of the download.</param>
        /// <returns></returns>
        /// <response code="200">The request completed successfully.</response>
        /// <response code="404">The specified download was not found.</response>
        [HttpGet("downloads/{username}/{id}/position")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(typeof(Transfer), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetPlaceInQueueAsync([FromRoute, UrlEncoded, Required] string username, [FromRoute, Required] string id)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (Users.IsBlacklisted(username))
            {
                return NotFound();
            }

            if (!Guid.TryParse(id, out var guid))
            {
                return BadRequest();
            }

            try
            {
                var place = await Transfers.Downloads.GetPlaceInQueueAsync(guid);
                return Ok(place);
            }
            catch (NotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        ///     Gets all uploads.
        /// </summary>
        /// <returns></returns>
        /// <response code="200">The request completed successfully.</response>
        [HttpGet("uploads")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(200)]
        public IActionResult GetUploads([FromQuery] bool includeRemoved = false)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            // todo: refactor this so it doesn't return the world. start and end time params
            // should be required.  consider pagination.
            var uploads = Transfers.Uploads.List(t => true, includeRemoved: includeRemoved);

            var response = uploads.GroupBy(t => t.Username).Select(grouping => new UserResponse()
            {
                Username = grouping.Key,
                Directories = grouping.GroupBy(g => g.Filename.DirectoryName()).Select(d => new DirectoryResponse()
                {
                    Directory = d.Key,
                    FileCount = d.Count(),
                    Files = d.ToList(),
                }),
            });

            return Ok(response);
        }

        /// <summary>
        ///     Gets all uploads for the specified username.
        /// </summary>
        /// <param name="username"></param>
        /// <returns></returns>
        /// <response code="200">The request completed successfully.</response>
        [HttpGet("uploads/{username}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(200)]
        public IActionResult GetUploads([FromRoute, UrlEncoded, Required] string username)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            var uploads = Transfers.Uploads.List(d => d.Username == username, includeRemoved: false);

            if (!uploads.Any())
            {
                return NotFound();
            }

            var response = new UserResponse()
            {
                Username = username,
                Directories = uploads.GroupBy(g => g.Filename.DirectoryName()).Select(d => new DirectoryResponse()
                {
                    Directory = d.Key,
                    FileCount = d.Count(),
                    Files = d.ToList(),
                }),
            };

            return Ok(response);
        }

        /// <summary>
        ///     Gets the upload for the specified username matching the specified filename.
        /// </summary>
        /// <param name="username">The username of the upload destination.</param>
        /// <param name="id">The id of the upload.</param>
        /// <returns></returns>
        /// <response code="200">The request completed successfully.</response>
        [HttpGet("uploads/{username}/{id}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(200)]
        public IActionResult GetUploads([FromRoute, UrlEncoded, Required] string username, [FromRoute, Required] string id)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (!Guid.TryParse(id, out var guid))
            {
                return BadRequest();
            }

            var upload = Transfers.Uploads.Find(t => t.Id == guid);

            if (upload == default)
            {
                return NotFound();
            }

            return Ok(upload);
        }

        /// <summary>
        ///     Parses the ids in the specified <paramref name="request"/>.
        /// </summary>
        /// <remarks>
        ///     Ids arrive as strings so that a malformed one can be reported as such. Bound as Guids they would become
        ///     an empty Guid, match no transfer, and be reported as a file that has gone missing -- an answer that is
        ///     both wrong and hard to argue with.
        /// </remarks>
        /// <param name="request">The request.</param>
        /// <param name="ids">The parsed ids, if all of them parsed.</param>
        /// <param name="error">What was wrong, if something was.</param>
        /// <returns>A value indicating whether the request carried a usable set of ids.</returns>
        private static bool TryParseIds(ArchiveRequest request, out Guid[] ids, out string error)
        {
            ids = null;
            error = null;

            var raw = request?.Ids?.ToArray() ?? [];

            if (raw.Length == 0)
            {
                error = "One or more download ids must be specified";
                return false;
            }

            var parsed = new List<Guid>();

            foreach (var value in raw)
            {
                if (!Guid.TryParse(value, out var id))
                {
                    error = $"'{value}' is not a valid download id";
                    return false;
                }

                parsed.Add(id);
            }

            ids = [.. parsed.Distinct()];
            return true;
        }

        /// <summary>
        ///     Returns the name to show an operator for the specified <paramref name="download"/>.
        /// </summary>
        /// <remarks>
        ///     The last segment of the remote filename -- what the transfer list already shows. Not the local path,
        ///     which is where the file sits on the server and is nobody's business in a confirmation dialog.
        /// </remarks>
        /// <param name="download">The download, if it is known.</param>
        /// <param name="id">The id, used when it isn't.</param>
        /// <returns>The name to display.</returns>
        private static string DisplayNameOf(slskd.Transfers.Transfer download, Guid id)
            => (download?.Filename ?? string.Empty)
                .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault() ?? id.ToString();

        /// <summary>
        ///     Opens the file produced by the specified <paramref name="download"/>, or says why it can't be opened.
        /// </summary>
        /// <remarks>
        ///     The several ways a download can have no file to offer -- unknown, unsuccessful, no recorded path, a
        ///     recorded path that no longer resolves to something readable inside an application-controlled directory
        ///     -- collapse to one answer here, because to a caller archiving a folder they are one fact. The
        ///     single-file endpoint keeps them apart, because there a status code can carry the difference.
        /// </remarks>
        /// <param name="download">The download.</param>
        /// <param name="stream">The opened file, if it could be opened.</param>
        /// <param name="reason">Why it could not be, if it could not.</param>
        /// <returns>A value indicating whether the file was opened.</returns>
        private bool TryOpenDownloadedFile(slskd.Transfers.Transfer download, out Stream stream, out string reason)
        {
            stream = null;
            reason = null;

            if (download is null)
            {
                reason = "this download is not known to this instance";
                return false;
            }

            if (!TransferStateCategories.Successful.Contains(download.State))
            {
                reason = $"this download did not succeed ({download.State})";
                return false;
            }

            if (string.IsNullOrWhiteSpace(download.LocalFilename))
            {
                reason = "no local file was recorded for this download";
                return false;
            }

            try
            {
                // through the file service, so that a path recorded from a filename a remote peer chose is checked
                // for containment before it is opened, exactly as it is everywhere else
                stream = Files.OpenFile(download.LocalFilename);
                return true;
            }
            catch (NotFoundException)
            {
                reason = "the file is no longer on disk";
                return false;
            }
            catch (Exception ex) when (ex is UnauthorizedException || ex is ArgumentException || ex is IOException)
            {
                reason = ex.Message;
                return false;
            }
        }

        /// <summary>
        ///     Replaces what this request will be logged as with its path alone, discarding the query string.
        /// </summary>
        /// <remarks>
        ///     Serilog's request logging reads the request's raw target -- which includes the query string -- once the
        ///     rest of the pipeline has run, so overwriting it from inside the action is what stops a credential in a
        ///     query string from reaching the log. Nothing downstream of an action reads the raw target for anything
        ///     other than diagnostics.
        /// </remarks>
        private void ScrubQueryFromRequestLog()
        {
            var feature = HttpContext.Features.Get<IHttpRequestFeature>();

            if (feature is not null)
            {
                feature.RawTarget = HttpContext.Request.Path;
            }
        }

        /// <summary>
        ///     Fills in the fields a download record cannot answer for itself.
        /// </summary>
        /// <remarks>
        ///     Both are questions about the world rather than about the record, and both are asked of a cache: see
        ///     <see cref="DownloadFileAvailability"/> and <see cref="slskd.Search.SearchExistence"/> for why neither
        ///     is a lookup per row per poll.
        ///
        ///     Here rather than in the service that lists them, for the same reason `LocalFileExists` was: they are
        ///     not persisted, they are not derivable from anything that is, and nothing but a caller drawing a list
        ///     has any use for them.
        /// </remarks>
        /// <param name="downloads">The downloads about to be served. Qualified because `Transfer` in this file is Soulseek's.</param>
        /// <returns>The operation context.</returns>
        private async Task AnnotateAsync(IEnumerable<slskd.Transfers.Transfer> downloads)
        {
            foreach (var download in downloads)
            {
                // say whether each finished download's file is still there, rather than leaving the UI to find out
                // by being refused
                if (download.State.HasFlag(TransferStates.Completed) && download.State.HasFlag(TransferStates.Succeeded))
                {
                    download.LocalFileExists = FileAvailability.Exists(download.LocalFilename);
                }

                /*
                    drop the id of a search that has since been deleted, and keep its text. The batch records both,
                    and only the id stops being true when the search goes: what was searched for still is, and it is
                    the more useful half. A caller can then tell the three cases apart -- no search, a search it can
                    still open, and a search that is gone -- which an empty field could not.
                */
                if (download.SearchId.HasValue && !await SearchExistence.ExistsAsync(download.SearchId.Value))
                {
                    download.SearchId = null;
                }
            }
        }
    }
}