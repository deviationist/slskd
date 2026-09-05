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
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Asp.Versioning;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
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
        public TransfersController(
            TransferService transferService,
            IUserService userService,
            FileService fileService,
            IOptionsSnapshot<Options> optionsSnapshot)
        {
            Transfers = transferService;
            Users = userService;
            Files = fileService;
            OptionsSnapshot = optionsSnapshot;
        }

        private static SemaphoreSlim DownloadRequestLimiter { get; } = new SemaphoreSlim(2, 2);
        private TransferService Transfers { get; }
        private IUserService Users { get; }
        private FileService Files { get; }
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
        /// <response code="200">The download was cancelled successfully, and the outcome of the file deletion is reported.</response>
        /// <response code="204">The download was cancelled successfully.</response>
        /// <response code="400">File deletion was requested without removal, or for a transfer that has not finished.</response>
        /// <response code="403">File deletion was requested, but it is disabled.</response>
        /// <response code="404">The specified download was not found.</response>
        [HttpDelete("downloads/{username}/{id}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(typeof(FileDeletionResult), 200)]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> CancelDownloadAsync([FromRoute, UrlEncoded, Required] string username, [FromRoute, Required] string id, [FromQuery] bool remove = false, [FromQuery] bool deleteFile = false)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (!Guid.TryParse(id, out var guid))
            {
                return BadRequest();
            }

            // the option is `delete_file_on_removal`, and that is the whole scope of what it grants.
            // deleting the file while keeping the record is a different thing, which nobody enabled --
            // and it leaves a transfer listed as a completed download whose file is not there, which is
            // exactly the stale state this feature exists to stop producing.
            if (deleteFile && !remove)
            {
                return BadRequest("deleteFile requires remove; the file is deleted when the download is removed");
            }

            if (deleteFile && !OptionsSnapshot.Value.Transfers.Download.DeleteFileOnRemoval)
            {
                Log.Warning("Deletion of the file for download {Id} forbidden; transfers.download.delete_file_on_removal is disabled", guid);
                return Forbid();
            }

            try
            {
                // read the record before removing it, while it is still there to be read; Remove() is a soft
                // delete, but Find() would still be a second trip and this is the only thing that knows where
                // the file is
                // `Transfer` alone is Soulseek.NET's, this file having `using Soulseek`; ours is the record
                slskd.Transfers.Transfer transfer = deleteFile ? Transfers.Downloads.Find(t => t.Id == guid) : null;

                // an id that names no download is a 404, the same as it is for a cancellation. only
                // checked when the record was read for deletion; without that this endpoint has never
                // looked, and starting to look would be a change to what it answers
                if (deleteFile && transfer is null)
                {
                    return NotFound();
                }

                // the recorded path is the incomplete file while a download is running, and unlinking a
                // file that is being written to is either allowed and confusing (POSIX: the writer keeps
                // the inode and the move at the end fails) or refused outright (Windows: the stream is
                // opened FileShare.None). refused here rather than reported in the result, because the
                // record would not be removed either -- Remove() only touches terminal transfers -- so
                // honouring half of it would answer "removed, but the file is still there" to a caller
                // whose download is also still listed and still running.
                if (deleteFile && !transfer.State.HasFlag(TransferStates.Completed))
                {
                    return BadRequest("the transfer has not finished; cancel it first");
                }

                Transfers.Downloads.TryCancel(guid);

                if (remove)
                {
                    Transfers.Downloads.Remove(guid);
                }

                if (!deleteFile)
                {
                    return NoContent();
                }

                return Ok(await DeleteDownloadedFileAsync(transfer));
            }
            catch (NotFoundException)
            {
                return NotFound();
            }
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
        private async Task<FileDeletionResult> DeleteDownloadedFileAsync(slskd.Transfers.Transfer transfer)
        {
            var filename = transfer?.LocalFilename;

            if (string.IsNullOrWhiteSpace(filename))
            {
                // said rather than reported as a failure: a download from before this was recorded has no
                // path, and that is an ordinary answer to "delete the file", not an error
                Log.Debug("No local file is recorded for download {Id}; nothing to delete", transfer?.Id);
                return new FileDeletionResult { Deleted = false, Filename = null, Error = null };
            }

            try
            {
                var results = await Files.DeleteFilesAsync(filename);

                return results[filename].Match(
                    success => new FileDeletionResult { Deleted = true, Filename = filename, Error = null },
                    failure => new FileDeletionResult { Deleted = false, Filename = filename, Error = failure.Message });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to delete the file for download {Id}: {Message}", transfer.Id, ex.Message);
                return new FileDeletionResult { Deleted = false, Filename = filename, Error = ex.Message };
            }
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
        public IActionResult GetDownloadsAsync([FromQuery] bool includeRemoved = false)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            var downloads = Transfers.Downloads.List(includeRemoved: includeRemoved);

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
        public IActionResult GetDownloadsAsync([FromRoute, UrlEncoded, Required] string username)
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
        public IActionResult GetDownload([FromRoute, UrlEncoded, Required] string username, [FromRoute, Required] string id)
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

            return Ok(download);
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
    }
}