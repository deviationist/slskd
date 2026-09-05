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
    ///     parameter that changes that: that it is refused when remote file management is off, that it
    ///     only ever deletes a file this application recorded writing, and that the answer says which of
    ///     the three outcomes happened rather than leaving the caller to infer it from a 204.
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
            SetRemoteFileManagement(true);
        }

        private Mock<IDownloadService> DownloadsMock { get; }
        private TransferService TransferService { get; }
        private Mock<FileService> FileServiceMock { get; }
        private Mock<IOptionsSnapshot<slskd.Options>> OptionsSnapshotMock { get; }

        private void SetRemoteFileManagement(bool enabled)
            => OptionsSnapshotMock.SetupGet(o => o.Value).Returns(new slskd.Options { RemoteFileManagement = enabled });

        private TransfersController Controller => new(
            transferService: TransferService,
            userService: new Mock<IUserService>().Object,
            fileService: FileServiceMock.Object,
            optionsSnapshot: OptionsSnapshotMock.Object);

        private void GivenDownload(Guid id, string localFilename)
            => DownloadsMock
                .Setup(d => d.Find(It.IsAny<Expression<Func<Transfer, bool>>>()))
                .Returns(new Transfer { Id = id, LocalFilename = localFilename });

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
        public async Task Deleting_Is_Forbidden_When_Remote_File_Management_Is_Disabled()
        {
            SetRemoteFileManagement(false);
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
