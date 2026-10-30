// <copyright file="Notification.cs" company="JP Dillingham">
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

namespace slskd.Integrations.Notifications;

using System;
using System.Collections.Generic;

/// <summary>
///     The things a notification can be about. Each is routed and switched on its own.
/// </summary>
public enum NotificationEvent
{
    /// <summary>
    ///     A check that a channel works, sent on request.
    /// </summary>
    Test,

    /// <summary>
    ///     A private message that has gone unread.
    /// </summary>
    PrivateMessage,

    /// <summary>
    ///     A room message mentioning the operator.
    /// </summary>
    RoomMention,

    /// <summary>
    ///     A room message that has gone unread.
    /// </summary>
    RoomMessage,

    /// <summary>
    ///     New results for a search watch.
    /// </summary>
    WatchResults,

    /// <summary>
    ///     The Soulseek connection down for a while, or back after being down.
    /// </summary>
    ConnectionLost,

    /// <summary>
    ///     A share scan that failed.
    /// </summary>
    ShareScanFailed,
}

/// <summary>
///     How much a notification should interrupt. Mapped onto each channel's own levels.
/// </summary>
public enum NotificationPriority
{
    Low,
    Default,
    High,
}

/// <summary>
///     A way of telling the operator something.
/// </summary>
public interface INotificationChannel
{
    /// <summary>
    ///     Gets the name the channel is chosen by in configuration.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Gets a value indicating whether the channel is configured, and so can send.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    ///     Sends the specified <paramref name="notification"/>.
    /// </summary>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The operation context.</returns>
    /// <exception cref="NotificationException">Thrown when it cannot be sent.</exception>
    System.Threading.Tasks.Task SendAsync(Notification notification, System.Threading.CancellationToken cancellationToken = default);
}

/// <summary>
///     Something to tell the operator. A sender composes one and does not know which channels will carry it.
/// </summary>
/// <remarks>
///     <see cref="Title"/> and <see cref="Text"/> are what every channel shows, and have to stand on their own on a
///     phone's lock screen. Mail may say more, in <see cref="MailText"/> and <see cref="MailHtml"/>, when the sender has
///     more to say -- a watch's table of results.
/// </remarks>
public record Notification
{
    /// <summary>
    ///     Gets what this is about, which decides where it goes.
    /// </summary>
    public required NotificationEvent Event { get; init; }

    /// <summary>
    ///     Gets the title: a line, short enough for a notification banner.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    ///     Gets the message: plain text, a few lines at most.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    ///     Gets the page to open, if there is one: a channel that can link does.
    /// </summary>
    public string Url { get; init; }

    /// <summary>
    ///     Gets how much it should interrupt.
    /// </summary>
    public NotificationPriority Priority { get; init; } = NotificationPriority.Default;

    /// <summary>
    ///     Gets the address to mail, when it is not the configured one: a watch's own.
    /// </summary>
    public string MailTo { get; init; }

    /// <summary>
    ///     Gets the plain mail body, when it says more than <see cref="Text"/>.
    /// </summary>
    public string MailText { get; init; }

    /// <summary>
    ///     Gets the HTML mail body, if any.
    /// </summary>
    public string MailHtml { get; init; }
}

/// <summary>
///     What became of a notification on one channel.
/// </summary>
/// <param name="Channel">The channel.</param>
/// <param name="Sent">Whether it was sent.</param>
/// <param name="Error">Why not, when it was not.</param>
public record NotificationResult(string Channel, bool Sent, string Error = null);

/// <summary>
///     Thrown when a channel cannot send.
/// </summary>
public class NotificationException : Exception
{
    public NotificationException(string message)
        : base(message)
    {
    }

    public NotificationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
