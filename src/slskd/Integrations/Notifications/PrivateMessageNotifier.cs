// <copyright file="PrivateMessageNotifier.cs" company="JP Dillingham">
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
///     Notifies a private message that has gone unread: once per conversation until it is read.
/// </summary>
/// <remarks>
///     <para>
///         A message is notified only if it is still unread <c>delay</c> seconds after it arrived, so nothing is sent
///         while the operator is in the conversation: the Chat page reads the open conversation as messages arrive.
///         And only once per person until they are read, so a burst of five lines is one notification, not five. What
///         is unread is the server's own count, <c>state.conversations</c>, the one the menu shows.
///     </para>
///     <para>
///         What was already unread when the application started counts as notified. The Soulseek server replays every
///         unacknowledged message on login, and without this a restart would notify every unread conversation at
///         once. Someone new who wrote while it was down is still notified; a replay of what was already waiting is
///         not.
///     </para>
/// </remarks>
public class PrivateMessageNotifier
{
    private const int MaximumPreview = 300;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PrivateMessageNotifier"/> class.
    /// </summary>
    /// <param name="eventBus">The event bus.</param>
    /// <param name="conversations">
    ///     The conversation service. Taken so that it is built first: it counts what is unread when it is constructed,
    ///     and that count is what this treats as already notified.
    /// </param>
    /// <param name="stateMonitor">The application state.</param>
    /// <param name="notifications">The notification service.</param>
    /// <param name="optionsMonitor">The options monitor.</param>
    public PrivateMessageNotifier(
        EventBus eventBus,
        IConversationService conversations,
        IStateMonitor<State> stateMonitor,
        NotificationService notifications,
        IOptionsMonitor<Options> optionsMonitor)
    {
        _ = conversations;
        StateMonitor = stateMonitor;
        Notifications = notifications;
        OptionsMonitor = optionsMonitor;

        foreach (var unread in StateMonitor.CurrentValue.Conversations.ByUser)
        {
            Notified.TryAdd(unread.Username, true);
        }

        // a conversation read is one that may be notified again
        StateMonitor.OnChange(change =>
        {
            foreach (var username in Notified.Keys)
            {
                if (CountOf(change.Current, username) == 0)
                {
                    Notified.TryRemove(username, out _);
                }
            }
        });

        eventBus.Subscribe<PrivateMessageReceivedEvent>(nameof(PrivateMessageNotifier), HandleAsync);
    }

    private IStateMonitor<State> StateMonitor { get; }
    private NotificationService Notifications { get; }
    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<PrivateMessageNotifier>();

    // people notified since they were last read, and people waiting out the delay
    private ConcurrentDictionary<string, bool> Notified { get; } = new();
    private ConcurrentDictionary<string, bool> Waiting { get; } = new();

    /// <summary>
    ///     Handles a message: waits the delay, then notifies if it is still unread and not yet notified.
    /// </summary>
    /// <param name="received">The event.</param>
    /// <returns>The operation context.</returns>
    public async Task HandleAsync(PrivateMessageReceivedEvent received)
    {
        var message = received.Message;
        var username = message?.Username;

        if (string.IsNullOrEmpty(username)
            || message.Direction != MessageDirection.In
            || !Notifications.IsEnabled(NotificationEvent.PrivateMessage)
            || Notified.ContainsKey(username)
            || !Waiting.TryAdd(username, true))
        {
            return;
        }

        try
        {
            var delay = OptionsMonitor.CurrentValue.Integrations.Notifications.Events.PrivateMessage.Delay;

            if (delay > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delay));
            }

            var unread = CountOf(StateMonitor.CurrentValue, username);

            if (unread == 0 || !Notified.TryAdd(username, true))
            {
                return;
            }

            await Notifications.NotifyAsync(Compose(username, message.Message, unread));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to notify a message from {Username}: {Message}", username, ex.Message);
        }
        finally
        {
            Waiting.TryRemove(username, out _);
        }
    }

    /// <summary>
    ///     The notification for someone's unread messages.
    /// </summary>
    /// <param name="username">Who wrote.</param>
    /// <param name="latest">What they wrote last.</param>
    /// <param name="unread">How many of their messages are unread.</param>
    /// <returns>The notification.</returns>
    public Notification Compose(string username, string latest, int unread)
    {
        var preview = (latest ?? string.Empty).Trim();

        if (preview.Length > MaximumPreview)
        {
            preview = preview[..MaximumPreview] + "…";
        }

        var baseUrl = OptionsMonitor.CurrentValue.Integrations.Mail.BaseUrl;

        return new Notification
        {
            Event = NotificationEvent.PrivateMessage,
            Title = unread == 1 ? $"Message from {username}" : $"{unread} messages from {username}",
            Text = preview,
            Url = string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/chat",
        };
    }

    private static int CountOf(State state, string username)
        => state.Conversations.ByUser.FirstOrDefault(u => u.Username == username)?.Count ?? 0;
}
