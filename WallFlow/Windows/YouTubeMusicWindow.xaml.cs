using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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
        if (DockWindow.Instance != null)
        {
            DockWindow.Instance.HookYouTubeMusicWindow(this);
        }
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

            var options = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling"
            };
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(userDataFolder: webViewFolder, options: options);
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

                        // Disable beforeunload prompts
                        try {
                            window.addEventListener('beforeunload', function(e) {
                                e.stopImmediatePropagation();
                            }, true);
                            window.onbeforeunload = null;
                        } catch (e) { }

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

                        // Auto-play trigger only once when landing on a new watch/song page
                        let lastAutoPlayedWatchUrl = '';
                        let autoPlayAttempts = 0;

                        function autoPlayWatchPage() {
                            try {
                                const currentUrl = window.location.href;
                                if (!currentUrl.includes('watch')) {
                                    lastAutoPlayedWatchUrl = '';
                                    autoPlayAttempts = 0;
                                    return;
                                }

                                if (lastAutoPlayedWatchUrl !== currentUrl) {
                                    const video = document.querySelector('video');
                                    if (video) {
                                        if (!video.paused && video.currentTime > 0) {
                                            lastAutoPlayedWatchUrl = currentUrl;
                                            autoPlayAttempts = 0;
                                        } else if (autoPlayAttempts < 15) {
                                            autoPlayAttempts++;
                                            video.play().then(function() {
                                                lastAutoPlayedWatchUrl = currentUrl;
                                            }).catch(function() {
                                                const playBtn = document.querySelector('ytmusic-player-bar #play-pause-button, button#play-pause-button');
                                                if (playBtn && (playBtn.getAttribute('aria-label') === 'Play' || playBtn.getAttribute('aria-label') === 'Reproducir')) {
                                                    playBtn.click();
                                                }
                                            });
                                        } else {
                                            lastAutoPlayedWatchUrl = currentUrl;
                                        }
                                    }
                                }
                            } catch (e) { }
                        }
                        setInterval(autoPlayWatchPage, 500);

                        // Track monitoring JS Bridge
                        (function() {
                            let lastTitle = '';
                            let lastArtist = '';
                            let lastPaused = null;
                            let lastTime = '';

                            function checkTrackInfo() {
                                try {
                                    const titleEl = document.querySelector('ytmusic-player-bar .title');
                                    const bylineEl = document.querySelector('ytmusic-player-bar .byline') || document.querySelector('ytmusic-player-bar [class*=\'byline\']') || document.querySelector('ytmusic-player-bar .subtitle');
                                    const videoEl = document.querySelector('video');
                                    const imgEl = document.querySelector('ytmusic-player-bar img.image');

                                    if (!titleEl || !videoEl) return;

                                    const title = (titleEl.innerText || titleEl.textContent || '').trim();
                                    let artist = bylineEl ? (bylineEl.innerText || bylineEl.textContent || '').trim() : '';
                                    if (artist) {
                                        const parts = artist.split(/[•\n\r]/);
                                        if (parts.length > 0 && parts[0].trim().length > 0) {
                                            artist = parts[0].trim();
                                        }
                                    }
                                    const paused = videoEl.paused;
                                    const artwork = imgEl ? imgEl.src : '';

                                    const timeEl = document.querySelector('ytmusic-player-bar .time-info');
                                    let timeStr = timeEl ? timeEl.innerText.trim() : '';
                                    let progressVal = 0;
                                    if (videoEl && videoEl.duration && !isNaN(videoEl.duration) && videoEl.duration > 0) {
                                        progressVal = videoEl.currentTime / videoEl.duration;
                                        if (!timeStr) {
                                            const cur = Math.floor(videoEl.currentTime);
                                            const curM = Math.floor(cur / 60);
                                            const curS = cur % 60;
                                            timeStr = (curM < 10 ? '0' : '') + curM + ':' + (curS < 10 ? '0' : '') + curS;
                                        }
                                    }

                                    if (title !== lastTitle || artist !== lastArtist || paused !== lastPaused || timeStr !== lastTime) {
                                        lastTitle = title;
                                        lastArtist = artist;
                                        lastPaused = paused;
                                        lastTime = timeStr;

                                        window.chrome.webview.postMessage(JSON.stringify({
                                            type: 'trackInfo',
                                            title: title,
                                            artist: artist,
                                            paused: paused,
                                            artwork: artwork,
                                            time: timeStr,
                                            progress: progressVal
                                        }));
                                    }
                                } catch (e) { }
                            }
                            setInterval(checkTrackInfo, 200);
                            document.addEventListener('play', checkTrackInfo, true);
                            document.addEventListener('playing', checkTrackInfo, true);
                            document.addEventListener('loadedmetadata', checkTrackInfo, true);
                        })();
                    })();
                ";
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(adBlockerScript);

                webView.CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;
                webView.CoreWebView2.WebMessageReceived += WebMessageReceived;
                webView.CoreWebView2.ScriptDialogOpening += (sender, args) =>
                {
                    // Automatically accept beforeunload / alert / confirm dialogs to prevent "¿Abandonar sitio?" popups
                    if (args.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ScriptDialogKind.Beforeunload ||
                        args.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ScriptDialogKind.Confirm ||
                        args.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ScriptDialogKind.Alert)
                    {
                        args.Accept();
                    }
                };
                UpdateNavigationButtonStates();
            }

            string startUrl;
            if (!string.IsNullOrEmpty(PendingSearchQuery))
            {
                if (PendingSearchQuery.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    PendingSearchQuery.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    startUrl = PendingSearchQuery;
                }
                else if (PendingSearchQuery.Length == 11 && !PendingSearchQuery.Contains(' ') && !PendingSearchQuery.Contains('?'))
                {
                    startUrl = $"https://music.youtube.com/watch?v={PendingSearchQuery}";
                }
                else
                {
                    startUrl = $"https://music.youtube.com/search?q={Uri.EscapeDataString(PendingSearchQuery)}";
                }
            }
            else
            {
                startUrl = "https://music.youtube.com";
            }
            PendingSearchQuery = null;
            webView.Source = new Uri(startUrl);
            _ = Task.Delay(2500).ContinueWith(_ => Dispatcher.Invoke(EnsurePlaying));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al iniciar el reproductor de música: {ex.Message}", "Error WebView2", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public string CurrentTitle { get; private set; } = string.Empty;
    public string CurrentArtist { get; private set; } = string.Empty;
    public string CurrentTimeStr { get; private set; } = string.Empty;
    public double CurrentProgress { get; private set; }
    public bool CurrentIsPlaying { get; private set; }
    public string CurrentArtworkUrl { get; private set; } = string.Empty;
    public ImageSource? CurrentArtwork { get; private set; }
    public bool HasActiveTrack => !string.IsNullOrEmpty(CurrentTitle);
    public event EventHandler? TrackChanged;

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
                    string time = root.TryGetProperty("time", out var tProp) ? (tProp.GetString() ?? "") : "";
                    double progress = root.TryGetProperty("progress", out var pProp) ? pProp.GetDouble() : 0;

                    bool metadataChanged = CurrentTitle != title || CurrentArtist != artist;
                    bool playStateChanged = CurrentIsPlaying == paused;

                    CurrentTitle = title;
                    CurrentArtist = artist;
                    CurrentTimeStr = time;
                    CurrentProgress = progress;
                    CurrentIsPlaying = !paused;

                    if (!string.IsNullOrEmpty(artwork) && artwork != CurrentArtworkUrl)
                    {
                        CurrentArtworkUrl = artwork;
                        _ = LoadArtworkAsync(artwork);
                    }

                    if (metadataChanged || playStateChanged)
                    {
                        TrackChanged?.Invoke(this, EventArgs.Empty);

                        if (!paused && !string.IsNullOrWhiteSpace(title) && metadataChanged)
                        {
                            try
                            {
                                WallFlow.Windows.FloatingMediaOrbWindow.ShowOrb(
                                    title,
                                    artist,
                                    CurrentArtwork,
                                    isPlaying: true);
                            }
                            catch { }
                        }
                    }
                }
            }
        }
        catch { }
    }

    private async Task LoadArtworkAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            var bytes = await client.GetByteArrayAsync(url);
            if (bytes == null || bytes.Length == 0) return;

            using var ms = new MemoryStream(bytes);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.DecodePixelWidth = 100;
            bi.EndInit();
            bi.Freeze();

            Dispatcher.Invoke(() =>
            {
                CurrentArtwork = bi;
                TrackChanged?.Invoke(this, EventArgs.Empty);
            });
        }
        catch { }
    }

    public static string? PendingSearchQuery { get; set; }

    public void SearchAndPlay(string query)
    {
        PlayMusic(query, showWindow: true);
    }

    public void PlayMusic(string trackIdOrQuery, bool showWindow = false)
    {
        if (string.IsNullOrWhiteSpace(trackIdOrQuery))
        {
            if (showWindow) ShowWithAnimation();
            return;
        }

        string targetUrl;
        if (trackIdOrQuery.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trackIdOrQuery.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            targetUrl = trackIdOrQuery;
        }
        else if (trackIdOrQuery.Length == 11 && !trackIdOrQuery.Contains(' '))
        {
            targetUrl = $"https://music.youtube.com/watch?v={trackIdOrQuery}";
        }
        else
        {
            targetUrl = $"https://music.youtube.com/search?q={Uri.EscapeDataString(trackIdOrQuery)}";
        }

        PendingSearchQuery = targetUrl;

        // If window is not loaded, show it invisibly so WPF initializes HWND and WebView2
        if (!IsLoaded)
        {
            Opacity = 0;
            Show();
            if (!showWindow)
            {
                Hide();
            }
        }

        try
        {
            if (webView != null && webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Navigate(targetUrl);
                PendingSearchQuery = null;
                _ = Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(EnsurePlaying));
            }
            else
            {
                if (webView != null)
                {
                    webView.Source = new Uri(targetUrl);
                }
            }
        }
        catch { }

        if (showWindow)
        {
            ShowWithAnimation();
        }
    }

    public async void EnsurePlaying()
    {
        try
        {
            if (webView?.CoreWebView2 != null)
            {
                await webView.CoreWebView2.ExecuteScriptAsync(@"
                    (function() {
                        try {
                            const video = document.querySelector('video');
                            if (video && video.paused) {
                                video.play().catch(() => {});
                            }
                            const playBtn = document.querySelector('ytmusic-player-bar #play-pause-button, button#play-pause-button');
                            if (playBtn && (playBtn.getAttribute('aria-label') === 'Play' || playBtn.getAttribute('aria-label') === 'Reproducir')) {
                                playBtn.click();
                            }
                        } catch (e) { }
                    })();
                ");
            }
        }
        catch { }
    }

    public async Task<bool> TogglePlayPauseAsync()
    {
        try
        {
            if (webView?.CoreWebView2 != null)
            {
                string script = @"
                    (function() {
                        try {
                            var v = document.querySelector('video');
                            var wasPaused = v ? v.paused : false;
                            var playBtn = document.querySelector('ytmusic-player-bar #play-pause-button, button#play-pause-button, #play-pause-button');
                            if (playBtn) {
                                playBtn.click();
                            } else if (v) {
                                if (wasPaused) v.play().catch(function(){});
                                else v.pause();
                            }
                            return wasPaused;
                        } catch(e) {
                            return false;
                        }
                    })();
                ";
                string result = await webView.CoreWebView2.ExecuteScriptAsync(script);
                if (bool.TryParse(result, out bool isNowPlaying))
                {
                    CurrentIsPlaying = isNowPlaying;
                    TrackChanged?.Invoke(this, EventArgs.Empty);
                    return isNowPlaying;
                }
            }
        }
        catch { }
        CurrentIsPlaying = !CurrentIsPlaying;
        TrackChanged?.Invoke(this, EventArgs.Empty);
        return CurrentIsPlaying;
    }

    public async void TogglePlayPause()
    {
        await TogglePlayPauseAsync();
    }

    public async Task NextTrackAsync()
    {
        try
        {
            if (webView?.CoreWebView2 != null)
            {
                await webView.CoreWebView2.ExecuteScriptAsync(@"
                    (function() {
                        try {
                            var btn = document.querySelector('ytmusic-player-bar .next-button, button.next-button, .next-button');
                            if (btn) btn.click();
                        } catch(e) {}
                    })();
                ");
            }
        }
        catch { }
    }

    public async void NextTrack()
    {
        await NextTrackAsync();
    }

    public async Task PreviousTrackAsync()
    {
        try
        {
            if (webView?.CoreWebView2 != null)
            {
                await webView.CoreWebView2.ExecuteScriptAsync(@"
                    (function() {
                        try {
                            var btn = document.querySelector('ytmusic-player-bar .previous-button, button.previous-button, .previous-button');
                            if (btn) btn.click();
                        } catch(e) {}
                    })();
                ");
            }
        }
        catch { }
    }

    public async void PreviousTrack()
    {
        await PreviousTrackAsync();
    }

    public bool IsRepeatTrack { get; private set; }

    public async Task<bool> ToggleRepeatTrackAsync(bool? forceState = null)
    {
        try
        {
            if (webView?.CoreWebView2 != null)
            {
                bool newState = forceState ?? !IsRepeatTrack;
                string jsBool = newState ? "true" : "false";

                string script = $@"(function() {{
                    try {{
                        const enable = {jsBool};
                        window.__wallflowRepeatTrack = enable;
                        const video = document.querySelector('video');
                        if (video) {{
                            video.loop = enable;
                            if (!window.__wallflowLoopHooked) {{
                                window.__wallflowLoopHooked = true;
                                video.addEventListener('ended', function(e) {{
                                    if (window.__wallflowRepeatTrack) {{
                                        try {{
                                            e.stopImmediatePropagation();
                                            video.currentTime = 0;
                                            video.play();
                                        }} catch(err) {{}}
                                    }}
                                }}, true);
                                video.addEventListener('timeupdate', function(e) {{
                                    if (window.__wallflowRepeatTrack && video.duration > 0 && video.currentTime >= video.duration - 0.25) {{
                                        try {{
                                            video.currentTime = 0;
                                            video.play();
                                        }} catch(err) {{}}
                                    }}
                                }}, true);
                            }}
                        }}

                        const playerBar = document.querySelector('ytmusic-player-bar');
                        const repeatBtn = document.querySelector('ytmusic-player-bar .repeat, button.repeat, .repeat-button');
                        if (playerBar && repeatBtn) {{
                            const targetMode = enable ? 'ONE' : 'NONE';
                            for (let i = 0; i < 3; i++) {{
                                if (playerBar.repeatMode_ === targetMode) break;
                                repeatBtn.click();
                            }}
                        }} else if (repeatBtn) {{
                            repeatBtn.click();
                        }}
                        return true;
                    }} catch(e) {{
                        return false;
                    }}
                }})();";

                string res = await webView.CoreWebView2.ExecuteScriptAsync(script);
                IsRepeatTrack = newState;
                return res == "true";
            }
        }
        catch { }
        return false;
    }

    public async Task<string> GetQueueJsonAsync()
    {
        try
        {
            if (webView?.CoreWebView2 != null)
            {
                string script = @"
                    (function() {
                        try {
                            function extractItems() {
                                const queueEls = document.querySelectorAll('ytmusic-player-queue-item');
                                if (!queueEls || queueEls.length === 0) return null;
                                let list = [];
                                for (let i = 0; i < queueEls.length; i++) {
                                    const el = queueEls[i];
                                    const titleEl = el.querySelector('.song-title') || el.querySelector('.title') || el.querySelector('yt-formatted-string.title');
                                    const bylineEl = el.querySelector('.byline') || el.querySelector('yt-formatted-string.byline');
                                    const durEl = el.querySelector('.duration') || el.querySelector('.time-info') || el.querySelector('yt-formatted-string.duration') || el.querySelector('[class*=\'duration\']');
                                    const imgEl = el.querySelector('img');
                                    const isSelected = el.hasAttribute('selected') || el.classList.contains('selected') || el.getAttribute('aria-selected') === 'true';

                                    // Robust extraction of videoId across Polymer models, links, and thumbnails
                                    let videoId = '';
                                    try {
                                        const d = el.data || (el.__data ? (el.__data.data || el.__data) : null);
                                        if (d) {
                                            if (typeof d.videoId === 'string' && d.videoId.length === 11) videoId = d.videoId;
                                            else if (d.playlistPanelVideoRenderer && d.playlistPanelVideoRenderer.videoId) videoId = d.playlistPanelVideoRenderer.videoId;
                                            else if (d.navigationEndpoint && d.navigationEndpoint.watchEndpoint && d.navigationEndpoint.watchEndpoint.videoId) videoId = d.navigationEndpoint.watchEndpoint.videoId;
                                        }
                                    } catch(e) {}

                                    if (!videoId) {
                                        try {
                                            const pbr = el.querySelector('ytmusic-play-button-renderer');
                                            const pd = pbr ? (pbr.data || (pbr.__data ? (pbr.__data.data || pbr.__data) : null)) : null;
                                            if (pd && pd.navigationEndpoint && pd.navigationEndpoint.watchEndpoint && pd.navigationEndpoint.watchEndpoint.videoId) {
                                                videoId = pd.navigationEndpoint.watchEndpoint.videoId;
                                            }
                                        } catch(e) {}
                                    }

                                    if (!videoId) {
                                        try {
                                            const img = el.querySelector('img');
                                            if (img && img.src) {
                                                const m = img.src.match(/\/vi(?:_webp)?\/([a-zA-Z0-9_-]{11})\//);
                                                if (m) videoId = m[1];
                                            }
                                        } catch(e) {}
                                    }

                                    if (!videoId) {
                                        try {
                                            const linkEl = el.querySelector('a[href*=\'watch?v=\']') || el.querySelector('a');
                                            if (linkEl && linkEl.href) {
                                                const hrefStr = linkEl.href;
                                                const vIdx = hrefStr.indexOf('v=');
                                                if (vIdx !== -1) {
                                                    const sub = hrefStr.substring(vIdx + 2);
                                                    const ampIdx = sub.indexOf('&');
                                                    videoId = ampIdx !== -1 ? sub.substring(0, ampIdx) : sub;
                                                }
                                            }
                                        } catch(e) {}
                                    }

                                    const title = titleEl ? (titleEl.innerText || titleEl.textContent || '').trim() : '';
                                    if (title) {
                                        list.push({
                                            index: i,
                                            title: title,
                                            artist: bylineEl ? (bylineEl.innerText || bylineEl.textContent || '').trim() : '',
                                            duration: durEl ? (durEl.innerText || durEl.textContent || '').trim() : '',
                                            thumbnail: imgEl ? (imgEl.src || '') : '',
                                            selected: isSelected,
                                            videoId: videoId
                                        });
                                    }
                                }
                                return list.length > 0 ? list : null;
                            }

                            let items = extractItems();
                            if (!items) {
                                const queueBtn = document.querySelector('ytmusic-player-bar .queue-button, #right-controls .queue-button, ytmusic-player-bar [aria-label*=""Queue"" i], ytmusic-player-bar [aria-label*=""Cola"" i]');
                                if (queueBtn) {
                                    queueBtn.click();
                                    items = extractItems();
                                }
                            }

                            return JSON.stringify(items || []);
                        } catch(e) {
                            return '[]';
                        }
                    })();
                ";
                string jsonResult = await webView.CoreWebView2.ExecuteScriptAsync(script);
                if (!string.IsNullOrEmpty(jsonResult))
                {
                    if (jsonResult.StartsWith("\"") && jsonResult.EndsWith("\""))
                    {
                        try
                        {
                            jsonResult = System.Text.Json.JsonSerializer.Deserialize<string>(jsonResult) ?? "[]";
                        }
                        catch { }
                    }
                    return jsonResult;
                }
            }
        }
        catch { }
        return "[]";
    }

    public async Task<bool> PlayQueueTrackAsync(int index, string? videoId = null)
    {
        try
        {
            if (webView?.CoreWebView2 != null)
            {
                string safeVid = (videoId ?? string.Empty).Replace("'", "").Replace("\"", "").Trim();
                string script = @"
                    (function() {
                        try {
                            const idx = __INDEX__;
                            const vid = '__VID__';

                            // 1. Direct Queue element interaction in DOM (click on row, title, play-button, dblclick)
                            const queueEls = document.querySelectorAll('ytmusic-player-queue-item');
                            let domClicked = false;
                            if (queueEls && queueEls[idx]) {
                                const row = queueEls[idx];
                                try {
                                    row.scrollIntoView({ block: 'nearest' });
                                } catch(e) {}

                                function dispatchClick(target) {
                                    if (!target) return;
                                    const opts = { bubbles: true, cancelable: true, composed: true, view: window };
                                    try { target.dispatchEvent(new PointerEvent('pointerdown', opts)); } catch(e) {}
                                    try { target.dispatchEvent(new MouseEvent('mousedown', opts)); } catch(e) {}
                                    try { target.dispatchEvent(new PointerEvent('pointerup', opts)); } catch(e) {}
                                    try { target.dispatchEvent(new MouseEvent('mouseup', opts)); } catch(e) {}
                                    try { target.dispatchEvent(new MouseEvent('click', opts)); } catch(e) {}
                                    if (typeof target.click === 'function') {
                                        try { target.click(); } catch(e) {}
                                    }
                                }

                                const titleEl = row.querySelector('.song-title, yt-formatted-string.title, .title') || row.querySelector('.song-info');
                                const playBtn = row.querySelector('ytmusic-play-button-renderer');
                                const playBtnInner = playBtn ? (playBtn.shadowRoot ? playBtn.shadowRoot.querySelector('#button, button, #icon') : null) : null;

                                if (titleEl) dispatchClick(titleEl);
                                if (playBtnInner) dispatchClick(playBtnInner);
                                if (playBtn) dispatchClick(playBtn);
                                dispatchClick(row);

                                try {
                                    row.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true, composed: true, view: window }));
                                } catch(e) {}

                                // Polymer navigation endpoint
                                try {
                                    const endpoint = row.data?.navigationEndpoint || 
                                                     row.__data?.data?.navigationEndpoint ||
                                                     row.__data?.navigationEndpoint;
                                    if (endpoint) {
                                        const app = document.querySelector('ytmusic-app');
                                        if (app && typeof app.navigate_ === 'function') {
                                            app.navigate_(endpoint);
                                        }
                                    }
                                } catch(e) {}

                                domClicked = true;
                            }

                            // 2. If videoId is provided and valid, try player.loadVideoById or window.location
                            if (vid && vid.length === 11) {
                                const player = document.querySelector('#movie_player') || 
                                               document.querySelector('ytmusic-player-bar')?.playerApi_;
                                if (player && typeof player.loadVideoById === 'function') {
                                    try {
                                        player.loadVideoById(vid);
                                        return true;
                                    } catch(e) {}
                                }
                                if (!domClicked) {
                                    window.location.href = 'https://music.youtube.com/watch?v=' + vid;
                                    return true;
                                }
                            }

                            return domClicked;
                        } catch(e) {
                            return false;
                        }
                    })();
                ".Replace("__INDEX__", index.ToString())
                 .Replace("__VID__", safeVid);

                string res = await webView.CoreWebView2.ExecuteScriptAsync(script);
                
                // If in-page JS failed and we have a valid 11-char videoId, navigate directly:
                if (res != "true" && !string.IsNullOrEmpty(safeVid) && safeVid.Length == 11)
                {
                    webView.CoreWebView2.Navigate($"https://music.youtube.com/watch?v={safeVid}");
                    _ = Task.Delay(1800).ContinueWith(_ => Dispatcher.Invoke(EnsurePlaying));
                    return true;
                }

                return res == "true";
            }
        }
        catch { }
        return false;
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
