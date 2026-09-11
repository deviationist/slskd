// <copyright file="Recurrence.cs" company="JP Dillingham">
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
namespace slskd.Search.Watches;

using System;
using System.Collections.Generic;
using System.Linq;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

/// <summary>
///     Reads a recurrence rule and says when it next comes due.
/// </summary>
/// <remarks>
///     <para>
///         Rules are stored in RFC 5545 form. The interface offers a handful of presets rather than the whole
///         standard, but nothing here is limited to those: a rule set through the API is evaluated the same way.
///     </para>
///     <para>
///         A rule is evaluated in a named time zone, not in UTC and not in the server's local time. "Every day at
///         03:00" is a different instant in summer than in winter, and an operator who asked for 03:00 means 03:00
///         where they are.
///     </para>
/// </remarks>
public static class Recurrence
{
    /// <summary>
    ///     Returns the first occurrence of <paramref name="rrule"/> strictly after <paramref name="after"/>.
    /// </summary>
    /// <param name="rrule">The recurrence rule.</param>
    /// <param name="anchor">The instant the rule is anchored to.</param>
    /// <param name="timeZone">The IANA time zone to evaluate in.</param>
    /// <param name="after">The instant to search from, in UTC.</param>
    /// <returns>The next occurrence, in UTC, or null if the rule has none.</returns>
    public static DateTime? Next(string rrule, DateTime anchor, string timeZone, DateTime after)
    {
        var calendar = Build(rrule, anchor, timeZone);

        // a window rather than an unbounded search: a rule can be exhausted (COUNT, UNTIL), and a year is long
        // enough to find the next occurrence of anything worth scheduling while still ending
        var occurrence = calendar
            .GetOccurrences(new CalDateTime(DateTime.SpecifyKind(after, DateTimeKind.Utc)))
            .TakeWhile(o => o.Period.StartTime.AsUtc <= after.AddYears(1))
            .Select(o => o.Period.StartTime.AsUtc)
            .FirstOrDefault(start => start > after);

        return occurrence == default ? null : occurrence;
    }

    /// <summary>
    ///     Returns the shortest gap between consecutive occurrences of <paramref name="rrule"/>, as a sanity check
    ///     against a rule that would search far too often.
    /// </summary>
    /// <remarks>
    ///     Looks at several occurrences rather than two: a weekly rule naming three days has gaps of one day and of
    ///     five, and the short one is the one that matters.
    /// </remarks>
    /// <param name="rrule">The recurrence rule.</param>
    /// <param name="anchor">The instant the rule is anchored to.</param>
    /// <param name="timeZone">The IANA time zone to evaluate in.</param>
    /// <returns>The shortest gap found, or null if fewer than two occurrences exist.</returns>
    public static TimeSpan? ShortestInterval(string rrule, DateTime anchor, string timeZone)
    {
        var calendar = Build(rrule, anchor, timeZone);
        var from = DateTime.SpecifyKind(anchor, DateTimeKind.Utc);

        var starts = calendar
            .GetOccurrences(new CalDateTime(from))
            .Select(o => o.Period.StartTime.AsUtc)
            .TakeWhile(start => start <= from.AddDays(90))
            .Take(32)
            .ToList();

        if (starts.Count < 2)
        {
            return null;
        }

        return starts.Zip(starts.Skip(1), (a, b) => b - a).Min();
    }

    /// <summary>
    ///     Returns a value indicating whether <paramref name="timeZone"/> names a time zone this system knows.
    /// </summary>
    /// <param name="timeZone">The IANA time zone name.</param>
    /// <returns>Whether it is known.</returns>
    public static bool IsKnownTimeZone(string timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException || ex is InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Returns a value indicating whether <paramref name="rrule"/> can be read at all.
    /// </summary>
    /// <param name="rrule">The recurrence rule.</param>
    /// <param name="error">The reason it cannot, if it cannot.</param>
    /// <returns>Whether the rule is valid.</returns>
    public static bool TryValidate(string rrule, out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(rrule))
        {
            error = "A recurrence rule is required";
            return false;
        }

        // FREQ=SECONDLY and MINUTELY are refused outright rather than left to the interval floor, because the floor
        // is a configurable number and these are never a reasonable thing to ask a peer-to-peer network for
        if (rrule.Contains("FREQ=SECONDLY", StringComparison.OrdinalIgnoreCase)
            || rrule.Contains("FREQ=MINUTELY", StringComparison.OrdinalIgnoreCase))
        {
            error = "A watch cannot recur more often than hourly";
            return false;
        }

        try
        {
            _ = new RecurrencePattern(rrule);
            return true;
        }
        catch (Exception ex)
        {
            error = $"The recurrence rule could not be read: {ex.Message}";
            return false;
        }
    }

    private static Calendar Build(string rrule, DateTime anchor, string timeZone)
    {
        var start = new CalDateTime(DateTime.SpecifyKind(anchor, DateTimeKind.Utc)).ToTimeZone(timeZone);

        var calendarEvent = new CalendarEvent
        {
            Start = start,
            Duration = new Duration(minutes: 1),
            RecurrenceRule = new RecurrencePattern(rrule),
        };

        var calendar = new Calendar();
        calendar.Events.Add(calendarEvent);

        return calendar;
    }
}
