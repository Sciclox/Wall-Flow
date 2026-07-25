using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WallFlow.Services;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace WallFlow.Windows;

public partial class YouTubeMusicWindow : Window, IWidget
{
    string IWidget.Name => "YouTube";
    public void Open() { ShowWithAnimation(); }
    public void CloseWidget() { SafeHide(); }
    public bool IsOpen => IsLoaded && Visibility == Visibility.Visible;
    private bool _isPinned = false;
    private bool _isHiding = false;
    private bool _allowClose = false;
    private Microsoft.Web.WebView2.Core.CoreWebView2Environment? _webViewEnv;
    public DateTime LastHiddenTime { get; private set; } = DateTime.MinValue;

    public static YouTubeMusicWindow? Instance { get; private set; }

    public YouTubeMusicWindow()
    {
        Instance = this;
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Apply acrylic blur
        BackdropHelper.ApplyAcrylic(this);
        RootBorder.SizeChanged += UpdateBorderClip;
        UpdateBorderClip(null, null);

        // Position window relative to the Dock's YouTube Music Button
        PositionWindow();

        // Initialize WebView2 safely
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

    private void PositionWindow()
    {
        if (DockWindow.Instance != null)
        {
            try
            {
                var btn = DockWindow.Instance.YouTubeMusicButton;
                var dpi = VisualTreeHelper.GetDpi(DockWindow.Instance);
                var screenPos = btn.PointToScreen(new Point(0, 0));
                double left = screenPos.X / dpi.DpiScaleX;
                Left = left - (Width - btn.ActualWidth) / 2;
            }
            catch
            {
                Left = DockWindow.Instance.Left + (DockWindow.Instance.ActualWidth - Width) / 2;
            }

            // Keep within screen bounds
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
            var webViewFolder = Path.Combine(settingsFolder, "WebView2_YouTubeMusic");
            
            if (!Directory.Exists(webViewFolder))
            {
                Directory.CreateDirectory(webViewFolder);
            }

            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(userDataFolder: webViewFolder);
            _webViewEnv = env;
            await webView.EnsureCoreWebView2Async(env);
            
            if (webView.CoreWebView2 != null)
            {
                // 1. Add resource filter for all network requests to allow custom C# filtering
                webView.CoreWebView2.AddWebResourceRequestedFilter("*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);

                webView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

                // 2. Inject JavaScript to hook initial page state variables and auto-mute, speed up, skip ads, and close warnings
                string adBlockerScript = @"
                    (function() {
                        function cleanObject(obj) {
                            if (obj) {
                                if (obj.adPlacements) obj.adPlacements = [];
                                if (obj.adSlots) obj.adSlots = [];
                                if (obj.playerAds) obj.playerAds = [];
                            }
                        }

                        // Intercept initial player data
                        let initialPlayerResponse = window.ytInitialPlayerResponse;
                        Object.defineProperty(window, 'ytInitialPlayerResponse', {
                            get: function() { return initialPlayerResponse; },
                            set: function(val) {
                                cleanObject(val);
                                initialPlayerResponse = val;
                            },
                            configurable: true
                        });
                        if (window.ytInitialPlayerResponse) {
                            cleanObject(window.ytInitialPlayerResponse);
                        }

                        let initialData = window.ytInitialData;
                        Object.defineProperty(window, 'ytInitialData', {
                            get: function() { return initialData; },
                            set: function(val) {
                                cleanObject(val);
                                initialData = val;
                            },
                            configurable: true
                        });
                        if (window.ytInitialData) {
                            cleanObject(window.ytInitialData);
                        }

                        function skipAds() {
                            const video = document.querySelector('video');
                            const adShowing = document.querySelector('.ad-showing, .ad-interrupting, .ytp-ad-player-overlay, ytd-ad-slot-renderer');
                            const ytmusAd = document.querySelector('ytmusic-player[player-ui-state=""ADVERTISING""], .ytmusic-ad-player-overlay');
                            
                            if ((adShowing || ytmusAd) && video) {
                                video.muted = true;
                                video.playbackRate = 16.0;
                                if (video.duration && !isNaN(video.duration) && isFinite(video.duration)) {
                                    video.currentTime = video.duration - 0.1;
                                }
                                if (video.paused) {
                                    video.play().catch(() => {});
                                }
                            }
                            
                            const skipSelectors = [
                                '.ytp-ad-skip-button', 
                                '.ytp-ad-skip-button-modern', 
                                '.ytp-skip-ad-button',
                                '.ytp-ad-skip-button-slot',
                                '.ytmusic-ad-skip-button',
                                'button.ytp-ad-skip-button'
                            ];
                            
                            for (const selector of skipSelectors) {
                                const btn = document.querySelector(selector);
                                if (btn && btn.style.display !== 'none') {
                                    btn.click();
                                }
                            }
                            
                            const bannerSelectors = [
                                '.ytp-ad-overlay-container',
                                'ytd-action-companion-ad-renderer',
                                'ytd-promoted-sparkles-web-renderer',
                                'div.image-companion-ad',
                                'ytmusic-carousel-shelf-basic-header-renderer #bar'
                            ];
                            for (const selector of bannerSelectors) {
                                const banners = document.querySelectorAll(selector);
                                for (const banner of banners) {
                                    banner.style.display = 'none';
                                }
                            }

                            // Dismiss adblocker warnings
                            const warnings = document.querySelectorAll('ytd-enforcement-message-view-model, tp-yt-paper-dialog, yt-playability-error-supported-renderers');
                            for (const warning of warnings) {
                                warning.remove();
                            }
                        }
                        
                        setInterval(skipAds, 100);

                        // Track monitoring JS Bridge
                        (function() {
                            let lastTitle = '';
                            let lastArtist = '';
                            let lastPaused = null;

                            function checkTrackInfo() {
                                try {
                                    const titleEl = document.querySelector('ytmusic-player-bar .title');
                                    const bylineEl = document.querySelector('ytmusic-player-bar .byline');
                                    const videoEl = document.querySelector('video');
                                    const imgEl = document.querySelector('ytmusic-player-bar img.image');

                                    if (!titleEl || !videoEl) return;

                                    const title = titleEl.innerText || '';
                                    const artist = bylineEl ? bylineEl.innerText : '';
                                    const paused = videoEl.paused;
                                    const artwork = imgEl ? imgEl.src : '';

                                    if (title !== lastTitle || artist !== lastArtist || paused !== lastPaused) {
                                        lastTitle = title;
                                        lastArtist = artist;
                                        lastPaused = paused;

                                        window.chrome.webview.postMessage(JSON.stringify({
                                            type: 'trackInfo',
                                            title: title,
                                            artist: artist,
                                            paused: paused,
                                            artwork: artwork
                                        }));
                                    }
                                } catch (e) { }
                            }
                            setInterval(checkTrackInfo, 500);
                        })();
                    })();
                ";
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(adBlockerScript);

                webView.CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;
                webView.CoreWebView2.WebMessageReceived += WebMessageReceived;
                UpdateNavigationButtonStates();
            }

            webView.Source = new Uri("https://music.youtube.com");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al iniciar el reproductor de música: {ex.Message}", "Error WebView2", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.TryGetWebMessageAsString();
            using (var doc = System.Text.Json.JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "trackInfo")
                {
                    string title = root.GetProperty("title").GetString() ?? "";
                    string artist = root.GetProperty("artist").GetString() ?? "";
                    bool paused = root.GetProperty("paused").GetBoolean();
                    string artwork = root.GetProperty("artwork").GetString() ?? "";

                    // If needed, we can push values to the DockViewModel or handle them.
                    // Windows natively links WebView2 playing media with system SMTC.
                }
            }
        }
        catch { }
    }

    public async void TogglePlayPause()
    {
        try
        {
            if (webView.CoreWebView2 != null)
            {
                await webView.ExecuteScriptAsync(
                    "document.querySelector('video')?.paused ? document.querySelector('video')?.play() : document.querySelector('video')?.pause()"
                );
            }
        }
        catch { }
    }

    public async void NextTrack()
    {
        try
        {
            if (webView.CoreWebView2 != null)
            {
                await webView.ExecuteScriptAsync(
                    "document.querySelector('ytmusic-player-bar .next-button')?.click()"
                );
            }
        }
        catch { }
    }

    public async void PreviousTrack()
    {
        try
        {
            if (webView.CoreWebView2 != null)
            {
                await webView.ExecuteScriptAsync(
                    "document.querySelector('ytmusic-player-bar .previous-button')?.click()"
                );
            }
        }
        catch { }
    }

    private void CoreWebView2_WebResourceRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            string url = e.Request.Uri;

            // 1. Intercept player API calls and rewrite JSON to remove ads
            if (url.Contains("youtubei/v1/player", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("youtubei/v1/next", StringComparison.OrdinalIgnoreCase))
            {
                var deferral = e.GetDeferral();
                Task.Run(async () =>
                {
                    try
                    {
                        string cleanedJson = await FetchAndCleanPlayerJsonAsync(e.Request);
                        
                        var headers = new StringBuilder();
                        headers.AppendLine("Content-Type: application/json; charset=UTF-8");
                        headers.AppendLine("Access-Control-Allow-Origin: *");
                        
                        var response = _webViewEnv!.CreateWebResourceResponse(
                            new MemoryStream(Encoding.UTF8.GetBytes(cleanedJson)),
                            200,
                            "OK",
                            headers.ToString()
                        );
                        
                        Dispatcher.Invoke(() =>
                        {
                            e.Response = response;
                        });
                    }
                    catch { }
                    finally
                    {
                        deferral.Complete();
                    }
                });
                return;
            }

            // 2. Block standard ad/tracker domains
            if (_webViewEnv != null && IsAdUrl(url))
            {
                // Return 403 Forbidden to block the ad/tracker request
                var response = _webViewEnv.CreateWebResourceResponse(null, 403, "Forbidden", null);
                e.Response = response;
            }
        }
        catch { }
    }

