using System;
using System.Collections.Generic;
using System.IO;
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

public partial class MainWindow : Window
{
    private const int IMG_W = 250;
    private const int IMG_H = 430;
    private const int GAP = 8;
    private const int PAD = 25;
    private const int HOTKEY_ID = 9001;
    private const int LAUNCHER_HOTKEY_ID = 9002;

    public static event Action? LauncherRequested;

    private readonly List<WallpaperEntry> _wallpapers = new();
    private readonly string _wallpaperDir;
    private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    private int _visibleCount;
    private int _totalPages;
    private int _currentPage;

    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounceTimer;
    private bool _isChangingWallpaper;
    private DispatcherTimer? _autoTimer;
    private bool _autoChangeEnabled;
    private bool _autoModeRandom;
    private int _sequentialIntervalIndex;
    private int _randomIntervalIndex;
    private int _currentWallpaperIndex = -1;

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
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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
    private const int MOD_ALT = 0x0001;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_SETTINGCHANGE = 0x001A;

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
        DependencyProperty.Register("ScrollOffset", typeof(double), typeof(MainWindow),
            new PropertyMetadata(0.0, OnScrollOffsetChanged));

    private static MainWindow? _instance;
    private static IntPtr _mouseHookId;
    private static LowLevelMouseProc? _mouseProc;

    private double ScrollOffset
    {
        get => (double)GetValue(ScrollOffsetProperty);
        set => SetValue(ScrollOffsetProperty, value);
    }

    private static void OnScrollOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var w = (MainWindow)d;
        w.ImageScroll?.ScrollToHorizontalOffset((double)e.NewValue);
    }

    public MainWindow()
    {
        _instance = this;
        InitializeComponent();
        _wallpaperDir = GetWallpaperDirectory();
        LoadWallpapers();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        int preference = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

        HideFromAltTab(hwnd);

        RegisterHotKey(hwnd, HOTKEY_ID, MOD_ALT, 0x57);
        RegisterHotKey(hwnd, LAUNCHER_HOTKEY_ID, 0x0002, 0x20);

        RootBorder.SizeChanged += UpdateBorderClip;

        StartWatching();

        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;
        var maxW = screenW - 30;

        _visibleCount = Math.Max(1, (int)((maxW - PAD * 2 - 2 + GAP) / (IMG_W + GAP)));
        _totalPages = (int)Math.Ceiling((double)_wallpapers.Count / _visibleCount);
        _currentPage = 0;

        var wh = _visibleCount * (IMG_W + GAP) + PAD * 2 + 2;
        var w = Math.Min(
            _wallpapers.Count * (IMG_W + GAP) + PAD * 2 + 2,
            wh
        );
        var h = IMG_H + PAD * 2 + 8 + 2;

        Left = (screenW - w) / 2;
        Top = (screenH - h) / 2;
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

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                var h = new WindowInteropHelper(this).Handle;
                RefreshBackdrop(h);
                UpdateWindowRegion(h);
            }
        };

        InstallMouseHook();

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
            RDW_INVALIDATE | RDW_ERASE | RDW_UPDATENOW | RDW_ALLCHILDREN);
    }

    private static string GetCurrentWallpaper()
    {
        var sb = new StringBuilder(260);
        SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0);
        return sb.ToString();
    }

    private void StartWatching()
    {
        if (!Directory.Exists(_wallpaperDir)) return;

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

        _wallpapers.Clear();

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

        _totalPages = (int)Math.Ceiling((double)_wallpapers.Count / _visibleCount);
        _currentPage = Math.Min(page, Math.Max(0, _totalPages - 1));

        WallpaperList.ItemsSource = _wallpapers.Select(CreateTile).ToList();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            ScrollOffset = Math.Min(offset, _currentPage * _visibleCount * (IMG_W + GAP));
        }), DispatcherPriority.Loaded);
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
        _instance = null;
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
        if (!Directory.Exists(_wallpaperDir))
            return;

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
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(entry.FilePath);
        bitmap.DecodePixelWidth = IMG_W * 2;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

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
            Background = new ImageBrush(bitmap)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            }
        };

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

        border.MouseLeftButtonUp += (_, _) => ChangeWallpaper(entry);

        return border;
    }

    private void ChangeWallpaper(WallpaperEntry entry)
    {
        var current = GetCurrentWallpaper();
        if (string.Equals(current, entry.FilePath, StringComparison.OrdinalIgnoreCase))
            return;

        _isChangingWallpaper = true;

        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, entry.FilePath,
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

        _isChangingWallpaper = false;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            if (wParam.ToInt32() == HOTKEY_ID)
            {
                Visibility = Visibility == Visibility.Visible
                    ? Visibility.Hidden
                    : Visibility.Visible;
                handled = true;
            }
            else if (wParam.ToInt32() == LAUNCHER_HOTKEY_ID)
            {
                LauncherRequested?.Invoke();
                handled = true;
            }
        }
        else if (msg == WM_SETTINGCHANGE)
        {
            if (_isChangingWallpaper) return IntPtr.Zero;

            var area = Marshal.PtrToStringAuto(lParam);
            if (area == "Desk")
            {
                RefreshBackdrop(hwnd);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public static bool IsAutoChangeEnabled() => _instance?._autoChangeEnabled ?? false;

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
    }

    private void CycleCurrentSpeedInternal()
    {
        if (_autoModeRandom)
            _randomIntervalIndex = (_randomIntervalIndex + 1) % AutoIntervals.Length;
        else
            _sequentialIntervalIndex = (_sequentialIntervalIndex + 1) % AutoIntervals.Length;

        if (_autoChangeEnabled)
            ApplyAutoChangeState();
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

    protected override void OnClosed(EventArgs e)
    {
        _autoTimer?.Stop();
        UninstallMouseHook();
        _watcher?.Dispose();
        _debounceTimer?.Stop();
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_ID);
        }
        catch { }
        base.OnClosed(e);
    }
}

public class WallpaperEntry
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
}
