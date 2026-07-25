using System;
using System.Collections.Generic;
using System.IO;
using WallFlow.Services;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WallFlow;

public partial class LocalGalleryWindow : Window
{
    private const int IMG_W = 250;
    private const int IMG_H = 430;
    private const int GAP = 8;
    private const int PAD = 25;
    public static event Action? RefreshRequested;
    public static void NotifyRefreshRequested() => RefreshRequested?.Invoke();

    private readonly List<WallpaperEntry> _wallpapers = new();
    private string _wallpaperDir;
    private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    private int _visibleCount;
    private int _totalPages;
    private int _currentPage;

    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounceTimer;
    private DispatcherTimer? _backdropRefreshTimer;
    private DispatcherTimer? _autoTimer;
    private bool _autoChangeEnabled;
    private bool _autoModeRandom;
    private int _sequentialIntervalIndex;
    private int _randomIntervalIndex;
    private int _currentWallpaperIndex = -1;
    private uint _activateMsgId;

    private readonly System.Threading.SemaphoreSlim _thumbnailThrottle = new(4, 4);

    private static readonly TimeSpan[] AutoIntervals =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
    ];

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, StringBuilder lpvParam, int fuWinIni);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rcUpdate, IntPtr hrgnUpdate, uint flags);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr hrgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern int DeleteObject(IntPtr hrgn);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT pt);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_ERASE = 0x0004;
    private const uint RDW_UPDATENOW = 0x0100;
    private const uint RDW_ALLCHILDREN = 0x0080;

    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;

    private const int SPI_SETDESKWALLPAPER = 20;
    private const int SPI_GETDESKWALLPAPER = 0x0073;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_COPYDATA = 0x004A;

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public int mouseData;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static readonly DependencyProperty ScrollOffsetProperty =
        DependencyProperty.Register("ScrollOffset", typeof(double), typeof(LocalGalleryWindow),
            new PropertyMetadata(0.0, OnScrollOffsetChanged));

    private static LocalGalleryWindow? _instance;
    public static LocalGalleryWindow? Instance => _instance;
    private static IntPtr _mouseHookId;
    private static LowLevelMouseProc? _mouseProc;

    private double ScrollOffset
    {
        get => (double)GetValue(ScrollOffsetProperty);
        set => SetValue(ScrollOffsetProperty, value);
    }

    private static void OnScrollOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var w = (LocalGalleryWindow)d;
        w.ImageScroll?.ScrollToHorizontalOffset((double)e.NewValue);
    }

    public LocalGalleryWindow()
    {
        _instance = this;
        InitializeComponent();
        Visibility = Visibility.Hidden;
        Services.SettingsService.Load();
        _wallpaperDir = Services.SettingsService.Instance.WallpaperDirectory;
        LoadWallpapers();
        RefreshRequested += OnRefreshRequested;
        ApplicationEvents.RefreshRequested += OnRefreshRequested;
        ApplicationEvents.DirectoryChanged += ChangeDirectory;

        IsVisibleChanged += OnIsVisibleChanged;
        // Load AutoChange settings
        var s = Services.SettingsService.Instance;
        _autoChangeEnabled = s.AutoChangeEnabled;
        _autoModeRandom = s.AutoModeRandom;
        _sequentialIntervalIndex = Math.Clamp(s.SequentialIntervalIndex, 0, AutoIntervals.Length - 1);
        _randomIntervalIndex = Math.Clamp(s.RandomIntervalIndex, 0, AutoIntervals.Length - 1);
        ApplyAutoChangeState();
    }

    private void OnIsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero)
            {
                RefreshBackdrop(h);
                UpdateWindowRegion(h);
            }
            InstallMouseHook();
        }
        else
        {
            UninstallMouseHook();
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        RootBorder.SizeChanged += UpdateBorderClip;

        StartWatching();

        var workArea = SystemParameters.WorkArea;
        var maxW = workArea.Width - 30;

        _visibleCount = Math.Max(1, (int)((maxW - PAD * 2 - 2 + GAP) / (IMG_W + GAP)));
        _totalPages = (int)Math.Ceiling((double)_wallpapers.Count / _visibleCount);
        _currentPage = 0;

        var wh = _visibleCount * (IMG_W + GAP) + PAD * 2 + 2;
        var w = Math.Min(
            _wallpapers.Count * (IMG_W + GAP) + PAD * 2 + 2,
            wh
        );
        var h = IMG_H + PAD * 2 + 8 + 2;

        Left = workArea.Left + (workArea.Width - w) / 2;
        Top = workArea.Top + (workArea.Height - h) / 2;
        Width = w;
        Height = h;

        UpdateBorderClip(null, null);

        var dpi = VisualTreeHelper.GetDpi(this);
        int pixelW = (int)Math.Round(w * dpi.DpiScaleX);
        int pixelH = (int)Math.Round(h * dpi.DpiScaleY);
        int radiusPx = (int)Math.Round(8 * dpi.DpiScaleX);
        var rgn = CreateRoundRectRgn(0, 0, pixelW, pixelH, radiusPx, radiusPx);
        if (rgn != IntPtr.Zero)
        {
            SetWindowRgn(hwnd, rgn, true);
            DeleteObject(rgn);
        }

        RefreshBackdrop(hwnd);

        if (IsVisible)
        {
            InstallMouseHook();
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var wallpaper = GetCurrentWallpaper();
            if (!string.IsNullOrEmpty(wallpaper) && File.Exists(wallpaper))
            {
                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, wallpaper,
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                RefreshBackdrop(hwnd);
            }
        }), DispatcherPriority.ApplicationIdle);
    }

    private void UpdateBorderClip(object? sender, RoutedEventArgs? e)
    {
        var w = RootBorder.ActualWidth;
        var h = RootBorder.ActualHeight;
        RootBorder.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 8, 8);
        Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 8, 8);

        var hwnd = new WindowInteropHelper(this).Handle;
        UpdateWindowRegion(hwnd);
    }

    public static void ChangeDirectory(string newDir)
    {
        if (_instance == null) return;
        _instance.Dispatcher.BeginInvoke(() => _instance.ChangeDirectoryInternal(newDir));
    }

    private void ChangeDirectoryInternal(string newDir)
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        Services.SettingsService.UpdateWallpaperDirectory(newDir);
        _wallpaperDir = newDir;

        StartWatching();
        RefreshWallpapers();
    }

    private void UpdateWindowRegion(IntPtr hwnd)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int pixelW = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        int pixelH = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        int radius = (int)Math.Round(8 * dpi.DpiScaleX);

        var rgn = CreateRoundRectRgn(0, 0, pixelW, pixelH, radius, radius);
        if (rgn != IntPtr.Zero)
        {
            SetWindowRgn(hwnd, rgn, true);
            DeleteObject(rgn);
        }
    }

    private static void ApplyAccent(IntPtr hwnd, AccentState state)
    {
        var accent = new AccentPolicy { AccentState = state };
        var size = Marshal.SizeOf(accent);
        var ptr = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(accent, ptr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size
            };

            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void RefreshBackdrop(IntPtr hwnd)
    {
        ApplyAccent(hwnd, AccentState.ACCENT_ENABLE_BLURBEHIND);
        RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
            RDW_INVALIDATE | RDW_UPDATENOW);
    }

    public static void RefreshAllBackdrops()
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(RefreshAllBackdrops));
            return;
        }

        // 1. Refresh LocalGalleryWindow
        if (_instance != null)
        {
            var hwnd = new WindowInteropHelper(_instance).Handle;
            if (hwnd != IntPtr.Zero)
                RefreshBackdrop(hwnd);
        }

        // 2. Refresh DockWindow
        if (DockWindow.Instance != null)
        {
            Services.BackdropHelper.RefreshBackdrop(DockWindow.Instance);
            DockWindow.Instance.UpdateAccentColorFromWallpaper();
        }

        // 3. Refresh any other open transparent windows
        foreach (Window window in Application.Current.Windows)
        {
            var name = window.GetType().Name;
            if (name == "OnlineGalleryWindow" || name == "CalendarWindow" || name == "MediaFlyoutWindow" || name == "PowerWindow")
            {
                Services.BackdropHelper.RefreshBackdrop(window);
            }
        }
    }

    public static string GetCurrentWallpaper()
    {
        var sb = new StringBuilder(260);
        SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0);
        return sb.ToString();
    }

    private void StartWatching()
    {
        Directory.CreateDirectory(_wallpaperDir);

        _watcher = new FileSystemWatcher(_wallpaperDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            Filter = "*.*"
        };

        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };

        _debounceTimer.Tick += OnDebounceTick;

        FileSystemEventHandler handler = OnFileChanged;
        RenamedEventHandler renamed = (s, e) => OnFileChanged(s, e);

        _watcher.Created += handler;
        _watcher.Deleted += handler;
        _watcher.Renamed += renamed;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (e.Name is null) return;
        var ext = Path.GetExtension(e.Name).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext)) return;

        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer?.Stop();
        RefreshWallpapers();
    }

    private void RefreshWallpapers()
    {
        var offset = ScrollOffset;
        var page = _currentPage;

        Task.Run(() =>
        {
            try
            {
                var files = Directory.EnumerateFiles(_wallpaperDir, "*", SearchOption.AllDirectories)
                    .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f)
                    .ToList();

                var temp = new List<WallpaperEntry>();
                foreach (var file in files)
                {
                    temp.Add(new WallpaperEntry
                    {
                        FilePath = file,
                        FileName = Path.GetFileNameWithoutExtension(file)
                    });
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _wallpapers.Clear();
                    foreach (var entry in temp)
                    {
                        _wallpapers.Add(entry);
                    }

                    _totalPages = (int)Math.Ceiling((double)_wallpapers.Count / _visibleCount);
                    _currentPage = Math.Min(page, Math.Max(0, _totalPages - 1));

                    WallpaperList.ItemsSource = _wallpapers.Select(CreateTile).ToList();

                    ScrollOffset = Math.Min(offset, _currentPage * _visibleCount * (IMG_W + GAP));
                }), DispatcherPriority.Background);
            }
            catch { }
        });
    }

    private void HandleMouseWheel(int delta)
    {
        if (_totalPages <= 1) return;

        var page = _currentPage;
        if (delta < 0) page = Math.Min(_currentPage + 1, _totalPages - 1);
        else page = Math.Max(_currentPage - 1, 0);

        if (page == _currentPage) return;
        _currentPage = page;

        var target = _currentPage * _visibleCount * (IMG_W + GAP);
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(350));
        anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(ScrollOffsetProperty, anim);
    }

    private void InstallMouseHook()
    {
        _instance = this;
        _mouseProc = MouseHookCallback;
        var moduleHandle = GetModuleHandle(null);
        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
    }

    private void UninstallMouseHook()
    {
        if (_mouseHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
        _mouseProc = null;
    }

    private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WM_MOUSEWHEEL)
        {
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var instance = _instance;

            if (instance?.IsVisible == true)
            {
                var ourHwnd = new WindowInteropHelper(instance).Handle;
                var hwndUnderMouse = WindowFromPoint(hookStruct.pt);

                if (hwndUnderMouse == ourHwnd || IsChild(ourHwnd, hwndUnderMouse))
                {
                    int delta = (short)(hookStruct.mouseData >> 16);
                    instance.Dispatcher.BeginInvoke(new Action(() => instance.HandleMouseWheel(delta)));
                    return new IntPtr(1);
                }
            }
        }

        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private static void HideFromAltTab(IntPtr hwnd)
    {
        try
        {
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }
        catch
        {
        }
    }

    private static string GetWallpaperDirectory()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wallflow.txt");
        if (File.Exists(configPath))
        {
            var dir = File.ReadAllText(configPath).Trim();
            if (Directory.Exists(dir))
                return dir;
        }

        var defaultDir = @"C:\Users\Lenovo\OneDrive\Imágenes\Wallpapers";

        if (!Directory.Exists(defaultDir))
        {
            var picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (Directory.Exists(picturesDir))
                return picturesDir;
        }

        return defaultDir;
    }

    private void LoadWallpapers()
    {
        Directory.CreateDirectory(_wallpaperDir);

        var files = Directory.EnumerateFiles(_wallpaperDir, "*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f)
            .ToList();

        foreach (var file in files)
        {
            _wallpapers.Add(new WallpaperEntry
            {
                FilePath = file,
                FileName = Path.GetFileNameWithoutExtension(file)
            });
        }

        WallpaperList.ItemsSource = _wallpapers.Select(CreateTile).ToList();
    }

    private Border CreateTile(WallpaperEntry entry)
    {
        var scale = new ScaleTransform(1, 1);

        var border = new Border
        {
            Style = (Style)FindResource("ImageTile"),
            Width = IMG_W,
            Height = IMG_H,
            Tag = entry,
            RenderTransform = scale,
            RenderTransformOrigin = new Point(0.5, 0.5),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20))
        };

        // Load image using ThumbnailCacheService (disk cache + ultra-fast thumbnail load)
        Task.Run(async () =>
        {
            try
            {
                var bmp = await ThumbnailCacheService.Instance.GetThumbnailAsync(entry.FilePath, IMG_W * 2);
                if (bmp != null)
                {
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        border.Background = new ImageBrush(bmp)
                        {
                            Stretch = Stretch.UniformToFill,
                            AlignmentX = AlignmentX.Center,
                            AlignmentY = AlignmentY.Center
                        };
                    }), DispatcherPriority.Background);
                }
            }
            catch { }
        });

        var pressAnim = new DoubleAnimation(0.93, TimeSpan.FromMilliseconds(80));
        var releaseAnim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(120))
            { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } };

        border.PreviewMouseDown += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pressAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pressAnim);
        };

        border.PreviewMouseUp += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, releaseAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, releaseAnim);
        };

        border.MouseLeave += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, releaseAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, releaseAnim);
        };

        border.MouseLeftButtonUp += async (_, _) =>
        {
            await Task.Delay(120);
            ChangeWallpaper(entry);
        };

        return border;
    }

    private void ChangeWallpaper(WallpaperEntry entry)
    {
        WallpaperTransitionService.ApplyWallpaperWithFade(entry.FilePath, async () =>
        {
            await Task.Delay(150);
            RefreshAllBackdrops();
        });

        // Sync index when wallpaper is manually changed
        _currentWallpaperIndex = _wallpapers.IndexOf(entry);

        // Sync Dock accent color instantly
        if (DockWindow.Instance != null)
        {
            DockWindow.Instance.UpdateAccentColorFromWallpaper(entry.FilePath);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Visibility = Visibility.Hidden;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        _activateMsgId = RegisterWindowMessage("WallFlow_Activate_Instance");

        int preference = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

        HideFromAltTab(hwnd);

        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _activateMsgId && _activateMsgId != 0)
        {
            Visibility = Visibility.Visible;
            try { Activate(); } catch { }
            handled = true;
        }
        else if (msg == WM_COPYDATA)
        {
            try
            {
                var cds = (COPYDATASTRUCT)Marshal.PtrToStructure(lParam, typeof(COPYDATASTRUCT))!;
                string? url = Marshal.PtrToStringUni(cds.lpData);
                if (!string.IsNullOrEmpty(url))
                {
                    App.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = url,
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }));
                }
            }
            catch { }
            handled = true;
            return IntPtr.Zero;
        }
        else if (msg == WM_SETTINGCHANGE)
        {
            var area = Marshal.PtrToStringAuto(lParam);
            if (area == "Desk" || wParam.ToInt32() == SPI_SETDESKWALLPAPER || string.IsNullOrEmpty(area))
            {
                // Debounce and delay refresh to allow Windows to complete drawing the new background
                _backdropRefreshTimer?.Stop();
                _backdropRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _backdropRefreshTimer.Tick += (s, args) =>
                {
                    _backdropRefreshTimer?.Stop();
                    RefreshAllBackdrops();
                };
                _backdropRefreshTimer.Start();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public static bool IsAutoChangeEnabled() => _instance?._autoChangeEnabled ?? false;

    public static void SetAutoChangeEnabled(bool enabled)
    {
        if (_instance == null) return;
        if (_instance._autoChangeEnabled != enabled)
        {
            _instance._autoChangeEnabled = enabled;
            _instance.ApplyAutoChangeState();
        }
    }

    public static int GetCurrentSpeedIndex()
    {
        if (_instance == null) return 0;
        return _instance._autoModeRandom ? _instance._randomIntervalIndex : _instance._sequentialIntervalIndex;
    }

    public static void SetCurrentSpeedIndex(int index)
    {
        if (_instance == null) return;
        if (index < 0 || index >= AutoIntervals.Length) return;
        
        if (_instance._autoModeRandom)
            _instance._randomIntervalIndex = index;
        else
            _instance._sequentialIntervalIndex = index;

        if (_instance._autoChangeEnabled)
            _instance.ApplyAutoChangeState();
        else
            _instance.SaveAutoChangeSettings();
    }

    public static string GetSequentialModeText()
    {
        if (_instance == null) return "Secuencial · 30s";
        return $"Secuencial · {FormatInterval(AutoIntervals[_instance._sequentialIntervalIndex])}";
    }

    public static string GetRandomModeText()
    {
        if (_instance == null) return "Aleatorio · 30s";
        return $"Aleatorio · {FormatInterval(AutoIntervals[_instance._randomIntervalIndex])}";
    }

    public static bool IsRandomMode() => _instance?._autoModeRandom ?? false;

    public static void ToggleAutoChange() => _instance?.ToggleAutoChangeInternal();

    public static void SelectSequentialMode() => _instance?.SelectModeInternal(false);

    public static void SelectRandomMode() => _instance?.SelectModeInternal(true);

    public static void CycleCurrentSpeed() => _instance?.CycleCurrentSpeedInternal();

    private static string FormatInterval(TimeSpan ts)
    {
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m";
        return $"{(int)ts.TotalSeconds}s";
    }

    private void SaveAutoChangeSettings()
    {
        var s = Services.SettingsService.Instance;
        s.AutoChangeEnabled = _autoChangeEnabled;
        s.AutoModeRandom = _autoModeRandom;
        s.SequentialIntervalIndex = _sequentialIntervalIndex;
        s.RandomIntervalIndex = _randomIntervalIndex;
        Services.SettingsService.Save();
    }

    private void ToggleAutoChangeInternal()
    {
        _autoChangeEnabled = !_autoChangeEnabled;
        ApplyAutoChangeState();
    }

    private void SelectModeInternal(bool random)
    {
        _autoModeRandom = random;
        if (_autoChangeEnabled)
            ApplyAutoChangeState();
        else
            SaveAutoChangeSettings();
    }

    private void CycleCurrentSpeedInternal()
    {
        if (_autoModeRandom)
            _randomIntervalIndex = (_randomIntervalIndex + 1) % AutoIntervals.Length;
        else
            _sequentialIntervalIndex = (_sequentialIntervalIndex + 1) % AutoIntervals.Length;

        if (_autoChangeEnabled)
            ApplyAutoChangeState();
        else
            SaveAutoChangeSettings();
    }

    private void ApplyAutoChangeState()
    {
        if (_autoChangeEnabled)
        {
            var interval = _autoModeRandom
                ? AutoIntervals[_randomIntervalIndex]
                : AutoIntervals[_sequentialIntervalIndex];

            _autoTimer ??= new DispatcherTimer();
            _autoTimer.Tick -= OnAutoTick;
            _autoTimer.Tick += OnAutoTick;
            _autoTimer.Interval = interval;
            _autoTimer.Start();
        }
        else
        {
            if (_autoTimer != null)
            {
                _autoTimer.Tick -= OnAutoTick;
                _autoTimer.Stop();
            }
        }
        SaveAutoChangeSettings();
    }

    private void OnAutoTick(object? sender, EventArgs e)
    {
        NextWallpaper();
    }

    private void NextWallpaper()
    {
        if (_wallpapers.Count == 0) return;

        if (_autoModeRandom)
        {
            var next = Random.Shared.Next(_wallpapers.Count);
            ChangeWallpaper(_wallpapers[next]);
        }
        else
        {
            if (_currentWallpaperIndex >= _wallpapers.Count)
                _currentWallpaperIndex = 0;
            _currentWallpaperIndex = (_currentWallpaperIndex + 1) % _wallpapers.Count;
            ChangeWallpaper(_wallpapers[_currentWallpaperIndex]);
        }
    }

    private void PrevWallpaper()
    {
        if (_wallpapers.Count == 0) return;

        if (_currentWallpaperIndex < 0)
        {
            var currentPath = GetCurrentWallpaper();
            _currentWallpaperIndex = _wallpapers.FindIndex(w => string.Equals(w.FilePath, currentPath, StringComparison.OrdinalIgnoreCase));
        }

        if (_currentWallpaperIndex <= 0)
            _currentWallpaperIndex = _wallpapers.Count - 1;
        else
            _currentWallpaperIndex--;

        ChangeWallpaper(_wallpapers[_currentWallpaperIndex]);
    }

    public void PreviousWallpaperExternal()
    {
        PrevWallpaper();
    }

    public void NextWallpaperExternal()
    {
        if (_currentWallpaperIndex < 0)
        {
            var currentPath = GetCurrentWallpaper();
            _currentWallpaperIndex = _wallpapers.FindIndex(w => string.Equals(w.FilePath, currentPath, StringComparison.OrdinalIgnoreCase));
        }
        NextWallpaper();
    }

    private void OnRefreshRequested()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RefreshWallpapers);
            return;
        }
        RefreshWallpapers();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Visibility = Visibility.Hidden;
    }

    protected override void OnClosed(EventArgs e)
    {
        RefreshRequested -= OnRefreshRequested;
        ApplicationEvents.RefreshRequested -= OnRefreshRequested;
        ApplicationEvents.DirectoryChanged -= ChangeDirectory;
        _autoTimer?.Stop();
        UninstallMouseHook();
        _watcher?.Dispose();
        _debounceTimer?.Stop();
        _instance = null;
        base.OnClosed(e);
    }
}

