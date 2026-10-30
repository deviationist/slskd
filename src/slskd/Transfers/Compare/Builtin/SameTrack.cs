// <copyright file="SameTrack.cs" company="JP Dillingham">
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
using System.Numerics;
using slskd.Player.Builtin;

/// <summary>
///     One file reduced to what matching needs: how its music moves, band by band, and the facts worth showing.
/// </summary>
/// <param name="Bands">Log energy per frame in each band, each band standardised: frames x <see cref="SameTrack.BandCount"/>.</param>
/// <param name="LeadSeconds">The silence trimmed off the start.</param>
/// <param name="TailSeconds">The silence trimmed off the end.</param>
/// <param name="MusicSeconds">What is left between them.</param>
/// <param name="PowerDb">Mean power of the music, only ever compared with another file's.</param>
public record Features(float[][] Bands, double LeadSeconds, double TailSeconds, double MusicSeconds, double PowerDb);

/// <summary>
///     How far two files are the same music, and how they differ if they are.
/// </summary>
/// <param name="Verdict"><c>same</c>, <c>partly</c> or <c>different</c>.</param>
/// <param name="Score">The median section score.</param>
/// <param name="Share">The share of sections that matched.</param>
/// <param name="OffsetSeconds">Where the second file's music starts relative to the first's.</param>
/// <param name="DriftMs">How far the alignment wanders across the track, as a speed difference does; 0 below noise.</param>
/// <param name="GainDb">How much louder the second is.</param>
public record PairMatch(string Verdict, double Score, double Share, double OffsetSeconds, int DriftMs, double GainDb);

/// <summary>
///     Are these files the same track, whatever they are called?
/// </summary>
/// <remarks>
///     <para>
///         Size, bitrate, format and loudness all differ between copies of one track, and none of them is the track.
///         What survives every one of them is how the music moves -- where it is loud, in which part of the spectrum,
///         and when. So each file is reduced to that: mono at 11025 Hz, silence trimmed off both ends, log energy in
///         nine bands at 100 frames a second, each band standardised so gain and EQ tilt drop out. Two files are lined
///         up once for the whole track, then section by section with a little give, and scored by how well the bands
///         agree once lined up. The section-by-section step is what catches a rip running a hair slow.
///     </para>
///     <para>
///         Ported from media-bridge's <c>sametrack.py</c> at features schema 1, check for check and threshold for
///         threshold, and kept independently from here on; versioned by <see cref="BuiltinCompare.Version"/>.
///         Calibrated there: copies of one track 0.98-1.00 in every section, a different remix built on the same parts
///         0.16, an unrelated track 0.04.
///     </para>
/// </remarks>
public static class SameTrack
{
    public const int Rate = 11025;
    public const int Hop = 110;
    public const int Window = 1024;
    public const double Fps = Rate / (double)Hop;

    /// <summary>Band edges in Hz; the last band runs to Nyquist. Coarse on purpose: an encoder's work lives in the
    /// fine detail and the treble, and neither is what makes a track the track.</summary>
    public static readonly double[] Edges = [60, 150, 300, 600, 1200, 2400, 4000, 5500];

    public static int BandCount => Edges.Length;

    /// <summary>Frames this far below the loudest are silence, for trimming the ends.</summary>
    public const double SilenceDb = 50.0;

    /// <summary>How far apart two copies' starts may be.</summary>
    public const double MaxLagSeconds = 30.0;

    public const double SectionSeconds = 10.0;

    /// <summary>The give each section gets around the whole-track alignment.</summary>
    public const double LocalSeconds = 1.5;

    /// <summary>A section agreeing this well is the same music.</summary>
    public const double SectionMatch = 0.8;

    /// <summary>Share of sections that must match to call it the same track...</summary>
    public const double SameShare = 0.8;

    /// <summary>...and below which nothing worth saying is shared.</summary>
    public const double PartlyShare = 0.3;

    /// <summary>A drift smaller than this is a frame or two of noise, not a speed.</summary>
    public const int DriftReportMs = 50;

    /// <summary>
    ///     Builds features from mono samples at <see cref="Rate"/>, as they are decoded.
    /// </summary>
    public sealed class Builder
    {
        private static readonly double[] Hann = Enumerable.Range(0, Window).Select(n => 0.5 - (0.5 * Math.Cos(2 * Math.PI * n / (Window - 1)))).ToArray();

        // which band each bin of the rfft falls in, or -1 below the first edge
        private static readonly int[] BandOf = Enumerable.Range(0, (Window / 2) + 1).Select(k =>
        {
            var freq = k * (double)Rate / Window;

            for (var b = Edges.Length - 1; b >= 0; b--)
            {
                if (freq >= Edges[b])
                {
                    return b;
                }
            }

            return -1;
        }).ToArray();

