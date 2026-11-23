using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WallFlow.Services;
using WallFlow.ViewModels;

namespace WallFlow.Windows;

public partial class SettingsWindow : Window, IWidget
{
    string IWidget.Name => "Settings";
    public void Open() { Show(); Activate(); }
    public void CloseWidget() { Close(); }
    public bool IsOpen => IsLoaded && IsVisible;
    private bool _loading;
    private bool _isCapturingHotkey = false;
    private TextBox? _activeCaptureTextBox;
    private string _previousHotkeyText = "";

    public static SettingsWindow? Instance { get; private set; }

    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        Instance = this;
        InitializeComponent();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            BackdropHelper.ApplyAcrylic(this);
            RootBorder.SizeChanged += UpdateBorderClip;
            UpdateBorderClip(null, null);

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            BeginAnimation(OpacityProperty, fadeIn);

            try { LoadSettings(); }
            catch (Exception ex) { LogCrash(ex); }



            try { UpdateTabSelection(0); }
            catch (Exception ex) { LogCrash(ex); }

            try
            {
                if (Resources["RotateGradientPreview"] is Storyboard rotateStoryboard)
                {
                    rotateStoryboard.Begin(this);
                }
            }
            catch (Exception ex) { LogCrash(ex); }
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            Opacity = 1;
        }
    }

    private void LogCrash(Exception ex)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallFlow");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "crash.txt"), ex.ToString());
        }
        catch { }
    }

    private void UpdateBorderClip(object? sender, RoutedEventArgs? e)
    {
        if (RootBorder == null) return;
        var w = RootBorder.ActualWidth > 0 ? RootBorder.ActualWidth : Width;
        var h = RootBorder.ActualHeight > 0 ? RootBorder.ActualHeight : Height;
        if (w <= 0 || h <= 0) return;
        RootBorder.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 8, 8);
        var actualW = ActualWidth > 0 ? ActualWidth : Width;
        var actualH = ActualHeight > 0 ? ActualHeight : Height;
        Clip = new RectangleGeometry(new Rect(0, 0, actualW, actualH), 8, 8);
        BackdropHelper.UpdateWindowRegion(this, 8);
    }

    private void Window_Deactivated(object sender, EventArgs e) { }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isCapturingHotkey && _activeCaptureTextBox != null)
        {
            e.Handled = true;
            
            // Get key and modifiers
            Key key = (e.Key == Key.System) ? e.SystemKey : e.Key;
            
            // Ignore isolated modifier presses
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                // Abort capture on Escape without modifiers
                _activeCaptureTextBox.Text = _previousHotkeyText;
                _activeCaptureTextBox.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
                _isCapturingHotkey = false;
                _activeCaptureTextBox = null;
                return;
            }

            // Build combination string
            var combo = new System.Collections.Generic.List<string>();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) combo.Add("Ctrl");
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) combo.Add("Shift");
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) combo.Add("Alt");
            if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) combo.Add("Win");
            
            combo.Add(key.ToString());
            
            _activeCaptureTextBox.Text = string.Join("+", combo);
            _activeCaptureTextBox.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            _isCapturingHotkey = false;
            _activeCaptureTextBox = null;
            return;
        }

        if (e.Key == Key.Escape) Close();
    }

    private void CloseBtn_Click(object sender, MouseButtonEventArgs e)
    {
        Close();
    }

    private void CloseBtn_MouseEnter(object sender, MouseEventArgs e)
    {
        CloseBtn.Background = new SolidColorBrush(Color.FromRgb(0xC7, 0x4B, 0x4B));
    }

    private void CloseBtn_MouseLeave(object sender, MouseEventArgs e)
    {
        CloseBtn.Background = Brushes.Transparent;
    }

    // ─── Settings logic ──────────────────────────────────────

    private void LoadSettings()
    {
        _loading = true;
        var s = SettingsService.Instance;

        if (EnableDockCheck != null) EnableDockCheck.IsChecked = s.EnableDock;
        if (EnableDockInTabCheck != null) EnableDockInTabCheck.IsChecked = s.EnableDock;
        if (EnableSpotlightCheck != null) EnableSpotlightCheck.IsChecked = s.EnableSpotlight;
        if (EnableSpotlightInTabCheck != null) EnableSpotlightInTabCheck.IsChecked = s.EnableSpotlight;
        if (HideWindowsTaskbarGlobalCheck != null) HideWindowsTaskbarGlobalCheck.IsChecked = s.HideWindowsTaskbar;
        if (EnableWallpaperEffectsGlobalCheck != null) EnableWallpaperEffectsGlobalCheck.IsChecked = s.SelectedWallpaperEffect != "Ninguno";
        if (EnableAutoChangeGlobalCheck != null) EnableAutoChangeGlobalCheck.IsChecked = LocalGalleryWindow.IsAutoChangeEnabled();
        if (EnableSoundsGlobalCheck != null) EnableSoundsGlobalCheck.IsChecked = s.SoundEnabled;
        if (EnableClipboardGlobalCheck != null) EnableClipboardGlobalCheck.IsChecked = !string.IsNullOrEmpty(s.Hotkey_Clipboard);

        UpdateDockEnabledState(s.EnableDock);
        UpdateSpotlightEnabledState(s.EnableSpotlight);

        UseWallpaperHoverCheck.IsChecked = string.IsNullOrEmpty(s.UserAccentColor);
        AccentHexBox.Text = s.UserAccentColor ?? "#AF52DE";
        UpdateAccentPreview();

        UseDefaultDockBgCheck.IsChecked = string.IsNullOrEmpty(s.UserDockBackgroundColor);
        DockBgHexBox.Text = s.UserDockBackgroundColor ?? "#B40B0B0B";
        UpdateDockBgPreview();

        UseWallpaperBorderCheck.IsChecked = string.IsNullOrEmpty(s.UserBorderColor1);
        BorderHex1.Text = s.UserBorderColor1 ?? "#007AFF";
        BorderHex2.Text = s.UserBorderColor2 ?? "#5856D6";
        BorderHex3.Text = s.UserBorderColor3 ?? "#AF52DE";
        UpdateBorderPreviews();

        AutoHideCheck.IsChecked = s.AutoHide;
        HideTaskbarCheck.IsChecked = s.HideWindowsTaskbar;
        DockPositionCombo.SelectedIndex = s.DockPosition == "Bottom" ? 1 : 0;
        ThicknessSlider.Value = s.BorderThickness;
        ThicknessValueText.Text = $"{s.BorderThickness:F1} px";
        DockHeightSlider.Value = s.DockHeight;
        DockHeightValueText.Text = $"{s.DockHeight:F0} px";

        AutoHideDurationSlider.Value = s.AutoHideDurationMs;
        AutoHideDurationValueText.Text = $"{s.AutoHideDurationMs} ms";
        AutoHideDelaySlider.Value = s.AutoHideDelayMs;
        AutoHideDelayValueText.Text = $"{s.AutoHideDelayMs} ms";

        // Populate monitor selection
        MonitorCombo.Items.Clear();
        var screens = System.Windows.Forms.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            MonitorCombo.Items.Add($"Pantalla {i + 1} ({(screens[i].Primary ? "Principal" : "Secundaria")}) - {screens[i].Bounds.Width}x{screens[i].Bounds.Height}");
        }
        MonitorCombo.SelectedIndex = Math.Clamp(s.DockMonitorIndex, 0, screens.Length - 1);

        StartWithWindowsCheck.IsChecked = ContextMenuWindow.IsAutoStartEnabled();

        // Populate and select font family
        PopulateFonts();

        // Load selected wallpaper effect
        string effect = s.SelectedWallpaperEffect ?? "Ninguno";
        int selectedIndex = 0;
        for (int i = 0; i < WeatherEffectCombo.Items.Count; i++)
        {
            if (WeatherEffectCombo.Items[i] is ComboBoxItem item && item.Content.ToString() == effect)
            {
                selectedIndex = i;
                break;
            }
        }
        WeatherEffectCombo.SelectedIndex = selectedIndex;

        EffectSpeedSlider.Value = s.WeatherEffectSpeed;
        EffectSpeedValueText.Text = $"{s.WeatherEffectSpeed:F1}x";
        EffectSizeSlider.Value = s.WeatherEffectSize;
        EffectSizeValueText.Text = $"{s.WeatherEffectSize:F1}x";
        EffectDensitySlider.Value = s.WeatherEffectDensity;
        EffectDensityValueText.Text = $"{s.WeatherEffectDensity:F0}%";
        MouseInteractionCheck.IsChecked = s.WeatherEffectMouseInteraction;

        // Auto Change Settings
        AutoChangeCheck.IsChecked = LocalGalleryWindow.IsAutoChangeEnabled();
        AutoModeCombo.SelectedIndex = LocalGalleryWindow.IsRandomMode() ? 1 : 0;
        AutoIntervalCombo.SelectedIndex = LocalGalleryWindow.GetCurrentSpeedIndex();
        UpdateAutoChangePanelState();

        ShowClockCheck.IsChecked = s.ShowClock;
        Use24HourCheck.IsChecked = s.Use24HourFormat;
        ShowWeatherCheck.IsChecked = s.ShowWeather;
        ShowMediaCheck.IsChecked = s.ShowMediaPlayer;


        WeatherCityBox.Text = s.WeatherCity ?? "";

        // Load hotkeys settings
        Hotkey_PinnedApps_Spotlight_Text.Text = s.Hotkey_PinnedApps;
        Hotkey_Power_Text.Text = s.Hotkey_Power;
        Hotkey_Settings_Text.Text = s.Hotkey_Settings;
        Hotkey_LocalGallery_Text.Text = s.Hotkey_LocalGallery;
        Hotkey_OnlineGallery_Text.Text = s.Hotkey_OnlineGallery;
        Hotkey_NextWallpaper_Text.Text = s.Hotkey_NextWallpaper;
        Hotkey_PrevWallpaper_Text.Text = s.Hotkey_PrevWallpaper;
        Hotkey_ToggleDock_Text.Text = s.Hotkey_ToggleDock;
        Hotkey_Clipboard_Text.Text = s.Hotkey_Clipboard;
        Hotkey_YouTube_Text.Text = s.Hotkey_YouTube;
        Hotkey_WebBrowser_Text.Text = s.Hotkey_WebBrowser;
        Hotkey_RunAsAdmin_Spotlight_Text.Text = s.Hotkey_RunAsAdmin;
        if (Hotkey_PinnedApps_Spotlight_Text != null) Hotkey_PinnedApps_Spotlight_Text.Text = s.Hotkey_PinnedApps;
        if (Hotkey_RunAsAdmin_Spotlight_Text != null) Hotkey_RunAsAdmin_Spotlight_Text.Text = s.Hotkey_RunAsAdmin;

        // Load effect color settings
        EffectColor_Rain_Hex.Text = s.EffectColor_Rain;
        EffectColor_Rain2_Hex.Text = s.EffectColor_Rain2;
        EffectColor_Snow_Hex.Text = s.EffectColor_Snow;
        EffectColor_Snow2_Hex.Text = s.EffectColor_Snow2;
        EffectColor_Fog_Hex.Text = s.EffectColor_Fog;
        EffectColor_Fog2_Hex.Text = s.EffectColor_Fog2;
        EffectColor_Fireflies_Hex.Text = s.EffectColor_Fireflies;
        EffectColor_Fireflies2_Hex.Text = s.EffectColor_Fireflies2;
        EffectColor_Thunderstorm_Hex.Text = s.EffectColor_Thunderstorm;
        EffectColor_Thunderstorm2_Hex.Text = s.EffectColor_Thunderstorm2;
        EffectColor_Stars_Hex.Text = s.EffectColor_Stars;
        EffectColor_Stars2_Hex.Text = s.EffectColor_Stars2;
        EffectColor_Aurora_Hex.Text = s.EffectColor_Aurora;
        EffectColor_Aurora2_Hex.Text = s.EffectColor_Aurora2;
        EffectColor_Sakura_Hex.Text = s.EffectColor_Sakura;
        EffectColor_Sakura2_Hex.Text = s.EffectColor_Sakura2;
        EffectColor_Leaves_Hex.Text = s.EffectColor_Leaves;
        EffectColor_Leaves2_Hex.Text = s.EffectColor_Leaves2;
        EffectColor_Energy_Hex.Text = s.EffectColor_Energy;
        EffectColor_Energy2_Hex.Text = s.EffectColor_Energy2;

        // Load sound settings
        SoundEnabledCheck.IsChecked = s.SoundEnabled;
        SoundVolumeSlider.Value = s.SoundVolume * 100;
        SoundVolumeText.Text = $"{(int)(s.SoundVolume * 100)}%";

        string profile = s.SoundProfile ?? "Zen";
        int profileIndex = 0;
        for (int i = 0; i < SoundProfileCombo.Items.Count; i++)
        {
            if (SoundProfileCombo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == profile)
            {
                profileIndex = i;
                break;
            }
        }
        SoundProfileCombo.SelectedIndex = profileIndex;
        UpdateSoundPanelState();

        UpdateEffectColorPreviews();
        UpdateEffectColorsPanelVisibility();

        // Load custom dock icons and commands settings
        ShowIconPinnedAppsCheck.IsChecked = s.ShowIconPinnedApps;
        ShowIconLocalGalleryCheck.IsChecked = s.ShowIconLocalGallery;
        ShowIconOnlineGalleryCheck.IsChecked = s.ShowIconOnlineGallery;
        ShowIconYouTubeMusicCheck.IsChecked = s.ShowIconYouTubeMusic;
        ShowIconSettingsCheck.IsChecked = s.ShowIconSettings;
        ShowIconVolumeCheck.IsChecked = s.ShowIconVolume;
        ShowIconNotificationsCheck.IsChecked = s.ShowIconNotifications;
        ShowIconShowDesktopCheck.IsChecked = s.ShowIconShowDesktop;
        ShowIconPowerCheck.IsChecked = s.ShowIconPower;

        // Load Spotlight settings
        SpotlightShowFavoritesCheck.IsChecked = s.SpotlightShowFavorites;
        SpotlightWidthSlider.Value = s.SpotlightWidth;
        SpotlightCornerRadiusSlider.Value = s.SpotlightCornerRadius;
        SpotlightBorderThicknessSlider.Value = s.SpotlightBorderThickness;

        SpotlightBgHexBox.Text = s.SpotlightBackgroundColor ?? "";
        SpotlightBorderHexBox.Text = s.SpotlightBorderColor ?? "";
        SpotlightTextHexBox.Text = s.SpotlightTextColor ?? "";
        SpotlightPlaceholderHexBox.Text = s.SpotlightPlaceholderColor ?? "";
        SpotlightIconHexBox.Text = s.SpotlightIconColor ?? "";
        SpotlightAccentHexBox.Text = s.SpotlightAccentColor ?? "";

        SpotlightHoverBgHexBox.Text = s.SpotlightHoverBackgroundColor ?? "";
        SpotlightHoverBorderHexBox.Text = s.SpotlightHoverBorderColor ?? "";
        SpotlightItemBgHexBox.Text = s.SpotlightItemBackgroundColor ?? "";
        SpotlightItemFgHexBox.Text = s.SpotlightItemForegroundColor ?? "";
        SpotlightSecondaryTextHexBox.Text = s.SpotlightSecondaryTextColor ?? "";
        SpotlightSeparatorHexBox.Text = s.SpotlightSeparatorColor ?? "";
        SpotlightFocusHexBox.Text = s.SpotlightSearchFocusColor ?? "";

        string spotlightMode = s.SpotlightThemeMode;
        if (string.IsNullOrEmpty(spotlightMode))
        {
            spotlightMode = s.SpotlightUseDockColors ? "Dock" : "Personalizado";
        }
        if (SpotlightThemeModeCombo != null)
        {
            bool found = false;
            foreach (ComboBoxItem item in SpotlightThemeModeCombo.Items)
            {
                if (item.Tag != null && item.Tag.ToString() == spotlightMode)
                {
                    SpotlightThemeModeCombo.SelectedItem = item;
                    found = true;
                    break;
                }
            }
            if (!found) SpotlightThemeModeCombo.SelectedIndex = 0;
        }

        if (SpotlightAppsPerRowCombo != null)
        {
            int val = s.SpotlightAppsPerRow;
            bool found = false;
            foreach (ComboBoxItem item in SpotlightAppsPerRowCombo.Items)
            {
                if (item.Tag != null && int.TryParse(item.Tag.ToString(), out int tagVal) && tagVal == val)
                {
                    SpotlightAppsPerRowCombo.SelectedItem = item;
                    found = true;
                    break;
                }
            }
            if (!found) SpotlightAppsPerRowCombo.SelectedIndex = 0;
        }

        if (GeminiApiKeyBox != null)
        {
            GeminiApiKeyBox.Password = s.GeminiApiKey ?? "";
        }

        UpdateSpotlightColorPreviews();

        UpdateEnabledStates();
        _loading = false;
    }

    private void GeminiApiKey_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SettingsService.Instance.GeminiApiKey = GeminiApiKeyBox.Password;
    }

    private async void VerifyGeminiApiKey_Click(object sender, RoutedEventArgs e)
    {
        string key = GeminiApiKeyBox?.Password?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(key))
        {
            if (GeminiStatusText != null)
            {
                GeminiStatusText.Text = "❌ Por favor ingresa una clave de API antes de verificar.";
                GeminiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                GeminiStatusText.Visibility = Visibility.Visible;
            }
            return;
        }

        if (GeminiStatusText != null)
        {
            GeminiStatusText.Text = "⏳ Conectando con Google AI Studio...";
            GeminiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            GeminiStatusText.Visibility = Visibility.Visible;
        }

        if (VerifyGeminiApiKeyBtn != null) VerifyGeminiApiKeyBtn.IsEnabled = false;

        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            try { client.DefaultRequestHeaders.Add("x-goog-api-key", key); } catch { }

            // 1. Discover active models via GET /v1beta/models
            string listModelsUrl = $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(key)}";
            var listResponse = await client.GetAsync(listModelsUrl);
            string listJson = await listResponse.Content.ReadAsStringAsync();

            var discoveredModels = new System.Collections.Generic.List<string>();

            if (listResponse.IsSuccessStatusCode)
            {
                try
                {
                    using var docList = System.Text.Json.JsonDocument.Parse(listJson);
                    if (docList.RootElement.TryGetProperty("models", out var modelsArr) &&
                        modelsArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var m in modelsArr.EnumerateArray())
                        {
                            if (m.TryGetProperty("name", out var nameProp))
                            {
                                string fullName = nameProp.GetString() ?? "";
                                string shortName = fullName.StartsWith("models/") ? fullName.Substring(7) : fullName;

                                if (m.TryGetProperty("supportedGenerationMethods", out var genMethods) &&
                                    genMethods.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    bool supportsGenerate = false;
                                    foreach (var method in genMethods.EnumerateArray())
                                    {
                                        if (method.GetString() == "generateContent") { supportsGenerate = true; break; }
                                    }
                                    if (supportsGenerate) discoveredModels.Add(shortName);
                                }
                                else
                                {
                                    discoveredModels.Add(shortName);
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // Separate Gemini and Gemma models, prioritizing Gemini Flash models
            var geminiModels = new System.Collections.Generic.List<string>();
            var gemmaModels = new System.Collections.Generic.List<string>();

            foreach (var m in discoveredModels)
            {
                if (m.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
                    geminiModels.Add(m);
                else if (m.StartsWith("gemma", StringComparison.OrdinalIgnoreCase))
                    gemmaModels.Add(m);
            }

            var preferred = new System.Collections.Generic.List<string> 
            { 
                "gemini-3.6-flash", 
                "gemini-3-flash-preview", 
                "gemini-3.5-flash-lite", 
                "gemini-flash-lite-latest", 
                "gemini-3.7-flash",
                "gemini-3.8-flash"
            };

            var reordered = new System.Collections.Generic.List<string>();
            foreach (var pref in preferred)
            {
                foreach (var m in geminiModels)
                {
                    if (string.Equals(m, pref, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!reordered.Contains(m)) reordered.Add(m);
                        break;
                    }
                }
            }
            foreach (var m in geminiModels)
            {
                if (!reordered.Contains(m)) reordered.Add(m);
            }
            foreach (var m in gemmaModels)
            {
                if (!reordered.Contains(m)) reordered.Add(m);
            }

            discoveredModels = reordered.Count > 0 ? reordered : new System.Collections.Generic.List<string> { "gemini-2.5-flash", "gemini-2.0-flash", "gemini-1.5-flash" };

            bool success = false;
            System.Net.HttpStatusCode lastStatusCode = listResponse.StatusCode;
            string lastErrorMessage = "";

            foreach (var model in discoveredModels)
            {
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(key)}";
                var requestBody = new
                {
                    contents = new[] { new { parts = new[] { new { text = "Ping" } } } }
                };

                string jsonContent = System.Text.Json.JsonSerializer.Serialize(requestBody);
                using var content = new System.Net.Http.StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    success = true;
                    SettingsService.Instance.GeminiModel = model;
                    SettingsService.Save();
                    break;
                }
                else
                {
                    lastStatusCode = response.StatusCode;
                    string respErr = await response.Content.ReadAsStringAsync();
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(respErr);
                        if (doc.RootElement.TryGetProperty("error", out var errObj) &&
                            errObj.TryGetProperty("message", out var msgProp))
                        {
                            lastErrorMessage = msgProp.GetString() ?? "";
                        }
                    }
                    catch { }
                }
            }

            if (success)
            {
                if (GeminiStatusText != null)
                {
                    GeminiStatusText.Text = $"✅ ¡API Key válida y lista! Modelo activo detectado: {SettingsService.Instance.GeminiModel}";
                    GeminiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(76, 217, 100));
                }
            }
            else
            {
                if (GeminiStatusText != null)
                {
                    if (lastStatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        GeminiStatusText.Text = "❌ Error 404 (Modelo o Endpoint No Encontrado): Revisa que la API de Gemini esté activa en tu proyecto de Google Cloud / AI Studio.";
                        GeminiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                    }
                    else if (lastStatusCode == (System.Net.HttpStatusCode)429)
                    {
                        GeminiStatusText.Text = "⏳ ¡La clave es válida! Pero alcanzaste la cuota temporal de consultas por minuto. Espera 10 segundos e inténtalo de nuevo.";
                        GeminiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 0));
                    }
                    else
                    {
                        string detail = string.IsNullOrWhiteSpace(lastErrorMessage) ? $"Status {lastStatusCode}" : lastErrorMessage;
                        GeminiStatusText.Text = $"❌ Clave rechazada por Google ({lastStatusCode}): {detail}";
                        GeminiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (GeminiStatusText != null)
            {
                GeminiStatusText.Text = "❌ Error de conexión: " + ex.Message;
                GeminiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
            }
        }
        finally
        {
            if (VerifyGeminiApiKeyBtn != null) VerifyGeminiApiKeyBtn.IsEnabled = true;
        }
    }

    private void UpdateEnabledStates()
    {
        var hoverAuto = UseWallpaperHoverCheck.IsChecked == true;
        AccentHexBox.IsEnabled = !hoverAuto;
        AccentHexBox.Opacity = hoverAuto ? 0.35 : 1.0;
        AccentPreview.IsEnabled = !hoverAuto;

        var dockBgAuto = UseDefaultDockBgCheck.IsChecked == true;
        DockBgHexBox.IsEnabled = !dockBgAuto;
        DockBgHexBox.Opacity = dockBgAuto ? 0.35 : 1.0;
        DockBgPreview.IsEnabled = !dockBgAuto;

        var borderAuto = UseWallpaperBorderCheck.IsChecked == true;
        BorderHex1.IsEnabled = !borderAuto;
        BorderHex2.IsEnabled = !borderAuto;
        BorderHex3.IsEnabled = !borderAuto;
        BorderPreview1.IsEnabled = !borderAuto;
        BorderPreview2.IsEnabled = !borderAuto;
        BorderPreview3.IsEnabled = !borderAuto;
        var opacity = borderAuto ? 0.35 : 1.0;
        BorderHex1.Opacity = opacity;
        BorderHex2.Opacity = opacity;
        BorderHex3.Opacity = opacity;

        bool spotlightBgAuto = true;
        if (SpotlightThemeModeCombo != null && SpotlightThemeModeCombo.SelectedItem is ComboBoxItem selectedTheme && selectedTheme.Tag != null)
        {
            spotlightBgAuto = selectedTheme.Tag.ToString() != "Personalizado";
        }
        if (SpotlightColorsGrid != null)
        {
            SpotlightColorsGrid.IsEnabled = !spotlightBgAuto;
            SpotlightColorsGrid.Opacity = spotlightBgAuto ? 0.35 : 1.0;
        }


    }

    private void UpdateAutoChangePanelState()
    {
        if (AutoChangeOptionsPanel != null)
        {
            AutoChangeOptionsPanel.IsEnabled = AutoChangeCheck.IsChecked == true;
            AutoChangeOptionsPanel.Opacity = AutoChangeCheck.IsChecked == true ? 1.0 : 0.35;
        }
    }

    private void UpdateDockEnabledState(bool enabled)
    {
        if (DockSettingsContainer != null)
        {
            DockSettingsContainer.IsEnabled = enabled;
            DockSettingsContainer.Opacity = enabled ? 1.0 : 0.35;
        }
        if (DockDisabledWarningBorder != null)
        {
            DockDisabledWarningBorder.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void UpdateSpotlightEnabledState(bool enabled)
    {
        if (SpotlightSettingsContainer != null)
        {
            SpotlightSettingsContainer.IsEnabled = enabled;
            SpotlightSettingsContainer.Opacity = enabled ? 1.0 : 0.35;
        }
        if (SpotlightDisabledWarningBorder != null)
        {
            SpotlightDisabledWarningBorder.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void EnableDockCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool isChecked = EnableDockCheck.IsChecked == true;
        if (EnableDockInTabCheck != null && EnableDockInTabCheck.IsChecked != isChecked)
        {
            EnableDockInTabCheck.IsChecked = isChecked;
        }
        UpdateDockEnabledState(isChecked);
    }

    private void EnableDockInTabCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool isChecked = EnableDockInTabCheck.IsChecked == true;
        if (EnableDockCheck != null && EnableDockCheck.IsChecked != isChecked)
        {
            EnableDockCheck.IsChecked = isChecked;
        }
        UpdateDockEnabledState(isChecked);
    }

    private void EnableSpotlightCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool isChecked = EnableSpotlightCheck.IsChecked == true;
        if (EnableSpotlightInTabCheck != null && EnableSpotlightInTabCheck.IsChecked != isChecked)
        {
            EnableSpotlightInTabCheck.IsChecked = isChecked;
        }
        UpdateSpotlightEnabledState(isChecked);
    }

    private void EnableSpotlightInTabCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool isChecked = EnableSpotlightInTabCheck.IsChecked == true;
        if (EnableSpotlightCheck != null && EnableSpotlightCheck.IsChecked != isChecked)
        {
            EnableSpotlightCheck.IsChecked = isChecked;
        }
        UpdateSpotlightEnabledState(isChecked);
    }

    private void HideWindowsTaskbarGlobalCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool isChecked = HideWindowsTaskbarGlobalCheck.IsChecked == true;
        if (HideTaskbarCheck != null && HideTaskbarCheck.IsChecked != isChecked)
        {
            HideTaskbarCheck.IsChecked = isChecked;
        }
    }

    private void HideTaskbarCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool isChecked = HideTaskbarCheck.IsChecked == true;
        if (HideWindowsTaskbarGlobalCheck != null && HideWindowsTaskbarGlobalCheck.IsChecked != isChecked)
        {
            HideWindowsTaskbarGlobalCheck.IsChecked = isChecked;
        }
    }

    private void EnableWallpaperEffectsGlobalCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool isChecked = EnableWallpaperEffectsGlobalCheck.IsChecked == true;
        if (WeatherEffectCombo != null)
        {
            if (isChecked)
            {
                if (WeatherEffectCombo.SelectedIndex <= 0 && WeatherEffectCombo.Items.Count > 1)
                {
                    WeatherEffectCombo.SelectedIndex = 1; // Primer efecto disponible
                }
            }
            else
            {
                WeatherEffectCombo.SelectedIndex = 0; // Ninguno
            }
        }
    }

    private void EnableAutoChangeGlobalCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool isChecked = EnableAutoChangeGlobalCheck.IsChecked == true;
        if (AutoChangeCheck != null && AutoChangeCheck.IsChecked != isChecked)
        {
            AutoChangeCheck.IsChecked = isChecked;
        }
    }

    private void EnableSoundsGlobalCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool isChecked = EnableSoundsGlobalCheck.IsChecked == true;
        if (SoundEnabledCheck != null && SoundEnabledCheck.IsChecked != isChecked)
        {
            SoundEnabledCheck.IsChecked = isChecked;
        }
    }

    private void EnableClipboardGlobalCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool isChecked = EnableClipboardGlobalCheck.IsChecked == true;
        if (Hotkey_Clipboard_Text != null)
        {
            if (isChecked && string.IsNullOrWhiteSpace(Hotkey_Clipboard_Text.Text))
            {
                Hotkey_Clipboard_Text.Text = "Alt+C";
            }
            else if (!isChecked)
            {
                Hotkey_Clipboard_Text.Text = "";
            }
        }
    }

    private void UseWallpaperHover_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
        if (UseWallpaperHoverCheck.IsChecked == true)
            AccentHexBox.Text = "#AF52DE";
    }

    private void UseWallpaperBorder_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
        if (UseWallpaperBorderCheck.IsChecked == true)
        {
            BorderHex1.Text = "#007AFF";
            BorderHex2.Text = "#5856D6";
            BorderHex3.Text = "#AF52DE";
        }
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        hex = hex.Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted != null)
            {
                color = (Color)converted;
                return true;
            }
        }
        catch { }
        return false;
    }

    private void ValidateColorInput(TextBox textBox, Border previewBorder)
    {
        if (textBox == null || previewBorder == null) return;

        if (TryParseColor(textBox.Text, out var color))
        {
            previewBorder.Background = new SolidColorBrush(color);
            textBox.ClearValue(BorderBrushProperty);
        }
        else
        {
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red #EF4444
        }
    }

    private void UpdateAccentPreview()
    {
        try
        {
            ValidateColorInput(AccentHexBox, AccentPreview);
            if (SettingsTabControl != null)
            {
                UpdateTabSelection(SettingsTabControl.SelectedIndex);
            }
        }
        catch { }
    }

    private void UpdateBorderPreviews()
    {
        try
        {
            ValidateColorInput(BorderHex1, BorderPreview1);
            ValidateColorInput(BorderHex2, BorderPreview2);
            ValidateColorInput(BorderHex3, BorderPreview3);

            if (TryParseColor(BorderHex1.Text, out var c1) &&
                TryParseColor(BorderHex2.Text, out var c2) &&
                TryParseColor(BorderHex3.Text, out var c3))
            {
                if (BorderGradientPreview.Background is LinearGradientBrush lgb && lgb.GradientStops.Count >= 3)
                {
                    if (lgb.CanFreeze && lgb.IsFrozen)
                    {
                        var clone = lgb.Clone();
                        clone.GradientStops[0].Color = c1;
                        clone.GradientStops[1].Color = c2;
                        clone.GradientStops[2].Color = c3;
                        BorderGradientPreview.Background = clone;
                    }
                    else
                    {
                        lgb.GradientStops[0].Color = c1;
                        lgb.GradientStops[1].Color = c2;
                        lgb.GradientStops[2].Color = c3;
                    }
                }
            }
        }
        catch { }
    }

    private void AccentColor_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || AccentPreview == null) return;
        UpdateAccentPreview();
    }

    private void BorderColor_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || BorderPreview1 == null) return;
        UpdateBorderPreviews();
    }

    // ─── Swatches Palette & Presets Handlers ───

    private static readonly Dictionary<string, string[]> GradientPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["neon"] = new[] { "#00F5FF", "#BF00FF", "#FF006E" },
        ["sunset"] = new[] { "#FF6B35", "#FF2D87", "#D946EF" },
        ["ocean"] = new[] { "#007AFF", "#5856D6", "#AF52DE" },
        ["forest"] = new[] { "#059669", "#10B981", "#34D399" },
        ["fire"] = new[] { "#FF4500", "#FF0080", "#7B00FF" },
        ["solarized"] = new[] { "#859900", "#268BD2", "#2AA198" },
        ["aurora"] = new[] { "#06D6A0", "#118AB2", "#7B2FF7" },
        ["lava"] = new[] { "#FF4500", "#FF0080", "#7B00FF" },
        ["pastel"] = new[] { "#A78BFA", "#F472B6", "#FB923C" }
    };

    private void PaletteColor_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Background is SolidColorBrush brush)
        {
            UseWallpaperHoverCheck.IsChecked = false;
            AccentHexBox.Text = brush.Color.ToString();
            UpdateAccentPreview();
            UpdateEnabledStates();

            var s = SettingsService.Instance;
            s.UserAccentColor = AccentHexBox.Text.Trim();
            SettingsService.NotifyPreviewColorsRequested();
        }
    }

    private void PaletteColor_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = Brushes.White;
        }
    }

    private void PaletteColor_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = Brushes.Transparent;
        }
    }

    private void GradientPreset_Click(object sender, RoutedEventArgs e)
    {
        string[]? colors = null;

        if (sender is FrameworkElement fe && fe.Tag is string tagStr)
        {
            if (GradientPresets.TryGetValue(tagStr, out var presetColors))
            {
                colors = presetColors;
            }
            else if (tagStr.Contains(','))
            {
                var parts = tagStr.Split(',');
                if (parts.Length == 3)
                {
                    colors = parts;
                }
            }
        }

        if (colors != null && colors.Length == 3)
        {
            _loading = true;
            UseWallpaperBorderCheck.IsChecked = false;
            BorderHex1.Text = colors[0];
            BorderHex2.Text = colors[1];
            BorderHex3.Text = colors[2];
            _loading = false;

            UpdateBorderPreviews();
            UpdateEnabledStates();

            // Real-time live preview on Dock
            var s = SettingsService.Instance;
            s.UserBorderColor1 = BorderHex1.Text.Trim();
            s.UserBorderColor2 = BorderHex2.Text.Trim();
            s.UserBorderColor3 = BorderHex3.Text.Trim();
            SettingsService.NotifyPreviewColorsRequested();
        }
    }

    private void Preset_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = Brushes.White;
        }
    }

    private void Preset_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        }
    }

    // ─── Actions ───

    private void Save_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            bool accentValid = UseWallpaperHoverCheck.IsChecked == true || TryParseColor(AccentHexBox.Text, out _);
            bool border1Valid = UseWallpaperBorderCheck.IsChecked == true || TryParseColor(BorderHex1.Text, out _);
            bool border2Valid = UseWallpaperBorderCheck.IsChecked == true || TryParseColor(BorderHex2.Text, out _);
            bool border3Valid = UseWallpaperBorderCheck.IsChecked == true || TryParseColor(BorderHex3.Text, out _);
            bool dockBgValid = UseDefaultDockBgCheck.IsChecked == true || TryParseColor(DockBgHexBox.Text, out _);

            bool effectColorsValid = TryParseColor(EffectColor_Rain_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Rain2_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Snow_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Snow2_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Fog_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Fog2_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Fireflies_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Fireflies2_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Thunderstorm_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Thunderstorm2_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Stars_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Stars2_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Aurora_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Aurora2_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Sakura_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Sakura2_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Leaves_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Leaves2_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Energy_Hex.Text, out _) &&
                                     TryParseColor(EffectColor_Energy2_Hex.Text, out _);

            bool isPersonalizado = false;
            if (SpotlightThemeModeCombo != null && SpotlightThemeModeCombo.SelectedItem is ComboBoxItem selectedTheme && selectedTheme.Tag != null)
            {
                isPersonalizado = selectedTheme.Tag.ToString() == "Personalizado";
            }

            bool spotlightColorsValid = !isPersonalizado ||
                                        ((string.IsNullOrEmpty(SpotlightBgHexBox.Text) || TryParseColor(SpotlightBgHexBox.Text, out _)) &&
                                         (string.IsNullOrEmpty(SpotlightBorderHexBox.Text) || TryParseColor(SpotlightBorderHexBox.Text, out _)) &&
                                         (string.IsNullOrEmpty(SpotlightTextHexBox.Text) || TryParseColor(SpotlightTextHexBox.Text, out _)) &&
                                         (string.IsNullOrEmpty(SpotlightPlaceholderHexBox.Text) || TryParseColor(SpotlightPlaceholderHexBox.Text, out _)) &&
                                         (string.IsNullOrEmpty(SpotlightIconHexBox.Text) || TryParseColor(SpotlightIconHexBox.Text, out _)) &&
                                         (string.IsNullOrEmpty(SpotlightAccentHexBox.Text) || TryParseColor(SpotlightAccentHexBox.Text, out _)) &&
                                         (string.IsNullOrEmpty(SpotlightHoverBgHexBox.Text) || TryParseColor(SpotlightHoverBgHexBox.Text, out _)) &&
                                         (string.IsNullOrEmpty(SpotlightHoverBorderHexBox.Text) || TryParseColor(SpotlightHoverBorderHexBox.Text, out _)) &&
                                         (string.IsNullOrEmpty(SpotlightItemBgHexBox.Text) || TryParseColor(SpotlightItemBgHexBox.Text, out _)) &&
                                         (string.IsNullOrEmpty(SpotlightItemFgHexBox.Text) || TryParseColor(SpotlightItemFgHexBox.Text, out _)) &&
                                         (string.IsNullOrEmpty(SpotlightSecondaryTextHexBox.Text) || TryParseColor(SpotlightSecondaryTextHexBox.Text, out _)) &&
                                         (string.IsNullOrEmpty(SpotlightSeparatorHexBox.Text) || TryParseColor(SpotlightSeparatorHexBox.Text, out _)) &&
                                         (string.IsNullOrEmpty(SpotlightFocusHexBox.Text) || TryParseColor(SpotlightFocusHexBox.Text, out _)));

            if (!accentValid || !border1Valid || !border2Valid || !border3Valid || !dockBgValid || !effectColorsValid || !spotlightColorsValid)
            {
                MessageBox.Show("Por favor, corrige los colores inválidos antes de guardar.", "Color Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var s = SettingsService.Instance;
            bool weatherChanged = s.WeatherCity != WeatherCityBox.Text.Trim();
            s.WeatherCity = WeatherCityBox.Text.Trim();

            s.UserAccentColor = UseWallpaperHoverCheck.IsChecked == true ? null : AccentHexBox.Text.Trim();
            s.UserBorderColor1 = UseWallpaperBorderCheck.IsChecked == true ? null : BorderHex1.Text.Trim();
            s.UserBorderColor2 = UseWallpaperBorderCheck.IsChecked == true ? null : BorderHex2.Text.Trim();
            s.UserBorderColor3 = UseWallpaperBorderCheck.IsChecked == true ? null : BorderHex3.Text.Trim();
            s.UserDockBackgroundColor = UseDefaultDockBgCheck.IsChecked == true ? null : DockBgHexBox.Text.Trim();

            // Save Spotlight settings
            s.SpotlightShowFavorites = SpotlightShowFavoritesCheck.IsChecked == true;
            if (SpotlightThemeModeCombo?.SelectedItem is ComboBoxItem selectedThemeItem && selectedThemeItem.Tag != null)
            {
                s.SpotlightThemeMode = selectedThemeItem.Tag.ToString() ?? "Dock";
                s.SpotlightUseDockColors = (s.SpotlightThemeMode == "Dock");
            }
            else
            {
                s.SpotlightThemeMode = "Dock";
                s.SpotlightUseDockColors = true;
            }
            s.SpotlightWidth = SpotlightWidthSlider.Value;
            s.SpotlightCornerRadius = SpotlightCornerRadiusSlider.Value;
            s.SpotlightBorderThickness = SpotlightBorderThicknessSlider.Value;

            if (SpotlightAppsPerRowCombo.SelectedItem is ComboBoxItem selectedAppsRowItem &&
                selectedAppsRowItem.Tag != null &&
                int.TryParse(selectedAppsRowItem.Tag.ToString(), out int appsPerRow))
            {
                s.SpotlightAppsPerRow = appsPerRow;
            }
            else
            {
                s.SpotlightAppsPerRow = 0;
            }

            s.SpotlightBackgroundColor = SpotlightBgHexBox.Text.Trim();
            s.SpotlightBorderColor = SpotlightBorderHexBox.Text.Trim();
            s.SpotlightTextColor = SpotlightTextHexBox.Text.Trim();
            s.SpotlightPlaceholderColor = SpotlightPlaceholderHexBox.Text.Trim();
            s.SpotlightIconColor = SpotlightIconHexBox.Text.Trim();
            s.SpotlightAccentColor = SpotlightAccentHexBox.Text.Trim();

            s.SpotlightHoverBackgroundColor = SpotlightHoverBgHexBox.Text.Trim();
            s.SpotlightHoverBorderColor = SpotlightHoverBorderHexBox.Text.Trim();
            s.SpotlightItemBackgroundColor = SpotlightItemBgHexBox.Text.Trim();
            s.SpotlightItemForegroundColor = SpotlightItemFgHexBox.Text.Trim();
            s.SpotlightSecondaryTextColor = SpotlightSecondaryTextHexBox.Text.Trim();
            s.SpotlightSeparatorColor = SpotlightSeparatorHexBox.Text.Trim();
            s.SpotlightSearchFocusColor = SpotlightFocusHexBox.Text.Trim();
            if (GeminiApiKeyBox != null) s.GeminiApiKey = GeminiApiKeyBox.Password;



            s.EnableDock = EnableDockCheck != null ? (EnableDockCheck.IsChecked == true) : (EnableDockInTabCheck?.IsChecked == true);
            s.EnableSpotlight = EnableSpotlightCheck != null ? (EnableSpotlightCheck.IsChecked == true) : (EnableSpotlightInTabCheck?.IsChecked == true);
            if (s.EnableSpotlight && string.IsNullOrWhiteSpace(Hotkey_PinnedApps_Spotlight_Text.Text))
            {
                Hotkey_PinnedApps_Spotlight_Text.Text = "Alt+Space";
            }

            s.AutoHide = AutoHideCheck.IsChecked == true;
            s.HideWindowsTaskbar = HideTaskbarCheck.IsChecked == true;
            s.DockPosition = DockPositionCombo.SelectedIndex == 1 ? "Bottom" : "Top";
            s.BorderThickness = Math.Round(ThicknessSlider.Value, 1);
            s.DockHeight = Math.Round(DockHeightSlider.Value, 0);
            s.AutoHideDurationMs = (int)Math.Round(AutoHideDurationSlider.Value, 0);
            s.AutoHideDelayMs = (int)Math.Round(AutoHideDelaySlider.Value, 0);
            s.DockMonitorIndex = MonitorCombo.SelectedIndex >= 0 ? MonitorCombo.SelectedIndex : 0;

            s.ShowClock = ShowClockCheck.IsChecked == true;
            s.Use24HourFormat = Use24HourCheck.IsChecked == true;
            s.ShowWeather = ShowWeatherCheck.IsChecked == true;
            s.ShowMediaPlayer = ShowMediaCheck.IsChecked == true;

            s.Hotkey_PinnedApps = Hotkey_PinnedApps_Spotlight_Text.Text;
            s.Hotkey_Power = Hotkey_Power_Text.Text;
            s.Hotkey_Settings = Hotkey_Settings_Text.Text;
            s.Hotkey_LocalGallery = Hotkey_LocalGallery_Text.Text;
            s.Hotkey_OnlineGallery = Hotkey_OnlineGallery_Text.Text;
            s.Hotkey_NextWallpaper = Hotkey_NextWallpaper_Text.Text;
            s.Hotkey_PrevWallpaper = Hotkey_PrevWallpaper_Text.Text;
            s.Hotkey_ToggleDock = Hotkey_ToggleDock_Text.Text;
            s.Hotkey_Clipboard = Hotkey_Clipboard_Text.Text;
            s.Hotkey_YouTube = Hotkey_YouTube_Text.Text;
            s.Hotkey_RunAsAdmin = Hotkey_RunAsAdmin_Spotlight_Text.Text;

            s.EffectColor_Rain = EffectColor_Rain_Hex.Text;
            s.EffectColor_Rain2 = EffectColor_Rain2_Hex.Text;
            s.EffectColor_Snow = EffectColor_Snow_Hex.Text;
            s.EffectColor_Snow2 = EffectColor_Snow2_Hex.Text;
            s.EffectColor_Fog = EffectColor_Fog_Hex.Text;
            s.EffectColor_Fog2 = EffectColor_Fog2_Hex.Text;
            s.EffectColor_Fireflies = EffectColor_Fireflies_Hex.Text;
            s.EffectColor_Fireflies2 = EffectColor_Fireflies2_Hex.Text;
            s.EffectColor_Thunderstorm = EffectColor_Thunderstorm_Hex.Text;
            s.EffectColor_Thunderstorm2 = EffectColor_Thunderstorm2_Hex.Text;
            s.EffectColor_Stars = EffectColor_Stars_Hex.Text;
            s.EffectColor_Stars2 = EffectColor_Stars2_Hex.Text;
            s.EffectColor_Aurora = EffectColor_Aurora_Hex.Text;
            s.EffectColor_Aurora2 = EffectColor_Aurora2_Hex.Text;
            s.EffectColor_Sakura = EffectColor_Sakura_Hex.Text;
            s.EffectColor_Sakura2 = EffectColor_Sakura2_Hex.Text;
            s.EffectColor_Leaves = EffectColor_Leaves_Hex.Text;
            s.EffectColor_Leaves2 = EffectColor_Leaves2_Hex.Text;
            s.EffectColor_Energy = EffectColor_Energy_Hex.Text;
            s.EffectColor_Energy2 = EffectColor_Energy2_Hex.Text;

            if (WeatherEffectCombo.SelectedItem is ComboBoxItem selectedItem)
            {
                s.SelectedWallpaperEffect = selectedItem.Content.ToString() ?? "Ninguno";
            }
            s.WeatherEffectSpeed = EffectSpeedSlider.Value;
            s.WeatherEffectSize = EffectSizeSlider.Value;
            s.WeatherEffectDensity = (int)EffectDensitySlider.Value;
            s.WeatherEffectMouseInteraction = MouseInteractionCheck.IsChecked == true;

            string? selectedFont = null;
            if (AppFontCombo.SelectedItem is ComboBoxItem selectedFontItem)
            {
                selectedFont = selectedFontItem.Content?.ToString();
            }
            else if (AppFontCombo.SelectedItem != null)
            {
                selectedFont = AppFontCombo.SelectedItem.ToString();
            }

            if (!string.IsNullOrEmpty(selectedFont))
            {
                s.AppFontFamily = selectedFont;
                App.ApplyFont(s.AppFontFamily);
            }

            // Save sound settings
            s.SoundEnabled = SoundEnabledCheck.IsChecked == true;
            s.SoundVolume = SoundVolumeSlider.Value / 100.0;
            if (SoundProfileCombo.SelectedItem is ComboBoxItem selectedProfileItem)
            {
                s.SoundProfile = selectedProfileItem.Tag?.ToString() ?? "Zen";
            }

            // Save custom dock icons and commands settings
            s.ShowIconPinnedApps = ShowIconPinnedAppsCheck.IsChecked == true;
            s.ShowIconLocalGallery = ShowIconLocalGalleryCheck.IsChecked == true;
            s.ShowIconOnlineGallery = ShowIconOnlineGalleryCheck.IsChecked == true;
            s.ShowIconYouTubeMusic = ShowIconYouTubeMusicCheck.IsChecked == true;
            s.ShowIconSettings = ShowIconSettingsCheck.IsChecked == true;
            s.ShowIconVolume = ShowIconVolumeCheck.IsChecked == true;
            s.ShowIconNotifications = ShowIconNotificationsCheck.IsChecked == true;
            s.ShowIconShowDesktop = ShowIconShowDesktopCheck.IsChecked == true;
            s.ShowIconPower = ShowIconPowerCheck.IsChecked == true;

            // Save Auto-Change Settings
            LocalGalleryWindow.SetAutoChangeEnabled(AutoChangeCheck.IsChecked == true);
            LocalGalleryWindow.SetCurrentSpeedIndex(AutoIntervalCombo.SelectedIndex);
            s.Hotkey_RunAsAdmin = Hotkey_RunAsAdmin_Spotlight_Text.Text;

            // Save custom dock icons and commands settings
            s.ShowIconPinnedApps = ShowIconPinnedAppsCheck.IsChecked == true;
            s.ShowIconLocalGallery = ShowIconLocalGalleryCheck.IsChecked == true;
            s.ShowIconOnlineGallery = ShowIconOnlineGalleryCheck.IsChecked == true;
            s.ShowIconYouTubeMusic = ShowIconYouTubeMusicCheck.IsChecked == true;
            s.ShowIconSettings = ShowIconSettingsCheck.IsChecked == true;
            s.ShowIconVolume = ShowIconVolumeCheck.IsChecked == true;
            s.ShowIconNotifications = ShowIconNotificationsCheck.IsChecked == true;
            s.ShowIconShowDesktop = ShowIconShowDesktopCheck.IsChecked == true;
            s.ShowIconPower = ShowIconPowerCheck.IsChecked == true;

            // Save Auto-Change Settings
            LocalGalleryWindow.SetAutoChangeEnabled(AutoChangeCheck.IsChecked == true);
            LocalGalleryWindow.SetCurrentSpeedIndex(AutoIntervalCombo.SelectedIndex);
            if (AutoModeCombo.SelectedIndex == 1)
                LocalGalleryWindow.SelectRandomMode();
            else
                LocalGalleryWindow.SelectSequentialMode();

            if (weatherChanged)
            {
                WeatherService.Instance.ResetCache();
            }

            ContextMenuWindow.SetAutoStart(StartWithWindowsCheck.IsChecked == true);

            // Sync and save settings via ViewModel
            _viewModel.LoadFromSettings();
            _viewModel.SaveToSettings();

            SystemCoordinator.Instance.RegisterGlobalHotkeys();
            Close();
        }
        catch { }
    }

    private void Reset_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (EnableDockCheck != null) EnableDockCheck.IsChecked = true;
            if (EnableDockInTabCheck != null) EnableDockInTabCheck.IsChecked = true;
            if (EnableSpotlightCheck != null) EnableSpotlightCheck.IsChecked = true;
            if (EnableSpotlightInTabCheck != null) EnableSpotlightInTabCheck.IsChecked = true;
            if (HideWindowsTaskbarGlobalCheck != null) HideWindowsTaskbarGlobalCheck.IsChecked = false;
            UpdateDockEnabledState(true);
            UpdateSpotlightEnabledState(true);

            UseWallpaperHoverCheck.IsChecked = true;
            UseWallpaperBorderCheck.IsChecked = true;
            UseDefaultDockBgCheck.IsChecked = true;
            AccentHexBox.Text = "#AF52DE";
            DockBgHexBox.Text = "#B40B0B0B";
            BorderHex1.Text = "#007AFF";
            BorderHex2.Text = "#5856D6";
            BorderHex3.Text = "#AF52DE";
            AutoHideCheck.IsChecked = false;
            HideTaskbarCheck.IsChecked = false;
            DockPositionCombo.SelectedIndex = 0;
            ThicknessSlider.Value = 3.8;
            ThicknessValueText.Text = "3.8 px";
            DockHeightSlider.Value = 56.0;
            DockHeightValueText.Text = "56 px";
            AutoHideDurationSlider.Value = 200;
            AutoHideDurationValueText.Text = "200 ms";
            AutoHideDelaySlider.Value = 0;
            AutoHideDelayValueText.Text = "0 ms";
            MonitorCombo.SelectedIndex = 0;

            StartWithWindowsCheck.IsChecked = ContextMenuWindow.IsAutoStartEnabled();

            ShowClockCheck.IsChecked = true;
            Use24HourCheck.IsChecked = false;
            ShowWeatherCheck.IsChecked = true;
            ShowMediaCheck.IsChecked = true;

            WeatherCityBox.Text = "";

            // Reset hotkeys
            Hotkey_PinnedApps_Spotlight_Text.Text = "";
            Hotkey_RunAsAdmin_Spotlight_Text.Text = "";
            Hotkey_Power_Text.Text = "";
            Hotkey_Settings_Text.Text = "";
            Hotkey_LocalGallery_Text.Text = "";
            Hotkey_OnlineGallery_Text.Text = "";
            Hotkey_NextWallpaper_Text.Text = "";
            Hotkey_PrevWallpaper_Text.Text = "";
            Hotkey_ToggleDock_Text.Text = "";
            Hotkey_Clipboard_Text.Text = "";
            Hotkey_WebBrowser_Text.Text = "";
            EffectColor_Rain2_Hex.Text = "#80007AFF";
            EffectColor_Snow_Hex.Text = "#FFFFFF";
            EffectColor_Snow2_Hex.Text = "#80B0E0FF";
            EffectColor_Fog_Hex.Text = "#1EC8C8C8";
            EffectColor_Fog2_Hex.Text = "#20A0E0FF";
            EffectColor_Fireflies_Hex.Text = "#FFFFBF00";
            EffectColor_Fireflies2_Hex.Text = "#FFDF3E00";
            EffectColor_Thunderstorm_Hex.Text = "#C896C8E6";
            EffectColor_Thunderstorm2_Hex.Text = "#804B0082";
            EffectColor_Stars_Hex.Text = "#FFFFFF";
            EffectColor_Stars2_Hex.Text = "#40FFE0B2";
            EffectColor_Aurora_Hex.Text = "#6600FF88";
            EffectColor_Aurora2_Hex.Text = "#660000FF";
            EffectColor_Sakura_Hex.Text = "#FFFFB7C5";
            EffectColor_Sakura2_Hex.Text = "#FFFA8072";
            EffectColor_Leaves_Hex.Text = "#FFCC6600";
            EffectColor_Leaves2_Hex.Text = "#FFA50000";
            EffectColor_Energy_Hex.Text = "#FF00BFFF";
            EffectColor_Energy2_Hex.Text = "#FFBF00FF";

            UpdateEffectColorPreviews();
            UpdateEffectColorsPanelVisibility();

            AutoChangeCheck.IsChecked = false;
            AutoModeCombo.SelectedIndex = 0;
            AutoIntervalCombo.SelectedIndex = 0;
            UpdateAutoChangePanelState();

            ShowIconPinnedAppsCheck.IsChecked = true;
            ShowIconLocalGalleryCheck.IsChecked = true;
            ShowIconOnlineGalleryCheck.IsChecked = true;
            ShowIconYouTubeMusicCheck.IsChecked = true;
            ShowIconSettingsCheck.IsChecked = true;
            ShowIconVolumeCheck.IsChecked = true;
            ShowIconNotificationsCheck.IsChecked = true;
            ShowIconShowDesktopCheck.IsChecked = true;
            ShowIconPowerCheck.IsChecked = true;

            UpdateEnabledStates();
            UpdateAccentPreview();
            UpdateBorderPreviews();

            // Reset Spotlight settings
            SpotlightShowFavoritesCheck.IsChecked = true;
            if (SpotlightThemeModeCombo != null) SpotlightThemeModeCombo.SelectedIndex = 0;
            SpotlightAppsPerRowCombo.SelectedIndex = 0;
            SpotlightWidthSlider.Value = 600;
            SpotlightCornerRadiusSlider.Value = 27;
            SpotlightBorderThicknessSlider.Value = 0;

            SpotlightBgHexBox.Text = "";
            SpotlightBorderHexBox.Text = "";
            SpotlightTextHexBox.Text = "";
            SpotlightPlaceholderHexBox.Text = "";
            SpotlightIconHexBox.Text = "";
            SpotlightAccentHexBox.Text = "";

            SpotlightHoverBgHexBox.Text = "";
            SpotlightHoverBorderHexBox.Text = "";
            SpotlightItemBgHexBox.Text = "";
            SpotlightItemFgHexBox.Text = "";
            SpotlightSecondaryTextHexBox.Text = "";
            SpotlightSeparatorHexBox.Text = "";
            SpotlightFocusHexBox.Text = "";

            UpdateSpotlightColorPreviews();

            _loading = false;
        }
        catch { }
    }

    private void ThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ThicknessValueText != null)
        {
            ThicknessValueText.Text = $"{e.NewValue:F1} px";
        }
    }

    private void AutoChangeCheck_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAutoChangePanelState();
    }

    private void AutoModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (AutoModeCombo.SelectedIndex == 1)
        {
            LocalGalleryWindow.SelectRandomMode();
        }
        else
        {
            LocalGalleryWindow.SelectSequentialMode();
        }
        _loading = true;
        AutoIntervalCombo.SelectedIndex = LocalGalleryWindow.GetCurrentSpeedIndex();
        _loading = false;
    }

    private void AutoIntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        LocalGalleryWindow.SetCurrentSpeedIndex(AutoIntervalCombo.SelectedIndex);
    }

    private void ResetBtn_MouseEnter(object sender, MouseEventArgs e)
    {
        try { ResetBtn.Background = new SolidColorBrush(Color.FromArgb(0x2D, 0xFF, 0xFF, 0xFF)); }
        catch { }
    }

    private void ResetBtn_MouseLeave(object sender, MouseEventArgs e)
    {
        try { ResetBtn.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)); }
        catch { }
    }

    private void SaveBtn_MouseEnter(object sender, MouseEventArgs e)
    {
        try { SaveBtn.Background = new SolidColorBrush(Color.FromRgb(0xC5, 0x7B, 0xFA)); }
        catch { }
    }

    private void SaveBtn_MouseLeave(object sender, MouseEventArgs e)
    {
        try { SaveBtn.Background = new SolidColorBrush(Color.FromRgb(0xAF, 0x52, 0xDE)); }
        catch { }
    }

    private void DetectWeatherLocation_Click(object sender, MouseButtonEventArgs e)
    {
        WeatherCityBox.Text = "";
    }

    private void DetectLocationBtn_MouseEnter(object sender, MouseEventArgs e)
    {
        try { DetectWeatherLocationBtn.Background = new SolidColorBrush(Color.FromArgb(0x2D, 0xFF, 0xFF, 0xFF)); }
        catch { }
    }

    private void DetectLocationBtn_MouseLeave(object sender, MouseEventArgs e)
    {
        try { DetectWeatherLocationBtn.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)); }
        catch { }
    }

    private void EffectSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (EffectSpeedValueText != null)
        {
            EffectSpeedValueText.Text = $"{e.NewValue:F1}x";
        }
    }

    private void EffectSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (EffectSizeValueText != null)
        {
            EffectSizeValueText.Text = $"{e.NewValue:F1}x";
        }
    }

    private void EffectDensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (EffectDensityValueText != null)
        {
            EffectDensityValueText.Text = $"{e.NewValue:F0}%";
        }
    }

    private void DockHeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DockHeightValueText != null)
        {
            DockHeightValueText.Text = $"{e.NewValue:F0} px";
        }
    }

    private void UseDefaultDockBg_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEnabledStates();
        if (UseDefaultDockBgCheck.IsChecked == true)
            DockBgHexBox.Text = "#B40B0B0B";
    }

    private void DockBgColor_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || DockBgPreview == null) return;
        UpdateDockBgPreview();
    }

    private void UpdateDockBgPreview()
    {
        try
        {
            ValidateColorInput(DockBgHexBox, DockBgPreview);
        }
        catch { }
    }

    private void AutoHideDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AutoHideDurationValueText != null)
        {
            AutoHideDurationValueText.Text = $"{e.NewValue:F0} ms";
        }
    }

    private void AutoHideDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AutoHideDelayValueText != null)
        {
            AutoHideDelayValueText.Text = $"{e.NewValue:F0} ms";
        }
    }

    private void ThemePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string theme)
        {
            _loading = true;
            UseWallpaperHoverCheck.IsChecked = false;
            UseWallpaperBorderCheck.IsChecked = false;
            UseDefaultDockBgCheck.IsChecked = false;

            if (ThemePresets.TryGetDockTheme(theme, out var preset) && preset != null)
            {
                AccentHexBox.Text = preset.Accent;
                BorderHex1.Text = preset.Border1;
                BorderHex2.Text = preset.Border2;
                BorderHex3.Text = preset.Border3;
                DockBgHexBox.Text = preset.DockBg;
            }

            _loading = false;
            UpdateAccentPreview();
            UpdateBorderPreviews();
            UpdateDockBgPreview();
            UpdateEnabledStates();

            // Apply theme colors directly to SettingsService and notify Dock in real-time
            try
            {
                var s = SettingsService.Instance;
                s.UserAccentColor = AccentHexBox.Text.Trim();
                s.UserBorderColor1 = BorderHex1.Text.Trim();
                s.UserBorderColor2 = BorderHex2.Text.Trim();
                s.UserBorderColor3 = BorderHex3.Text.Trim();
                s.UserDockBackgroundColor = DockBgHexBox.Text.Trim();

                SettingsService.NotifyPreviewColorsRequested();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ThemePreview error: " + ex.Message);
            }
        }
    }

    private void TabBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            int index = 0;
            if (btn == TabBtn1) index = 1;
            else if (btn == TabBtn2) index = 2;
            else if (btn == TabBtn3) index = 3;
            else if (btn == TabBtn4) index = 4;
            
            
            UpdateTabSelection(index);
        }
    }

    private void UpdateTabSelection(int index)
    {
        if (SettingsTabControl == null) return;
        SettingsTabControl.SelectedIndex = index;

        var buttons = new[] { TabBtn0, TabBtn1, TabBtn2, TabBtn3, TabBtn4 };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            if (i == index)
            {
                buttons[i].Background = (Brush)Application.Current.Resources["AccentGlowBrush"] ?? new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
                buttons[i].Foreground = (Brush)Application.Current.Resources["AccentBrush"] ?? Brushes.White;
            }
            else
            {
                buttons[i].Background = Brushes.Transparent;
                buttons[i].Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            }
        }
    }

    private void CaptureHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            string searchTag = tag.Replace("_Spotlight", "");
            var textBox = (FindName($"Hotkey_{tag}_Text") as TextBox)
                       ?? (FindName($"Hotkey_{searchTag}_Text") as TextBox);
            if (textBox != null)
            {
                if (_isCapturingHotkey && _activeCaptureTextBox != null)
                {
                    _activeCaptureTextBox.Text = _previousHotkeyText;
                    _activeCaptureTextBox.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
                }

                _isCapturingHotkey = true;
                _activeCaptureTextBox = textBox;
                _previousHotkeyText = textBox.Text;
                textBox.Text = "Presiona teclas...";
                textBox.Background = new SolidColorBrush(Color.FromArgb(0x80, 0xAF, 0x52, 0xDE));
            }
        }
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            string searchTag = tag.Replace("_Spotlight", "");
            var tb1 = FindName($"Hotkey_{searchTag}_Text") as TextBox;
            if (tb1 != null) tb1.Text = "";
            var tb2 = FindName($"Hotkey_{searchTag}_Spotlight_Text") as TextBox;
            if (tb2 != null) tb2.Text = "";
        }
    }

    private void Hotkey_PinnedApps_Spotlight_TextChanged(object sender, TextChangedEventArgs e)
    {
    }

    private void Hotkey_RunAsAdmin_Spotlight_TextChanged(object sender, TextChangedEventArgs e)
    {
    }

    private void EffectColor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        if (sender is TextBox textBox)
        {
            string name = textBox.Name;
            if (name.EndsWith("_Hex"))
            {
                string previewName = name.Replace("_Hex", "_Preview");
                if (FindName(previewName) is Border previewBorder)
                {
                    ValidateEffectColorInput(textBox, previewBorder);
                }
            }
        }
    }

    private void ValidateEffectColorInput(TextBox textBox, Border previewBorder)
    {
        if (textBox == null || previewBorder == null) return;

        if (TryParseColor(textBox.Text, out var color))
        {
            previewBorder.Background = new SolidColorBrush(color);
            textBox.ClearValue(BorderBrushProperty);
        }
        else
        {
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        }
    }

    private void UpdateEffectColorPreviews()
    {
        UpdateEffectColorPreview(EffectColor_Rain_Hex.Text, EffectColor_Rain_Preview);
        UpdateEffectColorPreview(EffectColor_Rain2_Hex.Text, EffectColor_Rain2_Preview);
        UpdateEffectColorPreview(EffectColor_Snow_Hex.Text, EffectColor_Snow_Preview);
        UpdateEffectColorPreview(EffectColor_Snow2_Hex.Text, EffectColor_Snow2_Preview);
        UpdateEffectColorPreview(EffectColor_Fog_Hex.Text, EffectColor_Fog_Preview);
        UpdateEffectColorPreview(EffectColor_Fog2_Hex.Text, EffectColor_Fog2_Preview);
        UpdateEffectColorPreview(EffectColor_Fireflies_Hex.Text, EffectColor_Fireflies_Preview);
        UpdateEffectColorPreview(EffectColor_Fireflies2_Hex.Text, EffectColor_Fireflies2_Preview);
        UpdateEffectColorPreview(EffectColor_Thunderstorm_Hex.Text, EffectColor_Thunderstorm_Preview);
        UpdateEffectColorPreview(EffectColor_Thunderstorm2_Hex.Text, EffectColor_Thunderstorm2_Preview);
        UpdateEffectColorPreview(EffectColor_Stars_Hex.Text, EffectColor_Stars_Preview);
        UpdateEffectColorPreview(EffectColor_Stars2_Hex.Text, EffectColor_Stars2_Preview);
        UpdateEffectColorPreview(EffectColor_Aurora_Hex.Text, EffectColor_Aurora_Preview);
        UpdateEffectColorPreview(EffectColor_Aurora2_Hex.Text, EffectColor_Aurora2_Preview);
        UpdateEffectColorPreview(EffectColor_Sakura_Hex.Text, EffectColor_Sakura_Preview);
        UpdateEffectColorPreview(EffectColor_Sakura2_Hex.Text, EffectColor_Sakura2_Preview);
        UpdateEffectColorPreview(EffectColor_Leaves_Hex.Text, EffectColor_Leaves_Preview);
        UpdateEffectColorPreview(EffectColor_Leaves2_Hex.Text, EffectColor_Leaves2_Preview);
        UpdateEffectColorPreview(EffectColor_Energy_Hex.Text, EffectColor_Energy_Preview);
        UpdateEffectColorPreview(EffectColor_Energy2_Hex.Text, EffectColor_Energy2_Preview);
    }

    private void UpdateEffectColorPreview(string hex, Border border)
    {
        try
        {
            if (TryParseColor(hex, out var color))
            {
                border.Background = new SolidColorBrush(color);
            }
            else
            {
                border.Background = Brushes.Transparent;
            }
        }
        catch
        {
            border.Background = Brushes.Transparent;
        }
    }

    private void WeatherEffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WeatherEffectCombo == null) return;
        UpdateEffectColorsPanelVisibility();
    }

    private void UpdateEffectColorsPanelVisibility()
    {
        if (WeatherEffectCombo == null || EffectColorsSectionPanel == null) return;

        string selectedEffect = "Ninguno";
        if (WeatherEffectCombo.SelectedItem is ComboBoxItem item)
        {
            selectedEffect = item.Content.ToString() ?? "Ninguno";
        }

        // Hide/show section border
        EffectColorsSectionPanel.Visibility = (selectedEffect == "Ninguno") ? Visibility.Collapsed : Visibility.Visible;

        // Hide all sub-panels
        var panels = new[]
        {
            ColorPanel_Rain, ColorPanel_Snow, ColorPanel_Fog, ColorPanel_Fireflies,
            ColorPanel_Thunderstorm, ColorPanel_Stars, ColorPanel_Aurora,
            ColorPanel_Sakura, ColorPanel_Leaves, ColorPanel_Energy
        };
        foreach (var p in panels)
        {
            if (p != null) p.Visibility = Visibility.Collapsed;
        }

        // Show the correct sub-panel
        switch (selectedEffect)
        {
            case "Lluvia":
                if (ColorPanel_Rain != null) ColorPanel_Rain.Visibility = Visibility.Visible;
                break;
            case "Nieve":
                if (ColorPanel_Snow != null) ColorPanel_Snow.Visibility = Visibility.Visible;
                break;
            case "Burbujas":
                if (ColorPanel_Fog != null) ColorPanel_Fog.Visibility = Visibility.Visible;
                break;
            case "Luciérnagas":
                if (ColorPanel_Fireflies != null) ColorPanel_Fireflies.Visibility = Visibility.Visible;
                break;
            case "Tormenta Eléctrica":
                if (ColorPanel_Thunderstorm != null) ColorPanel_Thunderstorm.Visibility = Visibility.Visible;
                break;
            case "Estrellas":
                if (ColorPanel_Stars != null) ColorPanel_Stars.Visibility = Visibility.Visible;
                break;
            case "Aurora Boreal":
                if (ColorPanel_Aurora != null) ColorPanel_Aurora.Visibility = Visibility.Visible;
                break;
            case "Hojas de Cerezo":
                if (ColorPanel_Sakura != null) ColorPanel_Sakura.Visibility = Visibility.Visible;
                break;
            case "Hojas de Otoño":
                if (ColorPanel_Leaves != null) ColorPanel_Leaves.Visibility = Visibility.Visible;
                break;
            case "Partículas de Energía":
                if (ColorPanel_Energy != null) ColorPanel_Energy.Visibility = Visibility.Visible;
                break;
        }
    }

    private void SoundEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSoundPanelState();
    }

    private void UpdateSoundPanelState()
    {
        if (SoundOptionsPanel != null)
        {
            SoundOptionsPanel.IsEnabled = SoundEnabledCheck.IsChecked == true;
            SoundOptionsPanel.Opacity = SoundEnabledCheck.IsChecked == true ? 1.0 : 0.35;
        }
    }

    private void SoundProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (SoundEnabledCheck.IsChecked == true && SoundProfileCombo.SelectedItem is ComboBoxItem item)
        {
            string profile = item.Tag?.ToString() ?? "Zen";
            SoundService.PlayPreview(profile, SoundVolumeSlider.Value / 100.0);
        }
    }

    private void SoundVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SoundVolumeText != null)
        {
            SoundVolumeText.Text = $"{(int)SoundVolumeSlider.Value}%";
        }
    }



    private void PopulateFonts()
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            AppFontCombo.Items.Clear();

            // 1. Add bundled fonts
            var bundledFonts = new string[]
            {
                "Outfit", "Orbitron", "Google Sans Flex"
            };

            foreach (var fontName in bundledFonts)
            {
                AppFontCombo.Items.Add(fontName);
            }

            // 2. Scan custom fonts folder
            var customFontsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallFlow", "CustomFonts");

            if (Directory.Exists(customFontsDir))
            {
                var fontFiles = Directory.GetFiles(customFontsDir);
                foreach (var file in fontFiles)
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (ext == ".ttf" || ext == ".otf")
                    {
                        try
                        {
                            string? familyName = null;
                            try
                            {
                                var glyphTypeface = new GlyphTypeface(new Uri(file));
                                if (glyphTypeface.Win32FamilyNames.TryGetValue(System.Globalization.CultureInfo.GetCultureInfo("en-us"), out var name))
                                {
                                    familyName = name;
                                }
                                else
                                {
                                    foreach (var val in glyphTypeface.Win32FamilyNames.Values)
                                    {
                                        familyName = val;
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                // Fallback to filename if file is locked or corrupt but has valid extension
                                familyName = Path.GetFileNameWithoutExtension(file);
                                if (familyName.EndsWith("-Regular", StringComparison.OrdinalIgnoreCase))
                                    familyName = familyName.Substring(0, familyName.Length - 8);
                                else if (familyName.EndsWith("-Bold", StringComparison.OrdinalIgnoreCase))
                                    familyName = familyName.Substring(0, familyName.Length - 5);
                                else if (familyName.EndsWith("-Medium", StringComparison.OrdinalIgnoreCase))
                                    familyName = familyName.Substring(0, familyName.Length - 7);
                                
                                // Replace underscores/hyphens with spaces for a cleaner look
                                familyName = familyName.Replace('-', ' ').Replace('_', ' ');
                            }

                            if (!string.IsNullOrEmpty(familyName))
                            {
                                // Avoid duplicates
                                bool exists = false;
                                foreach (var item in AppFontCombo.Items)
                                {
                                    string? fontItemName = (item is ComboBoxItem cbi) ? cbi.Content?.ToString() : item?.ToString();
                                    if (fontItemName?.Equals(familyName, StringComparison.OrdinalIgnoreCase) == true)
                                    {
                                        exists = true;
                                        break;
                                    }
                                }
                                if (!exists)
                                {
                                    AppFontCombo.Items.Add(familyName);
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }

            // Select active font
            var s = SettingsService.Instance;
            string activeFont = s.AppFontFamily ?? "Outfit";
            int selectIndex = 0;
            for (int i = 0; i < AppFontCombo.Items.Count; i++)
            {
                var item = AppFontCombo.Items[i];
                string? fontItemName = (item is ComboBoxItem cbi) ? cbi.Content?.ToString() : item?.ToString();
                if (fontItemName == activeFont)
                {
                    selectIndex = i;
                    break;
                }
            }
            AppFontCombo.SelectedIndex = selectIndex;
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private void UploadFontBtn_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Archivos de fuente (*.ttf;*.otf)|*.ttf;*.otf",
            Title = "Seleccionar Archivo de Fuente"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                string srcPath = openFileDialog.FileName;
                
                // Get the internal font family name to verify it's a valid font
                var glyphTypeface = new GlyphTypeface(new Uri(srcPath));
                string? familyName = null;
                if (glyphTypeface.Win32FamilyNames.TryGetValue(System.Globalization.CultureInfo.GetCultureInfo("en-us"), out var name))
                {
                    familyName = name;
                }
                else
                {
                    foreach (var val in glyphTypeface.Win32FamilyNames.Values)
                    {
                        familyName = val;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(familyName))
                {
                    MessageBox.Show("No se pudo extraer el nombre de la familia de fuentes de este archivo.", "Error de Fuente", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Copy to Local AppData
                var customFontsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WallFlow", "CustomFonts");
                
                Directory.CreateDirectory(customFontsDir);
                string ext = Path.GetExtension(srcPath);
                
                // Clean the family name to make it a valid filename
                string cleanFamilyName = string.Join("_", familyName.Split(Path.GetInvalidFileNameChars()));
                string destPath = Path.Combine(customFontsDir, cleanFamilyName + ext);
                
                File.Copy(srcPath, destPath, true);

                // Add to ComboBox items manually if not already present, avoiding direct file locks
                bool exists = false;
                for (int i = 0; i < AppFontCombo.Items.Count; i++)
                {
                    var item = AppFontCombo.Items[i];
                    string? fontItemName = (item is ComboBoxItem cbi) ? cbi.Content?.ToString() : item?.ToString();
                    if (string.Equals(fontItemName, familyName, StringComparison.OrdinalIgnoreCase))
                    {
                        AppFontCombo.SelectedIndex = i;
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    AppFontCombo.Items.Add(familyName);
                    AppFontCombo.SelectedIndex = AppFontCombo.Items.Count - 1;
                }

                // Apply changes immediately
                var s = SettingsService.Instance;
                s.AppFontFamily = familyName;
                SettingsService.Save();
                App.ApplyFont(familyName);

                MessageBox.Show($"Fuente '{familyName}' subida y aplicada con éxito.", "Fuente Subida", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al subir la fuente: {ex.Message}", "Error al Subir", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SpotlightBgHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || SpotlightBgPreview == null) return;
        ValidateSpotlightColorInput(SpotlightBgHexBox, SpotlightBgPreview);
    }

    private void SpotlightBorderHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || SpotlightBorderPreview == null) return;
        ValidateSpotlightColorInput(SpotlightBorderHexBox, SpotlightBorderPreview);
    }

    private void SpotlightTextHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || SpotlightTextPreview == null) return;
        ValidateSpotlightColorInput(SpotlightTextHexBox, SpotlightTextPreview);
    }

    private void SpotlightPlaceholderHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || SpotlightPlaceholderPreview == null) return;
        ValidateSpotlightColorInput(SpotlightPlaceholderHexBox, SpotlightPlaceholderPreview);
    }

    private void SpotlightIconHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || SpotlightIconPreview == null) return;
        ValidateSpotlightColorInput(SpotlightIconHexBox, SpotlightIconPreview);
    }

    private void SpotlightAccentHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || SpotlightAccentPreview == null) return;
        ValidateSpotlightColorInput(SpotlightAccentHexBox, SpotlightAccentPreview);
    }

    private void SpotlightHoverBgHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || SpotlightHoverBgPreview == null) return;
        ValidateSpotlightColorInput(SpotlightHoverBgHexBox, SpotlightHoverBgPreview);
    }

    private void SpotlightHoverBorderHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || SpotlightHoverBorderPreview == null) return;
        ValidateSpotlightColorInput(SpotlightHoverBorderHexBox, SpotlightHoverBorderPreview);
    }

    private void SpotlightItemBgHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || SpotlightItemBgPreview == null) return;
        ValidateSpotlightColorInput(SpotlightItemBgHexBox, SpotlightItemBgPreview);
    }

    private void SpotlightItemFgHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || SpotlightItemFgPreview == null) return;
        ValidateSpotlightColorInput(SpotlightItemFgHexBox, SpotlightItemFgPreview);
    }

    private void SpotlightSecondaryTextHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || SpotlightSecondaryTextPreview == null) return;
        ValidateSpotlightColorInput(SpotlightSecondaryTextHexBox, SpotlightSecondaryTextPreview);
    }

    private void SpotlightSeparatorHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || SpotlightSeparatorPreview == null) return;
        ValidateSpotlightColorInput(SpotlightSeparatorHexBox, SpotlightSeparatorPreview);
    }

    private void SpotlightFocusHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || SpotlightFocusPreview == null) return;
        ValidateSpotlightColorInput(SpotlightFocusHexBox, SpotlightFocusPreview);
    }

    private void ValidateSpotlightColorInput(TextBox textBox, Border previewBorder)
    {
        if (textBox == null || previewBorder == null) return;

        if (string.IsNullOrWhiteSpace(textBox.Text))
        {
            previewBorder.Background = Brushes.Transparent;
            textBox.ClearValue(BorderBrushProperty);
            return;
        }

        if (TryParseColor(textBox.Text, out var color))
        {
            previewBorder.Background = new SolidColorBrush(color);
            textBox.ClearValue(BorderBrushProperty);
        }
        else
        {
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red #EF4444
        }
    }

    private void UpdateSpotlightColorPreviews()
    {
        ValidateSpotlightColorInput(SpotlightBgHexBox, SpotlightBgPreview);
        ValidateSpotlightColorInput(SpotlightBorderHexBox, SpotlightBorderPreview);
        ValidateSpotlightColorInput(SpotlightTextHexBox, SpotlightTextPreview);
        ValidateSpotlightColorInput(SpotlightPlaceholderHexBox, SpotlightPlaceholderPreview);
        ValidateSpotlightColorInput(SpotlightIconHexBox, SpotlightIconPreview);
        ValidateSpotlightColorInput(SpotlightAccentHexBox, SpotlightAccentPreview);
        ValidateSpotlightColorInput(SpotlightHoverBgHexBox, SpotlightHoverBgPreview);
        ValidateSpotlightColorInput(SpotlightHoverBorderHexBox, SpotlightHoverBorderPreview);
        ValidateSpotlightColorInput(SpotlightItemBgHexBox, SpotlightItemBgPreview);
        ValidateSpotlightColorInput(SpotlightItemFgHexBox, SpotlightItemFgPreview);
        ValidateSpotlightColorInput(SpotlightSecondaryTextHexBox, SpotlightSecondaryTextPreview);
        ValidateSpotlightColorInput(SpotlightSeparatorHexBox, SpotlightSeparatorPreview);
        ValidateSpotlightColorInput(SpotlightFocusHexBox, SpotlightFocusPreview);
    }

    private void SpotlightThemeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        UpdateEnabledStates();
    }

    private void ColorPicker_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string hexBoxName)
        {
            using var dialog = new System.Windows.Forms.ColorDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                if (FindName(hexBoxName) is TextBox hexBox)
                    hexBox.Text = hex;
            }
        }
    }

    private void SelectComboItemByTag(ComboBox combo, string tagValue)
    {
        if (combo == null) return;
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && (item.Tag as string) == tagValue)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void MouseInteractionCheck_Changed(object sender, RoutedEventArgs e)
    {
    }
}
