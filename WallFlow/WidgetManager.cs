using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WallFlow.Services;
using WallFlow.ViewModels;

namespace WallFlow;

public class WidgetManager
{
    private static WidgetManager? _instance;
    public static WidgetManager Instance => _instance ??= new WidgetManager();

    private readonly Dictionary<string, Func<double, IWidget>> _widgetFactories = new(StringComparer.OrdinalIgnoreCase);

    // Active widget state tracking
    private IWidget? _activeWidget;
    private string? _activeWidgetType;
    private DateTime _lastWidgetCloseTime = DateTime.MinValue;
    private string? _lastClosedWidgetType;

    // View model reference
    private DockViewModel? _viewModel;

    // Window reference for wallpaper effects
    private Windows.WallpaperEffectsWindow? _wallpaperEffectsWindow;

    public void RegisterWidget(string type, Func<double, IWidget> factory)
    {
        _widgetFactories[type] = factory;
    }

    public void SetViewModel(DockViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public WidgetManager()
    {
        _instance = this;
        // Subscribe to SettingsChangedEvent via EventBus
        EventBus.Subscribe<SettingsChangedEvent>(OnSettingsChanged);
    }

    private void OnSettingsChanged(SettingsChangedEvent @event)
    {
        // Force reload wallpaper effects if settings changed
        UpdateWallpaperEffects();
    }

    public void ToggleWidget(string type)
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() => ToggleWidget(type)));
            return;
        }

        // Avoid race condition / bounce effect when widget closes by focus loss and button click triggers immediately
        if (_lastClosedWidgetType == type && (DateTime.Now - _lastWidgetCloseTime).TotalMilliseconds < 250)
        {
            _lastWidgetCloseTime = DateTime.MinValue;
            _lastClosedWidgetType = null;
            return;
        }

        if (type.Equals("LocalGallery", StringComparison.OrdinalIgnoreCase))
        {
            ToggleLocalGallery();
            return;
        }

        if (type.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
        {
            ToggleYouTubeMusic();
            return;
        }

        if (type.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase))
        {
            ToggleAiAssistant("ChatGPT");
            return;
        }

        if (type.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            ToggleAiAssistant("Gemini");
            return;
        }

        if (_activeWidget != null)
        {
            if (_activeWidgetType != null && _activeWidgetType.Equals(type, StringComparison.OrdinalIgnoreCase))
            {
                CloseActiveWidget();
                return;
            }
            else
            {
                CloseActiveWidget();
            }
        }
        else
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (type.Equals("OnlineGallery", StringComparison.OrdinalIgnoreCase) && window is Windows.OnlineGalleryWindow og && og.IsVisible)
                {
                    og.Close();
                    RecordWidgetClose(type);
                    return;
                }
            }
        }

        if (!_widgetFactories.TryGetValue(type, out var factory))
        {
            return;
        }

        _activeWidgetType = type;
        SoundService.Play(SoundType.Whoosh);

        try
        {
            double targetLeft = CalculateHorizontalCenterForWidget(type);
            var widget = factory(targetLeft);
            _activeWidget = widget;

            SetDockStateFlag(type, true);

            widget.Closed += (s, args) =>
            {
                SetDockStateFlag(type, false);
                if (_activeWidget == widget) ClearActiveWidget();
            };

            widget.Open();
        }
        catch
        {
            ClearActiveWidget();
        }

        // Publish event that a widget was toggled
        EventBus.Publish(new WidgetToggledEvent(type));
    }

    private void ToggleYouTubeMusic()
    {
        if (_activeWidget != null)
        {
            CloseActiveWidget();
        }

        var win = Windows.YouTubeMusicWindow.Instance;
        if (win == null || !win.IsLoaded)
        {
            win = new Windows.YouTubeMusicWindow();
        }

        // Prevent bounce when focus loss caused SafeHide() right before button click
        if ((DateTime.Now - win.LastHiddenTime).TotalMilliseconds < 350)
        {
            return;
        }

        if (win.Visibility == Visibility.Visible && win.Opacity > 0.1)
        {
            win.SafeHide();
            if (_viewModel != null) _viewModel.IsYouTubeMusicOpen = false;
        }
        else
        {
            win.ShowWithAnimation();
            if (_viewModel != null) _viewModel.IsYouTubeMusicOpen = true;
        }

        RecordWidgetClose("YouTube");
        EventBus.Publish(new WidgetToggledEvent("YouTube"));
    }

    private void ToggleAiAssistant(string mode)
    {
        if (_activeWidget != null)
        {
            CloseActiveWidget();
        }

        var win = Windows.AiAssistantWindow.Instance;
        if (win == null || !win.IsLoaded)
        {
            win = new Windows.AiAssistantWindow();
        }

        if ((DateTime.Now - win.LastHiddenTime).TotalMilliseconds < 350 && win.CurrentMode == mode)
        {
            return;
        }

        FrameworkElement? targetButton = DockWindow.Instance?.ChatGPTButton;

        if (win.Visibility == Visibility.Visible && win.Opacity > 0.1)
        {
            if (win.CurrentMode == mode)
            {
                win.SafeHide();
                if (_viewModel != null)
                {
                    _viewModel.IsAiAssistantOpen = false;
                }
            }
            else
            {
                win.ShowWithAnimation(mode, targetButton);
                if (_viewModel != null)
                {
                    _viewModel.IsAiAssistantOpen = true;
                }
            }
        }
        else
        {
            win.ShowWithAnimation(mode, targetButton);
            if (_viewModel != null)
            {
                _viewModel.IsAiAssistantOpen = true;
            }
        }

        RecordWidgetClose(mode);
        EventBus.Publish(new WidgetToggledEvent(mode));
    }

    private void ToggleLocalGallery()
    {
        if (_activeWidget != null)
        {
            CloseActiveWidget();
        }

        var main = LocalGalleryWindow.Instance;
        if (main != null)
        {
            if (main.Visibility == Visibility.Visible)
            {
                main.Visibility = Visibility.Hidden;
                if (_viewModel != null) _viewModel.IsLocalGalleryOpen = false;
            }
            else
            {
                main.Visibility = Visibility.Visible;
                try { main.Activate(); } catch { }
                if (_viewModel != null) _viewModel.IsLocalGalleryOpen = true;
            }
        }
        _activeWidget = null;
        _activeWidgetType = null;
    }

    private void SetDockStateFlag(string type, bool isOpen)
    {
        if (_viewModel == null) return;

        switch (type.ToLower())
        {
            case "settings":
                _viewModel.IsSettingsOpen = isOpen;
                break;
            case "onlinegallery":
                _viewModel.IsOnlineGalleryOpen = isOpen;
                break;
            case "youtube":
                _viewModel.IsYouTubeMusicOpen = isOpen;
                break;
            case "power":
                _viewModel.IsPowerWindowOpen = isOpen;
                break;
            case "pinnedapps":
            case "clipboard":
                _viewModel.IsPinnedFlyoutOpen = isOpen;
                break;
        }
    }

    private double CalculateHorizontalCenterForWidget(string type)
    {
        double widgetWidth = 300; // default fallbacks
        string anchorName = "";

        switch (type.ToLower())
        {
            case "calendar":
                widgetWidth = 410;
                anchorName = "Clock";
                break;
            case "weather":
                widgetWidth = 310;
                anchorName = "Weather";
                break;
            case "media":
                widgetWidth = 300;
                anchorName = "Media";
                break;
        }

        return CalculateHorizontalCenter(widgetWidth, anchorName);
    }

    private double CalculateHorizontalCenter(double widgetWidth, string anchorName)
    {
        double left = SystemParameters.PrimaryScreenWidth / 2 - widgetWidth / 2; // Default screen center fallback

        if (DockWindow.Instance != null && DockWindow.Instance.IsVisible)
        {
            try
            {
                FrameworkElement? anchorElement = null;
                if (anchorName == "Clock") anchorElement = DockWindow.Instance.ClockBlock;
                else if (anchorName == "Weather") anchorElement = DockWindow.Instance.WeatherButton;
                else if (anchorName == "Media") anchorElement = DockWindow.Instance.MediaWidget;

                if (anchorElement != null && anchorElement.IsVisible)
                {
                    var dpi = VisualTreeHelper.GetDpi(DockWindow.Instance);
                    var screenPos = anchorElement.PointToScreen(new Point(0, 0));
                    double elementLeft = screenPos.X / dpi.DpiScaleX;
                    left = elementLeft - (widgetWidth - anchorElement.ActualWidth) / 2;
                }
            }
            catch { /* fallback to default center */ }
        }
        return left;
    }

    public void UpdateWallpaperEffects()
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(UpdateWallpaperEffects));
            return;
        }

        try
        {
            string selectedEffect = SettingsService.Instance.SelectedWallpaperEffect ?? "Ninguno";
            if (selectedEffect != "Ninguno")
            {
                if (_wallpaperEffectsWindow == null)
                {
                    _wallpaperEffectsWindow = new Windows.WallpaperEffectsWindow();
                    _wallpaperEffectsWindow.Closed += (s, args) => { _wallpaperEffectsWindow = null; };
                    _wallpaperEffectsWindow.Show();
                }
                _wallpaperEffectsWindow.SetEffect(selectedEffect);
            }
            else
            {
                CloseWallpaperEffects();
            }
        }
        catch { /* ignored */ }
    }

    public void CloseWallpaperEffects()
    {
        if (_wallpaperEffectsWindow != null)
        {
            try { _wallpaperEffectsWindow.Close(); } catch { }
            _wallpaperEffectsWindow = null;
        }
    }

    public void StopWallpaperEffectsRendering()
    {
        if (_wallpaperEffectsWindow != null)
        {
            try { _wallpaperEffectsWindow.StopRendering(); } catch { }
        }
    }

    public void StartWallpaperEffectsRendering()
    {
        if (_wallpaperEffectsWindow != null)
        {
            try { _wallpaperEffectsWindow.StartRendering(); } catch { }
        }
    }

    public void CloseActiveWidget()
    {
        if (_activeWidget != null)
        {
            try { _activeWidget.Close(); } catch { }
        }
        ClearActiveWidget();
    }

    public void RecordWidgetClose(string type)
    {
        _lastWidgetCloseTime = DateTime.Now;
        _lastClosedWidgetType = type;
    }

    private void ClearActiveWidget()
    {
        _lastWidgetCloseTime = DateTime.Now;
        _lastClosedWidgetType = _activeWidgetType;

        _activeWidget = null;
        _activeWidgetType = null;
        DockWindow.Instance?.CheckAutoHide();
    }

    public bool IsAnyWidgetOpen()
    {
        return _activeWidget != null && _activeWidget.IsOpen;
    }

    public void CloseAllWidgets()
    {
        CloseActiveWidget();
    }
}
