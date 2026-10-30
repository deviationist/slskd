// <copyright file="Report.cs" company="JP Dillingham">
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

namespace slskd.Player.Builtin;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
///     One finding, as a person reads it.
/// </summary>
/// <param name="Code">The finding's id.</param>
/// <param name="Label">What the row is called.</param>
/// <param name="Level"><c>ok</c>, <c>warn</c> or <c>bad</c>.</param>
/// <param name="Value">The short answer.</param>
/// <param name="Note">What it means.</param>
public record Finding(string Code, string Label, string Level, string Value, string Note);

/// <summary>
///     Measurements put into words, and into the analysis contract's document.
/// </summary>
/// <remarks>
///     Every row is always present, the clean ones too: "clipping: none" is information, and a panel that lists only
///     problems cannot be told from one that failed to check. The wording says what the measurement supports and no
///     more -- "consistent with a 128 kbps source", never "this is fake".
/// </remarks>
public static partial class Report
{
    /// <summary>The cutoff a lossy encoder leaves at each bitrate, read as "the first this is at or below".</summary>
    private static readonly (double Edge, string Name)[] LossySignatures =
    [
        (17200.0, "a 128 kbps"),
        (18200.0, "a 160 kbps"),
        (19500.0, "a 192 kbps"),
        (20000.0, "a 256 kbps"),
    ];

    private static readonly IReadOnlySet<string> UncompressedCodecs = new HashSet<string>(StringComparer.Ordinal)
    {
        "pcm_s16be", "pcm_s16le", "pcm_s24le", "pcm_s24be", "pcm_s32le",
    };

    /// <summary>A lossy file peaking above this is holding overs, not clipped in the file.</summary>
    private const double OverDbfs = 0.5;

    private static readonly Dictionary<string, int> Severity = new(StringComparer.Ordinal) { ["bad"] = 0, ["warn"] = 1, ["ok"] = 2 };

    /// <summary>
    ///     Every measurement as a finding, worst first.
    /// </summary>
    /// <param name="m">The measurements.</param>
    /// <returns>The findings.</returns>
    public static IReadOnlyList<Finding> Findings(Measurements m)
        => new[] { Bandwidth(m), SampleRate(m), Bitrate(m), Clipping(m), Dropouts(m), Bits(m), Channels(m), Dynamics(m) }
            .OrderBy(f => Severity.GetValueOrDefault(f.Level, 9))
            .ToList();

    /// <summary>
    ///     The badge: a word for what to do about it, not a number to interpret.
    /// </summary>
    /// <param name="m">The measurements.</param>
    /// <param name="findings">The findings, worst first.</param>
    /// <returns>The level and the headline.</returns>
    public static (string Level, string Headline) Verdict(Measurements m, IReadOnlyList<Finding> findings)
    {
        var worst = findings.Count > 0 ? findings[0].Level : "ok";

        if (worst == "ok")
        {
            return ("ok", "clean");
        }

        var lead = findings[0];

        var headline = lead.Code switch
        {
            "bandwidth" => m.CutoffHz is { } cutoff && cutoff != 0 ? $"lossy? {Py.Fixed(cutoff / 1000.0, 1)} kHz" : "lossy?",
            "clipping" => "clipping",
            "bits" => "padded depth",
            "channels" => "not stereo",
            "dynamics" => "compressed",
            "samplerate" => "upsampled",
            "dropouts" => "dropouts",
            "bitrate" => $"{Py.RoundToLong((m.Facts.BitRate ?? 0) / 1000.0)} kbps",
            _ => lead.Code,
        };

        return (worst, headline);
    }

