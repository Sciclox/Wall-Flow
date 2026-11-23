using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using WallFlow.Native;
using static WallFlow.Native.NativeMethods;

namespace WallFlow.Services;

/// <summary>
/// Service encapsulating Windows AppBar API (SHAppBarMessage) and Windows Taskbar auto-hiding.
/// Desacoplado de la ventana gráfica para garantizar testabilidad y código limpio.
/// </summary>
public class WindowsAppBarService
{
    private static WindowsAppBarService? _instance;
    public static WindowsAppBarService Instance => _instance ??= new WindowsAppBarService();

    private bool _isRegistered;
    private int _appBarMessageId;
    private const int ABM_NEW = 0;
    private const int ABM_REMOVE = 1;
    private const int ABM_QUERYPOS = 2;
    private const int ABM_SETPOS = 3;
    private const int ABE_TOP = 1;
    private const int ABE_BOTTOM = 3;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    public int CallbackMessageId => _appBarMessageId;
    public bool IsRegistered => _isRegistered;

    public void Register(Window window)
    {
        if (_isRegistered) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        _appBarMessageId = RegisterWindowMessageW("WallFlowAppBarMessage");
        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            uCallbackMessage = _appBarMessageId
        };
        SHAppBarMessage(ABM_NEW, ref data);
        _isRegistered = true;
    }

    public void Unregister(Window window)
    {
        if (!_isRegistered) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd
        };
        SHAppBarMessage(ABM_REMOVE, ref data);
        _isRegistered = false;
    }

    public void SetPosition(Window window, FrameworkElement taskbarBorder)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var s = SettingsService.Instance;
        var screens = Screen.AllScreens;
        var screenIndex = Math.Clamp(s.DockMonitorIndex, 0, screens.Length - 1);
        var screen = screens[screenIndex];
        var dpi = VisualTreeHelper.GetDpi(window);

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

        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            uEdge = edge,
            rc = rc
        };

        SHAppBarMessage(ABM_QUERYPOS, ref data);
        SHAppBarMessage(ABM_SETPOS, ref data);

        window.Left = data.rc.Left / dpi.DpiScaleX;
        window.Top = data.rc.Top / dpi.DpiScaleY;
        window.Width = (data.rc.Right - data.rc.Left) / dpi.DpiScaleX;
        window.Height = (data.rc.Bottom - data.rc.Top) / dpi.DpiScaleY;

        taskbarBorder.Width = window.Width;
        taskbarBorder.Height = window.Height;

        if (s.HideWindowsTaskbar)
        {
            HideWindowsTaskbar();
        }
    }

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
        if (SettingsService.Instance.HideWindowsTaskbar)
            HideWindowsTaskbar();
        else
            ShowWindowsTaskbar();
    }
}
