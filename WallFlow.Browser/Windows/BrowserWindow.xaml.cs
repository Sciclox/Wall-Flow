using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text.Json;
using WallFlow.Services;

namespace WallFlow.Windows;

public partial class BrowserWindow : Window
{
    private static readonly HttpClient _httpClient = new();

    static BrowserWindow()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    private static readonly List<SuggestionItem> CommonSuggestions = new()
    {
        new SuggestionItem { Title = "Google", Url = "https://www.google.com" },
        new SuggestionItem { Title = "YouTube", Url = "https://www.youtube.com" },
        new SuggestionItem { Title = "YouTube Music", Url = "https://music.youtube.com" },
        new SuggestionItem { Title = "GitHub", Url = "https://github.com" },
        new SuggestionItem { Title = "Wikipedia", Url = "https://wikipedia.org" },
        new SuggestionItem { Title = "Reddit", Url = "https://www.reddit.com" },
        new SuggestionItem { Title = "Wallhaven", Url = "https://wallhaven.cc" },
        new SuggestionItem { Title = "Facebook", Url = "https://www.facebook.com" },
        new SuggestionItem { Title = "Twitter / X", Url = "https://x.com" },
        new SuggestionItem { Title = "Instagram", Url = "https://www.instagram.com" }
    };

    private List<BookmarkItem> _bookmarks = new();
    private Microsoft.Web.WebView2.Core.CoreWebView2Environment? _webViewEnv;

    // Workspace & Tabs fields
    private readonly System.Collections.ObjectModel.ObservableCollection<TabGroup> _groups = new();
    private TabGroup? _activeGroup;
    private readonly System.Collections.ObjectModel.ObservableCollection<BrowserTab> _fallbackTabs = new();
    private System.Collections.ObjectModel.ObservableCollection<BrowserTab> _tabs => _activeGroup?.Tabs ?? _fallbackTabs;
    private BrowserTab? _activeTab;
    private string _workspaceName = "Principal";
    private bool _isPinned = false;
    private bool _isMaximized = false;
    private bool _isHiding = false;
    private bool _allowClose = false;
    public DateTime LastHiddenTime { get; private set; } = DateTime.MinValue;
    private Rect _restoreBounds = Rect.Empty;
    public double CascadeOffset { get; set; } = 0;
    private string? _initialUrl;

    // Favicon loading: version counter to prevent race conditions between
    // SourceChanged and FaviconChanged events calling LoadFaviconAsync concurrently.
    private int _faviconLoadVersion = 0;
    private CancellationTokenSource? _faviconCts;

    // Fullscreen state
    private bool _isFullScreen = false;
    private bool _isExitingFullScreen = false;
    public bool IsFullScreen => _isFullScreen;
    private Rect _preFullScreenBounds;
    private bool _preFullScreenTopmost;
    private bool _preFullScreenIsMaximized;
    private CornerRadius _preFullScreenRootCornerRadius;
    private Thickness _preFullScreenRootBorderThickness;
    private Thickness _preFullScreenGridMargin;
    private CornerRadius _preFullScreenWebViewCornerRadius;

    private bool _bookmarksReceivedMouseDown = false;

    public BrowserWindow(string? initialUrl = null)
    {
        InitializeComponent();
        _initialUrl = initialUrl;

        if (System.Threading.SynchronizationContext.Current == null)
        {
            System.Threading.SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Threading.DispatcherSynchronizationContext(Application.Current.Dispatcher));
        }

        BookmarksPopup.Opened += (s, ev) => _bookmarksReceivedMouseDown = false;

        // Bind groups source
        GroupsItemsControl.ItemsSource = _groups;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Apply window icon programmatically to support both assemblies
        try
        {
            var assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
            var iconUri = new Uri($"pack://application:,,,/{assemblyName};component/browser_icon.png", UriKind.Absolute);
            this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
        }
        catch
        {
            try
            {
                var iconUri = new Uri("pack://application:,,,/browser_icon.png", UriKind.Absolute);
                this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
            }
            catch { }
        }

        // Apply acrylic blur and allow to show in Alt-Tab
        BackdropHelper.ApplyAcrylic(this, roundCorners: true, hideFromAltTab: false);
        BackdropHelper.UpdateWindowRegion(this, 0);

        // Intercept clicks to save group edits
        this.PreviewMouseLeftButtonDown += BrowserWindow_PreviewMouseLeftButtonDown;

        // Subscribe to size and location changes
        this.SizeChanged += Window_SizeChanged;
        this.LocationChanged += Window_LocationChanged;

        // Ensure Alt-Tab visibility when activated, and bring this window to the top (Smart Topmost).
        // Only this window gets Topmost=true while active; others relinquish it on Deactivated.
        this.Activated += (s, ev) =>
        {
            BackdropHelper.ForceAltTabVisibility(this, true);
            if (_isPinned)
            {
                Topmost = true;
            }

            // Pinned windows must always float above everything — including the window that just
            // activated. Re-assert their Topmost via a false→true toggle so Windows re-inserts
            // them at the top of the Topmost Z-order stack.
            foreach (var pinned in Application.Current.Windows
                .OfType<BrowserWindow>()
                .Where(w => w != this && w._isPinned))
            {
                pinned.Topmost = false;
                pinned.Topmost = true;
            }
        };

        // Position window relative to the Dock
        PositionWindow();