    /// <summary>
    ///     "AIFF · 16-bit · 44.1 kHz · stereo · 1411 kbps · 4:55", skipping what does not apply. For PCM the container
    ///     is the name people use.
    /// </summary>
    /// <param name="f">What the container says.</param>
    /// <returns>The line.</returns>
    public static string Summary(ContainerFacts f)
    {
        var codec = f.Codec ?? string.Empty;

        if (codec.StartsWith("pcm_", StringComparison.Ordinal))
        {
            codec = f.Container ?? "PCM";
        }

        var parts = new[]
        {
            codec.Length > 0 ? codec.ToUpperInvariant() : null,
            f.DeclaredBits is { } bits && bits != 0 ? $"{bits}-bit" : null,
            f.SampleRate != 0 ? $"{Py.Fixed(f.SampleRate / 1000.0, 1)} kHz" : null,
            f.Channels > 1 ? "stereo" : "mono",
            f.BitRate is { } rate && rate != 0 ? $"{Py.RoundToLong(rate / 1000.0)} kbps" : null,
            f.Duration is { } d && d != 0 ? Py.Clock(Math.Max(0, d)) : null,
        };

        return string.Join(" · ", parts.Where(p => p is not null));
    }

    /// <summary>
    ///     A finding's text as plain runs and seek points, so a caller never parses times out of prose.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The runs.</returns>
    public static List<Dictionary<string, object>> Runs(string text)
    {
        var runs = new List<Dictionary<string, object>>();
        text ??= string.Empty;
        var last = 0;

        foreach (Match match in TimePattern().Matches(text))
        {
            if (match.Index > last)
            {
                runs.Add(new() { ["text"] = text[last..match.Index] });
            }

            var seconds = (int.Parse(match.Groups[1].Value) * 60) + int.Parse(match.Groups[2].Value);
            runs.Add(new() { ["text"] = match.Value, ["seek"] = (double)seconds });
            last = match.Index + match.Length;
        }

        if (last < text.Length)
        {
            runs.Add(new() { ["text"] = text[last..] });
        }

        return runs;
    }

    /// <summary>
    ///     The analysis in the contract's shape: see docs/player-analysis-provider.md.
    /// </summary>
    /// <param name="m">The measurements.</param>
    /// <param name="id">The analysis's id: the audio's checksum and the analyser's version.</param>
    /// <returns>The document.</returns>
    public static JsonElement Document(Measurements m, string id)
    {
        var findings = Findings(m);
        var (level, headline) = Verdict(m, findings);

        var document = new Dictionary<string, object>
        {
            ["contract"] = AnalysisProvider.Contract,
            ["id"] = id,
            ["provider"] = new Dictionary<string, object> { ["name"] = "slskd", ["version"] = BuiltinAnalyser.Version },
            ["verdict"] = new Dictionary<string, object> { ["level"] = level, ["headline"] = headline },
            ["summary"] = Summary(m.Facts),
            ["findings"] = findings.Select(f => new Dictionary<string, object>
            {
                ["id"] = f.Code,
                ["label"] = f.Label,
                ["level"] = f.Level,
                ["value"] = Runs(f.Value),
                ["note"] = Runs(f.Note),
            }).ToList(),

            // in the findings' order, worst first, and only for a finding with a lane that has anything in it
            ["lanes"] = findings
                .Where(f => m.Lanes.TryGetValue(f.Code, out var buckets) && buckets.Any(b => b != 0))
                .Select(f => new Dictionary<string, object> { ["finding"] = f.Code, ["buckets"] = m.Lanes[f.Code] })
                .ToList(),
        };

        return JsonSerializer.SerializeToElement(document);
    }

