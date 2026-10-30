// <copyright file="CompareDocument.cs" company="JP Dillingham">
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
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using slskd.Player.Builtin;

/// <summary>
///     A file in a comparison: its features, or nothing where it could not be read.
/// </summary>
/// <param name="Features">The features, or null.</param>
public record ComparedFile(Features Features)
{
    public bool Readable => Features is not null;
}

/// <summary>
///     A scored pair, by index into the files.
/// </summary>
/// <param name="A">The first file.</param>
/// <param name="B">The second.</param>
/// <param name="Match">How they matched.</param>
public record ComparedPair(int A, int B, PairMatch Match);

/// <summary>
///     A comparison in the contract's shape (docs/transfers-compare-provider.md), with the words made here, from the
///     numbers, by the side that knows what they mean.
/// </summary>
public static class CompareDocument
{
    /// <summary>What a file that could not be read is called, whatever the reason.</summary>
    public const string Unreadable = "could not be read";

    /// <summary>A match this close is the same recording beyond reasonable doubt: copies of one file measured
    /// 0.962-1.000.</summary>
    public const double CloseMatch = 0.95;

    /// <summary>
    ///     The document.
    /// </summary>
    /// <param name="files">The files, in the caller's order.</param>
    /// <param name="pairs">Every pair of readable files, scored.</param>
    /// <param name="groups">The groups of files that are one track, biggest first; a lone file is a group of one.</param>
    /// <returns>The document.</returns>
    public static JsonElement Build(IReadOnlyList<ComparedFile> files, IReadOnlyList<ComparedPair> pairs, IReadOnlyList<List<int>> groups)
    {
        var together = groups.Where(g => g.Count > 1).ToList();
        var (level, headline, detail) = Verdict(files, pairs, groups);

        var document = new Dictionary<string, object>
        {
            ["contract"] = CompareProvider.Contract,
            ["provider"] = new Dictionary<string, object> { ["name"] = "slskd", ["version"] = BuiltinCompare.Version },
            ["verdict"] = new Dictionary<string, object> { ["level"] = level, ["headline"] = headline, ["detail"] = detail },
            ["files"] = files.Select(f => f.Readable
                ? new Dictionary<string, object>
                {
                    ["readable"] = true,
                    ["music_s"] = f.Features.MusicSeconds,
                    ["lead_s"] = f.Features.LeadSeconds,
                    ["tail_s"] = f.Features.TailSeconds,
                }
                : new Dictionary<string, object> { ["readable"] = false, ["note"] = Unreadable }).ToList(),
            ["groups"] = together.Select(g => new Dictionary<string, object>
            {
                ["files"] = g,
                ["notes"] = g.Select((i, k) => k == 0
                    ? "the others are compared with this one"
                    : Differences(files[g[0]].Features, files[i].Features, PairOf(pairs, g[0], i))).ToList(),
            }).ToList(),
            ["alone"] = groups.Where(g => g.Count == 1).Select(g => g[0]).ToList(),
            ["partly"] = pairs.Where(p => p.Match.Verdict == "partly").Select(p => new Dictionary<string, object>
            {
                ["a"] = p.A,
                ["b"] = p.B,
                ["note"] = $"share {Percent(p.Match.Share)} of their length and then part ways — an edit, an extended mix, or a mashup",
            }).ToList(),
            ["pairs"] = pairs.Select(p => new Dictionary<string, object>
            {
                ["a"] = p.A,
                ["b"] = p.B,
                ["verdict"] = p.Match.Verdict,
                ["match"] = p.Match.Score,
                ["share"] = p.Match.Share,
                ["offset_s"] = p.Match.OffsetSeconds,
                ["drift_ms"] = p.Match.DriftMs,
                ["gain_db"] = p.Match.GainDb,
            }).ToList(),
        };

        return JsonSerializer.SerializeToElement(document);
    }

