using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WallFlow.Services;
using WallFlow.Windows;
using NAudio.Wave;
using System.Net.NetworkInformation;

namespace WallFlow;

public class WindowItem
{
    public IntPtr Hwnd { get; set; }
    public string Title { get; set; } = "";
    public ImageSource? Icon { get; set; }
    public bool IsActive { get; set; }
    public bool IsMinimized { get; set; }
}

public partial class DockWindow : Window, IApplicationHost
{
    // ===== Win32 Constants =====

    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int GW_OWNER = 4;
    private const int WM_GETICON = 0x007F;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const int ICON_SMALL2 = 2;
    private const int GCLP_HICONSM = -34;
    private const int GCLP_HICON = -14;
    private const int ABM_NEW = 0;
    private const int ABM_REMOVE = 1;
    private const int ABM_QUERYPOS = 2;
    private const int ABM_SETPOS = 3;
    private const int ABE_TOP = 1;
    private const int ABE_BOTTOM = 3;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_HIDE = 0x8003;
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const int OBJID_WINDOW = 0;
    private const int CHILDID_SELF = 0;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int WM_APPCOMMAND = 0x0319;
    private const int APPCOMMAND_VOLUME_MUTE = 8;
    private const int APPCOMMAND_VOLUME_UP = 10;
    private const int APPCOMMAND_VOLUME_DOWN = 9;
    private const int WM_CLOSE = 0x0010;

