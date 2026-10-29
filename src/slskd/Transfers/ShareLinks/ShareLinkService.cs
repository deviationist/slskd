// <copyright file="ShareLinkService.cs" company="JP Dillingham">
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

namespace slskd.Transfers.ShareLinks;

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using slskd.Files;
using slskd.Integrations.Mail;
using slskd.Transfers.Downloads;

/// <summary>
///     Thrown when a share link cannot be created or changed as asked; the message says why, for the person asking.
/// </summary>
public class ShareLinkException : Exception
{
    public ShareLinkException(string message)
        : base(message)
    {
    }
}

/// <summary>
///     What became of mailing a share to one address.
/// </summary>
/// <param name="Recipient">The address.</param>
/// <param name="AccessId">The link the address was sent, made for it or reused.</param>
/// <param name="Sent">Whether it was sent.</param>
/// <param name="Error">Why it was not, if it was not.</param>
public record ShareLinkMailResult(string Recipient, Guid AccessId, bool Sent, string Error);

/// <summary>
///     A share as opened by one of its links.
/// </summary>
/// <param name="Link">The share.</param>
/// <param name="Access">The link it was opened with.</param>
public record ShareLinkGrant(ShareLink Link, ShareLinkAccess Access);

/// <summary>
///     Creates, serves and expires share links.
/// </summary>
/// <remarks>
///     Expiry is a sweep rather than a timer per link: every minute, and once at startup, anything past its time is
///     deleted. A link's expiry lives in the database, so a restart -- or a week of the service being down -- cannot
///     lose one; the first sweep after it simply finds more to do.
/// </remarks>
public class ShareLinkService
{
    /// <summary>
    ///     The most files one link may hold.
    /// </summary>
    public const int MaximumFiles = 500;

    /// <summary>
    ///     The most addresses one link may be mailed to at once.
    /// </summary>
    public const int MaximumRecipients = 20;

    /// <summary>
    ///     How long a folder with no link has to have been left alone before the sweep treats it as left over. A
    ///     link's folder exists before its row does -- the copies are made first -- so one written to recently may be
    ///     a link being made. Measured by the folder's modification time, which every copy landing in it moves on, and
    ///     which unlike a creation time every filesystem keeps.
    /// </summary>
    private static readonly TimeSpan OrphanGrace = TimeSpan.FromHours(1);

    /// <summary>
    ///     Initializes a new instance of the <see cref="ShareLinkService"/> class.
    /// </summary>
    public ShareLinkService(
        IDbContextFactory<TransfersDbContext> contextFactory,
        IDownloadService downloadService,
        FileService fileService,
        MailService mailService,
        IOptionsMonitor<Options> optionsMonitor)
    {
        ContextFactory = contextFactory;
        Downloads = downloadService;
        Files = fileService;
        Mail = mailService;
        OptionsMonitor = optionsMonitor;

        Clock.EveryMinute += (_, _) => _ = SweepAsync();
        _ = Task.Run(SweepAsync);
    }

    /// <summary>
    ///     Gets a value indicating whether share links are enabled.
    /// </summary>
    public bool Enabled => LinkOptions.Enabled;

    /// <summary>
    ///     Gets a value indicating whether a link can be mailed.
    /// </summary>
    public bool CanMail => Mail.IsEnabled;

