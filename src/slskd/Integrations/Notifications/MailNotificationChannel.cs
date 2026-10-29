// <copyright file="MailNotificationChannel.cs" company="JP Dillingham">
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

using System.Threading;
using System.Threading.Tasks;
using slskd.Integrations.Mail;

/// <summary>
///     Notifies by mail, through whichever mail adapter is configured.
/// </summary>
/// <remarks>
///     On whenever <c>integrations.mail</c> is: there is nothing to configure here that mail does not already have.
/// </remarks>
public class MailNotificationChannel : INotificationChannel
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MailNotificationChannel"/> class.
    /// </summary>
    /// <param name="mailService">The mail service.</param>
    public MailNotificationChannel(MailService mailService)
    {
        Mail = mailService;
    }

    /// <inheritdoc/>
    public string Name => "mail";

    /// <inheritdoc/>
    public bool IsEnabled => Mail.IsEnabled;

    private MailService Mail { get; }

    /// <inheritdoc/>
    public async Task SendAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        var text = notification.MailText ?? (string.IsNullOrWhiteSpace(notification.Url)
            ? notification.Text
            : $"{notification.Text}\n\n{notification.Url}");

        try
        {
            await Mail.SendAsync(
                new slskd.Integrations.Mail.Mail
                {
                    To = notification.MailTo,
                    Subject = notification.Title,
                    Text = text,
                    Html = notification.MailHtml,
                },
                cancellationToken);
        }
        catch (MailException ex)
        {
            throw new NotificationException(ex.Message, ex);
        }
    }
}
