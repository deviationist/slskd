namespace slskd.Tests.Unit.Integrations.OneTagger;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using slskd.Integrations.OneTagger;
using Xunit;
using OneTaggerOptions = slskd.Options.IntegrationsOptions.OneTaggerOptions;

/// <summary>
///     The address that opens a download in OneTagger's Tag Editor: the file, and its folder, as OneTagger sees them.
/// </summary>
public class OneTaggerLinkTests
{
    [Fact]
    public void A_download_opens_in_the_Tag_Editor_on_its_folder_under_OneTaggers_path_for_the_downloads()
    {
        var options = new OneTaggerOptions { Enabled = true, Url = "https://ot.example/", Downloads = "/downloads" };

        var url = OneTaggerLink.For(options, "/app/downloads", "/app/downloads/complete/Artist - Album/01 Track & Co.flac");

        Assert.Equal(
            "https://ot.example/#/tageditor?path=%2Fdownloads%2Fcomplete%2FArtist%20-%20Album&file=%2Fdownloads%2Fcomplete%2FArtist%20-%20Album%2F01%20Track%20%26%20Co.flac",
            url);
    }

    [Fact]
    public void Unmapped_OneTagger_is_taken_to_see_the_downloads_where_slskd_does()
    {
        var options = new OneTaggerOptions { Enabled = true, Url = "https://ot.example" };

        var url = OneTaggerLink.For(options, "/app/downloads/", "/app/downloads/one.mp3");

        Assert.Equal("https://ot.example/#/tageditor?path=%2Fapp%2Fdownloads&file=%2Fapp%2Fdownloads%2Fone.mp3", url);
    }

    [Fact]
    public void A_OneTagger_on_Windows_gets_its_own_separators()
    {
        var options = new OneTaggerOptions { Enabled = true, Url = "https://ot.example", Downloads = @"D:\Music\slskd\" };

        var url = OneTaggerLink.For(options, "/app/downloads", "/app/downloads/a/b.flac");

        Assert.Equal(@"D:\Music\slskd\a\b.flac", Uri.UnescapeDataString(url.Split("file=")[1]));
        Assert.Equal(@"D:\Music\slskd\a", Uri.UnescapeDataString(url.Split("path=")[1].Split('&')[0]));
    }

    [Theory]
    [InlineData("/app/elsewhere/one.mp3")]
    [InlineData("/app/downloads-other/one.mp3")]
    [InlineData("/app/downloads/../one.mp3")]
    public void A_file_outside_the_downloads_gets_no_link(string file)
    {
        var options = new OneTaggerOptions { Enabled = true, Url = "https://ot.example", Downloads = "/downloads" };

        Assert.Null(OneTaggerLink.For(options, "/app/downloads", file));
    }

    [Theory]
    [InlineData(false, false, null, null, true)]
    [InlineData(true, false, "https://ot.example", null, true)]
    [InlineData(false, true, "https://ot.example", null, true)]
    [InlineData(true, false, "https://ot.example", "/downloads", true)]
    [InlineData(true, false, "https://ot.example", @"D:\Music", true)]
    [InlineData(true, false, null, null, false)]
    [InlineData(false, true, "ot.example", null, false)]
    [InlineData(true, false, "ftp://ot.example", null, false)]
    [InlineData(true, false, "https://ot.example", "downloads", false)]
    public void Settings_need_an_address_when_anything_is_on_and_an_absolute_downloads_path(bool enabled, bool menu, string url, string downloads, bool valid)
    {
        var options = new OneTaggerOptions { Enabled = enabled, Menu = menu, Url = url, Downloads = downloads };

        Assert.Equal(valid, !options.Validate(new ValidationContext(options)).Any());
    }

    [Theory]
    [InlineData("downloads", true)]
    [InlineData("home", true)]
    [InlineData("Downloads", false)]
    [InlineData("library", false)]
    public void The_menu_opens_the_downloads_or_home(string opens, bool valid)
    {
        var options = new OneTaggerOptions { Menu = true, Url = "https://ot.example", MenuOpens = opens };

        Assert.Equal(valid, !options.Validate(new ValidationContext(options)).Any());
    }
}
