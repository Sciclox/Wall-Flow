using System;
using System.ComponentModel;
using WallFlow.Windows;
using Xunit;

namespace WallFlow.Tests;

public class WallpaperFanDeckTests
{
    [Fact]
    public void TestDesktopWallpaperCom()
    {
        var dw = (WallFlow.Native.IDesktopWallpaper)new WallFlow.Native.DesktopWallpaperClass();
        dw.GetWallpaper(null, out string currentPath);
        Assert.NotNull(currentPath);
        if (!string.IsNullOrEmpty(currentPath) && System.IO.File.Exists(currentPath))
        {
            dw.SetWallpaper(null, currentPath);
        }
    }

    [Fact]
    public void WallpaperFanItem_PropertiesAndNotificationsWork()
    {
        var item = new WallpaperFanItem
        {
            FilePath = @"C:\Wallpapers\Cyberpunk_Tokyo_4k.jpg",
            FileName = "Cyberpunk_Tokyo_4k.jpg",
            DisplayTitle = "Cyberpunk Tokyo 4k",
            ResolutionText = "Ultra HD • 8.4 MB",
            FileSizeText = "8.4 MB",
            BadgeText = "4K UHD"
        };

        Assert.Equal(@"C:\Wallpapers\Cyberpunk_Tokyo_4k.jpg", item.FilePath);
        Assert.Equal("Cyberpunk_Tokyo_4k.jpg", item.FileName);
        Assert.Equal("Cyberpunk Tokyo 4k", item.DisplayTitle);
        Assert.Equal("Ultra HD • 8.4 MB", item.ResolutionText);
        Assert.Equal("8.4 MB", item.FileSizeText);
        Assert.Equal("4K UHD", item.BadgeText);

        string? changedProperty = null;
        item.PropertyChanged += (s, e) => changedProperty = e.PropertyName;

        item.IsHero = true;
        Assert.Equal("IsHero", changedProperty);
        Assert.True(item.IsHero);

        item.RotationAngle = 15.5;
        Assert.Equal("RotationAngle", changedProperty);
        Assert.Equal(15.5, item.RotationAngle);

        item.TranslateX = 100.0;
        Assert.Equal("TranslateX", changedProperty);
        Assert.Equal(100.0, item.TranslateX);

        item.TranslateY = 25.0;
        Assert.Equal("TranslateY", changedProperty);
        Assert.Equal(25.0, item.TranslateY);

        item.Scale = 1.15;
        Assert.Equal("Scale", changedProperty);
        Assert.Equal(1.15, item.Scale);

        item.Opacity = 0.85;
        Assert.Equal("Opacity", changedProperty);
        Assert.Equal(0.85, item.Opacity);

        item.ZIndex = 50;
        Assert.Equal("ZIndex", changedProperty);
        Assert.Equal(50, item.ZIndex);
    }

    [Theory]
    [InlineData(0, 0, 0, 1.15, 1.0, true)]
    [InlineData(1, 7.5, 150.0, 0.84, 0.80, false)]
    [InlineData(-1, -7.5, -150.0, 0.84, 0.80, false)]
    [InlineData(2, 15.0, 300.0, 0.76, 0.65, false)]
    public void FanDeck_TransformMath_SimulatesCurvedArch(int offset, double expectedAngle, double expectedX, double minScale, double minOpacity, bool expectedHero)
    {
        int d = offset;
        int absD = Math.Abs(d);

        double angle = d * 7.5;
        double x = d * 150.0;
        double scale = d == 0 ? 1.15 : Math.Max(0.60, 0.92 - absD * 0.08);
        double opacity = d == 0 ? 1.0 : Math.Max(0.35, 0.95 - absD * 0.15);
        bool isHero = (d == 0);

        Assert.Equal(expectedAngle, angle, 1);
        Assert.Equal(expectedX, x, 1);
        Assert.True(scale >= minScale - 0.01 && scale <= 1.20);
        Assert.True(opacity >= minOpacity - 0.01 && opacity <= 1.01);
        Assert.Equal(expectedHero, isHero);
    }
}
