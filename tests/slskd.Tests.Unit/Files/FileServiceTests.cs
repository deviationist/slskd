using Microsoft.Extensions.Options;

namespace slskd.Tests.Unit.Files
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Moq;
    using slskd.Files;
    using Xunit;

    public class FileServiceTests : IDisposable
    {
        public FileServiceTests()
        {
            OptionsMonitorMock = new Mock<IOptionsMonitor<Options>>();

            Temp = Path.Combine(Path.GetTempPath(), $"slskd.test.{Guid.NewGuid()}");
            Directory.CreateDirectory(Temp);

            FileService = new FileService(
                optionsMonitor: OptionsMonitorMock.Object);
        }

        public void Dispose()
        {
            Directory.Delete(Temp, recursive: true);
        }

        private Mock<IOptionsMonitor<Options>> OptionsMonitorMock { get; init; }
        private string Temp { get; init; }
        private FileService FileService { get; init; }

        [Fact]
        public async Task ListContentsAsync_Throws_ArgumentException_Given_Relative_Path()
        {
            var ex = await Record.ExceptionAsync(() => FileService.ListContentsAsync(directory: "../"));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentException>(ex);
            Assert.Equal("directory", ((ArgumentException)ex).ParamName);
        }

        [Fact]
        public async Task ListContentsAsync_Throws_UnauthorizedException_Given_Disallowed_Directory()
        {
            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options
            {
                Directories = new Options.DirectoriesOptions
                {
                    Downloads = Path.Combine(Temp, "downloads"),
                    Incomplete = Path.Combine(Temp, "incomplete"),
                }
            });

            var ex = await Record.ExceptionAsync(() => FileService.ListContentsAsync(directory: Path.Combine(Temp, "foo")));

            Assert.NotNull(ex);
            Assert.IsType<UnauthorizedException>(ex);
        }

        [Fact]
        public async Task ListContentsAsync_Throws_NotFoundException_Given_NonExistent_Directory()
        {
            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options
            {
                Directories = new Options.DirectoriesOptions
                {
                    Downloads = Path.Combine(Temp, "downloads"),
                    Incomplete = Path.Combine(Temp, "incomplete"),
                }
            });

            var ex = await Record.ExceptionAsync(() => FileService.ListContentsAsync(directory: Path.Combine(Temp, "downloads", "foo")));

            Assert.NotNull(ex);
            Assert.IsType<NotFoundException>(ex);
        }

        [Fact]
        public async Task DeleteDirectoriesAsync_Throws_ArgumentException_Given_Relative_Path()
        {
            var ex = await Record.ExceptionAsync(() => FileService.DeleteDirectoriesAsync("../foo"));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentException>(ex);
            Assert.Equal("directories", ((ArgumentException)ex).ParamName);
        }

        [Fact]
        public async Task DeleteDirectoriesAsync_Throws_ArgumentException_Given_Disallowed_Path()
        {
            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options
            {
                Directories = new Options.DirectoriesOptions
                {
                    Downloads = Path.Combine(Temp, "downloads"),
                    Incomplete = Path.Combine(Temp, "incomplete"),
                }
            });

            var ex = await Record.ExceptionAsync(() => FileService.DeleteDirectoriesAsync(Path.Combine(Temp, "foo")));

            Assert.NotNull(ex);
            Assert.IsType<UnauthorizedException>(ex);
        }

        [Fact]
        public async Task DeleteFilesAsync_Throws_ArgumentException_Given_Relative_Path()
        {
            var ex = await Record.ExceptionAsync(() => FileService.DeleteFilesAsync("../foo.bar"));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentException>(ex);
            Assert.Equal("files", ((ArgumentException)ex).ParamName);
        }

        [Fact]
        public async Task DeleteFilesAsync_Throws_ArgumentException_Given_Disallowed_Path()
        {
            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options
            {
                Directories = new Options.DirectoriesOptions
                {
                    Downloads = Path.Combine(Temp, "downloads"),
                    Incomplete = Path.Combine(Temp, "incomplete"),
                }
            });

            var ex = await Record.ExceptionAsync(() => FileService.DeleteFilesAsync(Path.Combine(Temp, "foo")));

            Assert.NotNull(ex);
            Assert.IsType<UnauthorizedException>(ex);
        }

        [Fact]
        public void CreateFile_Creates_Directory_When_It_Does_Not_Exist()
        {
            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options());

            var dir = Path.Combine(Temp, "newdir");
            var filename = Path.Combine(dir, "file.txt");

            using var stream = FileService.CreateFile(filename);

            Assert.True(Directory.Exists(dir));
        }

        [Fact]
        public void CreateFile_Creates_Directory_With_Unix_File_Mode_From_Options()
        {
            if (OperatingSystem.IsWindows()) return;

            var mode = "0755";

            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options
            {
                Transfers = new Options.TransfersOptions
                {
                    Download = new Options.TransfersOptions.GlobalDownloadOptions
                    {
                        Destination = new Options.TransfersOptions.GlobalDownloadOptions.DestinationOptions
                        {
                            Permissions = new Options.TransfersOptions.GlobalDownloadOptions.DestinationOptions.DestinationPermissionsOptions
                            {
                                Mode = mode,
                            }
                        }
                    }
                }
            });

            var dir = Path.Combine(Temp, "newdir");
            var filename = Path.Combine(dir, "file.txt");

            using var stream = FileService.CreateFile(filename);

            var dirInfo = new DirectoryInfo(dir);
            Assert.Equal(mode.ToUnixFileMode(), dirInfo.UnixFileMode);
        }

        [Fact]
        public void CreateFile_Creates_Directory_With_Unix_File_Mode_From_CreateFileOptions()
        {
            if (OperatingSystem.IsWindows()) return;

            var unixMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                         | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;

            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options());

            var dir = Path.Combine(Temp, "newdir");
            var filename = Path.Combine(dir, "file.txt");

            using var stream = FileService.CreateFile(filename, new CreateFileOptions
            {
                UnixCreateMode = unixMode,
            });

            var dirInfo = new DirectoryInfo(dir);
            Assert.Equal(unixMode, dirInfo.UnixFileMode);
        }

        [Fact]
        public void CreateFile_Creates_File_With_Unix_File_Mode()
        {
            if (OperatingSystem.IsWindows()) return;

            var mode = "0644";

            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options
            {
                Transfers = new Options.TransfersOptions
                {
                    Download = new Options.TransfersOptions.GlobalDownloadOptions
                    {
                        Destination = new Options.TransfersOptions.GlobalDownloadOptions.DestinationOptions
                        {
                            Permissions = new Options.TransfersOptions.GlobalDownloadOptions.DestinationOptions.DestinationPermissionsOptions
                            {
                                Mode = mode,
                            }
                        }
                    }
                }
            });

            var filename = Path.Combine(Temp, "file.txt");

            using (var stream = FileService.CreateFile(filename))
            {
            }

            Assert.Equal(mode.ToUnixFileMode(), File.GetUnixFileMode(filename));
        }

        [Fact]
        public void CreateFile_Creates_Directory_Without_Unix_File_Mode_When_Not_Configured()
        {
            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options());

            var dir = Path.Combine(Temp, "newdir");
            var filename = Path.Combine(dir, "file.txt");

            using var stream = FileService.CreateFile(filename);

            Assert.True(Directory.Exists(dir));
        }

        [Fact]
        public void MoveFile_Creates_Destination_Directory_When_It_Does_Not_Exist()
        {
            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options());

            var sourceDir = Path.Combine(Temp, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "file.txt");
            File.WriteAllText(sourceFile, "test");

            var destDir = Path.Combine(Temp, "dest");

            FileService.MoveFile(sourceFile, destDir);

            Assert.True(Directory.Exists(destDir));
            Assert.True(File.Exists(Path.Combine(destDir, "file.txt")));
        }

        [Fact]
        public void MoveFile_Creates_Destination_Directory_With_Unix_File_Mode_From_Options()
        {
            if (OperatingSystem.IsWindows()) return;

            var mode = "0755";

            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options
            {
                Transfers = new Options.TransfersOptions
                {
                    Download = new Options.TransfersOptions.GlobalDownloadOptions
                    {
                        Destination = new Options.TransfersOptions.GlobalDownloadOptions.DestinationOptions
                        {
                            Permissions = new Options.TransfersOptions.GlobalDownloadOptions.DestinationOptions.DestinationPermissionsOptions
                            {
                                Mode = mode,
                            }
                        }
                    }
                }
            });

            var sourceDir = Path.Combine(Temp, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "file.txt");
            File.WriteAllText(sourceFile, "test");

            var destDir = Path.Combine(Temp, "dest");

            FileService.MoveFile(sourceFile, destDir);

            var dirInfo = new DirectoryInfo(destDir);
            Assert.Equal(mode.ToUnixFileMode(), dirInfo.UnixFileMode);
        }

        [Fact]
        public void MoveFile_Creates_Destination_Directory_With_Unix_File_Mode_From_Parameter()
        {
            if (OperatingSystem.IsWindows()) return;

            var unixMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                         | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;

            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options());

            var sourceDir = Path.Combine(Temp, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "file.txt");
            File.WriteAllText(sourceFile, "test");

            var destDir = Path.Combine(Temp, "dest");

            FileService.MoveFile(sourceFile, destDir, unixFileMode: unixMode);

            var dirInfo = new DirectoryInfo(destDir);
            Assert.Equal(unixMode, dirInfo.UnixFileMode);
        }

        [Fact]
        public void MoveFile_Sets_Unix_File_Mode_On_Moved_File()
        {
            if (OperatingSystem.IsWindows()) return;

            var mode = "0644";

            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options
            {
                Transfers = new Options.TransfersOptions
                {
                    Download = new Options.TransfersOptions.GlobalDownloadOptions
                    {
                        Destination = new Options.TransfersOptions.GlobalDownloadOptions.DestinationOptions
                        {
                            Permissions = new Options.TransfersOptions.GlobalDownloadOptions.DestinationOptions.DestinationPermissionsOptions
                            {
                                Mode = mode,
                            }
                        }
                    }
                }
            });

            var sourceDir = Path.Combine(Temp, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "file.txt");
            File.WriteAllText(sourceFile, "test");

            var destDir = Path.Combine(Temp, "dest");
            Directory.CreateDirectory(destDir);

            var result = FileService.MoveFile(sourceFile, destDir);

            Assert.Equal(mode.ToUnixFileMode(), File.GetUnixFileMode(result));
        }

        [Fact]
        public void MoveFile_Creates_Destination_Directory_Without_Unix_File_Mode_When_Not_Configured()
        {
            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options());

            var sourceDir = Path.Combine(Temp, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "file.txt");
            File.WriteAllText(sourceFile, "test");

            var destDir = Path.Combine(Temp, "dest");

            FileService.MoveFile(sourceFile, destDir);

            Assert.True(Directory.Exists(destDir));
            Assert.True(File.Exists(Path.Combine(destDir, "file.txt")));
        }

        [Fact]
        public void MoveFile_Prefers_Parameter_Unix_File_Mode_Over_Options()
        {
            if (OperatingSystem.IsWindows()) return;

            var optionsMode = "0644";
            var paramMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                          | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;

            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options
            {
                Transfers = new Options.TransfersOptions
                {
                    Download = new Options.TransfersOptions.GlobalDownloadOptions
                    {
                        Destination = new Options.TransfersOptions.GlobalDownloadOptions.DestinationOptions
                        {
                            Permissions = new Options.TransfersOptions.GlobalDownloadOptions.DestinationOptions.DestinationPermissionsOptions
                            {
                                Mode = optionsMode,
                            }
                        }
                    }
                }
            });

            var sourceDir = Path.Combine(Temp, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "file.txt");
            File.WriteAllText(sourceFile, "test");

            var destDir = Path.Combine(Temp, "dest");

            FileService.MoveFile(sourceFile, destDir, unixFileMode: paramMode);

            var dirInfo = new DirectoryInfo(destDir);
            Assert.Equal(paramMode, dirInfo.UnixFileMode);
            Assert.Equal(paramMode, File.GetUnixFileMode(Path.Combine(destDir, "file.txt")));
        }

        [Fact]
        public void CreateFile_Prefers_CreateFileOptions_Unix_Mode_Over_Options()
        {
            if (OperatingSystem.IsWindows()) return;

            var optionsMode = "0644";
            var paramMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                          | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;

            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options
            {
                Transfers = new Options.TransfersOptions
                {
                    Download = new Options.TransfersOptions.GlobalDownloadOptions
                    {
                        Destination = new Options.TransfersOptions.GlobalDownloadOptions.DestinationOptions
                        {
                            Permissions = new Options.TransfersOptions.GlobalDownloadOptions.DestinationOptions.DestinationPermissionsOptions
                            {
                                Mode = optionsMode,
                            }
                        }
                    }
                }
            });

            var dir = Path.Combine(Temp, "newdir");
            var filename = Path.Combine(dir, "file.txt");

            using (var stream = FileService.CreateFile(filename, new CreateFileOptions
            {
                UnixCreateMode = paramMode,
            }))
            {
            }

            var dirInfo = new DirectoryInfo(dir);
            Assert.Equal(paramMode, dirInfo.UnixFileMode);
            Assert.Equal(paramMode, File.GetUnixFileMode(filename));
        }

        [Fact]
        public void OpenFile_Throws_ArgumentException_Given_Null_Filename()
        {
            var ex = Record.Exception(() => FileService.OpenFile(null));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentNullException>(ex, exactMatch: false);
        }

        [Fact]
        public void OpenFile_Throws_ArgumentException_Given_Relative_Path()
        {
            var ex = Record.Exception(() => FileService.OpenFile("../foo.bar"));

            Assert.NotNull(ex);
            Assert.IsType<ArgumentException>(ex);
            Assert.Equal("filename", ((ArgumentException)ex).ParamName);
        }

        [Fact]
        public void OpenFile_Throws_UnauthorizedException_Given_Disallowed_Path()
        {
            UseTempDirectories();

            // a real file, sitting outside of both of the allowed directories
            var filename = Path.Combine(Temp, "secret.txt");
            File.WriteAllText(filename, "secret");

            var ex = Record.Exception(() => FileService.OpenFile(filename));

            Assert.NotNull(ex);
            Assert.IsType<UnauthorizedException>(ex);
        }

        [Fact]
        public void OpenFile_Throws_UnauthorizedException_Rather_Than_NotFoundException_Given_NonExistent_Disallowed_Path()
        {
            UseTempDirectories();

            // this is the assertion that a refused path is never opened, and never even looked at: were containment
            // checked after the filesystem, a path outside of the allowed directories that does not exist would be
            // reported as not found, and the difference between the two answers would disclose which files exist
            var ex = Record.Exception(() => FileService.OpenFile(Path.Combine(Temp, "does-not-exist.txt")));

            Assert.NotNull(ex);
            Assert.IsType<UnauthorizedException>(ex);
        }

        [Fact]
        public void OpenFile_Throws_NotFoundException_Given_NonExistent_File()
        {
            UseTempDirectories();

            var ex = Record.Exception(() => FileService.OpenFile(Path.Combine(Temp, "downloads", "nope.mp3")));

            Assert.NotNull(ex);
            Assert.IsType<NotFoundException>(ex);
        }

        [Fact]
        public void OpenFile_Throws_NotFoundException_Given_Directory()
        {
            UseTempDirectories();

            var dir = Path.Combine(Temp, "downloads", "album");
            Directory.CreateDirectory(dir);

            var ex = Record.Exception(() => FileService.OpenFile(dir));

            Assert.NotNull(ex);
            Assert.IsType<NotFoundException>(ex);
        }

        [Fact]
        public void OpenFile_Returns_Contents_Of_File_In_Downloads()
        {
            UseTempDirectories();

            var filename = Path.Combine(Temp, "downloads", "song.mp3");
            File.WriteAllText(filename, "contents");

            using var stream = FileService.OpenFile(filename);
            using var reader = new StreamReader(stream);

            Assert.Equal("contents", reader.ReadToEnd());
        }

        [Fact]
        public void OpenFile_Returns_Contents_Of_File_In_Subdirectory_Of_Incomplete()
        {
            UseTempDirectories();

            var dir = Path.Combine(Temp, "incomplete", "artist", "album");
            Directory.CreateDirectory(dir);

            var filename = Path.Combine(dir, "song.flac");
            File.WriteAllText(filename, "partial");

            using var stream = FileService.OpenFile(filename);
            using var reader = new StreamReader(stream);

            Assert.Equal("partial", reader.ReadToEnd());
        }

        [Fact]
        public void OpenFile_Throws_UnauthorizedException_Given_Symlink_Pointing_Outside_Allowed_Directories()
        {
            if (OperatingSystem.IsWindows()) return;

            UseTempDirectories();

            // the link is within an allowed directory, but its target is not. opening the link would follow it,
            // so the resolved target has to be checked as well as the path that was asked for
            var target = Path.Combine(Temp, "secret.txt");
            File.WriteAllText(target, "secret");

            var link = Path.Combine(Temp, "downloads", "innocent.mp3");
            File.CreateSymbolicLink(link, target);

            var ex = Record.Exception(() => FileService.OpenFile(link));

            Assert.NotNull(ex);
            Assert.IsType<UnauthorizedException>(ex);
        }

        [Fact]
        public void OpenFile_Returns_Contents_Given_Symlink_Pointing_Within_Allowed_Directories()
        {
            if (OperatingSystem.IsWindows()) return;

            UseTempDirectories();

            var target = Path.Combine(Temp, "incomplete", "song.flac");
            File.WriteAllText(target, "contents");

            var link = Path.Combine(Temp, "downloads", "song.flac");
            File.CreateSymbolicLink(link, target);

            using var stream = FileService.OpenFile(link);
            using var reader = new StreamReader(stream);

            Assert.Equal("contents", reader.ReadToEnd());
        }

        [Fact]
        public void OpenFile_Does_Not_Prevent_The_File_From_Being_Written_To()
        {
            UseTempDirectories();

            // a file that is still being downloaded must remain writable while it is being read
            var filename = Path.Combine(Temp, "incomplete", "song.flac");
            File.WriteAllText(filename, "partial");

            using var stream = FileService.OpenFile(filename);

            var ex = Record.Exception(() =>
            {
                using var writer = new FileStream(filename, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                writer.WriteByte(0x00);
            });

            Assert.Null(ex);
        }

        [Fact]
        public void OpenFile_Returns_A_Seekable_Stream()
        {
            UseTempDirectories();

            // range requests are served by seeking the returned Stream; a non-seekable one would silently
            // degrade every response to a whole-file transfer
            var filename = Path.Combine(Temp, "downloads", "song.mp3");
            File.WriteAllText(filename, "contents");

            using var stream = FileService.OpenFile(filename);

            Assert.True(stream.CanSeek);
            Assert.Equal(8, stream.Length);
        }

        private void UseTempDirectories()
        {
            OptionsMonitorMock.Setup(o => o.CurrentValue).Returns(new Options
            {
                Directories = new Options.DirectoriesOptions
                {
                    Downloads = Path.Combine(Temp, "downloads"),
                    Incomplete = Path.Combine(Temp, "incomplete"),
                }
            });

            Directory.CreateDirectory(Path.Combine(Temp, "downloads"));
            Directory.CreateDirectory(Path.Combine(Temp, "incomplete"));
        }
    }
}

