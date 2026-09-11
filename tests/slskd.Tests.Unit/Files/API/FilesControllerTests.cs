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
        /// <summary>
        ///     A value that isn't valid base 64, and so can't be decoded at all.
        /// </summary>
        private const string MalformedBase64 = "!!!not base 64!!!";

        /// <summary>
        ///     Valid base 64 that decodes to a string containing a null character. The decode succeeds, but the result
        ///     can't be resolved to a path.
        /// </summary>
        private const string Base64ContainingNullCharacter = "AA==";

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

        [Theory]
        [InlineData(MalformedBase64)]
        [InlineData(Base64ContainingNullCharacter)]
        public async Task GetDownloadSubdirectoryContentsAsync_Returns_BadRequest_Given_Undecodable_Name(string name)
        {
            var result = await Controller.GetDownloadSubdirectoryContentsAsync(name);

            AssertBadRequest(result);

            FileServiceMock.Verify(
                f => f.ListContentsAsync(It.IsAny<string>(), It.IsAny<EnumerationOptions>()),
                Times.Never);
        }

        [Theory]
        [InlineData(MalformedBase64)]
        [InlineData(Base64ContainingNullCharacter)]
        public async Task GetIncompleteSubdirectoryContentsAsync_Returns_BadRequest_Given_Undecodable_Name(string name)
        {
            var result = await Controller.GetIncompleteSubdirectoryContentsAsync(name);

            AssertBadRequest(result);
        }

        [Theory]
        [InlineData(MalformedBase64)]
        [InlineData(Base64ContainingNullCharacter)]
        public async Task DeleteDownloadSubdirectoryAsync_Returns_BadRequest_Given_Undecodable_Name(string name)
        {
            var result = await Controller.DeleteDownloadSubdirectoryAsync(name);

            AssertBadRequest(result);

            FileServiceMock.Verify(f => f.DeleteDirectoriesAsync(It.IsAny<string[]>()), Times.Never);
        }

        [Theory]
        [InlineData(MalformedBase64)]
        [InlineData(Base64ContainingNullCharacter)]
        public async Task DeleteIncompleteSubdirectoryAsync_Returns_BadRequest_Given_Undecodable_Name(string name)
        {
            var result = await Controller.DeleteIncompleteSubdirectoryAsync(name);

            AssertBadRequest(result);
        }

        [Theory]
        [InlineData(MalformedBase64)]
        [InlineData(Base64ContainingNullCharacter)]
        public async Task DeleteDownloadFileAsync_Returns_BadRequest_Given_Undecodable_Name(string name)
        {
            var result = await Controller.DeleteDownloadFileAsync(name);

            AssertBadRequest(result);

            FileServiceMock.Verify(f => f.DeleteFilesAsync(It.IsAny<string[]>()), Times.Never);
        }

        [Theory]
        [InlineData(MalformedBase64)]
        [InlineData(Base64ContainingNullCharacter)]
        public async Task DeleteIncompleteFileAsync_Returns_BadRequest_Given_Undecodable_Name(string name)
        {
            var result = await Controller.DeleteIncompleteFileAsync(name);

            AssertBadRequest(result);
        }

        [Fact]
        public async Task DeleteDownloadFileAsync_Returns_Forbidden_Given_Undecodable_Name_And_RemoteFileManagement_Disabled()
        {
            // remote file management is checked before the name is decoded, and that ordering is deliberate:
            // a disabled feature shouldn't report anything about the request it was handed.
            OptionsSnapshotMock.Setup(o => o.Value).Returns(new Options
            {
                RemoteFileManagement = false,
                Directories = new Options.DirectoriesOptions
                {
                    Downloads = Downloads,
                    Incomplete = Incomplete,
                },
            });

            var result = await Controller.DeleteDownloadFileAsync(MalformedBase64);

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

        [Fact]
        public async Task GetDownloadSubdirectoryContentsAsync_Resolves_The_Decoded_Name_Against_The_Downloads_Directory()
        {
            FileServiceMock
                .Setup(f => f.ListContentsAsync(It.IsAny<string>(), It.IsAny<EnumerationOptions>()))
                .ReturnsAsync(new FilesystemDirectory());

            var result = await Controller.GetDownloadSubdirectoryContentsAsync("foo/bar".ToBase64());

            Assert.IsType<OkObjectResult>(result);

            FileServiceMock.Verify(
                f => f.ListContentsAsync(
                    Path.GetFullPath(Path.Combine(Downloads, "foo", "bar")),
                    It.IsAny<EnumerationOptions>()),
                Times.Once);
        }

        [Fact]
        public async Task DeleteDownloadFileAsync_Resolves_The_Decoded_Name_Against_The_Downloads_Directory()
        {
            var expected = Path.GetFullPath(Path.Combine(Downloads, "foo", "bar.mp3"));

            FileServiceMock
                .Setup(f => f.DeleteFilesAsync(It.IsAny<string[]>()))
                .ReturnsAsync((string[] files) => files.ToDictionary(f => f, _ => (OneOf<bool, Exception>)true));

            var result = await Controller.DeleteDownloadFileAsync("/foo/bar.mp3".ToBase64());

            Assert.IsType<NoContentResult>(result);

            FileServiceMock.Verify(f => f.DeleteFilesAsync(new[] { expected }), Times.Once);
        }

        private static Dictionary<string, OneOf<bool, Exception>> Failures(string[] paths, Exception exception)
            => paths.ToDictionary(p => p, _ => (OneOf<bool, Exception>)exception);

        private static void AssertBadRequest(IActionResult result)
        {
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(400, badRequest.StatusCode);
        }
    }
}
