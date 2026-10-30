// <copyright file="Measurer.cs" company="JP Dillingham">
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
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
///     What a file's container says about it, read without decoding.
/// </summary>
/// <param name="Codec">The codec, as ffprobe names it.</param>
/// <param name="Container">The container format.</param>
/// <param name="SampleRate">The sample rate.</param>
/// <param name="Channels">The channel count.</param>
/// <param name="DeclaredBits">The bit depth the container declares, if any.</param>
/// <param name="BitRate">The audio's bitrate, in bits a second, if known.</param>
/// <param name="Duration">The length the container claims, in seconds.</param>
public record ContainerFacts(string Codec, string Container, int SampleRate, int Channels, int? DeclaredBits, long? BitRate, double? Duration);

/// <summary>
///     A span of the track, in seconds.
/// </summary>
/// <param name="Start">Where it starts.</param>
/// <param name="End">Where it ends.</param>
public record struct Span(double Start, double End);

/// <summary>
///     Everything measured of one file, before any of it is put into words.
/// </summary>
public record Measurements
{
    public ContainerFacts Facts { get; init; }
    public double AnalysedSeconds { get; init; }
    public long? CutoffHz { get; init; }
    public double? CliffDb { get; init; }
    public double? UltrasonicDb { get; init; }
    public int? MeasuredBits { get; init; }
    public double PeakDbfs { get; init; }
    public double RmsDbfs { get; init; }
    public double CrestDb { get; init; }
    public bool DualMono { get; init; }
    public double MonoShare { get; init; }
    public IReadOnlyList<Span> WalledSpans { get; init; } = [];
    public double? WalledShare { get; init; }
    public long ClippedSamples { get; init; }
    public long ClippedRuns { get; init; }
    public long LongestRun { get; init; }
    public double? WorstAtSeconds { get; init; }
    public double? FirstAtSeconds { get; init; }
    public double? LastAtSeconds { get; init; }
    public int Dropouts { get; init; }
    public double LongestDropoutSeconds { get; init; }
    public double? DropoutAtSeconds { get; init; }
    public IReadOnlyList<Span> DropoutSpans { get; init; } = [];

    /// <summary>
    ///     Gets the timeline: one lane per problem that is somewhere in the track rather than everywhere, by finding.
    /// </summary>
    public IReadOnlyDictionary<string, int[]> Lanes { get; init; } = new Dictionary<string, int[]>();
}

/// <summary>
///     Measures a file from its samples as they are decoded, one frame at a time.
/// </summary>
/// <remarks>
///     <para>
///         Ported from media-bridge's analyser (its <c>quality.py</c>, schema 6), check for check and threshold for
///         threshold, and kept independently from here on: a change to either is that side's own, and is versioned by
///         <see cref="BuiltinAnalyser.Version"/>.
///     </para>
///     <para>
///         Streamed rather than held: nothing here keeps the samples, only what each check needs from them -- running
///         spectra, peaks, the runs of clipped and silent frames, and one bit a frame for where the channels agree. A
///         decoded hour of 96 kHz stereo is over two gigabytes; this holds tens of megabytes of it at most.
///     </para>
/// </remarks>
public sealed class Measurer
{
    /// <summary>The FFT window: a 5.4 Hz bin at 44.1 kHz, and long enough to cover a whole musical event.</summary>
    public const int Window = 8192;

    /// <summary>How far above its own noise floor a bin must be to count as content.</summary>
    public const double FloorMarginDb = 25.0;

    /// <summary>Below this the file has a lowpass worth reporting; above it the test has nothing to say.</summary>
    public const double LowpassReportableHz = 20000.0;

    /// <summary>How far the spectrum must fall across the cutoff to count as a wall rather than a fade.</summary>
    public const double WallDb = 25.0;

    /// <summary>Above this the sample rate is a claim worth testing.</summary>
    public const int HighRateHz = 48000;

