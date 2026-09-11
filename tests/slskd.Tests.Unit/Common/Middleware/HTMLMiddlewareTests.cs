// <copyright file="HTMLMiddlewareTests.cs" company="JP Dillingham">
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

namespace slskd.Tests.Unit.Common.Middleware;

using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
///     Tests for the middleware that rewrites and injects into HTML responses.
/// </summary>
/// <remarks>
///     Both engage on the <em>request's</em> Accept header, and a browser sends text/html on any navigation --
///     including one that downloads a file. What they must not do is read a response that is not HTML into a string:
///     every byte that is not valid UTF-8 becomes U+FFFD, which corrupts the body and inflates it by roughly 80%.
/// </remarks>
public class HTMLMiddlewareTests
{
    private const string BrowserAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

    /// <summary>
    ///     Bytes that are not valid UTF-8, as any compressed or encoded file is full of.
    /// </summary>
    private static byte[] BinaryBody => [0x50, 0x4B, 0x03, 0x04, 0xCA, 0x99, 0x2B, 0x5D, 0xFF, 0xFE, 0x80, 0x00];

    [Fact]
    public async Task Injection_Replays_A_Binary_Response_Byte_For_Byte()
    {
        var (context, body) = await InvokeInjection(contentType: "application/zip", payload: BinaryBody);

        Assert.Equal(BinaryBody, body);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task Rewrite_Replays_A_Binary_Response_Byte_For_Byte()
    {
        var (_, body) = await InvokeRewrite(contentType: "application/zip", payload: BinaryBody);

        Assert.Equal(BinaryBody, body);
    }

    [Fact]
    public async Task Injection_Does_Not_Inflate_A_Binary_Response()
    {
        // the shape of the bug this covers: a text round-trip replaces each invalid byte with a three-byte
        // replacement character, so a corrupted body is *longer* than the one that was written
        var (_, body) = await InvokeInjection(contentType: "application/zip", payload: BinaryBody);

        Assert.Equal(BinaryBody.Length, body.Length);
        Assert.DoesNotContain(Encoding.UTF8.GetBytes("�"), Enumerable.Range(0, body.Length - 2).Select(i => body.Skip(i).Take(3).ToArray()), new ByteArrayComparer());
    }

    [Fact]
    public async Task Injection_Appends_To_An_HTML_Response()
    {
        var html = Encoding.UTF8.GetBytes("<html></html>");
        var (_, body) = await InvokeInjection(contentType: "text/html", payload: html);

        Assert.Equal("<html></html><script>injected</script>", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public async Task Injection_Leaves_An_Excluded_Route_Alone()
    {
        // the excluded routes have to reach the middleware for this to hold; a registration that drops them
        // silently includes everything the caller asked to exclude
        var (_, body) = await InvokeInjection(contentType: "text/html", payload: Encoding.UTF8.GetBytes("<html></html>"), path: "/api/v0/thing");

        Assert.Equal("<html></html>", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public async Task Injection_Ignores_A_Request_That_Did_Not_Ask_For_HTML()
    {
        var (_, body) = await InvokeInjection(contentType: "text/html", payload: Encoding.UTF8.GetBytes("<html></html>"), accept: "*/*");

        Assert.Equal("<html></html>", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public async Task Injection_Registered_Through_The_Builder_Honours_Excluded_Routes()
    {
        // through the extension method rather than the constructor, because that is where the routes were being
        // dropped: the middleware honoured whatever it was given, and it was silently given nothing
        var context = Context("/api/v0/thing", BrowserAccept, out var sink);

        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UseHTMLInjection("<script>injected</script>", ["/api", "/swagger"]);
        app.Run(Writer("text/html", Encoding.UTF8.GetBytes("<html></html>")));

        await app.Build().Invoke(context);

        Assert.Equal("<html></html>", Encoding.UTF8.GetString(sink.ToArray()));
    }

    private static async Task<(HttpContext Context, byte[] Body)> InvokeInjection(
        string contentType,
        byte[] payload,
        string path = "/index.html",
        string accept = BrowserAccept)
    {
        var context = Context(path, accept, out var sink);

        var middleware = new HTMLInjectionMiddleware(
            next: Writer(contentType, payload),
            html: "<script>injected</script>",
            excludedRoutes: ["/api", "/swagger"]);

        await middleware.InvokeAsync(context);

        return (context, sink.ToArray());
    }

    private static async Task<(HttpContext Context, byte[] Body)> InvokeRewrite(
        string contentType,
        byte[] payload,
        string path = "/index.html",
        string accept = BrowserAccept)
    {
        var context = Context(path, accept, out var sink);

        var middleware = new HTMLRewriteMiddleware(
            next: Writer(contentType, payload),
            pattern: "((\\.)?\\/static)",
            replacement: "/static");

        await middleware.InvokeAsync(context);

        return (context, sink.ToArray());
    }

    private static HttpContext Context(string path, string accept, out MemoryStream sink)
    {
        sink = new MemoryStream();

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Request.Headers.Accept = accept;
        context.Response.Body = sink;

        return context;
    }

    private static RequestDelegate Writer(string contentType, byte[] payload)
        => async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = contentType;
            await ctx.Response.Body.WriteAsync(payload);
        };

    private sealed class ByteArrayComparer : System.Collections.Generic.IEqualityComparer<byte[]>
    {
        public bool Equals(byte[] x, byte[] y) => x.SequenceEqual(y);

        public int GetHashCode(byte[] obj) => obj.Length;
    }
}
