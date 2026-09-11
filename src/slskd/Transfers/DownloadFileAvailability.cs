// <copyright file="DownloadFileAvailability.cs" company="JP Dillingham">
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
using System.IO;
using Microsoft.Extensions.Caching.Memory;

/// <summary>
///     Answers whether the file a completed download produced is still on disk.
/// </summary>
/// <remarks>
///     <para>
///         A transfer keeps the path it was written to whether or not anything is still there, so every field a
///         download record has says "yes" for a file that has gone. Something downstream may move finished files out
///         of the downloads directory, which makes a completed download with no file the normal end of its life
///         rather than a rarity, and a list that cannot say so offers buttons that can only be refused.
///     </para>
///     <para>
///         Cached, because the transfer list is polled once a second and the answer changes on the timescale of
///         somebody moving files. Each entry's lifetime is jittered so that a list filled in one burst does not
///         expire in one burst and stat every file again at the same instant.
///     </para>
///     <para>
///         This asks only whether the file <em>is there</em>. Whether it can be <em>read</em> is a stronger question,
///         and one worth paying more for where the answer is a promise: the archive pre-flight opens each file for
///         exactly that reason. Here the cost would be an open per completed row per poll, and a click already gives
///         the authoritative answer to anyone who wants one.
///     </para>
/// </remarks>
public class DownloadFileAvailability
{
    private const int MinimumLifetimeSeconds = 25;
    private const int MaximumLifetimeSeconds = 35;

    private IMemoryCache Cache { get; } = new MemoryCache(new MemoryCacheOptions());

    /// <summary>
    ///     Gets a value indicating whether the file at the specified <paramref name="filename"/> exists, or null if
    ///     the question does not apply because no filename was recorded.
    /// </summary>
    /// <param name="filename">The fully qualified filename to check.</param>
    /// <returns>Whether the file exists, or null if no filename was supplied.</returns>
    public bool? Exists(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return null;
        }

        if (Cache.TryGetValue<bool>(filename, out var cached))
        {
            return cached;
        }

        var exists = File.Exists(filename);

        Cache.Set(
            key: filename,
            value: exists,
            absoluteExpirationRelativeToNow: TimeSpan.FromSeconds(
                Random.Shared.Next(MinimumLifetimeSeconds, MaximumLifetimeSeconds + 1)));

        return exists;
    }

    /// <summary>
    ///     Forgets the cached answer for the specified <paramref name="filename"/>, so that the next question is
    ///     asked of the filesystem.
    /// </summary>
    /// <remarks>
    ///     Called where this application has just learned something the cache cannot know yet -- a retrieval that was
    ///     refused, or a file it deleted itself -- so that a stale "yes" does not outlive the fact by half a minute.
    /// </remarks>
    /// <param name="filename">The fully qualified filename to forget.</param>
    public void Forget(string filename)
    {
        if (!string.IsNullOrWhiteSpace(filename))
        {
            Cache.Remove(filename);
        }
    }
}
