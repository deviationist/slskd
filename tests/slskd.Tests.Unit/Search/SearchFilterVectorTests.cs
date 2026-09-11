// <copyright file="SearchFilterVectorTests.cs" company="JP Dillingham">
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
namespace slskd.Tests.Unit.Search;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using slskd.Search;
using Xunit;

/// <summary>
///     Runs the shared filter vectors against this implementation.
/// </summary>
/// <remarks>
///     The same file is read by the page's own suite. A change to either implementation that is not made to both
///     fails here, which is the entire point of keeping the corpus outside both of them.
/// </remarks>
public class SearchFilterVectorTests
{
    private static readonly JsonDocument Vectors = JsonDocument.Parse(System.IO.File.ReadAllText(VectorPath()));

    public static TheoryData<string, string, string, bool> ApplyCases()
    {
        var data = new TheoryData<string, string, string, bool>();

        foreach (var c in Vectors.RootElement.GetProperty("cases").EnumerateArray())
        {
            var name = c.GetProperty("name").GetString();
            var filter = c.GetProperty("filter").GetString();

            foreach (var (property, expected) in new[] { ("passes", true), ("rejects", false) })
            {
                if (c.TryGetProperty(property, out var files))
                {
                    foreach (var file in files.EnumerateArray())
                    {
                        data.Add(name, filter, file.GetString(), expected);
                    }
                }
            }
        }

        return data;
    }

    public static TheoryData<string> ParseCases()
    {
        var data = new TheoryData<string>();

        for (var i = 0; i < Vectors.RootElement.GetProperty("parseCases").GetArrayLength(); i++)
        {
            data.Add(i.ToString());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ApplyCases))]
    public void A_File_Is_Judged_As_The_Page_Would_Judge_It(string name, string filter, string file, bool expected)
    {
        var parsed = SearchFilters.Parse(filter);
        var passes = SearchFilters.Passes(parsed, FileNamed(file));

        Assert.True(expected == passes, $"{name}: expected '{file}' to {(expected ? "pass" : "be rejected by")} '{filter}'");
    }

    [Theory]
    [MemberData(nameof(ParseCases))]
    public void A_Filter_String_Is_Read_As_The_Page_Reads_It(string index)
    {
        var c = Vectors.RootElement.GetProperty("parseCases")[int.Parse(index)];
        var filter = SearchFilters.Parse(c.GetProperty("filter").GetString());
        var expect = c.GetProperty("expect");

        foreach (var property in expect.EnumerateObject())
        {
            switch (property.Name)
            {
                case "minBitRate": Assert.Equal(property.Value.GetInt32(), filter.MinBitRate); break;
                case "minBitDepth": Assert.Equal(property.Value.GetInt32(), filter.MinBitDepth); break;
                case "minFileSize": Assert.Equal(property.Value.GetInt32(), filter.MinFileSize); break;
                case "minLength": Assert.Equal(property.Value.GetInt32(), filter.MinLength); break;
                case "minFilesInFolder": Assert.Equal(property.Value.GetInt32(), filter.MinFilesInFolder); break;
                case "isVBR": Assert.Equal(property.Value.GetBoolean(), filter.IsVBR); break;
                case "isCBR": Assert.Equal(property.Value.GetBoolean(), filter.IsCBR); break;
                case "isLossless": Assert.Equal(property.Value.GetBoolean(), filter.IsLossless); break;
                case "isLossy": Assert.Equal(property.Value.GetBoolean(), filter.IsLossy); break;
                case "include": Assert.Equal(property.Value.EnumerateArray().Select(v => v.GetString()), filter.Include); break;
                case "exclude": Assert.Equal(property.Value.EnumerateArray().Select(v => v.GetString()), filter.Exclude); break;
                default: throw new InvalidOperationException($"The vectors expect an unknown property '{property.Name}'");
            }
        }
    }

    [Fact]
    public void A_Folder_Below_The_Minimum_Loses_Its_Files_And_Keeps_Its_Counts()
    {
        // reproduced from the page rather than corrected: the caller's own "did anything survive" test reads the
        // counts, so a response filtered this way still renders, with nothing in it
        var response = new Response
        {
            Username = "someone",
            FileCount = 2,
            Files = [FileNamed("flac"), FileNamed("mp3_320_cbr")],
            LockedFileCount = 0,
            LockedFiles = [],
        };

        var filtered = SearchFilters.Apply(SearchFilters.Parse("minfilesinfolder:8"), response);

        Assert.Empty(filtered.Files);
        Assert.Equal(2, filtered.FileCount);
    }

    private static string VectorPath()
    {
        // walk up to the repository root; the corpus sits outside both implementations on purpose
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "tests", "fixtures")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "tests", "fixtures", "search-filter-vectors.json");
    }

    private static slskd.Search.File FileNamed(string key)
    {
        var f = Vectors.RootElement.GetProperty("files").GetProperty(key);

        static int? Int(JsonElement e, string name)
            => e.GetProperty(name).ValueKind == JsonValueKind.Null ? null : e.GetProperty(name).GetInt32();

        static bool? Bool(JsonElement e, string name)
            => e.GetProperty(name).ValueKind == JsonValueKind.Null ? null : e.GetProperty(name).GetBoolean();

        return new slskd.Search.File
        {
            Filename = f.GetProperty("filename").GetString(),
            Size = f.GetProperty("size").GetInt64(),
            BitRate = Int(f, "bitRate"),
            BitDepth = Int(f, "bitDepth"),
            SampleRate = Int(f, "sampleRate"),
            Length = Int(f, "length"),
            IsVariableBitRate = Bool(f, "isVariableBitRate"),
        };
    }
}