    private async Task<string> FetchAndCleanPlayerJsonAsync(Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequest request)
    {
        using (var client = new HttpClient())
        {
            var reqMsg = new HttpRequestMessage(new HttpMethod(request.Method), request.Uri);

            // Copy all request headers
            foreach (var header in request.Headers)
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                reqMsg.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Copy POST request content
            if (request.Content != null)
            {
                var memoryStream = new MemoryStream();
                await request.Content.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                var streamContent = new StreamContent(memoryStream);

                if (request.Headers.Contains("Content-Type"))
                {
                    streamContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(request.Headers.GetHeader("Content-Type"));
                }
                reqMsg.Content = streamContent;
            }

            // Send request to YouTube
            var respMsg = await client.SendAsync(reqMsg);
            var jsonString = await respMsg.Content.ReadAsStringAsync();

            // Strip ad arrays from JSON response
            if (jsonString.Contains("\"adPlacements\"") || jsonString.Contains("\"adSlots\"") || jsonString.Contains("\"playerAds\""))
            {
                try
                {
                    jsonString = RemoveJsonArray(jsonString, "adPlacements");
                    jsonString = RemoveJsonArray(jsonString, "adSlots");
                    jsonString = RemoveJsonArray(jsonString, "playerAds");
                }
                catch { }
            }

            return jsonString;
        }
    }

