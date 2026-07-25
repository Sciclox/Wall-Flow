using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WallFlow.Services;

namespace WallFlow;

public class SystemCoordinator
{
    private static SystemCoordinator? _instance;
    public static SystemCoordinator Instance => _instance ??= new SystemCoordinator();

    private IntPtr _mainHwnd;
    private DispatcherTimer? _clockTimer;
    private bool _isSuspendedForGameMode = false;
    private bool _isSuspendedForMaximized = false;



    // WinEvent hooks
    private WinEventProc? _hookDelegate;
    private IntPtr _focusHook = IntPtr.Zero;
    private IntPtr _lifecycleHook = IntPtr.Zero;
    private IntPtr _stateHook = IntPtr.Zero;

    // Win32 APIs
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int pquns);

    private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_HIDE = 0x800B;
    private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    private const uint EVENT_OBJECT_STATECHANGE = 0x800A;
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const int OBJID_WINDOW = 0;
    private const int CHILDID_SELF = 0;
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_MAXIMIZE = 0x01000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    private static int GetWindowLong(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? (int)GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    public void Initialize(IntPtr mainHwnd)
    {
        _mainHwnd = mainHwnd;

        // Hook HwndSource WndProc to receive custom messages (like Clipboard Update)
        var source = HwndSource.FromHwnd(_mainHwnd);
        if (source != null)
        {
            source.AddHook(WndProc);
            AddClipboardFormatListener(_mainHwnd);
        }

        // Initialize HotkeyService on LocalGalleryWindow's HWND
        HotkeyService.Instance.Initialize(_mainHwnd);
        RegisterGlobalHotkeys();

        // Register window hooks
        RegisterHooks();

        // Initialize background clock timer (fast fallback check every 1s for instant game detection)
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += ClockTimer_Tick;
        _clockTimer.Start();

        // Enforce taskbar hiding if enabled on start
        if (Services.SettingsService.Instance.HideWindowsTaskbar)
        {
            HideWindowsTaskbar();
        }
    }

    public void Shutdown()
    {
        _clockTimer?.Stop();
        UnregisterHooks();

        if (_mainHwnd != IntPtr.Zero)
        {
            RemoveClipboardFormatListener(_mainHwnd);
            var source = HwndSource.FromHwnd(_mainHwnd);
            source?.RemoveHook(WndProc);
        }

        try { HotkeyService.Instance.UnregisterAll(); } catch { }
        ShowWindowsTaskbar();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            ClipboardService.Instance.OnClipboardUpdated();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ===== WinEvent Hooks =====

    private void RegisterHooks()
    {
        if (_hookDelegate != null) return;
        _hookDelegate = new WinEventProc(EventCallback);
        _focusHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _hookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        _lifecycleHook = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_NAMECHANGE, IntPtr.Zero, _hookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        _stateHook = SetWinEventHook(EVENT_OBJECT_STATECHANGE, EVENT_OBJECT_STATECHANGE, IntPtr.Zero, _hookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void UnregisterHooks()
    {
        if (_hookDelegate == null) return;
        if (_focusHook != IntPtr.Zero) { UnhookWinEvent(_focusHook); _focusHook = IntPtr.Zero; }
        if (_lifecycleHook != IntPtr.Zero) { UnhookWinEvent(_lifecycleHook); _lifecycleHook = IntPtr.Zero; }
        if (_stateHook != IntPtr.Zero) { UnhookWinEvent(_stateHook); _stateHook = IntPtr.Zero; }
        _hookDelegate = null;
    }

    private DateTime _lastGameModeCheckTime = DateTime.MinValue;
    private DateTime _lastWindowRefreshTime = DateTime.MinValue;

    private void EventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject == OBJID_WINDOW && idChild == CHILDID_SELF)
        {
            // Skip events from WallFlow's own process to prevent internal particle/popup windows from triggering checks
            GetWindowThreadProcessId(hwnd, out uint windowPid);
            if (windowPid == (uint)Environment.ProcessId) return;

            var now = DateTime.UtcNow;
            if ((now - _lastGameModeCheckTime).TotalMilliseconds >= 100)
            {
                _lastGameModeCheckTime = now;
                CheckGameMode();
            }

            if (eventType == EVENT_SYSTEM_FOREGROUND || (now - _lastWindowRefreshTime).TotalMilliseconds >= 50)
            {
                _lastWindowRefreshTime = now;
                DockWindow.Instance?.Dispatcher.BeginInvoke(new Action(() => DockWindow.Instance.RefreshWindows()));
            }
        }
    }

    // ===== Clock Timer =====

    private void ClockTimer_Tick(object? sender, EventArgs e)
    {
        // Periodic check for game mode
        CheckGameMode();

        // Enforce taskbar hiding if enabled, even during suspension
        if (_isSuspendedForGameMode && Services.SettingsService.Instance.HideWindowsTaskbar)
        {
            HideWindowsTaskbar();
        }
    }

    // ===== Game Mode / Fullscreen Detections =====

    private void CheckGameMode()
    {
        try
        {
            bool isGameActive = IsForegroundWindowFullScreen();
            bool isMaximized = !isGameActive && IsForegroundWindowMaximized();

            // 1. Handle Game Mode transitions (high priority)
            if (isGameActive && !_isSuspendedForGameMode)
            {
                _isSuspendedForGameMode = true;
                _isSuspendedForMaximized = false; // Game Mode override

                // Log the event
                try
                {
                    IntPtr hwnd = GetForegroundWindow();
                    var cls = new StringBuilder(256);
                    GetClassNameW(hwnd, cls, 256);
                    var title = new StringBuilder(256);
                    GetWindowTextW(hwnd, title, 256);
                    string logMsg = $"[{DateTime.Now:HH:mm:ss}] SUSPENDED FOR GAME: HWND={hwnd}, Class={cls}, Title={title}\n";
                    string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallFlow");
                    Directory.CreateDirectory(folder);
                    File.AppendAllText(Path.Combine(folder, "gamemode_log.txt"), logMsg);
                }
                catch { }

                // Pause hotkeys so they don't trigger by accident during games
                HotkeyService.Instance.IsPaused = true;

                // Hide Local Gallery (LocalGalleryWindow) if visible
                if (LocalGalleryWindow.Instance != null && LocalGalleryWindow.Instance.Visibility == Visibility.Visible)
                {
                    LocalGalleryWindow.Instance.Hide();
                }

                // If Dock window is running, tell it to suspend
                if (DockWindow.Instance != null)
                {
                    DockWindow.Instance.Dispatcher.Invoke(() => DockWindow.Instance.SuspendForGameMode());
                }
            }
            else if (!isGameActive && _isSuspendedForGameMode)
            {
                _isSuspendedForGameMode = false;
                _isSuspendedForMaximized = isMaximized;

                // Log the event
                try
                {
                    string logMsg = $"[{DateTime.Now:HH:mm:ss}] RESUMED FROM GAME (Maximized={isMaximized})\n";
                    string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallFlow");
                    File.AppendAllText(Path.Combine(folder, "gamemode_log.txt"), logMsg);
                }
                catch { }

                // Resume hotkeys processing
                HotkeyService.Instance.IsPaused = false;

                // If Dock window is running, tell it to resume
                if (DockWindow.Instance != null)
                {
                    DockWindow.Instance.Dispatcher.Invoke(() => {
                        DockWindow.Instance.ResumeFromGameMode();
                        if (isMaximized)
                        {
                            WidgetManager.Instance.StopWallpaperEffectsRendering();
                        }
                    });
                }
            }

            // 2. Handle Maximized Window transitions (when not in Game Mode)
            if (!_isSuspendedForGameMode)
            {
                if (isMaximized && !_isSuspendedForMaximized)
                {
                    _isSuspendedForMaximized = true;

                    try
                    {
                        string logMsg = $"[{DateTime.Now:HH:mm:ss}] PAUSED EFFECTS (Window Maximized)\n";
                        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallFlow");
                        File.AppendAllText(Path.Combine(folder, "gamemode_log.txt"), logMsg);
                    }
                    catch { }

                    Application.Current.Dispatcher.Invoke(() => WidgetManager.Instance.StopWallpaperEffectsRendering());
                }
                else if (!isMaximized && _isSuspendedForMaximized)
                {
                    _isSuspendedForMaximized = false;

                    try
                    {
                        string logMsg = $"[{DateTime.Now:HH:mm:ss}] RESUMED EFFECTS (Window Restored)\n";
                        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallFlow");
                        File.AppendAllText(Path.Combine(folder, "gamemode_log.txt"), logMsg);
                    }
                    catch { }

                    Application.Current.Dispatcher.Invoke(() => WidgetManager.Instance.StartWallpaperEffectsRendering());
                }
            }
        }
        catch { /* ignored */ }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    private const uint GA_ROOTOWNER = 3;

    private bool IsForegroundWindowMaximized()
    {
        try
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return false;

            IntPtr rootHwnd = GetAncestor(hWnd, GA_ROOTOWNER);
            if (rootHwnd != IntPtr.Zero) hWnd = rootHwnd;

            // Skip our own process
            GetWindowThreadProcessId(hWnd, out uint windowProcessId);
            uint currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            if (windowProcessId == currentProcessId)
                return false;

            // Skip Desktop, Taskbar, Start Menu, Search Host, and System Overlays
            string procName = GetProcessName(hWnd);
            var className = new StringBuilder(256);
            GetClassNameW(hWnd, className, 256);
            string cls = className.ToString();

            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" ||
                cls == "XamlExplorerHostIslandWindow" || cls == "MultitaskingViewFrame" || cls == "Windows.UI.Core.CoreWindow" ||
                cls == "Windows.UI.Composition.DesktopWindowContentBridge" || cls == "TopLevelWindowForOverflowSearch" ||
                procName == "startmenuexperiencehost.exe" || procName == "searchhost.exe" || procName == "searchapp.exe")
                return false;

            // Check if maximized style is set (WS_MAXIMIZE = 0x01000000)
            // Also verify the window is NOT minimized (IsIconic)
            int style = GetWindowLong(hWnd, GWL_STYLE);
            return (style & WS_MAXIMIZE) == WS_MAXIMIZE && !IsIconic(hWnd);
        }
        catch { return false; }
    }

    public void ForceRecheck()
    {
        CheckGameMode();
    }

    private bool IsSystemRunningFullscreenApp()
    {
        try
        {
            if (SHQueryUserNotificationState(out int state) == 0) // S_OK = 0
            {
                // 2 = QUNS_BUSY (fullscreen video/app like YouTube)
                // 3 = QUNS_RUNNING_D3D_FULL_SCREEN (exclusive DirectX/Vulkan game)
                // 4 = QUNS_PRESENTATION_MODE (fullscreen PowerPoint presentation)
                // 6 = QUNS_QUIET_TIME
                // 7 = QUNS_APP (Fullscreen Store App / UWP game)
                return state == 2 || state == 3 || state == 4 || state == 6 || state == 7;
            }
        }
        catch { }
        return false;
    }

    private static string GetProcessName(IntPtr hWnd)
    {
        try
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return string.Empty;
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageNameW(hProcess, 0, sb, ref size))
                    {
                        return Path.GetFileName(sb.ToString()).ToLower();
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
        }
        catch { }
        return string.Empty;
    }

    private bool IsForegroundWindowFullScreen()
    {
        IntPtr hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return false;

        // Get root owner window in case focused window is a child control or popup (e.g. CEF/SDL popups)
        IntPtr rootHwnd = GetAncestor(hWnd, GA_ROOTOWNER);
        if (rootHwnd != IntPtr.Zero)
        {
            hWnd = rootHwnd;
        }

        // Skip our own process
        GetWindowThreadProcessId(hWnd, out uint windowProcessId);
        uint currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        if (windowProcessId == currentProcessId)
            return false;

        var className = new StringBuilder(256);
        GetClassNameW(hWnd, className, 256);
        string cls = className.ToString();

        string procName = GetProcessName(hWnd);

        var title = new StringBuilder(256);
        GetWindowTextW(hWnd, title, 256);
        string titleStr = title.ToString().ToLower();

        // Skip Desktop, Taskbar, Start Menu, Search Host, and Alt+Tab System Overlays
        bool isSystemShellWindow = cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" ||
                                   cls == "XamlExplorerHostIslandWindow" || cls == "MultitaskingViewFrame" || cls == "Windows.UI.Core.CoreWindow" ||
                                   cls == "Windows.UI.Composition.DesktopWindowContentBridge" || cls == "TopLevelWindowForOverflowSearch" ||
                                   procName == "startmenuexperiencehost.exe" || procName == "searchhost.exe" || procName == "searchapp.exe" ||
                                   procName == "lockapp.exe" ||
                                   titleStr == "program manager" || titleStr == "start" || titleStr == "inicio" ||
                                   titleStr == "buscar" || titleStr == "search" || titleStr == "conmutación de tareas" || titleStr == "task switching";

        if (isSystemShellWindow)
            return false;

        // 1. Check official Windows D3D fullscreen / presentation mode state
        if (IsSystemRunningFullscreenApp())
        {
            return true;
        }

        bool isKnownGameOrLauncher = cls == "SDL_app" || 
                                     cls == "UnrealWindow" || 
                                     cls == "UnityWndClass" || 
                                     cls == "GLFW30" ||
                                     procName == "steam.exe" || 
                                     procName == "steamwebhelper.exe" ||
                                     procName.Contains("game") ||
                                     procName.EndsWith("-win64-shipping.exe");

        // 2. Fallback to coordinate check (allowing 20px DPI/margin tolerance)
        RECT rect;
        if (GetWindowRect(hWnd, out rect))
        {
            var screen = System.Windows.Forms.Screen.FromHandle(hWnd);
            int winWidth = rect.Right - rect.Left;
            int winHeight = rect.Bottom - rect.Top;
            int screenWidth = screen.Bounds.Width;
            int screenHeight = screen.Bounds.Height;

            bool coversWidth = winWidth >= (screenWidth - 20);
            bool coversHeight = winHeight >= (screenHeight - 20);
            bool nearOrigin = Math.Abs(rect.Left - screen.Bounds.X) <= 20 && Math.Abs(rect.Top - screen.Bounds.Y) <= 20;

            if (coversWidth && coversHeight && nearOrigin)
            {
                if (isKnownGameOrLauncher)
                {
                    return true;
                }

                int style = GetWindowLong(hWnd, GWL_STYLE);
                // Standard maximized windows (WS_MAXIMIZE = 0x01000000) are NOT fullscreen games/videos
                if ((style & WS_MAXIMIZE) == WS_MAXIMIZE)
                {
                    return false;
                }

                if ((style & WS_CAPTION) == WS_CAPTION)
                {
                    return false;
                }

                return true;
            }
        }
        return false;
    }

    // ===== Taskbar Actions =====

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

    // ===== Hotkeys Management =====

    public void RegisterGlobalHotkeys()
    {
        var h = HotkeyService.Instance;
        h.UnregisterAll();

        var s = Services.SettingsService.Instance;

        if (!string.IsNullOrEmpty(s.Hotkey_PinnedApps))
            h.Register(s.Hotkey_PinnedApps, () => HandleHotkey("PinnedApps"));

        if (!string.IsNullOrEmpty(s.Hotkey_Weather))
            h.Register(s.Hotkey_Weather, () => HandleHotkey("Weather"));

        if (!string.IsNullOrEmpty(s.Hotkey_Calendar))
            h.Register(s.Hotkey_Calendar, () => HandleHotkey("Calendar"));

        if (!string.IsNullOrEmpty(s.Hotkey_Media))
            h.Register(s.Hotkey_Media, () => HandleHotkey("Media"));

        if (!string.IsNullOrEmpty(s.Hotkey_Power))
            h.Register(s.Hotkey_Power, () => HandleHotkey("Power"));

        if (!string.IsNullOrEmpty(s.Hotkey_Settings))
            h.Register(s.Hotkey_Settings, () => HandleHotkey("Settings"));

        if (!string.IsNullOrEmpty(s.Hotkey_LocalGallery))
            h.Register(s.Hotkey_LocalGallery, () => HandleHotkey("LocalGallery"));

        if (!string.IsNullOrEmpty(s.Hotkey_OnlineGallery))
            h.Register(s.Hotkey_OnlineGallery, () => HandleHotkey("OnlineGallery"));

        if (!string.IsNullOrEmpty(s.Hotkey_NextWallpaper))
            h.Register(s.Hotkey_NextWallpaper, () => HandleHotkey("NextWallpaper"));

        if (!string.IsNullOrEmpty(s.Hotkey_PrevWallpaper))
            h.Register(s.Hotkey_PrevWallpaper, () => HandleHotkey("PrevWallpaper"));

        if (!string.IsNullOrEmpty(s.Hotkey_ToggleDock))
            h.Register(s.Hotkey_ToggleDock, () => HandleHotkey("ToggleDock"));

        if (!string.IsNullOrEmpty(s.Hotkey_Clipboard))
            h.Register(s.Hotkey_Clipboard, () => HandleHotkey("Clipboard"));

        if (!string.IsNullOrEmpty(s.Hotkey_YouTube))
            h.Register(s.Hotkey_YouTube, () => HandleHotkey("YouTube"));
    }

    private void HandleHotkey(string actionType)
    {
        if (HotkeyService.Instance.IsPaused) return;

        switch (actionType)
        {
            case "LocalGallery":
                if (LocalGalleryWindow.Instance != null)
                {
                    LocalGalleryWindow.Instance.Dispatcher.Invoke(() => {
                        if (LocalGalleryWindow.Instance.Visibility == Visibility.Visible)
                            LocalGalleryWindow.Instance.Visibility = Visibility.Hidden;
                        else
                        {
                            LocalGalleryWindow.Instance.Visibility = Visibility.Visible;
                            try { LocalGalleryWindow.Instance.Activate(); } catch { }
                        }
                    });
                }
                break;
            case "OnlineGallery":
                WidgetManager.Instance.ToggleWidget("OnlineGallery");
                break;
            case "Settings":
                WidgetManager.Instance.ToggleWidget("Settings");
                break;
            case "PinnedApps":
                WidgetManager.Instance.ToggleWidget("PinnedApps");
                break;
            case "Clipboard":
                WidgetManager.Instance.ToggleWidget("Clipboard");
                break;
            case "NextWallpaper":
                LocalGalleryWindow.Instance?.Dispatcher.Invoke(() => LocalGalleryWindow.Instance.NextWallpaperExternal());
                break;
            case "PrevWallpaper":
                LocalGalleryWindow.Instance?.Dispatcher.Invoke(() => LocalGalleryWindow.Instance.PreviousWallpaperExternal());
                break;
            case "ToggleDock":
                DockWindow.Instance?.Dispatcher.Invoke(() => DockWindow.Instance.ToggleDockVisibility());
                break;
            case "Calendar":
                WidgetManager.Instance.ToggleWidget("Calendar");
                break;
            case "Media":
                WidgetManager.Instance.ToggleWidget("Media");
                break;
            case "Weather":
                WidgetManager.Instance.ToggleWidget("Weather");
                break;
            case "Power":
                WidgetManager.Instance.ToggleWidget("Power");
                break;
            case "YouTube":
                WidgetManager.Instance.ToggleWidget("YouTube");
                break;
        }
    }
}