    /// <summary>The ceiling 48 kHz imposes.</summary>
    public const double UltrasonicEdgeHz = 24000.0;

    /// <summary>Distance above the edge before the measuring band starts: a resampler's filter skirt.</summary>
    public const double UltrasonicGapHz = 4000.0;

    /// <summary>Width of each measuring band.</summary>
    public const double UltrasonicWidthHz = 4000.0;

    /// <summary>How far the spectrum must fall across the gap for the band to count as empty.</summary>
    public const double UltrasonicEmptyDb = 40.0;

    /// <summary>The share of segments a wall must cover to be a finding about part of the track, and below which a
    /// wall throughout is reported in words instead.</summary>
    public static readonly (double Low, double High) PartialWallRange = (0.20, 0.95);

    /// <summary>Lane resolution: the waveform's own grid, so a mark sits under what it is about.</summary>
    public const int LaneBuckets = 1600;

    /// <summary>How much audio each spectral segment covers.</summary>
    public const double SegmentSeconds = 5.0;

    /// <summary>Below this a sample is digitally silent, 80 dB down.</summary>
    public const double DropoutFloor = 1e-4;

    /// <summary>A silence must last this long to be audible as a hole...</summary>
    public const double DropoutMinSeconds = 0.04;

    /// <summary>...and no longer than this to still read as one rather than the arrangement.</summary>
    public const double DropoutMaxSeconds = 1.5;

    /// <summary>Silence this close to either end is how tracks are cut.</summary>
    public const double DropoutEdgeSeconds = 1.0;

    /// <summary>Where the music is: from the first frame this loud to the last, -50 dBFS.</summary>
    public static readonly double MusicFloor = Math.Pow(10, -50 / 20.0);

    /// <summary>A sample this close to full scale is pinned.</summary>
    public const double ClipThreshold = 32700;

    /// <summary>Containers that promise the audio never went through a lossy encoder.</summary>
    public static readonly IReadOnlySet<string> LosslessCodecs = new HashSet<string>(StringComparer.Ordinal)
    {
        "flac", "alac", "pcm_s16be", "pcm_s16le", "pcm_s24le", "pcm_s24be", "pcm_s32le",
    };

    private static readonly float Pinned = (float)(ClipThreshold / 32768.0);

    /// <summary>
    ///     Initializes a new instance of the <see cref="Measurer"/> class.
    /// </summary>
    /// <param name="facts">What the container says.</param>
    /// <param name="channels">The channels decoded: one or two.</param>
    /// <param name="full">Full scale of the decoded integers, for the bit depth; 1 for float decodes.</param>
    public Measurer(ContainerFacts facts, int channels, double full)
    {
        Facts = facts;
        Channels = channels;
        Full = full;
        Rate = facts.SampleRate;
        Fft = new Fft(Window);
        PerSegment = Math.Max(4, (int)Math.Round(SegmentSeconds * Rate / Window, MidpointRounding.ToEven));
        SegmentPower = new double[Fft.Bins];
        TotalPower = new double[Fft.Bins];
    }

    private ContainerFacts Facts { get; }
    private int Channels { get; }
    private double Full { get; }
    private int Rate { get; }
    private Fft Fft { get; }
    private int PerSegment { get; }

    private float[] Buffer { get; } = new float[Window];
    private int Filled { get; set; }
    private double[] SegmentPower { get; }
    private int SegmentWindows { get; set; }
    private long SegmentFirstWindow { get; set; }
    private double[] TotalPower { get; }
    private long TotalWindows { get; set; }
    private List<(double Start, double End, double[] Db)> Segments { get; } = [];

    private long Frames { get; set; }
    private float Peak { get; set; }
    private double SquareSum { get; set; }
    private BitArray Same { get; set; } = new BitArray(1 << 20);
    private long SameCount { get; set; }

    private long PinnedSamples { get; set; }
    private long ClipStart { get; set; } = -1;
    private List<(long Start, long End)> ClipRuns { get; } = [];

