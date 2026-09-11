// <copyright file="SendmailMailAdapter.cs" company="JP Dillingham">
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
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
///     Sends mail by piping it to a local sendmail-compatible binary.
/// </summary>
/// <remarks>
///     For an installation that already has working local mail. A containerised deployment almost certainly does not
///     have one, and this adapter will fail at the first send rather than at startup.
/// </remarks>
public class SendmailMailAdapter : IMailAdapter
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SendmailMailAdapter"/> class.
    /// </summary>
    /// <param name="optionsMonitor">The options monitor used to derive application options.</param>
    public SendmailMailAdapter(IOptionsMonitor<Options> optionsMonitor)
    {
        OptionsMonitor = optionsMonitor;
    }

    /// <inheritdoc/>
    public string Name => "sendmail";

    private IOptionsMonitor<Options> OptionsMonitor { get; }

    /// <summary>
    ///     Composes the RFC 5322 message handed to the binary on stdin.
    /// </summary>
    /// <remarks>
    ///     Plain text only: an adapter that shells out is the one least able to get multipart right, and the plain body
    ///     is the one that has to be readable anyway. Headers are written explicitly because -t is what tells sendmail
    ///     to take the recipients from them.
    /// </remarks>
    /// <param name="mail">The mail to send.</param>
    /// <param name="from">The address to send from.</param>
    /// <param name="to">The address to send to.</param>
    /// <returns>The composed message.</returns>
    public static string Compose(Mail mail, string from, string to)
    {
        var builder = new StringBuilder();

        builder.Append("From: ").Append(from).Append('\n');
        builder.Append("To: ").Append(to).Append('\n');

        // a newline in a subject would let a caller write headers of its own
        builder.Append("Subject: ").Append(mail.Subject?.Replace('\r', ' ').Replace('\n', ' ')).Append('\n');
        builder.Append("Content-Type: text/plain; charset=utf-8\n");
        builder.Append('\n');
        builder.Append(mail.Text);

        return builder.ToString();
    }

    /// <inheritdoc/>
    public async Task SendAsync(Mail mail, string from, string to, CancellationToken cancellationToken = default)
    {
        var options = OptionsMonitor.CurrentValue.Integrations.Mail.Sendmail;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(options.Path, ["-t", "-i"])
                {
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            process.Start();

            await process.StandardInput.WriteAsync(Compose(mail, from, to));
            process.StandardInput.Close();

            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                throw new MailException($"{options.Path} exited with {process.ExitCode}: {error}");
            }
        }
        catch (Exception ex) when (ex is not MailException and not OperationCanceledException)
        {
            throw new MailException($"Failed to send mail via {options.Path}: {ex.Message}", ex);
        }
    }
}
