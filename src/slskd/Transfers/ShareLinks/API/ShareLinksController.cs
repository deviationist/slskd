// <copyright file="ShareLinksController.cs" company="JP Dillingham">
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

namespace slskd.Transfers.ShareLinks.API;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Serilog;

/// <summary>
///     A share link, as asked for.
/// </summary>
public class ShareLinkRequest
{
    /// <summary>
    ///     Gets or sets the downloads to share.
    /// </summary>
    [Required]
    public List<Guid> TransferIds { get; set; }

    /// <summary>
    ///     Gets or sets how many days the link lasts. Defaults to the configured number.
    /// </summary>
    public int? Days { get; set; }

    /// <summary>
    ///     Gets or sets what the link is called. Derived from the files when blank.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    ///     Gets or sets the addresses to mail it to, if any. Each gets a link of its own.
    /// </summary>
    public List<string> Recipients { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether to make a general link too, to hand out by other means.
    /// </summary>
    public bool General { get; set; } = true;
}

/// <summary>
///     A change to a share link's expiry.
/// </summary>
public class ShareLinkExtension
{
    /// <summary>
    ///     Gets or sets how many days from now the link should last.
    /// </summary>
    [Required]
    public int Days { get; set; }
}

/// <summary>
///     Addresses to mail a share link to.
/// </summary>
public class ShareLinkMailRequest
{
    /// <summary>
    ///     Gets or sets the addresses.
    /// </summary>
    [Required]
    public List<string> Recipients { get; set; }
}

/// <summary>
///     A share as the operator sees it: everything, including every link to it and its address.
/// </summary>
public record ShareLinkResponse
{
    public Guid Id { get; init; }
    public string Title { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public long Size { get; init; }
    public List<ShareLinkFile> Files { get; init; }

    /// <summary>
    ///     Gets the address of the general link, or null when there is none that works.
    /// </summary>
    public string Url { get; init; }

    /// <summary>
    ///     Gets every link to the share, revoked ones included, the general link first.
    /// </summary>
    public List<ShareLinkAccessResponse> Accesses { get; init; }

    /// <summary>
    ///     Gets the downloads through every link together.
    /// </summary>
    public int DownloadCount { get; init; }

    /// <summary>
    ///     Gets what became of mailing it, when this answers a request that asked for that.
    /// </summary>
    public List<ShareLinkMailResult> Mail { get; init; }
}

/// <summary>
///     One link to a share, as the operator sees it.
/// </summary>
public record ShareLinkAccessResponse
{
    public Guid Id { get; init; }

    /// <summary>
    ///     Gets the address the link was made for, or null for the general link.
    /// </summary>
    public string Recipient { get; init; }

    public string Url { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? SentAt { get; init; }
    public string MailError { get; init; }
    public DateTime? RevokedAt { get; init; }
    public int DownloadCount { get; init; }
    public DateTime? LastDownloadedAt { get; init; }
}

/// <summary>
///     A share link as the person it was sent to sees it: what it holds and until when, and nothing else.
/// </summary>
public record PublicShareLinkResponse
{
    public string Title { get; init; }
    public DateTime ExpiresAt { get; init; }
    public long Size { get; init; }
    public List<PublicShareLinkFile> Files { get; init; }
}

/// <summary>
///     A file in a share link, as the person it was sent to sees it.
/// </summary>
public record PublicShareLinkFile(int Position, string Name, long Size);

/// <summary>
///     Share links: creating and managing them, and the public side a link opens.
/// </summary>
/// <remarks>
///     The public routes -- the page at /l/{token} and everything under /api/v0/l/{token} -- answer without a login,
///     and are the only ones that do; publishing share links means exposing those two prefixes and nothing else.
///     Each refuses to say anything about a token it does not recognise, an expired one included, beyond a 404.
/// </remarks>
[ApiVersion("0")]
[ApiController]
public class ShareLinksController : ControllerBase
{
    /// <summary>
    ///     The page a link opens, in the web content.
    /// </summary>
    public const string PageFileName = "share-link.html";

    public ShareLinksController(ShareLinkService shareLinkService, IOptionsSnapshot<Options> optionsSnapshot)
    {
        Links = shareLinkService;
        OptionsSnapshot = optionsSnapshot;
    }

    private static FileExtensionContentTypeProvider ContentTypeProvider { get; } = new()
    {
        Mappings =
        {
            [".flac"] = "audio/flac",
            [".opus"] = "audio/ogg",
        },
    };

