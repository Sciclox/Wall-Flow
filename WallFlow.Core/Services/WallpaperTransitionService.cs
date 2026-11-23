using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using WallFlow.Native;
using static WallFlow.Native.NativeMethods;

namespace WallFlow.Services;

public static class WallpaperTransitionService
{
    private static readonly object _syncLock = new object();
    private static string? _pendingPath;
    private static Action? _pendingCallback;
    private static bool _isProcessing;

    /// <summary>
    /// Gets the current wallpaper path, preferring IDesktopWallpaper COM interface and falling back to SystemParametersInfo.
    /// </summary>
    public static string GetCurrentWallpaperPath()
    {
        try
        {
            var dw = (IDesktopWallpaper)new DesktopWallpaperClass();
            try
            {
                dw.GetWallpaper(null, out string path);
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dw);
            }
        }
        catch { }

        try
        {
            var sb = new StringBuilder(500);
            SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0);
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Instantly applies the wallpaper across all monitors using the high-performance native COM IDesktopWallpaper interface (<10ms).
    /// Asynchronously executed and serialized with request coalescing to eliminate COM collisions and UI lag.
    /// </summary>
    public static void ApplyWallpaper(string newFilePath, Action? onCompleted = null)
    {
        if (string.IsNullOrEmpty(newFilePath) || !File.Exists(newFilePath))
            return;

        lock (_syncLock)
        {
            _pendingPath = newFilePath;
            _pendingCallback = onCompleted;

            if (_isProcessing)
            {
                // Already processing; the worker will pick up the latest _pendingPath next
                return;
            }

            _isProcessing = true;
        }

        Task.Run(() =>
        {
            while (true)
            {
                string currentPath;
                Action? currentCallback;

                lock (_syncLock)
                {
                    if (_pendingPath == null)
                    {
                        _isProcessing = false;
                        return;
                    }

                    currentPath = _pendingPath;
                    currentCallback = _pendingCallback;

                    _pendingPath = null;
                    _pendingCallback = null;
                }

                bool success = false;
                try
                {
                    var dw = (IDesktopWallpaper)new DesktopWallpaperClass();
                    try
                    {
                        // monitorID = null applies wallpaper to all monitors instantly (<10ms)
                        dw.SetWallpaper(null, currentPath);
                        success = true;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(dw);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WallpaperTransitionService] IDesktopWallpaper failed: {ex.Message}");
                }

                if (!success)
                {
                    // Fallback to classic Win32 broadcast
                    try
                    {
                        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, currentPath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                    }
                    catch { }
                }

                if (currentCallback != null)
                {
                    if (Application.Current != null)
                    {
                        Application.Current.Dispatcher.BeginInvoke(currentCallback);
                    }
                    else
                    {
                        currentCallback();
                    }
                }
            }
        });
    }

    /// <summary>
    /// Applies wallpaper (maintains backward compatibility with callers expecting ApplyWallpaperWithFade).
    /// Transitions are handled natively and seamlessly by Windows DWM without heavy WPF overlay windows.
    /// </summary>
    public static void ApplyWallpaperWithFade(string newFilePath, Action? onCompleted = null)
    {
        ApplyWallpaper(newFilePath, onCompleted);
    }
}
