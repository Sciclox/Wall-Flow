using System;
using System.IO;
using System.Text.Json;
using WallFlow.Services;
using Xunit;

namespace WallFlow.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void AppSettings_DefaultValues_AreSensible()
    {
        var settings = new AppSettings();

        Assert.Equal("Top", settings.DockPosition);
        Assert.True(settings.ShowClock);
        Assert.True(settings.ShowWeather);
        Assert.True(settings.ShowMediaPlayer);
        Assert.True(settings.EnableDock);
        Assert.True(settings.EnableSpotlight);
        Assert.False(settings.AutoHide);
        Assert.Equal(56.0, settings.DockHeight);
        Assert.Equal("Outfit", settings.AppFontFamily);
    }

    [Fact]
    public void AppSettings_Serialization_RoundtripPreservesValues()
    {
        var original = new AppSettings
        {
            DockPosition = "Bottom",
            DockHeight = 64.0,
            WeatherCity = "Madrid",
            AutoHide = true,
            EnableDock = false,
            EnableSpotlight = true,
            UserAccentColor = "#FF5500"
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal("Bottom", restored.DockPosition);
        Assert.Equal(64.0, restored.DockHeight);
        Assert.Equal("Madrid", restored.WeatherCity);
        Assert.True(restored.AutoHide);
        Assert.False(restored.EnableDock);
        Assert.True(restored.EnableSpotlight);
        Assert.Equal("#FF5500", restored.UserAccentColor);
    }

    [Fact]
    public void AtomicSave_WritesValidJsonFile()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "WallFlowTest_" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);

        try
        {
            var targetFile = Path.Combine(testDir, "test_settings.json");
            var tempFile = targetFile + ".tmp";

            var settings = new AppSettings { WeatherCity = "Tokyo" };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(tempFile, json);
            File.Move(tempFile, targetFile, overwrite: true);

            Assert.True(File.Exists(targetFile));
            Assert.False(File.Exists(tempFile));

            var loadedJson = File.ReadAllText(targetFile);
            var loadedSettings = JsonSerializer.Deserialize<AppSettings>(loadedJson);

            Assert.NotNull(loadedSettings);
            Assert.Equal("Tokyo", loadedSettings.WeatherCity);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
        }
    }
}
