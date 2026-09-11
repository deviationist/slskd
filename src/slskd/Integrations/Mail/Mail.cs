// <copyright file="Mail.cs" company="JP Dillingham">
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

namespace slskd.Integrations.Mail;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
///     A message to be sent.
/// </summary>
/// <remarks>
///     Deliberately small, and deliberately carrying both a plain and an HTML body: the plain one is the message, and
///     the one that has to be readable. A sender composes this and does not know which adapter will carry it.
/// </remarks>
public record Mail
{
    /// <summary>
    ///     Gets the address to send to. Falls back to the configured default when not supplied.
    /// </summary>
    public string To { get; init; }

    /// <summary>
    ///     Gets the subject.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    ///     Gets the plain text body.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    ///     Gets the HTML body, if any.
    /// </summary>
    public string Html { get; init; }
}

/// <summary>
///     Sends mail.
/// </summary>
public interface IMailAdapter
{
    /// <summary>
    ///     Gets the name by which this adapter is selected in configuration.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Sends the specified <paramref name="mail"/>.
    /// </summary>
    /// <param name="mail">The mail to send.</param>
    /// <param name="from">The address to send from.</param>
    /// <param name="to">The address to send to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The operation context.</returns>
    Task SendAsync(Mail mail, string from, string to, CancellationToken cancellationToken = default);
}

/// <summary>
///     Thrown when mail cannot be sent.
/// </summary>
public class MailException : Exception
{
    public MailException(string message)
        : base(message)
    {
    }

    public MailException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