    private string RemoveJsonArray(string json, string key)
    {
        while (true)
        {
            int keyIndex = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (keyIndex == -1) break;

            int openBracketIndex = json.IndexOf('[', keyIndex);
            if (openBracketIndex == -1) break;

            int bracketCount = 1;
            int i = openBracketIndex + 1;
            bool inString = false;
            bool isEscaped = false;

            while (i < json.Length && bracketCount > 0)
            {
                char c = json[i];
                if (isEscaped)
                {
                    isEscaped = false;
                }
                else if (c == '\\')
                {
                    isEscaped = true;
                }
                else if (c == '"')
                {
                    inString = !inString;
                }
                else if (!inString)
                {
                    if (c == '[') bracketCount++;
                    else if (c == ']') bracketCount--;
                }
                i++;
            }

            if (bracketCount == 0)
            {
                string prefix = json.Substring(0, openBracketIndex);
                string suffix = json.Substring(i);
                
                string keyPlaceholder = "_" + key + "_processed_";
                prefix = prefix.Substring(0, keyIndex) + "\"" + keyPlaceholder + "\"";
                
                json = prefix + "[]" + suffix;
            }
            else
            {
                break;
            }
        }

        json = json.Replace("_" + key + "_processed_", key);
        return json;
    }

    private bool IsAdUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;

