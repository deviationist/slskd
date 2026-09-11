// <copyright file="MailServiceTests.cs" company="JP Dillingham">
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

namespace slskd.Tests.Unit.Integrations.Mail;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using slskd.Integrations.Mail;
using Xunit;

public class MailServiceTests
{
    [Fact]
    public async Task Refuses_To_Send_While_Mail_Is_Disabled()
    {
        var service = Service(Options(enabled: false), out _);

        var ex = await Assert.ThrowsAsync<MailException>(() => service.SendAsync(Message()));

        Assert.Contains("not enabled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_To_Send_With_No_Recipient_Anywhere()
    {
        // neither the message nor the configuration names one. this is worth its own answer rather than an adapter
        // failing in whatever way it happens to fail when handed an empty address
        var service = Service(Options(to: null), out _);

        var ex = await Assert.ThrowsAsync<MailException>(() => service.SendAsync(Message()));

        Assert.Contains("recipient", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sends_To_The_Configured_Address_When_The_Message_Names_None()
    {
        var service = Service(Options(to: "default@example.com"), out var adapter);

        await service.SendAsync(Message());

        Assert.Equal("default@example.com", adapter.To);
    }

    [Fact]
    public async Task Lets_A_Message_Name_Its_Own_Recipient()
    {
        var service = Service(Options(to: "default@example.com"), out var adapter);

        await service.SendAsync(Message() with { To = "someone@example.com" });

        Assert.Equal("someone@example.com", adapter.To);
    }

    [Fact]
    public async Task Sends_Through_The_Configured_Adapter()
    {
        var service = Service(Options(adapter: "fake"), out var adapter);

        var used = await service.SendAsync(Message());

        Assert.Equal("fake", used);
        Assert.Equal(1, adapter.Sends);
    }

    [Fact]
    public async Task Refuses_An_Adapter_That_Does_Not_Exist()
    {
        var service = Service(Options(adapter: "carrier-pigeon"), out var adapter);

        var ex = await Assert.ThrowsAsync<MailException>(() => service.SendAsync(Message()));

        Assert.Contains("carrier-pigeon", ex.Message);
        Assert.Equal(0, adapter.Sends);
    }

    [Fact]
    public async Task Lets_An_Adapter_Failure_Through_Unchanged()
    {
        // the adapter's own words are the only useful thing about a send that failed; a service that wrapped them in
        // its own would hide which of the host, the credential or the recipient was the problem
        var service = Service(Options(), out var adapter);
        adapter.Fail = new MailException("the server said no");

        var ex = await Assert.ThrowsAsync<MailException>(() => service.SendAsync(Message()));

        Assert.Equal("the server said no", ex.Message);
    }

    private static Mail Message() => new() { Subject = "subject", Text = "body" };

    private static Options Options(
        bool enabled = true,
        string adapter = "fake",
        string from = "slskd@example.com",
        string to = "default@example.com")
        => new()
        {
            Integrations = new Options.IntegrationsOptions
            {
                Mail = new Options.IntegrationsOptions.MailOptions
                {
                    Enabled = enabled,
                    Adapter = adapter,
                    From = from,
                    To = to,
                },
            },
        };

    private static MailService Service(Options options, out FakeAdapter adapter)
    {
        adapter = new FakeAdapter();
        return new MailService([adapter], new TestOptionsMonitor<Options>(options));
    }

    private sealed class FakeAdapter : IMailAdapter
    {
        public string Name => "fake";

        public int Sends { get; private set; }

        public string To { get; private set; }

        public string From { get; private set; }

        public MailException Fail { get; set; }

        public Task SendAsync(Mail mail, string from, string to, CancellationToken cancellationToken = default)
        {
            if (Fail is not null)
            {
                throw Fail;
            }

            Sends++;
            To = to;
            From = from;

            return Task.CompletedTask;
        }
    }
}

public class SendmailMailAdapterTests
{
    [Fact]
    public void Composes_A_Message_With_The_Headers_Sendmail_Reads_Recipients_From()
    {
        var composed = SendmailMailAdapter.Compose(
            new Mail { Subject = "hello", Text = "body" },
            from: "slskd@example.com",
            to: "someone@example.com");

        Assert.StartsWith("From: slskd@example.com\n", composed);
        Assert.Contains("To: someone@example.com\n", composed);
        Assert.Contains("Subject: hello\n", composed);
        Assert.EndsWith("\n\nbody", composed);
    }

    [Theory]
    [InlineData("one\nBcc: someone-else@example.com")]
    [InlineData("one\rBcc: someone-else@example.com")]
    [InlineData("one\r\nBcc: someone-else@example.com")]
    public void Refuses_To_Let_A_Subject_Write_Headers_Of_Its_Own(string subject)
    {
        // the subject is the one field a caller supplies that lands in the header block. a newline in it would end
        // the Subject header and begin another, which is how a message acquires recipients nobody asked for
        var composed = SendmailMailAdapter.Compose(
            new Mail { Subject = subject, Text = "body" },
            from: "slskd@example.com",
            to: "someone@example.com");

        // the text may well survive -- what must not is a *line* beginning with it, which is what makes a header
        Assert.DoesNotContain(composed.Split('\n'), line => line.StartsWith("Bcc:", StringComparison.Ordinal));
        Assert.Equal(4, composed.Split("\n\n", 2)[0].Split('\n').Length);
    }

    [Fact]
    public void Puts_The_Body_After_A_Blank_Line()
    {
        var composed = SendmailMailAdapter.Compose(
            new Mail { Subject = "s", Text = "line one\nline two" },
            from: "a@example.com",
            to: "b@example.com");

        var parts = composed.Split("\n\n", 2);

        Assert.Equal(2, parts.Length);
        Assert.Equal("line one\nline two", parts[1]);
        Assert.Contains("Content-Type: text/plain; charset=utf-8", parts[0]);
    }
}

public class MailSecretTests
{
    [Fact]
    public void Redacts_The_Credentials_Mail_Carries()
    {
        // options are served to the web client and written to logs; a credential that is not marked is a credential
        // that leaves. these are the only two mail carries, and both must survive a redaction as nothing
        var options = new Options
        {
            Integrations = new Options.IntegrationsOptions
            {
                Mail = new Options.IntegrationsOptions.MailOptions
                {
                    From = "slskd@example.com",
                    Smtp = new Options.IntegrationsOptions.MailOptions.SmtpOptions
                    {
                        Host = "smtp.example.com",
                        Username = "user",
                        Password = "hunter2",
                    },
                    Brevo = new Options.IntegrationsOptions.MailOptions.BrevoOptions
                    {
                        ApiKey = "xkeysib-secret",
                    },
                },
            },
        };

        Redactor.Redact(options);

        Assert.DoesNotContain("hunter2", options.Integrations.Mail.Smtp.Password);
        Assert.DoesNotContain("xkeysib-secret", options.Integrations.Mail.Brevo.ApiKey);

        // and everything that is not a secret is left alone, or the redaction has eaten the configuration
        Assert.Equal("smtp.example.com", options.Integrations.Mail.Smtp.Host);
        Assert.Equal("user", options.Integrations.Mail.Smtp.Username);
        Assert.Equal("slskd@example.com", options.Integrations.Mail.From);
    }
}