    private IDbContextFactory<TransfersDbContext> ContextFactory { get; }
    private IDownloadService Downloads { get; }
    private FileService Files { get; }
    private MailService Mail { get; }
    private IOptionsMonitor<Options> OptionsMonitor { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<ShareLinkService>();
    private SemaphoreSlim SweepGate { get; } = new(1, 1);
    private Options.TransfersOptions.ShareLinksOptions LinkOptions => OptionsMonitor.CurrentValue.Transfers.ShareLinks;
    private string Root => Path.GetFullPath(LinkOptions.Directory);

    /// <summary>
    ///     Creates a share serving copies of the specified downloads.
    /// </summary>
    /// <param name="transferIds">The downloads to share. Each must have finished successfully and still have its file.</param>
    /// <param name="days">How many days the share lasts.</param>
    /// <param name="title">What to call it; derived from the files when blank.</param>
    /// <param name="general">Whether to make a general link to it as well, rather than only the ones mailed later.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The share.</returns>
    /// <exception cref="ShareLinkException">Thrown when the share cannot be made as asked.</exception>
    public async Task<ShareLink> CreateAsync(IEnumerable<Guid> transferIds, int days, string title = null, bool general = true, CancellationToken cancellationToken = default)
    {
        var ids = (transferIds ?? []).Distinct().ToList();

        if (ids.Count == 0)
        {
            throw new ShareLinkException("No downloads were given to share");
        }

        if (ids.Count > MaximumFiles)
        {
            throw new ShareLinkException($"A link can hold at most {MaximumFiles} files");
        }

        var validity = ValidityFor(days);

        var downloads = ids.Select(id => Downloads.Find(t => t.Id == id)).ToList();

        for (var i = 0; i < ids.Count; i++)
        {
            var download = downloads[i];

            if (download is null)
            {
                throw new ShareLinkException($"Download {ids[i]} is not known to this instance");
            }

            if (!TransferStateCategories.Successful.Contains(download.State) || string.IsNullOrWhiteSpace(download.LocalFilename))
            {
                throw new ShareLinkException($"'{Path.GetFileName(download.Filename.Replace('\\', '/'))}' has not finished downloading, so there is nothing to share");
            }
        }

        // the same order, and the same names, an archive of these downloads would have
        downloads = downloads.OrderBy(d => d.Filename, StringComparer.OrdinalIgnoreCase).ToList();
        var names = ArchiveNaming.EntryNamesFor(downloads.Select(d => d.Filename));

        var link = new ShareLink
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
        };

        if (general)
        {
            link.Accesses.Add(NewAccess(link.Id, recipient: null));
        }

        link.ExpiresAt = link.CreatedAt.Add(validity);
        link.Title = string.IsNullOrWhiteSpace(title) ? TitleFor(names) : title.Trim();

        var folder = FolderFor(link.Id);

        try
        {
            for (var i = 0; i < downloads.Count; i++)
            {
                var path = Path.GetFullPath(Path.Combine(folder, names[i]));

                if (!IsInside(path, folder))
                {
                    throw new ShareLinkException($"'{names[i]}' does not make a safe filename");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));

                Stream source;

                try
                {
                    // through the file service, so that a path recorded from a filename a remote peer chose is checked
                    // for containment before it is opened, exactly as it is everywhere else
                    source = Files.OpenFile(downloads[i].LocalFilename);
                }
                catch (NotFoundException)
                {
                    throw new ShareLinkException($"'{names[i]}' is no longer on disk");
                }

                await using (source)
                await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
                {
                    await source.CopyToAsync(target, cancellationToken);
                }

                link.Files.Add(new ShareLinkFile
                {
                    Id = Guid.NewGuid(),
                    ShareLinkId = link.Id,
                    Position = i,
                    Name = names[i],
                    Path = path,
                    Size = new FileInfo(path).Length,
                    TransferId = downloads[i].Id,
                    Username = downloads[i].Username,
                });
            }

            using var context = ContextFactory.CreateDbContext();
            context.ShareLinks.Add(link);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // a link that did not come into being leaves nothing behind
            DeleteFolder(link.Id);
            throw;
        }

        Log.Information("Created share {Id} '{Title}' for {Count} file(s), expiring {ExpiresAt:u}", link.Id, link.Title, link.Files.Count, link.ExpiresAt);