    private static Finding Bandwidth(Measurements m)
    {
        var lossless = Measurer.LosslessCodecs.Contains(m.Facts.Codec ?? string.Empty);

        // checked first, because a whole-file spectrum cannot see it: averaging a lossy half with a clean one gives a
        // picture true of neither, and the file most likely to be spliced is exactly the kind that lands here
        if (m.WalledShare is { } share && m.WalledSpans.Count > 0
            && share >= Measurer.PartialWallRange.Low && share < Measurer.PartialWallRange.High)
        {
            var where = string.Join(", ", m.WalledSpans.Take(3).Select(s => $"{Py.Clock(s.Start)}-{Py.Clock(s.End)}"));

            if (m.WalledSpans.Count > 3)
            {
                where += $" and {m.WalledSpans.Count - 3} more";
            }

            return new("bandwidth", "Bandwidth", "bad", $"lossy in {Py.Percent(share)} of the track",
                $"Part of this track has a lossy wall and part does not ({where}). The file was assembled from more than one "
                + "source -- an edit or a bootleg built on whatever copy was to hand. A whole-file average would have called this "
                + "clean, because the good half hides the bad one.");
        }

        if (m.CutoffHz is not { } cutoff || cutoff == 0)
        {
            return new("bandwidth", "Bandwidth", "ok", "not measurable", "Too short or too quiet to read a spectrum from.");
        }

        var khz = $"{Py.Fixed(cutoff / 1000.0, 1)} kHz";

        if (cutoff >= Measurer.LowpassReportableHz)
        {
            return new("bandwidth", "Bandwidth", "ok", $"full, to {khz}",
                "Treble runs right to the top, as a CD-sourced file does. Note this cannot rule out a 320 kbps source: "
                + "that encoder keeps almost everything, so it looks the same from here.");
        }

        if (m.CliffDb is not { } cliff || cliff == 0 || cliff < Measurer.WallDb)
        {
            return new("bandwidth", "Bandwidth", "ok", "fades out, no wall",
                "The treble fades away gradually instead of stopping dead. That is what a real recording looks like -- a "
                + "lossy source leaves a sharp wall instead. A dark or old mix reads this way too, which is why the "
                + "frequency alone is not the test.");
        }

        var label = LossySignatures.Where(s => cutoff <= s.Edge).Select(s => s.Name).FirstOrDefault() ?? "a lossy";

        return new("bandwidth", "Bandwidth", lossless ? "bad" : "warn", $"wall at {khz}",
            $"Everything above {khz} is gone, and it stops dead ({Py.Fixed(cliff, 0)} dB) rather than fading. That is what "
            + $"{label} encoder leaves behind."
            + (lossless
                ? " This file claims to be lossless, so the audio was most likely lossy before someone re-wrapped it."
                : " Expected for this format -- a lossy file is supposed to look like this."));
    }

    private static Finding SampleRate(Measurements m)
    {
        var rate = m.Facts.SampleRate;

        if (rate == 0)
        {
            return new("samplerate", "Sample rate", "ok", "unknown", "The container did not report one.");
        }

        var khz = $"{Py.General(rate / 1000.0)} kHz";

        if (rate <= Measurer.HighRateHz)
        {
            return new("samplerate", "Sample rate", "ok", khz,
                "An ordinary rate for music. There is no ultrasonic band here for a conversion to have left empty, so "
                + "there is nothing to test.");
        }

        if (m.UltrasonicDb is not { } drop)
        {
            return new("samplerate", "Sample rate", "ok", khz,
                "Not enough of a spectrum to read, so whether the extra rate carries anything is unknown.");
        }

        var edge = $"{Py.General(Measurer.UltrasonicEdgeHz / 1000)} kHz";

        if (drop < Measurer.UltrasonicEmptyDb)
        {
            return new("samplerate", "Sample rate", "ok", $"{khz}, real",
                $"There is content above {edge}, which neither 44.1 nor 48 kHz can hold, so the rate is carrying "
                + "something and the file was captured at it.");
        }

        return new("samplerate", "Sample rate", "warn", $"{khz}, upsampled",
            $"Stored at {khz}, but the band above {edge} is {Py.Fixed(drop, 0)} dB down -- empty. Only a rate above 48 kHz can "
            + "hold anything up there, so this was converted up from 44.1 or 48 kHz. The audio is most likely just as good as "
            + "that source -- upsampling does not damage it -- but the file is larger than it needs to be, and it is not the "
            + "high-resolution master it looks like.");
    }

