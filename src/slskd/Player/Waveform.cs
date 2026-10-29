// <copyright file="Waveform.cs" company="JP Dillingham">
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

namespace slskd.Player;

using System;
using System.Collections.Generic;

/// <summary>
///     The rules that decide whether a waveform or a transcode can be trusted.
/// </summary>
/// <remarks>
///     Pure functions, kept apart from the process calls so they can be tested without ffmpeg. Each one is a rule
///     learned in production.
/// </remarks>
public static class Waveform
{
    /// <summary>
    ///     The sample rate the peaks are decoded at: high enough that a bucket is an average of real samples, low enough
    ///     that a 10-minute track is under 10 MB of PCM.
    /// </summary>
    public const int PeakRate = 8000;

    /// <summary>
    ///     Buckets in a waveform. The bar is ~1200 px at most; more is detail nobody can see, paid for in JSON.
    /// </summary>
    public const int PeakBuckets = 1600;

    /// <summary>
    ///     How far a transcode may differ from its source before it is not a finished one. Opus is encoded in 20 ms
    ///     frames and the muxer rounds to a frame, so exact is not on offer; a second is far outside that rounding and far
    ///     inside anything a listener would call an early ending.
    /// </summary>
    public const double DurationSlack = 1.0;

    /// <summary>
    ///     Per-bucket peak amplitude, 0..1, rounded to three places.
    /// </summary>
    /// <remarks>
    ///     The loudest sample in a bucket rather than its mean: a mean over 1500 samples of music is close to zero
    ///     everywhere and draws a flat line. Over 32768, not 32767: -32768 is a legal sample and would round past 1.0.
    /// </remarks>
    /// <param name="samples">Mono 16-bit samples.</param>
    /// <param name="buckets">How many buckets to return, at most.</param>
    /// <returns>The peaks.</returns>
    public static double[] Bucket(ReadOnlySpan<short> samples, int buckets = PeakBuckets)
    {
        var n = samples.Length;

        if (n == 0)
        {
            return Array.Empty<double>();
        }

        buckets = Math.Max(1, Math.Min(buckets, n));
        var peaks = new double[buckets];

        for (var i = 0; i < buckets; i++)
        {
            var lo = (int)((long)i * n / buckets);
            var hi = Math.Max(lo + 1, (int)((long)(i + 1) * n / buckets));
            var amp = 0;

            foreach (var sample in samples[lo..hi])
            {
                amp = Math.Max(amp, Math.Abs((int)sample));
            }

            peaks[i] = Math.Round(amp / 32768.0, 3);
        }

        return peaks;
    }

    /// <summary>
    ///     Decodes little-endian 16-bit PCM.
    /// </summary>
    /// <param name="pcm">The bytes; an odd trailing byte is dropped.</param>
    /// <returns>The samples.</returns>
    public static short[] Samples(ReadOnlySpan<byte> pcm)
    {
        var samples = new short[pcm.Length / 2];

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(pcm[2 * i] | (pcm[(2 * i) + 1] << 8));
        }

        return samples;
    }

    /// <summary>
    ///     Why a finished transcode is not to be served, or null when it is.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Checked in both directions.</b> On 2026-09-07 a 6:17 track played to 5:27 and then showed <c>60:00 /
    ///         6:17</c>: a half-written cache entry whose header claimed exactly 3600 seconds. ffmpeg writes <c>-t</c>'s
    ///         value into the WebM header up front and corrects it on the way out, so an encode that never finished reads
    ///         as <i>longer</i> than its source. A one-sided "not shorter" check waves it through.
    ///     </para>
    ///     <para>
    ///         An unreadable source duration is not a failure: this runs after a transcode ffmpeg reported success for,
    ///         and refusing it because a second probe would not answer trades a rare bad file for a reliably broken one.
    ///     </para>
    /// </remarks>
    /// <param name="got">The transcode's header duration, or null if it has none.</param>
    /// <param name="want">The source's duration, or null if it would not say.</param>
    /// <returns>A reason, or null.</returns>
    public static string Refusal(double? got, double? want)
    {
        if (got is null)
        {
            return "the transcode carries no duration -- it did not finish";
        }

        if (want is null)
        {
            return null;
        }

        var target = Math.Min(want.Value, Codecs.MaxSeconds);

        if (got + DurationSlack < target)
        {
            return $"the transcode stopped early: {got:0.0}s of {target:0.0}s";
        }

        if (got > target + DurationSlack)
        {
            // deliberately not "too long", which invites someone to widen the slack. nothing was over-encoded; the
            // header was never rewritten
            return $"the transcode was never finalised: its header claims {got:0.0}s of an expected {target:0.0}s";
        }

        return null;
    }

    /// <summary>
    ///     The decoded length of a waveform's samples, in seconds. From the sample count rather than a header: a
    ///     truncated download disagrees with its own header.
    /// </summary>
    /// <param name="samples">How many samples were decoded.</param>
    /// <returns>Seconds, rounded to milliseconds.</returns>
    public static double Duration(int samples) => Math.Round((double)samples / PeakRate, 3);
}