        return link;
    }

    /// <summary>
    ///     Lists every link, newest first.
    /// </summary>
    /// <returns>The links.</returns>
    public async Task<List<ShareLink>> ListAsync()
    {
        using var context = ContextFactory.CreateDbContext();

        var links = await context.ShareLinks
            .AsNoTracking()
            .Include(l => l.Files)
            .Include(l => l.Accesses)
            .AsSplitQuery()
            .ToListAsync();

        links.ForEach(l => Ordered(l));

        return [.. links.OrderByDescending(l => l.CreatedAt)];
    }

    /// <summary>
    ///     Finds the link with the specified id.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <returns>The link, or null.</returns>
    public async Task<ShareLink> FindAsync(Guid id)
    {
        using var context = ContextFactory.CreateDbContext();
        return Ordered(await context.ShareLinks
            .AsNoTracking()
            .Include(l => l.Files)
            .Include(l => l.Accesses)
            .AsSplitQuery()
            .FirstOrDefaultAsync(l => l.Id == id));
    }

    /// <summary>
    ///     Finds the share the specified token opens, and the link it belongs to, if both are still open.
    /// </summary>
    /// <remarks>
    ///     An expired share is not found even before the sweep has deleted it: what decides whether a link works is
    ///     its expiry, not whether the sweep has been round yet. Nor is one opened by a revoked link, whatever the
    ///     share's other links are doing.
    /// </remarks>
    /// <param name="token">The token from the link's address.</param>
    /// <returns>The share and the link, or null.</returns>
    public async Task<ShareLinkGrant> FindByTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        using var context = ContextFactory.CreateDbContext();

        var access = await context.ShareLinkAccesses.AsNoTracking().FirstOrDefaultAsync(a => a.Token == token);

        if (access is null || access.RevokedAt is not null)
        {
            return null;
        }

        var link = await context.ShareLinks.AsNoTracking().Include(l => l.Files).FirstOrDefaultAsync(l => l.Id == access.ShareLinkId);

        return link is null || link.ExpiresAt <= DateTime.UtcNow ? null : new ShareLinkGrant(Ordered(link), access);
    }

    /// <summary>
    ///     Makes a general link to the specified share, unless it already has one that works.
    /// </summary>
    /// <remarks>
    ///     A general link that was revoked stays revoked: this makes a new one, with a new token, so whoever had the
    ///     old one still has nothing.
    /// </remarks>
    /// <param name="id">The share's id.</param>
    /// <returns>The share, or null if there is no such share.</returns>
    public async Task<ShareLink> CreateGeneralAccessAsync(Guid id)
    {
        using (var context = ContextFactory.CreateDbContext())
        {
            if (!await context.ShareLinks.AnyAsync(l => l.Id == id))
            {
                return null;
            }

            if (!await context.ShareLinkAccesses.AnyAsync(a => a.ShareLinkId == id && a.Recipient == null && a.RevokedAt == null))
            {
                context.ShareLinkAccesses.Add(NewAccess(id, recipient: null));
                await context.SaveChangesAsync();

                Log.Information("Made a general link to share {Id}", id);
            }
        }

        return await FindAsync(id);
    }

    /// <summary>
    ///     Revokes one link to a share, leaving the share and its other links as they are.
    /// </summary>
    /// <param name="id">The share's id.</param>
    /// <param name="accessId">The link's id.</param>
    /// <returns>The share, or null if there is no such link on it.</returns>
    public async Task<ShareLink> RevokeAccessAsync(Guid id, Guid accessId)
    {
        using (var context = ContextFactory.CreateDbContext())
        {
            var access = await context.ShareLinkAccesses.FirstOrDefaultAsync(a => a.Id == accessId && a.ShareLinkId == id);

            if (access is null)
            {
                return null;
            }

            if (access.RevokedAt is null)
            {
                access.RevokedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();

                Log.Information("Revoked the {Kind} link to share {Id}", access.IsGeneral ? "general" : $"{access.Recipient}'s", id);
            }
        }

        return await FindAsync(id);
    }

    /// <summary>
    ///     Sets the specified link to expire the specified number of days from now.
    /// </summary>
    /// <param name="id">The link's id.</param>
    /// <param name="days">How many days from now it should last.</param>
    /// <returns>The link, or null if there is no such link.</returns>
    public async Task<ShareLink> ExtendAsync(Guid id, int days)
    {
        var validity = ValidityFor(days);

        using var context = ContextFactory.CreateDbContext();

        var link = await context.ShareLinks.FirstOrDefaultAsync(l => l.Id == id);

        if (link is null)
        {
            return null;
        }

        link.ExpiresAt = DateTime.UtcNow.Add(validity);
        await context.SaveChangesAsync();

        Log.Information("Share link {Id} '{Title}' now expires {ExpiresAt:u}", link.Id, link.Title, link.ExpiresAt);

        return await FindAsync(id);
    }

    /// <summary>
    ///     Deletes the specified link and its files now.
    /// </summary>
    /// <param name="id">The link's id.</param>
    /// <returns>A value indicating whether there was such a link.</returns>
    public async Task<bool> RevokeAsync(Guid id)
    {
        var deleted = await DeleteAsync(id);

        if (deleted)
        {
            Log.Information("Revoked share link {Id}", id);
        }

        return deleted;
    }

    /// <summary>
    ///     Counts a download through the specified link.
    /// </summary>
    /// <param name="accessId">The link's id.</param>
    /// <returns>The operation context.</returns>
    public async Task RecordDownloadAsync(Guid accessId)
    {
        using var context = ContextFactory.CreateDbContext();

        var now = DateTime.UtcNow;

        await context.ShareLinkAccesses
            .Where(a => a.Id == accessId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.DownloadCount, l => l.DownloadCount + 1)
                .SetProperty(l => l.LastDownloadedAt, now));
    }

    /// <summary>
    ///     Opens the copy behind the specified file of a link, if it is still where the link left it.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>
    ///     The full path, or null if the copy is gone, is not inside the link's own folder, or is reached through a
    ///     symlink.
    /// </returns>
    public string PathOf(ShareLinkFile file)
    {
        if (file is null)
        {
            return null;
        }

        // the recorded path is ours, written when the copy was made -- checked anyway, since serving it is the one
        // thing here a stranger can cause, and the share directory may have been reconfigured since
        var path = Path.GetFullPath(file.Path);
        var folder = FolderFor(file.ShareLinkId);

        return IsInside(path, folder) && File.Exists(path) && !IsLinkedBelow(path, folder) ? path : null;
    }

    /// <summary>
    ///     Mails the specified share to each of the specified recipients, each with a link of their own.
    /// </summary>
    /// <remarks>
    ///     An address that already has a working link is sent that one again rather than a new one, so mailing
    ///     someone twice does not leave them with two links to revoke. An address whose link was revoked gets a new
    ///     one: sending is the operator deciding to share with them again.
    /// </remarks>
    /// <param name="id">The share's id.</param>
    /// <param name="urlFor">Makes a link's address from its token.</param>
    /// <param name="recipients">The addresses.</param>
    /// <returns>What became of each, or null if there is no such share.</returns>
    public async Task<List<ShareLinkMailResult>> SendAsync(Guid id, Func<string, string> urlFor, IEnumerable<string> recipients)
    {
        var link = await FindAsync(id);

        if (link is null)
        {
            return null;
        }

        var results = new List<ShareLinkMailResult>();

        foreach (var recipient in recipients.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var context = ContextFactory.CreateDbContext();

            var access = (await context.ShareLinkAccesses
                    .Where(a => a.ShareLinkId == id && a.Recipient != null && a.RevokedAt == null)
                    .ToListAsync())
                .FirstOrDefault(a => string.Equals(a.Recipient, recipient, StringComparison.OrdinalIgnoreCase));

            if (access is null)
            {
                access = NewAccess(id, recipient);
                context.ShareLinkAccesses.Add(access);
            }

            try
            {
                await Mail.SendAsync(ShareLinkMail.Compose(link, urlFor(access.Token), OptionsMonitor.CurrentValue) with { To = recipient });

                access.SentAt = DateTime.UtcNow;
                access.MailError = null;
                await RememberAsync(context, recipient, access.SentAt.Value);
                results.Add(new ShareLinkMailResult(recipient, access.Id, true, null));
            }
            catch (Exception ex)
            {
                // per address: one that fails does not stop the others, and each is reported -- and recorded on its
                // link, so the list says which of them never arrived
                Log.Warning("Failed to mail share {Id} to {Recipient}: {Message}", id, recipient, ex.Message);

                access.MailError = ex.Message;
                results.Add(new ShareLinkMailResult(recipient, access.Id, false, ex.Message));
            }

            await context.SaveChangesAsync();
        }

        return results;
    }

    /// <summary>
    ///     Lists the addresses shares have been mailed to, the most recently mailed first.
    /// </summary>
    /// <returns>The addresses.</returns>
    public async Task<List<ShareLinkRecipient>> ListRecipientsAsync()
    {
        using var context = ContextFactory.CreateDbContext();

        return await context.ShareLinkRecipients
            .AsNoTracking()
            .OrderByDescending(r => r.LastSentAt)
            .ToListAsync();
    }

    /// <summary>
    ///     Forgets an address, so that it is no longer suggested. Any link it has keeps working.
    /// </summary>
    /// <param name="address">The address, in any case.</param>
    /// <returns>Whether it was remembered.</returns>
    public async Task<bool> ForgetRecipientAsync(string address)
    {
        using var context = ContextFactory.CreateDbContext();

        var recipient = await context.ShareLinkRecipients.FindAsync(KeyOf(address));

        if (recipient is null)
        {
            return false;
        }

        context.ShareLinkRecipients.Remove(recipient);
        await context.SaveChangesAsync();

        Log.Information("Forgot share link recipient {Recipient}", recipient.Address);

        return true;
    }

    /// <summary>
    ///     Deletes every link past its expiry, and any folder in the share directory that no link owns.
    /// </summary>
    /// <returns>The operation context.</returns>
    public async Task SweepAsync()
    {
        // one at a time. waited for rather than skipped: a sweep takes milliseconds, and one asked for while another
        // runs should still happen, since what it is asked to find may be newer than what the other looked at
        await SweepGate.WaitAsync();

        try
        {
            var now = DateTime.UtcNow;

            List<Guid> expired;

            using (var context = ContextFactory.CreateDbContext())
            {
                expired = await context.ShareLinks
                    .Where(l => l.ExpiresAt <= now)
                    .Select(l => l.Id)
                    .ToListAsync();
            }

            foreach (var id in expired)
            {
                await DeleteAsync(id);
                Log.Information("Share link {Id} expired; it and its files are deleted", id);
            }

            DeleteOrphanedFolders();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to sweep share links: {Message}", ex.Message);
        }
        finally
        {
            SweepGate.Release();
        }
    }

    /// <summary>
    ///     Returns the title a link of files with the specified entry names gets when none is given.
    /// </summary>
    /// <param name="names">The files' entry names.</param>
    /// <returns>The title.</returns>
    public static string TitleFor(IReadOnlyList<string> names)
    {
        if (names.Count == 1)
        {
            return Path.GetFileName(names[0]);
        }

        // an entry name is the file's folder and its name, so the part before the separator is the folder
        var folders = names
            .Select(n => n.Contains('/') ? n[..n.LastIndexOf('/')] : string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return folders.Count == 1 && folders[0].Length > 0 ? folders[0] : $"{names.Count} files";
    }

    private static string KeyOf(string address) => (address ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    ///     Notes that a mail went to an address, in the context the send is saved with.
    /// </summary>
    private static async Task RememberAsync(TransfersDbContext context, string address, DateTime sentAt)
    {
        var known = await context.ShareLinkRecipients.FindAsync(KeyOf(address));

        if (known is null)
        {
            context.ShareLinkRecipients.Add(new ShareLinkRecipient
            {
                Key = KeyOf(address),
                Address = address,
                FirstSentAt = sentAt,
                LastSentAt = sentAt,
                SendCount = 1,
            });

            return;
        }

        known.Address = address;
        known.LastSentAt = sentAt;
        known.SendCount++;
    }

    private static ShareLinkAccess NewAccess(Guid id, string recipient) => new()
    {
        Id = Guid.NewGuid(),
        ShareLinkId = id,
        Token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)),
        Recipient = recipient,
        CreatedAt = DateTime.UtcNow,
    };

    private static bool IsInside(string path, string folder)
    {
        var root = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Returns whether the specified <paramref name="path"/>, or any directory between it and
    ///     <paramref name="folder"/> (the folder included), is a symlink.
    /// </summary>
    /// <remarks>
    ///     Containment is checked on the path as written, and a symlink keeps that path inside while pointing anywhere
    ///     at all. Nothing here ever makes one -- every copy is a new file in a new folder -- so one found is something
    ///     put there by whoever else can write to the share directory, and following it would serve whatever it names.
    ///     The share directory itself, and what is above it, are the operator's to arrange, so they are not checked.
    /// </remarks>
    private static bool IsLinkedBelow(string path, string folder)
    {
        FileSystemInfo entry = new FileInfo(path);
        var stop = Path.TrimEndingDirectorySeparator(folder);

        while (entry is not null)
        {
            if (entry.LinkTarget is not null)
            {
                return true;
            }

            if (string.Equals(Path.TrimEndingDirectorySeparator(entry.FullName), stop, StringComparison.Ordinal))
            {
                return false;
            }

            entry = entry is FileInfo info ? info.Directory : ((DirectoryInfo)entry).Parent;
        }

        // ran out of parents without reaching the folder, which IsInside rules out; refuse rather than assume
        return true;
    }

    private static ShareLink Ordered(ShareLink link)
    {
        if (link is not null)
        {
            link.Files = [.. link.Files.OrderBy(f => f.Position)];

            // the general link first, then everyone it was mailed to in the order they were added
            link.Accesses = [.. link.Accesses.OrderBy(a => a.IsGeneral ? 0 : 1).ThenBy(a => a.CreatedAt)];
        }

        return link;
    }

    private TimeSpan ValidityFor(int days)
    {
        var max = LinkOptions.MaxDays;

        if (days < 1 || days > max)
        {
            throw new ShareLinkException($"A link can last from 1 to {max} days");
        }

        return TimeSpan.FromDays(days);
    }

    private string FolderFor(Guid id) => Path.Combine(Root, id.ToString("N"));

    private async Task<bool> DeleteAsync(Guid id)
    {
        int deleted;

        using (var context = ContextFactory.CreateDbContext())
        {
            // both tables by hand rather than trusting the foreign key's cascade, which SQLite applies only with
            // foreign keys switched on for the connection
            await context.ShareLinkFiles.Where(f => f.ShareLinkId == id).ExecuteDeleteAsync();
            await context.ShareLinkAccesses.Where(a => a.ShareLinkId == id).ExecuteDeleteAsync();
            deleted = await context.ShareLinks.Where(l => l.Id == id).ExecuteDeleteAsync();
        }

        // after the rows, so a link is never served with its files half gone. a folder that fails to go now is an
        // orphan the next sweep tries again
        DeleteFolder(id);

        return deleted > 0;
    }

    private void DeleteFolder(Guid id)
    {
        var folder = FolderFor(id);

        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Log.Warning("Failed to delete share link folder {Folder}: {Message}", folder, ex.Message);
        }
    }

    private void DeleteOrphanedFolders()
    {
        var root = Root;

        if (!Directory.Exists(root))
        {
            return;
        }

        HashSet<string> owned;

        using (var context = ContextFactory.CreateDbContext())
        {
            owned = context.ShareLinks.Select(l => l.Id).AsEnumerable().Select(id => id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(folder);

            // only the folders this service makes -- named for a link id -- are ever touched. anything else in the
            // directory is someone else's, and the directory may well have been pointed somewhere shared
            if (!Guid.TryParseExact(name, "N", out var id) || owned.Contains(name))
            {
                continue;
            }

            if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder) < OrphanGrace)
            {
                continue;
            }

            Log.Information("Deleting share link folder {Folder}, which no link owns", folder);
            DeleteFolder(id);
        }
    }
}
