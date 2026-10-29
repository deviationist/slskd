// <copyright file="RoomMentionNotifier.cs" company="JP Dillingham">
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

using Microsoft.Extensions.Options;

namespace slskd.Integrations.Notifications;

using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Serilog;
using slskd.Events;
using slskd.Messaging;

/// <summary>
///     Notifies a room message that mentions the operator by username.
/// </summary>
/// <remarks>
///     A mention is the username as a word of its own, in any case: "hey Quim" is one, "quimby" is not. A busy room
///     can mention someone often, so a room is notified at most once in <see cref="Quiet"/>.
/// </remarks>
public class RoomMentionNotifier
{
    /// <summary>
    ///     How long a room stays quiet after it has been notified.
    /// </summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(10);

    private const int MaximumPreview = 300;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RoomMentionNotifier"/> class.
    /// </summary>
    /// <param name="eventBus">The event bus.</param>
    /// <param name="stateMonitor">The application state, for the logged-in username.</param>
    /// <param name="notifications">The notification service.</param>
    /// <param name="optionsMonitor">The options monitor.</param>
    public RoomMentionNotifier(
        EventBus eventBus,
        IStateMonitor<State> stateMonitor,
        NotificationService notifications,
        IOptionsMonitor<Options> optionsMonitor)
    {
        StateMonitor = stateMonitor;
        Notifications = notifications;
        OptionsMonitor = optionsMonitor;

        eventBus.Subscribe<RoomMessageReceivedEvent>(nameof(RoomMentionNotifier), e => HandleAsync(e));
    }

    private IStateMonitor<State> StateMonitor { get; }
    private NotificationService Notifications { get; }
    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<RoomMentionNotifier>();
    private ConcurrentDictionary<string, DateTime> LastNotified { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Returns a value indicating whether <paramref name="text"/> mentions <paramref name="username"/>.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <param name="username">The operator's username.</param>
    /// <returns>Whether it does.</returns>
    public static bool Mentions(string text, string username)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        // a username may hold characters a regex treats as syntax, and a "word" here is anything bounded by what
        // cannot be part of a username, rather than \b, which does not bound names that end in a symbol
        var pattern = $"(?<![\\p{{L}}\\p{{N}}_]){Regex.Escape(username)}(?![\\p{{L}}\\p{{N}}_])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    /// <summary>
    ///     Handles a room message.
    /// </summary>
    /// <param name="received">The event.</param>
    /// <param name="now">The time, for the quiet period; now when not given.</param>
    /// <returns>The operation context.</returns>
    public async Task HandleAsync(RoomMessageReceivedEvent received, DateTime? now = null)
    {
        var message = received.Message;
        var me = StateMonitor.CurrentValue.User.Username;

        if (message is null
            || message.Direction == MessageDirection.Out
            || string.Equals(message.Username, me, StringComparison.OrdinalIgnoreCase)
            || !Notifications.IsEnabled(NotificationEvent.RoomMention)
            || !Mentions(message.Message, me))
        {
            return;
        }

        var at = now ?? DateTime.UtcNow;
        var room = message.RoomName ?? string.Empty;

        if (LastNotified.TryGetValue(room, out var last) && at - last < Quiet)
        {
            return;
        }

        LastNotified[room] = at;

        try
        {
            var preview = (message.Message ?? string.Empty).Trim();
            var baseUrl = OptionsMonitor.CurrentValue.Integrations.Mail.BaseUrl;

            await Notifications.NotifyAsync(new Notification
            {
                Event = NotificationEvent.RoomMention,
                Title = $"{message.Username} mentioned you in {room}",
                Text = preview.Length > MaximumPreview ? preview[..MaximumPreview] + "…" : preview,
                Url = string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/rooms",
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to notify a mention in {Room}: {Message}", room, ex.Message);
        }
    }
}