    private ShareLinkService Links { get; }
    private IOptionsSnapshot<Options> OptionsSnapshot { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<ShareLinksController>();

    /// <summary>
    ///     Lists every share link.
    /// </summary>
    /// <returns>The links.</returns>
    /// <response code="200">The request completed successfully.</response>
    [HttpGet("api/v{version:apiVersion}/links")]
    [Authorize(Policy = AuthPolicy.Any)]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<ShareLinkResponse>), 200)]
    public async Task<IActionResult> List()
    {
        var links = await Links.ListAsync();
        return Ok(links.Select(l => ToResponse(l)).ToList());
    }

    /// <summary>
    ///     Lists the addresses shares have been mailed to, for the recipients field to suggest.
    /// </summary>
    /// <returns>The addresses, the most recently mailed first.</returns>
    /// <response code="200">The request completed successfully.</response>
    [HttpGet("api/v{version:apiVersion}/links/recipients")]
    [Authorize(Policy = AuthPolicy.Any)]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<ShareLinkRecipient>), 200)]
    public async Task<IActionResult> ListRecipients()
    {
        return Ok(await Links.ListRecipientsAsync());
    }

    /// <summary>
    ///     Forgets an address, so that it is no longer suggested. A link already mailed to it keeps working.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The operation context.</returns>
    /// <response code="204">The address is forgotten.</response>
    /// <response code="404">The address was not remembered.</response>
    [HttpDelete("api/v{version:apiVersion}/links/recipients/{address}")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ForgetRecipient([FromRoute] string address)
    {
        return await Links.ForgetRecipientAsync(address) ? NoContent() : NotFound();
    }

    /// <summary>
    ///     Creates a share link, and mails it if asked to.
    /// </summary>
    /// <param name="request">The link.</param>
    /// <returns>The link, and what became of mailing it.</returns>
    /// <response code="200">The link was created. Mail is reported per address, and failing to send is not an error.</response>
    /// <response code="400">The link cannot be made as asked; the body says why.</response>
    /// <response code="403">Share links are disabled.</response>
    [HttpPost("api/v{version:apiVersion}/links")]
    [Authorize(Policy = AuthPolicy.Any)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ShareLinkResponse), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Create([FromBody] ShareLinkRequest request)
    {
        if (!Links.Enabled)
        {
            return Forbid();
        }

        if (!TryReadRecipients(request.Recipients, out var recipients, out var problem))
        {
            return BadRequest(problem);
        }

        ShareLink link;

        if (!request.General && recipients.Count == 0)
        {
            return BadRequest("A share needs a general link, an address to mail it to, or both");
        }

        try
        {
            link = await Links.CreateAsync(
                request.TransferIds,
                request.Days ?? OptionsSnapshot.Value.Transfers.ShareLinks.DefaultDays,
                request.Title,
                request.General,
                HttpContext.RequestAborted);
        }
        catch (ShareLinkException ex)
        {
            return BadRequest(ex.Message);
        }

        var mail = recipients.Count > 0 ? await Links.SendAsync(link.Id, UrlFor, recipients) : null;

        return Ok(ToResponse(mail is null ? link : await Links.FindAsync(link.Id), mail));
    }

    /// <summary>
    ///     Sets a share link to expire a number of days from now.
    /// </summary>
    /// <param name="id">The link's id.</param>
    /// <param name="extension">The new validity.</param>
    /// <returns>The link.</returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="400">The number of days is out of range.</response>
    /// <response code="404">There is no such link.</response>
    [HttpPut("api/v{version:apiVersion}/links/{id}")]
    [Authorize(Policy = AuthPolicy.Any)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ShareLinkResponse), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Extend([FromRoute] Guid id, [FromBody] ShareLinkExtension extension)
    {
        try
        {
            var link = await Links.ExtendAsync(id, extension.Days);
            return link is null ? NotFound() : Ok(ToResponse(link));
        }
        catch (ShareLinkException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    ///     Mails a share to more addresses, each with a link of its own -- or the one it already has.
    /// </summary>
    /// <param name="id">The share's id.</param>
    /// <param name="request">The addresses.</param>
    /// <returns>The share, with what became of each address.</returns>
    /// <response code="200">Mail is reported per address, and failing to send is not an error.</response>
    /// <response code="400">An address is not one.</response>
    /// <response code="404">There is no such link.</response>
    [HttpPost("api/v{version:apiVersion}/links/{id}/mail")]
    [Authorize(Policy = AuthPolicy.Any)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ShareLinkResponse), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Mail([FromRoute] Guid id, [FromBody] ShareLinkMailRequest request)
    {
        if (!TryReadRecipients(request.Recipients, out var recipients, out var problem) || recipients.Count == 0)
        {
            return BadRequest(problem ?? "No address was given");
        }

        var results = await Links.SendAsync(id, UrlFor, recipients);

        return results is null ? NotFound() : Ok(ToResponse(await Links.FindAsync(id), results));
    }

    /// <summary>
    ///     Makes a general link to a share, unless it has one that works. A revoked one is replaced, never revived.
    /// </summary>
    /// <param name="id">The share's id.</param>
    /// <returns>The share.</returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="404">There is no such share.</response>
    [HttpPost("api/v{version:apiVersion}/links/{id}/general")]
    [Authorize(Policy = AuthPolicy.Any)]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ShareLinkResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> CreateGeneral([FromRoute] Guid id)
    {
        var link = await Links.CreateGeneralAccessAsync(id);
        return link is null ? NotFound() : Ok(ToResponse(link));
    }

    /// <summary>
    ///     Revokes one link to a share. The share, and every other link to it, carry on.
    /// </summary>
    /// <param name="id">The share's id.</param>
    /// <param name="accessId">The link's id.</param>
    /// <returns>The share.</returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="404">There is no such link on that share.</response>
    [HttpDelete("api/v{version:apiVersion}/links/{id}/accesses/{accessId}")]
    [Authorize(Policy = AuthPolicy.Any)]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ShareLinkResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RevokeAccess([FromRoute] Guid id, [FromRoute] Guid accessId)
    {
        var link = await Links.RevokeAccessAsync(id, accessId);
        return link is null ? NotFound() : Ok(ToResponse(link));
    }

    /// <summary>
    ///     Deletes a share link and its files now.
    /// </summary>
    /// <param name="id">The link's id.</param>
    /// <returns>The operation context.</returns>
    /// <response code="204">The link and its files are gone.</response>
    /// <response code="404">There is no such link.</response>
    [HttpDelete("api/v{version:apiVersion}/links/{id}")]
    [Authorize(Policy = AuthPolicy.Any)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Revoke([FromRoute] Guid id)
    {
        return await Links.RevokeAsync(id) ? NoContent() : NotFound();
    }

    /// <summary>
    ///     Gets the page a share link opens.
    /// </summary>
    /// <param name="token">The link's token.</param>
    /// <returns>The page.</returns>
    [HttpGet("l/{token}")]
    [AllowAnonymous]
    [ApiVersionNeutral] // a page, not an API: its address carries no version, and versioning would refuse it
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Page([FromRoute] string token)
    {
        if (!Links.Enabled)
        {
            return NotFound();
        }

        var page = Path.Combine(AppContext.BaseDirectory, OptionsSnapshot.Value.Web.ContentPath, PageFileName);

        if (!System.IO.File.Exists(page))
        {
            return NotFound();
        }

        // read rather than served as a file, so that the policy is made from the very text that goes out
        var html = await System.IO.File.ReadAllTextAsync(page);

        // the page is the same for every link -- it asks the API about the one in its address -- so a bad token gets
        // the page too, and the page says the link does not work. whether a token is good is told only by the API
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.ContentSecurityPolicy = ShareLinkPagePolicy.For(html);
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>
    ///     Gets what a share link holds.
    /// </summary>
    /// <param name="token">The link's token.</param>
    /// <returns>The link's title, expiry and files.</returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="404">The link does not exist, or no longer does.</response>
    [HttpGet("api/v{version:apiVersion}/l/{token}")]
    [AllowAnonymous]
    [Produces("application/json")]
    [ProducesResponseType(typeof(PublicShareLinkResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Open([FromRoute] string token)
    {
        var link = (await FindOpenAsync(token))?.Link;

        if (link is null)
        {
            return NotFound();
        }

        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

        return Ok(new PublicShareLinkResponse
        {
            Title = link.Title,
            ExpiresAt = link.ExpiresAt,
            Size = link.Files.Sum(f => f.Size),
            Files = [.. link.Files.Select(f => new PublicShareLinkFile(f.Position, f.Name, f.Size))],
        });
    }

    /// <summary>
    ///     Downloads one file of a share link.
    /// </summary>
    /// <param name="token">The link's token.</param>
    /// <param name="position">The file's position in the link.</param>
    /// <returns>The file.</returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="206">Part of the file, as asked for.</response>
    /// <response code="404">The link or the file does not exist, or no longer does.</response>
    [HttpGet("api/v{version:apiVersion}/l/{token}/files/{position:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(206)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetFile([FromRoute] string token, [FromRoute] int position)
    {
        var grant = await FindOpenAsync(token);
        var file = grant?.Link.Files.FirstOrDefault(f => f.Position == position);
        var path = Links.PathOf(file);

        if (path is null)
        {
            return NotFound();
        }

        // a resumed download, or a player seeking, asks for the rest of a file it already started; only a request
        // from the beginning is a download worth counting
        if (IsFromTheStart(Request))
        {
            await Links.RecordDownloadAsync(grant.Access.Id);
        }

        if (!ContentTypeProvider.TryGetContentType(path, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return PhysicalFile(path, contentType, fileDownloadName: Path.GetFileName(file.Name), enableRangeProcessing: true);
    }

    /// <summary>
    ///     Downloads every file of a share link as one zip.
    /// </summary>
    /// <param name="token">The link's token.</param>
    /// <returns>The archive.</returns>
    /// <response code="200">The request completed successfully.</response>
    /// <response code="404">The link does not exist, or no longer does.</response>
    [HttpGet("api/v{version:apiVersion}/l/{token}/archive")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetArchive([FromRoute] string token)
    {
        var grant = await FindOpenAsync(token);

        if (grant is null)
        {
            return NotFound();
        }

        var link = grant.Link;

        await Links.RecordDownloadAsync(grant.Access.Id);

        var sources = link.Files
            .Select(f => new ArchiveSource(f.Name, () =>
            {
                var path = Links.PathOf(f);
                return path is null
                    ? (null, "the file is no longer on disk")
                    : (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024 * 1024, useAsync: true), null);
            }))
            .ToList();

        Log.Information("Streaming share link {Id} '{Title}' as an archive of {Count} file(s)", link.Id, link.Title, sources.Count);

        await ArchiveStreaming.WriteAsync(HttpContext, $"{ArchiveTitle(link.Title)}.zip", sources);

        return new EmptyResult();
    }

    private static bool IsFromTheStart(HttpRequest request)
    {
        var range = request.Headers.Range.ToString();
        return string.IsNullOrEmpty(range) || range.StartsWith("bytes=0-", StringComparison.OrdinalIgnoreCase);
    }

    private static string ArchiveTitle(string title)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var name = new string([.. title.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c)]).Trim().Trim('.');

        return string.IsNullOrEmpty(name) ? "shared" : name;
    }

    private static bool TryReadRecipients(IEnumerable<string> given, out List<string> recipients, out string problem)
    {
        recipients = [.. (given ?? [])
            .SelectMany(r => (r ?? string.Empty).Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        problem = null;

        if (recipients.Count > ShareLinkService.MaximumRecipients)
        {
            problem = $"A link can be mailed to at most {ShareLinkService.MaximumRecipients} addresses at once";
            return false;
        }

        var invalid = recipients.Where(r => !MailAddress.TryCreate(r, out var address) || address.Address != r).ToList();

        if (invalid.Count > 0)
        {
            problem = $"Not an email address: {string.Join(", ", invalid)}";
            return false;
        }

        return true;
    }

    private async Task<ShareLinkGrant> FindOpenAsync(string token)
    {
        // a disabled feature answers exactly as an unknown token does
        return Links.Enabled ? await Links.FindByTokenAsync(token) : null;
    }

    private string UrlFor(string token)
    {
        var configured = OptionsSnapshot.Value.Transfers.ShareLinks.BaseUrl;

        var root = string.IsNullOrWhiteSpace(configured)
            ? $"{Request.Scheme}://{Request.Host}{Request.PathBase}"
            : configured;

        return $"{root.TrimEnd('/')}/l/{token}";
    }

    private ShareLinkResponse ToResponse(ShareLink link, List<ShareLinkMailResult> mail = null)
    {
        var accesses = link.Accesses.Select(a => new ShareLinkAccessResponse
        {
            Id = a.Id,
            Recipient = a.Recipient,
            Url = UrlFor(a.Token),
            CreatedAt = a.CreatedAt,
            SentAt = a.SentAt,
            MailError = a.MailError,
            RevokedAt = a.RevokedAt,
            DownloadCount = a.DownloadCount,
            LastDownloadedAt = a.LastDownloadedAt,
        }).ToList();

        var general = link.Accesses.FirstOrDefault(a => a.IsGeneral && a.RevokedAt is null);

        return new ShareLinkResponse
        {
            Id = link.Id,
            Title = link.Title,
            CreatedAt = link.CreatedAt,
            ExpiresAt = link.ExpiresAt,
            Size = link.Files.Sum(f => f.Size),
            Files = link.Files,
            Url = general is null ? null : UrlFor(general.Token),
            Accesses = accesses,
            DownloadCount = link.Accesses.Sum(a => a.DownloadCount),
            Mail = mail,
        };
    }
}
