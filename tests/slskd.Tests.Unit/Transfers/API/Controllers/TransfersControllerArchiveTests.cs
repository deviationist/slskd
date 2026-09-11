namespace slskd.Tests.Unit.Transfers.API.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Options;
    using Moq;
    using slskd.Files;
    using slskd.Transfers;
    using slskd.Transfers.API;
    using slskd.Transfers.Downloads;
    using slskd.Users;
    using Xunit;

    /// <summary>
    ///     Downloading several finished downloads as one streamed archive.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The file service here is <b>real</b>, over real directories, rather than mocked. Containment is the
    ///         whole security story of this feature, and a mock asked whether it would have refused a path proves
    ///         nothing about whether the path would have been refused.
    ///     </para>
    ///     <para>
    ///         The archive is written to a MemoryStream standing in for the response body and then read back as a zip,
    ///         so these assert on the bytes an operator would actually receive.
    ///     </para>
    /// </remarks>
    public class TransfersControllerArchiveTests : IDisposable
    {
        public TransfersControllerArchiveTests()
        {
            Temp = Path.Combine(Path.GetTempPath(), $"slskd.test.{Guid.NewGuid()}");
            Downloads = Path.Combine(Temp, "downloads");
            Incomplete = Path.Combine(Temp, "incomplete");

            Directory.CreateDirectory(Downloads);
            Directory.CreateDirectory(Incomplete);

            var options = new slskd.Options
            {
                RemoteFileRetrieval = true,
                Directories = new slskd.Options.DirectoriesOptions
                {
                    Downloads = Downloads,
                    Incomplete = Incomplete,
                },
            };

            FileService = new FileService(new TestOptionsMonitor<slskd.Options>(options));

            OptionsSnapshotMock = new Mock<IOptionsSnapshot<slskd.Options>>();
            OptionsSnapshotMock.SetupGet(o => o.Value).Returns(options);

            DownloadsMock = new Mock<IDownloadService>();
            DownloadsMock
                .Setup(d => d.Find(It.IsAny<Expression<Func<Transfer, bool>>>()))
                .Returns((Expression<Func<Transfer, bool>> predicate) => Store.FirstOrDefault(predicate.Compile()));

            TransferService = new TransferService(contextFactory: null, downloadService: DownloadsMock.Object);
            Tickets = new DownloadTicketService();
        }

        private string Temp { get; }
        private string Downloads { get; }
        private string Incomplete { get; }
        private List<Transfer> Store { get; } = [];
        private FileService FileService { get; }
        private Mock<IOptionsSnapshot<slskd.Options>> OptionsSnapshotMock { get; }
        private Mock<IDownloadService> DownloadsMock { get; }
        private TransferService TransferService { get; }
        private DownloadTicketService Tickets { get; }

        public void Dispose()
        {
            Directory.Delete(Temp, recursive: true);
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void Availability_Separates_What_Is_There_From_What_Is_Not()
        {
            var present = GivenDownload(@"\Album\01 - Intro.flac", WriteDownloadedFile("01 - Intro.flac", "audio"));
            var gone = GivenDownload(@"\Album\02 - Gone.flac", Path.Combine(Downloads, "02 - Gone.flac"));

            var result = Controller().GetArchiveAvailability("somebody", Request(present, gone));

            var response = Assert.IsType<ArchiveAvailabilityResponse>(Assert.IsType<OkObjectResult>(result).Value);

            Assert.Equal(["01 - Intro.flac"], response.Available.Select(a => a.Filename));
            Assert.Equal(["02 - Gone.flac"], response.Missing.Select(m => m.Filename));
        }

        [Fact]
        public void Availability_Counts_A_Download_That_Never_Recorded_A_Path_As_Missing()
        {
            // finished before this fork began recording where the bytes went. there is nothing to archive, and
            // guessing a path is not something to do on the strength of a filename a peer chose
            var unrecorded = GivenDownload(@"\Album\01 - Intro.flac", localFilename: null);

            var result = Controller().GetArchiveAvailability("somebody", Request(unrecorded));

            var response = Assert.IsType<ArchiveAvailabilityResponse>(Assert.IsType<OkObjectResult>(result).Value);

            Assert.Empty(response.Available);
            Assert.Single(response.Missing);
        }

        [Fact]
        public void Availability_Counts_A_File_Outside_The_Allowed_Directories_As_Missing()
        {
            var outside = Path.Combine(Temp, "secret.txt");
            File.WriteAllText(outside, "secret");

            var sneaky = GivenDownload(@"\Album\01 - Intro.flac", outside);

            var result = Controller().GetArchiveAvailability("somebody", Request(sneaky));

            var response = Assert.IsType<ArchiveAvailabilityResponse>(Assert.IsType<OkObjectResult>(result).Value);

            Assert.Empty(response.Available);
            Assert.Single(response.Missing);
        }

        [Fact]
        public void Availability_Is_Refused_When_Retrieval_Is_Disabled()
        {
            GivenRetrievalDisabled();

            var result = Controller().GetArchiveAvailability("somebody", new ArchiveRequest { Ids = [Guid.NewGuid().ToString()] });

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public void Availability_Refuses_A_Request_With_No_Ids()
        {
            Assert.IsType<BadRequestObjectResult>(
                Controller().GetArchiveAvailability("somebody", new ArchiveRequest { Ids = null }));

            Assert.IsType<BadRequestObjectResult>(
                Controller().GetArchiveAvailability("somebody", new ArchiveRequest { Ids = [] }));

            Assert.IsType<BadRequestObjectResult>(
                Controller().GetArchiveAvailability("somebody", request: null));
        }

        [Fact]
        public void Availability_Refuses_An_Id_That_Is_Not_An_Id()
        {
            // bound as a string rather than a Guid precisely so this can be said, instead of matching nothing and
            // being reported as a file that has gone missing
            var result = Controller().GetArchiveAvailability("somebody", new ArchiveRequest { Ids = ["not-a-guid"] });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void A_Ticket_Is_Refused_When_Retrieval_Is_Disabled()
        {
            GivenRetrievalDisabled();

            var result = Controller().CreateArchiveTicket("somebody", new ArchiveRequest { Ids = [Guid.NewGuid().ToString()] });

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task An_Archive_Contains_The_Selected_Files_Under_Folder_Qualified_Names()
        {
            var one = GivenDownload(@"\Aphex Twin - Drukqs\01 - Intro.flac", WriteDownloadedFile("a.flac", "first"));
            var two = GivenDownload(@"\Boards of Canada - Geogaddi\01 - Intro.flac", WriteDownloadedFile("b.flac", "second"));

            using var archive = await ArchiveOf(one, two);

            Assert.Equal(
                ["Aphex Twin - Drukqs/01 - Intro.flac", "Boards of Canada - Geogaddi/01 - Intro.flac"],
                archive.Entries.Select(e => e.FullName).Order());

            Assert.Equal("first", ReadEntry(archive, "Aphex Twin - Drukqs/01 - Intro.flac"));
            Assert.Equal("second", ReadEntry(archive, "Boards of Canada - Geogaddi/01 - Intro.flac"));
        }

        [Fact]
        public async Task An_Archive_Is_Stored_Rather_Than_Deflated()
        {
            // audio does not compress, so deflating it spends CPU for nothing -- and storing is what makes the whole
            // response a pass-through with nothing to wait for
            var content = new string('a', 4096);
            var one = GivenDownload(@"\Album\01.flac", WriteDownloadedFile("a.flac", content));

            using var archive = await ArchiveOf(one);

            var entry = archive.Entries.Single();

            Assert.Equal(entry.Length, entry.CompressedLength);
        }

        [Fact]
        public async Task An_Archive_Skips_A_File_That_Went_Missing_And_Says_So_At_The_End()
        {
            // the case the availability check cannot remove: the file was there when it was asked about, and is not
            // there now. a response most of the way to the browser must not be failed over it
            var here = GivenDownload(@"\Album\01 - Here.flac", WriteDownloadedFile("here.flac", "audio"));
            var gone = GivenDownload(@"\Album\02 - Gone.flac", Path.Combine(Downloads, "gone.flac"));

            using var archive = await ArchiveOf(here, gone);

            Assert.Equal("audio", ReadEntry(archive, "Album/01 - Here.flac"));
            Assert.Null(archive.GetEntry("Album/02 - Gone.flac"));

            var missing = archive.Entries.Last();

            Assert.Equal(ArchiveNaming.MissingEntryName, missing.FullName);
            Assert.Contains("02 - Gone.flac", ReadEntry(archive, ArchiveNaming.MissingEntryName));
        }

        [Fact]
        public async Task An_Archive_Has_No_Skipped_List_When_Nothing_Was_Skipped()
        {
            var one = GivenDownload(@"\Album\01.flac", WriteDownloadedFile("a.flac", "audio"));

            using var archive = await ArchiveOf(one);

            Assert.Null(archive.GetEntry(ArchiveNaming.MissingEntryName));
        }

        [Fact]
        public async Task An_Archive_Never_Serves_A_File_Outside_The_Allowed_Directories()
        {
            // a recorded path that resolves somewhere this application may not read is refused entry by entry, the
            // same way a single-file retrieval of it would be. the archive is still produced -- it just does not
            // contain those bytes, and says which entry it does not contain
            var outside = Path.Combine(Temp, "secret.txt");
            File.WriteAllText(outside, "TOP-SECRET-CONTENT");

            var ok = GivenDownload(@"\Album\01 - Fine.flac", WriteDownloadedFile("fine.flac", "audio"));
            var sneaky = GivenDownload(@"\Album\02 - Sneaky.flac", outside);

            var bytes = await ArchiveBytesOf(ok, sneaky);

            Assert.DoesNotContain("TOP-SECRET-CONTENT", Encoding.UTF8.GetString(bytes));

            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

            Assert.Null(archive.GetEntry("Album/02 - Sneaky.flac"));
            Assert.Contains("02 - Sneaky.flac", ReadEntry(archive, ArchiveNaming.MissingEntryName));
        }

        [Fact]
        public async Task An_Archive_Never_Serves_The_Target_Of_A_Symlink_Pointing_Outside()
        {
            if (OperatingSystem.IsWindows()) return;

            // the recorded path is inside the downloads directory, and following it is not
            var outside = Path.Combine(Temp, "secret.txt");
            File.WriteAllText(outside, "TOP-SECRET-CONTENT");

            var link = Path.Combine(Downloads, "innocent.flac");
            File.CreateSymbolicLink(link, outside);

            var sneaky = GivenDownload(@"\Album\innocent.flac", link);

            var bytes = await ArchiveBytesOf(sneaky);

            Assert.DoesNotContain("TOP-SECRET-CONTENT", Encoding.UTF8.GetString(bytes));
        }

        [Fact]
        public async Task An_Archive_Cannot_Be_Fetched_Twice_With_One_Ticket()
        {
            var one = GivenDownload(@"\Album\01.flac", WriteDownloadedFile("a.flac", "audio"));
            var ticket = TicketFor(one);

            Assert.IsType<EmptyResult>(await Controller().GetArchiveAsync("somebody", ticket));
            Assert.IsType<ForbidResult>(await Controller().GetArchiveAsync("somebody", ticket));
        }

        [Fact]
        public async Task An_Archive_Cannot_Be_Fetched_With_Somebody_Elses_Ticket()
        {
            var one = GivenDownload(@"\Album\01.flac", WriteDownloadedFile("a.flac", "audio"));
            var ticket = TicketFor(one);

            Assert.IsType<ForbidResult>(await Controller().GetArchiveAsync("somebody-else", ticket));
        }

        [Fact]
        public async Task An_Expired_Ticket_Is_Reported_As_Gone_Rather_Than_Refused()
        {
            var service = new DownloadTicketService(lifetime: TimeSpan.FromMilliseconds(20));
            var one = GivenDownload(@"\Album\01.flac", WriteDownloadedFile("a.flac", "audio"));

            var (ticket, _) = service.Issue("somebody", [one.Id]);

            System.Threading.Thread.Sleep(100);

            var result = await Controller(tickets: service).GetArchiveAsync("somebody", ticket);

            Assert.Equal(StatusCodes.Status410Gone, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        [Fact]
        public async Task An_Archive_Is_Refused_Without_A_Ticket()
        {
            var result = await Controller().GetArchiveAsync("somebody", ticket: null);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task An_Archive_Is_Refused_When_Retrieval_Is_Disabled()
        {
            var one = GivenDownload(@"\Album\01.flac", WriteDownloadedFile("a.flac", "audio"));
            var ticket = TicketFor(one);

            GivenRetrievalDisabled();

            Assert.IsType<ForbidResult>(await Controller().GetArchiveAsync("somebody", ticket));
        }

        [Fact]
        public async Task An_Archive_Request_Is_Not_Logged_With_Its_Ticket()
        {
            // the ticket is a bearer credential in a query string. Serilog reads the raw target after the pipeline
            // has run, so what it is left holding is what ends up in the log
            var one = GivenDownload(@"\Album\01.flac", WriteDownloadedFile("a.flac", "audio"));
            var ticket = TicketFor(one);

            var controller = Controller();
            controller.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpRequestFeature>().RawTarget
                = $"/api/v0/transfers/downloads/somebody/archive?ticket={ticket}";

            await controller.GetArchiveAsync("somebody", ticket);

            var target = controller.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpRequestFeature>().RawTarget;

            Assert.DoesNotContain(ticket, target);
        }

        private static string ReadEntry(ZipArchive archive, string name)
        {
            using var reader = new StreamReader(archive.GetEntry(name).Open());
            return reader.ReadToEnd();
        }

        private static ArchiveRequest Request(params Transfer[] downloads)
            => new() { Ids = downloads.Select(d => d.Id.ToString()) };

        private TransfersController Controller(DownloadTicketService tickets = null)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.Body = new MemoryStream();

            return new TransfersController(
                transferService: TransferService,
                userService: new Mock<IUserService>().Object,
                fileService: FileService,
                downloadTicketService: tickets ?? Tickets,
                optionsSnapshot: OptionsSnapshotMock.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
            };
        }

        private void GivenRetrievalDisabled()
            => OptionsSnapshotMock.SetupGet(o => o.Value).Returns(new slskd.Options
            {
                RemoteFileRetrieval = false,
                Directories = new slskd.Options.DirectoriesOptions { Downloads = Downloads, Incomplete = Incomplete },
            });

        private Transfer GivenDownload(string remoteFilename, string localFilename)
        {
            var download = new Transfer
            {
                Id = Guid.NewGuid(),
                Username = "somebody",
                Filename = remoteFilename,
                LocalFilename = localFilename,
                State = Soulseek.TransferStates.Completed | Soulseek.TransferStates.Succeeded,
                BytesTransferred = 1024,
            };

            Store.Add(download);

            return download;
        }

        private string WriteDownloadedFile(string name, string content)
        {
            var filename = Path.Combine(Downloads, name);
            File.WriteAllText(filename, content);

            return filename;
        }

        private string TicketFor(params Transfer[] downloads)
            => Tickets.Issue("somebody", downloads.Select(d => d.Id)).Ticket;

        private async Task<byte[]> ArchiveBytesOf(params Transfer[] downloads)
        {
            var ticket = TicketFor(downloads);
            var controller = Controller();

            var result = await controller.GetArchiveAsync("somebody", ticket);

            Assert.IsType<EmptyResult>(result);
            Assert.Equal("application/zip", controller.Response.ContentType);
            Assert.Contains("attachment", controller.Response.Headers.ContentDisposition.ToString());

            return ((MemoryStream)controller.Response.Body).ToArray();
        }

        private async Task<ZipArchive> ArchiveOf(params Transfer[] downloads)
            => new(new MemoryStream(await ArchiveBytesOf(downloads)), ZipArchiveMode.Read);
    }
}
