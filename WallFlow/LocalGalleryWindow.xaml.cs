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
using WallFlow.Native;
using static WallFlow.Native.NativeMethods;

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



    private static readonly DependencyProperty ScrollOffsetProperty =
        DependencyProperty.Register("ScrollOffset", typeof(double), typeof(LocalGalleryWindow),
            new PropertyMetadata(0.0, OnScrollOffsetChanged));

    private static LocalGalleryWindow? _instance;
    public static LocalGalleryWindow? Instance => _instance;

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

        PreviewMouseWheel += (s, e) =>
        {
            HandleMouseWheel(e.Delta);
            e.Handled = true;
        };
    }

    private void OnIsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero)
            {
                BackdropHelper.RefreshBackdrop(this);
                BackdropHelper.UpdateWindowRegion(this, 8);
            }
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

        BackdropHelper.UpdateWindowRegion(this, 8);
        BackdropHelper.RefreshBackdrop(this);
    }

    private void UpdateBorderClip(object? sender, RoutedEventArgs? e)
    {
        var w = RootBorder.ActualWidth;
        var h = RootBorder.ActualHeight;
        RootBorder.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 8, 8);
        Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 8, 8);

        BackdropHelper.UpdateWindowRegion(this, 8);
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
            BackdropHelper.RefreshBackdrop(_instance);
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
        return WallpaperTransitionService.GetCurrentWallpaperPath();
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

                var favs = Services.SettingsService.Instance.FavoriteWallpapers;
                var temp = new List<WallpaperEntry>();
                foreach (var file in files)
                {
                    temp.Add(new WallpaperEntry
                    {
                        FilePath = file,
                        FileName = Path.GetFileNameWithoutExtension(file),
                        IsFavorite = favs != null && favs.Contains(file)
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

        var picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var defaultDir = Path.Combine(picturesDir, "Wallpapers");

        if (Directory.Exists(defaultDir))
            return defaultDir;

        if (Directory.Exists(picturesDir))
            return picturesDir;

        return defaultDir;
    }

    private void LoadWallpapers()
    {
        Directory.CreateDirectory(_wallpaperDir);

        var files = Directory.EnumerateFiles(_wallpaperDir, "*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f)
            .ToList();

        var favs = Services.SettingsService.Instance.FavoriteWallpapers;
        foreach (var file in files)
        {
            _wallpapers.Add(new WallpaperEntry
            {
                FilePath = file,
                FileName = Path.GetFileNameWithoutExtension(file),
                IsFavorite = favs != null && favs.Contains(file)
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

        // Favorite badge
        var favBadge = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 8, 0),
            Visibility = entry.IsFavorite ? Visibility.Visible : Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "⭐",
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        border.Child = new Grid { Children = { favBadge } };

        // Context Menu
        var cm = new ContextMenu();

        var miApply = new MenuItem { Header = "🖼️ Establecer como fondo" };
        miApply.Click += (_, _) => ChangeWallpaper(entry);

        var miFav = new MenuItem();
        void UpdateFavHeader() => miFav.Header = entry.IsFavorite ? "★ Quitar de favoritos" : "☆ Marcar como favorito";
        UpdateFavHeader();

        miFav.Click += (_, _) =>
        {
            entry.IsFavorite = !entry.IsFavorite;
            var favList = Services.SettingsService.Instance.FavoriteWallpapers;
            if (entry.IsFavorite)
            {
                if (!favList.Contains(entry.FilePath))
                    favList.Add(entry.FilePath);
            }
            else
            {
                favList.Remove(entry.FilePath);
            }
            Services.SettingsService.Save();
            UpdateFavHeader();
            favBadge.Visibility = entry.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
        };

        var miOpenExplorer = new MenuItem { Header = "📁 Mostrar en el Explorador" };
        miOpenExplorer.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{entry.FilePath}\"");
            }
            catch { }
        };

        var miCopyPath = new MenuItem { Header = "📋 Copiar ruta del archivo" };
        miCopyPath.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(entry.FilePath);
            }
            catch { }
        };

        cm.Items.Add(miApply);
        cm.Items.Add(new Separator());
        cm.Items.Add(miFav);
        cm.Items.Add(new Separator());
        cm.Items.Add(miOpenExplorer);
        cm.Items.Add(miCopyPath);

        border.ContextMenu = cm;

        // Load image using ThumbnailCacheService (disk cache + ultra-fast thumbnail load)
        Task.Run(async () =>
        {
            try
            {
                var bmp = await ThumbnailCacheService.Instance.GetThumbnailAsync(entry.FilePath, IMG_W * 2);
                if (bmp != null)
                {
                    entry.Thumbnail = bmp;
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

        border.MouseLeftButtonUp += (_, _) =>
        {
            ChangeWallpaper(entry);
        };

        return border;
    }

    private void ChangeWallpaper(WallpaperEntry entry)
    {
        // Sync index when wallpaper is manually changed
        _currentWallpaperIndex = _wallpapers.IndexOf(entry);

        // Sync Dock accent color instantly
        if (DockWindow.Instance != null)
        {
            DockWindow.Instance.UpdateAccentColorFromWallpaper(entry.FilePath);
        }

        WallpaperTransitionService.ApplyWallpaper(entry.FilePath, () =>
        {
            RefreshAllBackdrops();
        });
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
        _watcher?.Dispose();
        _debounceTimer?.Stop();
        _instance = null;
        base.OnClosed(e);
    }
}

