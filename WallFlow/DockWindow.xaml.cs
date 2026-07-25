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
using WallFlow.ViewModels;
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
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    private const uint GA_ROOTOWNER = 3;
    private const int DWMWA_CLOAKED = 14;
    private const int WM_COPYDATA = 0x004A;

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }




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

    // ===== IApplicationHost implementation =====

    public void RecordWidgetClose(string widgetType)
    {
        WidgetManager.Instance.RecordWidgetClose(widgetType);
    }

    public bool IsPinnedFlyoutOpen
    {
        get => _viewModel.IsPinnedFlyoutOpen;
        set => _viewModel.IsPinnedFlyoutOpen = value;
    }

    public bool IsLocalGalleryOpen
    {
        get => _viewModel.IsLocalGalleryOpen;
        set => _viewModel.IsLocalGalleryOpen = value;
    }

    public bool IsOnlineGalleryOpen
    {
        get => _viewModel.IsOnlineGalleryOpen;
        set => _viewModel.IsOnlineGalleryOpen = value;
    }

    public bool IsYouTubeMusicOpen
    {
        get => _viewModel.IsYouTubeMusicOpen;
        set => _viewModel.IsYouTubeMusicOpen = value;
    }

    public bool IsAiAssistantOpen
    {
        get => _viewModel.IsAiAssistantOpen;
        set => _viewModel.IsAiAssistantOpen = value;
    }

    public bool IsPowerWindowOpen
    {
        get => _viewModel.IsPowerWindowOpen;
        set => _viewModel.IsPowerWindowOpen = value;
    }

    public bool IsMutedActive
    {
        get => _viewModel.IsMutedActive;
        set => _viewModel.IsMutedActive = value;
    }

    public bool IsSettingsOpen
    {
        get => _viewModel.IsSettingsOpen;
        set => _viewModel.IsSettingsOpen = value;
    }

    // ===== Private Fields =====

    private int _appBarMessageId;
    private bool _isRegistered = false;
    private readonly DispatcherTimer _clockTimer;
    private System.Windows.Threading.DispatcherTimer? _autoHideDelayTimer;
    private System.Windows.Threading.DispatcherTimer? _mousePollTimer;

    private List<WindowItem> _currentItems = new();
    private IntPtr _currentActiveHwnd = IntPtr.Zero;
    private readonly Dictionary<IntPtr, ImageSource> _iconCache = new();

    private int _statusUpdateCounter = 0;
    private bool _isMutedLocally = false;
    private bool _allMinimized = false;
    private bool _isManuallyKeptOpen = false;
    private int _animationGeneration = 0;
    private bool _isToggleAnimating = false;
    private bool _isDockToggledVisible = true;

    // Context menu target
    private IntPtr _contextMenuHwnd = IntPtr.Zero;
    public bool IsContextMenuOpen { get; set; } = false;

    private DateTime _lastClosedTime = DateTime.MinValue;

    private bool _isUpdatingWeather;
    private string _cachedDateString = string.Empty;

    private void OnNetworkChanged(object? sender, EventArgs e)
    {

        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Services.WeatherService.Instance.ResetCache();
                UpdateWeather();
            }));
        }
        catch { }
    }





    // Reference to toggle media controller flyout
    private Windows.MediaFlyoutWindow? _mediaFlyoutWindow;

    // Media Playback Session variables (global:: to avoid namespace clashes)
    private global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager? _mediaSessionManager;
    private global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession? _currentMediaSession;
    internal global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession? CurrentMediaSession => _currentMediaSession;
    private System.Threading.CancellationTokenSource? _mediaUpdateCts;
    private Storyboard? _soundWavesStoryboard;
    private DispatcherTimer? _soundWaveRestartTimer;
    private bool _isMediaPlaying = false;
    private readonly DockViewModel _viewModel;

    public static DockWindow? Instance { get; private set; }

    // ===== Constructor =====

    public DockWindow(DockViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;

        InitializeComponent();
        Instance = this;
        ApplicationHost.Instance = this;

        // Clock timer (1s): updates clock, battery
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += ClockTimer_Tick;
        _clockTimer.Start();

        // Mouse polling timer (500ms) to ensure Auto-Hide is robust and doesn't get stuck hidden
        _mousePollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _mousePollTimer.Tick += MousePollTimer_Tick;
        
        UpdateMousePollTimerState();



        // Initial state
        UpdateClock();
        UpdateBattery();

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
        // BackdropHelper.UpdateWindowRegion(this, 8);

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

        ApplyDockLocation();
        RefreshWindows();

        // Initialize Media Playback
        InitMediaPlayback();

        // Load Weather
        UpdateWeather();

        // Register network change event handlers to invalidate network interface cache and refresh weather
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;

        // Register LocalGalleryWindow visibility observer to lock hover state
        var main = LocalGalleryWindow.Instance;
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

        // Register WndProc hook for AppBar messages
        try
        {
            var hWnd = new WindowInteropHelper(this).Handle;
            if (hWnd != IntPtr.Zero)
            {
                HwndSource.FromHwnd(hWnd)?.AddHook(WndProc);
            }
        }
        catch { /* ignored */ }

        // Subscribe to clipboard updates to play the absorption animation
        try
        {
            ClipboardService.Instance.HistoryChanged += OnClipboardHistoryChanged;
        }
        catch { /* ignored */ }

        // Subscribe to settings changes modularly via EventBus
        try
        {
            EventBus.Subscribe<SettingsChangedEvent>(OnSettingsChanged);
            EventBus.Subscribe<PreviewColorsRequestedEvent>(OnPreviewColorsRequested);
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
        }
        else
        {
            UnregisterAppBar();
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        try
        {
            // BackdropHelper.UpdateWindowRegion(this, 8);
        }
        catch { /* ignored */ }
    }

    protected override void OnClosed(EventArgs e)
    {
        ShowWindowsTaskbar();
        _clockTimer.Stop();
        _mousePollTimer?.Stop();
        _autoHideDelayTimer?.Stop();
        UnregisterAppBar();
        
        try { ClipboardService.Instance.HistoryChanged -= OnClipboardHistoryChanged; } catch { }
        try { EventBus.Unsubscribe<SettingsChangedEvent>(OnSettingsChanged); } catch { }
        try { EventBus.Unsubscribe<PreviewColorsRequestedEvent>(OnPreviewColorsRequested); } catch { }
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
        
        WidgetManager.Instance.CloseWallpaperEffects();
        WidgetManager.Instance.CloseAllWidgets();
 
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

        base.OnClosed(e);
    }

    // ===== Window Enumeration =====

    public void RefreshWindows()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(RefreshWindows); return; }

        var rawActive = GetForegroundWindow();
        if (rawActive != IntPtr.Zero)
        {
            IntPtr root = GetAncestor(rawActive, GA_ROOTOWNER);
            IntPtr activeHwnd = (root != IntPtr.Zero) ? root : rawActive;

            GetWindowThreadProcessId(activeHwnd, out uint activePid);

            // Exclude our own WallFlow process windows from replacing the active user app window
            if (activePid != (uint)Environment.ProcessId)
            {
                _currentActiveHwnd = activeHwnd;
            }
        }

        var tempItems = new List<WindowItem>();

        EnumWindows((hwnd, lParam) =>
        {
            if (IsTaskbarWindow(hwnd))
            {
                IntPtr rootOwner = GetAncestor(rawActive, GA_ROOTOWNER);
                bool isActive = (hwnd == _currentActiveHwnd || hwnd == rawActive || (rootOwner != IntPtr.Zero && hwnd == rootOwner));

                tempItems.Add(new WindowItem
                {
                    Hwnd = hwnd,
                    Title = GetWindowTextHelper(hwnd),
                    Icon = GetWindowIcon(hwnd),
                    IsActive = isActive,
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
        if (pid == Environment.ProcessId)
        {
            return false;
        }
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

    // WinEvent hooks removed (moved to SystemCoordinator)

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

    public void ApplyTaskbarVisibility()
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
        const int WM_SETTINGCHANGE = 0x001A;
        if (msg == WM_SETTINGCHANGE)
        {
            WallpaperThemeService.Instance.UpdateThemeFromWallpaper();
        }

        if (msg == _appBarMessageId && wParam.ToInt32() == 1) { SetAppBarPosition(); handled = true; }
        if (msg == WM_COPYDATA)
        {
            try
            {
                var cds = (COPYDATASTRUCT)Marshal.PtrToStructure(lParam, typeof(COPYDATASTRUCT))!;
                string? url = Marshal.PtrToStringUni(cds.lpData);
                if (!string.IsNullOrEmpty(url))
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

        // If we are suspended for game mode, do not waste CPU updating UI clocks/battery/recycle bin
        if (_isSuspendedForGameMode)
        {
            return;
        }

        UpdateClock();
        if (_statusUpdateCounter % 10 == 0) UpdateBattery();      // Every 10s
        if (_statusUpdateCounter % 900 == 0) UpdateWeather();     // Every 15m
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
        ChatGPTButton.Visibility = s.ShowIconChatGPT ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = s.ShowIconSettings ? Visibility.Visible : Visibility.Collapsed;

        VolumeButton.Visibility = s.ShowIconVolume ? Visibility.Visible : Visibility.Collapsed;
        NotificationsButton.Visibility = s.ShowIconNotifications ? Visibility.Visible : Visibility.Collapsed;
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
                              s.ShowIconYouTubeMusic || s.ShowIconChatGPT || s.ShowIconSettings;
                              
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

        // Right separator 1 (between notifications and show desktop)
        bool group1Visible = s.ShowIconVolume || s.ShowIconNotifications;
        bool group2Visible = s.ShowIconShowDesktop;
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
            WidgetManager.Instance.UpdateWallpaperEffects();
        }
        catch { /* ignored */ }
        finally { _isUpdatingWeather = false; }
    }

    private void WeatherButton_Click(object sender, RoutedEventArgs e)
    {
        WidgetManager.Instance.ToggleWidget("Weather");
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
        string newTime = s.Use24HourFormat ? now.ToString("HH:mm:ss") : now.ToString("hh:mm:ss tt");
        if (TimeText.Text != newTime)
        {
            TimeText.Text = newTime;
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
            WidgetManager.Instance.ToggleWidget("Media");
        }
    }

    // ===== Interactions =====

    private void ClockBlock_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            WidgetManager.Instance.ToggleWidget("Calendar");
        }
    }
 
    private void PinnedAppsButton_Click(object sender, RoutedEventArgs e)
    {
        WidgetManager.Instance.ToggleWidget("PinnedApps");
    }

    /// <summary>
    /// Binds the matrix ripple animation to MouseEnter hover to avoid
    /// continuous background CPU usage in idle.
    /// </summary>
    private void PinnedAppsButton_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var button = sender as Button;
            if (button?.Template == null) return;

            // Resolve named borders inside the applied template
            var cells = new System.Windows.Controls.Border?[9];
            for (int i = 0; i < 9; i++)
                cells[i] = button.Template.FindName($"Cell{i + 1}", button) as System.Windows.Controls.Border;

            if (cells[0] == null) return; // Template not ready

            // Diagonal ripple delay mapping (Manhattan distance from top-left)
            // Cell1=0, Cell2/4=0.15, Cell3/5/7=0.3, Cell6/8=0.45, Cell9=0.6
            double[] delays = { 0, 0.15, 0.3, 0.15, 0.3, 0.45, 0.3, 0.45, 0.6 };

            var storyboard = new Storyboard();
            var scalePath = new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
            var scaleYPath = new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
            var opacityPath = new PropertyPath("Opacity");
            var dur = new Duration(TimeSpan.FromSeconds(0.35));

            for (int i = 0; i < 9; i++)
            {
                if (cells[i] == null) continue;
                var begin = TimeSpan.FromSeconds(delays[i]);

                var sx = new DoubleAnimation(1, 1.35, dur) { AutoReverse = true, BeginTime = begin };
                Storyboard.SetTarget(sx, cells[i]);
                Storyboard.SetTargetProperty(sx, scalePath);
                storyboard.Children.Add(sx);

                var sy = new DoubleAnimation(1, 1.35, dur) { AutoReverse = true, BeginTime = begin };
                Storyboard.SetTarget(sy, cells[i]);
                Storyboard.SetTargetProperty(sy, scaleYPath);
                storyboard.Children.Add(sy);

                var op = new DoubleAnimation(0.6, 1.0, dur) { AutoReverse = true, BeginTime = begin };
                Storyboard.SetTarget(op, cells[i]);
                Storyboard.SetTargetProperty(op, opacityPath);
                storyboard.Children.Add(op);
            }

            // Trigger ripple animation on hover instead of continuous infinite loop
            button.MouseEnter += (s, args) =>
            {
                try { storyboard.Begin(button, true); } catch { }
            };
        }
        catch { /* Animation is non-critical; don't crash the app */ }
    }
 
    private void WallFlowLogoButton_Click(object sender, RoutedEventArgs e)
    {
        WidgetManager.Instance.ToggleWidget("LocalGallery");
    }
 
    private void OnlineGalleryButton_Click(object sender, RoutedEventArgs e)
    {
        WidgetManager.Instance.ToggleWidget("OnlineGallery");
    }
 
    private void YouTubeMusicButton_Click(object sender, RoutedEventArgs e)
    {
        WidgetManager.Instance.ToggleWidget("YouTube");
    }

    private void ChatGPTButton_Click(object sender, RoutedEventArgs e)
    {
        WidgetManager.Instance.ToggleWidget("ChatGPT");
    }
 
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        WidgetManager.Instance.ToggleWidget("Settings");
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
                    SystemCoordinator.Instance.ForceRecheck();
                }
                else
                {
                    shellType.InvokeMember("UndoMinimizeALL", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
                    _allMinimized = false;
                    SystemCoordinator.Instance.ForceRecheck();
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
        WidgetManager.Instance.ToggleWidget("Power");
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
                wallpaperPath = LocalGalleryWindow.GetCurrentWallpaper();
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

    public void ForceTopmost()
    {
        try
        {
            if (Visibility != Visibility.Visible) return;
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
        // PinnedAppsWindow state is now managed via WidgetManager
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

                double targetTop = (IsMousePhysicallyOverDock() || WidgetManager.Instance.IsAnyWidgetOpen()) ? GetDockVisibleTop() : GetDockHiddenTop();
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

                // Update visualizer wave bars via DynamicResource
                var waveBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1)
                };
                waveBrush.GradientStops.Add(new GradientStop(hover, 0));
                waveBrush.GradientStops.Add(new GradientStop(accent, 1));
                Application.Current.Resources["VisualizerWaveBrush"] = waveBrush;
            }

            // Determine and apply custom border colors via DynamicResource
            if (!string.IsNullOrEmpty(s.UserBorderColor1) &&
                !string.IsNullOrEmpty(s.UserBorderColor2) &&
                !string.IsNullOrEmpty(s.UserBorderColor3) &&
                TryParseColor(s.UserBorderColor1, out var c1) &&
                TryParseColor(s.UserBorderColor2, out var c2) &&
                TryParseColor(s.UserBorderColor3, out var c3))
            {
                Application.Current.Resources["BorderColor1"] = c1;
                Application.Current.Resources["BorderColor2"] = c2;
                Application.Current.Resources["BorderColor3"] = c3;

                var lgb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                lgb.GradientStops.Add(new GradientStop(c1, 0));
                lgb.GradientStops.Add(new GradientStop(c2, 0.5));
                lgb.GradientStops.Add(new GradientStop(c3, 1));
                try { Application.Current.Resources["DockBorderBrush"] = lgb; } catch { }
                if (TaskbarBorder != null) TaskbarBorder.BorderBrush = lgb;
            }
            else
            {
                var defaultBrush = Application.Current.Resources["AccentBrush"] as Brush 
                    ?? new SolidColorBrush(Color.FromRgb(0xAF, 0x52, 0xDE));
                try { Application.Current.Resources["DockBorderBrush"] = defaultBrush; } catch { }
                if (TaskbarBorder != null) TaskbarBorder.BorderBrush = defaultBrush;
            }

            // Apply dock icon theme and color styling
            ApplyDockIconThemeAndColor();
        }
        catch { /* ignored */ }
    }

    public void ApplyDockIconThemeAndColor()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ApplyDockIconThemeAndColor)); return; }
        try
        {
            Brush restBadgeBg = Brushes.Transparent;
            Brush hoverBadgeBg = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF));

            Button[] buttons = new[]
            {
                PinnedAppsButton, LocalGalleryButton, OnlineGalleryButton, YouTubeMusicButton,
                SettingsButton, VolumeButton, NotificationsButton, ShowDesktopButton, PowerButton
            };

            foreach (var btn in buttons)
            {
                if (btn == null) continue;
                btn.ApplyTemplate();
                btn.Tag = Tuple.Create(restBadgeBg, hoverBadgeBg);

                btn.MouseEnter -= Button_Glow_MouseEnter;
                btn.MouseEnter += Button_Glow_MouseEnter;
                btn.MouseLeave -= Button_Glow_MouseLeave;
                btn.MouseLeave += Button_Glow_MouseLeave;

                var children = FindVisualChildren<DependencyObject>(btn);
                foreach (var child in children)
                {
                    string elemName = child.GetType().GetProperty("Name")?.GetValue(child) as string ?? "";

                    if (elemName == "IconStyle_Minimal" && child is UIElement minimalIcon)
                    {
                        minimalIcon.Visibility = Visibility.Visible;
                        if (child is TextBlock tb)
                        {
                            tb.Foreground = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF));
                        }
                    }
                    else if (elemName.StartsWith("IconStyle_") && child is UIElement nonMinimalPack)
                    {
                        nonMinimalPack.Visibility = Visibility.Collapsed;
                    }

                    if (child is Border b && (b.Name.EndsWith("TileBadge") || b.Name == "TileBadge"))
                    {
                        b.Background = restBadgeBg;
                        b.BorderBrush = Brushes.Transparent;
                        b.BorderThickness = new Thickness(0);
                        b.CornerRadius = new CornerRadius(16);
                    }

                    if ((child is System.Windows.Shapes.Ellipse || child is Border) && elemName.EndsWith("Glow"))
                    {
                        if (child is UIElement glowElement)
                        {
                            glowElement.Opacity = 0.0;
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void Button_Glow_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Tuple<Brush, Brush> t)
        {
            var badge = FindVisualChildren<Border>(btn).FirstOrDefault(x => x.Name.EndsWith("TileBadge") || x.Name == "TileBadge");
            if (badge != null)
            {
                badge.Background = t.Item2;
            }
            var tb = FindVisualChildren<TextBlock>(btn).FirstOrDefault(x => x.Name == "IconStyle_Minimal");
            if (tb != null)
            {
                tb.Foreground = Brushes.White;
            }
        }
    }

    private void Button_Glow_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Tuple<Brush, Brush> t)
        {
            var badge = FindVisualChildren<Border>(btn).FirstOrDefault(x => x.Name.EndsWith("TileBadge") || x.Name == "TileBadge");
            if (badge != null)
            {
                badge.Background = t.Item1;
            }
            var tb = FindVisualChildren<TextBlock>(btn).FirstOrDefault(x => x.Name == "IconStyle_Minimal");
            if (tb != null)
            {
                tb.Foreground = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF));
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj != null)
        {
            int count = VisualTreeHelper.GetChildrenCount(depObj);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t)
                {
                    yield return t;
                }
                foreach (T childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }
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

    internal void CheckAutoHide()
    {
        var s = Services.SettingsService.Instance;
        if (!s.AutoHide) return;

        // Clear manual override when no widgets/settings windows are open
        if (!WidgetManager.Instance.IsAnyWidgetOpen())
        {
            _isManuallyKeptOpen = false;
        }
        else if (_isManuallyKeptOpen)
        {
            return;
        }

        double hiddenTop = GetDockHiddenTop();
        if (Math.Abs(Top - hiddenTop) < 2) return;
        if (!IsMousePhysicallyOverDock() && !WidgetManager.Instance.IsAnyWidgetOpen())
        {
            AnimateDock(hiddenTop);
        }
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

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    private static bool IsWindowCloaked(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return true;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0)
        {
            return cloaked != 0;
        }
        return false;
    }

    private bool IsStartMenuOpenOrFocused()
    {
        try
        {
            // 1. Check Foreground Window
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                if (IsStartMenuHwnd(fg)) return true;

                IntPtr root = GetAncestor(fg, GA_ROOTOWNER);
                if (root != IntPtr.Zero && IsStartMenuHwnd(root)) return true;
            }

            // 2. Fallback: Check known visible Windows Start Menu / Search windows directly ONLY IF NOT CLOAKED BY DWM
            IntPtr startWin = FindWindowW("Windows.UI.Core.CoreWindow", "Start");
            if (startWin == IntPtr.Zero) startWin = FindWindowW("Windows.UI.Core.CoreWindow", "Inicio");
            if (startWin != IntPtr.Zero && IsWindowVisible(startWin) && !IsIconic(startWin) && !IsWindowCloaked(startWin)) return true;

            IntPtr xamlWin = FindWindowW("XamlExplorerHostIslandWindow", null);
            if (xamlWin != IntPtr.Zero && IsWindowVisible(xamlWin) && !IsIconic(xamlWin) && !IsWindowCloaked(xamlWin)) return true;

            return false;
        }
        catch { return false; }
    }

    private bool IsStartMenuHwnd(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        var clsSb = new StringBuilder(256);
        GetClassNameW(hwnd, clsSb, 256);
        string cls = clsSb.ToString();

        var titleSb = new StringBuilder(256);
        GetWindowTextW(hwnd, titleSb, 256);
        string title = titleSb.ToString().ToLower();

        GetWindowThreadProcessId(hwnd, out uint pid);
        string procName = GetProcessPath((int)pid);
        if (!string.IsNullOrEmpty(procName))
        {
            procName = Path.GetFileName(procName).ToLower();
        }

        if (procName == "startmenuexperiencehost.exe" || 
            procName == "searchhost.exe" || 
            procName == "searchapp.exe" || 
            procName == "shellexperiencehost.exe")
            return true;

        if (cls == "Windows.UI.Core.CoreWindow" || 
            cls == "XamlExplorerHostIslandWindow" || 
            cls == "Windows.UI.Composition.DesktopWindowContentBridge" || 
            cls == "TopLevelWindowForOverflowSearch" || 
            cls == "LauncherSystemWindow")
        {
            if (title == "start" || title == "inicio" || title == "búsqueda" || title == "search" || string.IsNullOrEmpty(title))
                return true;
        }

        return false;
    }

    private void MousePollTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            var s = Services.SettingsService.Instance;
            if (_isSuspendedForGameMode) return;
            if (Visibility != Visibility.Visible) return;

            if (s.HideWindowsTaskbar)
            {
                HideWindowsTaskbar();
            }

            bool isStartOpen = IsStartMenuOpenOrFocused();
            bool isOver = IsMousePhysicallyOverDock() || isStartOpen;

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
                    
                    // If it is hidden, animate open (do NOT call ForceTopmost while Start menu is open to prevent Z-order fights)
                    if (!isAtVisiblePosition)
                    {
                        if (!isStartOpen) ForceTopmost();
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
                // Fast (250ms) when hidden so hover triggers smoothly,
                // Normal (500ms) when visible to minimize background CPU usage.
                double currentTop = Top;
                bool isHidden = s.DockPosition == "Bottom"
                    ? currentTop >= GetDockHiddenTop() - 5
                    : currentTop <= GetDockHiddenTop() + 5;

                TimeSpan targetInterval = isHidden ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromMilliseconds(500);
                if (_mousePollTimer != null && _mousePollTimer.Interval != targetInterval)
                {
                    _mousePollTimer.Interval = targetInterval;
                }
            }
            else
            {
                if (_mousePollTimer != null && _mousePollTimer.Interval != TimeSpan.FromMilliseconds(500))
                {
                    _mousePollTimer.Interval = TimeSpan.FromMilliseconds(500);
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
        if (s.AutoHide && !WidgetManager.Instance.IsAnyWidgetOpen() && !_isManuallyKeptOpen)
        {
            // Do not hide if Start Menu is open or mouse is physically over dock
            if (IsStartMenuOpenOrFocused()) return;
            if (IsMousePhysicallyOverDock()) return;

            double targetTop = GetDockHiddenTop();

            if (s.AutoHideDelayMs > 0)
            {
                if (_autoHideDelayTimer == null)
                {
                    _autoHideDelayTimer = new System.Windows.Threading.DispatcherTimer();
                    _autoHideDelayTimer.Tick += (snd, args) => {
                        _autoHideDelayTimer.Stop();
                        if (!WidgetManager.Instance.IsAnyWidgetOpen() && !IsMousePhysicallyOverDock())
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

    public void SuspendForGameMode()
    {
        _isSuspendedForGameMode = true;
        _clockTimer?.Stop();
        _mousePollTimer?.Stop();
        
        // Stop audio animation
        StopSoundWavesAnimationOnly();

        // Unregister AppBar to immediately free the desktop work area layout so other windows can expand
        if (_isRegistered)
        {
            UnregisterAppBar();
        }
        
        // Hide dock to stop all rendering
        Visibility = Visibility.Collapsed;
        // Suspend wallpaper effects rendering to save CPU/GPU for gaming
        WidgetManager.Instance.StopWallpaperEffectsRendering();
 
        // Close all auxiliary windows to save memory and avoid focus stealing
        WidgetManager.Instance.CloseAllWidgets();
    }

    public void ResumeFromGameMode()
    {
        _isSuspendedForGameMode = false;
        _clockTimer?.Start();
        UpdateMousePollTimerState();
        
        // Clear any active animation on TopProperty to release the dependency property lock
        BeginAnimation(TopProperty, null);
        
        // Pre-position window offscreen if auto-hide is enabled to prevent visual jumping/displacement on show
        if (Services.SettingsService.Instance.AutoHide)
        {
            double targetTop = (IsMousePhysicallyOverDock() || WidgetManager.Instance.IsAnyWidgetOpen()) ? GetDockVisibleTop() : GetDockHiddenTop();
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

        // Resume wallpaper effects rendering
        WidgetManager.Instance.StartWallpaperEffectsRendering();
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

    public void ToggleClipboard()
    {
        WidgetManager.Instance.ToggleWidget("Clipboard");
    }

    private void OnClipboardHistoryChanged()
    {
        // Option B: Clipboard history is captured silently in background.
        // Particle overlay window spawning is disabled to prevent dock hiding/overlays.
    }

    public void ForceReloadWallpaperEffects()
    {
        WidgetManager.Instance.CloseWallpaperEffects();
        UpdateWeather();
    }
 
    private void OnSettingsChanged(SettingsChangedEvent @event)
    {
        OnSettingsChanged();
    }
 
    private void OnSettingsChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnSettingsChanged)); return; }
        ApplyWidgetVisibilities();
        ApplyDockLocation();
        ApplyTaskbarVisibility();
        UpdateClock();
        UpdateWeather();
        ForceReloadWallpaperEffects();
        ApplyCustomColors();
        ApplySpotlightSettings();
        UpdateMousePollTimerState();
    }

    public void ApplyDockLocation()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ApplyDockLocation)); return; }

        var s = Services.SettingsService.Instance;
        var (screen, dpi) = GetCurrentScreenInfo();

        if (s.AutoHide)
        {
            if (_isRegistered)
            {
                UnregisterAppBar();
            }
            ApplyTaskbarVisibility();
            Width = screen.Bounds.Width / dpi.DpiScaleX;
            Height = s.DockHeight;
            Left = screen.Bounds.X / dpi.DpiScaleX;
            
            // Clear any active animation on TopProperty to release the dependency property lock
            BeginAnimation(TopProperty, null);

            bool isAnyWidgetOpen = WidgetManager.Instance.IsAnyWidgetOpen();
            if (!isAnyWidgetOpen)
            {
                _isManuallyKeptOpen = false;
            }

            Top = (IsMousePhysicallyOverDock() || isAnyWidgetOpen) ? GetDockVisibleTop() : GetDockHiddenTop();

            if (TaskbarBorder != null)
            {
                TaskbarBorder.Width = Width;
                TaskbarBorder.Height = s.DockHeight;
            }

            Dispatcher.BeginInvoke(new Action(() => CheckAutoHide()), System.Windows.Threading.DispatcherPriority.Background);
        }
        else
        {
            Width = screen.Bounds.Width / dpi.DpiScaleX;
            Height = s.DockHeight;
            Left = screen.Bounds.X / dpi.DpiScaleX;
            
            // Clear any active animation on TopProperty to release the dependency property lock
            BeginAnimation(TopProperty, null);
            Top = GetDockVisibleTop();

            if (TaskbarBorder != null)
            {
                TaskbarBorder.Width = Width;
                TaskbarBorder.Height = s.DockHeight;
            }

            if (!_isRegistered)
            {
                RegisterAppBar();
            }
            else
            {
                SetAppBarPosition();
            }
        }
    }
 
    private void OnPreviewColorsRequested(PreviewColorsRequestedEvent @event)
    {
        OnPreviewColorsRequested();
    }
 
    private void OnPreviewColorsRequested()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnPreviewColorsRequested)); return; }
        ApplyCustomColors();
    }

    private void UpdateMousePollTimerState()
    {
        var s = Services.SettingsService.Instance;
        if (s.AutoHide && !_isSuspendedForGameMode)
        {
            if (_mousePollTimer != null && !_mousePollTimer.IsEnabled)
            {
                _mousePollTimer.Start();
            }
        }
        else
        {
            _mousePollTimer?.Stop();
        }
    }
}