    private long QuietStart { get; set; } = -1;
    private List<(long Start, long End)> QuietRuns { get; } = [];
    private long FirstLoud { get; set; } = -1;
    private long LastLoud { get; set; } = -1;

    private long BitsSeen { get; set; }

    /// <summary>
    ///     Adds one frame: a sample per channel, in -1..1 (float decodes may go past it), and the integers they were
    ///     decoded from when the decode was integer.
    /// </summary>
    /// <param name="left">The first channel.</param>
    /// <param name="right">The second channel; the same as the first for mono.</param>
    /// <param name="leftInt">The first channel's integer, or 0 for a float decode.</param>
    /// <param name="rightInt">The second channel's integer.</param>
    public void Add(float left, float right, long leftInt = 0, long rightInt = 0)
    {
        var n = Frames;

        // the spectrum is of the mix, as numpy's mean over the channels makes it
        var mono = Channels > 1 ? (left + right) * 0.5f : left;
        Buffer[Filled++] = mono;

        if (Filled == Window)
        {
            Fft.AddPower(Buffer, SegmentPower);
            Filled = 0;
            SegmentWindows++;

            if (SegmentWindows == PerSegment)
            {
                CloseSegment();
            }
        }

        var absL = Math.Abs(left);
        var absR = Channels > 1 ? Math.Abs(right) : absL;
        var framePeak = Math.Max(absL, absR);

        if (framePeak > Peak)
        {
            Peak = framePeak;
        }

        SquareSum += (double)mono * mono;

        var same = Channels <= 1 || left == right;

        if (n >= Same.Length)
        {
            Same.Length *= 2;
        }

        if (same)
        {
            Same[(int)n] = true;
            SameCount++;
        }

        // pinned samples, counted one by one, and their runs, counted by frame
        var pinnedL = absL >= Pinned;
        var pinnedR = Channels > 1 && absR >= Pinned;
        PinnedSamples += (pinnedL ? 1 : 0) + (pinnedR ? 1 : 0);

        if (pinnedL || pinnedR)
        {
            if (ClipStart < 0)
            {
                ClipStart = n;
            }
        }
        else if (ClipStart >= 0)
        {
            ClipRuns.Add((ClipStart, n));
            ClipStart = -1;
        }

        // silence, kept only for the runs that could be a hole: shorter is inaudible, longer is the arrangement
        if (framePeak < DropoutFloor)
        {
            if (QuietStart < 0)
            {
                QuietStart = n;
            }
        }
        else if (QuietStart >= 0)
        {
            KeepQuiet(QuietStart, n);
            QuietStart = -1;
        }

        if (framePeak >= MusicFloor)
        {
            if (FirstLoud < 0)
            {
                FirstLoud = n;
            }

            LastLoud = n;
        }

        BitsSeen |= Math.Abs(leftInt) | (Channels > 1 ? Math.Abs(rightInt) : 0);

        Frames = n + 1;
    }