        private readonly float[] ring = new float[Window];
        private readonly Complex[] work = new Complex[Window];
        private readonly List<double[]> bands = [];
        private readonly List<double> totals = [];
        private long received;

        /// <summary>
        ///     Adds one sample.
        /// </summary>
        /// <param name="sample">The sample.</param>
        public void Add(float sample)
        {
            ring[received % Window] = sample;
            received++;

            // frame k covers samples k*Hop .. k*Hop+Window-1, and is ready once the last of them is in
            var end = received - Window;

            if (end >= 0 && end % Hop == 0)
            {
                Frame(end);
            }
        }

        /// <summary>
        ///     The features, once every sample is in.
        /// </summary>
        /// <returns>The features.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the file is too short to compare.</exception>
        public Features Finish()
        {
            var n = totals.Count;

            if (n < (int)(SectionSeconds * Fps))
            {
                throw new InvalidOperationException("too short to compare");
            }

            var level = totals.Select(t => 10.0 * Math.Log10(t + 1e-12)).ToArray();
            var max = level.Max();
            var lead = Array.FindIndex(level, l => l > max - SilenceDb);
            var tail = Array.FindLastIndex(level, l => l > max - SilenceDb) + 1;

            // log energy per band, standardised per band over the music: gain and EQ tilt drop out
            var frames = tail - lead;
            var e = new double[frames][];

            for (var i = 0; i < frames; i++)
            {
                e[i] = bands[lead + i].Select(v => Math.Log10(v + 1e-12)).ToArray();
            }

            var standardised = new float[frames][];

            for (var i = 0; i < frames; i++)
            {
                standardised[i] = new float[BandCount];
            }

            for (var b = 0; b < BandCount; b++)
            {
                var mean = 0.0;

                for (var i = 0; i < frames; i++)
                {
                    mean += e[i][b];
                }

                mean /= frames;
                var variance = 0.0;

                for (var i = 0; i < frames; i++)
                {
                    variance += (e[i][b] - mean) * (e[i][b] - mean);
                }

                var std = Math.Sqrt(variance / frames) + 1e-9;

                for (var i = 0; i < frames; i++)
                {
                    standardised[i][b] = (float)((e[i][b] - mean) / std);
                }
            }

            var power = 10.0 * Math.Log10(totals.Skip(lead).Take(frames).Average() + 1e-12);

            return new Features(
                standardised,
                LeadSeconds: Py.Round(lead / Fps, 2),
                TailSeconds: Py.Round((n - tail) / Fps, 2),
                MusicSeconds: Py.Round(frames / Fps, 2),
                PowerDb: Py.Round(power, 2));
        }

        private void Frame(long start)
        {
            for (var i = 0; i < Window; i++)
            {
                work[i] = new Complex(ring[(start + i) % Window] * Hann[i], 0);
            }

            Fft.Transform(work);

            var perBand = new double[BandCount];
            double total = 0;

            for (var k = 0; k <= Window / 2; k++)
            {
                var c = work[k];
                var power = (c.Real * c.Real) + (c.Imaginary * c.Imaginary);
                total += power;

                if (BandOf[k] >= 0)
                {
                    perBand[BandOf[k]] += power;
                }
            }

            bands.Add(perBand);
            totals.Add(total);
        }
    }

