using System;
using System.Text.RegularExpressions;
using WallFlow.Services;
using Xunit;

namespace WallFlow.Tests;

public class ThemePresetsTests
{
    private static readonly Regex HexColorRegex = new(@"^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);

    [Theory]
    [InlineData("cyberpunk")]
    [InlineData("dracula")]
    [InlineData("tokyo_night")]
    [InlineData("nord_frost")]
    [InlineData("rose_pine")]
    [InlineData("vaporwave")]
    [InlineData("glass_light")]
    [InlineData("emerald")]
    [InlineData("sunset")]
    [InlineData("solarized")]
    public void DockThemes_AllColorsAreValidHex(string themeKey)
    {
        Assert.True(ThemePresets.TryGetDockTheme(themeKey, out var theme));
        Assert.NotNull(theme);
        Assert.Matches(HexColorRegex, theme.Accent);
        Assert.Matches(HexColorRegex, theme.Border1);
        Assert.Matches(HexColorRegex, theme.Border2);
        Assert.Matches(HexColorRegex, theme.Border3);
        Assert.Matches(HexColorRegex, theme.DockBg);
    }

    [Theory]
    [InlineData("neon")]
    [InlineData("sunset")]
    [InlineData("ocean")]
    [InlineData("forest")]
    [InlineData("fire")]
    [InlineData("solarized")]
    [InlineData("aurora")]
    [InlineData("lava")]
    [InlineData("pastel")]
    public void GradientPresets_ContainThreeValidHexColors(string presetKey)
    {
        Assert.True(ThemePresets.TryGetGradientPreset(presetKey, out var colors));
        Assert.NotNull(colors);
        Assert.Equal(3, colors.Length);
        foreach (var c in colors)
        {
            Assert.Matches(HexColorRegex, c);
        }
    }

    [Fact]
    public void ApplyingThemeToSettings_CorrectlyUpdatesAppSettings()
    {
        var settings = new AppSettings();
        Assert.True(ThemePresets.TryGetDockTheme("cyberpunk", out var theme));
        Assert.NotNull(theme);

        settings.UserAccentColor = theme.Accent;
        settings.UserBorderColor1 = theme.Border1;
        settings.UserBorderColor2 = theme.Border2;
        settings.UserBorderColor3 = theme.Border3;
        settings.UserDockBackgroundColor = theme.DockBg;

        Assert.Equal("#FF0055", settings.UserAccentColor);
        Assert.Equal("#FF0055", settings.UserBorderColor1);
        Assert.Equal("#00F0FF", settings.UserBorderColor2);
        Assert.Equal("#FF0055", settings.UserBorderColor3);
        Assert.Equal("#E60B0B0F", settings.UserDockBackgroundColor);
    }
}
