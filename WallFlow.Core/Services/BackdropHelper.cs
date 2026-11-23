using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WallFlow.Native;
using static WallFlow.Native.NativeMethods;

namespace WallFlow.Services;

public static class BackdropHelper
{
    public static bool IsWindows11OrGreater()
    {
        return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;
    }

    public static void ApplyAcrylic(Window window, bool roundCorners = true, bool hideFromAltTab = true)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Hide or show in Alt-Tab
        try
        {
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (hideFromAltTab)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW);
            }
            else
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle & ~WS_EX_TOOLWINDOW) | WS_EX_APPWINDOW);
            }
        }
        catch { }

        // Enable native OS rounded corners to match MainWindow behavior
        if (IsWindows11OrGreater())
        {
            int cornerPreference = roundCorners ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
        }

        // Apply composition blur (works on Win10 and Win11, respects transparency & rounded corners)
        var accent = new AccentPolicy
        {
            AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND
        };
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

        RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
            RDW_INVALIDATE | RDW_ERASE | RDW_UPDATENOW | RDW_ALLCHILDREN);
    }

    public static void ApplyMica(Window window, bool hideFromAltTab = true)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Hide or show in Alt-Tab
        try
        {
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (hideFromAltTab)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW);
            }
            else
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle & ~WS_EX_TOOLWINDOW) | WS_EX_APPWINDOW);
            }
        }
        catch { }

        // Enable native OS rounded corners to match MainWindow behavior
        if (IsWindows11OrGreater())
        {
            int cornerPreference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

            // Apply Mica effect: DWMWA_SYSTEMBACKDROP_TYPE = 38
            // 0: Auto, 1: None, 2: Mica, 3: Acrylic, 4: MicaAlt
            int backdropType = 2; // Mica
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
            if (hr != 0)
            {
                // Fallback for older Windows 11 builds (build 22000)
                int trueVal = 1;
                DwmSetWindowAttribute(hwnd, 1029, ref trueVal, sizeof(int));
            }
        }

        RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
            RDW_INVALIDATE | RDW_ERASE | RDW_UPDATENOW | RDW_ALLCHILDREN);
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

    /// <summary>
    /// Forces the OS to re-compose the blur/acrylic effect so it picks up the
    /// current desktop wallpaper. Mirrors MainWindow.RefreshBackdrop behavior.
    /// </summary>
    public static void RefreshBackdrop(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        ApplyAccent(hwnd, AccentState.ACCENT_ENABLE_BLURBEHIND);
        RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
            RDW_INVALIDATE | RDW_UPDATENOW);
    }

    public static void UpdateWindowRegion(Window window, int radius)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        int pixelW = (int)Math.Round(window.ActualWidth * dpi.DpiScaleX);
        int pixelH = (int)Math.Round(window.ActualHeight * dpi.DpiScaleY);
        int radiusPx = (int)Math.Round(radius * dpi.DpiScaleX);

        // Win32 CreateRoundRectRgn expects the width and height of the rounding ellipse (diameter, which is 2 * radius)
        var rgn = CreateRoundRectRgn(0, 0, pixelW, pixelH, radiusPx * 2, radiusPx * 2);
        if (rgn != IntPtr.Zero)
        {
            SetWindowRgn(hwnd, rgn, true);
            DeleteObject(rgn);
        }
    }

    public static void SetWindowRounding(Window window, bool round)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (IsWindows11OrGreater())
        {
            int cornerPreference = round ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
        }
    }

    public static void ForceAltTabVisibility(Window window, bool show)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (show)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle & ~WS_EX_TOOLWINDOW) | WS_EX_APPWINDOW);
            }
            else
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW);
            }
        }
        catch { }
    }
}
