// <copyright file="MailService.cs" company="JP Dillingham">
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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

/// <summary>
///     Sends mail through whichever adapter is configured.
/// </summary>
/// <remarks>
///     The adapter is resolved per send rather than at startup, so a change to the configuration takes effect without
///     a restart -- the same way every other option here behaves.
/// </remarks>
public class MailService
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MailService"/> class.
    /// </summary>
    /// <param name="adapters">The available adapters.</param>
    /// <param name="optionsMonitor">The options monitor used to derive application options.</param>
    public MailService(IEnumerable<IMailAdapter> adapters, IOptionsMonitor<Options> optionsMonitor)
    {
        Adapters = adapters;
        OptionsMonitor = optionsMonitor;
    }

    private IEnumerable<IMailAdapter> Adapters { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<MailService>();
    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private Options.IntegrationsOptions.MailOptions MailOptions => OptionsMonitor.CurrentValue.Integrations.Mail;

    /// <summary>
    ///     Gets a value indicating whether mail can be sent.
    /// </summary>
    public bool IsEnabled => MailOptions.Enabled;

    /// <summary>
    ///     Sends the specified <paramref name="mail"/>.
    /// </summary>
    /// <param name="mail">The mail to send.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The name of the adapter that sent it.</returns>
    /// <exception cref="MailException">Thrown when mail is disabled, no recipient is known, or the send fails.</exception>
    public async Task<string> SendAsync(Mail mail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);

        var options = MailOptions;

        if (!options.Enabled)
        {
            throw new MailException("Mail is not enabled");
        }

        // a message may name its own recipient; the configured address is what it falls back to. one of them has to
        // exist, and finding that out at send time is too late to be an error the operator can act on -- which is what
        // the test endpoint is for
        var to = string.IsNullOrWhiteSpace(mail.To) ? options.To : mail.To;

        if (string.IsNullOrWhiteSpace(to))
        {
            throw new MailException("No recipient was supplied and no default recipient is configured");
        }

        var adapter = Adapters.FirstOrDefault(a => a.Name.Equals(options.Adapter, StringComparison.OrdinalIgnoreCase))
            ?? throw new MailException($"No mail adapter named '{options.Adapter}' exists");

        Log.Debug("Sending mail to {To} via {Adapter}: {Subject}", to, adapter.Name, mail.Subject);

        await adapter.SendAsync(mail, from: options.From, to: to, cancellationToken);

        Log.Information("Sent mail to {To} via {Adapter}: {Subject}", to, adapter.Name, mail.Subject);

        return adapter.Name;
    }
}