        // Check against common ad/tracking server paths
        if (url.Contains("doubleclick.net", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("googleads", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("googlesyndication.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("adservice.google", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("ads.youtube.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("adunit", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("ad_break", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("stats/ads", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("youtube.com/pagead", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("youtube.com/ptracking", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("youtube.com/pcs/activeview", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("get_midroll_info", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Native YouTube video/audio ads served from googlevideo
        if (url.Contains("googlevideo.com/videoplayback", StringComparison.OrdinalIgnoreCase))
        {
            if (url.Contains("adformat=", StringComparison.OrdinalIgnoreCase) || 
                url.Contains("oad=", StringComparison.OrdinalIgnoreCase) || 
                url.Contains("ad=", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void CoreWebView2_HistoryChanged(object? sender, object e)
    {
        UpdateNavigationButtonStates();
    }

    private void UpdateNavigationButtonStates()
    {
        if (webView.CoreWebView2 != null)
        {
            BackButton.IsEnabled = webView.CoreWebView2.CanGoBack;
            ForwardButton.IsEnabled = webView.CoreWebView2.CanGoForward;
        }
    }

    public void ShowWithAnimation()
    {
        // Cancel any running animations to prevent property locking and bounce glitches
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(TopProperty, null);
        _isHiding = false;

        SoundService.Play(SoundType.Whoosh);
        PositionWindow();

        this.Visibility = Visibility.Visible;
        this.Opacity = 0;

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

        this.Activate();
        this.Focus();
    }

    public void SafeHide()
    {
        if (_isHiding || this.Visibility != Visibility.Visible) return;

        // Cancel any running animations to prevent property locking
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(TopProperty, null);

        SoundService.Play(SoundType.Whoosh);
        _isHiding = true;
        LastHiddenTime = DateTime.Now;

        if (DockWindow.Instance != null)
        {
            DockWindow.Instance.IsYouTubeMusicOpen = false;
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
                this.Visibility = Visibility.Hidden;
                _isHiding = false;
                BeginAnimation(OpacityProperty, null);
                BeginAnimation(TopProperty, null);
            }
            if (DockWindow.Instance != null)
            {
                DockWindow.Instance.IsYouTubeMusicOpen = false;
            }
        };

        BeginAnimation(OpacityProperty, fadeOut);
        BeginAnimation(TopProperty, slide);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (webView.CoreWebView2 != null && webView.CoreWebView2.CanGoBack)
        {
            webView.CoreWebView2.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (webView.CoreWebView2 != null && webView.CoreWebView2.CanGoForward)
        {
            webView.CoreWebView2.GoForward();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (webView.CoreWebView2 != null)
        {
            webView.CoreWebView2.Reload();
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        if (_isPinned)
        {
            PinButton.SetResourceReference(ForegroundProperty, "AccentBrush");
            PinButton.Content = "\uE719"; // Pinned state icon
            PinButton.ToolTip = "Desanclar (Ocultar automáticamente)";
        }
        else
        {
            PinButton.ClearValue(ForegroundProperty);
            PinButton.Content = "\uE718"; // Unpinned state icon
            PinButton.ToolTip = "Mantener visible (Anclar)";
        }
    }



    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SafeHide();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_isPinned && !IsMouseOverWindow())
        {
            SafeHide();
        }
    }

    private bool IsMouseOverWindow()
    {
        try
        {
            var p = Mouse.GetPosition(this);
            return p.X >= 0 && p.X <= ActualWidth &&
                   p.Y >= 0 && p.Y <= ActualHeight;
        }
        catch
        {
            return false;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SafeHide();
        }
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
            // Destroy WebView2 to prevent hanging processes
            webView.Dispose();
            base.OnClosing(e);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Instance == this) Instance = null;
        base.OnClosed(e);

        // Force GC to clean resources
        GC.Collect();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
    }
}
