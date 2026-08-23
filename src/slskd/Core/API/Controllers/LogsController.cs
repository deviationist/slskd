// <copyright file="LogsController.cs" company="JP Dillingham">
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

namespace slskd.Core.API
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Asp.Versioning;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Serilog;
    using slskd.Files;

    /// <summary>
    ///     Logs.
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("0")]
    [ApiController]
    [Produces("application/json")]
    [Consumes("application/json")]
    public class LogsController : ControllerBase
    {
        public LogsController(
            FileService fileService)
        {
            Files = fileService;
        }

        private FileService Files { get; }
        private ILogger Log { get; } = Serilog.Log.ForContext<ApplicationController>();

        /// <summary>
        ///     Gets the last few application logs.
        /// </summary>
        /// <returns></returns>
        [HttpGet("live")]
        [Authorize(Policy = AuthPolicy.Any, Roles = AuthRole.AdministratorOnly)]
        public IActionResult Logs()
        {
            return Ok(Program.LogBuffer);
        }

        /// <summary>
        ///     Lists the log files currently on disk.
        /// </summary>
        /// <returns></returns>
        [HttpGet("files")]
        [Authorize(Policy = AuthPolicy.Any, Roles = AuthRole.AdministratorOnly)]
        public async Task<IActionResult> List()
        {
            var directory = await Files.ListDirectoryContentsAsync(System.IO.Path.GetFullPath(Program.LogDirectory), enumerationOptions: new EnumerationOptions
            {
                AttributesToSkip = FileAttributes.System,
                RecurseSubdirectories = false,
            });

            return Ok(directory.Files);
        }

        /// <summary>
        ///     Retrieves the requested log file from disk.
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="download"></param>
        /// <returns></returns>
        [HttpGet("files/{filename}")]
        [Authorize(Policy = AuthPolicy.Any, Roles = AuthRole.AdministratorOnly)]
        public async Task<IActionResult> Get(string filename, [FromQuery] bool download = false)
        {
            filename = FileSafety.GetFileNameSafely(filename);

            try
            {
                var stream = Files.GetFileContents(FileSafety.CombineSafely(Program.LogDirectory, filename));

                if (download)
                {
                    return File(stream, "text/plain", filename);
                }

                return File(stream, "text/plain");
            }
            catch (UnauthorizedException)
            {
                return NotFound();
            }
            catch (NotFoundException)
            {
                return NotFound();
            }
        }
    }
}