    /// <summary>
    ///     Everything measured, once every frame is in.
    /// </summary>
    /// <returns>The measurements.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no audio was added.</exception>
    public Measurements Finish()
    {
        if (Frames == 0)
        {
            throw new InvalidOperationException("decoded to no audio at all");
        }

        if (ClipStart >= 0)
        {
            ClipRuns.Add((ClipStart, Frames));
            ClipStart = -1;
        }

        if (QuietStart >= 0)
        {
            KeepQuiet(QuietStart, Frames);
            QuietStart = -1;
        }

        if (SegmentWindows > 0)
        {
            CloseSegment();
        }

        var played = Frames / (double)Rate;
        var freq = Enumerable.Range(0, Fft.Bins).Select(k => k * (double)Rate / Window).ToArray();
        double[] db = null;

        if (TotalWindows >= 4)
        {
            db = Normalised(TotalPower.Select(p => 10.0 * Math.Log10((p / TotalWindows) + 1e-20)).ToArray());
        }

        var segments = TotalWindows >= 4 ? Segments : [];
        var (cutoff, cliff) = Cutoff(db, freq);

        var monoRms = Math.Sqrt(SquareSum / Frames);
        var peakDb = ToDb(Peak);
        var rmsDb = ToDb(monoRms);
        var monoShare = SameCount / (double)Frames;

        var walled = segments
            .Where(s =>
            {
                var (c, k) = Cutoff(s.Db, freq);
                return c is not null && c < LowpassReportableHz && k is not null && k >= WallDb;
            })
            .Select(s => new Span(s.Start, s.End))
            .ToList();

        var clip = Clipping();
        var drops = Holes(played);

        return clip with
        {
            Facts = Facts,
            AnalysedSeconds = Py.Round(played, 2),
            CutoffHz = cutoff is null ? null : (long)Math.Round(cutoff.Value, MidpointRounding.ToEven),
            CliffDb = cliff is null ? null : Py.Round(cliff.Value, 1),
            UltrasonicDb = Ultrasonic(db, freq),
            MeasuredBits = RealBits(),
            PeakDbfs = peakDb,
            RmsDbfs = rmsDb,
            CrestDb = Py.Round(peakDb - rmsDb, 1),
            DualMono = Channels > 1 && monoShare >= 0.9995,
            MonoShare = Py.Round(monoShare, 4),
            WalledSpans = walled.Select(w => new Span(Py.Round(w.Start, 2), Py.Round(w.End, 2))).ToList(),
            WalledShare = segments.Count > 0 ? Py.Round(walled.Count / (double)segments.Count, 4) : null,
            Dropouts = drops.Count,
            LongestDropoutSeconds = drops.Count > 0 ? Py.Round(drops.Max(d => d.End - d.Start), 2) : 0.0,
            DropoutAtSeconds = drops.Count > 0 ? drops.MaxBy(d => d.End - d.Start).Start : null,
            DropoutSpans = drops,
            Lanes = Lanes(played, monoShare, walled, segments.Count, drops),
        };
    }

    /// <summary>
    ///     The highest frequency carrying content, and how far the spectrum falls across it.
    /// </summary>
    /// <param name="db">A spectrum normalised to its peak, or null.</param>
    /// <param name="freq">Each bin's frequency.</param>
    /// <returns>The cutoff and the cliff, either null where it cannot be read.</returns>
    public static (double? Cutoff, double? Cliff) Cutoff(double[] db, double[] freq)
    {
        if (db is null)
        {
            return (null, null);
        }

        var top = freq[^1];

        // the floor, from the top 5 % of the spectrum: every lossy cutoff sits below it, and for a CD it is the
        // anti-alias stopband -- the file's own silence either way
        var floor = Median(db.Where((_, i) => freq[i] >= top * 0.95));
        var last = -1;

        for (var i = 0; i < db.Length; i++)
        {
            if (db[i] > floor + FloorMarginDb)
            {
                last = i;
            }
        }

        // nothing above the very top: the content runs all the way there, full bandwidth and no cliff
        if (last < 0)
        {
            return (top, null);
        }

        var cutoff = freq[last];
        var below = Mean(db, freq, f => f >= cutoff - 2000 && f < cutoff - 500);
        var above = Mean(db, freq, f => f > cutoff + 500 && f <= cutoff + 2000);

        return below is null || above is null ? (cutoff, null) : (cutoff, below - above);
    }

    private void KeepQuiet(long start, long end)
    {
        var seconds = (end - start) / (double)Rate;

        if (seconds >= DropoutMinSeconds && seconds <= DropoutMaxSeconds)
        {
            QuietRuns.Add((start, end));
        }
    }

