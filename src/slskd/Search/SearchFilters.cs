// <copyright file="SearchFilters.cs" company="JP Dillingham">
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
namespace slskd.Search;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
///     The filter language used on the search results page, evaluated here.
/// </summary>
/// <remarks>
///     <para>
///         A port of <c>parseFiltersFromString</c> and <c>filterResponse</c> in <c>src/web/src/lib/searches.js</c>,
///         needed because a watch decides what to report without a browser present, and must decide it exactly as the
///         page would. Both implementations are checked against one shared corpus of vectors; two implementations of
///         one grammar drift the moment nothing forces them together.
///     </para>
///     <para>
///         Several behaviours here read as mistakes and are reproduced deliberately, because the page has them and
///         agreement matters more than either being right:
///     </para>
///     <list type="bullet">
///         <item><c>minfilesize</c> compares against bytes, so the page's own placeholder value of 10 excludes
///         nothing at all; <c>minlength</c> is in seconds, so 5000 means eighty-three minutes.</item>
///         <item>An unreported attribute is compared as zero and so fails every minimum, while the same attribute
///         decides <c>iscbr</c> and <c>isvbr</c> in opposite directions. Both follow from the page testing
///         <c>undefined</c> against values the API sends as <c>null</c>.</item>
///         <item><c>minfilesinfolder</c> empties the file list but leaves the count, so the caller's own
///         "did anything survive" test still passes and the response renders with nothing in it.</item>
///         <item><c>islossless</c> is a metadata heuristic, not a format check: it requires a sample rate and a bit
///         depth to have been reported, so a genuine FLAC from a peer that reports neither fails it.</item>
///     </list>
/// </remarks>
public static class SearchFilters
{
    private static readonly Regex MinBitRate = new(@"(minbr|minbitrate):(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MinBitDepth = new(@"(minbd|minbitdepth):(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MinFileSize = new(@"(minfs|minfilesize):(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MinLength = new(@"(minlen|minlength):(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MinFilesInFolder = new(@"(minfif|minfilesinfolder):(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] Flags = ["isvbr", "iscbr", "islossless", "islossy"];

    /// <summary>
    ///     Reads a filter string.
    /// </summary>
    /// <param name="text">The filter string.</param>
    /// <returns>The parsed filters.</returns>
    public static SearchFilter Parse(string text)
    {
        text ??= string.Empty;

        // note the split on a single space, matching the page: consecutive spaces yield empty terms, which are kept
        // as include terms and match everything, because "" is a substring of any filename
        var terms = text
            .ToLowerInvariant()
            .Split(' ')
            .Where(term => !term.Contains(':') && !Flags.Contains(term))
            .ToList();

        return new SearchFilter
        {
            MinBitRate = Match(MinBitRate, text),
            MinBitDepth = Match(MinBitDepth, text),
            MinFileSize = Match(MinFileSize, text),
            MinLength = Match(MinLength, text),
            MinFilesInFolder = Match(MinFilesInFolder, text),
            IsVBR = Regex.IsMatch(text, "isvbr", RegexOptions.IgnoreCase),
            IsCBR = Regex.IsMatch(text, "iscbr", RegexOptions.IgnoreCase),
            IsLossless = Regex.IsMatch(text, "islossless", RegexOptions.IgnoreCase),
            IsLossy = Regex.IsMatch(text, "islossy", RegexOptions.IgnoreCase),
            Include = [.. terms.Where(term => !term.StartsWith('-'))],
            Exclude = [.. terms.Where(term => term.StartsWith('-')).Select(term => term[1..])],
        };
    }

    /// <summary>
    ///     Returns a value indicating whether the specified <paramref name="file"/> passes the specified
    ///     <paramref name="filter"/>.
    /// </summary>
    /// <param name="filter">The filter to apply.</param>
    /// <param name="file">The file to test.</param>
    /// <returns>Whether the file passes.</returns>
    public static bool Passes(SearchFilter filter, File file)
    {
        // these two are asymmetric, and not by design: the page tests `flag === undefined || flag` for one and
        // `flag === undefined || !flag` for the other, while the API sends *null* for an unreported flag. `null ===
        // undefined` is false, `null` is falsy and `!null` is true -- so iscbr keeps a file whose flag was never
        // reported and isvbr rejects it. Reproduced, because the page does it.
        if (filter.IsCBR && file.IsVariableBitRate == true)
        {
            return false;
        }

        if (filter.IsVBR && file.IsVariableBitRate != true)
        {
            return false;
        }

        // 'falsy', not 'absent': a reported sample rate of zero counts as missing, as it does on the page
        var hasSampleRate = file.SampleRate.GetValueOrDefault() != 0;
        var hasBitDepth = file.BitDepth.GetValueOrDefault() != 0;

        if (filter.IsLossless && (!hasSampleRate || !hasBitDepth))
        {
            return false;
        }

        if (filter.IsLossy && (hasSampleRate || hasBitDepth))
        {
            return false;
        }

        // an unreported value is compared as zero, so it fails any minimum above zero. `null < 320` is true in
        // JavaScript -- null converts to 0 -- which is the opposite of what `undefined < 320` does, and the API
        // sends null. So a peer that reports no bitrate is excluded by minbitrate rather than let through.
        if ((file.BitRate ?? 0) < filter.MinBitRate)
        {
            return false;
        }

        if ((file.BitDepth ?? 0) < filter.MinBitDepth)
        {
            return false;
        }

        if (file.Size < filter.MinFileSize)
        {
            return false;
        }

        if ((file.Length ?? 0) < filter.MinLength)
        {
            return false;
        }

        var filename = (file.Filename ?? string.Empty).ToLowerInvariant();

        if (filter.Include.Count > 0 && !filter.Include.All(term => filename.Contains(term)))
        {
            return false;
        }

        if (filter.Exclude.Any(term => filename.Contains(term)))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Applies the specified <paramref name="filter"/> to the specified <paramref name="response"/>.
    /// </summary>
    /// <param name="filter">The filter to apply.</param>
    /// <param name="response">The response to filter.</param>
    /// <returns>The response, with the files that did not pass removed.</returns>
    public static Response Apply(SearchFilter filter, Response response)
    {
        // deliberately as the page has it: below the threshold the files go and the *counts* stay, so a caller
        // testing "did anything survive" on the counts still sees something
        if (response.FileCount + response.LockedFileCount < filter.MinFilesInFolder)
        {
            return new Response
            {
                FileCount = response.FileCount,
                Files = [],
                HasFreeUploadSlot = response.HasFreeUploadSlot,
                LockedFileCount = response.LockedFileCount,
                LockedFiles = response.LockedFiles,
                QueueLength = response.QueueLength,
                Token = response.Token,
                UploadSpeed = response.UploadSpeed,
                Username = response.Username,
            };
        }

        var files = response.Files.Where(file => Passes(filter, file)).ToList();
        var locked = response.LockedFiles.Where(file => Passes(filter, file)).ToList();

        return new Response
        {
            FileCount = files.Count,
            Files = files,
            HasFreeUploadSlot = response.HasFreeUploadSlot,
            LockedFileCount = locked.Count,
            LockedFiles = locked,
            QueueLength = response.QueueLength,
            Token = response.Token,
            UploadSpeed = response.UploadSpeed,
            Username = response.Username,
        };
    }

    private static int Match(Regex regex, string text)
    {
        var match = regex.Match(text);

        return match.Success && int.TryParse(match.Groups[2].Value, out var value) ? value : 0;
    }
}

/// <summary>
///     A parsed filter string.
/// </summary>
public record SearchFilter
{
    public int MinBitRate { get; init; }

    public int MinBitDepth { get; init; }

    public int MinFileSize { get; init; }

    public int MinLength { get; init; }

    public int MinFilesInFolder { get; init; }

    public bool IsVBR { get; init; }

    public bool IsCBR { get; init; }

    public bool IsLossless { get; init; }

    public bool IsLossy { get; init; }

    public IReadOnlyList<string> Include { get; init; } = [];

    public IReadOnlyList<string> Exclude { get; init; } = [];
}
