// <copyright file="NotificationService.cs" company="JP Dillingham">
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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

/// <summary>
///     Tells the operator something, on the channels chosen for what it is about.
/// </summary>
/// <remarks>
///     <para>
///         Routing is per event: an event that names channels goes to those that are enabled, and one that names none
///         goes to every enabled channel. An event that is not enabled goes nowhere.
///     </para>
///     <para>
///         Never throws at the caller. A notification is a side effect of something else -- a message, a watch run --
///         and must not fail it, so each channel's outcome is returned and logged instead, and one channel failing
///         does not stop the others.
///     </para>
/// </remarks>
public class NotificationService
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="NotificationService"/> class.
    /// </summary>
    /// <param name="channels">The channels.</param>
    /// <param name="optionsMonitor">The options monitor.</param>
    public NotificationService(IEnumerable<INotificationChannel> channels, IOptionsMonitor<Options> optionsMonitor)
    {
        Channels = channels.ToList();
        OptionsMonitor = optionsMonitor;
    }

    /// <summary>
    ///     Gets the names of the channels that can send right now.
    /// </summary>
    public IReadOnlyList<string> EnabledChannels => Channels.Where(c => c.IsEnabled).Select(c => c.Name).ToList();

    private List<INotificationChannel> Channels { get; }
    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<NotificationService>();
    private Options.IntegrationsOptions.NotificationEventsOptions Events => OptionsMonitor.CurrentValue.Integrations.Notifications.Events;

    /// <summary>
    ///     Returns the options for an event, or null for one that has none (a test).
    /// </summary>
    /// <param name="notificationEvent">The event.</param>
    /// <returns>Its options.</returns>
    public Options.IntegrationsOptions.NotificationEventOptions OptionsFor(NotificationEvent notificationEvent)
    {
        var events = Events;

        return notificationEvent switch
        {
            NotificationEvent.PrivateMessage => events.PrivateMessage,
            NotificationEvent.RoomMention => events.RoomMention,
            NotificationEvent.RoomMessage => events.RoomMessage,
            NotificationEvent.WatchResults => events.WatchResults,
            NotificationEvent.ConnectionLost => events.ConnectionLost,
            NotificationEvent.ShareScanFailed => events.ShareScanFailed,
            _ => null,
        };
    }

    /// <summary>
    ///     Returns a value indicating whether an event is notified about at all, on any channel. A source can ask
    ///     before composing anything.
    /// </summary>
    /// <param name="notificationEvent">The event.</param>
    /// <returns>Whether it is.</returns>
    public bool IsEnabled(NotificationEvent notificationEvent)
        => OptionsFor(notificationEvent)?.Enabled == true && ChannelsFor(notificationEvent).Any(c => c.IsEnabled);

    /// <summary>
    ///     Sends a notification on its event's channels.
    /// </summary>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What became of it on each channel; empty when the event is not enabled or no channel is.</returns>
    public Task<List<NotificationResult>> NotifyAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (OptionsFor(notification.Event)?.Enabled != true)
        {
            return Task.FromResult(new List<NotificationResult>());
        }

        return SendAsync(notification, ChannelsFor(notification.Event), cancellationToken);
    }

    /// <summary>
    ///     Sends a test notification on one channel, or on every enabled one, whatever the events are set to.
    /// </summary>
    /// <param name="channel">The channel's name, or null for every enabled channel.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What became of it on each channel.</returns>
    public Task<List<NotificationResult>> TestAsync(string channel = null, CancellationToken cancellationToken = default)
    {
        var chosen = string.IsNullOrWhiteSpace(channel)
            ? Channels.Where(c => c.IsEnabled)
            : Channels.Where(c => c.Name.Equals(channel, StringComparison.OrdinalIgnoreCase));

        var notification = new Notification
        {
            Event = NotificationEvent.Test,
            Title = "slskd notifications work",
            Text = "This is a test notification. Nothing has happened; you asked for this one.",
        };

        return SendAsync(notification, chosen.ToList(), cancellationToken);
    }

    private IEnumerable<INotificationChannel> ChannelsFor(NotificationEvent notificationEvent)
    {
        var named = OptionsFor(notificationEvent)?.Channels ?? [];

        return named.Length == 0
            ? Channels
            : Channels.Where(c => named.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<List<NotificationResult>> SendAsync(Notification notification, IEnumerable<INotificationChannel> channels, CancellationToken cancellationToken)
    {
        var results = new List<NotificationResult>();

        foreach (var channel in channels)
        {
            if (!channel.IsEnabled)
            {
                results.Add(new NotificationResult(channel.Name, false, $"{channel.Name} is not configured"));
                continue;
            }

            try
            {
                await channel.SendAsync(notification, cancellationToken);
                results.Add(new NotificationResult(channel.Name, true));
                Log.Information("Notified {Event} on {Channel}: {Title}", notification.Event, channel.Name, notification.Title);
            }
            catch (Exception ex)
            {
                results.Add(new NotificationResult(channel.Name, false, ex.Message));
                Log.Warning("Failed to notify {Event} on {Channel}: {Message}", notification.Event, channel.Name, ex.Message);
            }
        }

        return results;
    }
}
