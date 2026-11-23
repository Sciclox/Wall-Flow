using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallFlow.Services;
using Xunit;

namespace WallFlow.Tests;

public class ActiveWindowManagerTests
{
    [Fact]
    public void ComputeChromaticColor_MusicApps_ReturnsGreen()
    {
        var colorSpotify = ActiveWindowManager.ComputeChromaticColor("spotify", "Spotify Free");
        Assert.Equal(29, colorSpotify.R);
        Assert.Equal(185, colorSpotify.G);
        Assert.Equal(84, colorSpotify.B);

        var colorYtm = ActiveWindowManager.ComputeChromaticColor("chrome", "YouTube Music - Now Playing");
        Assert.Equal(29, colorYtm.R);
        Assert.Equal(185, colorYtm.G);
        Assert.Equal(84, colorYtm.B);
    }

    [Fact]
    public void ComputeChromaticColor_CodeApps_ReturnsCyan()
    {
        var colorCode = ActiveWindowManager.ComputeChromaticColor("code", "PinnedAppsWindow.xaml - WallFlow");
        Assert.Equal(0, colorCode.R);
        Assert.Equal(180, colorCode.G);
        Assert.Equal(255, colorCode.B);

        var colorDevEnv = ActiveWindowManager.ComputeChromaticColor("devenv", "WallFlow - Microsoft Visual Studio");
        Assert.Equal(0, colorDevEnv.R);
        Assert.Equal(180, colorDevEnv.G);
        Assert.Equal(255, colorDevEnv.B);
    }

    [Fact]
    public void ComputeChromaticColor_BrowserApps_ReturnsCoralAmber()
    {
        var colorChrome = ActiveWindowManager.ComputeChromaticColor("chrome", "Google - Search");
        Assert.Equal(255, colorChrome.R);
        Assert.Equal(122, colorChrome.G);
        Assert.Equal(0, colorChrome.B);

        var colorEdge = ActiveWindowManager.ComputeChromaticColor("msedge", "Microsoft Edge");
        Assert.Equal(255, colorEdge.R);
        Assert.Equal(122, colorEdge.G);
        Assert.Equal(0, colorEdge.B);
    }

    [Fact]
    public void ComputeChromaticColor_TerminalApps_ReturnsPurple()
    {
        var colorTerm = ActiveWindowManager.ComputeChromaticColor("windowsterminal", "PowerShell 7");
        Assert.Equal(168, colorTerm.R);
        Assert.Equal(85, colorTerm.G);
        Assert.Equal(247, colorTerm.B);
    }

    [Fact]
    public void ComputeChromaticColor_Explorer_ReturnsBlue()
    {
        var colorExp = ActiveWindowManager.ComputeChromaticColor("explorer", "Documentos");
        Assert.Equal(59, colorExp.R);
        Assert.Equal(130, colorExp.G);
        Assert.Equal(246, colorExp.B);
    }

    [Fact]
    public void ComputeChromaticColor_UnknownApp_ReturnsFrostCyanDefault()
    {
        var colorDefault = ActiveWindowManager.ComputeChromaticColor("randomapp123", "Untitled Document");
        Assert.Equal(56, colorDefault.R);
        Assert.Equal(189, colorDefault.G);
        Assert.Equal(248, colorDefault.B);
    }

    [Fact]
    public void ActiveWindowModel_ThumbnailStatusFlags_ReflectPresence()
    {
        var model = new ActiveWindowModel();
        Assert.Null(model.Thumbnail);
        Assert.False(model.HasThumbnail);
        Assert.True(model.HasNoThumbnail);

        var changedProps = new List<string>();
        model.PropertyChanged += (s, e) => { if (e.PropertyName != null) changedProps.Add(e.PropertyName); };

        // Create a dummy 1x1 frozen bitmap
        var bmp = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr32, null, new byte[4], 4);
        bmp.Freeze();

        model.Thumbnail = bmp;

        Assert.NotNull(model.Thumbnail);
        Assert.True(model.HasThumbnail);
        Assert.False(model.HasNoThumbnail);
        Assert.Contains(nameof(ActiveWindowModel.Thumbnail), changedProps);
        Assert.Contains(nameof(ActiveWindowModel.HasThumbnail), changedProps);
        Assert.Contains(nameof(ActiveWindowModel.HasNoThumbnail), changedProps);
    }
}