    /// <summary>
    ///     How far two files are the same music.
    /// </summary>
    /// <param name="a">The first file's features.</param>
    /// <param name="b">The second file's.</param>
    /// <returns>The match.</returns>
    public static PairMatch Match(Features a, Features b)
    {
        var fa = a.Bands;
        var fb = b.Bands;
        var lag = WholeLag(fa, fb);
        var step = (int)(SectionSeconds * Fps);
        var give = (int)(LocalSeconds * Fps);
        var ea = fa.Select(r => r.Sum(v => (double)v)).ToArray();
        var eb = fb.Select(r => r.Sum(v => (double)v)).ToArray();

        // running sums of b's envelope, so each candidate's mean and spread cost nothing to find
        var sum = new double[eb.Length + 1];
        var squares = new double[eb.Length + 1];

        for (var i = 0; i < eb.Length; i++)
        {
            sum[i + 1] = sum[i] + eb[i];
            squares[i + 1] = squares[i] + (eb[i] * eb[i]);
        }

        var scores = new List<double>();
        var shifts = new List<int>();

        for (var t = 0; t <= fa.Length - step; t += step)
        {
            // clamped to b rather than skipped at its edges: skipping threw away the first section of every pair
            // whose starts line up, which is most
            var lo = Math.Max(0, t - lag - give);
            var hi = Math.Min(fb.Length - step, t - lag + give);

            if (lo > hi)
            {
                continue;
            }

            // the section's best place in b, by the summed envelope: one 1-D correlation per section. each candidate
            // is standardised before the dot product, as in the original -- done here through the running sums, as
            // (sum(c·w) - mean(c)·sum(w)) / std(c), so nothing is allocated per candidate
            var window = Z(ea.AsSpan(t, step));
            var windowSum = window.Sum();
            var best = double.NegativeInfinity;
            var at = lo;

            for (var c = lo; c <= hi; c++)
            {
                var mean = (sum[c + step] - sum[c]) / step;
                var variance = ((squares[c + step] - squares[c]) / step) - (mean * mean);
                var std = Math.Sqrt(Math.Max(variance, 0)) + 1e-9;
                double cw = 0;

                for (var i = 0; i < step; i++)
                {
                    cw += eb[c + i] * window[i];
                }

                var dot = (cw - (mean * windowSum)) / std;

                if (dot > best)
                {
                    best = dot;
                    at = c;
                }
            }

            scores.Add(BandCorrelation(fa, t, fb, at, step));
            shifts.Add(at - (t - lag));
        }

        if (scores.Count == 0)
        {
            return new PairMatch("different", 0.0, 0.0, 0.0, 0, 0.0);
        }

        var hits = scores.Select(s => s >= SectionMatch).ToList();
        var share = hits.Count(h => h) / (double)hits.Count;
        var verdict = share >= SameShare ? "same" : share >= PartlyShare ? "partly" : "different";

        // drift across the matching sections only: a section that matched nothing found its best place at random
        var kept = shifts.Where((_, i) => hits[i]).ToList();
        var drift = kept.Count > 1 ? (int)Math.Round((kept[^1] - kept[0]) / Fps * 1000, MidpointRounding.ToEven) : 0;

        // where b's music starts relative to a's, in the files themselves: the trimmed lead-ins go back on
        var offset = (b.LeadSeconds - a.LeadSeconds) - (lag / Fps);

        return new PairMatch(
            verdict,
            Py.Round(Median(scores), 3),
            Py.Round(share, 3),
            Py.Round(offset, 2),
            Math.Abs(drift) >= DriftReportMs ? drift : 0,
            Py.Round(b.PowerDb - a.PowerDb, 1));
    }

    // frames to shift b by so it lines up with a, a[i] ~ b[i - lag], from the summed envelopes' cross-correlation
    private static int WholeLag(float[][] a, float[][] b)
    {
        var sa = Z(a.Select(r => r.Sum(v => (double)v)).ToArray());
        var sb = Z(b.Select(r => r.Sum(v => (double)v)).ToArray());
        var n = 1 << (int)Math.Ceiling(Math.Log2(sa.Length + sb.Length));

        var fa = new Complex[n];
        var fbc = new Complex[n];

        for (var i = 0; i < sa.Length; i++)
        {
            fa[i] = sa[i];
        }

        for (var i = 0; i < sb.Length; i++)
        {
            fbc[i] = sb[i];
        }

        Fft.Transform(fa);
        Fft.Transform(fbc);

        for (var i = 0; i < n; i++)
        {
            fa[i] *= Complex.Conjugate(fbc[i]);
        }

        Fft.Transform(fa, inverse: true);

        // lags 0..m, then -m..-1, as numpy lays them out; the first maximum wins
        var m = (int)(MaxLagSeconds * Fps);
        var best = double.NegativeInfinity;
        var lag = 0;

        foreach (var k in Enumerable.Range(0, m + 1).Concat(Enumerable.Range(-m, m)))
        {
            var value = fa[k >= 0 ? k : n + k].Real;

            if (value > best)
            {
                best = value;
                lag = k;
            }
        }

        return lag;
    }

    private static double BandCorrelation(float[][] p, int pFrom, float[][] q, int qFrom, int length)
    {
        double sum = 0;

        for (var band = 0; band < BandCount; band++)
        {
            double mp = 0, mq = 0;

            for (var i = 0; i < length; i++)
            {
                mp += p[pFrom + i][band];
                mq += q[qFrom + i][band];
            }

            mp /= length;
            mq /= length;
            double pq = 0, pp = 0, qq = 0;

            for (var i = 0; i < length; i++)
            {
                var x = p[pFrom + i][band] - mp;
                var y = q[qFrom + i][band] - mq;
                pq += x * y;
                pp += x * x;
                qq += y * y;
            }

            sum += pq / (Math.Sqrt(pp * qq) + 1e-9);
        }

        return sum / BandCount;
    }

    private static double[] Z(ReadOnlySpan<double> values)
    {
        double mean = 0;

        foreach (var v in values)
        {
            mean += v;
        }

        mean /= values.Length;
        double variance = 0;

        foreach (var v in values)
        {
            variance += (v - mean) * (v - mean);
        }

        var std = Math.Sqrt(variance / values.Length) + 1e-9;
        var z = new double[values.Length];

        for (var i = 0; i < values.Length; i++)
        {
            z[i] = (values[i] - mean) / std;
        }

        return z;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
