using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WallFlow.Services;

namespace WallFlow.Windows;

public partial class AiAssistantWindow : Window, IWidget
{
    string IWidget.Name => "AiAssistant";
    public void Open() { ShowWithAnimation(); }
    public void CloseWidget() { SafeHide(); }
    public bool IsOpen => IsLoaded && Visibility == Visibility.Visible;
    private bool _isPinned = false;
    private bool _isHiding = false;
    private bool _allowClose = false;
    private Microsoft.Web.WebView2.Core.CoreWebView2Environment? _webViewEnv;
    public DateTime LastHiddenTime { get; private set; } = DateTime.MinValue;

    public static AiAssistantWindow? Instance { get; private set; }

    public string CurrentMode { get; private set; } = "ChatGPT";

    private const string ChatGPTUrl = "https://chatgpt.com";
    private const string GeminiUrl = "https://gemini.google.com";

    public AiAssistantWindow()
    {
        Instance = this;
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        BackdropHelper.ApplyAcrylic(this);
        RootBorder.SizeChanged += UpdateBorderClip;
        UpdateBorderClip(null, null);

        PositionWindow();
        InitializeWebViewAsync();
    }

    private void UpdateBorderClip(object? sender, RoutedEventArgs? e)
    {
        if (RootBorder == null) return;
        var w = RootBorder.ActualWidth;
        var h = RootBorder.ActualHeight;
        RootBorder.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 8, 8);
        Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 8, 8);
        BackdropHelper.UpdateWindowRegion(this, 8);
    }

    public void PositionWindow(FrameworkElement? targetButton = null)
    {
        if (DockWindow.Instance != null)
        {
            try
            {
                var btn = targetButton ?? DockWindow.Instance.ChatGPTButton;
                if (btn != null && btn.IsVisible)
                {
                    var dpi = VisualTreeHelper.GetDpi(DockWindow.Instance);
                    var screenPos = btn.PointToScreen(new Point(0, 0));
                    double left = screenPos.X / dpi.DpiScaleX;
                    Left = left - (Width - btn.ActualWidth) / 2;
                }
                else
                {
                    Left = DockWindow.Instance.Left + (DockWindow.Instance.ActualWidth - Width) / 2;
                }
            }
            catch
            {
                Left = DockWindow.Instance.Left + (DockWindow.Instance.ActualWidth - Width) / 2;
            }

            if (Left < 10) Left = 10;
            if (Left + Width > SystemParameters.PrimaryScreenWidth - 10)
                Left = SystemParameters.PrimaryScreenWidth - Width - 10;

            bool isBottom = SettingsService.Instance.DockPosition == "Bottom";
            double finalTop = isBottom 
                ? DockWindow.Instance.Top - Height 
                : DockWindow.Instance.Top + DockWindow.Instance.ActualHeight;
            Top = finalTop;
        }
        else
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            bool isBottom = SettingsService.Instance.DockPosition == "Bottom";
            Top = isBottom ? SystemParameters.PrimaryScreenHeight - Height - 56 : 56;
        }
    }

    private async void InitializeWebViewAsync()
    {
        try
        {
            var settingsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallFlow");
            var webViewFolder = Path.Combine(settingsFolder, "WebView2_AiAssistant");
            
            if (!Directory.Exists(webViewFolder))
            {
                Directory.CreateDirectory(webViewFolder);
            }

            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(userDataFolder: webViewFolder);
            _webViewEnv = env;
            await webView.EnsureCoreWebView2Async(env);
            
            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.HistoryChanged += (s, args) => UpdateNavigationButtonStates();
                UpdateNavigationButtonStates();
            }

            SwitchToMode(CurrentMode);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al iniciar el navegador de IA: {ex.Message}", "Error WebView2", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void SwitchToChatGPT()
    {
        SwitchToMode("ChatGPT");
    }

    public void SwitchToGemini()
    {
        SwitchToMode("Gemini");
    }

    public void SwitchToMode(string mode)
    {
        CurrentMode = mode;
        UpdateTabUI();

        if (webView != null && webView.CoreWebView2 != null)
        {
            string targetUrl = mode == "Gemini" ? GeminiUrl : ChatGPTUrl;
            webView.Source = new Uri(targetUrl);
        }
    }

    private void UpdateTabUI()
    {
        var accentBrush = (Brush)FindResource("AccentBrush");
        var activeBg = (Brush)FindResource("AccentActiveBrush");
        var transparentBg = Brushes.Transparent;
        var whiteBrush = Brushes.White;
        var mutedBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));

        if (CurrentMode == "ChatGPT")
        {
            TabChatGpt.Background = activeBg;
            TabChatGpt.Foreground = whiteBrush;

            TabGemini.Background = transparentBg;
            TabGemini.Foreground = mutedBrush;
        }
        else
        {
            TabGemini.Background = activeBg;
            TabGemini.Foreground = whiteBrush;

            TabChatGpt.Background = transparentBg;
            TabChatGpt.Foreground = mutedBrush;
        }
    }

    private void UpdateNavigationButtonStates()
    {
        if (webView != null && webView.CoreWebView2 != null)
        {
            BackButton.IsEnabled = webView.CanGoBack;
            ForwardButton.IsEnabled = webView.CanGoForward;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (webView.CanGoBack) webView.GoBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (webView.CanGoForward) webView.GoForward();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        webView.Reload();
    }

    private void TabChatGpt_Click(object sender, RoutedEventArgs e)
    {
        SwitchToChatGPT();
    }

    private void TabGemini_Click(object sender, RoutedEventArgs e)
    {
        SwitchToGemini();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        PinButton.Background = _isPinned 
            ? (SolidColorBrush)FindResource("AccentBrush") 
            : Brushes.Transparent;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SafeHide();
    }

    public void ShowWithAnimation(string mode = "ChatGPT", FrameworkElement? targetButton = null)
    {
        // Cancel any running animations to prevent property locking and bounce glitches
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(TopProperty, null);
        _isHiding = false;

        SoundService.Play(SoundType.Whoosh);
        PositionWindow(targetButton);

        if (CurrentMode != mode)
        {
            SwitchToMode(mode);
        }

        Visibility = Visibility.Visible;
        Opacity = 0;

        bool isBottom = SettingsService.Instance.DockPosition == "Bottom";
        double targetTop;
        if (isBottom)
        {
            targetTop = DockWindow.Instance != null 
                ? DockWindow.Instance.Top - Height 
                : SystemParameters.PrimaryScreenHeight - Height - 56;
        }
        else
        {
            targetTop = DockWindow.Instance != null 
                ? DockWindow.Instance.Top + DockWindow.Instance.ActualHeight 
                : 56;
        }

        double startTop = isBottom ? targetTop + 15 : targetTop - 15;
        Top = startTop;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slide = new DoubleAnimation(startTop, targetTop, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        BeginAnimation(OpacityProperty, fadeIn);
        BeginAnimation(TopProperty, slide);

        Activate();
        Focus();
    }

    public void SafeHide()
    {
        if (_isHiding || Visibility != Visibility.Visible) return;

        // Cancel any running animations to prevent property locking
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(TopProperty, null);

        SoundService.Play(SoundType.Whoosh);
        _isHiding = true;
        LastHiddenTime = DateTime.Now;

        if (DockWindow.Instance != null)
        {
            DockWindow.Instance.IsAiAssistantOpen = false;
        }

        bool isBottom = SettingsService.Instance.DockPosition == "Bottom";
        double startTop = Top;
        double targetTop = isBottom ? startTop + 15 : startTop - 15;

        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var slide = new DoubleAnimation(startTop, targetTop, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (snd, args) =>
        {
            if (_isHiding)
            {
                Visibility = Visibility.Hidden;
                _isHiding = false;
                BeginAnimation(OpacityProperty, null);
                BeginAnimation(TopProperty, null);
            }
            if (DockWindow.Instance != null)
            {
                DockWindow.Instance.IsAiAssistantOpen = false;
            }
        };

        BeginAnimation(OpacityProperty, fadeOut);
        BeginAnimation(TopProperty, slide);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!_isPinned && !_isHiding && Visibility == Visibility.Visible)
        {
            SafeHide();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SafeHide();
        }
        else if (e.Key == Key.F5)
        {
            webView.Reload();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            SafeHide();
            return;
        }
        base.OnClosing(e);
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }
}
