namespace slskd.Tests.Unit.Transfers
{
    using System;
    using System.Linq;
    using slskd.Transfers;
    using Xunit;

    /// <summary>
    ///     Naming the entries, and the file, of an archive of downloaded files.
    /// </summary>
    /// <remarks>
    ///     These names are built from strings a remote peer chose, and two of them landing on one name means one of
    ///     the two files is not in the archive. Everything here is about that not happening quietly.
    /// </remarks>
    public class ArchiveNamingTests
    {
        [Fact]
        public void EntryNames_Keep_The_Folder_So_Identical_Filenames_Do_Not_Collide()
        {
            // the case this exists for: every album has a track 1
            var names = ArchiveNaming.EntryNamesFor([
                @"@@abcde\Music\Aphex Twin - Drukqs\01 - Intro.flac",
                @"@@abcde\Music\Boards of Canada - Geogaddi\01 - Intro.flac",
            ]);

            Assert.Equal("Aphex Twin - Drukqs/01 - Intro.flac", names[0]);
            Assert.Equal("Boards of Canada - Geogaddi/01 - Intro.flac", names[1]);
            Assert.Equal(2, names.Distinct().Count());
        }

        [Fact]
        public void EntryNames_Number_A_Collision_Rather_Than_Overwriting()
        {
            // two albums whose folders are both called 'CD1'. keeping the folder is not enough here, and an archive
            // with the same entry twice loses one of them on extraction
            var names = ArchiveNaming.EntryNamesFor([
                @"@@abcde\Some Album\CD1\01 - Intro.flac",
                @"@@abcde\Another Album\CD1\01 - Intro.flac",
                @"@@abcde\A Third\CD1\01 - Intro.flac",
            ]);

            Assert.Equal("CD1/01 - Intro.flac", names[0]);
            Assert.Equal("CD1/01 - Intro (2).flac", names[1]);
            Assert.Equal("CD1/01 - Intro (3).flac", names[2]);
            Assert.Equal(3, names.Distinct().Count());
        }

        [Fact]
        public void EntryNames_Treat_A_Collision_Of_Case_Alone_As_A_Collision()
        {
            // zip entry names are case-sensitive; the filesystem the archive is extracted onto very likely is not
            var names = ArchiveNaming.EntryNamesFor([
                @"\Album\Track.flac",
                @"\Album\track.flac",
            ]);

            Assert.Equal(2, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void EntryNames_Do_Not_Take_The_Name_Reserved_For_The_Skipped_List()
        {
            // a peer sharing a file called MISSING.txt in the root of a folder must not be able to displace, or be
            // displaced by, the list of files that were skipped
            var names = ArchiveNaming.EntryNamesFor(["MISSING.txt"]);

            Assert.NotEqual(ArchiveNaming.MissingEntryName, names[0]);
        }

        [Fact]
        public void EntryNames_Strip_Traversal_Segments()
        {
            // these names come from a remote peer, and an entry name that traverses is a trap for whatever unpacks
            // the archive rather than for this application
            var names = ArchiveNaming.EntryNamesFor([@"\..\..\etc\passwd"]);

            Assert.DoesNotContain("..", names[0]);
            Assert.Equal("etc/passwd", names[0]);
        }

        [Fact]
        public void EntryNames_Handle_A_File_With_No_Folder()
        {
            var names = ArchiveNaming.EntryNamesFor(["loose.mp3"]);

            Assert.Equal("loose.mp3", names[0]);
        }

        [Fact]
        public void EntryNames_Give_A_Nameless_File_A_Name()
        {
            // nothing survives sanitization, and an entry with no name at all is not something to hand an extractor
            var names = ArchiveNaming.EntryNamesFor([@"\\\"]);

            Assert.False(string.IsNullOrWhiteSpace(names[0]));
        }

        [Fact]
        public void EntryNames_Are_Returned_In_The_Order_Given()
        {
            var names = ArchiveNaming.EntryNamesFor([@"\B\b.flac", @"\A\a.flac"]);

            Assert.Equal("B/b.flac", names[0]);
            Assert.Equal("A/a.flac", names[1]);
        }

        [Fact]
        public void ArchiveName_Is_The_Folder_When_Everything_Came_From_One()
        {
            var name = ArchiveNaming.ArchiveNameFor(
                "somebody",
                [@"@@abcde\Music\Aphex Twin - Drukqs\01.flac", @"@@abcde\Music\Aphex Twin - Drukqs\02.flac"],
                new DateTime(2026, 9, 11, 13, 45, 1, DateTimeKind.Utc));

            Assert.Equal("Aphex Twin - Drukqs.zip", name);
        }

        [Fact]
        public void ArchiveName_Is_The_User_And_The_Time_When_It_Came_From_Several()
        {
            var name = ArchiveNaming.ArchiveNameFor(
                "somebody",
                [@"\One\01.flac", @"\Two\02.flac"],
                new DateTime(2026, 9, 11, 13, 45, 1, DateTimeKind.Utc));

            Assert.Equal("somebody-20260911-134501.zip", name);
        }

        [Fact]
        public void ArchiveName_Sanitizes_A_Username()
        {
            var name = ArchiveNaming.ArchiveNameFor(
                "some/body",
                [@"\One\01.flac", @"\Two\02.flac"],
                new DateTime(2026, 9, 11, 13, 45, 1, DateTimeKind.Utc));

            Assert.DoesNotContain("/", name);
        }

        [Fact]
        public void ArchiveName_Falls_Back_When_A_Username_Sanitizes_To_Nothing()
        {
            var name = ArchiveNaming.ArchiveNameFor(
                string.Empty,
                [@"\One\01.flac", @"\Two\02.flac"],
                new DateTime(2026, 9, 11, 13, 45, 1, DateTimeKind.Utc));

            Assert.Equal("downloads-20260911-134501.zip", name);
        }
    }
}