    private static Finding Clipping(Measurements m)
    {
        if (m.ClippedSamples == 0)
        {
            return new("clipping", "Clipping", "ok", "none", "No samples pinned at maximum.");
        }

        if (m.LongestRun < 3)
        {
            return new("clipping", "Clipping", "ok", $"{m.ClippedSamples} isolated samples",
                "A few single samples touch maximum. That is a loud master reaching the ceiling, not damage -- you will "
                + "not hear it.");
        }

        var worst = Py.Clock(m.WorstAtSeconds);
        var value = $"{m.ClippedRuns} run{(m.ClippedRuns != 1 ? "s" : string.Empty)}, longest {m.LongestRun}";

        if (worst is not null)
        {
            value += $" at {worst}";
        }

        var seek = worst is not null ? $" The worst of it is at {worst}; play from there to hear it." : string.Empty;
        var level = m.LongestRun >= 10 ? "bad" : "warn";

        // a lossy file peaking above full scale was not clipped when it was made: the overs are in the file, and
        // are cut off only at playback. turning it down brings them back, losing nothing
        var lossy = !Measurer.LosslessCodecs.Contains(m.Facts.Codec ?? string.Empty);

        if (lossy && m.PeakDbfs > OverDbfs)
        {
            var down = Math.Ceiling(m.PeakDbfs * 10) / 10;

            return new("clipping", "Clipping", level, value,
                $"Peaks reach +{Py.Fixed(m.PeakDbfs, 1)} dBFS, above what any player can output, so the tops are flattened as it "
                + "plays. They are not flattened in the file: it holds the whole waveform, and turning it down by "
                + $"{Py.Fixed(down, 1)} dB plays it clean with nothing lost (for an MP3, MP3Gain's undo or a gain change does it "
                + "without re-encoding)." + seek);
        }

        var where = string.Empty;

        if (m.FirstAtSeconds is { } first && m.LastAtSeconds is { } last && m.AnalysedSeconds != 0)
        {
            where = last - first < m.AnalysedSeconds * 0.25
                ? $" All of it falls between {Py.Clock(first)} and {Py.Clock(last)} rather than across the track, which points "
                    + "at one damaged passage rather than the master -- another copy of this release may well be clean."
                : $" It recurs from {Py.Clock(first)} to {Py.Clock(last)}, across the whole track, which points at the level the "
                    + "track was mastered or ripped at rather than one damaged passage. If that was the label's master, every copy "
                    + "has it; if it was gain added before sharing, a copy from another source may be clean.";
        }

        return new("clipping", "Clipping", level, value,
            "Consecutive samples stuck at maximum, so the tops of the waveform have been flattened. Long runs are audible as "
            + "distortion on the loudest parts; this cannot be undone." + seek + where);
    }

    private static Finding Bitrate(Measurements m)
    {
        var codec = m.Facts.Codec ?? string.Empty;

        if (m.Facts.BitRate is not { } rate || rate == 0)
        {
            return new("bitrate", "Bitrate", "ok", "unknown", "The container did not report one.");
        }

        var kbps = Py.RoundToLong(rate / 1000.0);

        if (UncompressedCodecs.Contains(codec))
        {
            return new("bitrate", "Bitrate", "ok", $"{kbps} kbps",
                "Uncompressed, so this is just sample rate x bit depth x channels. Every file of this shape has the same "
                + "number and it says nothing about quality.");
        }

        if (Measurer.LosslessCodecs.Contains(codec))
        {
            var pcm = (double)(m.Facts.SampleRate != 0 ? m.Facts.SampleRate : 44100)
                * (m.Facts.DeclaredBits is { } b && b != 0 ? b : 16)
                * (m.Facts.Channels != 0 ? m.Facts.Channels : 2);
            var ratio = pcm != 0 ? rate / pcm : (double?)null;

            // measured, and recorded so it does not get "improved" into a rule: real 16-bit FLACs came in at 66-71% of
            // raw, and the same tracks upscaled from 128/192/320 kbps MP3 at 62/64/65% -- the ranges overlap
            return new("bitrate", "Bitrate", "ok", $"{kbps} kbps" + (ratio is { } r && r != 0 ? $" ({Py.Percent(r)} of raw)" : string.Empty),
                "How well this music compressed, which depends mostly on the music. Measured here, a low figure does not reveal a "
                + "lossy source -- genuine files land around 66-71% of raw and upscales around 62-65%, which overlaps. The bandwidth "
                + "row is the test that separates them.");
        }

        var (level, tail) = kbps switch
        {
            >= 256 => ("ok", "About as good as a lossy file gets."),
            >= 192 => ("ok", "Decent. Fine to listen to; a lossless copy would still be better."),
            >= 128 => ("warn", "Modest. Audible on good speakers, and it cannot be improved."),
            _ => ("bad", "Low. Expect audible artefacts."),
        };

        return new("bitrate", "Bitrate", level, $"{kbps} kbps",
            $"This is what the encoder was set to, and it is the number that decided how much was discarded. {tail}");
    }

