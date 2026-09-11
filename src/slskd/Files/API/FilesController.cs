// <copyright file="FilesController.cs" company="JP Dillingham">
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

namespace slskd.Files.API
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.IO;
    using System.Security;
    using System.Threading.Tasks;
    using Asp.Versioning;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Serilog;

    /// <summary>
    ///     Files.
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("0")]
    [ApiController]
    [Produces("application/json")]
    [Consumes("application/json")]
    public class FilesController : ControllerBase
    {
        public FilesController(
            FileService fileService,
            IOptionsSnapshot<Options> optionsSnapshot)
        {
            Files = fileService;
            OptionsSnapshot = optionsSnapshot;
        }

        private FileService Files { get; }
        private IOptionsSnapshot<Options> OptionsSnapshot { get; }
        private ILogger Log { get; set; } = Serilog.Log.ForContext<FilesController>();

        /// <summary>
        ///     Lists the contents of the downloads directory.
        /// </summary>
        /// <param name="recursive">An optional value indicating whether to recursively list subdirectories and files.</param>
        /// <returns></returns>
        /// <response code="200">The request completed successfully.</response>
        /// <response code="401">Authentication failed.</response>
        [HttpGet("downloads/directories")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(typeof(FilesystemDirectory), 200)]
        [ProducesResponseType(401)]
        public Task<IActionResult> GetDownloadContentsAsync([FromQuery] bool recursive = false)
            => ListDirectoryAsync(rootDirectory: OptionsSnapshot.Value.Directories.Downloads, base64SubdirectoryName: null, recursive);

        /// <summary>
        ///     Lists the contents of the specified subdirectory within the downloads directory.
        /// </summary>
        /// <param name="base64SubdirectoryName">The relative, base 64 encoded, name of the subdirectory to list.</param>
        /// <param name="recursive">An optional value indicating whether to recursively list subdirectories and files.</param>
        /// <returns></returns>
        /// <response code="200">The request completed successfully.</response>
        /// <response code="400">The specified subdirectory name is not a valid base 64 encoded path.</response>
        /// <response code="401">Authentication failed.</response>
        /// <response code="403">Access to the specified subdirectory was denied.</response>
        /// <response code="404">The specified subdirectory does not exist.</response>
        [HttpGet("downloads/directories/{base64SubdirectoryName}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(typeof(FilesystemDirectory), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public Task<IActionResult> GetDownloadSubdirectoryContentsAsync([FromRoute] string base64SubdirectoryName, [FromQuery] bool recursive = false)
            => ListDirectoryAsync(rootDirectory: OptionsSnapshot.Value.Directories.Downloads, base64SubdirectoryName, recursive);

        /// <summary>
        ///     Deletes the specified subdirectory within the downloads directory.
        /// </summary>
        /// <param name="base64SubdirectoryName">The relative, base 64 encoded, name of the subdirectory to delete.</param>
        /// <returns></returns>
        /// <response code="204">The request completed successfully.</response>
        /// <response code="400">The specified subdirectory name is not a valid base 64 encoded path.</response>
        /// <response code="401">Authentication failed.</response>
        /// <response code="403">Access to the specified subdirectory was denied.</response>
        /// <response code="404">The specified subdirectory does not exist.</response>
        [HttpDelete("downloads/directories/{base64SubdirectoryName}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        public Task<IActionResult> DeleteDownloadSubdirectoryAsync([FromRoute] string base64SubdirectoryName)
            => DeleteSubdirectoryAsync(rootDirectory: OptionsSnapshot.Value.Directories.Downloads, base64SubdirectoryName);

        /// <summary>
        ///     Deletes the specified file within the downloads directory.
        /// </summary>
        /// <param name="base64FileName">The relative, base 64 encoded, name of the file to delete.</param>
        /// <returns></returns>
        /// <response code="204">The request completed successfully.</response>
        /// <response code="400">The specified file name is not a valid base 64 encoded path.</response>
        /// <response code="401">Authentication failed.</response>
        /// <response code="403">Access to the specified subdirectory was denied.</response>
        /// <response code="404">The specified subdirectory does not exist.</response>
        [HttpDelete("downloads/files/{base64FileName}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        public Task<IActionResult> DeleteDownloadFileAsync([FromRoute] string base64FileName)
            => DeleteFileAsync(rootDirectory: OptionsSnapshot.Value.Directories.Downloads, base64FileName);

        /// <summary>
        ///     Lists the contents of the downloads directory.
        /// </summary>
        /// <param name="recursive">An optional value indicating whether to recursively list subdirectories and files.</param>
        /// <returns></returns>
        /// <response code="200">The request completed successfully.</response>
        /// <response code="401">Authentication failed.</response>
        [HttpGet("incomplete/directories")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(typeof(FilesystemDirectory), 200)]
        public Task<IActionResult> GetIncompleteContentsAsync([FromQuery] bool recursive = false)
            => ListDirectoryAsync(rootDirectory: OptionsSnapshot.Value.Directories.Incomplete, base64SubdirectoryName: null, recursive);

        /// <summary>
        ///     Lists the contents of the specified subdirectory within the incomplete directory.
        /// </summary>
        /// <param name="base64SubdirectoryName">The relative, base 64 encoded, name of the subdirectory to list.</param>
        /// <param name="recursive">An optional value indicating whether to recursively list subdirectories and files.</param>
        /// <returns></returns>
        /// <response code="200">The request completed successfully.</response>
        /// <response code="400">The specified subdirectory name is not a valid base 64 encoded path.</response>
        /// <response code="401">Authentication failed.</response>
        /// <response code="403">Access to the specified subdirectory was denied.</response>
        /// <response code="404">The specified subdirectory does not exist.</response>
        [HttpGet("incomplete/directories/{base64SubdirectoryName}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(typeof(FilesystemDirectory), 200)]
        [ProducesResponseType(400)]
        public Task<IActionResult> GetIncompleteSubdirectoryContentsAsync([FromRoute, Required] string base64SubdirectoryName, [FromQuery] bool recursive = false)
            => ListDirectoryAsync(rootDirectory: OptionsSnapshot.Value.Directories.Incomplete, base64SubdirectoryName, recursive);

        /// <summary>
        ///     Deletes the specified subdirectory within the downloads directory.
        /// </summary>
        /// <param name="base64SubdirectoryName">The relative, base 64 encoded, name of the subdirectory to delete.</param>
        /// <returns></returns>
        /// <response code="204">The request completed successfully.</response>
        /// <response code="400">The specified subdirectory name is not a valid base 64 encoded path.</response>
        /// <response code="401">Authentication failed.</response>
        /// <response code="403">Access to the specified subdirectory was denied.</response>
        /// <response code="404">The specified subdirectory does not exist.</response>
        [HttpDelete("incomplete/directories/{base64SubdirectoryName}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        public Task<IActionResult> DeleteIncompleteSubdirectoryAsync([FromRoute] string base64SubdirectoryName)
            => DeleteSubdirectoryAsync(rootDirectory: OptionsSnapshot.Value.Directories.Incomplete, base64SubdirectoryName);

        /// <summary>
        ///     Deletes the specified file within the downloads directory.
        /// </summary>
        /// <param name="base64FileName">The relative, base 64 encoded, name of the file to delete.</param>
        /// <returns></returns>
        /// <response code="204">The request completed successfully.</response>
        /// <response code="400">The specified file name is not a valid base 64 encoded path.</response>
        /// <response code="401">Authentication failed.</response>
        /// <response code="403">Access to the specified subdirectory was denied.</response>
        /// <response code="404">The specified subdirectory does not exist.</response>
        [HttpDelete("incomplete/files/{base64FileName}")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        public Task<IActionResult> DeleteIncompleteFileAsync([FromRoute] string base64FileName)
            => DeleteFileAsync(rootDirectory: OptionsSnapshot.Value.Directories.Incomplete, base64FileName);

        /// <summary>
        ///     Decodes the specified base 64 encoded, relative <paramref name="base64Path"/> and resolves it against the
        ///     specified <paramref name="rootDirectory"/>.
        /// </summary>
        /// <remarks>
        ///     A caller-supplied value that can't be decoded, or that decodes to something that can't be resolved to a path,
        ///     is a bad request rather than a server error; both cases return false instead of throwing.
        /// </remarks>
        /// <param name="rootDirectory">The fully qualified directory against which to resolve the decoded path.</param>
        /// <param name="base64Path">The relative, base 64 encoded path to decode.</param>
        /// <param name="trimLeadingSeparators">
        ///     A value indicating whether leading directory separators should be trimmed from the decoded path before it is
        ///     resolved. Note that this determines whether a decoded path that is rooted escapes the root directory.
        /// </param>
        /// <param name="resolvedPath">The resolved, fully qualified path.</param>
        /// <returns>A value indicating whether the specified path was decoded and resolved successfully.</returns>
        private static bool TryResolvePath(string rootDirectory, string base64Path, bool trimLeadingSeparators, out string resolvedPath)
        {
            try
            {
                var decoded = base64Path
                    .FromBase64()
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);

                if (trimLeadingSeparators)
                {
                    decoded = decoded.TrimStart(Path.DirectorySeparatorChar);
                }

                resolvedPath = Path.GetFullPath(Path.Combine(rootDirectory, decoded));
                return true;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                // FormatException: the value isn't valid base 64.
                // ArgumentException: it decoded, but to something Path can't work with (a null character, for example).
                resolvedPath = null;
                return false;
            }
        }

        private async Task<IActionResult> ListDirectoryAsync(string rootDirectory, string base64SubdirectoryName = null, bool recursive = false)
        {
            if (!TryResolvePath(rootDirectory, base64SubdirectoryName ?? string.Empty, trimLeadingSeparators: false, out var requestedDir))
            {
                Log.Debug("Directory listing requested with a malformed subdirectory name '{Name}'", base64SubdirectoryName);
                return BadRequest("The specified subdirectory name is not a valid base 64 encoded path");
            }

            Log.Debug("Listing directory '{Directory}'", requestedDir);

            try
            {
                var response = await Files.ListContentsAsync(
                    directory: requestedDir,
                    enumerationOptions: new EnumerationOptions
                    {
                        AttributesToSkip = FileAttributes.System,
                        RecurseSubdirectories = recursive,
                    });

                return Ok(response);
            }
            catch (Exception ex) when (ex is UnauthorizedException or SecurityException)
            {
                Log.Warning("Directory listing of '{Directory}' forbidden", requestedDir);
                return Forbid();
            }
            catch (NotFoundException)
            {
                Log.Debug("Directory '{Directory}' not found", requestedDir);
                return NotFound();
            }
        }

        private async Task<IActionResult> DeleteSubdirectoryAsync(string rootDirectory, string base64SubdirectoryName)
        {
            if (!OptionsSnapshot.Value.RemoteFileManagement)
            {
                return Forbid();
            }

            if (!TryResolvePath(rootDirectory, base64SubdirectoryName, trimLeadingSeparators: true, out var requestedDir))
            {
                Log.Information("Directory deletion requested with a malformed subdirectory name '{Name}'", base64SubdirectoryName);
                return BadRequest("The specified subdirectory name is not a valid base 64 encoded path");
            }

            Log.Information("Deleting directory '{Directory}'", requestedDir);

            try
            {
                var results = await Files.DeleteDirectoriesAsync(requestedDir);

                return results[requestedDir].Match(
                    success => NoContent(),
                    failure => throw failure);
            }
            catch (Exception ex) when (ex is UnauthorizedException or SecurityException)
            {
                Log.Warning("Directory deletion of '{Directory}' forbidden", requestedDir);
                return Forbid();
            }
            catch (NotFoundException)
            {
                Log.Information("Directory '{Directory}' not found", requestedDir);
                return NotFound();
            }
        }

        private async Task<IActionResult> DeleteFileAsync(string rootDirectory, string base64FileName)
        {
            if (!OptionsSnapshot.Value.RemoteFileManagement)
            {
                return Forbid();
            }

            if (!TryResolvePath(rootDirectory, base64FileName, trimLeadingSeparators: true, out var requestedFilename))
            {
                Log.Information("File deletion requested with a malformed file name '{Name}'", base64FileName);
                return BadRequest("The specified file name is not a valid base 64 encoded path");
            }

            Log.Information("Deleting file '{File}'", requestedFilename);

            try
            {
                var results = await Files.DeleteFilesAsync(requestedFilename);

                return results[requestedFilename].Match(
                    success => NoContent(),
                    failure => throw failure);
            }
            catch (Exception ex) when (ex is UnauthorizedException or SecurityException)
            {
                Log.Warning("File deletion of '{File}' forbidden", requestedFilename);
                return Forbid();
            }
            catch (NotFoundException)
            {
                Log.Information("File '{File}' not found", requestedFilename);
                return NotFound();
            }
        }
    }
}
