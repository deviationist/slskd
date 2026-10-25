// <copyright file="OfflinePageTests.cs" company="JP Dillingham">
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

namespace slskd.Tests.Unit.Core;

using System.Net;
using Xunit;

public class OfflinePageTests
{
    private const string Template = "<p>{{message}}</p>";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public void A_Blank_Message_Gives_The_Generic_One(string message)
    {
        Assert.Equal($"<p>{WebUtility.HtmlEncode(OfflinePage.DefaultMessage)}</p>", OfflinePage.Render(Template, message));
    }

    [Fact]
    public void The_Configured_Message_Replaces_The_Placeholder()
    {
        Assert.Equal("<p>Join the home network first.</p>", OfflinePage.Render(Template, "  Join the home network first. "));
    }

    [Fact]
    public void Markup_In_The_Message_Is_Shown_Not_Run()
    {
        var page = OfflinePage.Render(Template, "<script>alert(1)</script> & \"quotes\"");

        Assert.DoesNotContain("<script>", page);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; &quot;quotes&quot;", page);
    }

    [Fact]
    public void Line_Breaks_Are_Kept_And_Normalised()
    {
        Assert.Equal("<p>one\ntwo\nthree</p>", OfflinePage.Render(Template, "one\r\ntwo\nthree"));
    }
}
