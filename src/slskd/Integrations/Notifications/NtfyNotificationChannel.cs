// <copyright file="NtfyNotificationChannel.cs" company="JP Dillingham">
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
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
///     Notifies through an ntfy server: a push to the operator's phone or desktop.
/// </summary>
/// <remarks>
///     Published as JSON to the server's base URL rather than as a body with headers. ntfy reads a title from a header
///     too, but a header cannot carry "Aurora's Embrace — 2 copies" safely; JSON carries any text. See
///     https://docs.ntfy.sh/publish/#publish-as-json.
/// </remarks>
public class NtfyNotificationChannel : INotificationChannel
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>
    ///     Initializes a new instance of the <see cref="NtfyNotificationChannel"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="optionsMonitor">The options monitor.</param>
    public NtfyNotificationChannel(IHttpClientFactory httpClientFactory, IOptionsMonitor<Options> optionsMonitor)
    {
        HttpClientFactory = httpClientFactory;
        OptionsMonitor = optionsMonitor;
    }

    /// <inheritdoc/>
    public string Name => "ntfy";

    /// <inheritdoc/>
    public bool IsEnabled => Settings.Configured;

    private IHttpClientFactory HttpClientFactory { get; }
    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private Options.IntegrationsOptions.NtfyOptions Settings => OptionsMonitor.CurrentValue.Integrations.Notifications.Ntfy;

    /// <summary>
    ///     ntfy's priority for a notification's: 2 (low), 3 (default), 4 (high). The two extremes are left alone -- 1
    ///     is silent, which a notification someone asked for should not be, and 5 overrides do-not-disturb.
    /// </summary>
    /// <param name="priority">The priority.</param>
    /// <returns>ntfy's.</returns>
    public static int PriorityOf(NotificationPriority priority) => priority switch
    {
        NotificationPriority.Low => 2,
        NotificationPriority.High => 4,
        _ => 3,
    };

    /// <inheritdoc/>
    public async Task SendAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        var settings = Settings;

        if (!settings.Configured)
        {
            throw new NotificationException("ntfy is not configured");
        }

        var body = new
        {
            topic = settings.Topic,
            title = notification.Title,
            message = notification.Text,
            priority = PriorityOf(notification.Priority),
            click = string.IsNullOrWhiteSpace(notification.Url) ? null : notification.Url,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Url.TrimEnd('/'))
        {
            Content = JsonContent.Create(body),
        };

        if (!string.IsNullOrWhiteSpace(settings.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        HttpResponseMessage response;

        try
        {
            var client = HttpClientFactory.CreateClient();
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            response = await client.SendAsync(request, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new NotificationException($"ntfy did not answer: {ex.Message}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // the status says enough, and a token or a topic must not travel into a log through a body
                throw new NotificationException($"ntfy refused the notification: {(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }
    }
}
