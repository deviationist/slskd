// <copyright file="RoomMessageNotifier.cs" company="JP Dillingham">
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
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using slskd.Events;
using slskd.Messaging;

/// <summary>
///     Notifies a room message that has gone unread: once per room until the room is read.
/// </summary>
/// <remarks>
///     <para>
///         The same rules as <see cref="PrivateMessageNotifier"/>, by room rather than by person. A message is notified
///         only if its room is still unread <c>delay</c> seconds after it arrived, so nothing is sent while the
///         operator has the room open: the Rooms page reads the open room as messages arrive. And only once per room
///         until it is read, which matters more here than in private chat -- a busy room is a stream of lines, and
///         this is one notification for all of them. What is unread is the server's own count,
///         <c>state.roomsUnread</c>, the one the menu shows.
///     </para>
///     <para>
///         A message that mentions the operator is left to <see cref="RoomMentionNotifier"/> when that is enabled, so a
///         mention is not notified twice.
///     </para>
/// </remarks>
public class RoomMessageNotifier
{
    private const int MaximumPreview = 300;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RoomMessageNotifier"/> class.
    /// </summary>
    /// <param name="eventBus">The event bus.</param>
    /// <param name="stateMonitor">The application state.</param>
    /// <param name="notifications">The notification service.</param>
    /// <param name="optionsMonitor">The options monitor.</param>
    public RoomMessageNotifier(
        EventBus eventBus,
        IStateMonitor<State> stateMonitor,
        NotificationService notifications,
        IOptionsMonitor<Options> optionsMonitor)
    {
        StateMonitor = stateMonitor;
        Notifications = notifications;
        OptionsMonitor = optionsMonitor;

        // a room read is one that may be notified again
        StateMonitor.OnChange(change =>
        {
            foreach (var room in Notified.Keys)
            {
                if (CountOf(change.Current, room) == 0)
                {
                    Notified.TryRemove(room, out _);
                }
            }
        });

        eventBus.Subscribe<RoomMessageReceivedEvent>(nameof(RoomMessageNotifier), HandleAsync);
    }

    private IStateMonitor<State> StateMonitor { get; }
    private NotificationService Notifications { get; }
    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<RoomMessageNotifier>();

    // rooms notified since they were last read, and rooms waiting out the delay
    private ConcurrentDictionary<string, bool> Notified { get; } = new(StringComparer.Ordinal);
    private ConcurrentDictionary<string, bool> Waiting { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Handles a message: waits the delay, then notifies if its room is still unread and not yet notified.
    /// </summary>
    /// <param name="received">The event.</param>
    /// <returns>The operation context.</returns>
    public async Task HandleAsync(RoomMessageReceivedEvent received)
    {
        var message = received.Message;
        var room = message?.RoomName;
        var me = StateMonitor.CurrentValue.User.Username;
        var options = OptionsMonitor.CurrentValue.Integrations.Notifications.Events.RoomMessage;

        if (string.IsNullOrEmpty(room)
            || message.Direction == MessageDirection.Out
            || string.Equals(message.Username, me, StringComparison.Ordinal)
            || !Notifications.IsEnabled(NotificationEvent.RoomMessage)
            || !(options.Rooms is not { Length: > 0 } || options.Rooms.Contains(room, StringComparer.OrdinalIgnoreCase))
            || (Notifications.IsEnabled(NotificationEvent.RoomMention) && RoomMentionNotifier.Mentions(message.Message, me))
            || Notified.ContainsKey(room)
            || !Waiting.TryAdd(room, true))
        {
            return;
        }

        try
        {
            if (options.Delay > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(options.Delay));
            }

            var unread = CountOf(StateMonitor.CurrentValue, room);

            if (unread == 0 || !Notified.TryAdd(room, true))
            {
                return;
            }

            await Notifications.NotifyAsync(Compose(room, message.Username, message.Message, unread));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to notify a message in {Room}: {Message}", room, ex.Message);
        }
        finally
        {
            Waiting.TryRemove(room, out _);
        }
    }

    /// <summary>
    ///     The notification for a room's unread messages.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <param name="username">Who wrote the message that started it.</param>
    /// <param name="latest">What they wrote.</param>
    /// <param name="unread">How many of the room's messages are unread.</param>
    /// <returns>The notification.</returns>
    public Notification Compose(string room, string username, string latest, int unread)
    {
        var preview = $"{username}: {(latest ?? string.Empty).Trim()}";

        if (preview.Length > MaximumPreview)
        {
            preview = preview[..MaximumPreview] + "…";
        }

        var baseUrl = OptionsMonitor.CurrentValue.Integrations.Mail.BaseUrl;

        return new Notification
        {
            Event = NotificationEvent.RoomMessage,
            Title = unread == 1 ? $"New message in {room}" : $"{unread} new messages in {room}",
            Text = preview,
            Url = string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/rooms",
        };
    }

    private static int CountOf(State state, string room)
        => state.RoomsUnread.ByRoom.FirstOrDefault(r => r.RoomName == room)?.Count ?? 0;
}