    private static Finding Dropouts(Measurements m)
    {
        if (m.Dropouts == 0)
        {
            return new("dropouts", "Dropouts", "ok", "none", "No stretch of the track goes silent unexpectedly.");
        }

        var at = Py.Clock(m.DropoutAtSeconds);

        return new("dropouts", "Dropouts", m.LongestDropoutSeconds >= 0.15 || m.Dropouts >= 3 ? "bad" : "warn",
            $"{m.Dropouts} gap{(m.Dropouts != 1 ? "s" : string.Empty)}, longest {Py.Fixed(m.LongestDropoutSeconds, 2)}s at {at}",
            "The audio goes digitally silent mid-track and comes back. That is a hole in the file -- a failed transfer or a bad rip "
            + $"-- and no tag or re-encode repairs it; another copy is the only fix. Play from {at} to hear it. A deliberate pause in "
            + "the arrangement can look the same, so listen before discarding the file.");
    }

    private static Finding Bits(Measurements m)
    {
        if (m.Facts.DeclaredBits is not { } declared || declared == 0)
        {
            return new("bits", "Bit depth", "ok", "n/a", "A lossy format has no bit depth to declare.");
        }

        if (m.MeasuredBits is { } real && real != 0 && real < declared)
        {
            return new("bits", "Bit depth", "warn", $"{real} used of {declared} declared",
                $"The file says {declared}-bit but only {real} bits ever carry anything -- the rest is padding. It was converted up "
                + $"from {real}-bit; the extra size buys nothing.");
        }

        return new("bits", "Bit depth", "ok", $"{(m.MeasuredBits is { } r && r != 0 ? r : declared)}-bit",
            "Declared depth and the samples agree.");
    }

    private static Finding Channels(Measurements m)
    {
        var stereo = (m.Facts.Channels != 0 ? m.Facts.Channels : 2) > 1;

        if (m.MonoShare > 0.02 && m.MonoShare < 0.9995 && stereo)
        {
            return new("channels", "Channels", "warn", $"mono for {Py.Percent(m.MonoShare)} of the track",
                "The two channels are identical for part of the track and differ for the rest. Usually an edit stitched from a mono "
                + "source, or a section that lost a channel in transfer -- either way it is not how the track was mixed, and it is "
                + "audible as the image collapsing.");
        }

        if (m.DualMono)
        {
            return new("channels", "Channels", "warn", "mono in a stereo file",
                "Both channels are byte-identical. It plays fine, but it is not a stereo mix and it is twice the size it needs to be.");
        }

        return new("channels", "Channels", "ok", stereo ? "stereo" : "mono", "Channels differ, as a stereo mix should.");
    }

    private static Finding Dynamics(Measurements m)
        => new("dynamics", "Dynamics", m.CrestDb < 8 ? "warn" : "ok", $"{Py.Fixed(m.CrestDb, 1)} dB crest",
            "The gap between the loudest peak and the average level. Under about 8 dB means heavily compressed -- loud throughout, "
            + "with the quiet parts squeezed out. Above roughly 12 dB is comfortable. This is a mastering choice, not a defect, "
            + "and club-oriented music is often deliberately low.");

    // m:ss anywhere in a finding: minutes at most three digits and seconds a real sexagesimal pair, so "1:2" is not a time
    [GeneratedRegex(@"\b(\d{1,3}):([0-5]\d)\b")]
    private static partial Regex TimePattern();
}
