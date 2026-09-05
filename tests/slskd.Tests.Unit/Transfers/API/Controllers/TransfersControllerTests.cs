// <copyright file="TransfersControllerTests.cs" company="JP Dillingham">
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

namespace slskd.Tests.Unit.Transfers.API.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Options;
    using Moq;
    using OneOf;
    using slskd.Files;
    using slskd.Transfers;
    using slskd.Transfers.Downloads;
    using slskd.Transfers.API;
    using slskd.Users;
    using Xunit;

    /// <summary>
    ///     Deleting the downloaded file along with the download.
    /// </summary>
    /// <remarks>
    ///     Removing a download has never touched the disk. With the option on it does, and everything
    ///     here is about that: that the option alone decides, that only a file this application recorded
    ///     writing is deleted, that the folders the deletion empties go with it, and that the answer says
    ///     which of the outcomes happened rather than leaving the caller to infer it from a 204.
    /// </remarks>
    public class TransfersControllerTests
    {
        public TransfersControllerTests()
        {
            DownloadsMock = new Mock<IDownloadService>();

            // constructed rather than mocked: TransferService is a real class with a real constructor,
            // and nothing on this path goes through its own methods -- the controller reaches straight
            // for .Downloads. the context factory is therefore never touched
            TransferService = new TransferService(
                contextFactory: null,
                downloadService: DownloadsMock.Object);

            FileServiceMock = new Mock<FileService>(new Mock<IOptionsMonitor<slskd.Options>>().Object);

            OptionsSnapshotMock = new Mock<IOptionsSnapshot<slskd.Options>>();
            SetDeleteFileOnRemoval(true);
        }

        private Mock<IDownloadService> DownloadsMock { get; }
        private TransferService TransferService { get; }
        private Mock<FileService> FileServiceMock { get; }
        private Mock<IOptionsSnapshot<slskd.Options>> OptionsSnapshotMock { get; }

        private void SetDeleteFileOnRemoval(bool enabled)
            => OptionsSnapshotMock.SetupGet(o => o.Value).Returns(new slskd.Options
            {
                Transfers = new slskd.Options.TransfersOptions
                {
                    Download = new slskd.Options.TransfersOptions.GlobalDownloadOptions
                    {
                        DeleteFileOnRemoval = enabled,
                    },
                },
            });

        private TransfersController Controller => new(
            transferService: TransferService,
            userService: new Mock<IUserService>().Object,
            fileService: FileServiceMock.Object,
            optionsSnapshot: OptionsSnapshotMock.Object);

        private void GivenDownload(Guid id, string localFilename, Soulseek.TransferStates state = Soulseek.TransferStates.Completed | Soulseek.TransferStates.Succeeded, long bytesTransferred = 1024)
            => DownloadsMock
                .Setup(d => d.Find(It.IsAny<Expression<Func<Transfer, bool>>>()))
                .Returns(new Transfer { Id = id, LocalFilename = localFilename, State = state, BytesTransferred = bytesTransferred });

        private void GivenTheRecordIsRemoved(bool removed = true)
            => DownloadsMock.Setup(d => d.Remove(It.IsAny<Guid>())).Returns(removed);

        /// <summary>
        ///     Puts a real file at <paramref name="filename"/> and makes the file service answer for it.
        /// </summary>
        /// <remarks>
        ///     Real, because the deletion reports whether the file was actually there: `File.Delete` does
        ///     not throw over a path that holds nothing, so a fixture that never created one would have
        ///     the code under test reporting an absence and the test calling it a bug.
        /// </remarks>
        private void GivenDeletionResult(string filename, OneOf<bool, Exception> result, bool onDisk = true)
        {
            if (onDisk)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filename));
                File.WriteAllText(filename, "audio");
            }

            FileServiceMock
                .Setup(f => f.DeleteFilesAsync(It.IsAny<string[]>()))
                .Returns((string[] files) =>
                {
                    foreach (var f in files.Where(File.Exists))
                    {
                        File.Delete(f);
                    }

                    return Task.FromResult(new Dictionary<string, OneOf<bool, Exception>> { { filename, result } });
                });
        }

        /// <summary>
        ///     A real directory tree, since the pruning walk asks the filesystem what is still in a
        ///     folder and a mock of that would only ever confirm what the mock was told to believe.
        /// </summary>
        private string Temp { get; } = Directory.CreateTempSubdirectory("slskd.test.").FullName;

        /// <summary>
        ///     Makes the file service's directory deletion behave like the real one: it removes the
        ///     directory, and refuses the root the way `DeleteDirectoriesAsync` does.
        /// </summary>
        private void GivenDirectoriesCanBePruned(string root)
            => FileServiceMock
                .Setup(f => f.DeleteDirectoriesAsync(It.IsAny<string[]>()))
                .Returns((string[] dirs) =>
                {
                    if (dirs.Any(d => d == root))
                    {
                        throw new ArgumentException("Deletion of application-controlled directory roots is not supported");
                    }

                    foreach (var d in dirs)
                    {
                        Directory.Delete(d);
                    }

                    return Task.FromResult(dirs.ToDictionary(d => d, _ => (OneOf<bool, Exception>)true));
                });

        // --- the option is the only switch -------------------------------------------------------

        [Fact]
        public async Task With_The_Option_Off_A_Removal_Deletes_Nothing_And_Answers_204()
        {
            SetDeleteFileOnRemoval(false);
            GivenTheRecordIsRemoved();

            var result = await Controller.CancelDownloadAsync("user", Guid.NewGuid().ToString(), remove: true);

            Assert.IsType<NoContentResult>(result);
            FileServiceMock.Verify(f => f.DeleteFilesAsync(It.IsAny<string[]>()), Times.Never);

            // it does not even look the record up: with the option off this endpoint is what it was
            DownloadsMock.Verify(d => d.Find(It.IsAny<Expression<Func<Transfer, bool>>>()), Times.Never);
        }

        [Fact]
        public async Task A_Cancellation_Never_Deletes_Anything_Whatever_The_Option_Says()
        {
            // `remove=false` is the Cancel button, and cancelling is not removing. the option is named
            // for the removal and grants nothing here.
            var result = await Controller.CancelDownloadAsync("user", Guid.NewGuid().ToString(), remove: false);

            Assert.IsType<NoContentResult>(result);
            FileServiceMock.Verify(f => f.DeleteFilesAsync(It.IsAny<string[]>()), Times.Never);
        }

        [Fact]
        public async Task With_The_Option_On_A_Removal_Deletes_The_Recorded_File()
        {
            var id = Guid.NewGuid();
            var filename = Path.Combine(Temp, "01 track.flac");

            GivenDownload(id, filename);
            GivenTheRecordIsRemoved();
            GivenDeletionResult(filename, true);

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true);

            var outcome = Assert.IsType<RemovalResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.True(outcome.Removed);
            Assert.True(outcome.Deleted);
            Assert.Equal(filename, outcome.Filename);
            Assert.Null(outcome.Error);

            FileServiceMock.Verify(f => f.DeleteFilesAsync(filename), Times.Once);
        }

        [Fact]
        public async Task Answers_404_When_There_Is_No_Such_Download()
        {
            DownloadsMock
                .Setup(d => d.Find(It.IsAny<Expression<Func<Transfer, bool>>>()))
                .Returns((Transfer)null);

            var result = await Controller.CancelDownloadAsync("user", Guid.NewGuid().ToString(), remove: true);

            Assert.IsType<NotFoundResult>(result);
        }

        // --- what gets deleted ---------------------------------------------------------------------

        [Fact]
        public async Task A_Cancelled_Download_Has_Its_Partial_Deleted()
        {
            // the recorded path is the incomplete file until the download finishes and it is moved, so a
            // transfer that was cancelled or failed still names something to delete. this is the case
            // that leaves litter otherwise -- slskd keeps partials on purpose, to resume from, and only
            // a retention timer ever removes them
            var id = Guid.NewGuid();
            var partial = Path.Combine(Temp, "01 track.flac");

            GivenDownload(id, partial, Soulseek.TransferStates.Completed | Soulseek.TransferStates.Cancelled);
            GivenTheRecordIsRemoved();
            GivenDeletionResult(partial, true);

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true);

            var outcome = Assert.IsType<RemovalResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.True(outcome.Deleted);
            FileServiceMock.Verify(f => f.DeleteFilesAsync(partial), Times.Once);
        }

        [Fact]
        public async Task A_Download_With_No_Recorded_File_Deletes_Nothing_And_Says_So()
        {
            // a download that finished before this was recorded. there is no honest path to delete, and
            // guessing one is the thing this feature must not do
            var id = Guid.NewGuid();

            GivenDownload(id, localFilename: null);
            GivenTheRecordIsRemoved();

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true);

            var outcome = Assert.IsType<RemovalResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.True(outcome.Removed);
            Assert.False(outcome.Deleted);
            Assert.Null(outcome.Filename);
            Assert.Null(outcome.Error);

            FileServiceMock.Verify(f => f.DeleteFilesAsync(It.IsAny<string[]>()), Times.Never);
        }

        [Fact]
        public async Task A_Refused_Deletion_Is_Reported_Rather_Than_Thrown()
        {
            // the file service refuses anything outside the downloads and incomplete directories, which
            // a recorded path can become if those are reconfigured. the removal has already happened by
            // then and must not read as a failure of the whole request
            var id = Guid.NewGuid();
            var filename = Path.Combine(Temp, "01 track.flac");

            GivenDownload(id, filename);
            GivenTheRecordIsRemoved();
            GivenDeletionResult(filename, new UnauthorizedException("nope"));

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true);

            var outcome = Assert.IsType<RemovalResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.True(outcome.Removed);
            Assert.False(outcome.Deleted);
            Assert.Equal("nope", outcome.Error);
        }

        [Fact]
        public async Task The_Removal_Is_Reported_By_Remove_Itself_Not_Inferred()
        {
            // Remove() applies its own filter and answers whether it removed anything. a caller told
            // "removed, but the file is still there" would otherwise be reading a claim nobody made.
            var id = Guid.NewGuid();
            var filename = Path.Combine(Temp, "01 track.flac");

            GivenDownload(id, filename);
            GivenTheRecordIsRemoved(false);
            GivenDeletionResult(filename, true);

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true);

            var outcome = Assert.IsType<RemovalResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.False(outcome.Removed);
            Assert.True(outcome.Deleted);
        }

        // --- and the folders it empties ------------------------------------------------------------

        [Fact]
        public async Task The_Folders_The_Deletion_Empties_Go_With_It()
        {
            // a download arrives inside the folder the peer named, sometimes nested several deep.
            // removing one level would move the litter outwards rather than clear it.
            var root = Path.Combine(Temp, "complete");
            var nested = Path.Combine(root, "peer", "Some Album", "CD1");
            Directory.CreateDirectory(nested);

            var filename = Path.Combine(nested, "01 track.flac");
            var id = Guid.NewGuid();

            GivenDownload(id, filename);
            GivenTheRecordIsRemoved();
            GivenDeletionResult(filename, true);
            GivenDirectoriesCanBePruned(root);

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true);

            var outcome = Assert.IsType<RemovalResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(3, outcome.PrunedDirectories);

            Assert.False(Directory.Exists(Path.Combine(root, "peer")));

            // and never the root, which is the shared guard's rule rather than the walk's
            Assert.True(Directory.Exists(root));
        }

        [Fact]
        public async Task The_Walk_Stops_At_The_First_Folder_That_Still_Holds_Something()
        {
            var root = Path.Combine(Temp, "complete");
            var album = Path.Combine(root, "peer", "Some Album");
            var disc = Path.Combine(album, "CD1");
            Directory.CreateDirectory(disc);

            // a cover image beside the disc folder: the album folder is not empty and must survive
            File.WriteAllText(Path.Combine(album, "cover.jpg"), "art");

            var filename = Path.Combine(disc, "01 track.flac");
            var id = Guid.NewGuid();

            GivenDownload(id, filename);
            GivenTheRecordIsRemoved();
            GivenDeletionResult(filename, true);
            GivenDirectoriesCanBePruned(root);

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true);

            var outcome = Assert.IsType<RemovalResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(1, outcome.PrunedDirectories);

            Assert.False(Directory.Exists(disc));
            Assert.True(Directory.Exists(album));
        }

        [Fact]
        public async Task Nothing_Is_Pruned_When_The_File_Was_Not_Deleted()
        {
            var root = Path.Combine(Temp, "complete");
            var nested = Path.Combine(root, "peer", "Some Album");
            Directory.CreateDirectory(nested);

            var filename = Path.Combine(nested, "01 track.flac");
            var id = Guid.NewGuid();

            GivenDownload(id, filename);
            GivenTheRecordIsRemoved();
            GivenDeletionResult(filename, new UnauthorizedException("nope"));
            GivenDirectoriesCanBePruned(root);

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true);

            var outcome = Assert.IsType<RemovalResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(0, outcome.PrunedDirectories);
            Assert.True(Directory.Exists(nested));
        }

        // --- the cases found by reading it back ----------------------------------------------------

        [Fact]
        public async Task A_Transfer_That_Does_Not_Stop_Keeps_Its_File()
        {
            // the wait makes this rare; skipping the delete is what makes it safe when the wait is not
            // enough. unlinking a file that is still being written to is either allowed and confusing
            // (POSIX keeps the inode for the writer, and the move at the end fails) or refused outright
            var id = Guid.NewGuid();
            var partial = Path.Combine(Temp, "01 track.flac");

            // never reaches a terminal state, so the wait runs out
            GivenDownload(id, partial, Soulseek.TransferStates.InProgress);
            GivenTheRecordIsRemoved(false);
            GivenDeletionResult(partial, true);

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true);

            var outcome = Assert.IsType<RemovalResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.False(outcome.Deleted);
            Assert.Contains("did not stop in time", outcome.Error);

            FileServiceMock.Verify(f => f.DeleteFilesAsync(It.IsAny<string[]>()), Times.Never);
            Assert.True(File.Exists(partial));
        }

        [Fact]
        public async Task A_File_That_Is_Already_Gone_Is_A_Success()
        {
            // removing the same transfer twice, or a file removed by hand in between. what was asked
            // for is that the file not be there, and it is not -- failure is for a file that is there,
            // should go, and will not.
            var id = Guid.NewGuid();
            var filename = Path.Combine(Temp, "gone.flac");

            GivenDownload(id, filename);
            GivenTheRecordIsRemoved();
            GivenDeletionResult(filename, true, onDisk: false);

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true);

            var outcome = Assert.IsType<RemovalResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.True(outcome.Removed);
            Assert.True(outcome.Deleted);
            Assert.Null(outcome.Error);
        }

        [Fact]
        public async Task A_Download_That_Wrote_Bytes_Somewhere_Unrecorded_Is_Not_A_Success()
        {
            // a download from before this instance began recording where it writes. its file may well
            // be sitting in the downloads directory under a name nobody wrote down, so claiming the
            // end state would be claiming something never checked.
            var id = Guid.NewGuid();

            GivenDownload(id, localFilename: null, bytesTransferred: 1024);
            GivenTheRecordIsRemoved();

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true);

            var outcome = Assert.IsType<RemovalResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.False(outcome.Deleted);
            Assert.Null(outcome.Filename);
        }

        [Fact]
        public async Task A_Download_That_Never_Started_Is_A_Success()
        {
            // the path is recorded immediately before the download begins, so no path and no bytes
            // means it never got there: queued, rejected, timed out, or cancelled while waiting.
            // nothing was written anywhere, which is the end state asked for.
            var id = Guid.NewGuid();

            GivenDownload(
                id,
                localFilename: null,
                state: Soulseek.TransferStates.Completed | Soulseek.TransferStates.Cancelled,
                bytesTransferred: 0);
            GivenTheRecordIsRemoved();

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true);

            var outcome = Assert.IsType<RemovalResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.True(outcome.Removed);
            Assert.True(outcome.Deleted);
            Assert.Null(outcome.Filename);
            Assert.Null(outcome.Error);

            FileServiceMock.Verify(f => f.DeleteFilesAsync(It.IsAny<string[]>()), Times.Never);
        }

        [Fact]
        public async Task A_Prune_That_Throws_Cannot_Turn_A_Deletion_Into_A_Failure()
        {
            // the pruning walk runs *after* the file is gone. an exception escaping it would be caught
            // by the caller and reported as a deletion that failed -- the file deleted, the answer
            // saying otherwise. it is the same false report this whole result shape exists to prevent
            var root = Path.Combine(Temp, "complete");
            var nested = Path.Combine(root, "peer", "Some Album");
            Directory.CreateDirectory(nested);

            var filename = Path.Combine(nested, "01 track.flac");
            var id = Guid.NewGuid();

            GivenDownload(id, filename);
            GivenTheRecordIsRemoved();
            GivenDeletionResult(filename, true);

            FileServiceMock
                .Setup(f => f.DeleteDirectoriesAsync(It.IsAny<string[]>()))
                .ThrowsAsync(new IOException("the filesystem said no"));

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true);

            var outcome = Assert.IsType<RemovalResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.True(outcome.Deleted);
            Assert.Null(outcome.Error);
            Assert.Equal(0, outcome.PrunedDirectories);
        }
    }
}
