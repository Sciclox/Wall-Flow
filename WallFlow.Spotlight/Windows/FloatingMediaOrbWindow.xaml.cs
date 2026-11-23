using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WallFlow.Services;
using WallFlow.Spotlight.Models;
using WallFlow.Spotlight.Services;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WallFlow.Windows;

public partial class FloatingMediaOrbWindow : Window
{
    public static FloatingMediaOrbWindow? Instance { get; private set; }

    private static double? _savedLeft;
    private static double? _savedTop;

    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private DispatcherTimer? _inactivityTimer;
    private DispatcherTimer? _searchDebounceTimer;
    private DispatcherTimer? _progressTimer;
    private DispatcherTimer? _autoCollapseTimer;
    private CancellationTokenSource? _searchCts;

    private bool _isExpanded;
    private bool _isPlaying;
    private bool _isPopoverOpen;
    private bool _isDragging;
    private Point _dragStartScreenPos;
    private Point _dragStartWindowPos;

    private string _currentTitle = "Reproductor";
    private string _currentArtist = "";
    private string _currentTimeStr = "00:00";
    private double _currentProgress = 0;
    private int _queueCount = 1;
    private ImageSource? _currentArtwork;
    private static readonly SolidColorBrush FluorescentProgressBrush = CreateFrozenSolidBrush(Color.FromRgb(255, 106, 0));
    private bool _isRepeatEnabled;
    private bool _isRightDocked;
    private double? _prePopoverTop;
    private DateTime _ignoreDeactivateUntil = DateTime.MinValue;
    private DateTime _lastPopoverToggleTime = DateTime.MinValue;
    private DateTime _lastExpandToggleTime = DateTime.MinValue;
    private DateTime _lastPlayPauseTime = DateTime.MinValue;