        if (WindowState == WindowState.Normal && !_isMaximized && !_isFullScreen)
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
        }

        // Initialize WebView2 safely
        InitializeWebViewAsync();

        // Handle composition host resize to update active controller bounds
        WebViewHost.HostResized += OnWebViewHostResized;

        // Apply custom browser colors if configured
        ApplyBrowserColors();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        BackdropHelper.UpdateWindowRegion(this, 0);
        ClampToDockBounds();

        if (WindowState == WindowState.Normal && !_isMaximized && !_isFullScreen)
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
        }
    }

    private void OnWebViewHostResized(double width, double height)
    {
        if (_activeTab?.WebView != null)
        {
            _activeTab.WebView.Resize(width, height);
        }
    }

    private void PositionWindow()
    {
        // Find other open and visible browser windows
        var otherWindows = Application.Current.Windows.OfType<BrowserWindow>()
            .Where(w => w != this && w.Visibility == Visibility.Visible)
            .OrderByDescending(w => w.Left)
            .ToList();

        if (otherWindows.Count > 0)
        {
            var prevWin = otherWindows[0]; // The rightmost one
            Left = prevWin.Left + prevWin.Width + 2;
            Top = prevWin.Top;

            // Keep within screen work area
            var workArea = SystemParameters.WorkArea;
            if (Left + Width > workArea.Right - 10)
            {
                // Wrap around to the left edge of the work area
                Left = workArea.Left + 10;
                // Shift down slightly to not overlap exactly
                Top = prevWin.Top + 30;
                if (Top + Height > workArea.Bottom - 10)
                {
                    Top = workArea.Top + 56;
                }
            }
            return;
        }

        // Default positioning for the first window
        if (ApplicationHost.Instance != null)
        {
            try
            {
                // Align with the WebBrowserButton in the dock
                var btn = ApplicationHost.Instance.WebBrowserButton;
                var dpi = VisualTreeHelper.GetDpi((System.Windows.Window)ApplicationHost.Instance);
                var screenPos = btn.PointToScreen(new Point(0, 0));
                double left = screenPos.X / dpi.DpiScaleX;
                Left = left - (Width - btn.ActualWidth) / 2;
            }
            catch
            {
                Left = ApplicationHost.Instance.Left + (ApplicationHost.Instance.ActualWidth - Width) / 2;
            }

            // Keep within screen bounds
            if (Left < 10) Left = 10;
            if (Left + Width > SystemParameters.PrimaryScreenWidth - 10)
                Left = SystemParameters.PrimaryScreenWidth - Width - 10;

            bool dockIsBottom = Services.SettingsService.Instance?.DockPosition == "Bottom";
            double finalTop;
            if (dockIsBottom)
            {
                finalTop = ApplicationHost.Instance.Top - Height;
                if (finalTop < SystemParameters.WorkArea.Top + 10)
                    finalTop = SystemParameters.WorkArea.Top + 10;
            }
            else
            {
                finalTop = ApplicationHost.Instance.Top + ApplicationHost.Instance.ActualHeight;
                if (finalTop + Height > SystemParameters.WorkArea.Bottom - 10)
                    finalTop = SystemParameters.WorkArea.Bottom - Height - 10;
            }
            Top = finalTop;
        }
        else
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 56;
        }
    }

    private async void InitializeWebViewAsync()
    {
        try
        {
            // Initialize dynamic ad blocker rules (background loading)
            AdBlockerService.Initialize();

            var settingsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallFlow");
            var webViewFolder = Path.Combine(settingsFolder, "WebView2_Browser");
            
            if (!Directory.Exists(webViewFolder))
            {
                Directory.CreateDirectory(webViewFolder);
            }

            _webViewEnv = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(userDataFolder: webViewFolder);

            // Since we set the SynchronizationContext in the constructor, we are guaranteed 
            // to resume on the UI thread after the await above.
            LoadMostVisited();
            LoadBookmarks();

            if (_groups.Count == 0)
            {
                var defaultGroup = new TabGroup { Name = "Principal", IsActive = true };
                _groups.Add(defaultGroup);
                _activeGroup = defaultGroup;

                string startUrl = string.IsNullOrEmpty(_initialUrl) ? "https://www.google.com" : _initialUrl;
                CreateNewTab(startUrl);
            }
            else
            {
                if (_activeGroup == null && _groups.Count > 0)
                {
                    _activeGroup = _groups.FirstOrDefault(g => g.IsActive) ?? _groups[0];
                }

                if (_activeGroup != null)
                {
                    _activeGroup.IsActive = true;
                    int selIndex = 0;
                    if (_activeGroup.ActiveTab != null)
                    {
                        selIndex = _activeGroup.Tabs.IndexOf(_activeGroup.ActiveTab);
                    }
                    selIndex = Math.Clamp(selIndex, 0, _tabs.Count - 1);
                    if (_tabs.Count > 0)
                    {
                        SelectTab(_tabs[selIndex]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al iniciar el navegador web: {ex.Message}", "Error WebView2", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CoreWebView2_HistoryChanged(object? sender, object e)
    {
        UpdateNavigationButtonStates();
    }

    private async void CoreWebView2_NewWindowRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NewWindowRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            e.Handled = true;
            string url = e.Uri;

            string docHost = string.Empty;
            bool isThirdParty = false;
            try
            {
                var sourceUri = new Uri(this._activeTab?.WebView?.Source?.ToString() ?? string.Empty);
                docHost = sourceUri.Host;
                var reqUri = new Uri(url);
                isThirdParty = !reqUri.Host.Equals(docHost, StringComparison.OrdinalIgnoreCase) &&
                               !reqUri.Host.EndsWith("." + docHost, StringComparison.OrdinalIgnoreCase);
            }
            catch { /* ignored */ }

            if (!string.IsNullOrEmpty(url) && AdBlockerService.IsUrlBlocked(url, AdBlockResourceType.Document, docHost, isThirdParty))
            {
                return;
            }

            // Bloquear popups automáticos no iniciados por el usuario
            if (!e.IsUserInitiated)
            {
                return;
            }

            var newTab = new BrowserTab
            {
                Url = string.IsNullOrEmpty(url) ? "about:blank" : url,
                Title = "Cargando..."
            };
            _tabs.Add(newTab);

            var compositionTab = new CompositionWebViewTab();
            await compositionTab.InitializeAsync(_webViewEnv!, WebViewHost.Handle);
            compositionTab.Attach(WebViewHost.HostVisual!);
            newTab.WebView = compositionTab;
            SetupTabWebView(newTab);

            e.NewWindow = compositionTab.CoreWebView2;
            SelectTab(newTab);
        }
        catch { /* ignored */ }
        finally
        {
            deferral.Complete();
        }
    }

    private void UpdateNavigationButtonStates()
    {
        if (_activeTab?.WebView?.CoreWebView2 != null)
        {
            BackButton.IsEnabled = _activeTab.WebView.CoreWebView2.CanGoBack;
            ForwardButton.IsEnabled = _activeTab.WebView.CoreWebView2.CanGoForward;
        }
        else
        {
            BackButton.IsEnabled = false;
            ForwardButton.IsEnabled = false;
        }
    }

    private static string GetBlockedPageHtml(string blockedUrl)
    {
        string host;
        try { host = WebUtility.HtmlEncode(new Uri(blockedUrl).Host); }
        catch { host = WebUtility.HtmlEncode(blockedUrl); }

        return $@"<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'><title>Sitio bloqueado</title></head>
<body style='background:#151518;color:white;font-family:Segoe UI,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;flex-direction:column;text-align:center;padding:20px;'>
    <div style='font-size:56px;margin-bottom:16px;'>&#x26A0;&#xFE0F;</div>
    <h1 style='margin:0 0 8px;font-weight:400;'>Sitio bloqueado por seguridad</h1>
    <p style='color:#aaa;max-width:400px;'>El dominio <strong style='color:#ff9800;'>{host}</strong> est&#xE1; en la lista de bloqueo de WallFlow.</p>
    <p style='color:#666;font-size:13px;margin:4px 0 20px;'>Este sitio puede contener publicidad no deseada o rastreadores.</p>
    <button onclick='window.history.back();' style='padding:10px 36px;border-radius:8px;border:1px solid #555;background:#222;color:white;cursor:pointer;font-size:14px;'>Volver atr&#xE1;s</button>
</body>
</html>";
    }

    private void UpdateSecurityIndicator(Uri? source)
    {
        if (source == null)
        {
            SecurityIcon.Foreground = new SolidColorBrush(Color.FromArgb(96, 255, 255, 255));
            SecurityIcon.ToolTip = null;
            return;
        }

        string scheme = source.Scheme;
        if (scheme == "https")
        {
            SecurityIcon.Foreground = new SolidColorBrush(Color.FromArgb(200, 76, 175, 80));
            SecurityIcon.ToolTip = "Conexión segura (HTTPS)";
        }
        else if (scheme == "http")
        {
            SecurityIcon.Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 152, 0));
            SecurityIcon.ToolTip = "Conexión no segura (HTTP)";
        }
        else
        {
            SecurityIcon.Foreground = new SolidColorBrush(Color.FromArgb(96, 255, 255, 255));
            SecurityIcon.ToolTip = null;
        }
    }

    private Point GetDockTargetPoint()
    {
        if (ApplicationHost.Instance != null)
        {
            var button = ApplicationHost.Instance.WebBrowserButton;
            if (button != null && button.IsLoaded)
            {
                try
                {
                    // Center of the WebBrowserButton in screen coordinates
                    var screenPoint = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
                    // Convert screen point to window relative point
                    return this.PointFromScreen(screenPoint);
                }
                catch
                {
                    // Fallback if PointToScreen fails
                }
            }
        }
        // Fallback: top center of the window area
        return new Point(this.ActualWidth / 2, -30);
    }

    private void ClampToDockBounds()
    {
        if (WindowState == WindowState.Maximized || _isFullScreen) return;
        if (ApplicationHost.Instance == null || Visibility != Visibility.Visible) return;
        try
        {
            bool dockIsBottom = Services.SettingsService.Instance?.DockPosition == "Bottom";
            double dockTop = ApplicationHost.Instance.Top;
            double dockHeight = ApplicationHost.Instance.ActualHeight;

            if (dockIsBottom)
            {
                // Ensure window bottom edge does not exceed dock top edge
                double maxBottom = dockTop;
                if (Top + Height > maxBottom)
                {
                    double overflow = Top + Height - maxBottom;
                    if (Height - overflow >= 100)
                        Height -= overflow;
                    else
                        Top = maxBottom - Height;
                }
            }
            else
            {
                // Ensure window top edge does not go above dock bottom edge
                double minTop = dockTop + dockHeight;
                if (Top < minTop)
                {
                    double overflow = minTop - Top;
                    if (Height - overflow >= 100)
                    {
                        Top = minTop;
                        Height -= overflow;
                    }
                    else
                    {
                        Top = minTop;
                    }
                }
            }
        }
        catch { /* ignored */ }
    }

    public void ShowWithAnimation()
    {
        WebViewHost.Visibility = Visibility.Visible;
        SoundService.Play(SoundType.Whoosh);
        _isHiding = false; // Reset any stuck hide state
        
        if (!this.IsLoaded)
        {
            PositionWindow();
        }

        // Resume active tab WebView if it was suspended
        if (_activeTab != null && _activeTab.WebView != null && _activeTab.WebView.CoreWebView2 != null)
        {
            try
            {
                _activeTab.WebView.CoreWebView2.Resume();
            }
            catch { /* ignored */ }
        }

        // Clamp coordinates to screen bounds if not maximized
        if (!_isMaximized && WindowState != WindowState.Maximized)
        {
            ClampToDockBounds();
            this.WindowState = WindowState.Normal; // Ensure it's not minimized
        }

        this.Visibility = Visibility.Visible;

        var duration = TimeSpan.FromMilliseconds(300);
        var fadeIn = new DoubleAnimation(0, 1, duration);

        fadeIn.Completed += (snd, args) =>
        {
            this.BeginAnimation(OpacityProperty, null);
            this.Opacity = 1;
        };

        this.BeginAnimation(OpacityProperty, fadeIn);
        this.Activate();
        this.Focus();
    }

    public void SafeHide()
    {
        if (_isHiding || this.Visibility != Visibility.Visible) return;
        SoundService.Play(SoundType.Whoosh);
        _isHiding = true;
        LastHiddenTime = DateTime.Now;

        var duration = TimeSpan.FromMilliseconds(300);
        var fadeOut = new DoubleAnimation(1, 0, duration);

        fadeOut.Completed += (snd, args) =>
        {
            WebViewHost.Visibility = Visibility.Collapsed;
            this.Visibility = Visibility.Hidden;
            _isHiding = false;
            this.BeginAnimation(OpacityProperty, null);
            this.Opacity = 1;

            // Suspend active WebView2 instances if they are not playing audio
            foreach (var t in _tabs)
            {
                if (t.WebView != null && t.WebView.CoreWebView2 != null)
                {
                    try
                    {
                        bool isPlayingAudio = t.WebView.CoreWebView2.IsDocumentPlayingAudio;
                        if (!isPlayingAudio)
                        {
                            _ = t.WebView.CoreWebView2.TrySuspendAsync();
                        }
                    }
                    catch { /* ignored */ }
                }
            }
            
            if (ApplicationHost.Instance != null)
            {
                ApplicationHost.Instance.UpdateBrowserOpenState();
            }
        };

        this.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.WebView?.CoreWebView2 != null && _activeTab.WebView.CoreWebView2.CanGoBack)
        {
            _activeTab.WebView.CoreWebView2.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.WebView?.CoreWebView2 != null && _activeTab.WebView.CoreWebView2.CanGoForward)
        {
            _activeTab.WebView.CoreWebView2.GoForward();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.WebView?.CoreWebView2 != null)
        {
            _activeTab.WebView.CoreWebView2.Reload();
        }
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.WebView?.CoreWebView2 != null)
        {
            _activeTab.WebView.CoreWebView2.Navigate("https://www.google.com");
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        if (_isPinned)
        {
            Topmost = true; // Pinned windows always stay on top
            PinButton.SetResourceReference(ForegroundProperty, "AccentBrush");
            PinButton.ToolTip = "Desanclar (Ocultar automáticamente)";
        }
        else
        {
            Topmost = false;
            PinButton.ClearValue(ForegroundProperty);
            PinButton.ToolTip = "Mantener visible (Anclar)";
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        this.Close();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // Smart Topmost: relinquish the top Z-position when this window loses focus,
        // UNLESS it's pinned (user explicitly wants it always on top).
        // With Topmost=false, the window naturally goes behind the newly focused app
        // like any standard window — no need to hide it.
        if (!_isPinned)
            Topmost = false;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_isFullScreen)
            {
                // Exit fullscreen by executing the document's exitFullscreen
                _activeTab?.WebView?.CoreWebView2?.ExecuteScriptAsync("document.exitFullscreen()");
            }
        }
    }

    private void AddressTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        AddressTextBox.SelectAll();
        e.Handled = true;
    }

    private void AddressTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string input = AddressTextBox.Text.Trim();
            if (string.IsNullOrEmpty(input)) return;

            string targetUrl = ResolveSearchUrl(input);

            if (_activeTab != null)
            {
                _activeTab.Url = targetUrl;
                if (_activeTab.WebView != null)
                {
                    _activeTab.WebView.Source = new Uri(targetUrl);
                }
                else
                {
                    SelectTab(_activeTab);
                }
            }

            // Defocus text box to let WebView capture keyboard input
            Keyboard.ClearFocus();
        }
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            SafeHide();
        }
        else
        {
            foreach (var tab in _tabs)
            {
                if (tab.WebView != null)
                {
                    try { tab.WebView.Dispose(); } catch { /* ignored */ }
                }
            }
            _tabs.Clear();
            base.OnClosing(e);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _faviconCts?.Dispose();
        base.OnClosed(e);
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        // Clear active animations to prevent property locks during maximize/restore
        this.BeginAnimation(Window.TopProperty, null);
        this.BeginAnimation(OpacityProperty, null);

        if (WindowState == WindowState.Minimized)
        {
            if (_activeTab?.WebView != null)
            {
                _activeTab.WebView.Detach();
            }
        }
        else if (WindowState == WindowState.Maximized)
        {
            _isMaximized = true;

            // Remove decorations for maximized state
            RootBorder.CornerRadius = new CornerRadius(0);
            RootBorder.BorderThickness = new Thickness(0);
            RootBorder.Effect = null;
            MainGrid.Margin = new Thickness(15, 10, 15, 0);
            WebViewBorder.CornerRadius = new CornerRadius(0);
            WebViewHost.UpdateClip(WebViewHost.ActualWidth, WebViewHost.ActualHeight, 0);

            // Disable OS rounding to fill screen edges completely
            BackdropHelper.SetWindowRounding(this, false);

            MaximizeButton.Content = FindResource("RestoreIcon");
            MaximizeButton.ToolTip = "Restaurar";

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_activeTab?.WebView != null)
                {
                    _activeTab.WebView.Attach(WebViewHost.HostVisual!);
                    double dpiScale = 1.0;
                    try { dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { }
                    _activeTab.WebView.Resize(WebViewHost.ActualWidth * dpiScale, WebViewHost.ActualHeight * dpiScale);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        else if (WindowState == WindowState.Normal)
        {
            _isMaximized = false;

            // Restore decorations
            RootBorder.CornerRadius = new CornerRadius(8);
            RootBorder.BorderThickness = new Thickness(1);
            RootBorder.SetResourceReference(System.Windows.Controls.Border.StyleProperty, "FrostedFlyoutBorder");
            MainGrid.Margin = new Thickness(12);
            WebViewBorder.CornerRadius = new CornerRadius(8);
            WebViewHost.UpdateClip(WebViewHost.ActualWidth, WebViewHost.ActualHeight, 8);

            // Restore OS rounding
            BackdropHelper.SetWindowRounding(this, true);

            MaximizeButton.Content = FindResource("MaximizeIcon");
            MaximizeButton.ToolTip = "Maximizar";

            // Restore to normal size only if we are not exiting fullscreen
            if (!_isExitingFullScreen)
            {
                if (_restoreBounds != Rect.Empty)
                {
                    Left = _restoreBounds.Left;
                    Top = _restoreBounds.Top;
                    Width = _restoreBounds.Width;
                    Height = _restoreBounds.Height;
                }
                else
                {
                    Width = 600;
                    Height = 700;
                    PositionWindow();
                }
            }
            ClampToDockBounds();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_activeTab?.WebView != null)
                {
                    _activeTab.WebView.Attach(WebViewHost.HostVisual!);
                    double dpiScale = 1.0;
                    try { dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { }
                    _activeTab.WebView.Resize(WebViewHost.ActualWidth * dpiScale, WebViewHost.ActualHeight * dpiScale);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void NewWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (ApplicationHost.Instance != null)
        {
            ApplicationHost.Instance.OpenNewBrowserWindow();
        }
    }

    public void ResetSizeAndPosition()
    {
        WindowState = WindowState.Normal;
        Width = 600;
        Height = 700;
        PositionWindow();
        ClampToDockBounds();

        if (this.Visibility != Visibility.Visible)
        {
            ShowWithAnimation();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var hwndSource = HwndSource.FromHwnd(hwnd);
        hwndSource?.AddHook(DragAndResizeHook);

        // Force Alt-Tab and taskbar visibility at handle creation time
        BackdropHelper.ForceAltTabVisibility(this, true);
    }

    private const int WM_NCHITTEST = 0x0084;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        if (_isFullScreen) return;
        try
        {
            MINMAXINFO mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            // Get the screen containing the window
            var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
            var workingArea = screen.WorkingArea;
            var monitorArea = screen.Bounds;

            // In Windows, a maximized window is positioned slightly outside the working area
            // by the thickness of the window's resize borders so they are pushed off-screen.
            // We inspect the default ptMaxPosition to determine the exact border sizes Windows is using.
            int borderX = Math.Abs(mmi.ptMaxPosition.x);
            int borderY = Math.Abs(mmi.ptMaxPosition.y);

            // If the default values are 0 (e.g. if the window style doesn't have borders),
            // we fall back to a standard default of 8 pixels.
            if (borderX == 0) borderX = 8;
            if (borderY == 0) borderY = 8;

            // Set the maximized size to working area (respects Taskbars and AppBars like WallFlow Dock)
            // but expand and shift it by the border thickness to push the window borders off-screen.
            mmi.ptMaxSize.x = workingArea.Width + (borderX * 2);
            mmi.ptMaxSize.y = workingArea.Height + (borderY * 2);
            mmi.ptMaxPosition.x = (workingArea.X - monitorArea.X) - borderX;
            mmi.ptMaxPosition.y = (workingArea.Y - monitorArea.Y) - borderY;

            Marshal.StructureToPtr(mmi, lParam, false);
        }
        catch { /* ignored */ }
    }

    private IntPtr DragAndResizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0024) // WM_GETMINMAXINFO
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        else if (msg == WM_NCHITTEST)
        {
            // Disable resizing if window is maximized or in fullscreen mode
            if (_isMaximized || _isFullScreen)
            {
                return IntPtr.Zero;
            }

            // Get screen coordinates of the mouse
            int x = (short)(lParam.ToInt32() & 0xFFFF);
            int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

            // Convert to window coordinates
            var gridPoint = PointFromScreen(new Point(x, y));

            double borderThickness = 7; // Resize border hit thickness

            bool left = gridPoint.X <= borderThickness;
            bool right = gridPoint.X >= ActualWidth - borderThickness;
            bool top = gridPoint.Y <= borderThickness;
            bool bottom = gridPoint.Y >= ActualHeight - borderThickness;

            if (top && left) { handled = true; return new IntPtr(HTTOPLEFT); }
            if (top && right) { handled = true; return new IntPtr(HTTOPRIGHT); }
            if (bottom && left) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
            if (bottom && right) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }
            if (left) { handled = true; return new IntPtr(HTLEFT); }
            if (right) { handled = true; return new IntPtr(HTRIGHT); }
            if (top) { handled = true; return new IntPtr(HTTOP); }
            if (bottom) { handled = true; return new IntPtr(HTBOTTOM); }
        }
        return IntPtr.Zero;
    }

    private void CoreWebView2_WebResourceRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            string url = e.Request.Uri;
            var resType = MapToAdBlockResourceType(e.ResourceContext);
            
            string docHost = string.Empty;
            bool isThirdParty = false;
            
            if (sender is Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView2)
            {
                try
                {
                    var sourceUri = new Uri(coreWebView2.Source);
                    docHost = sourceUri.Host;
                    
                    var requestUri = new Uri(url);
                    string requestHost = requestUri.Host;
                    
                    isThirdParty = !requestHost.Equals(docHost, StringComparison.OrdinalIgnoreCase) &&
                                   !requestHost.EndsWith("." + docHost, StringComparison.OrdinalIgnoreCase);
                }
                catch { /* ignored */ }
            }

            if (AdBlockerService.IsUrlBlocked(url, resType, docHost, isThirdParty))
            {
                if (_webViewEnv != null)
                {
                    // Return 403 Forbidden with Content-Length: 0 to close connection instantly
                    e.Response = _webViewEnv.CreateWebResourceResponse(
                        new MemoryStream(), 
                        403, 
                        "Forbidden", 
                        "Content-Length: 0\nContent-Type: text/plain\nAccess-Control-Allow-Origin: *"
                    );
                }
            }
        }
        catch { /* ignored */ }
    }

    private void CoreWebView2_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "page_ready")
            {
                if (root.TryGetProperty("host", out var hostProp))
                {
                    string host = hostProp.GetString() ?? string.Empty;
                    string css = AdBlockerService.GetCosmeticCssForDomain(host);
                    if (!string.IsNullOrEmpty(css) && sender is Microsoft.Web.WebView2.Core.CoreWebView2 core)
                    {
                        var response = new { type = "inject_css", css = css };
                        string responseJson = JsonSerializer.Serialize(response);
                        core.PostWebMessageAsJson(responseJson);
                    }
                }
            }
        }
        catch { /* ignored */ }
    }

    private static AdBlockResourceType MapToAdBlockResourceType(Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext context)
    {
        return context switch
        {
            Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.Document => AdBlockResourceType.Document,
            Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.Stylesheet => AdBlockResourceType.Stylesheet,
            Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.Image => AdBlockResourceType.Image,
            Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.Media => AdBlockResourceType.Media,
            Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.Font => AdBlockResourceType.Font,
            Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.Script => AdBlockResourceType.Script,
            Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.XmlHttpRequest => AdBlockResourceType.XmlHttpRequest,
            Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.Fetch => AdBlockResourceType.XmlHttpRequest,
            Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.Ping => AdBlockResourceType.Ping,
            _ => AdBlockResourceType.Other
        };
    }

    private void LoadBookmarks()
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallFlow");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            var filePath = Path.Combine(folder, "bookmarks.json");

            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var items = System.Text.Json.JsonSerializer.Deserialize<List<BookmarkItem>>(json);
                if (items != null)
                {
                    _bookmarks = items;
                }
            }
            else
            {
                // Create default bookmarks
                _bookmarks = new List<BookmarkItem>
                {
                    new BookmarkItem { Title = "Google", Url = "https://www.google.com" },
                    new BookmarkItem { Title = "YouTube", Url = "https://www.youtube.com" },
                    new BookmarkItem { Title = "GitHub", Url = "https://github.com" },
                    new BookmarkItem { Title = "Wallhaven", Url = "https://wallhaven.cc" }
                };
                SaveBookmarks();
            }
        }
        catch 
        {
            // Fail gracefully with fallback list
            _bookmarks = new List<BookmarkItem>
            {
                new BookmarkItem { Title = "Google", Url = "https://www.google.com" }
            };
        }
        UpdateBookmarksPanel();
    }

    private void SaveBookmarks()
    {
        try
        {
            var filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallFlow",
                "bookmarks.json");

            var json = System.Text.Json.JsonSerializer.Serialize(_bookmarks);
            File.WriteAllText(filePath, json);
        }
        catch { /* ignored */ }
    }

    private void UpdateBookmarksPanel()
    {
        try
        {
            BookmarksPopupPanel.Children.Clear();
            foreach (var item in _bookmarks)
            {
                var itemGrid = new Grid
                {
                    Margin = new Thickness(0, 2, 0, 2),
                    Height = 36,
                    Cursor = Cursors.Hand
                };

                var backgroundBorder = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    SnapsToDevicePixels = true
                };
                itemGrid.Children.Add(backgroundBorder);

                var contentGrid = new Grid { Margin = new Thickness(8, 0, 8, 0) };
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                itemGrid.MouseEnter += (s, e) =>
                {
                    backgroundBorder.Background = new SolidColorBrush(Color.FromArgb(0x12, 255, 255, 255));
                };
                itemGrid.MouseLeave += (s, e) =>
                {
                    backgroundBorder.Background = Brushes.Transparent;
                };

                itemGrid.MouseDown += (s, e) =>
                {
                    if (e.ChangedButton == MouseButton.Left)
                    {
                        if (_activeTab != null)
                        {
                            if (_activeTab.WebView?.CoreWebView2 != null)
                            {
                                _activeTab.WebView.CoreWebView2.Navigate(item.Url);
                            }
                            else if (_activeTab.WebView != null)
                            {
                                _activeTab.WebView.Source = new Uri(item.Url);
                            }
                            else
                            {
                                NavigateNewTabTo(item.Url);
                            }
                        }
                        BookmarksPopup.IsOpen = false;
                    }
                };

                var faviconImg = CreateFaviconImage(GetDomainName(item.Url), 14);
                Grid.SetColumn(faviconImg, 0);
                contentGrid.Children.Add(faviconImg);

                var titleText = new TextBlock
                {
                    Text = item.Title,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 255, 255, 255)),
                    FontSize = 11.5,
                    FontFamily = (FontFamily)FindResource("AppFontFamily"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                Grid.SetColumn(titleText, 1);
                contentGrid.Children.Add(titleText);

                var menu = new System.Windows.Controls.ContextMenu();

                var openItem = new System.Windows.Controls.MenuItem { Header = "Abrir" };
                openItem.Click += (s, e) => {
                    if (_activeTab?.WebView?.CoreWebView2 != null) _activeTab.WebView.CoreWebView2.Navigate(item.Url);
                    BookmarksPopup.IsOpen = false;
                };
                menu.Items.Add(openItem);

                var openNewTabItem = new System.Windows.Controls.MenuItem { Header = "Abrir en nueva pestaña" };
                openNewTabItem.Click += (s, e) => {
                    CreateNewTab(item.Url);
                    BookmarksPopup.IsOpen = false;
                };
                menu.Items.Add(openNewTabItem);

                var openNewItem = new System.Windows.Controls.MenuItem { Header = "Abrir en nueva ventana" };
                openNewItem.Click += (s, e) => {
                    ApplicationHost.Instance?.OpenNewBrowserWindow(item.Url);
                    BookmarksPopup.IsOpen = false;
                };
                menu.Items.Add(openNewItem);

                menu.Items.Add(new System.Windows.Controls.Separator());

                var editItem = new System.Windows.Controls.MenuItem { Header = "Editar..." };
                editItem.Click += (s, e) => {
                    EditBookmark(item);
                    BookmarksPopup.IsOpen = false;
                };
                menu.Items.Add(editItem);

                var deleteItem = new System.Windows.Controls.MenuItem { Header = "Eliminar" };
                deleteItem.Click += (s, e) => {
                    DeleteBookmark(item);
                    BookmarksPopup.IsOpen = false;
                };
                menu.Items.Add(deleteItem);

                itemGrid.ContextMenu = menu;
                itemGrid.Children.Add(contentGrid);
                BookmarksPopupPanel.Children.Add(itemGrid);
            }
        }
        catch { /* ignored */ }
    }

    private void EditBookmark(BookmarkItem item)
    {
        try
        {
            var editWin = new BookmarkEditWindow(item.Title, item.Url);
            editWin.Owner = this;
            if (editWin.ShowDialog() == true)
            {
                item.Title = editWin.BookmarkName;
                item.Url = editWin.BookmarkUrl;
                SaveBookmarks();
                UpdateBookmarksPanel();
                UpdateStarButtonState();
            }
        }
        catch { /* ignored */ }
    }

    private void DeleteBookmark(BookmarkItem item)
    {
        try
        {
            var result = MessageBox.Show($"¿Estás seguro de que deseas eliminar el marcador '{item.Title}'?", "Eliminar Marcador", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _bookmarks.Remove(item);
                SaveBookmarks();
                UpdateBookmarksPanel();
                UpdateStarButtonState();
            }
        }
        catch { /* ignored */ }
    }

    private void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag != null)
        {
            string url = string.Empty;
            if (btn.Tag is BookmarkItem item)
            {
                url = item.Url;
            }
            else
            {
                url = btn.Tag.ToString()!;
            }

            if (!string.IsNullOrEmpty(url) && _activeTab?.WebView?.CoreWebView2 != null)
            {
                _activeTab.WebView.CoreWebView2.Navigate(url);
            }
        }
    }

    private void StarButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string url = _activeTab?.WebView?.Source?.ToString() ?? AddressTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            var existing = _bookmarks.FirstOrDefault(b => b.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _bookmarks.Remove(existing);
            }
            else
            {
                string title = _activeTab?.WebView?.CoreWebView2?.DocumentTitle ?? string.Empty;
                if (string.IsNullOrEmpty(title))
                {
                    try { title = new Uri(url).Host; } catch { title = "Página Web"; }
                }

                _bookmarks.Add(new BookmarkItem { Title = title, Url = url });
            }

            SaveBookmarks();
            UpdateBookmarksPanel();
            UpdateStarButtonState();
        }
        catch { /* ignored */ }
    }

    private string NormalizeUrlForComparison(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        url = url.Trim().ToLower();
        if (url.EndsWith("/"))
        {
            url = url.Substring(0, url.Length - 1);
        }
        return url;
    }

    private void UpdateStarButtonState()
    {
        try
        {
            string url = _activeTab?.WebView?.Source?.ToString() ?? AddressTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                StarButton.Content = "\uF09A"; // star_border (outline)
                StarButton.Foreground = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255));
                StarButton.ToolTip = "Añadir a marcadores";
                return;
            }

            string normalizedActiveUrl = NormalizeUrlForComparison(url);
            var isBookmarked = _bookmarks.Any(b => NormalizeUrlForComparison(b.Url) == normalizedActiveUrl);
            if (isBookmarked)
            {
                StarButton.Content = "\uF31D"; // star_shine (filled)
                StarButton.Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)); // Gold color
                StarButton.ToolTip = "Quitar de marcadores";
            }
            else
            {
                StarButton.Content = "\uF09A"; // star_border (outline)
                StarButton.Foreground = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255));
                StarButton.ToolTip = "Añadir a marcadores";
            }
        }
        catch { /* ignored */ }
    }

    private void CoreWebView2_NavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
    {
        try
        {
            string url = e.Uri;

            // Forzar HTTPS
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains("://localhost") &&
                !url.Contains("://127.0.0.1") &&
                !url.Contains("://10.") &&
                !url.Contains("://172.") &&
                !url.Contains("://192.168.") &&
                !url.Contains("[::1]"))
            {
                string httpsUri = "https://" + url.Substring(7);
                e.Cancel = true;
                if (sender is Microsoft.Web.WebView2.Core.CoreWebView2 httpsCore)
                    httpsCore.Navigate(httpsUri);
                return;
            }

            // Cancel navigation if the target is an advertising/tracking domain
            string docHost = string.Empty;
            try { docHost = new Uri(url).Host; } catch { /* ignored */ }
            if (AdBlockerService.IsUrlBlocked(url, AdBlockResourceType.Document, docHost, false))
            {
                e.Cancel = true;
                if (sender is Microsoft.Web.WebView2.Core.CoreWebView2 blockedCore)
                    blockedCore.NavigateToString(GetBlockedPageHtml(url));
                return;
            }

            if (sender is Microsoft.Web.WebView2.Core.CoreWebView2 senderCore)
            {
                var tab = _tabs.FirstOrDefault(t => t.WebView?.CoreWebView2 == senderCore);
                if (tab != null)
                {
                    if (tab == _activeTab)
                    {
                        tab.Url = e.Uri;
                        UpdateTabVisibilityState();
                    }
                    try
                    {
                        string? currentHost = tab.WebView?.Source?.Host;
                        string newHost = new Uri(e.Uri).Host;

                        if (!string.Equals(currentHost, newHost, StringComparison.OrdinalIgnoreCase))
                        {
                            // Cross-domain navigation: reset favicon
                            _faviconCts?.Cancel();
                            _faviconLoadVersion++;
                            tab.Favicon = null;
                            UpdateTabsDropdown();
                        }
                    }
                    catch
                    {
                        _faviconCts?.Cancel();
                        _faviconLoadVersion++;
                        tab.Favicon = null;
                        UpdateTabsDropdown();
                    }
                }
            }
        }
        catch { /* ignored */ }
    }

    private void CoreWebView2_NavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        if (sender is Microsoft.Web.WebView2.Core.CoreWebView2 senderCore)
        {
            var tab = _tabs.FirstOrDefault(t => t.WebView?.CoreWebView2 == senderCore);
            if (tab != null)
            {
                if (tab.Favicon == null)
                {
                    LoadFaviconAsync(tab);
                }

                if (e.IsSuccess)
                {
                    RegisterPageVisit(senderCore.Source, senderCore.DocumentTitle);
                }
            }
        }
    }

    private void CoreWebView2_FaviconChanged(object? sender, object e)
    {
        if (sender is Microsoft.Web.WebView2.Core.CoreWebView2 senderCore)
        {
            var tab = _tabs.FirstOrDefault(t => t.WebView?.CoreWebView2 == senderCore);
            if (tab != null)
            {
                LoadFaviconAsync(tab);
            }
        }
    }

    private void CoreWebView2_ContainsFullScreenElementChanged(object? sender, object e)
    {
        Dispatcher.Invoke(() =>
        {
            if (sender is Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView2)
            {
                if (coreWebView2.ContainsFullScreenElement)
                {
                    EnterFullScreen();
                }
                else
                {
                    ExitFullScreen();
                }
            }
        });
    }

    private void CoreWebView2_PermissionRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
    }

    private void EnterFullScreen()
    {
        if (_isFullScreen) return;
        _isFullScreen = true;

        if (ApplicationHost.Instance != null)
        {
            ApplicationHost.Instance.OnBrowserFullScreenChanged(true);
        }

        // Save current state
        _preFullScreenBounds = new Rect(Left, Top, Width, Height);
        _preFullScreenTopmost = Topmost;
        _preFullScreenIsMaximized = _isMaximized;

        // Save visual properties
        _preFullScreenRootCornerRadius = RootBorder.CornerRadius;
        _preFullScreenRootBorderThickness = RootBorder.BorderThickness;
        _preFullScreenGridMargin = MainGrid.Margin;
        _preFullScreenWebViewCornerRadius = WebViewBorder.CornerRadius;

        // Hide browser UI elements
        TitleRow.Visibility = Visibility.Collapsed;
        AddressBarRow.Visibility = Visibility.Collapsed;

        // Remove all decorations
        RootBorder.CornerRadius = new CornerRadius(0);
        RootBorder.BorderThickness = new Thickness(0);
        RootBorder.Effect = null;
        MainGrid.Margin = new Thickness(0);
        WebViewBorder.CornerRadius = new CornerRadius(0);
        WebViewHost.UpdateClip(WebViewHost.ActualWidth, WebViewHost.ActualHeight, 0);

        // Change ResizeMode first so the OS treats it as a non-resizable window (hides 1px border)
        ResizeMode = ResizeMode.NoResize;

        // Force native maximization (since _isFullScreen is true, WmGetMinMaxInfo will return early, letting it cover the whole screen)
        WindowState = WindowState.Maximized;
        Topmost = true;

        // Disable OS rounding for fullscreen
        BackdropHelper.SetWindowRounding(this, false);
    }

    private void ExitFullScreen()
    {
        if (!_isFullScreen) return;
        _isFullScreen = false;

        if (ApplicationHost.Instance != null)
        {
            ApplicationHost.Instance.OnBrowserFullScreenChanged(false);
        }

        // Restore resize borders when exiting fullscreen
        ResizeMode = ResizeMode.CanResize;

        // Set the flag to prevent Window_StateChanged from overriding sizes during the state transition
        _isExitingFullScreen = true;
        WindowState = _preFullScreenIsMaximized ? WindowState.Maximized : WindowState.Normal;
        _isExitingFullScreen = false;

        // Restore browser UI elements
        TitleRow.Visibility = Visibility.Visible;
        AddressBarRow.Visibility = Visibility.Visible;
        // BookmarksBorder is restored to its previous state via the toggle button, so leave it as-is

        // Restore visual properties
        RootBorder.CornerRadius = _preFullScreenRootCornerRadius;
        RootBorder.BorderThickness = _preFullScreenRootBorderThickness;
        RootBorder.SetResourceReference(System.Windows.Controls.Border.StyleProperty, "FrostedFlyoutBorder");
        MainGrid.Margin = _preFullScreenGridMargin;
        WebViewBorder.CornerRadius = _preFullScreenWebViewCornerRadius;
        double clipRadius = _preFullScreenWebViewCornerRadius.TopLeft;
        WebViewHost.UpdateClip(WebViewHost.ActualWidth, WebViewHost.ActualHeight, clipRadius);

        // Restore window bounds only if it was NOT maximized
        if (!_preFullScreenIsMaximized)
        {
            Left = _preFullScreenBounds.Left;
            Top = _preFullScreenBounds.Top;
            Width = _preFullScreenBounds.Width;
            Height = _preFullScreenBounds.Height;
        }
        Topmost = _preFullScreenTopmost;
        _isMaximized = _preFullScreenIsMaximized;

        // Restore OS rounding
        BackdropHelper.SetWindowRounding(this, !_isMaximized);

        if (_isMaximized)
        {
            MaximizeButton.Content = FindResource("RestoreIcon");
            MaximizeButton.ToolTip = "Restaurar";
        }
        else
        {
            MaximizeButton.Content = FindResource("MaximizeIcon");
            MaximizeButton.ToolTip = "Maximizar";
        }
    }

    private async Task<BitmapImage?> DownloadFaviconAsync(string url, CancellationToken ct)
    {
        try
        {
            var bytes = await _httpClient.GetByteArrayAsync(url, ct);
            if (bytes == null || bytes.Length == 0) return null;

            using (var memoryStream = new MemoryStream(bytes))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = memoryStream;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }
        catch
        {
            return null;
        }
    }

    private async void LoadFaviconAsync(BrowserTab tab)
    {
        int myVersion = ++_faviconLoadVersion;
        if (_faviconCts != null) { _faviconCts.Cancel(); _faviconCts.Dispose(); }
        var cts = new CancellationTokenSource();
        _faviconCts = cts;
        var ct = cts.Token;

        try
        {
            if (tab?.WebView?.CoreWebView2 == null || tab.WebView.Source == null) return;

            string host = tab.WebView.Source.Host;
            if (string.IsNullOrEmpty(host)) return;

            // 1. Try CoreWebView2 GetFaviconAsync first (offline-friendly)
            try
            {
                using (var stream = await tab.WebView.CoreWebView2.GetFaviconAsync(Microsoft.Web.WebView2.Core.CoreWebView2FaviconImageFormat.Png))
                {
                    if (myVersion != _faviconLoadVersion) return;

                    if (stream != null && stream.Length > 0)
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = stream;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        tab.Favicon = bitmap;
                        UpdateTabsDropdown();
                        return;
                    }
                }
            }
            catch { /* ignored */ }

            if (myVersion != _faviconLoadVersion) return;

            // 2. Try FaviconUri (web fallback)
            try
            {
                string faviconUriStr = tab.WebView.CoreWebView2.FaviconUri;
                if (!string.IsNullOrEmpty(faviconUriStr))
                {
                    var bitmap = await DownloadFaviconAsync(faviconUriStr, ct);
                    if (myVersion != _faviconLoadVersion) return;
                    if (bitmap != null)
                    {
                        tab.Favicon = bitmap;
                        UpdateTabsDropdown();
                        return;
                    }
                }
            }
            catch { /* ignored */ }

            if (myVersion != _faviconLoadVersion) return;

            // 3. Fallback using Google t3
            try
            {
                string fallbackUrl = $"https://t3.gstatic.com/faviconV2?client=SOCIAL&type=FAVICON&fallback_opts=TYPE,SIZE,URL&url=https://{host}&size=32";
                var bitmap = await DownloadFaviconAsync(fallbackUrl, ct);
                if (myVersion != _faviconLoadVersion) return;
                if (bitmap != null)
                {
                    tab.Favicon = bitmap;
                    UpdateTabsDropdown();
                    return;
                }
            }
            catch { /* ignored */ }

            if (myVersion != _faviconLoadVersion) return;

            // 4. Backup fallback using s2
            try
            {
                string backupUrl = $"https://www.google.com/s2/favicons?sz=32&domain={host}";
                var bitmap = await DownloadFaviconAsync(backupUrl, ct);
                if (myVersion != _faviconLoadVersion) return;
                if (bitmap != null)
                {
                    tab.Favicon = bitmap;
                    UpdateTabsDropdown();
                    return;
                }
            }
            catch { /* ignored */ }

            if (myVersion != _faviconLoadVersion) return;

            tab.Favicon = null;
            UpdateTabsDropdown();
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (myVersion == _faviconLoadVersion)
            {
                tab.Favicon = null;
                UpdateTabsDropdown();
            }
        }
    }



    private void ToggleBookmarksButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _bookmarksReceivedMouseDown = true;
    }

    private void ToggleBookmarksButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_bookmarksReceivedMouseDown)
        {
            return;
        }
        _bookmarksReceivedMouseDown = false;

        UpdateBookmarksPanel();
        BookmarksPopup.IsOpen = true;
    }

    private void AddressTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        string query = AddressTextBox.Text.Trim();
        if (string.IsNullOrEmpty(query) || AddressTextBox.IsFocused == false)
        {
            AutoCompletePopup.IsOpen = false;
            return;
        }

        var filtered = new List<SuggestionItem>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Check bookmarks (max 7)
        foreach (var bookmark in _bookmarks)
        {
            if (filtered.Count >= 7) break;
            if (bookmark.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || 
                bookmark.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                if (seenUrls.Add(bookmark.Url))
                    filtered.Add(new SuggestionItem { Title = bookmark.Title, Url = bookmark.Url });
            }
        }

        // 2. Check common suggestions (fill remaining slots)
        foreach (var common in CommonSuggestions)
        {
            if (filtered.Count >= 7) break;
            if ((common.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || 
                 common.Url.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
                seenUrls.Add(common.Url))
            {
                filtered.Add(common);
            }
        }

        if (filtered.Count > 0)
        {
            SuggestionsListBox.ItemsSource = filtered;
            AutoCompletePopup.IsOpen = true;
        }
        else
        {
            AutoCompletePopup.IsOpen = false;
        }
    }

    private void AddressTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (AutoCompletePopup.IsOpen)
        {
            if (e.Key == Key.Down)
            {
                int index = SuggestionsListBox.SelectedIndex;
                if (index < SuggestionsListBox.Items.Count - 1)
                {
                    SuggestionsListBox.SelectedIndex = index + 1;
                }
                else
                {
                    SuggestionsListBox.SelectedIndex = 0;
                }
                SuggestionsListBox.ScrollIntoView(SuggestionsListBox.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                int index = SuggestionsListBox.SelectedIndex;
                if (index > 0)
                {
                    SuggestionsListBox.SelectedIndex = index - 1;
                }
                else
                {
                    SuggestionsListBox.SelectedIndex = SuggestionsListBox.Items.Count - 1;
                }
                SuggestionsListBox.ScrollIntoView(SuggestionsListBox.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && SuggestionsListBox.SelectedItem != null)
            {
                AcceptSuggestion();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                AutoCompletePopup.IsOpen = false;
                e.Handled = true;
            }
        }
    }

    private void SuggestionsListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        AcceptSuggestion();
    }

    private void AcceptSuggestion()
    {
        if (SuggestionsListBox.SelectedItem is SuggestionItem selected)
        {
            AddressTextBox.Text = selected.Url;
            AutoCompletePopup.IsOpen = false;

            if (_activeTab != null)
            {
                _activeTab.Url = selected.Url;
                if (_activeTab.WebView?.CoreWebView2 != null)
                {
                    _activeTab.WebView.CoreWebView2.Navigate(selected.Url);
                }
                else
                {
                    SelectTab(_activeTab);
                }
            }

            Keyboard.ClearFocus();
        }
    }

    private void RestoreFromMaximized()
    {
        if (!_isMaximized) return;

        Width = 600;
        Height = 700;
        PositionWindow();
        MaximizeButton.Content = FindResource("MaximizeIcon");
        MaximizeButton.ToolTip = "Maximizar";
        _isMaximized = false;

        // Restore decorations
        RootBorder.CornerRadius = new CornerRadius(8);
        RootBorder.BorderThickness = new Thickness(1);
        RootBorder.SetResourceReference(System.Windows.Controls.Border.StyleProperty, "FrostedFlyoutBorder");
        MainGrid.Margin = new Thickness(12);
        WebViewBorder.CornerRadius = new CornerRadius(12);

        // Restore OS rounding
        BackdropHelper.SetWindowRounding(this, true);
    }

    private bool _isUpdatingPosition = false;

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingPosition || _isMaximized || _isFullScreen || WindowState != WindowState.Normal) return;

        _isUpdatingPosition = true;
        try
        {
            // Get current monitor bounds
            var hwnd = new WindowInteropHelper(this).Handle;
            var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
            var dpi = VisualTreeHelper.GetDpi(this);

            double screenLeft = screen.WorkingArea.Left / dpi.DpiScaleX;
            double screenTop = screen.WorkingArea.Top / dpi.DpiScaleY;
            double screenRight = screen.WorkingArea.Right / dpi.DpiScaleX;
            double screenBottom = screen.WorkingArea.Bottom / dpi.DpiScaleY;

            double snapThreshold = 15; // snap distance in WPF units

            double currentLeft = Left;
            double currentTop = Top;

            // 1. Snap to screen edges
            if (Math.Abs(currentLeft - screenLeft) < snapThreshold)
                Left = screenLeft;
            else if (Math.Abs((currentLeft + Width) - screenRight) < snapThreshold)
                Left = screenRight - Width;

            if (Math.Abs(currentTop - screenTop) < snapThreshold)
                Top = screenTop;
            else if (Math.Abs((currentTop + Height) - screenBottom) < snapThreshold)
                Top = screenBottom - Height;

            // 2. Snap to other BrowserWindow instances
            foreach (var other in Application.Current.Windows.OfType<BrowserWindow>())
            {
                if (other == this || other.Visibility != Visibility.Visible || other.WindowState != WindowState.Normal)
                    continue;

                // Snap Left to other.Right
                if (Math.Abs(Left - (other.Left + other.Width + 4)) < snapThreshold)
                {
                    Left = other.Left + other.Width + 4;
                    if (Math.Abs(Top - other.Top) < snapThreshold)
                        Top = other.Top;
                }
                // Snap Right to other.Left
                else if (Math.Abs((Left + Width) - (other.Left - 4)) < snapThreshold)
                {
                    Left = other.Left - Width - 4;
                    if (Math.Abs(Top - other.Top) < snapThreshold)
                        Top = other.Top;
                }

                // Snap Top to other.Bottom
                if (Math.Abs(Top - (other.Top + other.Height + 4)) < snapThreshold)
                {
                    Top = other.Top + other.Height + 4;
                    if (Math.Abs(Left - other.Left) < snapThreshold)
                        Left = other.Left;
                }
                // Snap Bottom to other.Top
                else if (Math.Abs((Top + Height) - (other.Top - 4)) < snapThreshold)
                {
                    Top = other.Top - Height - 4;
                    if (Math.Abs(Left - other.Left) < snapThreshold)
                        Left = other.Left;
                }
            }
        }
        catch { /* ignored */ }
        finally
        {
            _isUpdatingPosition = false;
        }

        if (WindowState == WindowState.Normal && !_isMaximized && !_isFullScreen)
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
        }
    }

    private void TileButton_Click(object sender, RoutedEventArgs e)
    {
        TileBrowserWindows();
    }

    public static void TileBrowserWindows()
    {
        try
        {
            var activeWindows = Application.Current.Windows.OfType<BrowserWindow>()
                .Where(w => w.Visibility == Visibility.Visible && w.WindowState != WindowState.Minimized)
                .ToList();

            if (activeWindows.Count == 0) return;

            // Use the screen of the primary active window as reference
            var firstWin = activeWindows[0];
            var hwnd = new WindowInteropHelper(firstWin).Handle;
            var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
            var dpi = VisualTreeHelper.GetDpi(firstWin);

            double screenLeft = screen.WorkingArea.Left / dpi.DpiScaleX;
            double screenTop = screen.WorkingArea.Top / dpi.DpiScaleY;
            double screenWidth = screen.WorkingArea.Width / dpi.DpiScaleX;
            double screenHeight = screen.WorkingArea.Height / dpi.DpiScaleY;

            int count = activeWindows.Count;

            // Calculate grid dimensions
            int cols = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling((double)count / cols);

            double winWidth = screenWidth / cols;
            double winHeight = screenHeight / rows;

            for (int i = 0; i < count; i++)
            {
                var win = activeWindows[i];
                int col = i % cols;
                int row = i / cols;

                // If maximized, restore first to make it resizable/tileable
                win.RestoreFromMaximized();

                win.Left = screenLeft + (col * winWidth);
                win.Top = screenTop + (row * winHeight);
                win.Width = winWidth;
                win.Height = winHeight;
            }
        }
        catch { /* ignored */ }
    }

    // ===== Merge Windows =====
    private void MergeWindowsButton_Click(object sender, RoutedEventArgs e)
    {
        // Collect all OTHER visible browser windows
        var otherWindows = Application.Current.Windows
            .OfType<BrowserWindow>()
            .Where(w => w != this && w.Visibility == Visibility.Visible)
            .ToList();

        if (otherWindows.Count == 0) return;

        // Gather all URLs (and titles) from every tab in every other window.
        // We transfer URLs, not WebView controls — re-parenting WebView2 HwndHosts
        // across windows is not supported and causes renderer crashes.
        var tabsToImport = new List<(string Url, string Title)>();
        foreach (var win in otherWindows)
        {
            foreach (var tab in win._tabs)
            {
                // Skip empty/blank tabs unless they're the only tab in a window
                string url = tab.Url?.Trim() ?? "about:blank";
                if (url == "about:blank" && win._tabs.Count > 1) continue;
                tabsToImport.Add((url, tab.Title));
            }
        }

        // Close all other windows first (they will dispose their WebViews cleanly)
        foreach (var win in otherWindows)
        {
            win._allowClose = true;
            win.Close();
        }

        // Now create a new tab here for each imported URL
        foreach (var (url, title) in tabsToImport)
        {
            var tab = new BrowserTab { Url = url, Title = title };
            _tabs.Add(tab);
        }

        // Rebuild the tabs dropdown to show all new tabs
        UpdateTabsDropdown();

        // Save session
        ApplicationHost.Instance?.SaveBrowserSession();
    }

    // ===== Workspace and Tab methods =====
    public string WorkspaceName
    {
        get => _workspaceName;
        set
        {
            _workspaceName = value;
            this.Title = $"{value} - {(_activeTab != null ? _activeTab.Title : "Navegador Web")}";
        }
    }

    private void GroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TabGroup group)
        {
            // Activate group visually and for popup populating
            foreach (var g in _groups)
            {
                g.IsActive = (g == group);
            }
            _activeGroup = group;

            // Open the TabsPopup positioned below this button
            TabsPopup.PlacementTarget = btn;
            TabsPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            UpdateTabsDropdown();
            TabsPopup.IsOpen = true;

            ApplicationHost.Instance?.SaveBrowserSession();
        }
    }

    private void AddGroupButton_Click(object sender, RoutedEventArgs e)
    {
        // Create new group
        int nextGroupNum = _groups.Count + 1;
        var newGroup = new TabGroup { Name = $"Grupo {nextGroupNum}", IsActive = false };
        _groups.Add(newGroup);

        // Select the new group visually
        foreach (var g in _groups)
        {
            g.IsActive = (g == newGroup);
        }
        _activeGroup = newGroup;

        // Add a default blank tab inside it
        CreateNewTab("about:blank");

        // Position tabs popup below the new group button
        var btn = FindVisualChildForGroup(newGroup);
        if (btn != null)
        {
            TabsPopup.PlacementTarget = btn;
            TabsPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            UpdateTabsDropdown();
            TabsPopup.IsOpen = true;
        }

        ApplicationHost.Instance?.SaveBrowserSession();
    }

    private Button? FindVisualChildForGroup(TabGroup group)
    {
        try
        {
            for (int i = 0; i < GroupsItemsControl.Items.Count; i++)
            {
                var container = GroupsItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                if (container != null)
                {
                    if (VisualTreeHelper.GetChildrenCount(container) > 0)
                    {
                        var btn = VisualTreeHelper.GetChild(container, 0) as Button;
                        if (btn != null && btn.DataContext == group)
                        {
                            return btn;
                        }
                    }
                }
            }
        }
        catch { /* ignored */ }
        return null;
    }



    private void RenameGroupMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is TabGroup group)
        {
            group.IsEditing = true;

            // Find the TextBox and focus it
            var btn = FindVisualChildForGroup(group);
            if (btn != null)
            {
                // Give WPF layout a millisecond to switch visibility via trigger before finding and focusing the TextBox
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    var textBox = FindVisualChild<TextBox>(btn);
                    if (textBox != null)
                    {
                        textBox.Focus();
                        textBox.SelectAll();
                    }
                }));
            }
        }
    }

    private void CloseGroupMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is TabGroup group)
        {
            CloseGroup(group);
        }
    }

    private void CloseGroup(TabGroup group)
    {
        if (group == null) return;

        // Dispose all tab webviews in this group
        foreach (var tab in group.Tabs)
        {
            if (tab.WebView != null)
            {
                tab.WebView.Detach();
                tab.WebView.Dispose();
                tab.WebView = null;
            }
        }

        int index = _groups.IndexOf(group);
        _groups.Remove(group);

        if (_groups.Count == 0)
        {
            this.Close();
            return;
        }

        if (group == _activeGroup)
        {
            int nextIndex = Math.Min(index, _groups.Count - 1);
            var nextGroup = _groups[nextIndex];
            _activeGroup = nextGroup;
            _activeGroup.IsActive = true;

            if (_tabs.Count > 0)
            {
                int selIndex = Math.Clamp(_activeGroup.ActiveTab != null ? _activeGroup.Tabs.IndexOf(_activeGroup.ActiveTab) : 0, 0, _tabs.Count - 1);
                SelectTab(_tabs[selIndex]);
            }
            else
            {
                CreateNewTab("about:blank");
            }
        }

        ApplicationHost.Instance?.SaveBrowserSession();
    }

    // ===== Drag and Drop of Groups (Horizontal Sliding) =====
    private Point _dragStartMousePos;
    private Button? _draggedButton;
    private TranslateTransform? _dragTranslate;
    private double _draggedButtonWidth;
    private bool _isDraggingActive = false;
    private TabGroup? _draggedGroup;

    private void GroupButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBox || (e.OriginalSource is DependencyObject d && FindVisualParent<TextBox>(d) != null))
        {
            return;
        }

        if (sender is Button btn && btn.DataContext is TabGroup group)
        {
            _draggedButton = btn;
            _draggedGroup = group;
            _dragStartMousePos = e.GetPosition(GroupsItemsControl);
            _draggedButtonWidth = btn.ActualWidth;
            _isDraggingActive = false;
            _dragTranslate = new TranslateTransform();
        }
    }

    private void GroupButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedButton != null && _draggedGroup != null && e.LeftButton == MouseButtonState.Pressed)
        {
            Point currentMousePos = e.GetPosition(GroupsItemsControl);
            double deltaX = currentMousePos.X - _dragStartMousePos.X;

            if (!_isDraggingActive)
            {
                if (Math.Abs(deltaX) > SystemParameters.MinimumHorizontalDragDistance)
                {
                    _isDraggingActive = true;
                    _draggedButton.CaptureMouse();
                    _draggedButton.RenderTransform = _dragTranslate;
                    Panel.SetZIndex(_draggedButton, 999);
                }
            }

            if (_isDraggingActive && _dragTranslate != null)
            {
                // Check if mouse left the group bar vertically to start cross-window drag
                var posInWindow = e.GetPosition(this);
                if (posInWindow.Y < 0 || posInWindow.Y > TitleRow.ActualHeight + 10)
                {
                    _draggedButton.ReleaseMouseCapture();
                    _draggedButton.RenderTransform = null;
                    Panel.SetZIndex(_draggedButton, 0);
                    
                    _isDraggingActive = false;
                    
                    DataObject dragData = new DataObject("TabGroupData", _draggedGroup);
                    DragDrop.DoDragDrop(_draggedButton, dragData, DragDropEffects.Move);
                    
                    _draggedButton = null;
                    _draggedGroup = null;
                    _dragTranslate = null;
                    return;
                }

                _dragTranslate.X = deltaX;

                int currentIndex = _groups.IndexOf(_draggedGroup);
                if (currentIndex >= 0)
                {
                    if (deltaX > 0 && currentIndex < _groups.Count - 1)
                    {
                        var nextGroup = _groups[currentIndex + 1];
                        var nextButton = FindVisualChildForGroup(nextGroup);
                        if (nextButton != null)
                        {
                            double swapThreshold = nextButton.ActualWidth / 2;
                            if (deltaX > swapThreshold)
                            {
                                _groups.Move(currentIndex, currentIndex + 1);
                                _dragStartMousePos.X += nextButton.ActualWidth + 6;
                                _dragTranslate.X = currentMousePos.X - _dragStartMousePos.X;
                                ApplicationHost.Instance?.SaveBrowserSession();
                            }
                        }
                    }
                    else if (deltaX < 0 && currentIndex > 0)
                    {
                        var prevGroup = _groups[currentIndex - 1];
                        var prevButton = FindVisualChildForGroup(prevGroup);
                        if (prevButton != null)
                        {
                            double swapThreshold = -prevButton.ActualWidth / 2;
                            if (deltaX < swapThreshold)
                            {
                                _groups.Move(currentIndex, currentIndex - 1);
                                _dragStartMousePos.X -= (prevButton.ActualWidth + 6);
                                _dragTranslate.X = currentMousePos.X - _dragStartMousePos.X;
                                ApplicationHost.Instance?.SaveBrowserSession();
                            }
                        }
                    }
                }
            }
        }
    }

    private void GroupButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingActive)
        {
            ReleaseGroupDrag();
            e.Handled = true; // Suppress Click event
        }
        else
        {
            ReleaseGroupDrag();
        }
    }

    private void ReleaseGroupDrag()
    {
        if (_draggedButton != null)
        {
            if (_isDraggingActive)
            {
                _draggedButton.ReleaseMouseCapture();
                _draggedButton.RenderTransform = null;
                Panel.SetZIndex(_draggedButton, 0);
            }
            
            _draggedButton = null;
            _draggedGroup = null;
            _dragTranslate = null;
            _isDraggingActive = false;
        }
    }

    private void GroupButton_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("TabGroupData"))
        {
            var sourceGroup = e.Data.GetData("TabGroupData") as TabGroup;
            if (sender is Button btn && btn.DataContext is TabGroup targetGroup)
            {
                if (sourceGroup != null && sourceGroup != targetGroup)
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }
            }
        }
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void GroupButton_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("TabGroupData"))
        {
            var sourceGroup = e.Data.GetData("TabGroupData") as TabGroup;
            if (sender is Button btn && btn.DataContext is TabGroup targetGroup)
            {
                if (sourceGroup != null && sourceGroup != targetGroup)
                {
                    if (_groups.Contains(sourceGroup))
                    {
                        int oldIndex = _groups.IndexOf(sourceGroup);
                        int newIndex = _groups.IndexOf(targetGroup);
                        if (oldIndex >= 0 && newIndex >= 0)
                        {
                            _groups.Move(oldIndex, newIndex);
                            ApplicationHost.Instance?.SaveBrowserSession();
                        }
                    }
                    else
                    {
                        MoveGroupBetweenWindows(sourceGroup);
                    }
                }
            }
        }
        e.Handled = true;
    }

    private void GroupsItemsControl_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("TabGroupData"))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void GroupsItemsControl_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("TabGroupData"))
        {
            var sourceGroup = e.Data.GetData("TabGroupData") as TabGroup;
            if (sourceGroup != null)
            {
                if (_groups.Contains(sourceGroup))
                {
                    int oldIndex = _groups.IndexOf(sourceGroup);
                    if (oldIndex >= 0 && oldIndex != _groups.Count - 1)
                    {
                        _groups.Move(oldIndex, _groups.Count - 1);
                        ApplicationHost.Instance?.SaveBrowserSession();
                    }
                }
                else
                {
                    MoveGroupBetweenWindows(sourceGroup);
                }
            }
        }
        e.Handled = true;
    }

    private void MoveGroupBetweenWindows(TabGroup sourceGroup)
    {
        var sourceWindow = Application.Current.Windows
            .OfType<BrowserWindow>()
            .FirstOrDefault(w => w._groups.Contains(sourceGroup));
            
        if (sourceWindow != null)
        {
            var newGroup = new TabGroup { Name = sourceGroup.Name, IsActive = false };
            _groups.Add(newGroup);
            
            foreach (var sourceTab in sourceGroup.Tabs)
            {
                newGroup.Tabs.Add(new BrowserTab { Url = sourceTab.Url, Title = sourceTab.Title, HasCustomTitle = sourceTab.HasCustomTitle });
            }
            
            if (newGroup.Tabs.Count == 0)
            {
                newGroup.Tabs.Add(new BrowserTab { Url = "about:blank", Title = "Nueva pestaña" });
            }
            
            foreach (var g in _groups)
            {
                g.IsActive = (g == newGroup);
            }
            _activeGroup = newGroup;
            if (_activeGroup.Tabs.Count > 0)
            {
                SelectTab(_activeGroup.Tabs[0]);
            }
            
            sourceWindow.RemoveGroupInternal(sourceGroup);
            
            UpdateTabsDropdown();
            ApplicationHost.Instance?.SaveBrowserSession();
        }
    }

    public void RemoveGroupInternal(TabGroup group)
    {
        _groups.Remove(group);
        if (_groups.Count == 0)
        {
            var defaultGroup = new TabGroup { Name = "Principal", IsActive = true };
            _groups.Add(defaultGroup);
            _activeGroup = defaultGroup;
            CreateNewTab("about:blank");
        }
        else if (_activeGroup == group)
        {
            _activeGroup = _groups[0];
            _activeGroup.IsActive = true;
            if (_tabs.Count > 0)
            {
                SelectTab(_tabs[0]);
            }
        }
        UpdateTabsDropdown();
        ApplicationHost.Instance?.SaveBrowserSession();
    }

    // ===== Separate Groups into New Window =====
    private void MoveGroupToNewWindowMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is TabGroup group)
            {
                if (_groups.Count == 1 && group.Tabs.Count <= 1 && (group.Tabs.Count == 0 || group.Tabs[0].Url == "about:blank"))
                {
                    return;
                }
                
                var newWin = new BrowserWindow();
                var newGroup = new TabGroup { Name = group.Name, IsActive = true };
                foreach (var tab in group.Tabs)
                {
                    newGroup.Tabs.Add(new BrowserTab { Url = tab.Url, Title = tab.Title, HasCustomTitle = tab.HasCustomTitle });
                }
                newWin._groups.Add(newGroup);
                newWin._activeGroup = newGroup;
                
                RemoveGroupInternal(group);
                
                if (ApplicationHost.Instance != null)
                {
                    double offset = 25 * (ApplicationHost.Instance.GetBrowserWindowsCount() % 6);
                    newWin.CascadeOffset = offset;
                    ApplicationHost.Instance.AddBrowserWindow(newWin);
                }
                
                newWin.Show();
                if (newGroup.Tabs.Count > 0)
                {
                    newWin.SelectTab(newGroup.Tabs[0]);
                }
                
                ApplicationHost.Instance?.SaveBrowserSession();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al mover grupo a nueva ventana: {ex.Message}");
        }
    }

    // ===== Dynamic Window Selection and Merging =====
    private void MergeWindowSubMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mergeMenuItem)
        {
            var group = mergeMenuItem.DataContext as TabGroup;
            if (group == null) return;
            
            mergeMenuItem.Items.Clear();
            
            var otherWindows = Application.Current.Windows
                .OfType<BrowserWindow>()
                .Where(w => w != this && w.Visibility == Visibility.Visible)
                .ToList();
                
            if (otherWindows.Count == 0)
            {
                var noWinItem = new MenuItem { Header = "(No hay otras ventanas)", IsEnabled = false };
                mergeMenuItem.Items.Add(noWinItem);
                return;
            }
            
            foreach (var win in otherWindows)
            {
                string title = string.IsNullOrEmpty(win.Title) ? "Navegador" : win.Title;
                var winItem = new MenuItem { Header = title, Tag = win };
                winItem.Click += (s, ev) =>
                {
                    if (winItem.Tag is BrowserWindow sourceWin)
                    {
                        MergeWindowIntoGroup(sourceWin, group);
                    }
                };
                mergeMenuItem.Items.Add(winItem);
            }
        }
    }

    private void MergeWindowIntoGroup(BrowserWindow sourceWin, TabGroup targetGroup)
    {
        try
        {
            var tabsToImport = new List<(string Url, string Title, bool HasCustom)>();
            foreach (var group in sourceWin._groups)
            {
                foreach (var tab in group.Tabs)
                {
                    string url = tab.Url?.Trim() ?? "about:blank";
                    if (url == "about:blank" && group.Tabs.Count > 1) continue;
                    tabsToImport.Add((url, tab.Title, tab.HasCustomTitle));
                }
            }

            sourceWin._allowClose = true;
            sourceWin.Close();

            foreach (var (url, title, hasCustom) in tabsToImport)
            {
                targetGroup.Tabs.Add(new BrowserTab { Url = url, Title = title, HasCustomTitle = hasCustom });
            }

            if (_activeGroup == targetGroup)
            {
                UpdateTabsDropdown();
            }

            ApplicationHost.Instance?.SaveBrowserSession();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al unir ventana al grupo: {ex.Message}");
        }
    }

    // ===== Separate Tab into New Window =====
    private void MoveTabToNewWindow(BrowserTab tab)
    {
        try
        {
            var group = _groups.FirstOrDefault(g => g.Tabs.Contains(tab));
            if (group == null) return;

            var newWin = new BrowserWindow();
            var newGroup = new TabGroup { Name = group.Name, IsActive = true };
            newGroup.Tabs.Add(new BrowserTab { Url = tab.Url, Title = tab.Title, HasCustomTitle = tab.HasCustomTitle });
            newWin._groups.Add(newGroup);
            newWin._activeGroup = newGroup;
            
            group.Tabs.Remove(tab);
            if (tab.WebView != null)
            {
                tab.WebView.Detach();
                try { tab.WebView.Dispose(); } catch { /* ignored */ }
            }

            if (group.Tabs.Count == 0)
            {
                if (_groups.Count > 1)
                {
                    _groups.Remove(group);
                    if (_activeGroup == group)
                    {
                        _activeGroup = _groups[0];
                        _activeGroup.IsActive = true;
                    }
                }
                else
                {
                    _allowClose = true;
                    this.Close();
                }
            }

            if (_activeTab == tab && this.Visibility == Visibility.Visible)
            {
                if (_tabs.Count > 0)
                {
                    SelectTab(_tabs[0]);
                }
                else if (_groups.Count > 0 && _groups[0].Tabs.Count > 0)
                {
                    SelectTab(_groups[0].Tabs[0]);
                }
            }

            if (ApplicationHost.Instance != null)
            {
                double offset = 25 * (ApplicationHost.Instance.GetBrowserWindowsCount() % 6);
                newWin.CascadeOffset = offset;
                ApplicationHost.Instance.AddBrowserWindow(newWin);
            }

            newWin.Show();
            if (newGroup.Tabs.Count > 0)
            {
                newWin.SelectTab(newGroup.Tabs[0]);
            }

            UpdateTabsDropdown();
            ApplicationHost.Instance?.SaveBrowserSession();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al mover pestaña a nueva ventana: {ex.Message}");
        }
    }

    private void MoveTabToGroup(BrowserTab tab, TabGroup targetGroup)
    {
        try
        {
            var sourceGroup = _groups.FirstOrDefault(g => g.Tabs.Contains(tab));
            if (sourceGroup == null || sourceGroup == targetGroup) return;

            sourceGroup.Tabs.Remove(tab);
            targetGroup.Tabs.Add(tab);

            if (targetGroup != _activeGroup && tab.WebView != null)
            {
                tab.WebView.Detach();
                try { tab.WebView.CoreWebView2?.TrySuspendAsync(); } catch { /* ignored */ }
            }

            if (_activeTab == tab)
            {
                if (sourceGroup.Tabs.Count > 0)
                {
                    SelectTab(sourceGroup.Tabs[0]);
                }
                else
                {
                    var newBlank = new BrowserTab { Url = "about:blank", Title = "Nueva pestaña" };
                    sourceGroup.Tabs.Add(newBlank);
                    SelectTab(newBlank);
                }
            }
            else
            {
                UpdateTabsDropdown();
            }

            ApplicationHost.Instance?.SaveBrowserSession();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al mover pestaña a grupo: {ex.Message}");
        }
    }

    private void GroupNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is TabGroup group)
        {
            group.IsEditing = false;
            string name = textBox.Text.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                group.Name = name;
            }
            else
            {
                textBox.Text = group.Name;
            }
            ApplicationHost.Instance?.SaveBrowserSession();
        }
    }

    private void GroupNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            if (e.Key == Key.Enter)
            {
                if (textBox.DataContext is TabGroup group)
                {
                    group.IsEditing = false;
                    string name = textBox.Text.Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        group.Name = name;
                    }
                    else
                    {
                        textBox.Text = group.Name;
                    }
                    ApplicationHost.Instance?.SaveBrowserSession();
                }
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (textBox.DataContext is TabGroup group)
                {
                    group.IsEditing = false;
                    textBox.Text = group.Name; // Revert to original name
                }
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }
    }

    private void GroupNameTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Prevent click from bubbling up to the Button and raising Click event
        e.Handled = true;
    }

    private void BrowserWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // If we are editing a group, and the user clicked outside the textbox, cancel/save editing
        var editingGroup = _groups.FirstOrDefault(g => g.IsEditing);
        if (editingGroup != null)
        {
            var btn = FindVisualChildForGroup(editingGroup);
            if (btn != null)
            {
                var textBox = FindVisualChild<TextBox>(btn);
                if (textBox != null && !textBox.IsMouseOver)
                {
                    editingGroup.IsEditing = false;
                    string name = textBox.Text.Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        editingGroup.Name = name;
                    }
                    else
                    {
                        textBox.Text = editingGroup.Name;
                    }
                    ApplicationHost.Instance?.SaveBrowserSession();
                }
            }
        }
    }

    private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T target)
            {
                return target;
            }
            var childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null)
            {
                return childOfChild;
            }
        }
        return null;
    }

    private T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;
        if (parentObject is T parent) return parent;
        return FindVisualParent<T>(parentObject);
    }

    public void CreateNewTab(string url)
    {
        var tab = new BrowserTab
        {
            Url = url,
            Title = "Nueva pestaña"
        };
        _tabs.Add(tab);
        SelectTab(tab);
    }

    private void CloseTab(BrowserTab tab)
    {
        try
        {
            if (tab.WebView != null)
            {
                tab.WebView.Detach();
                tab.WebView.Dispose();
                tab.WebView = null;
            }

            int index = _tabs.IndexOf(tab);
            _tabs.Remove(tab);

            if (_tabs.Count == 0)
            {
                if (_groups.Count > 1)
                {
                    var oldGroup = _activeGroup;
                    if (oldGroup != null)
                    {
                        _groups.Remove(oldGroup);
                    }
                    _activeGroup = _groups[0];
                    _activeGroup.IsActive = true;
                    
                    if (_tabs.Count > 0)
                    {
                        int selIndex = Math.Clamp(_activeGroup.ActiveTab != null ? _activeGroup.Tabs.IndexOf(_activeGroup.ActiveTab) : 0, 0, _tabs.Count - 1);
                        SelectTab(_tabs[selIndex]);
                    }
                    else
                    {
                        CreateNewTab("about:blank");
                    }
                    
                    ApplicationHost.Instance?.SaveBrowserSession();
                    return;
                }
                else
                {
                    this.Close();
                    return;
                }
            }

            if (tab == _activeTab)
            {
                int nextIndex = Math.Min(index, _tabs.Count - 1);
                SelectTab(_tabs[nextIndex]);
            }
            else
            {
                UpdateTabsDropdown();
            }

            ApplicationHost.Instance?.SaveBrowserSession();
        }
        catch { /* ignored */ }
    }

    private async void SelectTab(BrowserTab tab)
    {
        // Find which group contains this tab and activate it if needed
        var groupWithTab = _groups.FirstOrDefault(g => g.Tabs.Contains(tab));
        if (groupWithTab != null && groupWithTab != _activeGroup)
        {
            // Suspend old tab
            if (_activeTab != null && _activeTab.WebView != null)
            {
                _activeTab.WebView.Detach();
                try { _activeTab.WebView.CoreWebView2?.TrySuspendAsync(); } catch { /* ignored */ }
            }

            foreach (var g in _groups)
            {
                g.IsActive = (g == groupWithTab);
            }
            _activeGroup = groupWithTab;
        }

        if (tab == _activeTab && tab.WebView != null) return;

        var oldTab = _activeTab;
        _activeTab = tab;

        // Hide and suspend the old tab's webView (unless it is playing audio/video in background)
        if (oldTab != null && oldTab.WebView != null)
        {
            oldTab.WebView.Detach();
            try
            {
                bool isPlayingAudio = false;
                if (oldTab.WebView.CoreWebView2 != null)
                {
                    isPlayingAudio = oldTab.WebView.CoreWebView2.IsDocumentPlayingAudio;
                }

                if (!isPlayingAudio && oldTab.WebView.CoreWebView2 != null)
                {
                    await oldTab.WebView.CoreWebView2.TrySuspendAsync();
                }
            }
            catch { /* ignored */ }
        }

        // Show/Load the new tab
        if (tab.WebView == null)
        {
            var compositionTab = new CompositionWebViewTab();

            try
            {
                await compositionTab.InitializeAsync(_webViewEnv!, WebViewHost.Handle);
                compositionTab.Attach(WebViewHost.HostVisual!);
                var dpi = VisualTreeHelper.GetDpi(WebViewHost);
                compositionTab.Resize(WebViewHost.ActualWidth * dpi.DpiScaleX, WebViewHost.ActualHeight * dpi.DpiScaleY);
                tab.WebView = compositionTab;
                SetupTabWebView(tab);
                compositionTab.CoreWebView2?.Navigate(tab.Url);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar pestaña: {ex.Message}");
            }
        }
        else
        {
            tab.WebView.Attach(WebViewHost.HostVisual!);
            var dpi = VisualTreeHelper.GetDpi(WebViewHost);
            tab.WebView.Resize(WebViewHost.ActualWidth * dpi.DpiScaleX, WebViewHost.ActualHeight * dpi.DpiScaleY);
            try
            {
                tab.WebView.CoreWebView2.Resume();
            }
            catch { /* ignored */ }

            AddressTextBox.Text = tab.Url;
            UpdateStarButtonState();
            LoadFaviconAsync(tab);
        }

        UpdateTabVisibilityState();
        if (_activeGroup != null)
        {
            _activeGroup.ActiveTab = tab;
        }
        this.Title = $"{_workspaceName} - {tab.Title}";
        UpdateTabsDropdown();
        UpdateNavigationButtonStates();
    }

    private void SetupTabWebView(BrowserTab tab)
    {
        var web = tab.WebView;
        if (web == null || web.CoreWebView2 == null) return;

        var settings = web.CoreWebView2.Settings;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsReputationCheckingRequired = true;

        web.CoreWebView2.WindowCloseRequested += (s, args) => DispatchHelper.RunOnUI(() => CloseTab(tab));

        web.CoreWebView2.AddWebResourceRequestedFilter("*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
        web.CoreWebView2.WebResourceRequested += (s, e) => DispatchHelper.RunOnUI(() => CoreWebView2_WebResourceRequested(s, e));

        string adblockScript = @"
            (function() {
                try {
                    window.chrome.webview.addEventListener('message', function(e) {
                        if (e.data && e.data.type === 'inject_css') {
                            const style = document.createElement('style');
                            style.textContent = e.data.css;
                            if (document.head) {
                                document.head.appendChild(style);
                            } else {
                                const observer = new MutationObserver(function() {
                                    if (document.head) {
                                        document.head.appendChild(style);
                                        observer.disconnect();
                                    }
                                });
                                observer.observe(document.documentElement, { childList: true });
                            }
                        }
                    });
                    window.chrome.webview.postMessage({ type: 'page_ready', host: window.location.hostname });
                } catch(err) {}
            })();";

        _ = web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(adblockScript);
        web.CoreWebView2.WebMessageReceived += (s, e) => DispatchHelper.RunOnUI(() => CoreWebView2_WebMessageReceived(s, e));

        web.CoreWebView2.HistoryChanged += (s, e) => DispatchHelper.RunOnUI(() => CoreWebView2_HistoryChanged(s, e));
        web.CoreWebView2.SourceChanged += (s, e) => DispatchHelper.RunOnUI(() =>
        {
            if (tab == _activeTab)
            {
                AddressTextBox.Text = web.Source?.ToString() ?? "";
                ApplicationHost.Instance?.SaveBrowserSession();
                UpdateStarButtonState();
                LoadFaviconAsync(tab);
                UpdateTabVisibilityState();
                UpdateSecurityIndicator(web.Source);
            }
            tab.Url = web.Source?.ToString() ?? tab.Url;
        });

        web.CoreWebView2.NewWindowRequested += (s, e) => DispatchHelper.RunOnUI(() => CoreWebView2_NewWindowRequested(s, e));
        web.CoreWebView2.NavigationStarting += (s, e) => DispatchHelper.RunOnUI(() => CoreWebView2_NavigationStarting(s, e));
        web.CoreWebView2.NavigationCompleted += (s, e) => DispatchHelper.RunOnUI(() => CoreWebView2_NavigationCompleted(s, e));
        web.CoreWebView2.FaviconChanged += (s, e) => DispatchHelper.RunOnUI(() => CoreWebView2_FaviconChanged(s, e));
        web.CoreWebView2.ContainsFullScreenElementChanged += (s, e) => DispatchHelper.RunOnUI(() => CoreWebView2_ContainsFullScreenElementChanged(s, e));
        web.CoreWebView2.PermissionRequested += (s, e) => DispatchHelper.RunOnUI(() => CoreWebView2_PermissionRequested(s, e));

        web.CoreWebView2.DocumentTitleChanged += (s, args) => DispatchHelper.RunOnUI(() =>
        {
            if (!tab.HasCustomTitle)
            {
                tab.Title = web.CoreWebView2.DocumentTitle;
                if (string.IsNullOrEmpty(tab.Title)) tab.Title = "Nueva pestaña";
            }

            if (tab == _activeTab)
            {
                this.Title = $"{_workspaceName} - {tab.Title}";
            }
            UpdateTabsDropdown();
            ApplicationHost.Instance?.SaveBrowserSession();
        });
    }

    private void UpdateTabsDropdown()
    {
        // Populate the Popup panel
        TabsPopupPanel.Children.Clear();
        foreach (var tab in _tabs)
        {
            bool isActive = (tab == _activeTab);

            var itemGrid = new Grid
            {
                Margin = new Thickness(0, 2, 0, 2),
                Height = 36,
                Cursor = Cursors.Hand
            };
            
            // Hover effect border
            var backgroundBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = isActive 
                    ? new SolidColorBrush(Color.FromArgb(0x20, 255, 255, 255)) 
                    : Brushes.Transparent,
                BorderBrush = isActive
                    ? new SolidColorBrush(Color.FromArgb(0x18, 255, 255, 255))
                    : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                SnapsToDevicePixels = true
            };
            itemGrid.Children.Add(backgroundBorder);

            // Inner content layout
            var contentGrid = new Grid { Margin = new Thickness(8, 0, 8, 0) };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Column 0: Left Accent Line
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Column 1: Favicon
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Column 2: Title
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Column 3: Close Button

            // Hover handlers for itemGrid
            itemGrid.MouseEnter += (s, e) =>
            {
                if (!isActive)
                {
                    backgroundBorder.Background = new SolidColorBrush(Color.FromArgb(0x12, 255, 255, 255));
                }
            };
            itemGrid.MouseLeave += (s, e) =>
            {
                if (!isActive)
                {
                    backgroundBorder.Background = Brushes.Transparent;
                }
            };

            // Click to select tab (Left click)
            itemGrid.MouseDown += (s, e) =>
            {
                // If clicked inside a TextBox (like renaming), don't select the tab
                if (e.OriginalSource is TextBox || (e.OriginalSource is DependencyObject d && FindVisualParent<TextBox>(d) != null))
                {
                    return;
                }

                if (e.ChangedButton == MouseButton.Left)
                {
                    SelectTab(tab);
                    TabsPopup.IsOpen = false;
                }
            };

            // Left accent vertical line for active tab
            var accentLine = new Border
            {
                Width = 3,
                Height = 16,
                CornerRadius = new CornerRadius(1.5),
                Background = isActive 
                    ? (Brush)FindResource("AccentBrush") 
                    : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(accentLine, 0);
            contentGrid.Children.Add(accentLine);

            // Favicon
            if (tab.Favicon != null)
            {
                var faviconImage = new Image
                {
                    Source = tab.Favicon,
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(faviconImage, 1);
                contentGrid.Children.Add(faviconImage);
            }
            else
            {
                string domain = GetDomainName(tab.Url);
                if (!string.IsNullOrEmpty(domain) && tab.Url != "about:blank")
                {
                    var faviconImage = CreateFaviconImage(domain, 14);
                    Grid.SetColumn(faviconImage, 1);
                    contentGrid.Children.Add(faviconImage);
                }
                else
                {
                    var fallbackGlobe = new TextBlock
                    {
                        Text = "\uEA07",  // language / globe (Material Symbols Outlined)
                        FontFamily = new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Material Symbols Outlined"),
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)),
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(fallbackGlobe, 1);
                    contentGrid.Children.Add(fallbackGlobe);
                }
            }
            // Context menu to rename the tab
            var tabMenu = new ContextMenu();
            var renameTabItem = new MenuItem { Header = "Renombrar pestaña" };
            renameTabItem.Click += (s, ev) =>
            {
                tab.IsEditing = true;
                TabsPopup.StaysOpen = true; // Keep popup open during edit
                UpdateTabsDropdown();
            };
            tabMenu.Items.Add(renameTabItem);

            var separateTabItem = new MenuItem { Header = "Mover a nueva ventana" };
            separateTabItem.Click += (s, ev) =>
            {
                MoveTabToNewWindow(tab);
            };
            tabMenu.Items.Add(separateTabItem);

            var currentGroup = _groups.FirstOrDefault(g => g.Tabs.Contains(tab));
            bool addedSeparator = false;
            foreach (var g in _groups)
            {
                if (g == currentGroup) continue;
                if (!addedSeparator)
                {
                    tabMenu.Items.Add(new Separator());
                    addedSeparator = true;
                }
                var gItem = new MenuItem { Header = $"Mover al grupo: {g.Name}", Tag = g };
                gItem.Click += (s2, ev2) =>
                {
                    MoveTabToGroup(tab, g);
                };
                tabMenu.Items.Add(gItem);
            }

            tabMenu.Opened += (s, e) =>
            {
                TabsPopup.StaysOpen = true;
            };
            tabMenu.Closed += (s, e) =>
            {
                TabsPopup.StaysOpen = tab.IsEditing;
            };
            itemGrid.ContextMenu = tabMenu;

            // Title text or TextBox
            if (tab.IsEditing)
            {
                var renameTextBox = new TextBox
                {
                    Text = tab.Title,
                    FontFamily = (FontFamily)FindResource("AppFontFamily"),
                    FontSize = 11.5,
                    Foreground = Brushes.White,
                    CaretBrush = Brushes.White,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };

                renameTextBox.LostFocus += (s, ev) =>
                {
                    if (!tab.IsEditing) return; // Re-entry guard!

                    tab.IsEditing = false;
                    string newTitle = renameTextBox.Text.Trim();
                    if (!string.IsNullOrEmpty(newTitle))
                    {
                        tab.Title = newTitle;
                        tab.HasCustomTitle = true;
                    }
                    else
                    {
                        tab.HasCustomTitle = false;
                        if (tab.WebView?.CoreWebView2 != null && !string.IsNullOrEmpty(tab.WebView.CoreWebView2.DocumentTitle))
                        {
                            tab.Title = tab.WebView.CoreWebView2.DocumentTitle;
                        }
                        else
                        {
                            tab.Title = "Nueva pestaña";
                        }
                    }

                    // Restore StaysOpen when editing is finished
                    TabsPopup.StaysOpen = false;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateTabsDropdown();
                        ApplicationHost.Instance?.SaveBrowserSession();
                    }), System.Windows.Threading.DispatcherPriority.Input);
                };

                renameTextBox.KeyDown += (s, ev) =>
                {
                    if (ev.Key == Key.Enter)
                    {
                        Keyboard.ClearFocus();
                        ev.Handled = true;
                    }
                    else if (ev.Key == Key.Escape)
                    {
                        tab.IsEditing = false;
                        TabsPopup.StaysOpen = false; // Restore
                        UpdateTabsDropdown();
                        ev.Handled = true;
                    }
                };

                renameTextBox.Loaded += (s, ev) =>
                {
                    renameTextBox.Focus();
                    renameTextBox.SelectAll();
                };

                Grid.SetColumn(renameTextBox, 2);
                contentGrid.Children.Add(renameTextBox);
            }
            else
            {
                var titleText = new TextBlock
                {
                    Text = tab.Title,
                    Foreground = isActive ? Brushes.White : new SolidColorBrush(Color.FromArgb(0xC0, 255, 255, 255)),
                    FontSize = 11.5,
                    FontFamily = (FontFamily)FindResource("AppFontFamily"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                Grid.SetColumn(titleText, 2);
                contentGrid.Children.Add(titleText);
            }

            // Close button
            var closeBtnBorder = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            var closeText = new TextBlock
            {
                Text = "×",
                Foreground = new SolidColorBrush(Color.FromArgb(0x60, 255, 255, 255)),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 1.5)
            };
            closeBtnBorder.Child = closeText;

            // Hover handlers for close button
            closeBtnBorder.MouseEnter += (s, e) =>
            {
                closeBtnBorder.Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 0, 0));
                closeText.Foreground = Brushes.Red;
            };
            closeBtnBorder.MouseLeave += (s, e) =>
            {
                closeBtnBorder.Background = Brushes.Transparent;
                closeText.Foreground = new SolidColorBrush(Color.FromArgb(0x60, 255, 255, 255));
            };

            closeBtnBorder.MouseDown += (s, e) =>
            {
                e.Handled = true;
                CloseTab(tab);
                
                // If the window is still open, keep popup open and refresh dropdown items
                if (_tabs.Count > 0)
                {
                    UpdateTabsDropdown();
                }
            };

            Grid.SetColumn(closeBtnBorder, 3);
            contentGrid.Children.Add(closeBtnBorder);

            itemGrid.Children.Add(contentGrid);
            TabsPopupPanel.Children.Add(itemGrid);
        }

        // Add [+ Nueva pestaña] item at the bottom of the popup
        var addGrid = new Grid
        {
            Margin = new Thickness(0, 4, 0, 2),
            Height = 32,
            Cursor = Cursors.Hand
        };
        
        var addBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(0x08, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x10, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true
        };
        addGrid.Children.Add(addBorder);
        
        var addContent = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var plusIcon = new TextBlock
        {
            Text = "+",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255)),
            Margin = new Thickness(0, 0, 6, 0)
        };
        var addText = new TextBlock
        {
            Text = "Nueva pestaña",
            FontSize = 11,
            FontFamily = (FontFamily)FindResource("AppFontFamily"),
            Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 255, 255, 255))
        };
        addContent.Children.Add(plusIcon);
        addContent.Children.Add(addText);
        addGrid.Children.Add(addContent);
        
        addGrid.MouseEnter += (s, e) => { addBorder.Background = new SolidColorBrush(Color.FromArgb(0x18, 255, 255, 255)); };
        addGrid.MouseLeave += (s, e) => { addBorder.Background = new SolidColorBrush(Color.FromArgb(0x08, 255, 255, 255)); };
        
        addGrid.MouseDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                CreateNewTab("about:blank");
                UpdateTabsDropdown();
                TabsPopup.IsOpen = false;
            }
        };
        
        TabsPopupPanel.Children.Add(addGrid);
    }

    public List<string> GetTabUrls()
    {
        return _tabs.Select(t => t.Url).ToList();
    }

    public List<string> GetTabTitles()
    {
        return _tabs.Select(t => t.Title).ToList();
    }

    public int GetSelectedTabIndex()
    {
        return _activeTab != null ? _tabs.IndexOf(_activeTab) : 0;
    }

    public void RestoreWorkspace(WorkspaceSessionData wsData)
    {
        _workspaceName = wsData.Name;
        _groups.Clear();

        if (wsData.Groups != null && wsData.Groups.Count > 0)
        {
            foreach (var gData in wsData.Groups)
            {
                var group = new TabGroup { Name = gData.Name, IsActive = gData.IsActive };
                for (int i = 0; i < gData.TabUrls.Count; i++)
                {
                    string url = gData.TabUrls[i];
                    string title = (gData.TabTitles != null && i < gData.TabTitles.Count) ? gData.TabTitles[i] : "Cargando...";
                    bool hasCustom = (gData.TabHasCustomTitles != null && i < gData.TabHasCustomTitles.Count) ? gData.TabHasCustomTitles[i] : false;
                    group.Tabs.Add(new BrowserTab { Url = url, Title = title, HasCustomTitle = hasCustom });
                }
                _groups.Add(group);
            }

            int selGroupIndex = Math.Clamp(wsData.SelectedGroupIndex, 0, _groups.Count - 1);
            if (_groups.Count > 0)
            {
                _activeGroup = _groups[selGroupIndex];
                _activeGroup.IsActive = true;

                int selTabIndex = Math.Clamp(wsData.Groups[selGroupIndex].SelectedTabIndex, 0, _activeGroup.Tabs.Count - 1);
                if (_activeGroup.Tabs.Count > 0)
                {
                    _activeTab = _activeGroup.Tabs[selTabIndex];
                }
            }
        }
        else
        {
            // Backwards compatibility migration
            var group = new TabGroup { Name = "Principal", IsActive = true };
            if (wsData.TabUrls != null)
            {
                for (int i = 0; i < wsData.TabUrls.Count; i++)
                {
                    string url = wsData.TabUrls[i];
                    string title = (wsData.TabTitles != null && i < wsData.TabTitles.Count) ? wsData.TabTitles[i] : "Cargando...";
                    bool hasCustom = (wsData.TabHasCustomTitles != null && i < wsData.TabHasCustomTitles.Count) ? wsData.TabHasCustomTitles[i] : false;
                    group.Tabs.Add(new BrowserTab { Url = url, Title = title, HasCustomTitle = hasCustom });
                }
            }

            _groups.Add(group);
            _activeGroup = group;

            int selTabIndex = Math.Clamp(wsData.SelectedTabIndex, 0, group.Tabs.Count - 1);
            if (group.Tabs.Count > 0)
            {
                _activeTab = group.Tabs[selTabIndex];
            }
        }

        GroupsItemsControl.ItemsSource = _groups;
    }

    public WorkspaceSessionData ExportWorkspace()
    {
        var wsData = new WorkspaceSessionData
        {
            Name = _workspaceName,
            SelectedGroupIndex = _groups.IndexOf(_activeGroup ?? _groups.FirstOrDefault() ?? new TabGroup())
        };

        foreach (var group in _groups)
        {
            var gData = new TabGroupSessionData
            {
                Name = group.Name,
                IsActive = group.IsActive,
                SelectedTabIndex = group.ActiveTab != null ? group.Tabs.IndexOf(group.ActiveTab) : 0
            };

            foreach (var tab in group.Tabs)
            {
                gData.TabUrls.Add(tab.Url);
                gData.TabTitles.Add(tab.Title);
                gData.TabHasCustomTitles.Add(tab.HasCustomTitle);
            }

            wsData.Groups.Add(gData);
        }

        // Maintain compatibility for older logic
        wsData.TabUrls = GetTabUrls();
        wsData.TabTitles = GetTabTitles();
        wsData.TabHasCustomTitles = _tabs.Select(t => t.HasCustomTitle).ToList();
        wsData.SelectedTabIndex = GetSelectedTabIndex();

        return wsData;
    }

    private void UpdateTabVisibilityState()
    {
        if (_activeTab == null) return;

        bool isNewTab = string.IsNullOrEmpty(_activeTab.Url) || 
                         _activeTab.Url == "about:blank" || 
                         _activeTab.Url.StartsWith("wallflow://newtab", StringComparison.OrdinalIgnoreCase);

        if (isNewTab)
        {
            LoadWallpaperAsNewTabBackground();
            RenderNewTabShortcuts();

            NewTabPageOverlay.Visibility = Visibility.Visible;
            WebViewHost.Visibility = Visibility.Collapsed;
            if (_activeTab.WebView != null)
            {
                _activeTab.WebView.Detach();
            }
            AddressTextBox.Text = string.Empty;
            NewTabSearchTextBox.Text = string.Empty;
            NewTabSuggestionsBorder.Visibility = Visibility.Collapsed;
            NewTabSearchPlaceholder.Visibility = Visibility.Visible;

            // Set focus to the centered NewTabSearchTextBox
            Dispatcher.BeginInvoke(new Action(() =>
            {
                NewTabSearchTextBox.Focus();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
        else
        {
            NewTabPageOverlay.Visibility = Visibility.Collapsed;
            WebViewHost.Visibility = Visibility.Visible;
            if (_activeTab.WebView != null)
            {
                _activeTab.WebView.Attach(WebViewHost.HostVisual!);
            }
            AddressTextBox.Text = _activeTab.Url;
        }
    }

    private string _originalSearchText = "";
    private bool _isNavigatingSuggestions = false;

    private void NewTabSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string text = NewTabSearchTextBox.Text;
        NewTabSearchPlaceholder.Visibility = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
        
        if (!_isNavigatingSuggestions)
        {
            _originalSearchText = text;
            UpdateNewTabSuggestions(text);
        }
    }

    private void UpdateNewTabSuggestions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            NewTabSuggestionsBorder.Visibility = Visibility.Collapsed;
            NewTabSuggestionsListBox.ItemsSource = null;
            return;
        }

        var textLower = text.ToLower();
        var matches = _bookmarks.Where(b => 
            b.Title.ToLower().Contains(textLower) || 
            b.Url.ToLower().Contains(textLower)
        ).Take(5).ToList();

        if (matches.Any())
        {
            NewTabSuggestionsListBox.ItemsSource = matches;
            NewTabSuggestionsBorder.Visibility = Visibility.Visible;
        }
        else
        {
            NewTabSuggestionsBorder.Visibility = Visibility.Collapsed;
            NewTabSuggestionsListBox.ItemsSource = null;
        }
    }

    private void NewTabSuggestionsListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (NewTabSuggestionsListBox.SelectedItem is BookmarkItem selected)
        {
            NewTabSearchTextBox.Text = selected.Url;
            NavigateNewTabTo(selected.Url);
        }
    }

    private void NewTabSearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (NewTabSuggestionsListBox.SelectedItem is BookmarkItem selected)
            {
                NavigateNewTabTo(selected.Url);
                return;
            }

            string input = NewTabSearchTextBox.Text.Trim();
            if (string.IsNullOrEmpty(input)) return;

            string targetUrl = ResolveSearchUrl(input);
            NavigateNewTabTo(targetUrl);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (NewTabSuggestionsBorder.Visibility == Visibility.Visible && NewTabSuggestionsListBox.Items.Count > 0)
            {
                int index = NewTabSuggestionsListBox.SelectedIndex;
                int count = NewTabSuggestionsListBox.Items.Count;
                int nextIndex = index + 1;

                if (nextIndex < count)
                {
                    _isNavigatingSuggestions = true;
                    NewTabSuggestionsListBox.SelectedIndex = nextIndex;

                    if (NewTabSuggestionsListBox.SelectedItem is BookmarkItem selected)
                    {
                        NewTabSearchTextBox.Text = selected.Url;
                        NewTabSearchTextBox.CaretIndex = NewTabSearchTextBox.Text.Length;
                    }
                    _isNavigatingSuggestions = false;
                }
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Up)
        {
            if (NewTabSuggestionsBorder.Visibility == Visibility.Visible && NewTabSuggestionsListBox.Items.Count > 0)
            {
                int index = NewTabSuggestionsListBox.SelectedIndex;
                int prevIndex = index - 1;

                if (prevIndex >= -1)
                {
                    _isNavigatingSuggestions = true;
                    NewTabSuggestionsListBox.SelectedIndex = prevIndex;

                    if (prevIndex == -1)
                    {
                        NewTabSearchTextBox.Text = _originalSearchText;
                    }
                    else if (NewTabSuggestionsListBox.SelectedItem is BookmarkItem selected)
                    {
                        NewTabSearchTextBox.Text = selected.Url;
                    }

                    NewTabSearchTextBox.CaretIndex = NewTabSearchTextBox.Text.Length;
                    _isNavigatingSuggestions = false;
                }
                e.Handled = true;
            }
        }
    }

    private void NewTabSearchTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        UpdateNewTabSuggestions(NewTabSearchTextBox.Text);
    }

    private void Shortcut_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag != null)
        {
            NavigateNewTabTo(border.Tag.ToString()!);
        }
    }

    private void NavigateNewTabTo(string url)
    {
        if (_activeTab == null) return;
        
        _activeTab.Url = url;
        UpdateTabVisibilityState();

        if (_activeTab.WebView != null)
        {
            if (_activeTab.WebView.CoreWebView2 != null)
            {
                _activeTab.WebView.CoreWebView2.Navigate(url);
            }
            else
            {
                _activeTab.WebView.Source = new Uri(url);
            }
        }
    }

    private List<MostVisitedPage> _mostVisitedPages = new();
    private static readonly string MostVisitedFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallFlow",
        "most_visited.json");

    private void LoadMostVisited()
    {
        try
        {
            if (File.Exists(MostVisitedFile))
            {
                var json = File.ReadAllText(MostVisitedFile);
                _mostVisitedPages = System.Text.Json.JsonSerializer.Deserialize<List<MostVisitedPage>>(json) ?? new();
            }
        }
        catch
        {
            _mostVisitedPages = new();
        }
    }

    private void SaveMostVisited()
    {
        try
        {
            var dir = Path.GetDirectoryName(MostVisitedFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var json = System.Text.Json.JsonSerializer.Serialize(_mostVisitedPages, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(MostVisitedFile, json);
        }
        catch { /* ignored */ }
    }

    private string NormalizeUrlForTracking(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        try
        {
            var uri = new Uri(url);
            string normalized = uri.AbsoluteUri;
            if (normalized.EndsWith("/"))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }
            return normalized;
        }
        catch
        {
            return url;
        }
    }

    private void RegisterPageVisit(string url, string title)
    {
        if (string.IsNullOrEmpty(url) || url == "about:blank" || url.StartsWith("wallflow://", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            string normalizedUrl = NormalizeUrlForTracking(url);
            var existing = _mostVisitedPages.FirstOrDefault(p => NormalizeUrlForTracking(p.Url).Equals(normalizedUrl, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.VisitCount++;
                if (string.IsNullOrEmpty(existing.Title) || existing.Title == "Cargando...")
                {
                    existing.Title = string.IsNullOrEmpty(title) ? GetDomainName(url) : title;
                }
            }
            else
            {
                _mostVisitedPages.Add(new MostVisitedPage
                {
                    Url = url,
                    Title = string.IsNullOrEmpty(title) ? GetDomainName(url) : title,
                    VisitCount = 1
                });
            }

            _mostVisitedPages = _mostVisitedPages.OrderByDescending(p => p.VisitCount).Take(50).ToList();
            SaveMostVisited();

            if (_activeTab != null && (_activeTab.Url == "about:blank" || string.IsNullOrEmpty(_activeTab.Url)))
            {
                RenderNewTabShortcuts();
            }
        }
        catch { /* ignored */ }
    }

    private void LoadWallpaperAsNewTabBackground()
    {
        try
        {
            string wallpaperPath = "";
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
            {
                if (key != null)
                {
                    wallpaperPath = key.GetValue("Wallpaper") as string ?? "";
                }
            }

            if (!string.IsNullOrEmpty(wallpaperPath) && File.Exists(wallpaperPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(wallpaperPath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                var brush = new ImageBrush
                {
                    ImageSource = bitmap,
                    Stretch = Stretch.UniformToFill
                };
                NewTabPageOverlay.Background = brush;
            }
            else
            {
                NewTabPageOverlay.Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x18));
            }
        }
        catch
        {
            NewTabPageOverlay.Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x18));
        }
    }

    private void RenderNewTabShortcuts()
    {
        NewTabShortcutsPanel.Children.Clear();

        var list = _mostVisitedPages
            .GroupBy(p => GetDomainName(p.Url).ToLower())
            .Select(g => g.OrderByDescending(p => p.VisitCount).First())
            .OrderByDescending(p => p.VisitCount)
            .Take(8)
            .ToList();

        if (list.Count == 0)
        {
            list.Add(new MostVisitedPage { Title = "Google", Url = "https://www.google.com" });
            list.Add(new MostVisitedPage { Title = "YouTube", Url = "https://www.youtube.com" });
            list.Add(new MostVisitedPage { Title = "GitHub", Url = "https://github.com" });
        }

        foreach (var page in list)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0x15, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x0E, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(6),
                Cursor = Cursors.Hand,
                Tag = page.Url
            };

            border.MouseEnter += (s, e) => {
                border.Background = new SolidColorBrush(Color.FromArgb(0x25, 255, 255, 255));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255));
            };
            border.MouseLeave += (s, e) => {
                border.Background = new SolidColorBrush(Color.FromArgb(0x15, 255, 255, 255));
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x0E, 255, 255, 255));
            };

            border.MouseLeftButtonDown += (s, e) => {
                NavigateNewTabTo(page.Url);
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            var faviconImg = CreateFaviconImage(GetDomainName(page.Url), 14);

            stack.Children.Add(faviconImg);

            var titleTxt = new TextBlock
            {
                Text = page.Title,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 255, 255, 255)),
                FontFamily = (FontFamily)FindResource("AppFontFamily"),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 100,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(titleTxt);

            border.Child = stack;
            NewTabShortcutsPanel.Children.Add(border);
        }
    }

    private string GetDomainName(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Image CreateFaviconImage(string? domain, double size)
    {
        var img = new Image
        {
            Width = size,
            Height = size,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        if (!string.IsNullOrEmpty(domain))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri($"https://www.google.com/s2/favicons?sz=32&domain={domain}");
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                img.Source = bitmap;
            }
            catch { /* ignored */ }
        }
        return img;
    }

    private static string ResolveSearchUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "https://www.google.com";
        input = input.Trim();
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return input;
        if (input.Contains(".") && !input.Contains(" "))
            return "https://" + input;
        return "https://www.google.com/search?q=" + Uri.EscapeDataString(input);
    }

    public void ApplyBrowserColors()
    {
        try
        {
            var s = SettingsService.Instance;
            string? bgColor = s.BrowserUseDockColors ? s.UserDockBackgroundColor : s.BrowserBackgroundColor;
            string? accentColor = s.BrowserUseDockColors ? s.UserAccentColor : s.BrowserAccentColor;
            string? borderColor = s.BrowserUseDockColors ? s.UserBorderColor1 : s.BrowserBorderColor;

            if (!string.IsNullOrEmpty(bgColor) && TryParseColor(bgColor, out var bg))
                Resources["DockBackgroundBrush"] = new SolidColorBrush(bg);

            if (!string.IsNullOrEmpty(accentColor) && TryParseColor(accentColor, out var accent))
            {
                Resources["AccentBrush"] = new SolidColorBrush(accent);
                Resources["AccentHoverBrush"] = new SolidColorBrush(Lighten(accent));
                Resources["AccentActiveBrush"] = new SolidColorBrush(Color.FromArgb(0x65, accent.R, accent.G, accent.B));
                Resources["AccentGroupBrush"] = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B));
                Resources["AccentGlowBrush"] = new SolidColorBrush(Color.FromArgb(0x18, accent.R, accent.G, accent.B));
            }

            if (!string.IsNullOrEmpty(borderColor) && TryParseColor(borderColor, out var border))
                RootBorder.BorderBrush = new SolidColorBrush(border);
        }
        catch { /* ignored */ }
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
        catch { /* ignored */ }
        return false;
    }

    private static Color Lighten(Color color)
    {
        return Color.FromArgb(color.A,
            Math.Min((byte)255, (byte)(color.R + 35)),
            Math.Min((byte)255, (byte)(color.G + 35)),
            Math.Min((byte)255, (byte)(color.B + 35)));
    }
}


