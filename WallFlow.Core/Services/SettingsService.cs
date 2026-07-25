using System;
using System.IO;
using System.Text.Json;

namespace WallFlow.Services;

public class AppSettings
{
    public string WallpaperDirectory { get; set; } = "";
    public System.Collections.Generic.List<string> PinnedAppPaths { get; set; } = new();
    public System.Collections.Generic.Dictionary<string, int> AppLaunchCounts { get; set; } = new();
    public string WeatherCity { get; set; } = "";

    // Custom color settings (null/empty = usar colores del wallpaper)
    public string? UserAccentColor { get; set; }
    public string? UserBorderColor1 { get; set; }
    public string? UserBorderColor2 { get; set; }
    public string? UserBorderColor3 { get; set; }
    public string? UserDockBackgroundColor { get; set; }

    // Behavior & dimensions
    public bool AutoHide { get; set; } = false;
    public double BorderThickness { get; set; } = 3.8;
    public double DockHeight { get; set; } = 56.0;
    public int AutoHideDurationMs { get; set; } = 200;
    public int AutoHideDelayMs { get; set; } = 0;
    public int DockMonitorIndex { get; set; } = 0;
    public bool HideWindowsTaskbar { get; set; } = false;
    public string DockPosition { get; set; } = "Top"; // "Top" or "Bottom"

    // Automatic rotation settings
    public bool AutoChangeEnabled { get; set; } = false;
    public bool AutoModeRandom { get; set; } = false;
    public int SequentialIntervalIndex { get; set; } = 0;
    public int RandomIntervalIndex { get; set; } = 0;

    // Visible elements in dock
    public bool ShowClock { get; set; } = true;
    public bool ShowWeather { get; set; } = true;
    public bool ShowMediaPlayer { get; set; } = true;

    // Weather effects
    public string SelectedWallpaperEffect { get; set; } = "Ninguno";
    public double WeatherEffectSpeed { get; set; } = 1.0;
    public double WeatherEffectSize { get; set; } = 1.0;
    public int WeatherEffectDensity { get; set; } = 100;
    public bool WeatherEffectMouseInteraction { get; set; } = true;
    public string AppFontFamily { get; set; } = "Outfit";

    // Hotkeys configuration
    public string Hotkey_PinnedApps { get; set; } = "";
    public string Hotkey_Weather { get; set; } = "";
    public string Hotkey_Calendar { get; set; } = "";
    public string Hotkey_Media { get; set; } = "";
    public string Hotkey_Power { get; set; } = "";
    public string Hotkey_Settings { get; set; } = "";
    public string Hotkey_LocalGallery { get; set; } = "";
    public string Hotkey_OnlineGallery { get; set; } = "";
    public string Hotkey_NextWallpaper { get; set; } = "";
    public string Hotkey_PrevWallpaper { get; set; } = "";
    public string Hotkey_ToggleDock { get; set; } = "";
    public string Hotkey_Clipboard { get; set; } = "Alt+C";
    public string Hotkey_YouTube { get; set; } = "";
    public string Hotkey_WebBrowser { get; set; } = "";
    public string Hotkey_TogglePinApp { get; set; } = "Ctrl+P";
    public double WidgetClipboardX { get; set; } = -1;
    public double WidgetClipboardY { get; set; } = -1;

    // Effect colors configuration
    public string EffectColor_Rain { get; set; } = "#B4ADD8E6";
    public string EffectColor_Rain2 { get; set; } = "#80007AFF";
    public string EffectColor_Snow { get; set; } = "#FFFFFF";
    public string EffectColor_Snow2 { get; set; } = "#80B0E0FF";
    public string EffectColor_Fog { get; set; } = "#1EC8C8C8";
    public string EffectColor_Fog2 { get; set; } = "#20A0E0FF";
    public string EffectColor_Fireflies { get; set; } = "#FFFFBF00";
    public string EffectColor_Fireflies2 { get; set; } = "#FFDF3E00";
    public string EffectColor_Thunderstorm { get; set; } = "#C896C8E6";
    public string EffectColor_Thunderstorm2 { get; set; } = "#804B0082";
    public string EffectColor_Stars { get; set; } = "#FFFFFF";
    public string EffectColor_Stars2 { get; set; } = "#40FFE0B2";
    public string EffectColor_Aurora { get; set; } = "#6600FF88";
    public string EffectColor_Aurora2 { get; set; } = "#660000FF";
    public string EffectColor_Leaves { get; set; } = "#FFCC6600";
    public string EffectColor_Leaves2 { get; set; } = "#FFA50000";
    public string EffectColor_Sakura { get; set; } = "#FFFFB7C5";
    public string EffectColor_Sakura2 { get; set; } = "#FFFA8072";
    public string EffectColor_Energy { get; set; } = "#FF00BFFF";
    public string EffectColor_Energy2 { get; set; } = "#FFBF00FF";
    public bool Use24HourFormat { get; set; } = false;

    // Sound settings
    public bool SoundEnabled { get; set; } = true;
    public double SoundVolume { get; set; } = 0.5; // 0.0 to 1.0
    public string SoundProfile { get; set; } = "Zen"; // Zen, Cyberpunk, Holographic