    private void CloseSegment()
    {
        for (var k = 0; k < TotalPower.Length; k++)
        {
            TotalPower[k] += SegmentPower[k];
        }

        TotalWindows += SegmentWindows;

        // a stub of a segment has too few windows to average; it still counts toward the whole-file mean
        if (SegmentWindows >= 4)
        {
            var db = Normalised(SegmentPower.Select(p => 10.0 * Math.Log10((p / SegmentWindows) + 1e-20)).ToArray());
            Segments.Add((SegmentFirstWindow * Window / (double)Rate, (SegmentFirstWindow + SegmentWindows) * Window / (double)Rate, db));
        }

        SegmentFirstWindow += SegmentWindows;
        SegmentWindows = 0;
        Array.Clear(SegmentPower);
    }

    private Measurements Clipping()
    {
        if (PinnedSamples == 0 || ClipRuns.Count == 0)
        {
            return new Measurements();
        }

        var worst = ClipRuns.MaxBy(r => r.End - r.Start);

        return new Measurements
        {
            ClippedSamples = PinnedSamples,
            ClippedRuns = ClipRuns.Count,
            LongestRun = worst.End - worst.Start,
            WorstAtSeconds = Py.Round(worst.Start / (double)Rate, 2),
            FirstAtSeconds = Py.Round(ClipRuns[0].Start / (double)Rate, 2),
            LastAtSeconds = Py.Round(ClipRuns[^1].End / (double)Rate, 2),
        };
    }

    private List<Span> Holes(double total)
    {
        // a file of silence and noise has no music to have a hole in
        if (FirstLoud < 0)
        {
            return [];
        }

        var musicFrom = FirstLoud / (double)Rate;
        var musicTo = (LastLoud + 1) / (double)Rate;

        return QuietRuns
            .Select(r => (A: r.Start / (double)Rate, B: r.End / (double)Rate))
            .Where(r => r.A >= DropoutEdgeSeconds && r.B <= total - DropoutEdgeSeconds)
            .Where(r => r.A >= musicFrom && r.B <= musicTo)
            .Select(r => new Span(Py.Round(r.A, 2), Py.Round(r.B, 2)))
            .ToList();
    }

    private int? RealBits()
    {
        if (Facts.DeclaredBits is not { } declared || declared == 0)
        {
            return null;
        }

        if (BitsSeen == 0)
        {
            return 0;
        }

        // the lowest bit ever set across the file; everything below it is padding, whatever the header says.
        // +1 for the sign bit: log2 of full scale counts the magnitude bits only
        var shift = System.Numerics.BitOperations.TrailingZeroCount(BitsSeen);
        return Math.Max(1, (int)Math.Round(Math.Log2(Full), MidpointRounding.ToEven) + 1 - shift);
    }

    private double? Ultrasonic(double[] db, double[] freq)
    {
        if (db is null)
        {
            return null;
        }

        var hiA = UltrasonicEdgeHz + UltrasonicGapHz;
        var hiB = hiA + UltrasonicWidthHz;

        if (hiB > Rate / 2.0 * 0.95)
        {
            return null;
        }

        var below = Mean(db, freq, f => f >= UltrasonicEdgeHz - UltrasonicWidthHz && f < UltrasonicEdgeHz);
        var above = Mean(db, freq, f => f >= hiA && f <= hiB);

        return below is null || above is null ? null : Py.Round(below.Value - above.Value, 1);
    }

    private Dictionary<string, int[]> Lanes(double played, double monoShare, List<Span> walled, int segments, List<Span> drops)
    {
        var lanes = new Dictionary<string, int[]>(StringComparer.Ordinal);

        // only the runs the clipping row calls damage: three samples or more
        var audible = ClipRuns.Where(r => r.End - r.Start >= 3).ToList();

        if (audible.Count > 0)
        {
            lanes["clipping"] = Scaled(Counts(audible));
        }

        if (drops.Count > 0)
        {
            lanes["dropouts"] = Spread(drops, played);
        }

        // partial only: a file lossy or mono from end to end is reported in words, not painted flat
        var share = segments > 0 ? walled.Count / (double)segments : 0.0;

        if (walled.Count > 0 && share >= PartialWallRange.Low && share < PartialWallRange.High)
        {
            lanes["bandwidth"] = Spread(walled, played);
        }

        if (monoShare > 0.02 && monoShare < 0.9995)
        {
            lanes["channels"] = Scaled(SameCounts());
        }

        return lanes;
    }

