// <copyright file="SmtpMailAdapter.cs" company="JP Dillingham">
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

namespace slskd.Integrations.Mail;

using System;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

/// <summary>
///     Sends mail by talking SMTP to a server.
/// </summary>
public class SmtpMailAdapter : IMailAdapter
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SmtpMailAdapter"/> class.
    /// </summary>
    /// <param name="optionsMonitor">The options monitor used to derive application options.</param>
    public SmtpMailAdapter(IOptionsMonitor<Options> optionsMonitor)
    {
        OptionsMonitor = optionsMonitor;
    }

    /// <inheritdoc/>
    public string Name => "smtp";

    private IOptionsMonitor<Options> OptionsMonitor { get; }

    /// <inheritdoc/>
    public async Task SendAsync(Mail mail, string from, string to, CancellationToken cancellationToken = default)
    {
        var options = OptionsMonitor.CurrentValue.Integrations.Mail.Smtp;

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = mail.Subject;
        message.Body = new BodyBuilder { HtmlBody = mail.Html, TextBody = mail.Text }.ToMessageBody();

        var security = options.Encryption?.ToLowerInvariant() switch
        {
            "none" => SecureSocketOptions.None,
            "tls" => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.StartTls,
        };

        try
        {
            using var client = new SmtpClient { Timeout = options.Timeout };

            await client.ConnectAsync(options.Host, options.Port, security, cancellationToken);

            // only authenticate when a username is configured. a relay on a trusted network may want none, and
            // offering empty credentials is refused by servers that would otherwise have accepted the message
            if (!string.IsNullOrWhiteSpace(options.Username))
            {
                await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MailException($"Failed to send mail via SMTP: {ex.Message}", ex);
        }
    }
}
