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
    ///     Removing a download has never touched the disk, so everything here is about the one query
    ///     parameter that changes that: that it is refused unless the option is on, that it only ever
    ///     deletes a file this application recorded writing, and that the answer says which of the three
    ///     outcomes happened rather than leaving the caller to infer it from a 204.
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

        private void GivenDownload(Guid id, string localFilename, Soulseek.TransferStates state = Soulseek.TransferStates.Completed | Soulseek.TransferStates.Succeeded)
            => DownloadsMock
                .Setup(d => d.Find(It.IsAny<Expression<Func<Transfer, bool>>>()))
                .Returns(new Transfer { Id = id, LocalFilename = localFilename, State = state });

        private void GivenDeletionResult(string filename, OneOf<bool, Exception> result)
            => FileServiceMock
                .Setup(f => f.DeleteFilesAsync(It.IsAny<string[]>()))
                .ReturnsAsync(new Dictionary<string, OneOf<bool, Exception>> { { filename, result } });

        [Fact]
        public async Task Removing_Without_Asking_To_Delete_Answers_204_And_Touches_No_File()
        {
            var id = Guid.NewGuid();

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true);

            Assert.IsType<NoContentResult>(result);
            FileServiceMock.Verify(f => f.DeleteFilesAsync(It.IsAny<string[]>()), Times.Never);
        }

        [Fact]
        public async Task Deleting_Without_Removing_Is_Refused()
        {
            // the option is `delete_file_on_removal`, and that is the whole scope of what it grants.
            // it would also leave a transfer listed as a completed download whose file is not there,
            // which is the stale state this feature exists to stop producing
            var id = Guid.NewGuid();
            GivenDownload(id, Path.Combine(Path.GetTempPath(), "downloads", "01 track.flac"));

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: false, deleteFile: true);

            Assert.IsType<BadRequestObjectResult>(result);
            FileServiceMock.Verify(f => f.DeleteFilesAsync(It.IsAny<string[]>()), Times.Never);

            // refused before anything happened at all, cancellation included
            DownloadsMock.Verify(d => d.TryCancel(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task Cancelling_Without_Removing_Still_Works()
        {
            // the pairing is only required when a deletion is asked for; cancel-without-remove is what
            // the Cancel button has always done
            var result = await Controller.CancelDownloadAsync("user", Guid.NewGuid().ToString(), remove: false);

            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public async Task Deleting_Is_Forbidden_When_The_Option_Is_Disabled()
        {
            SetDeleteFileOnRemoval(false);
            var id = Guid.NewGuid();

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true, deleteFile: true);

            Assert.IsType<ForbidResult>(result);
            FileServiceMock.Verify(f => f.DeleteFilesAsync(It.IsAny<string[]>()), Times.Never);

            // and the transfer is left alone; a refused request must not half-happen
            DownloadsMock.Verify(d => d.Remove(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task Deleting_Answers_404_When_There_Is_No_Such_Download()
        {
            DownloadsMock
                .Setup(d => d.Find(It.IsAny<Expression<Func<Transfer, bool>>>()))
                .Returns((Transfer)null);

            var result = await Controller.CancelDownloadAsync("user", Guid.NewGuid().ToString(), remove: true, deleteFile: true);

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Deleting_Removes_The_Recorded_File()
        {
            var id = Guid.NewGuid();
            var filename = Path.Combine(Path.GetTempPath(), "downloads", "album", "01 track.flac");

            GivenDownload(id, filename);
            GivenDeletionResult(filename, true);

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true, deleteFile: true);

            var deletion = Assert.IsType<FileDeletionResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.True(deletion.Deleted);
            Assert.Equal(filename, deletion.Filename);
            Assert.Null(deletion.Error);

            FileServiceMock.Verify(f => f.DeleteFilesAsync(filename), Times.Once);
            DownloadsMock.Verify(d => d.Remove(id), Times.Once);
        }

        [Fact]
        public async Task Deleting_A_Download_With_No_Recorded_File_Deletes_Nothing_And_Says_So()
        {
            // a download that never completed, or one that completed before this was recorded. there is
            // no honest path to delete, and guessing one is the thing this feature must not do
            var id = Guid.NewGuid();
            GivenDownload(id, localFilename: null);

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true, deleteFile: true);

            var deletion = Assert.IsType<FileDeletionResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.False(deletion.Deleted);
            Assert.Null(deletion.Filename);
            Assert.Null(deletion.Error);

            FileServiceMock.Verify(f => f.DeleteFilesAsync(It.IsAny<string[]>()), Times.Never);

            // the record still goes; only the file was in question
            DownloadsMock.Verify(d => d.Remove(id), Times.Once);
        }

        [Fact]
        public async Task A_Cancelled_Download_Has_Its_Partial_File_Deleted()
        {
            // the recorded path is the incomplete file until the download finishes and it is moved, so a
            // transfer that was cancelled or failed still names something to delete. this is the case
            // that leaves litter in the incomplete directory otherwise -- slskd keeps partials on
            // purpose, to resume from, and only a retention timer ever removes them
            var id = Guid.NewGuid();
            var partial = Path.Combine(Path.GetTempPath(), "incomplete", "peer", "album", "01 track.flac");

            GivenDownload(id, partial, Soulseek.TransferStates.Completed | Soulseek.TransferStates.Cancelled);
            GivenDeletionResult(partial, true);

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true, deleteFile: true);

            var deletion = Assert.IsType<FileDeletionResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.True(deletion.Deleted);
            Assert.Equal(partial, deletion.Filename);

            FileServiceMock.Verify(f => f.DeleteFilesAsync(partial), Times.Once);
        }

        [Fact]
        public async Task A_Running_Download_Is_Not_Deleted_From_Under_Itself()
        {
            // unlinking a file that is being written to is either allowed and confusing (POSIX: the
            // writer keeps the inode and the move at the end fails) or refused outright (Windows:
            // FileShare.None). cancel first
            var id = Guid.NewGuid();
            var partial = Path.Combine(Path.GetTempPath(), "incomplete", "peer", "album", "01 track.flac");

            GivenDownload(id, partial, Soulseek.TransferStates.InProgress);

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true, deleteFile: true);

            var deletion = Assert.IsType<FileDeletionResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.False(deletion.Deleted);
            Assert.Equal(partial, deletion.Filename);
            Assert.Contains("cancel it first", deletion.Error);

            FileServiceMock.Verify(f => f.DeleteFilesAsync(It.IsAny<string[]>()), Times.Never);

            // and it is still cancelled, which is what the caller asked for first
            DownloadsMock.Verify(d => d.TryCancel(id), Times.Once);
        }

        [Fact]
        public async Task A_Refused_Deletion_Is_Reported_Rather_Than_Thrown()
        {
            // the file service refuses anything outside the downloads and incomplete directories, which
            // a recorded path can become if those are reconfigured. the removal has already happened by
            // then and must not read as a failure of the whole request
            var id = Guid.NewGuid();
            var filename = Path.Combine(Path.GetTempPath(), "elsewhere", "01 track.flac");

            GivenDownload(id, filename);
            GivenDeletionResult(filename, new UnauthorizedException("nope"));

            var result = await Controller.CancelDownloadAsync("user", id.ToString(), remove: true, deleteFile: true);

            var deletion = Assert.IsType<FileDeletionResult>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.False(deletion.Deleted);
            Assert.Equal(filename, deletion.Filename);
            Assert.Equal("nope", deletion.Error);
        }
    }
}