    /// <summary>
    ///     What sets copy <paramref name="copy"/> apart from <paramref name="reference"/>, the first of its group: only
    ///     the differences worth acting on, since a fraction of a second of lead-in or half a dB is the encoder.
    /// </summary>
    /// <param name="reference">The group's first file.</param>
    /// <param name="copy">The copy.</param>
    /// <param name="pair">Their pair.</param>
    /// <returns>The words.</returns>
    public static string Differences(Features reference, Features copy, ComparedPair pair)
    {
        if (pair is null)
        {
            return string.Empty;
        }

        var m = pair.Match;
        var parts = new List<string> { $"{Percent(m.Score)} match" };
        var longer = (copy?.MusicSeconds ?? 0) - (reference?.MusicSeconds ?? 0);

        if (Math.Abs(longer) >= 0.5)
        {
            parts.Add($"{Py.Fixed(Math.Abs(longer), 1)} s {(longer > 0 ? "longer" : "shorter")}");
        }

        if (Math.Abs(m.OffsetSeconds) >= 0.3)
        {
            parts.Add($"starts {Py.Fixed(Math.Abs(m.OffsetSeconds), 1)} s {(m.OffsetSeconds > 0 ? "later" : "earlier")}");
        }

        if (Math.Abs(m.GainDb) >= 0.5)
        {
            parts.Add($"{Py.Fixed(Math.Abs(m.GainDb), 1)} dB {(m.GainDb > 0 ? "louder" : "quieter")}");
        }

        if (m.DriftMs != 0)
        {
            parts.Add($"drifts {Math.Abs(m.DriftMs)} ms over the track — a speed difference, like a vinyl rip");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    ///     The one-line answer, and how sure it is: <c>same</c>, <c>likely</c>, <c>mixed</c>, <c>partly</c>,
    ///     <c>different</c> or <c>unknown</c>. Two files get a yes or a no, since that is the question; more get a count.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <param name="pairs">The pairs.</param>
    /// <param name="groups">The groups.</param>
    /// <returns>The level, headline and detail.</returns>
    public static (string Level, string Headline, string Detail) Verdict(IReadOnlyList<ComparedFile> files, IReadOnlyList<ComparedPair> pairs, IReadOnlyList<List<int>> groups)
    {
        var ok = files.Count(f => f.Readable);

        if (ok < 2)
        {
            return ("unknown", "Could not compare", "Fewer than two of these files could be read.");
        }

        if (ok == 2 && pairs.Count == 1)
        {
            var p = pairs[0];
            var pct = $"{Percent(p.Match.Score)} match";

            if (p.Match.Verdict == "same")
            {
                var rest = Differences(files[p.A].Features, files[p.B].Features, p).Split(" · ").Skip(1).ToList();
                var close = p.Match.Score >= CloseMatch;
                var detail = pct + (rest.Count > 0 ? " · " + string.Join(" · ", rest) : " · no difference worth noting");

                if (!close)
                {
                    detail += ". A looser match than copies of one file usually give — a different master or a processed rip.";
                }

                return (close ? "same" : "likely", close ? "Yes — the same track" : "Yes — very likely the same track", detail);
            }

            if (p.Match.Verdict == "partly")
            {
                return ("partly", "Partly — they share long stretches",
                    $"{Percent(p.Match.Share)} of their length matches, then they part ways — an edit, an extended mix, or a mashup.");
            }

            return ("different", "No — different tracks", $"{pct}. Whatever the names say, the music does not line up.");
        }

        var together = groups.Where(g => g.Count > 1).ToList();
        var biggest = together.Count > 0 ? together.Max(g => g.Count) : 0;
        var loose = pairs.Any(p => p.Match.Verdict == "same" && p.Match.Score < CloseMatch);

        if (together.Count == 1 && biggest == ok)
        {
            return (loose ? "likely" : "same", $"All {ok} are the same track",
                loose ? "Some copies match more loosely than others — see below." : "Every copy lines up with every other.");
        }

        if (together.Count > 0)
        {
            return ("mixed",
                together.Count == 1 ? $"{biggest} of {ok} are the same track" : $"{together.Count} different tracks among {ok} files",
                "Grouped below; the rest match nothing else here.");
        }

        if (pairs.Any(p => p.Match.Verdict == "partly"))
        {
            return ("partly", "None are the same, some overlap", "Some share long stretches and then part ways — see below.");
        }

        return ("different", "No two of these are the same track", "Whatever the names say, none of them line up.");
    }

    // half up, as the words were first written in JavaScript, rather than to even
    private static string Percent(double share) => $"{(int)((share * 100) + 0.5)}%";

    private static ComparedPair PairOf(IReadOnlyList<ComparedPair> pairs, int a, int b)
        => pairs.FirstOrDefault(p => (p.A == a && p.B == b) || (p.A == b && p.B == a));
}
