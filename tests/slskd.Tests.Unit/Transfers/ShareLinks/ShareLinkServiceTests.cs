// <copyright file="ShareLinkServiceTests.cs" company="JP Dillingham">
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

namespace slskd.Tests.Unit.Transfers.ShareLinks;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using slskd.Files;
using slskd.Integrations.Mail;
using slskd.Transfers;
using slskd.Transfers.Downloads;
using slskd.Transfers.ShareLinks;
using TransferDirection = Soulseek.TransferDirection;
using TransferStates = Soulseek.TransferStates;
using Xunit;
using Transfer = slskd.Transfers.Transfer;

public sealed class ShareLinkServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"sharelinks_{Guid.NewGuid():N}");
    private readonly SqliteConnection anchor;
    private readonly DbContextOptions<TransfersDbContext> dbOptions;
    private readonly List<Transfer> downloads = [];

    public ShareLinkServiceTests()
    {
        Directory.CreateDirectory(Downloads);

        var connectionString = $"Data Source=sharelinks_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        // keep the shared-cache in-memory database alive for the life of the test
        anchor = new SqliteConnection(connectionString);
        anchor.Open();

        dbOptions = new DbContextOptionsBuilder<TransfersDbContext>().UseSqlite(connectionString).Options;

        using var context = new TransfersDbContext(dbOptions);
        context.Database.EnsureCreated();

        var options = new Options
        {
            Directories = new Options.DirectoriesOptions { Downloads = Downloads, Incomplete = Downloads },
            Transfers = new Options.TransfersOptions
            {
                ShareLinks = new Options.TransfersOptions.ShareLinksOptions { Enabled = true, Directory = Shared, MaxDays = 30 },
            },
            Integrations = new Options.IntegrationsOptions
            {
                Mail = new Options.IntegrationsOptions.MailOptions { Enabled = true, Adapter = "fake", From = "me@example.com" },
            },
        };

        var monitor = new TestOptionsMonitor<Options>(options);

        var factory = new Mock<IDbContextFactory<TransfersDbContext>>();
        factory.Setup(f => f.CreateDbContext()).Returns(() => new TransfersDbContext(dbOptions));

        var downloadService = new Mock<IDownloadService>();
        downloadService
            .Setup(d => d.Find(It.IsAny<Expression<Func<Transfer, bool>>>()))
            .Returns((Expression<Func<Transfer, bool>> e) => downloads.FirstOrDefault(e.Compile()));

        Service = new ShareLinkService(
            factory.Object,
            downloadService.Object,
            new FileService(monitor),
            new MailService([Adapter], monitor),
            monitor);
    }

    private FakeMailAdapter Adapter { get; } = new();
    private string Downloads => Path.Combine(root, "downloads");
    private string Shared => Path.Combine(root, "shared");
    private ShareLinkService Service { get; }

    public void Dispose()
    {
        anchor.Dispose();

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_Link_Serves_Copies_Named_As_An_Archive_Would_Name_Them()
    {
        var a = Download("@@x\\Music\\Album\\01 - One.flac", "one");
        var b = Download("@@x\\Music\\Album\\02 - Two.flac", "two");

        var link = await Service.CreateAsync([b.Id, a.Id], days: 7);

        Assert.Equal("Album", link.Title);
        Assert.Equal(["Album/01 - One.flac", "Album/02 - Two.flac"], link.Files.Select(f => f.Name));
        Assert.Equal([0, 1], link.Files.Select(f => f.Position));
        Assert.Equal("one", File.ReadAllText(Service.PathOf(link.Files[0])));
        Assert.InRange(link.ExpiresAt - link.CreatedAt, TimeSpan.FromDays(7), TimeSpan.FromDays(7));
        Assert.Equal(43, Assert.Single(link.Accesses).Token.Length);
        Assert.Null(link.Accesses[0].Recipient);
    }

    [Fact]
    public async Task The_Copy_Outlives_The_Download_It_Came_From()
    {
        var a = Download("@@x\\Album\\track.flac", "bytes");

        var link = await Service.CreateAsync([a.Id], days: 1);
        File.Delete(a.LocalFilename);

        Assert.Equal("track.flac", link.Title);
        Assert.Equal("bytes", File.ReadAllText(Service.PathOf(link.Files[0])));
    }

    [Fact]
    public async Task Files_From_Several_Folders_Are_Counted_Rather_Than_Named()
    {
        var a = Download("@@x\\One\\a.flac", "a");
        var b = Download("@@x\\Two\\b.flac", "b");

        var link = await Service.CreateAsync([a.Id, b.Id], days: 1);

        Assert.Equal("2 files", link.Title);
    }

    [Fact]
    public async Task A_Given_Title_Is_Kept()
    {
        var a = Download("@@x\\Album\\a.flac", "a");

        var link = await Service.CreateAsync([a.Id], days: 1, title: "  For Kim ");

        Assert.Equal("For Kim", link.Title);
    }

    [Fact]
    public async Task An_Unfinished_Download_Cannot_Be_Shared_And_Leaves_Nothing_Behind()
    {
        var a = Download("@@x\\Album\\a.flac", "a");
        var b = Download("@@x\\Album\\b.flac", "b", TransferStates.InProgress);

        await Assert.ThrowsAsync<ShareLinkException>(() => Service.CreateAsync([a.Id, b.Id], days: 1));

        Assert.Empty(await Service.ListAsync());
        Assert.False(Directory.Exists(Shared) && Directory.EnumerateFileSystemEntries(Shared).Any());
    }

    [Fact]
    public async Task A_File_Gone_From_Disk_Fails_The_Link_And_Removes_Copies_Already_Made()
    {
        var a = Download("@@x\\Album\\a.flac", "a");
        var b = Download("@@x\\Album\\b.flac", "b");
        File.Delete(b.LocalFilename);

        await Assert.ThrowsAsync<ShareLinkException>(() => Service.CreateAsync([a.Id, b.Id], days: 1));

        Assert.Empty(await Service.ListAsync());
        Assert.Empty(Directory.EnumerateFileSystemEntries(Shared));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public async Task A_Validity_Outside_The_Allowed_Range_Is_Refused(int days)
    {
        var a = Download("@@x\\Album\\a.flac", "a");

        await Assert.ThrowsAsync<ShareLinkException>(() => Service.CreateAsync([a.Id], days));
    }

    [Fact]
    public async Task A_Token_Opens_Its_Link_Until_It_Expires()
    {
        var a = Download("@@x\\Album\\a.flac", "a");
        var link = await Service.CreateAsync([a.Id], days: 1);

        var token = link.Accesses[0].Token;

        Assert.Equal(link.Id, (await Service.FindByTokenAsync(token)).Link.Id);
        Assert.Null(await Service.FindByTokenAsync("not-a-token"));

        Expire(link.Id);

        // not yet swept, and already closed
        Assert.Null(await Service.FindByTokenAsync(token));
    }

    [Fact]
    public async Task The_Sweep_Deletes_Expired_Links_With_Their_Files_And_Keeps_The_Rest()
    {
        var a = Download("@@x\\Album\\a.flac", "a");
        var b = Download("@@x\\Album\\b.flac", "b");

        var expired = await Service.CreateAsync([a.Id], days: 1);
        var live = await Service.CreateAsync([b.Id], days: 1);
        var expiredFolder = Path.GetDirectoryName(Path.GetDirectoryName(Service.PathOf(expired.Files[0])));

        Expire(expired.Id);
        await Service.SweepAsync();

        Assert.Equal([live.Id], (await Service.ListAsync()).Select(l => l.Id));
        Assert.False(Directory.Exists(expiredFolder));
        Assert.NotNull(Service.PathOf(live.Files[0]));
    }

    [Fact]
    public async Task The_Sweep_Deletes_A_Stale_Folder_No_Link_Owns_But_Nothing_Else()
    {
        Directory.CreateDirectory(Shared);

        var stale = Path.Combine(Shared, Guid.NewGuid().ToString("N"));
        var fresh = Path.Combine(Shared, Guid.NewGuid().ToString("N"));
        var foreign = Path.Combine(Shared, "not-ours");

        foreach (var folder in new[] { stale, fresh, foreign })
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "file"), "x");
        }

        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));
        Directory.SetLastWriteTimeUtc(foreign, DateTime.UtcNow.AddHours(-2));

        await Service.SweepAsync();

        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(fresh));
        Assert.True(Directory.Exists(foreign));
    }

    [Fact]
    public async Task Revoking_Deletes_The_Link_And_Its_Files_Now()
    {
        var a = Download("@@x\\Album\\a.flac", "a");
        var link = await Service.CreateAsync([a.Id], days: 1);
        var path = Service.PathOf(link.Files[0]);

        Assert.True(await Service.RevokeAsync(link.Id));
        Assert.False(File.Exists(path));
        Assert.Null(await Service.FindAsync(link.Id));
        Assert.False(await Service.RevokeAsync(link.Id));
    }

    [Fact]
    public async Task Extending_Counts_From_Now()
    {
        var a = Download("@@x\\Album\\a.flac", "a");
        var link = await Service.CreateAsync([a.Id], days: 1);
        Expire(link.Id);

        var extended = await Service.ExtendAsync(link.Id, days: 3);

        Assert.InRange(extended.ExpiresAt, DateTime.UtcNow.AddDays(3).AddMinutes(-1), DateTime.UtcNow.AddDays(3));
        Assert.NotNull(await Service.FindByTokenAsync(link.Accesses[0].Token));
    }

    [Fact]
    public async Task A_Recorded_Path_Outside_The_Links_Own_Folder_Is_Not_Served()
    {
        var a = Download("@@x\\Album\\a.flac", "a");
        var link = await Service.CreateAsync([a.Id], days: 1);
        var file = link.Files[0];

        file.Path = a.LocalFilename;

        Assert.Null(Service.PathOf(file));
    }

    [Fact]
    public async Task Downloads_Are_Counted()
    {
        var a = Download("@@x\\Album\\a.flac", "a");
        var link = await Service.CreateAsync([a.Id], days: 1);

        await Service.RecordDownloadAsync(link.Accesses[0].Id);
        await Service.RecordDownloadAsync(link.Accesses[0].Id);

        var stored = (await Service.FindAsync(link.Id)).Accesses[0];
        Assert.Equal(2, stored.DownloadCount);
        Assert.NotNull(stored.LastDownloadedAt);
    }

    [Fact]
    public async Task A_Share_Can_Be_Made_Without_A_General_Link()
    {
        var a = Download("@@x\\Album\\a.flac", "a");

        var link = await Service.CreateAsync([a.Id], days: 1, general: false);

        Assert.Empty(link.Accesses);
    }

    [Fact]
    public async Task Each_Recipient_Gets_A_Link_Of_Their_Own_And_Keeps_It_When_Mailed_Again()
    {
        var a = Download("@@x\\Album\\a.flac", "a");
        var link = await Service.CreateAsync([a.Id], days: 1);

        var first = await Service.SendAsync(link.Id, Url, ["kim@example.com", "Lee@example.com"]);
        var again = await Service.SendAsync(link.Id, Url, ["KIM@example.com"]);

        Assert.All(first.Concat(again), r => Assert.True(r.Sent));
        Assert.Equal(first[0].AccessId, again[0].AccessId);

        var accesses = (await Service.FindAsync(link.Id)).Accesses;
        Assert.Equal([null, "kim@example.com", "Lee@example.com"], accesses.Select(x => x.Recipient));
        Assert.Equal(3, accesses.Select(x => x.Token).Distinct().Count());
        Assert.All(accesses.Skip(1), x => Assert.NotNull(x.SentAt));

        // each mail carries its own recipient's link, never the general one
        Assert.Equal(3, Adapter.Sent.Count);
        Assert.Contains(Url(accesses[1].Token), Adapter.Sent[0].Mail.Text);
        Assert.Contains(Url(accesses[2].Token), Adapter.Sent[1].Mail.Text);
        Assert.DoesNotContain(Url(accesses[0].Token), string.Concat(Adapter.Sent.Select(m => m.Mail.Text)));
    }

    [Fact]
    public async Task Revoking_One_Link_Leaves_The_Others_Working()
    {
        var a = Download("@@x\\Album\\a.flac", "a");
        var link = await Service.CreateAsync([a.Id], days: 1);
        await Service.SendAsync(link.Id, Url, ["kim@example.com", "lee@example.com"]);

        var accesses = (await Service.FindAsync(link.Id)).Accesses;
        var after = await Service.RevokeAccessAsync(link.Id, accesses[1].Id);

        Assert.NotNull(after.Accesses[1].RevokedAt);
        Assert.Null(await Service.FindByTokenAsync(accesses[1].Token));
        Assert.NotNull(await Service.FindByTokenAsync(accesses[0].Token));
        Assert.NotNull(await Service.FindByTokenAsync(accesses[2].Token));
        Assert.NotNull(Service.PathOf(after.Files[0]));
    }

    [Fact]
    public async Task Mailing_A_Revoked_Recipient_Again_Gives_Them_A_New_Link()
    {
        var a = Download("@@x\\Album\\a.flac", "a");
        var link = await Service.CreateAsync([a.Id], days: 1, general: false);

        var first = (await Service.SendAsync(link.Id, Url, ["kim@example.com"]))[0];
        await Service.RevokeAccessAsync(link.Id, first.AccessId);
        var second = (await Service.SendAsync(link.Id, Url, ["kim@example.com"]))[0];

        Assert.NotEqual(first.AccessId, second.AccessId);

        var accesses = (await Service.FindAsync(link.Id)).Accesses;
        Assert.Equal(2, accesses.Count);
        Assert.NotNull(accesses.Single(x => x.Id == first.AccessId).RevokedAt);
        Assert.NotNull(await Service.FindByTokenAsync(accesses.Single(x => x.Id == second.AccessId).Token));
    }

    [Fact]
    public async Task A_Revoked_General_Link_Is_Replaced_With_A_New_Token_Never_Revived()
    {
        var a = Download("@@x\\Album\\a.flac", "a");
        var link = await Service.CreateAsync([a.Id], days: 1);
        var old = link.Accesses[0];

        await Service.RevokeAccessAsync(link.Id, old.Id);
        var after = await Service.CreateGeneralAccessAsync(link.Id);
        var unchanged = await Service.CreateGeneralAccessAsync(link.Id);

        var general = after.Accesses.Where(x => x.Recipient is null).ToList();
        Assert.Equal(2, general.Count);
        Assert.Null(await Service.FindByTokenAsync(old.Token));
        Assert.NotNull(await Service.FindByTokenAsync(general.Single(x => x.RevokedAt is null).Token));
        Assert.Equal(2, unchanged.Accesses.Count);
    }

    [Fact]
    public async Task A_Failed_Send_Is_Recorded_On_The_Recipients_Link()
    {
        var a = Download("@@x\\Album\\a.flac", "a");
        var link = await Service.CreateAsync([a.Id], days: 1, general: false);
        Adapter.Fail = true;

        var result = Assert.Single(await Service.SendAsync(link.Id, Url, ["kim@example.com"]));

        Assert.False(result.Sent);
        var access = Assert.Single((await Service.FindAsync(link.Id)).Accesses);
        Assert.Null(access.SentAt);
        Assert.Equal("boom", access.MailError);
    }

    [Fact]
    public async Task Withdrawing_A_Share_Ends_Every_Link_To_It()
    {
        var a = Download("@@x\\Album\\a.flac", "a");
        var link = await Service.CreateAsync([a.Id], days: 1);
        await Service.SendAsync(link.Id, Url, ["kim@example.com"]);
        var tokens = (await Service.FindAsync(link.Id)).Accesses.Select(x => x.Token).ToList();

        await Service.RevokeAsync(link.Id);

        foreach (var token in tokens)
        {
            Assert.Null(await Service.FindByTokenAsync(token));
        }
    }

    [Fact]
    public void The_Mail_Carries_The_Address_And_Encodes_What_A_Peer_Named()
    {
        var link = new ShareLink
        {
            Title = "<b>Album</b>",
            ExpiresAt = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc),
            Files = [new ShareLinkFile { Name = "Album/<i>a</i>.flac", Size = 1024 * 1024 }],
        };

        var mail = ShareLinkMail.Compose(link, "https://example.com/l/abc", new Options());

        Assert.Contains("https://example.com/l/abc", mail.Text);
        Assert.Contains("href=\"https://example.com/l/abc\"", mail.Html);
        Assert.DoesNotContain("<b>Album</b>", mail.Html);
        Assert.DoesNotContain("<i>a</i>", mail.Html);
        Assert.Contains("Sunday 1 November 2026, 12:00 (Etc/UTC)", mail.Text);
    }

    private static string Url(string token) => $"https://example.com/l/{token}";

    private Transfer Download(string remote, string content, TransferStates state = TransferStates.Completed | TransferStates.Succeeded)
    {
        var local = Path.Combine(Downloads, $"{Guid.NewGuid():N}-{remote[(remote.LastIndexOf('\\') + 1)..]}");
        File.WriteAllText(local, content);

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            Direction = TransferDirection.Download,
            Username = "peer",
            Filename = remote,
            LocalFilename = local,
            State = state,
        };

        downloads.Add(transfer);
        return transfer;
    }

    private void Expire(Guid id)
    {
        using var context = new TransfersDbContext(dbOptions);
        var link = context.ShareLinks.Single(l => l.Id == id);
        link.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        context.SaveChanges();
    }

    private sealed class FakeMailAdapter : IMailAdapter
    {
        public string Name => "fake";
        public bool Fail { get; set; }
        public List<(Mail Mail, string To)> Sent { get; } = [];

        public Task SendAsync(Mail mail, string from, string to, System.Threading.CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new MailException("boom");
            }

            Sent.Add((mail, to));
            return Task.CompletedTask;
        }
    }
}