    private long Edge(int bucket) => bucket * Frames / LaneBuckets;

    private double[] Counts(IEnumerable<(long Start, long End)> runs)
    {
        var counts = new double[LaneBuckets];

        foreach (var (start, end) in runs)
        {
            for (var b = (int)(start * LaneBuckets / Frames); b < LaneBuckets && Edge(b) < end; b++)
            {
                var lo = Math.Max(start, Edge(b));
                var hi = Math.Min(end, Edge(b + 1));

                if (hi > lo)
                {
                    counts[b] += hi - lo;
                }
            }
        }

        return counts;
    }

    private double[] SameCounts()
    {
        var counts = new double[LaneBuckets];

        for (var b = 0; b < LaneBuckets; b++)
        {
            for (var n = Edge(b); n < Edge(b + 1); n++)
            {
                if (Same[(int)n])
                {
                    counts[b]++;
                }
            }
        }

        return counts;
    }

    // scaled against the busiest bucket, and anything at all draws at least faintly: a share of each bucket's width
    // rounds a handful of clipped samples to nothing, and the lane came out blank on a file the row called bad
    private static int[] Scaled(double[] counts)
    {
        var top = counts.Max();

        if (top <= 0)
        {
            return new int[counts.Length];
        }

        return counts.Select(c => c > 0 ? (int)Math.Round(Math.Max(1.0, c / top * 100.0), MidpointRounding.ToEven) : 0).ToArray();
    }

    private static int[] Spread(IEnumerable<Span> spans, double seconds)
    {
        var lane = new int[LaneBuckets];

        if (seconds <= 0)
        {
            return lane;
        }

        foreach (var span in spans)
        {
            var lo = Math.Max(0, Math.Min(LaneBuckets - 1, (int)(span.Start / seconds * LaneBuckets)));
            var hi = Math.Max(lo + 1, Math.Min(LaneBuckets, (int)Math.Round(span.End / seconds * LaneBuckets, MidpointRounding.ToEven)));

            for (var i = lo; i < hi; i++)
            {
                lane[i] = 100;
            }
        }

        return lane;
    }

    private static double ToDb(double value) => Py.Round(20.0 * Math.Log10(Math.Max(value, 1e-9)), 2);

    private static double[] Normalised(double[] db)
    {
        var max = db.Max();
        return db.Select(d => d - max).ToArray();
    }

    private static double? Mean(double[] db, double[] freq, Func<double, bool> within)
    {
        double sum = 0;
        var count = 0;

        for (var i = 0; i < db.Length; i++)
        {
            if (within(freq[i]))
            {
                sum += db[i];
                count++;
            }
        }

        return count > 0 ? sum / count : null;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();

        if (sorted.Length == 0)
        {
            return double.NaN;
        }

        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}

/// <summary>
///     Python's rounding, which the wording this was ported from was written against: halves to even.
/// </summary>
public static class Py
{
    public static double Round(double value, int digits) => Math.Round(value, digits, MidpointRounding.ToEven);

    public static string Fixed(double value, int digits)
        => Round(value, digits).ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture);

    public static string Percent(double share) => Fixed(share * 100, 0) + "%";

    public static string General(double value) => value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    public static long RoundToLong(double value) => (long)Math.Round(value, MidpointRounding.ToEven);

    public static string Clock(double? seconds)
    {
        if (seconds is not { } s)
        {
            return null;
        }

        var whole = (long)s;
        return $"{whole / 60}:{whole % 60:00}";
    }
}
