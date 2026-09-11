using Microsoft.Extensions.Options;

namespace slskd.Tests.Unit.Files.API
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Moq;
    using OneOf;
    using slskd.Files;
    using slskd.Files.API;
    using Xunit;

    public class FilesControllerTests
    {
        public FilesControllerTests()
        {
            Downloads = Path.Combine(Path.GetTempPath(), $"slskd.test.{Guid.NewGuid()}", "downloads");
            Incomplete = Path.Combine(Path.GetTempPath(), $"slskd.test.{Guid.NewGuid()}", "incomplete");

            FileServiceMock = new Mock<FileService>(new Mock<IOptionsMonitor<Options>>().Object);

            OptionsSnapshotMock = new Mock<IOptionsSnapshot<Options>>();
            OptionsSnapshotMock.Setup(o => o.Value).Returns(new Options
            {
                RemoteFileManagement = true,
                Directories = new Options.DirectoriesOptions
                {
                    Downloads = Downloads,
                    Incomplete = Incomplete,
                },
            });

            Controller = new FilesController(
                fileService: FileServiceMock.Object,
                optionsSnapshot: OptionsSnapshotMock.Object);
        }

        private string Downloads { get; init; }
        private string Incomplete { get; init; }
        private Mock<FileService> FileServiceMock { get; init; }
        private Mock<IOptionsSnapshot<Options>> OptionsSnapshotMock { get; init; }
        private FilesController Controller { get; init; }

        [Fact]
        public async Task GetDownloadSubdirectoryContentsAsync_Returns_Forbidden_Given_Unauthorized_Directory()
        {
            FileServiceMock
                .Setup(f => f.ListContentsAsync(It.IsAny<string>(), It.IsAny<EnumerationOptions>()))
                .ThrowsAsync(new UnauthorizedException("Only application-controlled directories can be listed"));

            var result = await Controller.GetDownloadSubdirectoryContentsAsync("../../etc".ToBase64());

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task GetDownloadSubdirectoryContentsAsync_Returns_Forbidden_Given_SecurityException()
        {
            FileServiceMock
                .Setup(f => f.ListContentsAsync(It.IsAny<string>(), It.IsAny<EnumerationOptions>()))
                .ThrowsAsync(new SecurityException("nope"));

            var result = await Controller.GetDownloadSubdirectoryContentsAsync("foo".ToBase64());

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task GetIncompleteSubdirectoryContentsAsync_Returns_Forbidden_Given_Unauthorized_Directory()
        {
            FileServiceMock
                .Setup(f => f.ListContentsAsync(It.IsAny<string>(), It.IsAny<EnumerationOptions>()))
                .ThrowsAsync(new UnauthorizedException("Only application-controlled directories can be listed"));

            var result = await Controller.GetIncompleteSubdirectoryContentsAsync("../../etc".ToBase64());

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task DeleteDownloadSubdirectoryAsync_Returns_Forbidden_Given_Unauthorized_Directory()
        {
            FileServiceMock
                .Setup(f => f.DeleteDirectoriesAsync(It.IsAny<string[]>()))
                .ThrowsAsync(new UnauthorizedException("Only application-controlled directories can be deleted"));

            var result = await Controller.DeleteDownloadSubdirectoryAsync("../../etc".ToBase64());

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task DeleteDownloadSubdirectoryAsync_Returns_Forbidden_Given_Unauthorized_Result()
        {
            FileServiceMock
                .Setup(f => f.DeleteDirectoriesAsync(It.IsAny<string[]>()))
                .ReturnsAsync((string[] dirs) => Failures(dirs, new UnauthorizedException("denied")));

            var result = await Controller.DeleteDownloadSubdirectoryAsync("foo".ToBase64());

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task DeleteDownloadFileAsync_Returns_Forbidden_Given_Unauthorized_File()
        {
            FileServiceMock
                .Setup(f => f.DeleteFilesAsync(It.IsAny<string[]>()))
                .ThrowsAsync(new UnauthorizedException("Only files in application-controlled directories can be deleted"));

            var result = await Controller.DeleteDownloadFileAsync("../../etc/passwd".ToBase64());

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task DeleteDownloadFileAsync_Returns_Forbidden_Given_Unauthorized_Result()
        {
            FileServiceMock
                .Setup(f => f.DeleteFilesAsync(It.IsAny<string[]>()))
                .ReturnsAsync((string[] files) => Failures(files, new UnauthorizedException("denied")));

            var result = await Controller.DeleteDownloadFileAsync("foo.mp3".ToBase64());

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task DeleteIncompleteFileAsync_Returns_Forbidden_Given_Unauthorized_File()
        {
            FileServiceMock
                .Setup(f => f.DeleteFilesAsync(It.IsAny<string[]>()))
                .ThrowsAsync(new UnauthorizedException("Only files in application-controlled directories can be deleted"));

            var result = await Controller.DeleteIncompleteFileAsync("../../etc/passwd".ToBase64());

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task GetDownloadSubdirectoryContentsAsync_Returns_NotFound_Given_Missing_Directory()
        {
            FileServiceMock
                .Setup(f => f.ListContentsAsync(It.IsAny<string>(), It.IsAny<EnumerationOptions>()))
                .ThrowsAsync(new NotFoundException("nope"));

            var result = await Controller.GetDownloadSubdirectoryContentsAsync("foo".ToBase64());

            Assert.IsType<NotFoundResult>(result);
        }

        private static Dictionary<string, OneOf<bool, Exception>> Failures(string[] paths, Exception exception)
            => paths.ToDictionary(p => p, _ => (OneOf<bool, Exception>)exception);

    }
}
