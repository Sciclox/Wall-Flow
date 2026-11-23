using CommunityToolkit.Mvvm.ComponentModel;
using System;
using WallFlow.Services;

namespace WallFlow.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public SettingsViewModel()
    {
        _settings = SettingsService.Instance;
        LoadFromSettings();
    }

    public void LoadFromSettings()
    {
        EnableDock = _settings.EnableDock;
        EnableSpotlight = _settings.EnableSpotlight;
        AutoHide = _settings.AutoHide;
        HideWindowsTaskbar = _settings.HideWindowsTaskbar;
        DockPosition = _settings.DockPosition;
        BorderThickness = _settings.BorderThickness;
        DockHeight = _settings.DockHeight;
        AutoHideDurationMs = _settings.AutoHideDurationMs;
        AutoHideDelayMs = _settings.AutoHideDelayMs;
        DockMonitorIndex = _settings.DockMonitorIndex;
        ShowClock = _settings.ShowClock;
        Use24HourFormat = _settings.Use24HourFormat;
        ShowWeather = _settings.ShowWeather;
        ShowMediaPlayer = _settings.ShowMediaPlayer;

        UserAccentColor = _settings.UserAccentColor;
        UserBorderColor1 = _settings.UserBorderColor1;
        UserBorderColor2 = _settings.UserBorderColor2;
        UserBorderColor3 = _settings.UserBorderColor3;
        UserDockBackgroundColor = _settings.UserDockBackgroundColor;

        DockIconTheme = _settings.DockIconTheme;
        DockIconColorMode = _settings.DockIconColorMode;
        DockIconCustomColor = _settings.DockIconCustomColor;
    }

    [ObservableProperty]
    private bool _enableDock;

    [ObservableProperty]
    private bool _enableSpotlight;

    [ObservableProperty]
    private bool _autoHide;

    [ObservableProperty]
    private bool _hideWindowsTaskbar;

    [ObservableProperty]
    private string _dockPosition = "Top";

    [ObservableProperty]
    private double _borderThickness;

    [ObservableProperty]
    private double _dockHeight;

    [ObservableProperty]
    private int _autoHideDurationMs;

    [ObservableProperty]
    private int _autoHideDelayMs;

    [ObservableProperty]
    private int _dockMonitorIndex;

    [ObservableProperty]
    private bool _showClock;

    [ObservableProperty]
    private bool _use24HourFormat;

    [ObservableProperty]
    private bool _showWeather;

    [ObservableProperty]
    private bool _showMediaPlayer;

    [ObservableProperty]
    private string? _userAccentColor;

    [ObservableProperty]
    private string? _userBorderColor1;

    [ObservableProperty]
    private string? _userBorderColor2;

    [ObservableProperty]
    private string? _userBorderColor3;

    [ObservableProperty]
    private string? _userDockBackgroundColor;

    [ObservableProperty]
    private string _dockIconTheme = "Original";

    [ObservableProperty]
    private string _dockIconColorMode = "Multicolor";

    [ObservableProperty]
    private string? _dockIconCustomColor = "#00F5FF";

    public void SaveToSettings()
    {
        _settings.EnableDock = EnableDock;
        _settings.EnableSpotlight = EnableSpotlight;
        _settings.AutoHide = AutoHide;
        _settings.HideWindowsTaskbar = HideWindowsTaskbar;
        _settings.DockPosition = DockPosition;
        _settings.BorderThickness = BorderThickness;
        _settings.DockHeight = DockHeight;
        _settings.AutoHideDurationMs = AutoHideDurationMs;
        _settings.AutoHideDelayMs = AutoHideDelayMs;
        _settings.DockMonitorIndex = DockMonitorIndex;
        _settings.ShowClock = ShowClock;
        _settings.Use24HourFormat = Use24HourFormat;
        _settings.ShowWeather = ShowWeather;
        _settings.ShowMediaPlayer = ShowMediaPlayer;

        _settings.UserAccentColor = UserAccentColor;
        _settings.UserBorderColor1 = UserBorderColor1;
        _settings.UserBorderColor2 = UserBorderColor2;
        _settings.UserBorderColor3 = UserBorderColor3;
        _settings.UserDockBackgroundColor = UserDockBackgroundColor;

        _settings.DockIconTheme = DockIconTheme;
        _settings.DockIconColorMode = DockIconColorMode;
        _settings.DockIconCustomColor = DockIconCustomColor;

        SettingsService.Save();

        // Publish dynamic color theme updating if colors are null (Auto-Theming)
        if (string.IsNullOrEmpty(UserAccentColor))
        {
            WallpaperThemeService.Instance.UpdateThemeFromWallpaper(true);
        }

        EventBus.Publish(new SettingsChangedEvent());
    }
}
