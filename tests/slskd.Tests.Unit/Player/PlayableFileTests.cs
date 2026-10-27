namespace slskd.Tests.Unit.Player
{
    using System;
    using System.IO;
    using slskd.Files;
    using slskd.Player;
    using slskd.Transfers;
    using Transfer = slskd.Transfers.Transfer;
    using TransferDirection = Soulseek.TransferDirection;
    using TransferStates = Soulseek.TransferStates;
    using Xunit;

    public class PlayableFileTests : IDisposable
    {
        public PlayableFileTests()
        {
            Temp = Path.Combine(Path.GetTempPath(), $"slskd.test.{Guid.NewGuid()}");
            Downloads = Path.Combine(Temp, "downloads");
            Outside = Path.Combine(Temp, "outside");
            Directory.CreateDirectory(Path.Combine(Downloads, "User", "Album"));
            Directory.CreateDirectory(Outside);

            Files = new FileService(new TestOptionsMonitor<slskd.Options>(new slskd.Options
            {
                Directories = new slskd.Options.DirectoriesOptions { Downloads = Downloads, Incomplete = Path.Combine(Temp, "incomplete") },
            }));
        }

        private string Temp { get; }
        private string Downloads { get; }
        private string Outside { get; }
        private FileService Files { get; }

        public void Dispose() => Directory.Delete(Temp, recursive: true);

        [Fact]
        public void A_finished_download_plays_the_file_it_recorded()
        {
            var file = Write(Path.Combine(Downloads, "User", "Album", "01 Track.aiff"));

            var got = PlayableFile.Resolve(Finished(file), Downloads, Files, out _);

            Assert.Equal(file, got.FullName);
        }

        [Fact]
        public void A_download_still_running_is_not_yet_rather_than_gone()
        {
            var download = Finished(Write(Path.Combine(Downloads, "x.flac")));
            download.State = TransferStates.InProgress;

            Assert.Null(PlayableFile.Resolve(download, Downloads, Files, out var reason));
            Assert.Equal(Unplayable.NotYet, reason);
        }

        [Fact]
        public void A_download_that_failed_is_gone()
        {
            var download = Finished(Write(Path.Combine(Downloads, "x.flac")));
            download.State = TransferStates.Completed | TransferStates.Errored;

            Assert.Null(PlayableFile.Resolve(download, Downloads, Files, out var reason));
            Assert.Equal(Unplayable.Gone, reason);
        }

        [Fact]
        public void A_file_that_has_since_left_is_gone()
        {
            var download = Finished(Path.Combine(Downloads, "moved-away.flac"));

            Assert.Null(PlayableFile.Resolve(download, Downloads, Files, out var reason));
            Assert.Equal(Unplayable.Gone, reason);
        }

        [Fact]
        public void A_recorded_file_outside_the_downloads_directory_is_refused()
        {
            var download = Finished(Write(Path.Combine(Outside, "x.flac")));

            Assert.Null(PlayableFile.Resolve(download, Downloads, Files, out var reason));
            Assert.Equal(Unplayable.Refused, reason);
        }

        [Fact]
        public void A_sibling_folder_sharing_the_prefix_is_outside()
        {
            var sibling = Downloads + "-old";
            Directory.CreateDirectory(sibling);
            var download = Finished(Write(Path.Combine(sibling, "x.flac")));

            Assert.Null(PlayableFile.Resolve(download, Downloads, Files, out var reason));
            Assert.Equal(Unplayable.Refused, reason);
        }

        [Fact]
        public void A_link_inside_the_downloads_directory_pointing_out_of_it_is_refused()
        {
            var target = Write(Path.Combine(Outside, "secret.flac"));
            var link = Path.Combine(Downloads, "innocent.flac");
            File.CreateSymbolicLink(link, target);

            Assert.Null(PlayableFile.Resolve(Finished(link), Downloads, Files, out var reason));
            Assert.Equal(Unplayable.Refused, reason);
        }

        [Fact]
        public void A_recorded_path_with_traversal_segments_is_refused()
        {
            Write(Path.Combine(Outside, "x.flac"));
            var download = Finished(Downloads + "/../outside/x.flac");

            Assert.Null(PlayableFile.Resolve(download, Downloads, Files, out var reason));
            Assert.Equal(Unplayable.Refused, reason);
        }

        [Fact]
        public void A_file_that_is_not_audio_is_refused()
        {
            var download = Finished(Write(Path.Combine(Downloads, "cover.jpg")));

            Assert.Null(PlayableFile.Resolve(download, Downloads, Files, out var reason));
            Assert.Equal(Unplayable.Refused, reason);
        }

        [Fact]
        public void An_upload_is_not_a_download()
        {
            var file = Write(Path.Combine(Downloads, "x.flac"));
            var upload = new Transfer
            {
                Id = Guid.NewGuid(),
                Direction = TransferDirection.Upload,
                LocalFilename = file,
                State = TransferStates.Completed | TransferStates.Succeeded,
            };

            Assert.Null(PlayableFile.Resolve(upload, Downloads, Files, out var reason));
            Assert.Equal(Unplayable.NoSuchDownload, reason);
        }

        private static string Write(string path)
        {
            File.WriteAllBytes(path, new byte[] { 1 });
            return path;
        }

        private static Transfer Finished(string localFilename) => new()
        {
            Id = Guid.NewGuid(),
            Direction = TransferDirection.Download,
            Username = "somebody",
            Filename = "remote\\x.flac",
            LocalFilename = localFilename,
            State = TransferStates.Completed | TransferStates.Succeeded,
        };
    }

    public class StreamTicketsTests
    {
        [Fact]
        public void A_ticket_admits_the_download_it_was_issued_for_and_no_other()
        {
            var tickets = new StreamTickets();
            var id = Guid.NewGuid();
            var ticket = tickets.Issue(id);

            Assert.True(tickets.Admits(ticket, id));
            Assert.True(tickets.Admits(ticket, id));
            Assert.False(tickets.Admits(ticket, Guid.NewGuid()));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("made-up")]
        public void A_ticket_never_issued_admits_nothing(string ticket)
            => Assert.False(new StreamTickets().Admits(ticket, Guid.NewGuid()));

        [Fact]
        public void A_ticket_left_unused_past_its_idle_time_expires()
        {
            var tickets = new StreamTickets(idle: TimeSpan.FromMilliseconds(50));
            var id = Guid.NewGuid();
            var ticket = tickets.Issue(id);

            System.Threading.Thread.Sleep(300);

            Assert.False(tickets.Admits(ticket, id));
        }

        [Fact]
        public void Tickets_are_url_safe()
        {
            var ticket = new StreamTickets().Issue(Guid.NewGuid());
            Assert.Matches("^[A-Za-z0-9_-]+$", ticket);
        }
    }
}
