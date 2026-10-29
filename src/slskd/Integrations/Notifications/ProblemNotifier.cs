// <copyright file="ProblemNotifier.cs" company="JP Dillingham">
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
using System.Threading.Tasks;
using Serilog;

/// <summary>
///     Notifies problems worth knowing about without looking: the Soulseek connection down for a while, and a share
///     scan that failed.
/// </summary>
/// <remarks>
///     <para>
///         Read from the application state, which already says both. The connection counts as down only while the
///         watchdog is trying to bring it back: a disconnect the operator asked for is not a problem. It is notified
///         once it has been down for <c>after</c> minutes -- a reconnect after a blip is not news -- and once more when
///         it is back, so the first notification is not left standing as if nothing had changed.
///     </para>
///     <para>
///         A scan is notified when it becomes faulted, once per failure.
///     </para>
/// </remarks>
public class ProblemNotifier
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ProblemNotifier"/> class.
    /// </summary>
    /// <param name="stateMonitor">The application state.</param>
    /// <param name="notifications">The notification service.</param>
    /// <param name="optionsMonitor">The options monitor.</param>
    public ProblemNotifier(
        IStateMonitor<State> stateMonitor,
        NotificationService notifications,
        IOptionsMonitor<Options> optionsMonitor)
    {
        StateMonitor = stateMonitor;
        Notifications = notifications;
        OptionsMonitor = optionsMonitor;

        StateMonitor.OnChange(change => _ = CheckAsync(change.Current, DateTime.UtcNow));

        // the time passing is itself what makes a connection that stays down a problem, and the state does not change
        // while it stays down
        Clock.EveryMinute += (_, _) => _ = CheckAsync(StateMonitor.CurrentValue, DateTime.UtcNow);
    }

    private IStateMonitor<State> StateMonitor { get; }
    private NotificationService Notifications { get; }
    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<ProblemNotifier>();
    private object SyncRoot { get; } = new();
    private DateTime? DownSince { get; set; }
    private bool DownNotified { get; set; }
    private bool Faulted { get; set; }

    /// <summary>
    ///     Looks at the state and notifies what has become a problem, or stopped being one.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="now">The time.</param>
    /// <returns>The operation context.</returns>
    public async Task CheckAsync(State state, DateTime now)
    {
        Notification connection = null;
        Notification scan = null;

        lock (SyncRoot)
        {
            var down = state.ConnectionWatchdog.IsEnabled && !state.Server.IsLoggedIn;

            if (down)
            {
                DownSince ??= now;

                var after = TimeSpan.FromMinutes(OptionsMonitor.CurrentValue.Integrations.Notifications.Events.ConnectionLost.After);

                if (!DownNotified && now - DownSince.Value >= after)
                {
                    DownNotified = true;
                    connection = new Notification
                    {
                        Event = NotificationEvent.ConnectionLost,
                        Title = "slskd lost its Soulseek connection",
                        Text = $"Not logged in for {(int)(now - DownSince.Value).TotalMinutes} minutes, and still trying to reconnect.",
                        Priority = NotificationPriority.High,
                    };
                }
            }
            else
            {
                if (DownNotified)
                {
                    connection = new Notification
                    {
                        Event = NotificationEvent.ConnectionLost,
                        Title = "slskd is back on Soulseek",
                        Text = $"Logged in again after {(int)(now - DownSince.Value).TotalMinutes} minutes.",
                        Priority = NotificationPriority.Low,
                    };
                }

                DownSince = null;
                DownNotified = false;
            }

            if (state.Shares.Faulted && !Faulted)
            {
                scan = new Notification
                {
                    Event = NotificationEvent.ShareScanFailed,
                    Title = "slskd's share scan failed",
                    Text = "The shares could not be scanned. Peers see what was shared before; the log says why.",
                    Priority = NotificationPriority.High,
                };
            }

            Faulted = state.Shares.Faulted;
        }

        foreach (var notification in new[] { connection, scan })
        {
            if (notification is null)
            {
                continue;
            }

            try
            {
                await Notifications.NotifyAsync(notification);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to notify {Event}: {Message}", notification.Event, ex.Message);
            }
        }
    }
}
