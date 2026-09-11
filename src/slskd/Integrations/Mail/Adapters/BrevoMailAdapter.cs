// <copyright file="BrevoMailAdapter.cs" company="JP Dillingham">
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
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
///     Sends mail through Brevo's transactional API.
/// </summary>
/// <remarks>
///     No SMTP conversation and no mail library; an HTTP request with an API key. Note that Brevo restricts API keys
///     by source IP by default, and nothing here maintains that allowlist -- a key that works today stops working when
///     the address it was allowed for changes, and the failure is a 401 naming an address rather than anything about
///     the message.
/// </remarks>
public class BrevoMailAdapter : IMailAdapter
{
    private static readonly Uri SendUri = new("https://api.brevo.com/v3/smtp/email");

    /// <summary>
    ///     Initializes a new instance of the <see cref="BrevoMailAdapter"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HttpClientFactory to use.</param>
    /// <param name="optionsMonitor">The options monitor used to derive application options.</param>
    public BrevoMailAdapter(IHttpClientFactory httpClientFactory, IOptionsMonitor<Options> optionsMonitor)
    {
        HttpClientFactory = httpClientFactory;
        OptionsMonitor = optionsMonitor;
    }

    /// <inheritdoc/>
    public string Name => "brevo";

    private IHttpClientFactory HttpClientFactory { get; }
    private IOptionsMonitor<Options> OptionsMonitor { get; }

    /// <inheritdoc/>
    public async Task SendAsync(Mail mail, string from, string to, CancellationToken cancellationToken = default)
    {
        var options = OptionsMonitor.CurrentValue.Integrations.Mail.Brevo;

        var payload = new
        {
            sender = new { email = from },
            to = new[] { new { email = to } },
            subject = mail.Subject,
            textContent = mail.Text,
            htmlContent = mail.Html,
        };

        try
        {
            using var http = HttpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, SendUri) { Content = JsonContent.Create(payload) };

            request.Headers.TryAddWithoutValidation("api-key", options.ApiKey);

            using var response = await http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // the body carries the reason, and the reason is usually the IP restriction rather than the message
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new MailException($"Brevo refused the message: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
            }
        }
        catch (Exception ex) when (ex is not MailException and not OperationCanceledException)
        {
            throw new MailException($"Failed to send mail via Brevo: {ex.Message}", ex);
        }
    }
}