    // VK Keyboard codes
    private const byte VK_LWIN = 0x5B;
    private const byte VK_N = 0x4E;
    private const byte VK_B = 0x42;
    private const byte VK_SPACE = 0x20;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    // ===== Win32 Structs =====

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize; public IntPtr hWnd; public int uCallbackMessage;
        public int uEdge; public RECT rc; public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus; public byte BatteryFlag; public byte BatteryLifePercent;
        public byte SystemStatusFlag; public uint BatteryLifeTime; public uint BatteryFullLifeTime;
    }

    // ===== P/Invoke =====

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
    private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hwndParent, EnumChildProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(IntPtr hProcess, ref uint AppUserModelIdLength, StringBuilder sb);

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
    private const uint SHERB_NOCONFIRMATION = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private const int WM_CLIPBOARDUPDATE = 0x031D;
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetClassLongW")] private static extern uint GetClassLong32(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);
    [DllImport("shell32.dll")] private static extern uint SHAppBarMessage(int dwMessage, ref APPBARDATA pData);
    [DllImport("user32.dll")] private static extern int RegisterWindowMessageW([MarshalAs(UnmanagedType.LPWStr)] string lpString);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    private const int DWMWA_CLOAKED = 14;
    private const int WM_COPYDATA = 0x004A;

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
        public MEMORYSTATUSEX()
        {
            this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime, 
        out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime, 
        out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private static int GetWindowLong(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? (int)GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetClassLongPtr64(hWnd, nIndex) : new IntPtr(GetClassLong32(hWnd, nIndex));

    // ===== Dependency Properties for Hover Lock State =====

    private static void OnOpenStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DockWindow dock)
        {
            dock.CheckAutoHide();
        }
    }

    public static readonly DependencyProperty IsLocalGalleryOpenProperty =
        DependencyProperty.Register(nameof(IsLocalGalleryOpen), typeof(bool), typeof(DockWindow), new PropertyMetadata(false, OnOpenStateChanged));

    public bool IsLocalGalleryOpen
    {
        get => (bool)GetValue(IsLocalGalleryOpenProperty);
        set => SetValue(IsLocalGalleryOpenProperty, value);
    }

    public static readonly DependencyProperty IsOnlineGalleryOpenProperty =
        DependencyProperty.Register(nameof(IsOnlineGalleryOpen), typeof(bool), typeof(DockWindow), new PropertyMetadata(false, OnOpenStateChanged));

    public bool IsOnlineGalleryOpen
    {
        get => (bool)GetValue(IsOnlineGalleryOpenProperty);
        set => SetValue(IsOnlineGalleryOpenProperty, value);
    }

    public static readonly DependencyProperty IsYouTubeMusicOpenProperty =
        DependencyProperty.Register(nameof(IsYouTubeMusicOpen), typeof(bool), typeof(DockWindow), new PropertyMetadata(false, OnOpenStateChanged));

    public bool IsYouTubeMusicOpen
    {
        get => (bool)GetValue(IsYouTubeMusicOpenProperty);
        set => SetValue(IsYouTubeMusicOpenProperty, value);
    }

    public static readonly DependencyProperty IsBrowserOpenProperty =
        DependencyProperty.Register(nameof(IsBrowserOpen), typeof(bool), typeof(DockWindow), new PropertyMetadata(false, OnOpenStateChanged));

    public bool IsBrowserOpen
    {
        get => (bool)GetValue(IsBrowserOpenProperty);
        set => SetValue(IsBrowserOpenProperty, value);
    }

    public static readonly DependencyProperty IsPowerWindowOpenProperty =
        DependencyProperty.Register(nameof(IsPowerWindowOpen), typeof(bool), typeof(DockWindow), new PropertyMetadata(false, OnOpenStateChanged));

    public bool IsPowerWindowOpen
    {
        get => (bool)GetValue(IsPowerWindowOpenProperty);
        set => SetValue(IsPowerWindowOpenProperty, value);
    }

    public static readonly DependencyProperty IsMutedActiveProperty =
        DependencyProperty.Register(nameof(IsMutedActive), typeof(bool), typeof(DockWindow), new PropertyMetadata(false));

    public bool IsMutedActive
    {
        get => (bool)GetValue(IsMutedActiveProperty);
        set => SetValue(IsMutedActiveProperty, value);
    }

    public static readonly DependencyProperty IsPinnedFlyoutOpenProperty =
        DependencyProperty.Register(nameof(IsPinnedFlyoutOpen), typeof(bool), typeof(DockWindow), new PropertyMetadata(false, OnOpenStateChanged));

    public bool IsPinnedFlyoutOpen
    {
        get => (bool)GetValue(IsPinnedFlyoutOpenProperty);
        set => SetValue(IsPinnedFlyoutOpenProperty, value);
    }

    public static readonly DependencyProperty IsSettingsOpenProperty =
        DependencyProperty.Register(nameof(IsSettingsOpen), typeof(bool), typeof(DockWindow), new PropertyMetadata(false, OnOpenStateChanged));

    public bool IsSettingsOpen
    {
        get => (bool)GetValue(IsSettingsOpenProperty);
        set => SetValue(IsSettingsOpenProperty, value);
    }

    // ===== Private Fields =====

    private int _appBarMessageId;
    private bool _isRegistered = false;
    private readonly DispatcherTimer _clockTimer;
    private System.Windows.Threading.DispatcherTimer? _autoHideDelayTimer;
    private System.Windows.Threading.DispatcherTimer? _mousePollTimer;

    private IntPtr _focusHook;
    private IntPtr _lifecycleHook;
    private WinEventProc? _hookDelegate;
    private List<WindowItem> _currentItems = new();
    private IntPtr _currentActiveHwnd = IntPtr.Zero;
    private readonly Dictionary<IntPtr, ImageSource> _iconCache = new();

    private int _statusUpdateCounter = 0;
    private bool _isMutedLocally = false;
    private bool _allMinimized = false;
    private bool _isClosing = false;
    private bool _isManuallyKeptOpen = false;
    private int _animationGeneration = 0;
    private bool _isToggleAnimating = false;
    private bool _isDockToggledVisible = true;

    // Context menu target
    private IntPtr _contextMenuHwnd = IntPtr.Zero;
    public bool IsContextMenuOpen { get; set; } = false;

    // Reference to toggle online gallery window
    private Windows.OnlineGalleryWindow? _onlineGalleryWindow;

    // Reference to toggle YouTube Music window
    private Windows.YouTubeMusicWindow? _youtubeMusicWindow;

    // Reference to toggle Web Browser windows
    private readonly List<System.Windows.Window> _browserWindows = new();

    // Reference to toggle power options window
    private Windows.PowerWindow? _powerWindow;

    // Reference to toggle weather effects window
    private Windows.WeatherEffectsWindow? _weatherEffectsWindow;

    // Reference to toggle calendar window
    private Windows.CalendarWindow? _calendarWindow;

    // Reference to toggle pinned apps window
    private Windows.PinnedAppsWindow? _pinnedAppsWindow;

    // Reference to toggle settings window
    private Windows.SettingsWindow? _settingsWindow;

    private DateTime _lastClosedTime = DateTime.MinValue;

    // Reference to persistent floating clipboard widget
    private Windows.ClipboardWidgetWindow? _clipboardWidgetWindow;

    // Performance monitor state variables
    private System.Runtime.InteropServices.ComTypes.FILETIME _prevIdleTime;
    private System.Runtime.InteropServices.ComTypes.FILETIME _prevKernelTime;
    private System.Runtime.InteropServices.ComTypes.FILETIME _prevUserTime;
    private bool _firstCpuCall = true;
    private bool _isUpdatingWeather;

    private long _prevBytesReceived = 0;
    private long _prevBytesSent = 0;
    private DateTime _prevNetTime = DateTime.UtcNow;

    private readonly List<System.Net.NetworkInformation.NetworkInterface> _cachedNetworkInterfaces = new();
    private readonly object _netCacheLock = new();
    private bool _netCacheInitialized = false;
    private string _cachedDateString = string.Empty;

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        lock (_netCacheLock)
        {
            _netCacheInitialized = false;
        }
    }





    // Reference to toggle media controller flyout
    private Windows.MediaFlyoutWindow? _mediaFlyoutWindow;

    // Media Playback Session variables (global:: to avoid namespace clashes)
    private global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager? _mediaSessionManager;
    private global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession? _currentMediaSession;
    private System.Threading.CancellationTokenSource? _mediaUpdateCts;
    private Storyboard? _soundWavesStoryboard;
    private bool _isMediaPlaying = false;

    public static DockWindow? Instance { get; private set; }

    // ===== Constructor =====

    public DockWindow()
    {
        InitializeComponent();
        Instance = this;
        ApplicationHost.Instance = this;

        // Clock timer (1s): updates clock, battery
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += ClockTimer_Tick;
        _clockTimer.Start();

        // Mouse polling timer (300ms) to ensure Auto-Hide is robust and doesn't get stuck hidden
        _mousePollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _mousePollTimer.Tick += MousePollTimer_Tick;
        _mousePollTimer.Start();



        // Initial state
        UpdateClock();
        UpdateBattery();
        UpdateRecycleBin();

        // Initial weather icon based on day/night
        bool isDay = DateTime.Now.Hour >= 6 && DateTime.Now.Hour < 18;
        WeatherButton.Content = isDay ? "\uE706" : "\uE708";

        IsVisibleChanged += Window_IsVisibleChanged;
        SizeChanged += Window_SizeChanged;

        // Prevent auto-hide when context menu is open
        AddHandler(ContextMenuService.ContextMenuOpeningEvent, new ContextMenuEventHandler((s, ev) => {
            IsContextMenuOpen = true;
        }));
        AddHandler(ContextMenuService.ContextMenuClosingEvent, new ContextMenuEventHandler((s, ev) => {
            IsContextMenuOpen = false;
            // Execute after layout finishes to avoid race conditions with MouseLeave
            Dispatcher.BeginInvoke(new Action(() => CheckAutoHide()), System.Windows.Threading.DispatcherPriority.Background);
        }));

        // Register global button click sound handler
        AddHandler(Button.ClickEvent, new RoutedEventHandler(OnAnyButtonClick));
    }

    // ===== Window Lifecycle =====

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        BackdropHelper.ApplyAcrylic(this);
        BackdropHelper.UpdateWindowRegion(this, 8);

        // Hover events for Auto-Hide
        MouseEnter += DockWindow_MouseEnter;
        MouseLeave += DockWindow_MouseLeave;



        // Register hover sound on all buttons (including custom-templated ones)
        RegisterButtonHoverSounds(this);

        var s = Services.SettingsService.Instance;
        if (s.HideWindowsTaskbar)
        {
            HideWindowsTaskbar();
        }

        if (s.AutoHide)
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            var screenIndex = Math.Clamp(s.DockMonitorIndex, 0, screens.Length - 1);
            var screen = screens[screenIndex];
            var dpi = VisualTreeHelper.GetDpi(this);

            Width = screen.Bounds.Width / dpi.DpiScaleX;
            Height = s.DockHeight;
            Left = screen.Bounds.X / dpi.DpiScaleX;
            Top = GetDockHiddenTop();
            TaskbarBorder.Width = Width;
            TaskbarBorder.Height = s.DockHeight;
        }
        else
        {
            RegisterAppBar();
        }
        RegisterHooks();
        RefreshWindows();

        // Initialize Media Playback
        InitMediaPlayback();

        // Load Weather
        UpdateWeather();

        // Register network change event handlers to invalidate network interface cache
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;

        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (netSender, netArgs) =>
        {
            if (netArgs.IsAvailable)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Services.WeatherService.Instance.ResetCache();
                    UpdateWeather();
                }));
            }
        };

        // Register MainWindow visibility observer to lock hover state
        var main = MainWindow.Instance;
        if (main != null)
        {
            main.IsVisibleChanged += (sender_main, args) =>
            {
                IsLocalGalleryOpen = main.Visibility == Visibility.Visible;
                CheckAutoHide();
            };
            // Set initial state
            IsLocalGalleryOpen = main.Visibility == Visibility.Visible;
        }

        // Initialize accent color matching the desktop wallpaper
        UpdateAccentColorFromWallpaper();

        // Initialize widget visibilities from settings
        ApplyWidgetVisibilities();

        // Initialize global hotkeys and WndProc hook
        try
        {
            var hWnd = new WindowInteropHelper(this).Handle;
            if (hWnd != IntPtr.Zero)
            {
                HwndSource.FromHwnd(hWnd)?.AddHook(WndProc);
            }
            HotkeyService.Instance.Initialize(hWnd);
            RegisterGlobalHotkeys();
        }
        catch { /* ignored */ }

        // Subscribe to clipboard updates to play the absorption animation
        try
        {
            ClipboardService.Instance.HistoryChanged += OnClipboardHistoryChanged;
        }
        catch { /* ignored */ }
    }

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (IsVisible)
        {
            if (!Services.SettingsService.Instance.AutoHide && !_isToggleAnimating)
            {
                RegisterAppBar();
            }
            RegisterHooks();
        }
        else
        {
            UnregisterHooks();
            UnregisterAppBar();
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        try
        {
            BackdropHelper.UpdateWindowRegion(this, 8);
        }
        catch { /* ignored */ }
    }

    protected override void OnClosed(EventArgs e)
    {
        ShowWindowsTaskbar();
        _clockTimer.Stop();
        _mousePollTimer?.Stop();
        _autoHideDelayTimer?.Stop();
        UnregisterHooks();
        UnregisterAppBar();
        
        try { ClipboardService.Instance.HistoryChanged -= OnClipboardHistoryChanged; } catch { }
        try { System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkChanged; } catch { }
        try { System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged; } catch { }

        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                HwndSource.FromHwnd(hwnd)?.RemoveHook(WndProc);
            }
        }
        catch { /* ignored */ }
        
        // Unregister global hotkeys
        try { HotkeyService.Instance.UnregisterAll(); } catch { }
        
        if (_weatherEffectsWindow != null)
        {
            _weatherEffectsWindow.Close();
            _weatherEffectsWindow = null;
        }

        // Clean media session events
        if (_currentMediaSession != null)
        {
            _currentMediaSession.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
            _currentMediaSession.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
        }
        if (_mediaSessionManager != null)
        {
            _mediaSessionManager.CurrentSessionChanged -= MediaSessionManager_CurrentSessionChanged;
        }

        if (_clipboardWidgetWindow != null)
        {
            try { _clipboardWidgetWindow.Close(); } catch { }
            _clipboardWidgetWindow = null;
        }


        if (_youtubeMusicWindow != null)
        {
            try { _youtubeMusicWindow.ForceClose(); } catch { }
            _youtubeMusicWindow = null;
        }

        SaveBrowserSession();
        _isClosing = true;

        foreach (var win in _browserWindows.ToArray())
        {
            try { win.ForceClose(); } catch { }
        }
        _browserWindows.Clear();

        base.OnClosed(e);
    }

    // ===== Window Enumeration =====

    private void RefreshWindows()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(RefreshWindows); return; }

        var activeHwnd = GetForegroundWindow();
        var ourHwnd = new WindowInteropHelper(this).Handle;

        // Exclude our dock from being tracked as the active user app window.
        if (activeHwnd != ourHwnd && activeHwnd != IntPtr.Zero)
        {
            _currentActiveHwnd = activeHwnd;
        }

        var tempItems = new List<WindowItem>();

        EnumWindows((hwnd, lParam) =>
        {
            if (IsTaskbarWindow(hwnd))
            {
                tempItems.Add(new WindowItem
                {
                    Hwnd = hwnd,
                    Title = GetWindowTextHelper(hwnd),
                    Icon = GetWindowIcon(hwnd),
                    IsActive = hwnd == _currentActiveHwnd,
                    IsMinimized = IsIconic(hwnd)
                });
            }
            return true;
        }, IntPtr.Zero);

        bool changed = tempItems.Count != _currentItems.Count;
        if (!changed)
        {
            for (int i = 0; i < tempItems.Count; i++)
            {
                if (tempItems[i].Hwnd != _currentItems[i].Hwnd ||
                    tempItems[i].Title != _currentItems[i].Title ||
                    tempItems[i].IsActive != _currentItems[i].IsActive ||
                    tempItems[i].IsMinimized != _currentItems[i].IsMinimized)
                { changed = true; break; }
            }
        }

        if (changed)
        {
            _currentItems = tempItems;
            WindowsList.ItemsSource = tempItems;

            // Clean up icon cache for closed windows
            var currentHwnds = new HashSet<IntPtr>();
            foreach (var item in tempItems)
            {
                currentHwnds.Add(item.Hwnd);
            }

            var toRemove = new List<IntPtr>();
            foreach (var hwnd in _iconCache.Keys)
            {
                if (!currentHwnds.Contains(hwnd))
                {
                    toRemove.Add(hwnd);
                }
            }

            foreach (var hwnd in toRemove)
            {
                _iconCache.Remove(hwnd);
            }

            UpdateSeparatorsVisibility();
        }
    }

    private bool IsTaskbarWindow(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd)) return false;
        var ourHwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == ourHwnd) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return false;
        if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;
        if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return false;

        // Filter out DWM cloaked windows (suspended UWP apps like settings or store)
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0)
        {
            if (cloaked != 0) return false;
        }

        var cn = GetClassNameHelper(hwnd);
        if (cn is "Shell_TrayWnd" or "Progman" or "Button") return false;
        if (string.IsNullOrWhiteSpace(GetWindowTextHelper(hwnd))) return false;
        return true;
    }

    private string GetWindowTextHelper(IntPtr hwnd) { var sb = new StringBuilder(512); GetWindowTextW(hwnd, sb, sb.Capacity); return sb.ToString().Trim(); }
    private string GetClassNameHelper(IntPtr hwnd) { var sb = new StringBuilder(256); GetClassNameW(hwnd, sb, sb.Capacity); return sb.ToString().Trim(); }

    private ImageSource? GetWindowIcon(IntPtr hwnd)
    {
        if (_iconCache.TryGetValue(hwnd, out var cachedIcon))
        {
            return cachedIcon;
        }

        try
        {
            var hIcon = SendMessageW(hwnd, WM_GETICON, new IntPtr(ICON_BIG), IntPtr.Zero);
            if (hIcon == IntPtr.Zero) hIcon = GetClassLongPtr(hwnd, GCLP_HICON);
            if (hIcon == IntPtr.Zero) hIcon = SendMessageW(hwnd, WM_GETICON, new IntPtr(ICON_SMALL), IntPtr.Zero);
            if (hIcon == IntPtr.Zero) hIcon = SendMessageW(hwnd, WM_GETICON, new IntPtr(ICON_SMALL2), IntPtr.Zero);
            if (hIcon == IntPtr.Zero) hIcon = GetClassLongPtr(hwnd, GCLP_HICONSM);

            if (hIcon != IntPtr.Zero)
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                _iconCache[hwnd] = bmp;
                return bmp;
            }

            GetWindowThreadProcessId(hwnd, out var pid);
            var path = GetProcessPath((int)pid);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (ico != null)
                {
                    var bmp = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bmp.Freeze();
                    _iconCache[hwnd] = bmp;
                    return bmp;
                }
            }
        }
        catch { /* ignored */ }
        return null;
    }

    private string GetProcessPath(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero) return "";
        try { var buf = new StringBuilder(1024); int sz = buf.Capacity; return QueryFullProcessImageNameW(handle, 0, buf, ref sz) ? buf.ToString() : ""; }
        catch { return ""; }
        finally { CloseHandle(handle); }
    }

    // ===== WinEvent Hooks =====

    private void RegisterHooks()
    {
        if (_hookDelegate != null) return;
        _hookDelegate = new WinEventProc(EventCallback);
        _focusHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _hookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        _lifecycleHook = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_HIDE, IntPtr.Zero, _hookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                AddClipboardFormatListener(hwnd);
            }
        }
        catch { /* ignored */ }
    }

    private void UnregisterHooks()
    {
        if (_hookDelegate == null) return;
        if (_focusHook != IntPtr.Zero) { UnhookWinEvent(_focusHook); _focusHook = IntPtr.Zero; }
        if (_lifecycleHook != IntPtr.Zero) { UnhookWinEvent(_lifecycleHook); _lifecycleHook = IntPtr.Zero; }
        _hookDelegate = null;

        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                RemoveClipboardFormatListener(hwnd);
            }
        }
        catch { /* ignored */ }
    }

    private void EventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject == OBJID_WINDOW && idChild == CHILDID_SELF) RefreshWindows();
    }

    // ===== AppBar =====

    private void RegisterAppBar()
    {
        if (_isRegistered) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        _appBarMessageId = RegisterWindowMessageW("WallFlowAppBarMessage");
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd, uCallbackMessage = _appBarMessageId };
        SHAppBarMessage(ABM_NEW, ref data);
        _isRegistered = true;
        SetAppBarPosition();
    }

    private void UnregisterAppBar()
    {
        if (!_isRegistered) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
        SHAppBarMessage(ABM_REMOVE, ref data);
        _isRegistered = false;
    }

    // ===== Windows Taskbar Hide/Show =====

    public void HideWindowsTaskbar()
    {
        try
        {
            var taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero && IsWindowVisible(taskbar)) 
                ShowWindow(taskbar, SW_HIDE);
            var secondary = FindWindow("Shell_SecondaryTrayWnd", null);
            if (secondary != IntPtr.Zero && IsWindowVisible(secondary)) 
                ShowWindow(secondary, SW_HIDE);
        }
        catch { /* ignored */ }
    }

    public void ShowWindowsTaskbar()
    {
        try
        {
            var taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero) ShowWindow(taskbar, SW_SHOW);
            var secondary = FindWindow("Shell_SecondaryTrayWnd", null);
                if (secondary != IntPtr.Zero) ShowWindow(secondary, SW_SHOW);
        }
        catch { /* ignored */ }
    }

    private (System.Windows.Forms.Screen Screen, System.Windows.DpiScale Dpi) GetCurrentScreenInfo()
    {
        var s = Services.SettingsService.Instance;
        var screens = System.Windows.Forms.Screen.AllScreens;
        var screenIndex = Math.Clamp(s.DockMonitorIndex, 0, screens.Length - 1);
        return (screens[screenIndex], VisualTreeHelper.GetDpi(this));
    }

    private double GetDockVisibleTop()
    {
        var s = Services.SettingsService.Instance;
        var (screen, dpi) = GetCurrentScreenInfo();
        if (s.DockPosition == "Bottom")
        {
            return (screen.Bounds.Y + screen.Bounds.Height) / dpi.DpiScaleY - s.DockHeight;
        }
        else
        {
            return screen.Bounds.Y / dpi.DpiScaleY;
        }
    }

    private double GetDockHiddenTop()
    {
        var s = Services.SettingsService.Instance;
        var (screen, dpi) = GetCurrentScreenInfo();
        if (s.DockPosition == "Bottom")
        {
            return (screen.Bounds.Y + screen.Bounds.Height) / dpi.DpiScaleY - 2;
        }
        else
        {
            return screen.Bounds.Y / dpi.DpiScaleY - (s.DockHeight - 2);
        }
    }

    private void SetAppBarPosition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var s = Services.SettingsService.Instance;
        var (screen, dpi) = GetCurrentScreenInfo();

        int h = (int)Math.Round(s.DockHeight * dpi.DpiScaleY);
        RECT rc;
        int edge;

        if (s.DockPosition == "Bottom")
        {
            rc = new RECT
            {
                Left = screen.Bounds.X,
                Top = screen.Bounds.Y + screen.Bounds.Height - h,
                Right = screen.Bounds.X + screen.Bounds.Width,
                Bottom = screen.Bounds.Y + screen.Bounds.Height
            };
            edge = ABE_BOTTOM;
        }
        else
        {
            rc = new RECT
            {
                Left = screen.Bounds.X,
                Top = screen.Bounds.Y,
                Right = screen.Bounds.X + screen.Bounds.Width,
                Bottom = screen.Bounds.Y + h
            };
            edge = ABE_TOP;
        }

        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd, uEdge = edge, rc = rc };
        SHAppBarMessage(ABM_QUERYPOS, ref data);
        SHAppBarMessage(ABM_SETPOS, ref data);
        Left = data.rc.Left / dpi.DpiScaleX; Top = data.rc.Top / dpi.DpiScaleY;
        Width = (data.rc.Right - data.rc.Left) / dpi.DpiScaleX; Height = (data.rc.Bottom - data.rc.Top) / dpi.DpiScaleY;
        TaskbarBorder.Width = Width;
        TaskbarBorder.Height = Height;

        if (s.HideWindowsTaskbar)
        {
            HideWindowsTaskbar();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _appBarMessageId && wParam.ToInt32() == 1) { SetAppBarPosition(); handled = true; }
        if (msg == WM_CLIPBOARDUPDATE)
        {
            ClipboardService.Instance.OnClipboardUpdated();
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
                    OpenNewBrowserWindow(url);
                }
            }
            catch { /* ignored */ }
            handled = true;
            return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    // ===== Clock, Battery =====

    private void ClockTimer_Tick(object? sender, EventArgs e)
    {
        _statusUpdateCounter++;

        // Check Game Mode every 3 seconds
        if (_statusUpdateCounter % 3 == 0)
        {
            CheckGameMode();
        }

        // If we are suspended for game mode, do not waste CPU updating UI clocks/battery/recycle bin
        if (_isSuspendedForGameMode)
        {
            return;
        }

        UpdateClock();
        if (_statusUpdateCounter % 10 == 0) UpdateBattery();      // Every 10s
        if (_statusUpdateCounter % 3 == 0) UpdateRecycleBin();    // Every 3s
        if (_statusUpdateCounter % 900 == 0) UpdateWeather();     // Every 15m

        UpdatePerformanceStats();
    }

    public void ApplyWidgetVisibilities()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(ApplyWidgetVisibilities); return; }

        var s = Services.SettingsService.Instance;

        // Toggle Mouse Poll Timer depending on settings to save CPU
        if (_mousePollTimer != null)
        {
            if (s.AutoHide)
            {
                if (!_mousePollTimer.IsEnabled) _mousePollTimer.Start();
            }
            else
            {
                if (_mousePollTimer.IsEnabled) _mousePollTimer.Stop();
            }
        }

        // Show/Hide Clock
        ClockBlock.Visibility = s.ShowClock ? Visibility.Visible : Visibility.Collapsed;

        // Show/Hide Weather
        WeatherButton.Visibility = s.ShowWeather ? Visibility.Visible : Visibility.Collapsed;

        // Show/Hide Performance sub-widgets
        CpuWidget.Visibility = s.ShowPerfCpu ? Visibility.Visible : Visibility.Collapsed;
        RamWidget.Visibility = s.ShowPerfRam ? Visibility.Visible : Visibility.Collapsed;
        NetWidget.Visibility = s.ShowPerfNet ? Visibility.Visible : Visibility.Collapsed;

        bool anyPerfVisible = s.ShowPerfCpu || s.ShowPerfRam || s.ShowPerfNet;
        PerfMonitorPanel.Visibility = anyPerfVisible ? Visibility.Visible : Visibility.Collapsed;
        PerfSeparator.Visibility = anyPerfVisible ? Visibility.Visible : Visibility.Collapsed;

        // If media player should not be shown, make sure it is hidden
        if (!s.ShowMediaPlayer)
        {
            HideMediaWidget();
        }
        else
        {
            UpdateMediaDetails();
        }

        // --- Dock Icon Visibilities ---
        PinnedAppsButton.Visibility = s.ShowIconPinnedApps ? Visibility.Visible : Visibility.Collapsed;
        LocalGalleryButton.Visibility = s.ShowIconLocalGallery ? Visibility.Visible : Visibility.Collapsed;
        OnlineGalleryButton.Visibility = s.ShowIconOnlineGallery ? Visibility.Visible : Visibility.Collapsed;
        YouTubeMusicButton.Visibility = s.ShowIconYouTubeMusic ? Visibility.Visible : Visibility.Collapsed;
        WebBrowserButton.Visibility = s.ShowIconWebBrowser ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = s.ShowIconSettings ? Visibility.Visible : Visibility.Collapsed;

        VolumeButton.Visibility = s.ShowIconVolume ? Visibility.Visible : Visibility.Collapsed;
        NotificationsButton.Visibility = s.ShowIconNotifications ? Visibility.Visible : Visibility.Collapsed;
        RecycleBinButton.Visibility = s.ShowIconRecycleBin ? Visibility.Visible : Visibility.Collapsed;
        ShowDesktopButton.Visibility = s.ShowIconShowDesktop ? Visibility.Visible : Visibility.Collapsed;
        PowerButton.Visibility = s.ShowIconPower ? Visibility.Visible : Visibility.Collapsed;

        // --- Separators Visibilities ---
        UpdateSeparatorsVisibility();
    }

    private void UpdateSeparatorsVisibility()
    {
        var s = Services.SettingsService.Instance;
        
        // Left apps separator: visible if at least one left icon is visible AND windows list has items
        bool anyLeftVisible = s.ShowIconPinnedApps || s.ShowIconLocalGallery || s.ShowIconOnlineGallery ||
                              s.ShowIconYouTubeMusic || s.ShowIconWebBrowser || s.ShowIconSettings;
                              
        bool hasOpenWindows = false;
        if (WindowsList != null && WindowsList.ItemsSource is System.Collections.IEnumerable enumerable)
        {
            var enumerator = enumerable.GetEnumerator();
            if (enumerator.MoveNext())
            {
                hasOpenWindows = true;
            }
        }
        
        if (LeftAppsSeparator != null)
        {
            LeftAppsSeparator.Visibility = (anyLeftVisible && hasOpenWindows) ? Visibility.Visible : Visibility.Collapsed;
        }

        // Right separator 1 (between notifications and recycle bin)
        bool group1Visible = s.ShowIconVolume || s.ShowIconNotifications;
        bool group2Visible = s.ShowIconRecycleBin || s.ShowIconShowDesktop;
        if (RightSeparator1 != null)
        {
            RightSeparator1.Visibility = (group1Visible && group2Visible) ? Visibility.Visible : Visibility.Collapsed;
        }

        // Right separator 2 (before power button)
        bool group3Visible = s.ShowIconPower;
        if (RightSeparator2 != null)
        {
            RightSeparator2.Visibility = (group3Visible && (group1Visible || group2Visible)) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdatePerformanceStats()
    {
        var s = Services.SettingsService.Instance;
        if (!s.ShowPerfCpu && !s.ShowPerfRam && !s.ShowPerfNet) return;

        try
        {
            if (s.ShowPerfCpu)
            {
                double cpu = GetCpuUsage();
                CpuText.Text = $"{cpu:F0}%";
            }
            if (s.ShowPerfRam)
            {
                double ram = GetRamUsage();
                RamText.Text = $"{ram:F0}%";
            }
            if (s.ShowPerfNet)
            {
                var (down, up) = GetNetworkSpeed();
                NetText.Text = FormatNetSpeed(down);
            }
        }
        catch { /* ignored */ }
    }

    private double GetCpuUsage()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            return 0;

        if (_firstCpuCall)
        {
            _firstCpuCall = false;
            _prevIdleTime = idleTime;
            _prevKernelTime = kernelTime;
            _prevUserTime = userTime;
            return 0;
        }

        ulong idle = ((ulong)idleTime.dwHighDateTime << 32) | (uint)idleTime.dwLowDateTime;
        ulong kernel = ((ulong)kernelTime.dwHighDateTime << 32) | (uint)kernelTime.dwLowDateTime;
        ulong user = ((ulong)userTime.dwHighDateTime << 32) | (uint)userTime.dwLowDateTime;

        ulong prevIdle = ((ulong)_prevIdleTime.dwHighDateTime << 32) | (uint)_prevIdleTime.dwLowDateTime;
        ulong prevKernel = ((ulong)_prevKernelTime.dwHighDateTime << 32) | (uint)_prevKernelTime.dwLowDateTime;
        ulong prevUser = ((ulong)_prevUserTime.dwHighDateTime << 32) | (uint)_prevUserTime.dwLowDateTime;

        _prevIdleTime = idleTime;
        _prevKernelTime = kernelTime;
        _prevUserTime = userTime;

        ulong idleDiff = idle - prevIdle;
        ulong kernelDiff = kernel - prevKernel;
        ulong userDiff = user - prevUser;

        ulong totalDiff = kernelDiff + userDiff;
        if (totalDiff == 0) return 0;

        double cpu = (double)(totalDiff - idleDiff) / totalDiff * 100.0;
        return Math.Clamp(cpu, 0.0, 100.0);
    }

    private double GetRamUsage()
    {
        var memStatus = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(memStatus))
        {
            return memStatus.dwMemoryLoad;
        }
        return 0;
    }

    private (double downloadSpeedBytes, double uploadSpeedBytes) GetNetworkSpeed()
    {
        long bytesReceived = 0;
        long bytesSent = 0;

        List<NetworkInterface> interfaces;
        lock (_netCacheLock)
        {
            if (!_netCacheInitialized)
            {
                _cachedNetworkInterfaces.Clear();
                try
                {
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus == OperationalStatus.Up && 
                            (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet || 
                             ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                        {
                            _cachedNetworkInterfaces.Add(ni);
                        }
                    }
                }
                catch { /* ignored */ }
                _netCacheInitialized = true;
            }
            interfaces = new List<NetworkInterface>(_cachedNetworkInterfaces);
        }

        foreach (var ni in interfaces)
        {
            try
            {
                var stats = ni.GetIPStatistics();
                bytesReceived += stats.BytesReceived;
                bytesSent += stats.BytesSent;
            }
            catch { /* ignored */ }
        }

        var now = DateTime.UtcNow;
        double secDiff = (now - _prevNetTime).TotalSeconds;
        if (secDiff <= 0) secDiff = 1.0;

        long rxDiff = bytesReceived - _prevBytesReceived;
        long txDiff = bytesSent - _prevBytesSent;

        if (_prevBytesReceived == 0 || rxDiff < 0) rxDiff = 0;
        if (_prevBytesSent == 0 || txDiff < 0) txDiff = 0;

        _prevBytesReceived = bytesReceived;
        _prevBytesSent = bytesSent;
        _prevNetTime = now;

        double downSpeed = rxDiff / secDiff;
        double upSpeed = txDiff / secDiff;

        return (downSpeed, upSpeed);
    }

    private string FormatNetSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 1024)
            return $"{bytesPerSec:F0} B/s";
        else if (bytesPerSec < 1024 * 1024)
            return $"{(bytesPerSec / 1024.0):F0} KB/s";
        else
            return $"{(bytesPerSec / (1024.0 * 1024.0)):F1} MB/s";
    }

    private void ContextEmptyRecycleBin_Click(object sender, RoutedEventArgs e)
    {
        Task.Run(() =>
        {
            try
            {
                SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION);
                Dispatcher.Invoke(() =>
                {
                    UpdateRecycleBin();
                });
            }
            catch { /* ignored */ }
        });
    }

    private void UpdateRecycleBin()
    {
        Task.Run(() =>
        {
            try
            {
                var info = new SHQUERYRBINFO();
                info.cbSize = Marshal.SizeOf(info);
                int hr = SHQueryRecycleBin(null, ref info);
                if (hr == 0)
                {
                    long itemCount = info.i64NumItems;
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            bool isEmpty = itemCount == 0;
                            
                            // Expose visual elements of the black hole
                            if (RecycleBinButton.Template != null)
                            {
                                var eventHorizon = RecycleBinButton.Template.FindName("EventHorizon", RecycleBinButton) as System.Windows.Shapes.Ellipse;
                                var accretionDisk = RecycleBinButton.Template.FindName("AccretionDisk", RecycleBinButton) as System.Windows.Shapes.Ellipse;
                                var accretionDiskInner = RecycleBinButton.Template.FindName("AccretionDiskInner", RecycleBinButton) as System.Windows.Shapes.Ellipse;
                                var accretionGlow = RecycleBinButton.Template.FindName("AccretionGlow", RecycleBinButton) as System.Windows.Shapes.Ellipse;

                                if (eventHorizon != null && accretionDisk != null && accretionDiskInner != null && accretionGlow != null)
                                {
                                    double itemsCount = itemCount;
                                    double baseHorizonSize = 8;
                                    double maxHorizonSize = 22;
                                    double newHorizonSize = baseHorizonSize + Math.Min(itemsCount * 0.4, maxHorizonSize - baseHorizonSize);
                                    
                                    eventHorizon.Width = newHorizonSize;
                                    eventHorizon.Height = newHorizonSize;

                                    if (isEmpty)
                                    {
                                        accretionGlow.Opacity = 0.25;
                                        accretionDisk.StrokeThickness = 2.0;
                                        accretionDiskInner.StrokeThickness = 1.0;
                                        accretionDiskInner.Opacity = 0.4;
                                        
                                        var glowBrush = new RadialGradientBrush();
                                        glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x35, 0xAF, 0x52, 0xDE), 0));
                                        glowBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
                                        accretionGlow.Fill = glowBrush;
                                    }
                                    else
                                    {
                                        accretionGlow.Opacity = Math.Min(0.4 + (itemsCount * 0.02), 0.95);
                                        accretionDisk.StrokeThickness = Math.Min(3.0 + (itemsCount * 0.1), 6.5);
                                        accretionDiskInner.StrokeThickness = Math.Min(1.5 + (itemsCount * 0.05), 3.0);
                                        accretionDiskInner.Opacity = Math.Min(0.6 + (itemsCount * 0.01), 0.9);
                                        
                                        // Accretion disk glow shifts color dynamically if very full (purple -> magenta)
                                        var glowBrush = new RadialGradientBrush();
                                        Color coreColor = itemsCount > 40 ? Color.FromRgb(0xFF, 0x00, 0x7F) : Color.FromRgb(0xAF, 0x52, 0xDE);
                                        glowBrush.GradientStops.Add(new GradientStop(coreColor, 0));
                                        glowBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
                                        accretionGlow.Fill = glowBrush;
                                    }
                                }
                            }

                            RecycleBinButton.ToolTip = isEmpty ? "Singularidad de Basura (Vacía)" : $"Singularidad de Basura ({itemCount} elementos)";
                        }
                        catch { /* ignored */ }
                    });
                }
            }
            catch { /* ignored */ }
        });
    }

    internal async void UpdateWeather()
    {
        if (_isUpdatingWeather) return;
        _isUpdatingWeather = true;
        try
        {
            var weather = await WeatherService.Instance.GetWeatherAsync();
            bool isDay = DateTime.Now.Hour >= 6 && DateTime.Now.Hour < 18;
            WeatherButton.Content = isDay ? "\uE706" : "\uE708";
            WeatherButton.ToolTip = $"Clima: {weather.Condition}, {Math.Round(weather.Temperature)}°C\nUbicación: {weather.City}";

            // Apply custom wallpaper effects selection
            string selectedEffect = SettingsService.Instance.SelectedWallpaperEffect ?? "Ninguno";
            if (selectedEffect != "Ninguno")
            {
                if (_weatherEffectsWindow == null)
                {
                    _weatherEffectsWindow = new Windows.WeatherEffectsWindow();
                    _weatherEffectsWindow.Closed += (s, args) => { _weatherEffectsWindow = null; };
                    _weatherEffectsWindow.Show();
                }
                _weatherEffectsWindow.SetEffect(selectedEffect);
            }
            else
            {
                if (_weatherEffectsWindow != null)
                {
                    _weatherEffectsWindow.Close();
                    _weatherEffectsWindow = null;
                }
            }
        }
        catch { /* ignored */ }
        finally { _isUpdatingWeather = false; }
    }

    private void WeatherButton_Click(object sender, RoutedEventArgs e)
    {
        ShowWidget("Weather");
    }

    public void UpdateClock()
    {
        var now = DateTime.Now;
        var dayOfWeek = now.ToString("dddd", System.Globalization.CultureInfo.CurrentCulture).ToUpper();
        var month = now.ToString("MMMM", System.Globalization.CultureInfo.CurrentCulture).ToUpper();

        string dateStr = $"{dayOfWeek}, {now.Day} DE {month}";
        if (dateStr != _cachedDateString)
        {
            _cachedDateString = dateStr;
            DateText.Text = dateStr;
        }

        var s = Services.SettingsService.Instance;
        if (s.Use24HourFormat)
        {
            TimeText.Text = now.ToString("HH:mm:ss");
        }
        else
        {
            TimeText.Text = now.ToString("hh:mm:ss tt");
        }
    }

    private void UpdateBattery()
    {
        if (GetSystemPowerStatus(out var s))
        {
            if (s.BatteryLifePercent == 255) 
            { 
                BatteryPanel.Visibility = Visibility.Collapsed; 
                BatterySeparator.Visibility = Visibility.Collapsed; 
                return; 
            }
            BatteryPanel.Visibility = Visibility.Visible;
            BatterySeparator.Visibility = Visibility.Visible;
            BatteryText.Text = $"{s.BatteryLifePercent}%";
            
            bool charging = s.ACLineStatus == 1;
            double percent = Math.Clamp((int)s.BatteryLifePercent, 0, 100);
            
            // Available inner height for BatteryFill is 21px.
            BatteryFill.Height = Math.Max(1, 21.0 * (percent / 100.0));

            if (charging)
            {
                // Green outline, tip, and fill + show white charging bolt overlay
                var greenColor = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
                BatteryFill.Background = greenColor;
                BatteryOutline.BorderBrush = greenColor;
                BatteryTip.Background = greenColor;
                BatteryChargeIndicator.Foreground = Brushes.White;
                BatteryChargeIndicator.Visibility = Visibility.Visible;
            }
            else
            {
                // Normal discharging mode
                BatteryChargeIndicator.Visibility = Visibility.Collapsed;
                BatteryOutline.BorderBrush = Brushes.White;
                BatteryTip.Background = Brushes.White;
                
                if (percent <= 20)
                {
                    // Red fill when under 20%
                    BatteryFill.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
                }
                else
                {
                    // Solid white fill
                    BatteryFill.Background = Brushes.White;
                }
            }
        }
        else 
        {
            BatteryPanel.Visibility = Visibility.Collapsed;
            BatterySeparator.Visibility = Visibility.Collapsed;
        }
    }

    // ===== Media Playback Integration =====

    private async void InitMediaPlayback()
    {
        try
        {
            _mediaSessionManager = await global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mediaSessionManager.CurrentSessionChanged += MediaSessionManager_CurrentSessionChanged;

            // Initial manual sync
            UpdateMediaSession(_mediaSessionManager.GetCurrentSession());
        }
        catch { /* ignored */ }
    }

    private void MediaSessionManager_CurrentSessionChanged(
        global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager sender,
        global::Windows.Media.Control.CurrentSessionChangedEventArgs args)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateMediaSession(sender.GetCurrentSession());
        });
    }

    private void UpdateMediaSession(global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession? session)
    {
        if (_currentMediaSession != null)
        {
            _currentMediaSession.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
            _currentMediaSession.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
        }

        _currentMediaSession = session;

        if (_currentMediaSession != null)
        {
            _currentMediaSession.MediaPropertiesChanged += Session_MediaPropertiesChanged;
            _currentMediaSession.PlaybackInfoChanged += Session_PlaybackInfoChanged;

            UpdateMediaDetails();
        }
        else
        {
            HideMediaWidget();
        }
    }

    private void Session_MediaPropertiesChanged(
        global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession sender,
        global::Windows.Media.Control.MediaPropertiesChangedEventArgs args)
    {
        Dispatcher.BeginInvoke(UpdateMediaDetails);
    }

    private void Session_PlaybackInfoChanged(
        global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession sender,
        global::Windows.Media.Control.PlaybackInfoChangedEventArgs args)
    {
        Dispatcher.BeginInvoke(UpdateMediaDetails);
    }

    private async void UpdateMediaDetails()
    {
        if (!Services.SettingsService.Instance.ShowMediaPlayer)
        {
            HideMediaWidget();
            return;
        }

        _mediaUpdateCts?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _mediaUpdateCts = cts;
        var token = cts.Token;

        await UpdateMediaPropertiesAsync(_currentMediaSession, token);
    }

    private async Task UpdateMediaPropertiesAsync(global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession? session, System.Threading.CancellationToken token)
    {
        if (!Services.SettingsService.Instance.ShowMediaPlayer)
        {
            HideMediaWidget();
            return;
        }

        if (session == null)
        {
            HideMediaWidget();
            return;
        }

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (token.IsCancellationRequested) return;

            var playback = session.GetPlaybackInfo();

            if (props == null || string.IsNullOrEmpty(props.Title))
            {
                HideMediaWidget();
                return;
            }

            // Reveal player widget and its separator
            MediaWidget.Visibility = Visibility.Visible;
            MediaSeparator.Visibility = Visibility.Visible;

            MediaTitleText.Text = props.Title;
            MediaArtistText.Text = props.Artist;

            // Control play icon state
            _isMediaPlaying = playback != null && playback.PlaybackStatus == global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // Control animated sound waves
            UpdateSoundWavesAnimationState();
        }
        catch
        {
            HideMediaWidget();
        }
    }

    private void HideMediaWidget()
    {
        MediaWidget.Visibility = Visibility.Collapsed;
        MediaSeparator.Visibility = Visibility.Collapsed;
        StopSoundWaves();
    }

    private void UpdateSoundWavesAnimationState()
    {
        bool dockVisible = !Services.SettingsService.Instance.AutoHide || Top == 0;
        if (_isMediaPlaying && dockVisible)
        {
            StartSoundWaves();
        }
        else
        {
            StopSoundWaves();
        }
    }

    private void StartSoundWaves()
    {
        StartSoundWavesAnimationOnly();
    }

    private void StopSoundWaves()
    {
        StopSoundWavesAnimationOnly();
    }

    private void StartSoundWavesAnimationOnly()
    {
        if (_soundWavesStoryboard == null)
        {
            _soundWavesStoryboard = Resources["SoundWavesPlaying"] as Storyboard;
        }
        _soundWavesStoryboard?.Begin(this, true);
    }

    private void StopSoundWavesAnimationOnly()
    {
        _soundWavesStoryboard?.Stop(this);
        // Reset wave bars to flat state by resetting ScaleY
        ResetWaveBarScale(WaveBar1);
        ResetWaveBarScale(WaveBar2);
        ResetWaveBarScale(WaveBar3);
        ResetWaveBarScale(WaveBar4);
        ResetWaveBarScale(WaveBar5);
        ResetWaveBarScale(WaveBar6);
    }

    private void ResetWaveBarScale(Border? bar)
    {
        if (bar != null && bar.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleY = 0.2;
        }
    }

    private void MediaWidget_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            ShowWidget("Media");
        }
    }

    // ===== Interactions =====

    private void ClockBlock_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            ShowWidget("Calendar");
        }
    }

    private void PinnedAppsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowWidget("PinnedApps");
    }

    private void WallFlowLogoButton_Click(object sender, RoutedEventArgs e)
    {
        var main = MainWindow.Instance;
        if (main == null) return;
        if (main.Visibility == Visibility.Visible)
        {
            main.Visibility = Visibility.Hidden;
            IsLocalGalleryOpen = false;
        }
        else
        {
            main.Visibility = Visibility.Visible;
            try { main.Activate(); } catch { }
            IsLocalGalleryOpen = true;
        }
    }

    private void OnlineGalleryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Toggle online gallery window: if already open, close it!
            if (_onlineGalleryWindow != null)
            {
                _onlineGalleryWindow.Close();
                _onlineGalleryWindow = null;
                IsOnlineGalleryOpen = false;
            }
            else
            {
                _onlineGalleryWindow = new Windows.OnlineGalleryWindow();
                IsOnlineGalleryOpen = true;
                _onlineGalleryWindow.Closed += (s, args) => {
                    _onlineGalleryWindow = null;
                    IsOnlineGalleryOpen = false;
                    ForceTopmost();
                    CheckAutoHide();
                };
                _onlineGalleryWindow.Show();
                _onlineGalleryWindow.Activate();
            }
        }
        catch
        {
            _onlineGalleryWindow = null;
            IsOnlineGalleryOpen = false;
        }
    }

    private void YouTubeMusicButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_youtubeMusicWindow != null)
            {
                // Debounce: If closed within the last 250ms (e.g. from losing focus/deactivating on this click),
                // ignore this toggle request to prevent it from reopening immediately.
                if ((DateTime.Now - _youtubeMusicWindow.LastHiddenTime).TotalMilliseconds < 250)
                {
                    return;
                }

                if (_youtubeMusicWindow.Visibility == Visibility.Visible)
                {
                    _youtubeMusicWindow.SafeHide();
                    IsYouTubeMusicOpen = false;
                }
                else
                {
                    IsYouTubeMusicOpen = true;
                    _youtubeMusicWindow.ShowWithAnimation();
                }
            }
            else
            {
                _youtubeMusicWindow = new Windows.YouTubeMusicWindow();
                IsYouTubeMusicOpen = true;
                _youtubeMusicWindow.Closed += (s, args) => {
                    _youtubeMusicWindow = null;
                    IsYouTubeMusicOpen = false;
                    ForceTopmost();
                    CheckAutoHide();
                };
                _youtubeMusicWindow.ShowWithAnimation();
            }
        }
        catch
        {
            _youtubeMusicWindow = null;
            IsYouTubeMusicOpen = false;
        }
    }

    private void WebBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OpenNewBrowserWindow();
        }
        catch
        {
            IsBrowserOpen = false;
        }
    }

    public void OpenNewBrowserWindow(string? initialUrl = null)
    {
        try
        {
            string exePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "NavegadorFlow.exe");

            if (File.Exists(exePath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = initialUrl ?? "",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            else
            {
                // Fallback: try to find it relative to the solution structure
                string fallback = @"C:\Users\Lenovo\OneDrive\Documentos\Proyectos\NavegadorFlow\bin\Release\net8.0-windows10.0.19041.0\NavegadorFlow.exe";
                if (File.Exists(fallback))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = fallback,
                        Arguments = initialUrl ?? "",
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
            }

            IsBrowserOpen = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al abrir NavegadorFlow: {ex.Message}");
        }
    }

    private void AttachBrowserWindowClosedHandler(System.Windows.Window win)
    {
        // No-op: NavegadorFlow manages its own windows
    }

    private void NewBrowserWindowMenu_Click(object sender, RoutedEventArgs e)
    {
        OpenNewBrowserWindow();
    }

    public void UpdateBrowserOpenState()
    {
        IsBrowserOpen = _browserWindows.Any(w => w.Visibility == Visibility.Visible);
    }

    public void SaveBrowserSession()
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallFlow");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            var filePath = Path.Combine(folder, "browser_session.json");

            var workspaces = _browserWindows
                .Select(w => w.ExportWorkspace())
                .ToList();

            var json = System.Text.Json.JsonSerializer.Serialize(workspaces);
            File.WriteAllText(filePath, json);
        }
        catch { /* ignored */ }
    }

    private void RestoreBrowserSession()
    {
        // If launched with an initial URL, open that instead of restoring session
        if (!string.IsNullOrEmpty(App.InitialUrl))
        {
            string url = App.InitialUrl;
            App.InitialUrl = null; // Clear it
            OpenNewBrowserWindow(url);
            return;
        }

        try
        {
            var filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallFlow",
                "browser_session.json");

            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                try
                {
                    var workspaces = System.Text.Json.JsonSerializer.Deserialize<List<Windows.WorkspaceSessionData>>(json);
                    if (workspaces != null && workspaces.Count > 0)
                    {
                        foreach (var ws in workspaces)
                        {
                            OpenNewBrowserWindowWithWorkspace(ws);
                        }
                        return;
                    }
                }
                catch
                {
                    var urls = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                    if (urls != null && urls.Count > 0)
                    {
                        foreach (var url in urls)
                        {
                            OpenNewBrowserWindow(url);
                        }
                        return;
                    }
                }
            }
        }
        catch { /* ignored */ }

        // Fallback to a single window with Google
        OpenNewBrowserWindow();
    }

    public void OpenNewBrowserWindowWithWorkspace(Windows.WorkspaceSessionData wsData)
    {
        try
        {
            var win = new Windows.BrowserWindow();
            win.RestoreWorkspace(wsData);
            
            double offset = 25 * (_browserWindows.Count % 6);
            win.CascadeOffset = offset;
            
            _browserWindows.Add(win);
            IsBrowserOpen = true;

            AttachBrowserWindowClosedHandler(win);

            win.ShowWithAnimation();
        }
        catch { /* ignored */ }
    }

    public void AddBrowserWindow(Windows.BrowserWindow win)
    {
        try
        {
            _browserWindows.Add(win);
            IsBrowserOpen = true;

            AttachBrowserWindowClosedHandler(win);
        }
        catch { /* ignored */ }
    }

    System.Windows.FrameworkElement? IApplicationHost.WebBrowserButton => this.WebBrowserButton;

    void IApplicationHost.AddBrowserWindow(System.Windows.Window window)
    {
        if (window is Windows.BrowserWindow win)
        {
            AddBrowserWindow(win);
        }
    }

    public int GetBrowserWindowsCount()
    {
        return _browserWindows.Count;
    }

    public void OrganizeBrowserWindows()
    {
        double currentLeft = -1;
        double currentTop = -1;
        var workArea = SystemParameters.WorkArea;

        for (int i = 0; i < _browserWindows.Count; i++)
        {
            var win = _browserWindows[i];
            
            if (i == 0)
            {
                win.ResetSizeAndPosition();
                currentLeft = win.Left;
                currentTop = win.Top;
            }
            else
            {
                win.Left = currentLeft + win.Width + 2;
                win.Top = currentTop;

                // Keep within screen work area
                if (win.Left + win.Width > workArea.Right - 10)
                {
                    win.Left = workArea.Left + 10;
                    win.Top = currentTop + 30;
                }

                currentLeft = win.Left;
                currentTop = win.Top;
            }
        }
    }

    private void BrowserContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        IsContextMenuOpen = true;
        var menu = sender as ContextMenu;
        if (menu == null) return;

        menu.Items.Clear();

        var newWinItem = new MenuItem { Header = "Nueva ventana" };
        newWinItem.Icon = new TextBlock 
        { 
            Text = "\xE710", 
            FontFamily = new FontFamily("Segoe MDL2 Assets"), 
            FontSize = 12, 
            VerticalAlignment = VerticalAlignment.Center 
        };
        newWinItem.Click += (s, args) => OpenNewBrowserWindow();
        menu.Items.Add(newWinItem);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Close();
                _settingsWindow = null;
                IsSettingsOpen = false;
            }
            else
            {
                _settingsWindow = new Windows.SettingsWindow();
                IsSettingsOpen = true;
                _settingsWindow.Closed += (s, args) => {
                    _settingsWindow = null;
                    IsSettingsOpen = false;
                    ForceTopmost();
                    CheckAutoHide();
                };
                _settingsWindow.Show();
                _settingsWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallFlow");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "crash.txt"), ex.ToString());
            }
            catch { /* ignored */ }
            _settingsWindow = null;
            IsSettingsOpen = false;
        }
    }

    private void WindowItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is IntPtr hwnd)
        {
            // Single-click toggle action:
            if (hwnd == _currentActiveHwnd)
            {
                ShowWindow(hwnd, 6); // Minimize
                _currentActiveHwnd = IntPtr.Zero;
            }
            else
            {
                if (IsIconic(hwnd)) ShowWindow(hwnd, 9);
                SetForegroundWindow(hwnd);
                _currentActiveHwnd = hwnd;
            }
            RefreshWindows();
        }
    }

    private void VolumeButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) SendMessageW(hwnd, WM_APPCOMMAND, hwnd, (IntPtr)(APPCOMMAND_VOLUME_MUTE << 16));
        _isMutedLocally = !_isMutedLocally;
        VolumeButton.Content = _isMutedLocally ? "\uE74F" : "\uE767";
        VolumeButton.ToolTip = _isMutedLocally ? "Activar Sonido" : "Silenciar";
        IsMutedActive = _isMutedLocally;
    }

    private void VolumeButton_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        int cmd = e.Delta > 0 ? APPCOMMAND_VOLUME_UP : APPCOMMAND_VOLUME_DOWN;
        SendMessageW(hwnd, WM_APPCOMMAND, hwnd, (IntPtr)(cmd << 16));
        e.Handled = true;
    }

    private void RecycleBinButton_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "shell:RecycleBinFolder", UseShellExecute = true }); }
        catch { /* ignored */ }
    }

    private static bool SendToRecycleBin(string path)
    {
        try
        {
            var shf = new SHFILEOPSTRUCT();
            shf.wFunc = FO_DELETE;
            shf.pFrom = path + '\0' + '\0'; // Double-null terminated
            shf.fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION;
            int result = SHFileOperation(ref shf);
            return result == 0;
        }
        catch
        {
            return false;
        }
    }

    private void RecycleBinButton_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            // Make the black hole pull visually (grow and intensify glow)
            if (RecycleBinButton.Template != null)
            {
                var scale = RecycleBinButton.Template.FindName("SingularityScale", RecycleBinButton) as ScaleTransform;
                var glow = RecycleBinButton.Template.FindName("AccretionGlow", RecycleBinButton) as System.Windows.Shapes.Ellipse;
                if (scale != null)
                {
                    scale.ScaleX = 1.35;
                    scale.ScaleY = 1.35;
                }
                if (glow != null)
                {
                    glow.Opacity = 0.95;
                }
            }
        }
    }

    private void RecycleBinButton_DragLeave(object sender, DragEventArgs e)
    {
        ResetSingularityScale();
    }

    private void ResetSingularityScale()
    {
        if (RecycleBinButton.Template != null)
        {
            var scale = RecycleBinButton.Template.FindName("SingularityScale", RecycleBinButton) as ScaleTransform;
            if (scale != null)
            {
                scale.ScaleX = 1.0;
                scale.ScaleY = 1.0;
            }
            UpdateRecycleBin(); // Restore original size/glow based on fullness
        }
    }

    private void RecycleBinButton_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
            e.Handled = true;

            ResetSingularityScale();

            if (files != null && files.Length > 0)
            {
                Task.Run(() =>
                {
                    bool success = false;
                    foreach (var file in files)
                    {
                        if (File.Exists(file) || Directory.Exists(file))
                        {
                            if (SendToRecycleBin(file))
                            {
                                success = true;
                            }
                        }
                    }

                    if (success)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            PlaySingularitySuctionSound();
                            TriggerImplosionAnimation();
                            UpdateRecycleBin();
                        });
                    }
                });
            }
        }
    }

    private void TriggerImplosionAnimation()
    {
        try
        {
            if (RecycleBinButton.Template != null)
            {
                var scale = RecycleBinButton.Template.FindName("SingularityScale", RecycleBinButton) as ScaleTransform;
                var flash = RecycleBinButton.Template.FindName("FlashOverlay", RecycleBinButton) as System.Windows.Shapes.Ellipse;

                if (scale != null && flash != null)
                {
                    // 1. Implosion: shrink black hole down to 0.1 scale rapidly
                    var shrinkX = new DoubleAnimation(1.0, 0.1, new Duration(TimeSpan.FromMilliseconds(200)))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    };
                    var shrinkY = new DoubleAnimation(1.0, 0.1, new Duration(TimeSpan.FromMilliseconds(200)))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    };

                    // When shrink completes, trigger flash and scale back up (explosion)
                    shrinkX.Completed += (s, ev) =>
                    {
                        // 2. Flash overlay fade in and out
                        var flashAnim = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(60)))
                        {
                            AutoReverse = true
                        };
                        flash.BeginAnimation(System.Windows.Shapes.Ellipse.OpacityProperty, flashAnim);

                        // 3. Bounce back to normal scale
                        var expandX = new DoubleAnimation(0.1, 1.0, new Duration(TimeSpan.FromMilliseconds(300)))
                        {
                            EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 4 }
                        };
                        var expandY = new DoubleAnimation(0.1, 1.0, new Duration(TimeSpan.FromMilliseconds(300)))
                        {
                            EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 4 }
                        };
                        scale.BeginAnimation(ScaleTransform.ScaleXProperty, expandX);
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, expandY);
                    };

                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrinkX);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrinkY);
                }
            }
        }
        catch { /* ignored */ }
    }

    private void PlaySingularitySuctionSound()
    {
        Task.Run(() =>
        {
            try
            {
                int sampleRate = 44100;
                double duration = 0.65;
                int totalSamples = (int)(sampleRate * duration);
                float[] buffer = new float[totalSamples];

                for (int i = 0; i < totalSamples; i++)
                {
                    double t = (double)i / sampleRate;
                    // Linear frequency sweep from 650Hz down to 60Hz (suction effect)
                    double frequency = 650.0 - (590.0 * (t / duration));
                    
                    // Suction sound volume shape: swells slightly at start, then fades to zero
                    double volume = 0.0;
                    if (t < 0.15)
                        volume = (t / 0.15) * 0.18; // Swell
                    else
                        volume = (1.0 - ((t - 0.15) / (duration - 0.15))) * 0.18; // Fade

                    buffer[i] = (float)(volume * Math.Sin(2.0 * Math.PI * frequency * t));
                }

                byte[] byteBuffer = new byte[buffer.Length * 4];
                Buffer.BlockCopy(buffer, 0, byteBuffer, 0, byteBuffer.Length);
                
                var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
                var ms = new MemoryStream(byteBuffer);
                var rawSource = new RawSourceWaveStream(ms, waveFormat);
                var waveOut = new WaveOutEvent();
                
                waveOut.PlaybackStopped += (s, ev) =>
                {
                    waveOut.Dispose();
                    rawSource.Dispose();
                    ms.Dispose();
                };
                
                waveOut.Init(rawSource);
                waveOut.Play();
            }
            catch { /* ignored */ }
        });
    }

    private void ShowDesktopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                object? shell = Activator.CreateInstance(shellType);
                if (!_allMinimized)
                {
                    shellType.InvokeMember("MinimizeAll", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
                    _allMinimized = true;
                }
                else
                {
                    shellType.InvokeMember("UndoMinimizeALL", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
                    _allMinimized = false;
                }
            }
        }
        catch { /* ignored */ }
    }

    // ===== System Tray & Notifications =====

    private void NotificationsButton_Click(object sender, RoutedEventArgs e)
    {
        // Simulate pressing Win + N to toggle the notification center and calendar flyout
        keybd_event(VK_LWIN, 0, 0, 0);
        keybd_event(VK_N, 0, 0, 0);
        keybd_event(VK_N, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, 0);
    }

    // ===== Power Options =====

    private void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_powerWindow != null)
            {
                _powerWindow.Close();
                _powerWindow = null;
                IsPowerWindowOpen = false;
            }
            else
            {
                _powerWindow = new Windows.PowerWindow();
                IsPowerWindowOpen = true;
                _powerWindow.Closed += (s, args) => {
                    _powerWindow = null;
                    IsPowerWindowOpen = false;
                    ForceTopmost();
                    CheckAutoHide();
                };
                _powerWindow.Show();
                _powerWindow.Activate();
            }
        }
        catch
        {
            _powerWindow = null;
            IsPowerWindowOpen = false;
        }
    }

    // ===== Context Menu =====

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        IsContextMenuOpen = true;
    }

    private void ContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        IsContextMenuOpen = false;
        // Execute after layout finishes to avoid race conditions with MouseLeave
        Dispatcher.BeginInvoke(new Action(() => CheckAutoHide()), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void WindowButton_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is Button btn && btn.Tag is IntPtr hwnd) _contextMenuHwnd = hwnd;
        IsContextMenuOpen = true;
    }

    private void ContextClose_Click(object sender, RoutedEventArgs e)
    {
        IsContextMenuOpen = false;
        if (_contextMenuHwnd != IntPtr.Zero) { PostMessageW(_contextMenuHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); RefreshWindows(); }
    }

    private void ContextMinimize_Click(object sender, RoutedEventArgs e)
    {
        IsContextMenuOpen = false;
        if (_contextMenuHwnd != IntPtr.Zero) ShowWindow(_contextMenuHwnd, 6);
    }

    private void ContextRestore_Click(object sender, RoutedEventArgs e)
    {
        IsContextMenuOpen = false;
        if (_contextMenuHwnd != IntPtr.Zero) { ShowWindow(_contextMenuHwnd, 9); SetForegroundWindow(_contextMenuHwnd); }
    }

    private void ContextMinimizeAll_Click(object sender, RoutedEventArgs e)
    {
        IsContextMenuOpen = false;
        foreach (var item in _currentItems.ToArray())
            ShowWindow(item.Hwnd, 6);
    }

    private void ContextPin_Click(object sender, RoutedEventArgs e)
    {
        IsContextMenuOpen = false;
        if (_contextMenuHwnd == IntPtr.Zero) return;

        try
        {
            GetWindowThreadProcessId(_contextMenuHwnd, out var pid);
            if (pid != 0)
            {
                var targetPid = pid;

                // Check if UWP wrapper window (ApplicationFrameHost)
                try
                {
                    using (var proc = Process.GetProcessById((int)pid))
                    {
                        if (string.Equals(proc.ProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                        {
                            var hostPid = pid;
                            EnumChildWindows(_contextMenuHwnd, (hwnd, lParam) =>
                            {
                                GetWindowThreadProcessId(hwnd, out var childPid);
                                if (childPid != 0 && childPid != hostPid)
                                {
                                    targetPid = childPid;
                                    return false; // Stop enumeration
                                }
                                return true; // Continue enumeration
                            }, IntPtr.Zero);
                        }
                    }
                }
                catch { /* ignored */ }

                var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, targetPid);
                if (handle != IntPtr.Zero)
                {
                    try
                    {
                        var sb = new StringBuilder(1024);
                        int size = sb.Capacity;
                        if (QueryFullProcessImageNameW(handle, 0, sb, ref size))
                        {
                            var path = sb.ToString();
                            if (!string.IsNullOrEmpty(path))
                            {
                                // Check if UWP App (has ApplicationUserModelId)
                                var aumidBuilder = new StringBuilder(1024);
                                uint aumidLength = (uint)aumidBuilder.Capacity;
                                if (GetApplicationUserModelId(handle, ref aumidLength, aumidBuilder) == 0) // SUCCESS
                                {
                                    var aumid = aumidBuilder.ToString();
                                    path = $"uwp:{aumid}|{path}";
                                }

                                if (SettingsService.Instance.PinnedAppPaths == null)
                                {
                                    SettingsService.Instance.PinnedAppPaths = new List<string>();
                                }

                                if (!SettingsService.Instance.PinnedAppPaths.Contains(path))
                                {
                                    SettingsService.Instance.PinnedAppPaths.Add(path);
                                    SettingsService.Save();
                                    MessageBox.Show("Aplicación anclada correctamente al Dock.", "Anclar al Dock", MessageBoxButton.OK, MessageBoxImage.Information);
                                }
                                else
                                {
                                    MessageBox.Show("Esta aplicación ya está anclada al Dock.", "Anclar al Dock", MessageBoxButton.OK, MessageBoxImage.Warning);
                                }
                            }
                        }
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo obtener la ruta del proceso para anclar: {ex.Message}", "Error al anclar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ===== Wallpaper Accent Matching =====

    public void UpdateAccentColorFromWallpaper(string? wallpaperPath = null)
    {
        try
        {
            if (string.IsNullOrEmpty(wallpaperPath))
            {
                wallpaperPath = MainWindow.GetCurrentWallpaper();
            }

            if (string.IsNullOrEmpty(wallpaperPath) || !System.IO.File.Exists(wallpaperPath))
            {
                SetDefaultGradient();
                return;
            }

            // Run image decoding and color calculation in a background thread to prevent UI thread lag / mouse stuttering
            Task.Run(() =>
            {
                try
                {
                    System.Windows.Media.Color dominantColor;

                    using (var bmp = new System.Drawing.Bitmap(wallpaperPath))
                    {
                        using (var smallBmp = new System.Drawing.Bitmap(bmp, new System.Drawing.Size(16, 16)))
                        {
                            long r = 0, g = 0, b = 0;
                            int count = 0;
                            var vibrantColors = new List<System.Drawing.Color>();

                            for (int x = 0; x < smallBmp.Width; x++)
                            {
                                for (int y = 0; y < smallBmp.Height; y++)
                                {
                                    var pixel = smallBmp.GetPixel(x, y);
                                    r += pixel.R;
                                    g += pixel.G;
                                    b += pixel.B;
                                    count++;

                                    float max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) / 255f;
                                    float min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) / 255f;
                                    float lightnessVal = (max + min) / 2f;
                                    float saturationVal = 0f;
                                    if (max != min)
                                    {
                                        saturationVal = lightnessVal > 0.5f ? (max - min) / (2f - max - min) : (max - min) / (max + min);
                                    }

                                    if (saturationVal > 0.15f && lightnessVal > 0.15f && lightnessVal < 0.85f)
                                    {
                                        vibrantColors.Add(pixel);
                                    }
                                }
                            }

                            if (vibrantColors.Count > 0)
                            {
                                long vr = 0, vg = 0, vb = 0;
                                foreach (var color in vibrantColors)
                                {
                                    vr += color.R;
                                    vg += color.G;
                                    vb += color.B;
                                }
                                dominantColor = System.Windows.Media.Color.FromRgb(
                                    (byte)(vr / vibrantColors.Count),
                                    (byte)(vg / vibrantColors.Count),
                                    (byte)(vb / vibrantColors.Count)
                                );
                            }
                            else
                            {
                                dominantColor = System.Windows.Media.Color.FromRgb(
                                    (byte)(r / count),
                                    (byte)(g / count),
                                    (byte)(b / count)
                                );
                            }
                        }
                    }

                    ColorToHsl(dominantColor, out double h, out double s, out double l);
                    var color1 = ColorFromHsl((h - 0.083 + 1.0) % 1.0, Math.Min(s + 0.1, 1.0), Math.Min(l + 0.1, 0.8));
                    var color2 = dominantColor;
                    var color3 = ColorFromHsl((h + 0.083) % 1.0, Math.Min(s + 0.1, 1.0), Math.Min(l + 0.1, 0.8));

                    // Post the calculated colors back to the UI thread to safely update WPF visual elements
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (TaskbarBorder.BorderBrush is LinearGradientBrush lgb && lgb.GradientStops.Count >= 3)
                            {
                                if (lgb.IsFrozen)
                                {
                                    lgb = lgb.Clone();
                                    TaskbarBorder.BorderBrush = lgb;
                                }
                                lgb.GradientStops[0].Color = color1;
                                lgb.GradientStops[1].Color = color2;
                                lgb.GradientStops[2].Color = color3;
                            }

                            Application.Current.Resources["AccentColor"] = color2;
                            Application.Current.Resources["AccentHoverColor"] = color1;
                            Application.Current.Resources["AccentBrush"] = new SolidColorBrush(color2);
                            Application.Current.Resources["AccentHoverBrush"] = new SolidColorBrush(color1);
                            Application.Current.Resources["AccentGlowBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x18, color2.R, color2.G, color2.B));
                            Application.Current.Resources["AccentActiveBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x65, color2.R, color2.G, color2.B));

                            var waveBrush = new LinearGradientBrush
                            {
                                StartPoint = new Point(0, 0),
                                EndPoint = new Point(0, 1)
                            };
                            waveBrush.GradientStops.Add(new GradientStop(color1, 0));
                            waveBrush.GradientStops.Add(new GradientStop(color2, 1));

                            if (WaveBar1 != null) WaveBar1.Background = waveBrush;
                            if (WaveBar2 != null) WaveBar2.Background = waveBrush;
                            if (WaveBar3 != null) WaveBar3.Background = waveBrush;
                            if (WaveBar4 != null) WaveBar4.Background = waveBrush;
                            if (WaveBar5 != null) WaveBar5.Background = waveBrush;
                            if (WaveBar6 != null) WaveBar6.Background = waveBrush;

                            ApplyCustomColors();
                        }
                        catch { /* ignored */ }
                    }));
                }
                catch
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        SetDefaultGradient();
                    }));
                }
            });
        }
        catch
        {
            SetDefaultGradient();
        }
    }

    private void SetDefaultGradient()
    {
        var c1 = System.Windows.Media.Color.FromRgb(0, 122, 255);
        var c2 = System.Windows.Media.Color.FromRgb(88, 86, 214);
        var c3 = System.Windows.Media.Color.FromRgb(175, 82, 222);

        if (TaskbarBorder.BorderBrush is LinearGradientBrush lgb && lgb.GradientStops.Count >= 3)
        {
            if (lgb.IsFrozen)
            {
                lgb = lgb.Clone();
                TaskbarBorder.BorderBrush = lgb;
            }
            lgb.GradientStops[0].Color = c1;
            lgb.GradientStops[1].Color = c2;
            lgb.GradientStops[2].Color = c3;
        }

        // Reset global application resources to defaults
        Application.Current.Resources["AccentColor"] = c2;
        Application.Current.Resources["AccentHoverColor"] = c1;
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush(c2);
        Application.Current.Resources["AccentHoverBrush"] = new SolidColorBrush(c1);
        Application.Current.Resources["AccentGlowBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x18, c2.R, c2.G, c2.B));
        Application.Current.Resources["AccentActiveBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x65, c2.R, c2.G, c2.B));

        var defaultWaveBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        defaultWaveBrush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(211, 155, 255), 0));
        defaultWaveBrush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(175, 82, 222), 1));

        if (WaveBar1 != null) WaveBar1.Background = defaultWaveBrush;
        if (WaveBar2 != null) WaveBar2.Background = defaultWaveBrush;
        if (WaveBar3 != null) WaveBar3.Background = defaultWaveBrush;
        if (WaveBar4 != null) WaveBar4.Background = defaultWaveBrush;
        if (WaveBar5 != null) WaveBar5.Background = defaultWaveBrush;
        if (WaveBar6 != null) WaveBar6.Background = defaultWaveBrush;

        ApplyCustomColors();
    }

    private static void ColorToHsl(System.Windows.Media.Color color, out double h, out double s, out double l)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));

        h = 0;
        s = 0;
        l = (max + min) / 2.0;

        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

            if (max == r)
                h = (g - b) / d + (g < b ? 6.0 : 0.0);
            else if (max == g)
                h = (b - r) / d + 2.0;
            else if (max == b)
                h = (r - g) / d + 4.0;

            h /= 6.0;
        }
    }

    private static System.Windows.Media.Color ColorFromHsl(double h, double s, double l)
    {
        double r, g, b;

        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            double Hue2Rgb(double p, double q, double t)
            {
                if (t < 0.0) t += 1.0;
                if (t > 1.0) t -= 1.0;
                if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
                if (t < 1.0 / 2.0) return q;
                if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
                return p;
            }

            double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
            double p = 2.0 * l - q;

            r = Hue2Rgb(p, q, h + 1.0 / 3.0);
            g = Hue2Rgb(p, q, h);
            b = Hue2Rgb(p, q, h - 1.0 / 3.0);
        }

        return System.Windows.Media.Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    // ===== Custom Color Overrides from Settings =====

    private bool _isSuspendedForBrowserFullScreen = false;

    public bool IsAnyBrowserFullScreen()
    {
        try
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is Windows.BrowserWindow browserWindow && browserWindow.IsFullScreen)
                {
                    return true;
                }
            }
        }
        catch { /* ignored */ }
        return false;
    }

    public void OnBrowserFullScreenChanged(bool isFullScreen)
    {
        try
        {
            bool anyFullScreen = IsAnyBrowserFullScreen();
            var s = Services.SettingsService.Instance;

            if (anyFullScreen)
            {
                if (_isSuspendedForBrowserFullScreen) return;
                _isSuspendedForBrowserFullScreen = true;

                Topmost = false;
                Visibility = Visibility.Collapsed;

                if (_isRegistered)
                {
                    UnregisterAppBar();
                }
            }
            else
            {
                if (!_isSuspendedForBrowserFullScreen) return;
                _isSuspendedForBrowserFullScreen = false;

                if (_isDockToggledVisible)
                {
                    Visibility = Visibility.Visible;
                    if (s.AutoHide)
                    {
                        Top = IsMousePhysicallyOverDock() ? GetDockVisibleTop() : GetDockHiddenTop();
                    }
                    else
                    {
                        Top = GetDockVisibleTop();
                    }
                }

                if (!s.AutoHide && !_isRegistered && _isDockToggledVisible)
                {
                    RegisterAppBar();
                }

                ForceTopmost();
            }
        }
        catch { /* ignored */ }
    }

    public void ForceTopmost()
    {
        try
        {
            if (Visibility != Visibility.Visible) return;
            if (IsAnyBrowserFullScreen()) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            Topmost = false;
            Topmost = true;
        }
        catch { /* ignored */ }
    }

    public void ApplySpotlightSettings()
    {
        if (_pinnedAppsWindow != null)
        {
            try
            {
                _pinnedAppsWindow.ApplySettings();
            }
            catch { /* ignored */ }
        }
    }

    public void ApplyBrowserColors()
    {
        foreach (var win in _browserWindows.ToArray())
        {
            try
            {
                win.ApplyBrowserColors();
            }
            catch { /* ignored */ }
        }
    }

    public void ApplyCustomColors()
    {
        try
        {
            var s = Services.SettingsService.Instance;

            if (s.HideWindowsTaskbar)
            {
                HideWindowsTaskbar();
            }
            else
            {
                ShowWindowsTaskbar();
            }

            // Apply custom border thickness and background color
            Brush bgBrush;
            if (!string.IsNullOrEmpty(s.UserDockBackgroundColor) && TryParseColor(s.UserDockBackgroundColor, out var bgColor))
            {
                bgBrush = new SolidColorBrush(bgColor);
            }
            else
            {
                bgBrush = new SolidColorBrush(Color.FromArgb(0xB4, 0x0B, 0x0B, 0x0B));
            }

            try
            {
                Application.Current.Resources["DockBackgroundBrush"] = bgBrush;
            }
            catch { /* ignored */ }

            if (TaskbarBorder != null)
            {
                TaskbarBorder.BorderThickness = new Thickness(s.BorderThickness);
                TaskbarBorder.Height = s.DockHeight;
                TaskbarBorder.Background = bgBrush;
            }

            // Apply scale to the main grid
            double scale = s.DockHeight / 56.0;
            if (MainGrid != null)
            {
                MainGrid.LayoutTransform = new ScaleTransform(scale, scale);
            }

            // Apply Auto-Hide transition
            if (s.AutoHide)
            {
                if (_isRegistered)
                {
                    UnregisterAppBar();
                }
                var (screen, dpi) = GetCurrentScreenInfo();
                Width = screen.Bounds.Width / dpi.DpiScaleX;
                Height = s.DockHeight;
                Left = screen.Bounds.X / dpi.DpiScaleX;

                double targetTop = (IsMousePhysicallyOverDock() || IsAnyWindowOpen()) ? GetDockVisibleTop() : GetDockHiddenTop();
                AnimateDock(targetTop);
                if (TaskbarBorder != null) TaskbarBorder.Width = Width;
                ForceTopmost();
            }
            else
            {
                BeginAnimation(TopProperty, null);
                if (!_isRegistered)
                {
                    RegisterAppBar();
                }
                else
                {
                    SetAppBarPosition();
                }
            }

            // Apply custom accent/hover color
            if (!string.IsNullOrEmpty(s.UserAccentColor) && TryParseColor(s.UserAccentColor, out var accent))
            {
                var hover = Lighten(accent);
                Application.Current.Resources["AccentColor"] = accent;
                Application.Current.Resources["AccentHoverColor"] = hover;
                Application.Current.Resources["AccentBrush"] = new SolidColorBrush(accent);
                Application.Current.Resources["AccentHoverBrush"] = new SolidColorBrush(hover);
                Application.Current.Resources["AccentGlowBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x18, accent.R, accent.G, accent.B));
                Application.Current.Resources["AccentActiveBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x65, accent.R, accent.G, accent.B));

                // Update media visualizer wave bars
                var waveBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1)
                };
                waveBrush.GradientStops.Add(new GradientStop(hover, 0));
                waveBrush.GradientStops.Add(new GradientStop(accent, 1));

                if (WaveBar1 != null) WaveBar1.Background = waveBrush;
                if (WaveBar2 != null) WaveBar2.Background = waveBrush;
                if (WaveBar3 != null) WaveBar3.Background = waveBrush;
                if (WaveBar4 != null) WaveBar4.Background = waveBrush;
                if (WaveBar5 != null) WaveBar5.Background = waveBrush;
                if (WaveBar6 != null) WaveBar6.Background = waveBrush;
            }

            // Determine and apply custom border brush (gradient or accent)
            Brush finalBorderBrush;
            if (!string.IsNullOrEmpty(s.UserBorderColor1) &&
                !string.IsNullOrEmpty(s.UserBorderColor2) &&
                !string.IsNullOrEmpty(s.UserBorderColor3) &&
                TryParseColor(s.UserBorderColor1, out var c1) &&
                TryParseColor(s.UserBorderColor2, out var c2) &&
                TryParseColor(s.UserBorderColor3, out var c3))
            {
                var lgb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                lgb.GradientStops.Add(new GradientStop(c1, 0));
                lgb.GradientStops.Add(new GradientStop(c2, 0.5));
                lgb.GradientStops.Add(new GradientStop(c3, 1));
                finalBorderBrush = lgb;
            }
            else
            {
                finalBorderBrush = Application.Current.Resources["AccentBrush"] as Brush 
                    ?? new SolidColorBrush(Color.FromRgb(0xAF, 0x52, 0xDE));
            }

            try
            {
                Application.Current.Resources["DockBorderBrush"] = finalBorderBrush;
            }
            catch { /* ignored */ }

            if (TaskbarBorder != null)
            {
                TaskbarBorder.BorderBrush = finalBorderBrush;
            }

            // Force recalculate window clipping region after dynamic scale updates
            BackdropHelper.UpdateWindowRegion(this, 8);
        }
        catch { /* ignored */ }
    }

    private static System.Windows.Media.Color Lighten(System.Windows.Media.Color color)
    {
        byte r = (byte)Math.Min(255, color.R + 35);
        byte g = (byte)Math.Min(255, color.G + 35);
        byte b = (byte)Math.Min(255, color.B + 35);
        return System.Windows.Media.Color.FromRgb(r, g, b);
    }

    private static bool TryParseColor(string hex, out System.Windows.Media.Color color)
    {
        color = System.Windows.Media.Colors.Transparent;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        hex = hex.Trim();
        if (!hex.StartsWith("#")) hex = "#" + hex;
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted != null)
            {
                color = (System.Windows.Media.Color)converted;
                return true;
            }
        }
        catch { /* ignored */ }
        return false;
    }

    private Window? _activeWidget;
    private string? _activeWidgetType; // "Calendar", "PinnedApps", "Weather"
    private DateTime _lastWidgetCloseTime = DateTime.MinValue;
    private string? _lastClosedWidgetType;

    private void ShowWidget(string type)
    {
        // Ignorar el clic si el mismo widget fue cerrado recientemente por pérdida de foco (evita condición de carrera de deactivación-click)
        if (_lastClosedWidgetType == type && (DateTime.Now - _lastWidgetCloseTime).TotalMilliseconds < 250)
        {
            _lastWidgetCloseTime = DateTime.MinValue;
            _lastClosedWidgetType = null;
            return;
        }

        if (_activeWidget != null)
        {
            if (_activeWidgetType == type)
            {
                CloseActiveWidget();
                return;
            }
            else
            {
                CloseActiveWidget();
            }
        }

        _activeWidgetType = type;

        // Play transition whoosh sound
        SoundService.Play(SoundType.Whoosh);

        try
        {
            if (type == "Calendar")
            {
                if (_activeWidget != null && _activeWidgetType == type) return;

                var dpi = VisualTreeHelper.GetDpi(this);
                var screenPos = ClockBlock.PointToScreen(new Point(0, 0));
                double left = screenPos.X / dpi.DpiScaleX;
                double targetLeft = left - (410 - ClockBlock.ActualWidth) / 2;

                var win = new Windows.CalendarWindow(targetLeft);
                _calendarWindow = win;
                _activeWidget = win;
                win.Closed += (s, args) => { 
                    _calendarWindow = null;
                    if (_activeWidget == win) ClearActiveWidget(); 
                };
                win.Show();
                win.Activate();
            }
            else if (type == "PinnedApps")
            {
                if (_activeWidget != null && _activeWidgetType == type) return;

                var win = new Windows.PinnedAppsWindow();
                _pinnedAppsWindow = win;
                _activeWidget = win;
                IsPinnedFlyoutOpen = true;
                win.Closed += (s, args) =>
                {
                    _pinnedAppsWindow = null;
                    IsPinnedFlyoutOpen = false;
                    if (_activeWidget == win) ClearActiveWidget();
                };
                win.Show();
                win.Activate();
            }
            else if (type == "Weather")
            {
                if (_activeWidget != null && _activeWidgetType == type) return;

                var dpi = VisualTreeHelper.GetDpi(this);
                var screenPos = WeatherButton.PointToScreen(new Point(0, 0));
                double left = screenPos.X / dpi.DpiScaleX;
                
                var win = new WeatherWindow(left - (310 - WeatherButton.ActualWidth) / 2);
                _activeWidget = win;
                win.Closed += (s, args) => { if (_activeWidget == win) ClearActiveWidget(); };
                win.Show();
                win.Activate();
            }
            else if (type == "Media")
            {
                if (_activeWidget != null && _activeWidgetType == type) return;

                var dpi = VisualTreeHelper.GetDpi(this);
                var screenPos = MediaWidget.PointToScreen(new Point(0, 0));
                double left = screenPos.X / dpi.DpiScaleX;
                double targetLeft = left - (300 - MediaWidget.ActualWidth) / 2;

                var win = new Windows.MediaFlyoutWindow(_currentMediaSession, targetLeft);
                _mediaFlyoutWindow = win;
                _activeWidget = win;
                win.Closed += (s, args) => { 
                    _mediaFlyoutWindow = null;
                    if (_activeWidget == win) ClearActiveWidget(); 
                };
                win.Show();
                win.Activate();
            }
        }
        catch
        {
            ClearActiveWidget();
        }
    }

    private void ClearActiveWidget()
    {
        _lastWidgetCloseTime = DateTime.Now;
        _lastClosedWidgetType = _activeWidgetType;

        _activeWidget = null;
        _activeWidgetType = null;
        CheckAutoHide();
    }

    public void RecordWidgetClose(string type)
    {
        _lastWidgetCloseTime = DateTime.Now;
        _lastClosedWidgetType = type;
    }

    private void CloseActiveWidget()
    {
        if (_activeWidget != null)
        {
            SoundService.Play(SoundType.Whoosh);
            
            bool animated = false;
            try
            {
                var method = _activeWidget.GetType().GetMethod("SafeClose", 
                    System.Reflection.BindingFlags.Public | 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);
                if (method != null)
                {
                    method.Invoke(_activeWidget, null);
                    animated = true;
                }
            }
            catch { /* ignored */ }

            if (!animated)
            {
                try { _activeWidget.Close(); } catch { }
            }
            ClearActiveWidget();
        }
    }

    private bool IsAnyWindowOpen()
    {
        return IsLocalGalleryOpen || 
               IsOnlineGalleryOpen || 
               IsPowerWindowOpen || 
               IsPinnedFlyoutOpen || 
               IsSettingsOpen ||
               IsContextMenuOpen ||
               (_calendarWindow != null && _calendarWindow.IsVisible) ||
               (_mediaFlyoutWindow != null && _mediaFlyoutWindow.IsVisible) ||
               (WeatherWindow.Instance != null && WeatherWindow.Instance.IsVisible);
    }

    private void AnimateDock(double targetTop, Action? onCompleted = null)
    {
        try
        {
            _animationGeneration++;
            int currentGen = _animationGeneration;

            var anim = new DoubleAnimation
            {
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(Services.SettingsService.Instance.AutoHideDurationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (s, args) => {
                if (_animationGeneration != currentGen) return;
                UpdateSoundWavesAnimationState();
                onCompleted?.Invoke();
            };
            
            // If going off-screen, stop immediately for responsiveness
            double hiddenTop = GetDockHiddenTop();
            if (targetTop == hiddenTop)
            {
                StopSoundWaves();
            }

            BeginAnimation(TopProperty, anim);
        }
        catch { /* ignored */ }
    }

    private bool IsMousePhysicallyOverDock()
    {
        try
        {
            if (!GetCursorPos(out var p)) return false;

            var s = Services.SettingsService.Instance;
            var (screen, dpi) = GetCurrentScreenInfo();
            double activeHeight;
            double yStart, yEnd;

            if (s.DockPosition == "Bottom")
            {
                bool isShowing = Top <= GetDockVisibleTop() + 5;
                activeHeight = isShowing ? (s.DockHeight * dpi.DpiScaleY) : (14 * dpi.DpiScaleY);
                yStart = screen.Bounds.Y + screen.Bounds.Height - activeHeight;
                yEnd = screen.Bounds.Y + screen.Bounds.Height;
            }
            else
            {
                bool isShowing = Top >= GetDockVisibleTop() - 5;
                activeHeight = isShowing ? (s.DockHeight * dpi.DpiScaleY) : (14 * dpi.DpiScaleY);
                yStart = screen.Bounds.Y;
                yEnd = screen.Bounds.Y + activeHeight;
            }

            bool insideX = p.X >= screen.Bounds.X && p.X <= screen.Bounds.X + screen.Bounds.Width;
            bool insideY;

            if (s.DockPosition == "Bottom")
            {
                bool isShowing = Top <= GetDockVisibleTop() + 5;
                insideY = (p.Y >= yStart && p.Y <= yEnd) || (!isShowing && p.Y >= screen.Bounds.Y + screen.Bounds.Height - 2);
            }
            else
            {
                bool isShowing = Top >= GetDockVisibleTop() - 5;
                insideY = (p.Y >= yStart && p.Y <= yEnd) || (!isShowing && p.Y <= screen.Bounds.Y + 2);
            }

            return insideX && insideY;
        }
        catch
        {
            return false;
        }
    }

    private void CheckAutoHide()
    {
        var s = Services.SettingsService.Instance;
        if (!s.AutoHide || _isManuallyKeptOpen) return;
        double hiddenTop = GetDockHiddenTop();
        if (Math.Abs(Top - hiddenTop) < 2) return;
        if (!IsMousePhysicallyOverDock() && !IsAnyWindowOpen())
        {
            AnimateDock(hiddenTop);
        }
    }

    private void MousePollTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            var s = Services.SettingsService.Instance;
            if (_isSuspendedForGameMode) return;
            if (Visibility != Visibility.Visible) return;

            bool isOver = IsMousePhysicallyOverDock();

            if (isOver)
            {
                _isManuallyKeptOpen = false;
            }

            if (s.AutoHide && !_isManuallyKeptOpen)
            {
                bool isAtVisiblePosition = Math.Abs(Top - GetDockVisibleTop()) < 5;
                if (isOver)
                {
                    if (_autoHideDelayTimer != null) _autoHideDelayTimer.Stop();
                    
                    // If it is hidden or lost topmost, force show/bring to front
                    if (!isAtVisiblePosition || !Topmost)
                    {
                        ForceTopmost();
                        AnimateDock(GetDockVisibleTop());
                    }
                }
                else
                {
                    // Fallback: If the mouse is physically not over the dock, ensure it hides.
                    // This covers cases where WPF's IsMouseOver is stuck true, where MouseLeave was missed,
                    // or where windows closed but didn't trigger a hide check.
                    bool isVisible = s.DockPosition == "Bottom" 
                        ? Top <= GetDockVisibleTop() + 5 
                        : Top >= GetDockVisibleTop() - 5;
                    if (IsMouseOver || isVisible)
                    {
                        DockWindow_MouseLeave(this, null!);
                    }
                }

                // Adjust polling interval dynamically:
                // Fast (100ms) when hidden so hover triggers instantly,
                // Normal (300ms) when visible to save CPU resources.
                double currentTop = Top;
                bool isHidden = s.DockPosition == "Bottom"
                    ? currentTop >= GetDockHiddenTop() - 5
                    : currentTop <= GetDockHiddenTop() + 5;

                TimeSpan targetInterval = isHidden ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMilliseconds(300);
                if (_mousePollTimer != null && _mousePollTimer.Interval != targetInterval)
                {
                    _mousePollTimer.Interval = targetInterval;
                }
            }
            else
            {
                if (_mousePollTimer != null && _mousePollTimer.Interval != TimeSpan.FromMilliseconds(300))
                {
                    _mousePollTimer.Interval = TimeSpan.FromMilliseconds(300);
                }
            }
        }
        catch { /* ignored */ }
    }

    private void OnAnyButtonMouseEnter(object sender, MouseEventArgs e)
    {
        SoundService.Play(SoundType.Hover);
    }

    private void RegisterButtonHoverSounds(System.Windows.DependencyObject parent)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Button btn)
            {
                btn.MouseEnter -= OnAnyButtonMouseEnter;
                btn.MouseEnter += OnAnyButtonMouseEnter;
            }
            RegisterButtonHoverSounds(child);
        }
    }

    private void OnAnyButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button)
        {
            SoundService.Play(SoundType.Click);
        }
    }

    private void DockWindow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_autoHideDelayTimer != null) _autoHideDelayTimer.Stop();
        _isManuallyKeptOpen = false;
        if (Services.SettingsService.Instance.AutoHide)
        {
            AnimateDock(GetDockVisibleTop());
        }
    }

    private void DockWindow_MouseLeave(object sender, MouseEventArgs e)
    {
        var s = Services.SettingsService.Instance;
        if (s.AutoHide && !IsAnyWindowOpen() && !_isManuallyKeptOpen)
        {
            // Do not hide if the mouse is physically over the dock
            if (IsMousePhysicallyOverDock()) return;

            double targetTop = GetDockHiddenTop();

            if (s.AutoHideDelayMs > 0)
            {
                if (_autoHideDelayTimer == null)
                {
                    _autoHideDelayTimer = new System.Windows.Threading.DispatcherTimer();
                    _autoHideDelayTimer.Tick += (snd, args) => {
                        _autoHideDelayTimer.Stop();
                        if (!IsAnyWindowOpen() && !IsMousePhysicallyOverDock())
                        {
                            AnimateDock(GetDockHiddenTop());
                        }
                    };
                }
                _autoHideDelayTimer.Interval = TimeSpan.FromMilliseconds(s.AutoHideDelayMs);
                if (!_autoHideDelayTimer.IsEnabled)
                {
                    _autoHideDelayTimer.Start();
                }
            }
            else
            {
                AnimateDock(targetTop);
            }
        }
    }

    private bool _isSuspendedForGameMode = false;

    private void CheckGameMode()
    {
        try
        {
            bool isGameActive = IsForegroundWindowFullScreen();
            if (isGameActive && !_isSuspendedForGameMode)
            {
                _isSuspendedForGameMode = true;
                
                // Stop audio animation
                StopSoundWavesAnimationOnly();

                // Unregister AppBar to immediately free the desktop work area layout so other windows can expand
                if (_isRegistered)
                {
                    UnregisterAppBar();
                }
                
                // Hide dock to stop all rendering
                Visibility = Visibility.Collapsed;

                // Suspend weather effects rendering to save CPU/GPU for gaming
                if (_weatherEffectsWindow != null)
                {
                    try { _weatherEffectsWindow.StopRendering(); } catch { }
                }
            }
            else if (!isGameActive && _isSuspendedForGameMode)
            {
                _isSuspendedForGameMode = false;
                
                // Clear any active animation on TopProperty to release the dependency property lock
                BeginAnimation(TopProperty, null);
                
                // Pre-position window offscreen if auto-hide is enabled to prevent visual jumping/displacement on show
                if (Services.SettingsService.Instance.AutoHide)
                {
                    double targetTop = (IsMousePhysicallyOverDock() || IsAnyWindowOpen()) ? GetDockVisibleTop() : GetDockHiddenTop();
                    Top = targetTop;
                }
                
                // Restore dock visibility
                if (_isDockToggledVisible)
                {
                    Visibility = Visibility.Visible;
                }

                // Re-apply styles, layout and AppBar registration
                ApplyCustomColors();
                
                // Force check if dock should hide
                CheckAutoHide();
                
                // Resume audio loopback if music is playing
                UpdateSoundWavesAnimationState();

                // Resume weather effects rendering
                if (_weatherEffectsWindow != null)
                {
                    try { _weatherEffectsWindow.StartRendering(); } catch { }
                }
            }
        }
        catch { /* ignored */ }
    }

    private bool IsForegroundWindowFullScreen()
    {
        IntPtr hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return false;

        // Skip all windows belonging to our own process
        GetWindowThreadProcessId(hWnd, out uint windowProcessId);
        uint currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        if (windowProcessId == currentProcessId)
            return false;

        // Get class name
        var className = new StringBuilder(256);
        GetClassNameW(hWnd, className, 256);
        string cls = className.ToString();
        
        // Skip Windows Shell, Taskbar and Desktop windows
        if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
            return false;

        RECT rect;
        if (GetWindowRect(hWnd, out rect))
        {
            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            // If the window covers the primary screen exactly
            if (rect.Left <= 0 && rect.Top <= 0 && rect.Right >= screenWidth && rect.Bottom >= screenHeight)
            {
                // Verify it doesn't have WS_CAPTION style (standard title bar).
                // Maximized standard windows retain the caption style. Fullscreen games/videos do NOT.
                int style = GetWindowLong(hWnd, GWL_STYLE);
                if ((style & WS_CAPTION) == WS_CAPTION)
                {
                    return false;
                }

                // Verify it has a valid title so we don't catch empty popups
                var title = new StringBuilder(256);
                GetWindowTextW(hWnd, title, 256);
                string titleStr = title.ToString().ToLower();
                
                if (titleStr == "program manager" || titleStr == "start" || string.IsNullOrEmpty(titleStr))
                    return false;

                return true;
            }
        }
        return false;
    }

    public void ToggleCalendar()
    {
        ShowWidget("Calendar");
    }

    public void ToggleMedia()
    {
        ShowWidget("Media");
    }

    public void ToggleDockVisibility()
    {
        var s = Services.SettingsService.Instance;
        if (s.AutoHide)
        {
            // Toggle sliding position if auto-hide is enabled
            bool isCurrentlyVisible = s.DockPosition == "Bottom"
                ? Top <= GetDockVisibleTop() + 5
                : Top >= GetDockVisibleTop() - 5;

            if (isCurrentlyVisible)
            {
                AnimateDock(GetDockHiddenTop(), () => _isManuallyKeptOpen = false);
            }
            else
            {
                _isManuallyKeptOpen = true;
                ForceTopmost();
                AnimateDock(GetDockVisibleTop());
            }
        }
        else
        {
            bool isCurrentlyVisible = Visibility == Visibility.Visible &&
                (s.DockPosition == "Bottom"
                    ? Top <= GetDockVisibleTop() + 5
                    : Top >= GetDockVisibleTop() - 5);

            if (isCurrentlyVisible)
            {
                _isDockToggledVisible = false;
                _isManuallyKeptOpen = false;
                _isToggleAnimating = true;
                UnregisterAppBar();
                StopSoundWaves();

                double targetTop = s.DockPosition == "Bottom"
                    ? GetDockVisibleTop() + s.DockHeight
                    : GetDockVisibleTop() - s.DockHeight;

                var anim = new DoubleAnimation
                {
                    To = targetTop,
                    Duration = TimeSpan.FromMilliseconds(s.AutoHideDurationMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                anim.Completed += (_, _) =>
                {
                    Visibility = Visibility.Collapsed;
                    _isToggleAnimating = false;
                    UpdateSoundWavesAnimationState();
                };
                BeginAnimation(TopProperty, anim);
            }
            else
            {
                _isDockToggledVisible = true;
                _isManuallyKeptOpen = true;
                _isToggleAnimating = true;

                double startTop = s.DockPosition == "Bottom"
                    ? GetDockVisibleTop() + s.DockHeight
                    : GetDockVisibleTop() - s.DockHeight;
                Top = startTop;

                Visibility = Visibility.Visible;

                var anim = new DoubleAnimation
                {
                    To = GetDockVisibleTop(),
                    Duration = TimeSpan.FromMilliseconds(s.AutoHideDurationMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                anim.Completed += (_, _) =>
                {
                    RegisterAppBar();
                    ForceTopmost();
                    _isToggleAnimating = false;
                    UpdateSoundWavesAnimationState();
                };
                BeginAnimation(TopProperty, anim);
                Activate();
            }
        }
    }

    public void RegisterGlobalHotkeys()
    {
        var h = HotkeyService.Instance;
        h.UnregisterAll();

        var s = Services.SettingsService.Instance;

        if (!string.IsNullOrEmpty(s.Hotkey_PinnedApps))
            h.Register(s.Hotkey_PinnedApps, () => Dispatcher.Invoke(() => PinnedAppsButton_Click(null!, null!)));

        if (!string.IsNullOrEmpty(s.Hotkey_Weather))
            h.Register(s.Hotkey_Weather, () => Dispatcher.Invoke(() => WeatherButton_Click(null!, null!)));

        if (!string.IsNullOrEmpty(s.Hotkey_Calendar))
            h.Register(s.Hotkey_Calendar, () => Dispatcher.Invoke(() => ToggleCalendar()));

        if (!string.IsNullOrEmpty(s.Hotkey_Media))
            h.Register(s.Hotkey_Media, () => Dispatcher.Invoke(() => ToggleMedia()));

        if (!string.IsNullOrEmpty(s.Hotkey_Power))
            h.Register(s.Hotkey_Power, () => Dispatcher.Invoke(() => PowerButton_Click(null!, null!)));

        if (!string.IsNullOrEmpty(s.Hotkey_Settings))
            h.Register(s.Hotkey_Settings, () => Dispatcher.Invoke(() => SettingsButton_Click(null!, null!)));

        if (!string.IsNullOrEmpty(s.Hotkey_LocalGallery))
            h.Register(s.Hotkey_LocalGallery, () => Dispatcher.Invoke(() => WallFlowLogoButton_Click(null!, null!)));

        if (!string.IsNullOrEmpty(s.Hotkey_OnlineGallery))
            h.Register(s.Hotkey_OnlineGallery, () => Dispatcher.Invoke(() => OnlineGalleryButton_Click(null!, null!)));

        if (!string.IsNullOrEmpty(s.Hotkey_NextWallpaper))
            h.Register(s.Hotkey_NextWallpaper, () => Dispatcher.Invoke(() => MainWindow.Instance?.NextWallpaperExternal()));

        if (!string.IsNullOrEmpty(s.Hotkey_PrevWallpaper))
            h.Register(s.Hotkey_PrevWallpaper, () => Dispatcher.Invoke(() => MainWindow.Instance?.PreviousWallpaperExternal()));

        if (!string.IsNullOrEmpty(s.Hotkey_ToggleDock))
            h.Register(s.Hotkey_ToggleDock, () => Dispatcher.Invoke(() => ToggleDockVisibility()));

        if (!string.IsNullOrEmpty(s.Hotkey_Clipboard))
            h.Register(s.Hotkey_Clipboard, () => Dispatcher.Invoke(() => ToggleClipboard()));

        if (!string.IsNullOrEmpty(s.Hotkey_YouTube))
            h.Register(s.Hotkey_YouTube, () => Dispatcher.Invoke(() => YouTubeMusicButton_Click(null!, null!)));

        if (!string.IsNullOrEmpty(s.Hotkey_WebBrowser))
            h.Register(s.Hotkey_WebBrowser, () => Dispatcher.Invoke(() => WebBrowserButton_Click(null!, null!)));
    }

    public void ToggleClipboard()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ToggleClipboard)); return; }

        if (_pinnedAppsWindow != null && _pinnedAppsWindow.IsLoaded)
        {
            if (_pinnedAppsWindow.CurrentSearchMode == "Clipboard")
            {
                _pinnedAppsWindow.Close();
                _pinnedAppsWindow = null;
                _lastClosedTime = DateTime.Now;
            }
            else
            {
                _pinnedAppsWindow.SetSearchMode("Clipboard");
                _pinnedAppsWindow.Activate();
            }
        }
        else
        {
            ShowWidget("PinnedApps");
            if (_pinnedAppsWindow != null)
            {
                _pinnedAppsWindow.SetSearchMode("Clipboard");
            }
        }
    }

    private void OnClipboardHistoryChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnClipboardHistoryChanged));
            return;
        }

        try
        {
            // 1. Get global mouse cursor position (where copy event occurred)
            if (GetCursorPos(out var mousePos))
            {
                // 2. Center of this DockWindow (or top center of the screen if hidden)
                double targetX = Left + Width / 2;
                double targetY = Math.Max(0.0, Top + Height / 2);

                // 3. Spawn spiral energy particle window
                ClipboardSpiralWindow.Spawn(mousePos.X, mousePos.Y, targetX, targetY);
            }
        }
        catch { /* ignored */ }
    }

    public void ForceReloadWeatherEffects()
    {
        if (_weatherEffectsWindow != null)
        {
            try { _weatherEffectsWindow.Close(); } catch { }
            _weatherEffectsWindow = null;
        }
        UpdateWeather();
    }
}
