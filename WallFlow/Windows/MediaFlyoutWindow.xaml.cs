using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallFlow.Services;

namespace WallFlow.Windows;

public partial class MediaFlyoutWindow : Window, IWidget
{
    string IWidget.Name => "Media";
    public void Open() { Show(); Activate(); }
    public bool IsOpen => IsLoaded && IsVisible;

    private global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private readonly List<global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession> _sessions = new();
    private int _selectedSessionIndex = 0;
    private bool _isInitialSessionSelected = false;
    private readonly global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession? _initialSession;

    private System.Threading.CancellationTokenSource? _updateCts;
    private bool _isClosing = false;
    private readonly double _targetLeft;

    private global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession? CurrentSession =>
        (_sessions.Count > 0 && _selectedSessionIndex >= 0 && _selectedSessionIndex < _sessions.Count)
            ? _sessions[_selectedSessionIndex]
            : _initialSession;

    public MediaFlyoutWindow(global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession? session, double left)
    {
        InitializeComponent();
        _initialSession = session;
        _targetLeft = left;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Apply acrylic blur
        BackdropHelper.ApplyAcrylic(this);
        BackdropHelper.UpdateWindowRegion(this, 0);

        // Position window relative to Media player section of the dock
        if (DockWindow.Instance != null)
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            var screenIndex = Math.Clamp(SettingsService.Instance.DockMonitorIndex, 0, screens.Length - 1);
            var screen = screens[screenIndex];
            var dpi = VisualTreeHelper.GetDpi(this);

            double screenLeft = screen.Bounds.X / dpi.DpiScaleX;
            double screenRight = (screen.Bounds.X + screen.Bounds.Width) / dpi.DpiScaleX;

            Left = _targetLeft;

            // Ensure window does not go off-screen on the target monitor
            if (Left < screenLeft) Left = screenLeft + 10;
            if (Left + Width > screenRight)
            {
                Left = screenRight - Width - 10;
            }

            bool isBottom = SettingsService.Instance.DockPosition == "Bottom";
            double finalTop = isBottom 
                ? DockWindow.Instance.Top - Height 
                : DockWindow.Instance.Top + DockWindow.Instance.ActualHeight;

            Top = isBottom ? finalTop + 15 : finalTop - 15;
            Opacity = 0;

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)) 
            { 
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } 
            };
            var slide = new System.Windows.Media.Animation.DoubleAnimation(isBottom ? finalTop + 15 : finalTop - 15, finalTop, TimeSpan.FromMilliseconds(250)) 
            { 
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } 
            };

            BeginAnimation(OpacityProperty, fadeIn);
            BeginAnimation(TopProperty, slide);
        }
        else
        {
            Left = _targetLeft;
            bool isBottom = SettingsService.Instance.DockPosition == "Bottom";
            Top = isBottom ? SystemParameters.PrimaryScreenHeight - Height - 56 : 56;
        }

        InitSessions();
    }

    private async void InitSessions()
    {
        try
        {
            _sessionManager = await global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_sessionManager != null)
            {
                _sessionManager.SessionsChanged += SessionManager_SessionsChanged;
            }
        }
        catch { }

        RefreshSessions();
    }

    private void SessionManager_SessionsChanged(
        global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager sender,
        global::Windows.Media.Control.SessionsChangedEventArgs args)
    {
        Dispatcher.BeginInvoke(RefreshSessions);
    }

    private void RefreshSessions()
    {
        try
        {
            UnsubscribeCurrentSession();
            _sessions.Clear();

            if (_sessionManager != null)
            {
                var raw = _sessionManager.GetSessions();
                if (raw != null)
                {
                    foreach (var s in raw)
                    {
                        _sessions.Add(s);
                    }
                }
            }

            if (_sessions.Count == 0 && _initialSession != null)
            {
                _sessions.Add(_initialSession);
            }

            // Auto-select session that is actively playing on initial open
            if (!_isInitialSessionSelected && _sessions.Count > 0)
            {
                int playingIndex = -1;
                for (int i = 0; i < _sessions.Count; i++)
                {
                    try
                    {
                        var pb = _sessions[i].GetPlaybackInfo();
                        if (pb != null && pb.PlaybackStatus == global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        {
                            playingIndex = i;
                            break;
                        }
                    }
                    catch { }
                }

                if (playingIndex >= 0)
                {
                    _selectedSessionIndex = playingIndex;
                }
                _isInitialSessionSelected = true;
            }

            if (_selectedSessionIndex >= _sessions.Count)
            {
                _selectedSessionIndex = Math.Max(0, _sessions.Count - 1);
            }

            SubscribeCurrentSession();
            UpdateDetails();
        }
        catch { }
    }

    private void SubscribeCurrentSession()
    {
        var s = CurrentSession;
        if (s != null)
        {
            s.MediaPropertiesChanged += Session_MediaPropertiesChanged;
            s.PlaybackInfoChanged += Session_PlaybackInfoChanged;
        }
    }

    private void UnsubscribeCurrentSession()
    {
        var s = CurrentSession;
        if (s != null)
        {
            s.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
            s.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
        }
    }

    private void Session_MediaPropertiesChanged(
        global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession sender,
        global::Windows.Media.Control.MediaPropertiesChangedEventArgs args)
    {
        Dispatcher.BeginInvoke(UpdateDetails);
    }

    private void Session_PlaybackInfoChanged(
        global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession sender,
        global::Windows.Media.Control.PlaybackInfoChangedEventArgs args)
    {
        Dispatcher.BeginInvoke(UpdateDetails);
    }

    private string GetAppFriendlyName(global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession? session)
    {
        if (session == null) return "Reproductor";
        try
        {
            string appId = session.SourceAppUserModelId ?? "";
            if (string.IsNullOrEmpty(appId)) return "Reproductor";

            string name = Path.GetFileNameWithoutExtension(appId);
            if (name.Equals("chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
            if (name.Equals("brave", StringComparison.OrdinalIgnoreCase)) return "Brave Browser";
            if (name.Equals("msedge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
            if (name.Equals("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
            if (name.Equals("spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
            if (name.Equals("vlc", StringComparison.OrdinalIgnoreCase)) return "VLC Media Player";
            if (name.Equals("wmplayer", StringComparison.OrdinalIgnoreCase)) return "Windows Media Player";

            if (name.Length > 1) return char.ToUpper(name[0]) + name.Substring(1);
            return name;
        }
        catch { return "Reproductor"; }
    }

    private async void UpdateDetails()
    {
        _updateCts?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _updateCts = cts;
        var token = cts.Token;

        var session = CurrentSession;
        if (session == null)
        {
            SafeClose();
            return;
        }

        if (_sessions.Count > 1)
        {
            string appName = GetAppFriendlyName(session);
            SessionAppText.Text = $"{appName} ({_selectedSessionIndex + 1}/{_sessions.Count})";
            SessionSelectorPanel.Visibility = Visibility.Visible;
        }
        else
        {
            SessionSelectorPanel.Visibility = Visibility.Collapsed;
        }

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (token.IsCancellationRequested) return;

            var playback = session.GetPlaybackInfo();

            if (props == null || string.IsNullOrEmpty(props.Title))
            {
                TitleText.Text = GetAppFriendlyName(session);
                ArtistText.Text = "Reproduciendo audio";
                PlayPauseButton.Content = "\uE769";
                BgImageBorder.Visibility = Visibility.Collapsed;
                return;
            }

            TitleText.Text = props.Title;
            ArtistText.Text = props.Artist;

            // Set play/pause button state (Play: \uE768, Pause: \uE769)
            bool isPlaying = playback != null && playback.PlaybackStatus == global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            PlayPauseButton.Content = isPlaying ? "\uE769" : "\uE768";

            // Load album art thumbnail as background overlay
            if (props.Thumbnail != null)
            {
                try
                {
                    using (var stream = await props.Thumbnail.OpenReadAsync())
                    {
                        if (token.IsCancellationRequested) return;

                        using (var memStream = new MemoryStream())
                        {
                            await stream.AsStreamForRead().CopyToAsync(memStream);
                            if (token.IsCancellationRequested) return;
                            memStream.Position = 0;

                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = memStream;
                            bmp.EndInit();
                            bmp.Freeze();

                            BgImageBrush.ImageSource = bmp;
                            BgImageBorder.Visibility = Visibility.Visible;
                        }
                    }
                }
                catch
                {
                    BgImageBorder.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                BgImageBorder.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            // If failed to read properties for current session, fallback gracefully
        }
    }

    private void PrevSession_Click(object sender, RoutedEventArgs e)
    {
        if (_sessions.Count <= 1) return;
        UnsubscribeCurrentSession();
        _selectedSessionIndex = (_selectedSessionIndex - 1 + _sessions.Count) % _sessions.Count;
        SubscribeCurrentSession();
        UpdateDetails();
    }

    private void NextSession_Click(object sender, RoutedEventArgs e)
    {
        if (_sessions.Count <= 1) return;
        UnsubscribeCurrentSession();
        _selectedSessionIndex = (_selectedSessionIndex + 1) % _sessions.Count;
        SubscribeCurrentSession();
        UpdateDetails();
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_sessions.Count <= 1) return;

        if (e.Delta > 0)
        {
            PrevSession_Click(sender, e);
        }
        else if (e.Delta < 0)
        {
            NextSession_Click(sender, e);
        }
        e.Handled = true;
    }

    private async void Prev_Click(object sender, RoutedEventArgs e)
    {
        var session = CurrentSession;
        if (session != null)
        {
            try { await session.TrySkipPreviousAsync(); } catch { }
        }
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        var session = CurrentSession;
        if (session != null)
        {
            try
            {
                var pb = session.GetPlaybackInfo();
                if (pb != null && pb.PlaybackStatus == global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    await session.TryPauseAsync();
                }
                else
                {
                    await session.TryPlayAsync();
                }
            }
            catch
            {
                try { await session.TryTogglePlayPauseAsync(); } catch { }
            }
        }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        var session = CurrentSession;
        if (session != null)
        {
            try { await session.TrySkipNextAsync(); } catch { }
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        SafeClose();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SafeClose();
        }
    }

    private void SafeClose()
    {
        if (_isClosing) return;
        _isClosing = true;

        bool isBottom = SettingsService.Instance.DockPosition == "Bottom";
        double finalTop;
        if (isBottom)
        {
            finalTop = DockWindow.Instance != null 
                ? DockWindow.Instance.Top - Height 
                : SystemParameters.PrimaryScreenHeight - Height - 56;
        }
        else
        {
            finalTop = DockWindow.Instance != null 
                ? DockWindow.Instance.Top + DockWindow.Instance.ActualHeight 
                : 56;
        }

        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) 
        { 
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } 
        };
        var slide = new System.Windows.Media.Animation.DoubleAnimation(isBottom ? finalTop + 15 : finalTop - 15, TimeSpan.FromMilliseconds(200)) 
        { 
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } 
        };

        fadeOut.Completed += (snd, args) =>
        {
            Close();
        };

        BeginAnimation(OpacityProperty, fadeOut);
        BeginAnimation(TopProperty, slide);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_sessionManager != null)
        {
            _sessionManager.SessionsChanged -= SessionManager_SessionsChanged;
        }
        UnsubscribeCurrentSession();

        base.OnClosed(e);

        // Force GC collection to immediately free the media album art BitmapImages
        GC.Collect();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
    }
}
