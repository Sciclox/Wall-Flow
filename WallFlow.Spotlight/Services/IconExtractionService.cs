using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallFlow.Native;
using WallFlow.Services;
using static WallFlow.Native.NativeMethods;

namespace WallFlow.Spotlight.Services;

public interface IIconExtractionService
{
    ImageSource? LoadIconForApp(string appPath);
    ImageSource? GetAppIcon(string path);
    ImageSource? GetUwpIcon(string aumid);
    void PreloadPinnedIcons();
}

/// <summary>
/// Service responsible for extracting and caching executable, shortcut, and UWP application icons.
/// </summary>
public class IconExtractionService : IIconExtractionService
{
    private static IconExtractionService? _instance;
    public static IconExtractionService Instance => _instance ??= new IconExtractionService();

    private readonly ConcurrentDictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _cacheKeys = new();
    private const int MaxIconCacheSize = 250;

    private void CacheIcon(string key, ImageSource icon)
    {
        if (_iconCache.TryAdd(key, icon))
        {
            _cacheKeys.Enqueue(key);
            while (_cacheKeys.Count > MaxIconCacheSize && _cacheKeys.TryDequeue(out var oldKey))
            {
                _iconCache.TryRemove(oldKey, out _);
            }
        }
    }

    public ImageSource? LoadIconForApp(string appPath)
    {
        if (string.IsNullOrWhiteSpace(appPath)) return null;

        try
        {
            return appPath.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase)
                ? GetUwpIcon(appPath.Substring(4).Split('|')[0])
                : GetAppIcon(ResolvePath(appPath));
        }
        catch
        {
            return null;
        }
    }

    public void PreloadPinnedIcons()
    {
        var paths = SettingsService.Instance.PinnedAppPaths;
        if (paths == null || paths.Count == 0)
        {
            paths = new System.Collections.Generic.List<string>
            {
                "explorer.exe",
                "chrome.exe",
                "notepad.exe",
                "cmd.exe",
                "taskmgr.exe"
            };
        }

        Task.Run(() =>
        {
            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path) || path == "ADD_APP") continue;
                try
                {
                    LoadIconForApp(path);
                }
                catch { /* ignored */ }
            }
        });
    }

    public ImageSource? GetAppIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (_iconCache.TryGetValue(path, out var cached)) return cached;

        try
        {
            string targetPath = path;
            if (path.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                targetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            }

            if (File.Exists(targetPath))
            {
                using var icon = Icon.ExtractAssociatedIcon(targetPath);
                if (icon != null)
                {
                    var bs = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    var cloned = CloneBitmapSource(bs);
                    CacheIcon(path, cloned);
                    return cloned;
                }
            }
        }
        catch { /* ignored */ }
        return null;
    }

    public ImageSource? GetUwpIcon(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return null;
        if (_iconCache.TryGetValue(aumid, out var cached)) return cached;

        try
        {
            string path = @"shell:AppsFolder\" + aumid;
            IntPtr pidl = IntPtr.Zero;
            uint sfgaoOut = 0;
            int hr = SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out sfgaoOut);

            if (hr == 0 && pidl != IntPtr.Zero)
            {
                try
                {
                    var shfi = new SHFILEINFO();
                    uint shfiSize = (uint)Marshal.SizeOf(shfi);

                    IntPtr res = SHGetFileInfo(pidl, 0, ref shfi, shfiSize, SHGFI_PIDL | SHGFI_ICON | SHGFI_LARGEICON);
                    if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                    {
                        var bs = Imaging.CreateBitmapSourceFromHIcon(
                            shfi.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        var cloned = CloneBitmapSource(bs);
                        DestroyIcon(shfi.hIcon);
                        CacheIcon(aumid, cloned);
                        return cloned;
                    }
                }
                finally
                {
                    CoTaskMemFree(pidl);
                }
            }
        }
        catch { /* ignored */ }
        return null;
    }

    public static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        // Skip URIs and protocol schemes
        if (path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("system:", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("clipboard:", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (File.Exists(path)) return path;

        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        try
        {
            var candidate = Path.Combine(system32, path);
            if (File.Exists(candidate)) return candidate;

            candidate = Path.Combine(windows, path);
            if (File.Exists(candidate)) return candidate;

            if (path.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase))
            {
                var p1 = Path.Combine(programFiles, @"Google\Chrome\Application\chrome.exe");
                if (File.Exists(p1)) return p1;
                var p2 = Path.Combine(programFilesX86, @"Google\Chrome\Application\chrome.exe");
                if (File.Exists(p2)) return p2;
            }
        }
        catch { /* ignored */ }

        return path;
    }

    private static BitmapSource CloneBitmapSource(BitmapSource source)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        int bitsPerPixel = source.Format.BitsPerPixel;
        int stride = ((width * bitsPerPixel + 31) / 32) * 4;
        byte[] bits = new byte[height * stride];
        source.CopyPixels(bits, stride, 0);

        var clone = BitmapSource.Create(
            width, height,
            source.DpiX, source.DpiY,
            source.Format,
            source.Palette,
            bits, stride);
        clone.Freeze();
        return clone;
    }
}