    // Visibilidades de los iconos del dock
    public bool ShowIconPinnedApps { get; set; } = true;
    public bool ShowIconLocalGallery { get; set; } = true;
    public bool ShowIconOnlineGallery { get; set; } = true;
    public bool ShowIconYouTubeMusic { get; set; } = true;
    public bool ShowIconChatGPT { get; set; } = true;
    public bool ShowIconGemini { get; set; } = true;
    public bool ShowIconWebBrowser { get; set; } = true;
    public bool ShowIconSettings { get; set; } = true;
    public bool ShowIconVolume { get; set; } = true;
    public bool ShowIconNotifications { get; set; } = true;
    public bool ShowIconShowDesktop { get; set; } = true;
    public bool ShowIconPower { get; set; } = true;

    // Tema y Color de Iconos del Dock
    public string DockIconTheme { get; set; } = "Original"; // Original, Minimalista, Vidrio, Cyberpunk, CircularTile
    public string DockIconColorMode { get; set; } = "Multicolor"; // Multicolor, Auto, White, Custom
    public string? DockIconCustomColor { get; set; } = "#00F5FF";

    // Spotlight customization settings
    public string? SpotlightBackgroundColor { get; set; } = "";
    public string? SpotlightBorderColor { get; set; } = "";
    public double SpotlightBorderThickness { get; set; } = 0.0;
    public string? SpotlightTextColor { get; set; } = "";
    public string? SpotlightPlaceholderColor { get; set; } = "";
    public string? SpotlightIconColor { get; set; } = "";
    public string? SpotlightAccentColor { get; set; } = "";
    public double SpotlightWidth { get; set; } = 600.0;
    public double SpotlightCornerRadius { get; set; } = 27.0;
    public bool SpotlightShowFavorites { get; set; } = true;
    public bool SpotlightUseDockColors { get; set; } = true;
    public string SpotlightThemeMode { get; set; } = "Dock"; // Dock, Oscuro, Claro, Personalizado
    public int SpotlightAppsPerRow { get; set; } = 0; // 0 = Auto
    public string? SpotlightHoverBackgroundColor { get; set; } = "";
    public string? SpotlightHoverBorderColor { get; set; } = "";
    public string? SpotlightItemBackgroundColor { get; set; } = "";
    public string? SpotlightItemForegroundColor { get; set; } = "";
    public string? SpotlightSecondaryTextColor { get; set; } = "";
    public string? SpotlightSeparatorColor { get; set; } = "";
    public string? SpotlightSearchFocusColor { get; set; } = "";

    // Gemini AI settings
    public string? GeminiApiKey { get; set; } = "";
    public string GeminiModel { get; set; } = "gemini-2.5-flash";

    // Browser color customization
    public bool BrowserUseDockColors { get; set; } = true;
    public string? BrowserBackgroundColor { get; set; }
    public string? BrowserAccentColor { get; set; }
    public string? BrowserBorderColor { get; set; }

    // Theme & customization settings for SettingsWindow
    public string AppTheme { get; set; } = "Oscuro"; // Oscuro, Claro, Sistema
    public bool SettingsUseDockAccent { get; set; } = false;
    public bool ShowMacOSTrafficLights { get; set; } = false;
}

public static class SettingsService
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallFlow");

    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

    private static AppSettings? _settings;

    public static void NotifyPreviewColorsRequested() => EventBus.Publish(new PreviewColorsRequestedEvent());

    public static AppSettings Instance
    {
        get
        {
            if (_settings == null)
            {
                Load();
            }
            return _settings!;
        }
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                _settings = JsonSerializer.Deserialize<AppSettings>(json);
            }
        }
        catch
        {
            // Fallback if settings file is corrupted
        }

        _settings ??= new AppSettings();

        // Migration from wallflow.txt
        var legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wallflow.txt");
        if (string.IsNullOrEmpty(_settings.WallpaperDirectory) && File.Exists(legacyPath))
        {
            try
            {
                var dir = File.ReadAllText(legacyPath).Trim();
                if (Directory.Exists(dir))
                {
                    _settings.WallpaperDirectory = dir;
                    Save();
                    File.Delete(legacyPath);
                }
            }
            catch { }
        }

        if (string.IsNullOrEmpty(_settings.WallpaperDirectory) || !Directory.Exists(_settings.WallpaperDirectory))
        {
            _settings.WallpaperDirectory = GetDefaultWallpaperDirectory();
        }

        if (_settings.PinnedAppPaths == null || _settings.PinnedAppPaths.Count == 0)
        {
            _settings.PinnedAppPaths = new System.Collections.Generic.List<string>
            {
                "explorer.exe",
                "chrome.exe",
                "notepad.exe",
                "cmd.exe",
                "taskmgr.exe"
            };
            Save();
        }

        if (_settings.AppLaunchCounts == null)
        {
            _settings.AppLaunchCounts = new System.Collections.Generic.Dictionary<string, int>();
        }

        if (string.IsNullOrEmpty(_settings.SpotlightThemeMode))
        {
            _settings.SpotlightThemeMode = _settings.SpotlightUseDockColors ? "Dock" : "Personalizado";
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            var json = JsonSerializer.Serialize(Instance, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
            EventBus.Publish(new SettingsChangedEvent());
        }
        catch
        {
            // Handle save error
        }
    }

    public static void UpdateWallpaperDirectory(string newPath)
    {
        Instance.WallpaperDirectory = newPath;
        Save();
    }

    private static string GetDefaultWallpaperDirectory()
    {
        var picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var defaultDir = Path.Combine(picturesDir, "Wallpapers");
        try
        {
            if (!Directory.Exists(defaultDir))
            {
                Directory.CreateDirectory(defaultDir);
            }
            return defaultDir;
        }
        catch
        {
            return picturesDir;
        }
    }
}