    private void RootLayout_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }

    private static SolidColorBrush CreateFrozenSolidBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public FloatingMediaOrbWindow()
    {
        InitializeComponent();
        Instance = this;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            int exStyle = WallFlow.Native.NativeMethods.GetWindowLong(helper.Handle, WallFlow.Native.NativeMethods.GWL_EXSTYLE);
            // WS_EX_TOOLWINDOW hides from Alt+Tab, WS_EX_NOACTIVATE prevents stealing focus from other windows
            WallFlow.Native.NativeMethods.SetWindowLong(
                helper.Handle,
                WallFlow.Native.NativeMethods.GWL_EXSTYLE,
                (exStyle & ~WallFlow.Native.NativeMethods.WS_EX_APPWINDOW) |
                WallFlow.Native.NativeMethods.WS_EX_TOOLWINDOW |
                WallFlow.Native.NativeMethods.WS_EX_NOACTIVATE);

            var source = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProc);
        }
        catch { }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_MAXIMIZE = 0xF030;
        const int SC_RESTORE = 0xF120;

        if (msg == WM_SYSCOMMAND)
        {
            int command = (wParam.ToInt32() & 0xFFF0);
            if (command == SC_MAXIMIZE || command == SC_RESTORE)
            {
                handled = true;
                return IntPtr.Zero;
            }
        }
        return IntPtr.Zero;
    }

    public static void EnsureInitialized()
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            if (Instance == null)
            {
                var orb = new FloatingMediaOrbWindow
                {
                    Opacity = 0,
                    Visibility = Visibility.Collapsed
                };
                orb.Show();
            }
        });
    }

    private static bool _isExplicitlyHidden;

    public static void ShowOrb(string title, string artist, ImageSource? artwork, bool isPlaying, GlobalSystemMediaTransportControlsSession? session = null)
    {
        _isExplicitlyHidden = false;
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            if (Instance == null || !Instance.IsLoaded)
            {
                var orb = new FloatingMediaOrbWindow();
                orb.Show();
                orb.ApplyMediaState(title, artist, artwork, isPlaying, session);
            }
            else
            {
                Instance.ApplyMediaState(title, artist, artwork, isPlaying, session);
                Instance.ShowWithFade();
            }
        });
    }

    public static void HideOrb()
    {
        _isExplicitlyHidden = true;
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            Instance?.HideWithFade();
        });
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Normal;

        // Position window on screen
        if (_savedLeft.HasValue && _savedTop.HasValue)
        {
            Left = _savedLeft.Value;
            Top = _savedTop.Value;
        }
        else
        {
            Left = (SystemParameters.WorkArea.Width - Width) / 2;
            Top = 24;
        }

        UpdateDockAlignment();
        InitSessionMonitoring();
        ResetInactivityTimer();
        if (!_isExplicitlyHidden &&
            _isPlaying && !string.IsNullOrWhiteSpace(_currentTitle) && _currentTitle != "Reproductor")
        {
            ShowWithFade();
        }
        else
        {
            Visibility = Visibility.Collapsed;
            Opacity = 0;
        }
    }

    public void ApplyMediaState(string title, string artist, ImageSource? artwork, bool isPlaying, GlobalSystemMediaTransportControlsSession? session = null, string? time = null, double progress = 0)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            var firstLine = title.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            _currentTitle = string.IsNullOrWhiteSpace(firstLine) ? "Reproductor" : firstLine.Trim();
        }
        else
        {
            _currentTitle = "Reproductor";
        }

        _currentArtist = string.IsNullOrWhiteSpace(artist) || artist.Equals("En reproducción", StringComparison.OrdinalIgnoreCase)
            ? "YouTube Music"
            : artist.Trim();
        _currentArtwork = artwork;
        _isPlaying = isPlaying;
        if (!string.IsNullOrEmpty(time)) _currentTimeStr = time;
        if (progress > 0) _currentProgress = progress;
        if (session != null) _currentSession = session;

        UpdateUiElements();

        // Reveal the floating button immediately as soon as a real track is playing
        if (!_isExplicitlyHidden)
        {
            if (_isPlaying && !string.IsNullOrWhiteSpace(_currentTitle) && _currentTitle != "Reproductor")
            {
                if (Visibility != Visibility.Visible || Opacity < 0.1)
                {
                    ShowWithFade();
                }
            }
        }
    }

    private void UpdateUiElements()
    {
        TxtTrackTitle.Text = _currentTitle;
        if (TxtTrackArtist != null)
        {
            string artistText = string.IsNullOrWhiteSpace(_currentArtist) || _currentArtist == "En reproducción"
                ? "YouTube Music"
                : _currentArtist;
            TxtTrackArtist.Text = artistText;
        }
        TxtPlayPauseGlyph.Text = _isPlaying ? "\uE769" : "\uE768";

        UpdateProgressArc(_currentProgress);

        if (_currentArtwork != null)
        {
            ImgArtworkBrush.ImageSource = _currentArtwork;
            VinylCenterArtwork.Visibility = Visibility.Visible;
            FallbackIconGrid.Visibility = Visibility.Collapsed;
        }
        else
        {
            VinylCenterArtwork.Visibility = Visibility.Collapsed;
            FallbackIconGrid.Visibility = Visibility.Visible;
        }

        if (_isExpanded && !_isPopoverOpen && !_isDragging)
        {
            AdjustPillWidthToCurrentTrack();
        }
    }

    private void UpdateProgressArc(double progress)
    {
        try
        {
            double p = Math.Clamp(progress, 0.0, 1.0);
            if (p <= 0.005)
            {
                SquircleProgressArc.Data = null;
                return;
            }

            // Outer squircle perimeter bounds: 64x64 with CornerRadius=16
            double w = 64.0;
            double h = 64.0;
            double r = 16.0;

            if (p >= 0.999)
            {
                SquircleProgressArc.Data = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
                SquircleProgressArc.Stroke = FluorescentProgressBrush;
                return;
            }

            var (geom, _, _) = BuildSquircleProgress(w, h, r, p);
            SquircleProgressArc.Data = geom;
            SquircleProgressArc.Stroke = FluorescentProgressBrush;
        }
        catch { }
    }

    private (PathGeometry geom, Point tip, double angleDeg) BuildSquircleProgress(double w, double h, double r, double p)
    {
        double straightHalfTop = (w / 2.0) - r;
        double arcLen = (Math.PI / 2.0) * r;
        double straightSide = h - (2.0 * r);
        double straightBottom = w - (2.0 * r);

        double[] segLengths = new double[]
        {
            straightHalfTop, // 0: top center to top-right corner start
            arcLen,          // 1: top-right corner arc
            straightSide,    // 2: right edge
            arcLen,          // 3: bottom-right corner arc
            straightBottom,  // 4: bottom edge
            arcLen,          // 5: bottom-left corner arc
            straightSide,    // 6: left edge
            arcLen,          // 7: top-left corner arc
            straightHalfTop  // 8: top-left corner to top center
        };

        double totalLen = 0;
        for (int i = 0; i < segLengths.Length; i++) totalLen += segLengths[i];

        double targetDist = p * totalLen;

        Point startPoint = new Point(w / 2.0, 0);
        var figure = new PathFigure
        {
            StartPoint = startPoint,
            IsClosed = false,
            IsFilled = false
        };

        Point currentTip = startPoint;
        double tangentAngle = 0;
        double distRemaining = targetDist;

        for (int seg = 0; seg < 9; seg++)
        {
            double len = segLengths[seg];
            if (len <= 0) continue;

            if (distRemaining >= len)
            {
                distRemaining -= len;
                switch (seg)
                {
                    case 0:
                        currentTip = new Point(w - r, 0);
                        figure.Segments.Add(new LineSegment(currentTip, true));
                        tangentAngle = 0;
                        break;
                    case 1:
                        currentTip = new Point(w, r);
                        figure.Segments.Add(new ArcSegment(currentTip, new Size(r, r), 0, false, SweepDirection.Clockwise, true));
                        tangentAngle = 90;
                        break;
                    case 2:
                        currentTip = new Point(w, h - r);
                        figure.Segments.Add(new LineSegment(currentTip, true));
                        tangentAngle = 90;
                        break;
                    case 3:
                        currentTip = new Point(w - r, h);
                        figure.Segments.Add(new ArcSegment(currentTip, new Size(r, r), 0, false, SweepDirection.Clockwise, true));
                        tangentAngle = 180;
                        break;
                    case 4:
                        currentTip = new Point(r, h);
                        figure.Segments.Add(new LineSegment(currentTip, true));
                        tangentAngle = 180;
                        break;
                    case 5:
                        currentTip = new Point(0, h - r);
                        figure.Segments.Add(new ArcSegment(currentTip, new Size(r, r), 0, false, SweepDirection.Clockwise, true));
                        tangentAngle = 270;
                        break;
                    case 6:
                        currentTip = new Point(0, r);
                        figure.Segments.Add(new LineSegment(currentTip, true));
                        tangentAngle = 270;
                        break;
                    case 7:
                        currentTip = new Point(r, 0);
                        figure.Segments.Add(new ArcSegment(currentTip, new Size(r, r), 0, false, SweepDirection.Clockwise, true));
                        tangentAngle = 0;
                        break;
                    case 8:
                        currentTip = new Point(w / 2.0, 0);
                        figure.Segments.Add(new LineSegment(currentTip, true));
                        tangentAngle = 0;
                        break;
                }
                if (distRemaining <= 0.001) break;
            }
            else
            {
                double frac = Math.Clamp(distRemaining / len, 0.0, 1.0);
                switch (seg)
                {
                    case 0:
                        currentTip = new Point((w / 2.0) + (frac * straightHalfTop), 0);
                        figure.Segments.Add(new LineSegment(currentTip, true));
                        tangentAngle = 0;
                        break;
                    case 1:
                        {
                            double a = -Math.PI / 2.0 + (frac * (Math.PI / 2.0));
                            currentTip = new Point((w - r) + (r * Math.Cos(a)), r + (r * Math.Sin(a)));
                            figure.Segments.Add(new ArcSegment(currentTip, new Size(r, r), 0, false, SweepDirection.Clockwise, true));
                            tangentAngle = (a + (Math.PI / 2.0)) * 180.0 / Math.PI;
                        }
                        break;
                    case 2:
                        currentTip = new Point(w, r + (frac * straightSide));
                        figure.Segments.Add(new LineSegment(currentTip, true));
                        tangentAngle = 90;
                        break;
                    case 3:
                        {
                            double a = frac * (Math.PI / 2.0);
                            currentTip = new Point((w - r) + (r * Math.Cos(a)), (h - r) + (r * Math.Sin(a)));
                            figure.Segments.Add(new ArcSegment(currentTip, new Size(r, r), 0, false, SweepDirection.Clockwise, true));
                            tangentAngle = 90 + (a * 180.0 / Math.PI);
                        }
                        break;
                    case 4:
                        currentTip = new Point((w - r) - (frac * straightBottom), h);
                        figure.Segments.Add(new LineSegment(currentTip, true));
                        tangentAngle = 180;
                        break;
                    case 5:
                        {
                            double a = (Math.PI / 2.0) + (frac * (Math.PI / 2.0));
                            currentTip = new Point(r + (r * Math.Cos(a)), (h - r) + (r * Math.Sin(a)));
                            figure.Segments.Add(new ArcSegment(currentTip, new Size(r, r), 0, false, SweepDirection.Clockwise, true));
                            tangentAngle = 180 + ((a - Math.PI / 2.0) * 180.0 / Math.PI);
                        }
                        break;
                    case 6:
                        currentTip = new Point(0, (h - r) - (frac * straightSide));
                        figure.Segments.Add(new LineSegment(currentTip, true));
                        tangentAngle = 270;
                        break;
                    case 7:
                        {
                            double a = Math.PI + (frac * (Math.PI / 2.0));
                            currentTip = new Point(r + (r * Math.Cos(a)), r + (r * Math.Sin(a)));
                            figure.Segments.Add(new ArcSegment(currentTip, new Size(r, r), 0, false, SweepDirection.Clockwise, true));
                            tangentAngle = 270 + ((a - Math.PI) * 180.0 / Math.PI);
                        }
                        break;
                    case 8:
                        currentTip = new Point(r + (frac * straightHalfTop), 0);
                        figure.Segments.Add(new LineSegment(currentTip, true));
                        tangentAngle = 0;
                        break;
                }
                break;
            }
        }

        var geom = new PathGeometry();
        geom.Figures.Add(figure);
        return (geom, currentTip, tangentAngle);
    }

    private async void InitSessionMonitoring()
    {
        try
        {
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_sessionManager != null)
            {
                _sessionManager.CurrentSessionChanged += SessionManager_CurrentSessionChanged;
                HookCurrentSession(_sessionManager.GetCurrentSession());
            }
        }
        catch { }

        // Also hook WallFlow internal music state changes
        if (ApplicationHost.Instance != null)
        {
            ApplicationHost.Instance.InternalMusicStateChanged += Host_InternalMusicStateChanged;
        }

        // Live smooth progress ring ticker
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _progressTimer.Tick += (s, e) =>
        {
            if (!_isPlaying) return;

            if (ApplicationHost.Instance != null && ApplicationHost.Instance.IsInternalMusicActive)
            {
                var t = ApplicationHost.Instance.InternalMusicTime;
                if (!string.IsNullOrEmpty(t))
                {
                    _currentTimeStr = t;
                }
                double prog = ApplicationHost.Instance.InternalMusicProgress;
                if (prog >= 0)
                {
                    _currentProgress = prog;
                    UpdateProgressArc(_currentProgress);
                }
            }
            else if (_currentSession != null)
            {
                try
                {
                    var tl = _currentSession.GetTimelineProperties();
                    if (tl != null && tl.EndTime.TotalSeconds > 0)
                    {
                        _currentProgress = tl.Position.TotalSeconds / tl.EndTime.TotalSeconds;
                        int curM = (int)tl.Position.TotalMinutes;
                        int curS = tl.Position.Seconds;
                        _currentTimeStr = $"{curM:D2}:{curS:D2}";
                        UpdateProgressArc(_currentProgress);
                    }
                }
                catch { }
            }

            if (_isRepeatEnabled && _currentProgress >= 0.995)
            {
                _currentProgress = 0;
                UpdateProgressArc(0);
                if (_currentSession != null)
                {
                    _ = _currentSession.TryChangePlaybackPositionAsync(0);
                    _ = _currentSession.TryPlayAsync();
                }
            }
        };
        _progressTimer.Start();
    }

    private void Host_InternalMusicStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(RefreshFromInternalHost);
    }

    private void RefreshFromInternalHost()
    {
        if (ApplicationHost.Instance != null && ApplicationHost.Instance.IsInternalMusicActive)
        {
            var title = ApplicationHost.Instance.InternalMusicTitle ?? "YouTube Music";
            var artist = ApplicationHost.Instance.InternalMusicArtist ?? "";
            var art = ApplicationHost.Instance.InternalMusicArtwork;
            var playing = ApplicationHost.Instance.IsInternalMusicPlaying;
            var time = ApplicationHost.Instance.InternalMusicTime ?? "00:00";
            var progress = ApplicationHost.Instance.InternalMusicProgress;
            ApplyMediaState(title, artist, art, playing, session: null, time: time, progress: progress);

            if (_isRepeatEnabled != ApplicationHost.Instance.IsInternalMusicRepeat)
            {
                _isRepeatEnabled = ApplicationHost.Instance.IsInternalMusicRepeat;
                UpdateRepeatUi();
            }
        }
    }

    private void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        Dispatcher.InvokeAsync(() =>
        {
            HookCurrentSession(sender.GetCurrentSession());
            UpdateFromCurrentSession();
        });
    }

    private void HookCurrentSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_currentSession != null)
        {
            try
            {
                _currentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
            }
            catch { }
        }

        _currentSession = session;
        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged += CurrentSession_MediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += CurrentSession_PlaybackInfoChanged;
        }
    }

    private void CurrentSession_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        Dispatcher.InvokeAsync(UpdateFromCurrentSession);
    }

    private void CurrentSession_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        Dispatcher.InvokeAsync(UpdateFromCurrentSession);
    }

    private async void UpdateFromCurrentSession()
    {
        try
        {
            if (ApplicationHost.Instance != null && ApplicationHost.Instance.IsInternalMusicActive)
            {
                RefreshFromInternalHost();
                return;
            }

            if (_currentSession == null) return;

            var props = await _currentSession.TryGetMediaPropertiesAsync();
            var pbInfo = _currentSession.GetPlaybackInfo();
            bool isPlaying = pbInfo != null && pbInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            if (props != null && !string.IsNullOrEmpty(props.Title))
            {
                string cleanTitle = props.Title.Trim();
                if (cleanTitle.EndsWith(" - YouTube", StringComparison.OrdinalIgnoreCase))
                    cleanTitle = cleanTitle.Substring(0, cleanTitle.Length - " - YouTube".Length).Trim();
                else if (cleanTitle.EndsWith(" - YouTube Music", StringComparison.OrdinalIgnoreCase))
                    cleanTitle = cleanTitle.Substring(0, cleanTitle.Length - " - YouTube Music".Length).Trim();

                string cleanArtist = props.Artist?.Trim() ?? "";
                if (cleanArtist.Contains("vistas", StringComparison.OrdinalIgnoreCase) ||
                    cleanArtist.Contains("views", StringComparison.OrdinalIgnoreCase) ||
                    cleanArtist.Contains("suscriptor", StringComparison.OrdinalIgnoreCase) ||
                    cleanArtist.StartsWith("•"))
                {
                    cleanArtist = "";
                }

                BitmapImage? artwork = null;
                if (props.Thumbnail != null)
                {
                    try
                    {
                        using var stream = await props.Thumbnail.OpenReadAsync();
                        using var memStream = new MemoryStream();
                        await stream.AsStreamForRead().CopyToAsync(memStream);
                        memStream.Position = 0;
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = memStream;
                        bmp.EndInit();
                        bmp.Freeze();
                        artwork = bmp;
                    }
                    catch { }
                }

                ApplyMediaState(cleanTitle, cleanArtist, artwork, isPlaying, _currentSession);

                if (pbInfo?.AutoRepeatMode != null)
                {
                    bool repeatActive = pbInfo.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.Track ||
                                        pbInfo.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.List;
                    if (_isRepeatEnabled != repeatActive)
                    {
                        _isRepeatEnabled = repeatActive;
                        UpdateRepeatUi();
                    }
                }
            }
        }
        catch { }
    }

    private void ResetInactivityTimer()
    {
        _inactivityTimer?.Stop();
        _inactivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _inactivityTimer.Tick += (s, e) =>
        {
            if (!_isPlaying && !_isPopoverOpen)
            {
                HideOrb();
            }
        };
        _inactivityTimer.Start();
    }

    public void ShowWithFade()
    {
        if (_isExplicitlyHidden)
        {
            return;
        }

        Topmost = true;
        if (Visibility == Visibility.Visible && Opacity >= 0.98) return;

        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, null);
        var anim = new DoubleAnimation(Math.Max(0.0, Opacity), 1.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, anim);
    }

    public void HideWithFade()
    {
        if (Visibility != Visibility.Visible || Opacity <= 0.02)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        ClosePopover();
        var anim = new DoubleAnimation(Opacity, 0.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (s, e) =>
        {
            Visibility = Visibility.Collapsed;
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private void PillBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && FindVisualParent<Button>(dep) != null) return;
        _isDragging = false;
        try
        {
            _dragStartScreenPos = PointToScreen(e.GetPosition(this));
            _dragStartWindowPos = new Point(Left, Top);
            PillBorder.CaptureMouse();
        }
        catch { }
    }

    private void PillBorder_MouseMove(object sender, MouseEventArgs e)
    {
        if (PillBorder.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            Point currentScreenPos;
            try
            {
                currentScreenPos = PointToScreen(e.GetPosition(this));
            }
            catch
            {
                return;
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            double diffX = (currentScreenPos.X - _dragStartScreenPos.X) / (dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0);
            double diffY = (currentScreenPos.Y - _dragStartScreenPos.Y) / (dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0);

            if (!_isDragging && (Math.Abs(diffX) > 4 || Math.Abs(diffY) > 4))
            {
                _isDragging = true;
            }

            if (_isDragging)
            {
                double screenWidth = SystemParameters.WorkArea.Width;
                double screenHeight = SystemParameters.WorkArea.Height;

                double targetLeft = _dragStartWindowPos.X + diffX;
                double targetTop = _dragStartWindowPos.Y + diffY;

                // Clamp to screen boundaries without triggering Aero Snap
                targetLeft = Math.Clamp(targetLeft, 6, screenWidth - Width - 6);
                targetTop = Math.Clamp(targetTop, 6, screenHeight - Height - 6);

                Left = targetLeft;
                Top = targetTop;
            }
        }
    }

    private void PillBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (PillBorder.IsMouseCaptured)
        {
            PillBorder.ReleaseMouseCapture();
        }

        if (e.OriginalSource is DependencyObject dep && FindVisualParent<Button>(dep) != null) return;

        if (_isDragging)
        {
            UpdateDockAlignment();
            _isDragging = false;
            return;
        }
        _isDragging = false;

        if ((DateTime.UtcNow - _lastExpandToggleTime).TotalMilliseconds < 350)
        {
            return;
        }
        _lastExpandToggleTime = DateTime.UtcNow;

        // 1-Click to toggle between Micro Dial and Pill Player
        if (!_isExpanded)
        {
            SoundService.Play(SoundType.Click);
            ExpandToPill();
        }
        else if (!_isPopoverOpen)
        {
            SoundService.Play(SoundType.Click);
            CollapseToOrb();
        }
    }

    private void UpdateDockAlignment()
    {
        double screenWidth = SystemParameters.WorkArea.Width;
        double screenHeight = SystemParameters.WorkArea.Height;

        if (Top < 10) Top = 10;
        if (Top + Height > screenHeight - 10) Top = Math.Max(10, screenHeight - Height - 10);

        // Check if placed on right half where dynamic pill expansion would exceed screen width
        double orbRestLeft = _isExpanded && _isRightDocked ? (Left + Width - 96) : Left;
        _isRightDocked = (orbRestLeft + 420 > screenWidth - 15);

        if (_isRightDocked)
        {
            if (Left + Width > screenWidth - 10)
            {
                Left = Math.Max(10, screenWidth - Width - 10);
            }
            _savedLeft = _isExpanded ? (Left + Width - 96) : Left;
        }
        else
        {
            if (Left < 10) Left = 10;
            _savedLeft = Left;
        }

        _savedTop = Top;
        ApplyRightDockedLayout(_isRightDocked);
    }

    private void ApplyRightDockedLayout(bool rightDocked)
    {
        if (rightDocked)
        {
            MainStack.HorizontalAlignment = HorizontalAlignment.Right;
            PillBorder.HorizontalAlignment = HorizontalAlignment.Right;
            PopoverCard.HorizontalAlignment = HorizontalAlignment.Right;

            PillCol0.Width = new GridLength(1, GridUnitType.Star);
            PillCol1.Width = new GridLength(76);
            Grid.SetColumn(VinylColumnGrid, 1);
            Grid.SetColumn(PillExpandedContent, 0);

            PillExpandedContent.Margin = new Thickness(10, 0, 4, 0);

            // In right-docked mode: Column 0 is Controls (Auto) so buttons NEVER compress; Column 1 is Title (Star)
            PillTextCol.Width = new GridLength(GridLength.Auto.Value, GridUnitType.Auto);
            PillControlsCol.Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(PillControlsStack, 0);
            Grid.SetColumn(TrackTitleGrid, 1);
            TrackTitleGrid.Margin = new Thickness(8, 0, 8, 0);
            if (TxtTrackTitle != null) TxtTrackTitle.MaxWidth = 260;
            if (TxtTrackArtist != null) TxtTrackArtist.MaxWidth = 260;

            // Reorder controls so BtnTogglePlaylist is at the far left edge when opening to the left!
            if (PillControlsStack != null && BtnTogglePlaylist != null && BtnRepeat != null)
            {
                PillControlsStack.Children.Clear();
                PillControlsStack.Children.Add(BtnTogglePlaylist);
                PillControlsStack.Children.Add(BtnRepeat);
                PillControlsStack.Children.Add(BtnPrev);
                PillControlsStack.Children.Add(BtnPlayPause);
                PillControlsStack.Children.Add(BtnNext);

                BtnTogglePlaylist.Margin = new Thickness(0, 0, 2, 0);
                BtnRepeat.Margin = new Thickness(2, 0, 2, 0);
            }
        }
        else
        {
            MainStack.HorizontalAlignment = HorizontalAlignment.Left;
            PillBorder.HorizontalAlignment = HorizontalAlignment.Left;
            PopoverCard.HorizontalAlignment = HorizontalAlignment.Left;

            PillCol0.Width = new GridLength(76);
            PillCol1.Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(VinylColumnGrid, 0);
            Grid.SetColumn(PillExpandedContent, 1);

            PillExpandedContent.Margin = new Thickness(4, 0, 10, 0);

            // In left-docked mode: Column 0 is Title (Star); Column 1 is Controls (Auto)
            PillTextCol.Width = new GridLength(1, GridUnitType.Star);
            PillControlsCol.Width = new GridLength(GridLength.Auto.Value, GridUnitType.Auto);
            Grid.SetColumn(TrackTitleGrid, 0);
            Grid.SetColumn(PillControlsStack, 1);
            TrackTitleGrid.Margin = new Thickness(6, 0, 8, 0);
            if (TxtTrackTitle != null) TxtTrackTitle.MaxWidth = 280;
            if (TxtTrackArtist != null) TxtTrackArtist.MaxWidth = 280;

            // Normal order: Repeat -> Prev -> Play/Pause -> Next -> Playlist Chevron (far right)
            if (PillControlsStack != null && BtnTogglePlaylist != null && BtnRepeat != null)
            {
                PillControlsStack.Children.Clear();
                PillControlsStack.Children.Add(BtnRepeat);
                PillControlsStack.Children.Add(BtnPrev);
                PillControlsStack.Children.Add(BtnPlayPause);
                PillControlsStack.Children.Add(BtnNext);
                PillControlsStack.Children.Add(BtnTogglePlaylist);

                BtnRepeat.Margin = new Thickness(0, 0, 2, 0);
                BtnTogglePlaylist.Margin = new Thickness(2, 0, 0, 0);
            }
        }
    }

    private void MainStack_MouseEnter(object sender, MouseEventArgs e)
    {
        _autoCollapseTimer?.Stop();
    }

    private void MainStack_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isExpanded || _isDragging || _isPopoverOpen) return;

        _autoCollapseTimer?.Stop();
        _autoCollapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _autoCollapseTimer.Tick += (s, args) =>
        {
            _autoCollapseTimer.Stop();
            if (!MainStack.IsMouseOver && !IsMouseOver && !_isPopoverOpen && !_isDragging)
            {
                CollapseToOrb();
            }
        };
        _autoCollapseTimer.Start();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow < _ignoreDeactivateUntil) return;
        if (IsMouseOver || MainStack.IsMouseOver || (_isPopoverOpen && PopoverCard.IsMouseOver)) return;

        if (_isPopoverOpen)
        {
            ClosePopover();
        }
        if (_isExpanded && !_isDragging)
        {
            CollapseToOrb();
        }
    }

    private double ComputeTargetPillWidth()
    {
        if (_isPopoverOpen) return 430;

        try
        {
            TxtTrackTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            TxtTrackArtist.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double titleW = TxtTrackTitle.DesiredSize.Width;
            double artistW = TxtTrackArtist.DesiredSize.Width;
            double textW = Math.Max(titleW, artistW);

            // Clamp text width between 55px and 220px
            double clampedText = Math.Max(55, Math.Min(textW, 220));

            // Fixed non-text overhead:
            // Artwork (76) + content margins (14) + controls (148) + title margins (16) + breathing room (10) = 264
            double target = 264 + clampedText;

            // Overall pill bounds: min 320px, max 480px
            return Math.Max(320, Math.Min(480, Math.Round(target)));
        }
        catch
        {
            return 380;
        }
    }

    private void AdjustPillWidthToCurrentTrack()
    {
        if (!_isExpanded || _isPopoverOpen || _isDragging) return;

        double newTarget = ComputeTargetPillWidth();
        if (Math.Abs(PillBorder.ActualWidth - newTarget) > 8)
        {
            double windowTargetWidth = newTarget + 24;
            if (windowTargetWidth > Width)
            {
                Width = windowTargetWidth;
                if (_isRightDocked)
                {
                    double rightEdge = _savedLeft.HasValue ? (_savedLeft.Value + 96) : (Left + Width);
                    Left = Math.Max(10, rightEdge - Width);
                }
            }

            var animWidth = new DoubleAnimation(PillBorder.ActualWidth, newTarget, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            animWidth.Completed += (s, e) =>
            {
                if (_isExpanded && !_isPopoverOpen)
                {
                    Width = newTarget + 24;
                    if (_isRightDocked)
                    {
                        double rightEdge = _savedLeft.HasValue ? (_savedLeft.Value + 96) : (Left + Width);
                        Left = Math.Max(10, rightEdge - Width);
                    }
                }
            };
            PillBorder.BeginAnimation(WidthProperty, animWidth);
        }
    }

    private void ExpandToPill()
    {
        if (_isExpanded) return;
        _isExpanded = true;

        double screenWidth = SystemParameters.WorkArea.Width;
        double orbLeft = _savedLeft ?? Left;
        if (!_savedLeft.HasValue) _savedLeft = orbLeft;

        double targetPillWidth = _isPopoverOpen ? 430 : ComputeTargetPillWidth();
        double windowTargetWidth = Math.Max(targetPillWidth + 24, _isPopoverOpen ? 454 : targetPillWidth + 24);

        _isRightDocked = (orbLeft + windowTargetWidth > screenWidth - 15);
        ApplyRightDockedLayout(_isRightDocked);

        Width = windowTargetWidth;
        if (_isRightDocked)
        {
            double rightEdge = orbLeft + 96;
            Left = Math.Max(10, rightEdge - Width);
        }
        else
        {
            Left = Math.Max(10, orbLeft);
        }

        PillExpandedContent.Visibility = Visibility.Visible;

        var animWidth = new DoubleAnimation(76, targetPillWidth, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        PillBorder.BeginAnimation(WidthProperty, animWidth);

        var animContent = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            BeginTime = TimeSpan.FromMilliseconds(50)
        };
        PillExpandedContent.BeginAnimation(OpacityProperty, animContent);
    }

    private void CollapseToOrb()
    {
        if (!_isExpanded) return;
        if (_isPopoverOpen) ClosePopover();
        _isExpanded = false;

        var animContent = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
        animContent.Completed += (s, e) =>
        {
            if (!_isExpanded)
            {
                PillExpandedContent.Visibility = Visibility.Collapsed;
            }
        };
        PillExpandedContent.BeginAnimation(OpacityProperty, animContent);

        var animWidth = new DoubleAnimation(PillBorder.ActualWidth, 76, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        animWidth.Completed += (s, e) =>
        {
            if (!_isExpanded && !_isPopoverOpen)
            {
                Width = 96;
                Height = 96;
                if (_isRightDocked && _savedLeft.HasValue)
                {
                    Left = _savedLeft.Value;
                }
            }
        };
        PillBorder.BeginAnimation(WidthProperty, animWidth);
    }

    private void BtnTogglePlaylist_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((DateTime.UtcNow - _lastPopoverToggleTime).TotalMilliseconds < 350)
        {
            return;
        }
        _lastPopoverToggleTime = DateTime.UtcNow;

        SoundService.Play(SoundType.Click);
        TogglePopover();
    }

    private void TogglePopover()
    {
        if (_isPopoverOpen)
        {
            ClosePopover();
        }
        else
        {
            OpenPopover();
        }
    }

    private void SetWindowActivationEnabled(bool enabled)
    {
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            if (helper.Handle == IntPtr.Zero) return;

            int exStyle = WallFlow.Native.NativeMethods.GetWindowLong(helper.Handle, WallFlow.Native.NativeMethods.GWL_EXSTYLE);
            int newStyle = enabled
                ? (exStyle & ~WallFlow.Native.NativeMethods.WS_EX_NOACTIVATE)
                : (exStyle | WallFlow.Native.NativeMethods.WS_EX_NOACTIVATE);

            if (newStyle != exStyle)
            {
                WallFlow.Native.NativeMethods.SetWindowLong(helper.Handle, WallFlow.Native.NativeMethods.GWL_EXSTYLE, newStyle);
                WallFlow.Native.NativeMethods.SetWindowPos(helper.Handle, IntPtr.Zero, 0, 0, 0, 0,
                    WallFlow.Native.NativeMethods.SWP_NOMOVE | WallFlow.Native.NativeMethods.SWP_NOSIZE | WallFlow.Native.NativeMethods.SWP_NOZORDER | 0x0020);
            }

            if (enabled)
            {
                WallFlow.Native.NativeMethods.SetForegroundWindow(helper.Handle);
            }
        }
        catch { }
    }

    private void OpenPopover()
    {
        _ignoreDeactivateUntil = DateTime.UtcNow.AddMilliseconds(800);
        _isPopoverOpen = true;
        TxtChevronGlyph.Text = "\uE70E"; // Chevron Up

        if (!_isExpanded)
        {
            ExpandToPill();
        }
        else
        {
            var animPill = new DoubleAnimation(PillBorder.ActualWidth, 430, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            PillBorder.BeginAnimation(WidthProperty, animPill);
        }

        Width = 454;

        _prePopoverTop = Top;

        double screenHeight = SystemParameters.WorkArea.Height;
        double screenWidth = SystemParameters.WorkArea.Width;
        double workAreaTop = SystemParameters.WorkArea.Top;

        // Dynamically compute safe height so it fits comfortably on screen without clipping
        double desiredHeight = 610;
        if (desiredHeight > screenHeight - 20)
        {
            desiredHeight = Math.Max(420, screenHeight - 20);
        }
        Height = desiredHeight;

        // Position window cleanly within safe work area margins (never cut off top or bottom)
        if (Top + Height > workAreaTop + screenHeight - 12)
        {
            Top = Math.Max(workAreaTop + 14, (workAreaTop + screenHeight) - Height - 12);
        }
        else if (Top < workAreaTop + 14)
        {
            Top = workAreaTop + 14;
        }

        if (_isRightDocked)
        {
            double rightEdge = _savedLeft.HasValue ? (_savedLeft.Value + 96) : (Left + Width);
            Left = Math.Max(10, rightEdge - Width);
        }
        else
        {
            if (Left + Width > screenWidth - 10)
            {
                Left = Math.Max(10, screenWidth - Width - 10);
            }
        }

        PopoverCard.Visibility = Visibility.Visible;
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        PopoverCard.BeginAnimation(OpacityProperty, anim);

        UpdateUiElements();
        TxtSearchBox.Text = string.Empty;

        // Enable Windows activation and keyboard focus for immediate typing!
        SetWindowActivationEnabled(true);
        Activate();

        Dispatcher.InvokeAsync(() =>
        {
            TxtSearchBox.Focus();
            Keyboard.Focus(TxtSearchBox);
        }, System.Windows.Threading.DispatcherPriority.Input);

        // Automatically load the playlist/queue on open
        _ = LoadPlaylistAsync();
    }

    private void ClosePopover()
    {
        if (!_isPopoverOpen) return;
        _isPopoverOpen = false;
        TxtChevronGlyph.Text = "\uE70D"; // Chevron Down

        SetWindowActivationEnabled(false);

        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
        anim.Completed += (s, e) =>
        {
            PopoverCard.Visibility = Visibility.Collapsed;
            Height = 96;
            if (_prePopoverTop.HasValue)
            {
                Top = _prePopoverTop.Value;
                _prePopoverTop = null;
            }
            if (!MainStack.IsMouseOver && !IsMouseOver && !_isDragging)
            {
                CollapseToOrb();
            }
            else if (_isExpanded)
            {
                double compactPillWidth = ComputeTargetPillWidth();
                double compactWindowWidth = compactPillWidth + 24;

                var animPill = new DoubleAnimation(PillBorder.ActualWidth, compactPillWidth, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                animPill.Completed += (s2, e2) =>
                {
                    if (_isExpanded && !_isPopoverOpen)
                    {
                        Width = compactWindowWidth;
                        if (_isRightDocked)
                        {
                            double rightEdge = _savedLeft.HasValue ? (_savedLeft.Value + 96) : (Left + Width);
                            Left = Math.Max(10, rightEdge - Width);
                        }
                    }
                };
                PillBorder.BeginAnimation(WidthProperty, animPill);
            }
        };
        PopoverCard.BeginAnimation(OpacityProperty, anim);
    }

    private void BtnClosePopup_Click(object sender, RoutedEventArgs e)
    {
        SoundService.Play(SoundType.Click);
        ClosePopover();
    }

    private async void BtnRepeat_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SoundService.Play(SoundType.Click);
        await ToggleRepeatAsync();
    }

    private async Task ToggleRepeatAsync()
    {
        _isRepeatEnabled = !_isRepeatEnabled;
        UpdateRepeatUi();

        try
        {
            if (ApplicationHost.Instance != null && ApplicationHost.Instance.IsInternalMusicActive)
            {
                await ApplicationHost.Instance.ToggleRepeatMusicAsync(_isRepeatEnabled);
            }
            else if (_currentSession != null)
            {
                var mode = _isRepeatEnabled
                    ? global::Windows.Media.MediaPlaybackAutoRepeatMode.Track
                    : global::Windows.Media.MediaPlaybackAutoRepeatMode.None;
                await _currentSession.TryChangeAutoRepeatModeAsync(mode);
            }
        }
        catch { }
    }

    private void UpdateRepeatUi()
    {
        if (BtnRepeat == null || TxtRepeatGlyph == null) return;

        if (_isRepeatEnabled)
        {
            BtnRepeat.Opacity = 1.0;
            TxtRepeatGlyph.Foreground = FluorescentProgressBrush;
            TxtRepeatGlyph.Text = "\uE8EE"; // Continuous loop arrows (indefinite repeat)
            BtnRepeat.ToolTip = "Bucle indefinido: Activado (repite esta canción sin fin)";
        }
        else
        {
            BtnRepeat.Opacity = 0.5;
            TxtRepeatGlyph.Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            TxtRepeatGlyph.Text = "\uE8EE"; // Continuous loop arrows
            BtnRepeat.ToolTip = "Bucle indefinido: Desactivado";
        }
    }

    private async void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((DateTime.UtcNow - _lastPlayPauseTime).TotalMilliseconds < 250)
        {
            return;
        }
        _lastPlayPauseTime = DateTime.UtcNow;

        SoundService.Play(SoundType.Click);

        try
        {
            if (ApplicationHost.Instance != null && ApplicationHost.Instance.IsInternalMusicActive)
            {
                _isPlaying = await ApplicationHost.Instance.TogglePlayPauseMusicAsync();
            }
            else if (_currentSession != null)
            {
                await _currentSession.TryTogglePlayPauseAsync();
                _isPlaying = !_isPlaying;
            }

            TxtPlayPauseGlyph.Text = _isPlaying ? "\uE769" : "\uE768";
        }
        catch { }
    }

    private async void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SoundService.Play(SoundType.Click);

        try
        {
            if (ApplicationHost.Instance != null && ApplicationHost.Instance.IsInternalMusicActive)
            {
                await ApplicationHost.Instance.PreviousMusicTrackAsync();
            }
            else if (_currentSession != null)
            {
                await _currentSession.TrySkipPreviousAsync();
            }
        }
        catch { }
    }

    private async void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SoundService.Play(SoundType.Click);

        try
        {
            if (ApplicationHost.Instance != null && ApplicationHost.Instance.IsInternalMusicActive)
            {
                await ApplicationHost.Instance.NextMusicTrackAsync();
            }
            else if (_currentSession != null)
            {
                await _currentSession.TrySkipNextAsync();
            }
        }
        catch { }
    }

    private void BtnDismissOrb_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SoundService.Play(SoundType.Click);
        ClosePopover();
        CollapseToOrb();
    }

    private void TxtSearchBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SetWindowActivationEnabled(true);
        Activate();
        TxtSearchBox.Focus();
        Keyboard.Focus(TxtSearchBox);
    }

    private void TxtSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _searchDebounceTimer.Tick += (s, args) =>
        {
            _searchDebounceTimer.Stop();
            _ = LoadPlaylistAsync(TxtSearchBox.Text);
        };
        _searchDebounceTimer.Start();
    }

    private void TxtSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _searchDebounceTimer?.Stop();
            _ = LoadPlaylistAsync(TxtSearchBox.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClosePopover();
            e.Handled = true;
        }
    }

    private void BtnExecuteSearch_Click(object sender, RoutedEventArgs e)
    {
        _searchDebounceTimer?.Stop();
        _ = LoadPlaylistAsync(TxtSearchBox.Text);
    }

    private static string NormalizeTitleForDeduplication(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        // Strip bracketed text: [Official Video], (Full Opening), etc.
        string t = System.Text.RegularExpressions.Regex.Replace(title, @"\[.*?\]|\(.*?\)|【.*?】|『.*?』", " ");
        // Strip common noise tokens
        t = System.Text.RegularExpressions.Regex.Replace(t, @"(?i)\b(official\s+video|official\s+audio|audio\s+oficial|video\s+oficial|full\s+ver|full\s+version|sub\s+español|letra|lyrics|4k|hd|1080p|creditless|clean|music\s+video|amv)\b", " ");
        // Retain alphanumeric only
        t = System.Text.RegularExpressions.Regex.Replace(t, @"[^\p{L}\p{Nd}]", "");
        return t.ToLowerInvariant();
    }

    private static string BuildRecommendationQuery(string currentTitle, string currentArtist)
    {
        if (string.IsNullOrWhiteSpace(currentTitle) || currentTitle == "Reproductor")
        {
            return "musica top hits";
        }

        // Clean current title of brackets and video/audio tags
        string cleanTitle = System.Text.RegularExpressions.Regex.Replace(currentTitle, @"\[.*?\]|\(.*?\)|【.*?】|『.*?』", " ");
        cleanTitle = System.Text.RegularExpressions.Regex.Replace(cleanTitle, @"(?i)\b(official\s+video|official\s+audio|audio\s+oficial|video\s+oficial|full\s+ver|full\s+version|sub\s+español|letra|lyrics|4k|hd|1080p|creditless|clean|music\s+video|amv)\b", " ");
        cleanTitle = System.Text.RegularExpressions.Regex.Replace(cleanTitle, @"\s+", " ").Trim(' ', '-', '|', ':');

        string cleanArt = (currentArtist ?? string.Empty).Trim();
        // Ignore generic or video platform channels as artist
        if (cleanArt.Equals("YouTube Music", StringComparison.OrdinalIgnoreCase) ||
            cleanArt.Equals("Reproductor", StringComparison.OrdinalIgnoreCase) ||
            cleanArt.Equals("En reproducción", StringComparison.OrdinalIgnoreCase) ||
            cleanArt.Equals("YouTube", StringComparison.OrdinalIgnoreCase) ||
            cleanArt.Equals("Crunchyroll", StringComparison.OrdinalIgnoreCase) ||
            cleanArt.Contains("vistas", StringComparison.OrdinalIgnoreCase) ||
            cleanArt.Contains("views", StringComparison.OrdinalIgnoreCase) ||
            cleanArt.Contains("suscriptor", StringComparison.OrdinalIgnoreCase))
        {
            cleanArt = string.Empty;
        }

        if (!string.IsNullOrEmpty(cleanArt))
        {
            return $"{cleanArt} mix canciones";
        }

        // If title has "by Artist"
        if (cleanTitle.Contains(" by ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = System.Text.RegularExpressions.Regex.Split(cleanTitle, @"(?i)\s+by\s+");
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                return $"{parts[1].Trim()} mix canciones";
            }
        }

        // If title has " - "
        if (cleanTitle.Contains(" - "))
        {
            var parts = cleanTitle.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            {
                return $"{parts[0].Trim()} mix canciones";
            }
        }

        // If title contains "Opening" or "Ending", take the show/franchise name
        if (cleanTitle.Contains("Opening", StringComparison.OrdinalIgnoreCase))
        {
            int opIdx = cleanTitle.IndexOf("Opening", StringComparison.OrdinalIgnoreCase);
            string showName = cleanTitle.Substring(0, opIdx).Trim(' ', '-', '|', ':');
            if (showName.Length > 2)
            {
                return $"{showName} anime mix canciones";
            }
        }

        return $"{cleanTitle} mix canciones";
    }

    private async Task LoadPlaylistAsync(string? query = null)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            SearchResultsContainer.Children.Clear();

            string searchQuery = (query ?? string.Empty).Trim();
            bool isSearching = !string.IsNullOrWhiteSpace(searchQuery);

            if (isSearching)
            {
                TxtPlaylistTitle.Text = "RESULTADOS DE BÚSQUEDA";
            }
            else
            {
                TxtPlaylistTitle.Text = "COLA DE REPRODUCCIÓN";
            }

            var loadingText = new TextBlock
            {
                Text = isSearching ? "Buscando canciones..." : "Cargando cola de reproducción...",
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontSize = 12,
                Margin = new Thickness(14),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            SearchResultsContainer.Children.Add(loadingText);

            // 1. Direct Real Queue from YouTube Music:
            // When not searching and YouTube Music is playing, load the real Up Next / Queue without replacing or disturbing playback!
            if (!isSearching && ApplicationHost.Instance != null && ApplicationHost.Instance.IsInternalMusicActive)
            {
                try
                {
                    string? queueJson = await ApplicationHost.Instance.GetInternalMusicQueueJsonAsync();
                    if (token.IsCancellationRequested) return;

                    if (!string.IsNullOrWhiteSpace(queueJson) && queueJson != "[]")
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(queueJson);
                        var queueElements = doc.RootElement.EnumerateArray().ToList();

                        if (queueElements.Count > 0)
                        {
                            SearchResultsContainer.Children.Clear();
                            _queueCount = queueElements.Count;
                            TxtPlaylistCount.Text = $"{queueElements.Count} canciones";

                            foreach (var qEl in queueElements)
                            {
                                int idx = qEl.GetProperty("index").GetInt32();
                                string title = qEl.GetProperty("title").GetString() ?? "";
                                string artist = qEl.GetProperty("artist").GetString() ?? "";
                                artist = System.Text.RegularExpressions.Regex.Replace(artist, @"\r?\n\s*", " ").Trim();
                                string thumb = qEl.TryGetProperty("thumbnail", out var th) ? (th.GetString() ?? "") : "";
                                bool isSelected = qEl.TryGetProperty("selected", out var sel) && sel.GetBoolean();
                                string vid = qEl.TryGetProperty("videoId", out var vp) ? (vp.GetString() ?? "") : "";
                                string duration = qEl.TryGetProperty("duration", out var dur) ? (dur.GetString() ?? "") : "";

                                var item = new PinnedApp
                                {
                                    Name = title,
                                    Subtitle = !string.IsNullOrWhiteSpace(artist) ? artist : "YouTube Music",
                                    ThumbnailUrl = thumb,
                                    VideoId = vid,
                                    Duration = duration,
                                    Path = !string.IsNullOrEmpty(vid) ? $"ytmusic:play:{vid}" : $"ytmusic:queue:{idx}",
                                    ActionText = isSelected ? "Reproduciendo" : "Cola",
                                    ToolTip = $"{title} - {artist}"
                                };

                                var btn = CreateTrackResultButton(item, isSelected, queueIndex: idx);
                                SearchResultsContainer.Children.Add(btn);
                            }
                            return;
                        }
                    }
                }
                catch { }
            }

            if (!isSearching)
            {
                searchQuery = BuildRecommendationQuery(_currentTitle, _currentArtist);
            }

            var results = await SpotlightSearchService.Instance.SearchMusicAsync(searchQuery, token, maxResults: 20);
            if (token.IsCancellationRequested) return;

            SearchResultsContainer.Children.Clear();

            // Filter out system command routes (ytmusic:toggle, etc.) and repetitive "Reproducir ..." command cards
            var trackResults = results.Where(r => !string.IsNullOrEmpty(r.VideoId) &&
                                                  !r.Path.StartsWith("ytmusic:toggle") &&
                                                  !r.Path.StartsWith("ytmusic:next") &&
                                                  !r.Path.StartsWith("ytmusic:prev") &&
                                                  !r.Path.StartsWith("ytmusic:open") &&
                                                  !r.Path.StartsWith("https://www.youtube.com/results") &&
                                                  !r.Name.StartsWith("Reproducir \"", StringComparison.OrdinalIgnoreCase)).ToList();

            if (trackResults.Count == 0)
            {
                trackResults = results.Where(r => !r.Path.StartsWith("ytmusic:toggle") &&
                                                  !r.Path.StartsWith("ytmusic:next") &&
                                                  !r.Path.StartsWith("ytmusic:prev") &&
                                                  !r.Path.StartsWith("ytmusic:open") &&
                                                  !r.Name.StartsWith("Reproducir \"", StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // Deduplicate items based on normalized title so re-uploads of the same song are not repeated
            var deduplicatedTracks = new List<PinnedApp>();
            var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string currentNorm = NormalizeTitleForDeduplication(_currentTitle);

            // If not in a search query, show the current active track first
            if (!isSearching && !string.IsNullOrWhiteSpace(_currentTitle) && _currentTitle != "Reproductor")
            {
                if (!string.IsNullOrEmpty(currentNorm))
                {
                    seenTitles.Add(currentNorm);
                }
                var currentItem = new PinnedApp
                {
                    Name = _currentTitle,
                    Subtitle = !string.IsNullOrWhiteSpace(_currentArtist) && _currentArtist != "En reproducción"
                        ? _currentArtist
                        : "En reproducción",
                    Icon = _currentArtwork,
                    ActionText = "Reproduciendo",
                    ToolTip = $"{_currentTitle} - {_currentArtist}"
                };
                deduplicatedTracks.Add(currentItem);
            }

            foreach (var item in trackResults)
            {
                string norm = NormalizeTitleForDeduplication(item.Name);
                if (string.IsNullOrEmpty(norm)) continue;

                // Avoid duplicating the current track or previous results
                if (seenTitles.Add(norm))
                {
                    deduplicatedTracks.Add(item);
                }
            }

            if (deduplicatedTracks.Count == 0)
            {
                var noResults = new TextBlock
                {
                    Text = "No se encontraron canciones.",
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 12,
                    Margin = new Thickness(14),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                SearchResultsContainer.Children.Add(noResults);
                TxtPlaylistCount.Text = "0 canciones";
                _queueCount = 0;
                return;
            }

            _queueCount = deduplicatedTracks.Count;
            TxtPlaylistCount.Text = $"{deduplicatedTracks.Count} canciones";

            bool currentTrackAssigned = false;
            foreach (var item in deduplicatedTracks)
            {
                bool isCurrent = false;
                if (!currentTrackAssigned && !string.IsNullOrWhiteSpace(_currentTitle) && _currentTitle != "Reproductor")
                {
                    if (item.Name.Equals(_currentTitle, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(currentNorm) && NormalizeTitleForDeduplication(item.Name).Equals(currentNorm, StringComparison.OrdinalIgnoreCase)))
                    {
                        isCurrent = true;
                        currentTrackAssigned = true;
                    }
                }

                var btn = CreateTrackResultButton(item, isCurrent);
                SearchResultsContainer.Children.Add(btn);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private static FrameworkElement CreateEqualizerBars(Brush brush, bool isPlaying)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var bar1 = new Border
        {
            Width = 3.2,
            Height = 6,
            CornerRadius = new CornerRadius(1),
            Background = brush,
            Margin = new Thickness(0, 0, 2.2, 0),
            VerticalAlignment = VerticalAlignment.Bottom
        };

        var bar2 = new Border
        {
            Width = 3.2,
            Height = 14,
            CornerRadius = new CornerRadius(1),
            Background = brush,
            Margin = new Thickness(0, 0, 2.2, 0),
            VerticalAlignment = VerticalAlignment.Bottom
        };

        var bar3 = new Border
        {
            Width = 3.2,
            Height = 9.5,
            CornerRadius = new CornerRadius(1),
            Background = brush,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        if (isPlaying)
        {
            var anim1 = new DoubleAnimation(4, 11, new Duration(TimeSpan.FromMilliseconds(380)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            var anim2 = new DoubleAnimation(7, 15, new Duration(TimeSpan.FromMilliseconds(270)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            var anim3 = new DoubleAnimation(4, 13, new Duration(TimeSpan.FromMilliseconds(430)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            bar1.BeginAnimation(FrameworkElement.HeightProperty, anim1);
            bar2.BeginAnimation(FrameworkElement.HeightProperty, anim2);
            bar3.BeginAnimation(FrameworkElement.HeightProperty, anim3);
        }

        panel.Children.Add(bar1);
        panel.Children.Add(bar2);
        panel.Children.Add(bar3);

        return new Border
        {
            Width = 20,
            Height = 18,
            Child = panel,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
    }

    private Button CreateTrackResultButton(PinnedApp item, bool isCurrentTrack, int queueIndex = -1)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("LiquidSearchResultItemStyle"),
            Tag = item,
            Margin = new Thickness(0, 2, 0, 2)
        };

        var border = new Border
        {
            CornerRadius = new CornerRadius(0, 8, 8, 0),
            Background = isCurrentTrack ? new LinearGradientBrush(Color.FromArgb(32, 234, 88, 12), Color.FromArgb(4, 234, 88, 12), 0) : Brushes.Transparent,
            BorderBrush = isCurrentTrack ? new SolidColorBrush(Color.FromRgb(234, 88, 12)) : Brushes.Transparent,
            BorderThickness = new Thickness(3.5, 0, 0, 0),
            Padding = new Thickness(10, 8, 10, 8),
            SnapsToDevicePixels = true
        };

        var monoFont = new FontFamily("Cascadia Code, Consolas, JetBrains Mono, monospace");
        var orangeBrush = new SolidColorBrush(Color.FromRgb(234, 88, 12));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left side: Text Stack (Title wrapped up to 3 lines + Subtitle beneath)
        var sp = new StackPanel { Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };

        var titleBlock = new TextBlock
        {
            FontFamily = monoFont,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 62,
            LineHeight = 19,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        };

        if (isCurrentTrack)
        {
            titleBlock.Inlines.Add(new Run("[ON] ")
            {
                Foreground = orangeBrush,
                FontWeight = FontWeights.Bold
            });
            titleBlock.Inlines.Add(new Run(item.Name)
            {
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            });
        }
        else
        {
            titleBlock.Inlines.Add(new Run(item.Name)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                FontWeight = FontWeights.Normal
            });
        }

        string rawSub = string.IsNullOrWhiteSpace(item.Subtitle) ? (item.Channel ?? "YouTube Music") : item.Subtitle;
        rawSub = System.Text.RegularExpressions.Regex.Replace(rawSub, @"\r?\n\s*", " ").Trim();
        var subBlock = new TextBlock
        {
            Text = rawSub,
            FontFamily = monoFont,
            FontSize = 11,
            Foreground = isCurrentTrack ? new SolidColorBrush(Color.FromArgb(215, 234, 88, 12)) : new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 3.5, 0, 0)
        };

        sp.Children.Add(titleBlock);
        sp.Children.Add(subBlock);
        Grid.SetColumn(sp, 0);
        grid.Children.Add(sp);

        // Right side: Duration + Play / Equalizer indicator
        var rightPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 2, 0)
        };

        if (!string.IsNullOrWhiteSpace(item.Duration))
        {
            var durBlock = new TextBlock
            {
                Text = item.Duration,
                FontFamily = monoFont,
                FontSize = 12,
                FontWeight = isCurrentTrack ? FontWeights.Bold : FontWeights.Normal,
                Foreground = isCurrentTrack ? orangeBrush : new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            rightPanel.Children.Add(durBlock);
        }

        var iconContainer = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = isCurrentTrack ? new SolidColorBrush(Color.FromArgb(32, 234, 88, 12)) : Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center
        };

        var playIcon = new TextBlock
        {
            Text = "\uE768",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(140, 148, 163, 184)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 0, 0)
        };

        if (isCurrentTrack)
        {
            iconContainer.Child = CreateEqualizerBars(orangeBrush, _isPlaying);
        }
        else
        {
            iconContainer.Child = playIcon;
        }

        rightPanel.Children.Add(iconContainer);
        Grid.SetColumn(rightPanel, 1);
        grid.Children.Add(rightPanel);

        if (!isCurrentTrack)
        {
            border.MouseEnter += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
                playIcon.Foreground = orangeBrush;
                iconContainer.Background = new SolidColorBrush(Color.FromArgb(32, 234, 88, 12));
            };
            border.MouseLeave += (s, e) =>
            {
                border.Background = Brushes.Transparent;
                playIcon.Foreground = new SolidColorBrush(Color.FromArgb(140, 148, 163, 184));
                iconContainer.Background = Brushes.Transparent;
            };
        }

        border.Child = grid;
        btn.Content = border;

        btn.Click += async (s, e) =>
        {
            SoundService.Play(SoundType.Click);

            if (queueIndex >= 0 && ApplicationHost.Instance != null && ApplicationHost.Instance.IsInternalMusicActive)
            {
                if (isCurrentTrack)
                {
                    _isPlaying = await ApplicationHost.Instance.TogglePlayPauseMusicAsync();
                    TxtPlayPauseGlyph.Text = _isPlaying ? "\uE769" : "\uE768";
                }
                else
                {
                    bool success = await ApplicationHost.Instance.PlayInternalMusicQueueIndexAsync(queueIndex, item.VideoId);
                    if (!success)
                    {
                        // Fallback: If direct queue jump failed, play track by VideoId or title
                        if (!string.IsNullOrEmpty(item.VideoId))
                        {
                            ApplicationHost.Instance.PlayMusic(item.VideoId, showWindow: false);
                        }
                        else
                        {
                            ApplicationHost.Instance.PlayMusic(item.Name, showWindow: false);
                        }
                    }

                    // Optimistic update of capsule header UI
                    ApplyMediaState(item.Name, item.Subtitle ?? "", item.Icon, isPlaying: true);

                    // Refresh queue shortly after to update track highlights
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(800);
                        await Dispatcher.InvokeAsync(() => LoadPlaylistAsync());
                    });
                }
                return;
            }

            if (isCurrentTrack)
            {
                if (ApplicationHost.Instance != null && ApplicationHost.Instance.IsInternalMusicActive)
                {
                    _isPlaying = await ApplicationHost.Instance.TogglePlayPauseMusicAsync();
                }
                else if (_currentSession != null)
                {
                    await _currentSession.TryTogglePlayPauseAsync();
                    _isPlaying = !_isPlaying;
                }
                TxtPlayPauseGlyph.Text = _isPlaying ? "\uE769" : "\uE768";
                return;
            }

            if (!string.IsNullOrEmpty(item.VideoId))
            {
                ApplicationHost.Instance?.PlayMusic(item.VideoId, showWindow: false);
            }
            else
            {
                ApplicationHost.Instance?.PlayMusic(item.Name, showWindow: false);
            }

            ApplyMediaState(item.Name, item.Channel ?? item.Subtitle ?? "", item.Icon, isPlaying: true);
        };

        return btn;
    }

    private void BtnOpenYouTubeMusic_Click(object sender, RoutedEventArgs e)
    {
        ClosePopover();
        ApplicationHost.Instance?.ToggleWidget("YouTube");
        HideOrb();
    }

    private void BtnOpenSpotlight_Click(object sender, RoutedEventArgs e)
    {
        ClosePopover();
        ApplicationHost.Instance?.ToggleWidget("PinnedApps");
        HideOrb();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        Instance = null;
        if (_sessionManager != null)
        {
            _sessionManager.CurrentSessionChanged -= SessionManager_CurrentSessionChanged;
        }
        if (_currentSession != null)
        {
            try
            {
                _currentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
            }
            catch { }
        }
        if (ApplicationHost.Instance != null)
        {
            ApplicationHost.Instance.InternalMusicStateChanged -= Host_InternalMusicStateChanged;
        }
        _inactivityTimer?.Stop();
        _searchDebounceTimer?.Stop();
        _progressTimer?.Stop();
        _autoCollapseTimer?.Stop();
        _searchCts?.Cancel();
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;
        if (parentObject is T parent) return parent;
        return FindVisualParent<T>(parentObject);
    }
}
