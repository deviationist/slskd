// <copyright file="Fft.cs" company="JP Dillingham">
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
using System.Numerics;

/// <summary>
///     The power spectrum of a real signal, one window at a time.
/// </summary>
/// <remarks>
///     A plain radix-2 transform, written here rather than taken from a library: the analyser needs only this, at one
///     size, and a dependency for sixty lines of arithmetic is a dependency to keep up with.
/// </remarks>
public sealed class Fft
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Fft"/> class.
    /// </summary>
    /// <param name="size">The window length, a power of two.</param>
    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException("The size must be a power of two", nameof(size));
        }

        Size = size;
        Window = new double[size];

        // numpy's hanning: symmetric, so it reaches zero at both ends
        for (var n = 0; n < size; n++)
        {
            Window[n] = 0.5 - (0.5 * Math.Cos(2 * Math.PI * n / (size - 1)));
        }

        Reversed = new int[size];
        var bits = (int)Math.Log2(size);

        for (var i = 0; i < size; i++)
        {
            var r = 0;

            for (var b = 0; b < bits; b++)
            {
                r |= ((i >> b) & 1) << (bits - 1 - b);
            }

            Reversed[i] = r;
        }

        Twiddles = new Complex[size / 2];

        for (var k = 0; k < size / 2; k++)
        {
            Twiddles[k] = Complex.FromPolarCoordinates(1, -2 * Math.PI * k / size);
        }

        Work = new Complex[size];
    }

    /// <summary>
    ///     Gets the window length.
    /// </summary>
    public int Size { get; }

    /// <summary>
    ///     Gets the number of bins a power spectrum has: <c>Size / 2 + 1</c>, as numpy's rfft.
    /// </summary>
    public int Bins => (Size / 2) + 1;

    private double[] Window { get; }
    private int[] Reversed { get; }
    private Complex[] Twiddles { get; }
    private Complex[] Work { get; }

    /// <summary>
    ///     Transforms <paramref name="data"/> in place: the discrete Fourier transform, or with
    ///     <paramref name="inverse"/> its inverse, scaled by 1/n as numpy's is.
    /// </summary>
    /// <param name="data">The values, a power of two long.</param>
    /// <param name="inverse">Whether to take the inverse.</param>
    public static void Transform(Complex[] data, bool inverse = false)
    {
        var size = data.Length;

        if (size < 2 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException("The length must be a power of two", nameof(data));
        }

        for (int i = 1, j = 0; i < size; i++)
        {
            var bit = size >> 1;

            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;

            if (i < j)
            {
                (data[i], data[j]) = (data[j], data[i]);
            }
        }

        for (var length = 2; length <= size; length <<= 1)
        {
            var angle = (inverse ? 2 : -2) * Math.PI / length;
            var step = Complex.FromPolarCoordinates(1, angle);

            for (var start = 0; start < size; start += length)
            {
                var w = Complex.One;

                for (var k = 0; k < length / 2; k++)
                {
                    var odd = data[start + k + (length / 2)] * w;
                    var even = data[start + k];
                    data[start + k] = even + odd;
                    data[start + k + (length / 2)] = even - odd;
                    w *= step;
                }
            }
        }

        if (inverse)
        {
            for (var i = 0; i < size; i++)
            {
                data[i] /= size;
            }
        }
    }

    /// <summary>
    ///     Adds the power spectrum, <c>|rfft(samples * hanning)|²</c>, of one window to <paramref name="into"/>.
    /// </summary>
    /// <param name="samples">One window of samples.</param>
    /// <param name="into">Where the power is summed, <see cref="Bins"/> long.</param>
    public void AddPower(ReadOnlySpan<float> samples, double[] into)
    {
        for (var i = 0; i < Size; i++)
        {
            Work[Reversed[i]] = new Complex(samples[i] * Window[i], 0);
        }

        for (var length = 2; length <= Size; length <<= 1)
        {
            var half = length / 2;
            var step = Size / length;

            for (var start = 0; start < Size; start += length)
            {
                for (var k = 0; k < half; k++)
                {
                    var odd = Work[start + k + half] * Twiddles[k * step];
                    var even = Work[start + k];
                    Work[start + k] = even + odd;
                    Work[start + k + half] = even - odd;
                }
            }
        }

        for (var k = 0; k < Bins; k++)
        {
            var c = Work[k];
            into[k] += (c.Real * c.Real) + (c.Imaginary * c.Imaginary);
        }
    }
}
