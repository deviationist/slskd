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
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Threading;
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
        private Regex LogLineParseRegex { get; } = new Regex(@"^(\[?([a-zA-Z\.]*)\]?\s)?(\[((\d{4}-\d{2}-\d{2})T)?(\d{2}:\d{2}:\d{2}(?:.\d{3})?)\s([A-Z]+)\]\s)?(.*)", RegexOptions.Compiled);

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
        public async Task<IActionResult> List(CancellationToken cancellationToken, [FromQuery] bool download = false)
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
            if (string.IsNullOrWhiteSpace(filename))
            {
                return BadRequest("Filename is required");
            }

            if (filename.ContainsAny('/', '\\'))
            {
                return BadRequest("Filename must not contain a path");
            }

            var sanitizedFilename = FileSafety.GetFileNameSafely(filename, sanitize: true);

            if (!sanitizedFilename.Equals(filename, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("Input filename {Filename} sanitized to {Sanitized}", filename, sanitizedFilename);
                return BadRequest("Filename contains one or more invalid characters");
            }

            try
            {
                var stream = Files.GetFileContents(FileSafety.CombineSafely(Program.LogDirectory, sanitizedFilename));

                if (download)
                {
                    return File(stream, "text/plain", filename);
                }

                // future optimization: use IAsyncEnumerable to 'stream' the log records instead of beating up
                // memory to build a list. save each log in memory and then emit it when the next log
                // (starting with '[') is read, which indicates that it's not multi-line. in other words, a 1-log buffer
                // that is flushed when the next log is read, or the file is complete
                var logs = await ParseLogEntriesAsync(stream);

                return Ok(logs);
            }
            catch (UnauthorizedException)
            {
                return Unauthorized();
            }
            catch (NotFoundException)
            {
                return NotFound();
            }
        }

        private async Task<List<LogRecord>> ParseLogEntriesAsync(Stream stream)
        {
            Dictionary<string, string> levels = new()
            {
                ["VRB"] = nameof(Serilog.Events.LogEventLevel.Verbose),
                ["DBG"] = nameof(Serilog.Events.LogEventLevel.Debug),
                ["INF"] = nameof(Serilog.Events.LogEventLevel.Information),
                ["WRN"] = nameof(Serilog.Events.LogEventLevel.Warning),
                ["ERR"] = nameof(Serilog.Events.LogEventLevel.Error),
                ["FTL"] = nameof(Serilog.Events.LogEventLevel.Fatal),
            };

            var list = new List<LogRecord>();

            var currentLength = stream.Length;

            var reader = new StreamReader(stream);
            string line;
            Match match = default;

            while ((line = await reader.ReadLineAsync()) is not null)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                if (stream.Position > currentLength)
                {
                    // if we're reading the latest file, Serilog can append it while we're inside of this loop,
                    // or worse, a problem inside this loop appends lines. if this happens the position will grow
                    // beyond the length at the start
                    Log.Information("Parsing of log file {Filename} stopped before end of file; log was appended during the read");
                    break;
                }

                try
                {
                    // if the line isn't a log, append it to the previous message
                    if (!line.StartsWith('['))
                    {
                        var lastIndex = list.Count - 1;

                        if (lastIndex >= 0)
                        {
                            var old = list[lastIndex];

                            list[lastIndex] = new LogRecord
                            {
                                Timestamp = old.Timestamp,
                                Level = old.Level,
                                Message = old.Message + '\n' + line,
                            };
                        }

                        continue;
                    }

                    /*
                        there should be 3 possible line types:

                        debug:
                            [Some.Context] [2000-1-1T11:11:11 WRN] foo bar

                        info:
                            [2000-1-1T11:11:11 WRN] foo bar

                        line wrap/newline:
                            foo bar

                        additionally, date was added to disk log files around 9/1/26, so we need to gracefully handle
                        cases where the timestamp contains only hh:mm:ss, and substitute the unix epoch for the date

                        the regex includes 8 matching groups (hopefully this comment and the regex don't diverge!)
                        using the debug log as an example, the groups are:

                        0. [Some.Context] [2026-08-29T11:11:11 WRN] foo bar
                        1. [Some.Context]<space>
                        2. Some.Context
                        3. [2000-01-01T11:11:11 WRN]<space>
                        4. 2000-01-01T
                        5. 2000-01-01
                        6. 11:11:11
                        7. WRN
                        8. foo bar

                        the final group in the regex is simply `.*`, so we're guaranteed to get a match for every line
                    */
                    match = LogLineParseRegex.Match(line);

                    var grp = match.Groups;

                    var date = grp[5].Success ? DateOnly.Parse(grp[5].Value) : DateOnly.FromDateTime(DateTime.UnixEpoch);
                    var time = grp[6].Success ? TimeOnly.Parse(grp[6].Value) : TimeOnly.FromDateTime(DateTime.UnixEpoch);
                    var dateTime = date.ToDateTime(time);

                    var level = levels.ContainsKey(grp[7].Value) ? levels[grp[7].Value] : null;

                    list.Add(new LogRecord
                    {
                        Context = grp[2].Value,
                        Timestamp = dateTime,
                        Level = level,
                        Message = grp[8].Value,
                    });

                    Log.Information("Added: {Text}", grp[8].Value);
                }
                catch (Exception ex)
                {
                    Log.Debug("Error parsing log message: {Message}.  Line: {Line}", ex.Message, line);

                    Log.Warning("Got {GroupCount} groups", match.Groups.Count);

                    foreach (var key in match.Groups.Keys)
                    {
                        Log.Warning("Key: {Key}, Success: {Success}, Value: {Value}", key, match.Groups[key].Success, match.Groups[key].Value);
                    }

                    break;

                    list.Add(new LogRecord
                    {
                        Message = line,
                    });
                }
            }

            Log.Information("Returned {Lines}", list.Count);

            return list;
        }
    }
}
