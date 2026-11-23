using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallFlow.Native;
using static WallFlow.Native.NativeMethods;

namespace WallFlow.Services;

/// <summary>
/// Model representing an active top-level desktop window with chromatic glow telemetry.
/// </summary>
public class ActiveWindowModel : INotifyPropertyChanged
{
    public IntPtr Hwnd { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string ProcessPath { get; set; } = string.Empty;
    public ImageSource? Icon { get; set; }
    public bool IsActive { get; set; }
    public bool IsMinimized { get; set; }

    private ImageSource? _thumbnail;
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (_thumbnail != value)
            {
                _thumbnail = value;
                OnPropertyChanged(nameof(Thumbnail));
                OnPropertyChanged(nameof(HasThumbnail));
                OnPropertyChanged(nameof(HasNoThumbnail));
            }
        }
    }
    public bool HasThumbnail => _thumbnail != null;
    public bool HasNoThumbnail => _thumbnail == null;

    public Color GlowColor { get; set; }
    public Brush GlowBrush { get; set; } = Brushes.Transparent;
    public Brush BorderGlowBrush { get; set; } = Brushes.Transparent;
    public Brush TileBackgroundBrush { get; set; } = Brushes.Transparent;
    public string StatusBadge { get; set; } = "En ejecución";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}

/// <summary>
/// Precursor service for active desktop window telemetry, chromatic ambient glow analysis,
/// and instant spatial window switching.
/// </summary>
public class ActiveWindowManager
{
    private static readonly Lazy<ActiveWindowManager> _instance = new(() => new ActiveWindowManager());
    public static ActiveWindowManager Instance => _instance.Value;

    private readonly Dictionary<IntPtr, ImageSource> _iconCache = new();
    private readonly Dictionary<IntPtr, ImageSource> _thumbnailCache = new();

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int GW_OWNER = 4;
    private const int WM_GETICON = 0x007F;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const int ICON_SMALL2 = 2;
    private const int GCLP_HICONSM = -34;
    private const int GCLP_HICON = -14;
    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;
    private const int SW_MINIMIZE = 6;
    private const int WM_CLOSE = 0x0010;
    private const int DWMWA_CLOAKED = 14;
    private const uint GA_ROOTOWNER = 3;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private ActiveWindowManager() { }

    /// <summary>
    /// Enumerates visible top-level windows currently open on the user's desktop,
    /// assigning dynamic chromatic glow lighting based on process signature.
    /// </summary>
    public List<ActiveWindowModel> GetActiveWindows(IntPtr ignoreHwnd = default)
    {
        var result = new List<ActiveWindowModel>();
        var rawActive = GetForegroundWindow();

        EnumWindows((hwnd, lParam) =>
        {
            if (hwnd == ignoreHwnd) return true;
            if (!IsTaskbarWindow(hwnd, ignoreHwnd)) return true;

            string title = GetWindowTextHelper(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return true;

            GetWindowThreadProcessId(hwnd, out uint pid);
            string procPath = GetProcessPath((int)pid);
            string procName = string.Empty;
            if (!string.IsNullOrEmpty(procPath))
            {
                procName = Path.GetFileNameWithoutExtension(procPath).ToLowerInvariant();
            }

            // Exclude Windows background search/shell hosts that do not have tangible windows
            if (procName is "startmenuexperiencehost" or "searchhost" or "searchapp" or "shellexperiencehost" or "lockapp")
            {
                return true;
            }

            IntPtr rootOwner = GetAncestor(rawActive, GA_ROOTOWNER);
            bool isActive = (hwnd == rawActive || (rootOwner != IntPtr.Zero && hwnd == rootOwner));
            bool isMinimized = IsIconic(hwnd);

            var glowColor = ComputeChromaticColor(procName, title);
            var glowBrush = new SolidColorBrush(Color.FromArgb(140, glowColor.R, glowColor.G, glowColor.B));
            glowBrush.Freeze();

            var borderGlowBrush = new SolidColorBrush(Color.FromArgb(200, glowColor.R, glowColor.G, glowColor.B));
            borderGlowBrush.Freeze();

            var bgBrush = new SolidColorBrush(Color.FromArgb(38, Math.Max((byte)15, glowColor.R), Math.Max((byte)18, glowColor.G), Math.Max((byte)28, glowColor.B)));
            bgBrush.Freeze();

            string status = isActive ? "🟢 En foco" : (isMinimized ? "⏸ Minimizada" : "⚡ Activa");

            ImageSource? cachedThumbnail = null;
            _thumbnailCache.TryGetValue(hwnd, out cachedThumbnail);

            result.Add(new ActiveWindowModel
            {
                Hwnd = hwnd,
                Title = title,
                ProcessName = string.IsNullOrEmpty(procName) ? "App" : procName,
                ProcessPath = procPath,
                Icon = GetWindowIcon(hwnd, procPath),
                Thumbnail = cachedThumbnail,
                IsActive = isActive,
                IsMinimized = isMinimized,
                GlowColor = glowColor,
                GlowBrush = glowBrush,
                BorderGlowBrush = borderGlowBrush,
                TileBackgroundBrush = bgBrush,
                StatusBadge = status
            });

            return true;
        }, IntPtr.Zero);

        // Sort: Active first, then non-minimized, then minimized
        return result;
    }

    /// <summary>
    /// Switches keyboard and window focus directly to the target window.
    /// </summary>
    public void SwitchToWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
            }
            else
            {
                ShowWindow(hwnd, SW_SHOW);
            }
            SetForegroundWindow(hwnd);
        }
        catch { }
    }

    /// <summary>
    /// Minimizes the target window to the taskbar.
    /// </summary>
    public void MinimizeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            ShowWindow(hwnd, SW_MINIMIZE);
        }
        catch { }
    }

    /// <summary>
    /// Sends a graceful close signal to the target window.
    /// </summary>
    public void CloseWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            _thumbnailCache.Remove(hwnd);
            PostMessageW(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    /// <summary>
    /// Captures a crisp, hardware-accelerated visual thumbnail preview of the target window.
    /// Employs a multi-strategy capture pipeline (PW_RENDERFULLCONTENT -> PW_CLIENTONLY -> Screen DC BitBlt)
    /// and converts through GDI+ to eliminate the 32-bit alpha transparency corruption bug.
    /// </summary>
    public ImageSource? CaptureWindowThumbnail(IntPtr hwnd, int targetWidth = 360, int targetHeight = 200)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return null;
        if (IsIconic(hwnd)) return null;

        // 1. Get true window bounds using DWM extended frame bounds (excludes invisible drop shadows)
        RECT rect;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) != 0)
        {
            if (!GetWindowRect(hwnd, out rect)) return null;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 64 || height <= 64) return null;

        // Proportional aspect ratio
        if (targetWidth <= 0) targetWidth = 360;
        double ratio = (double)height / width;
        int thumbW = targetWidth;
        int thumbH = Math.Clamp((int)(thumbW * ratio), 120, 320);

        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero) return null;

        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBmpMem = IntPtr.Zero;
        IntPtr hOldMem = IntPtr.Zero;

        IntPtr hdcThumb = IntPtr.Zero;
        IntPtr hBmpThumb = IntPtr.Zero;
        IntPtr hOldThumb = IntPtr.Zero;

        try
        {
            hdcMem = CreateCompatibleDC(hdcScreen);
            hBmpMem = CreateCompatibleBitmap(hdcScreen, width, height);
            hOldMem = SelectObject(hdcMem, hBmpMem);

            bool success = false;

            // Strategy 1: Modern hardware-accelerated PrintWindow
            try
            {
                success = PrintWindow(hwnd, hdcMem, PW_RENDERFULLCONTENT);
            }
            catch { }

            // Strategy 2: Legacy PrintWindow
            if (!success)
            {
                try
                {
                    success = PrintWindow(hwnd, hdcMem, 0);
                }
                catch { }
            }

            // Strategy 3: BitBlt from Window DC
            if (!success)
            {
                IntPtr winDC = GetWindowDC(hwnd);
                if (winDC != IntPtr.Zero)
                {
                    try
                    {
                        success = BitBlt(hdcMem, 0, 0, width, height, winDC, 0, 0, SRCCOPY);
                    }
                    catch { }
                    finally
                    {
                        ReleaseDC(hwnd, winDC);
                    }
                }
            }

            // Strategy 4: BitBlt directly from Screen DC at desktop coordinates
            if (!success)
            {
                try
                {
                    success = BitBlt(hdcMem, 0, 0, width, height, hdcScreen, rect.Left, rect.Top, SRCCOPY);
                }
                catch { }
            }

            if (!success) return null;

            // 2. High-quality Halftone downscale into thumbnail bitmap
            hdcThumb = CreateCompatibleDC(hdcScreen);
            hBmpThumb = CreateCompatibleBitmap(hdcScreen, thumbW, thumbH);
            hOldThumb = SelectObject(hdcThumb, hBmpThumb);

            SetStretchBltMode(hdcThumb, HALFTONE);
            SetBrushOrgEx(hdcThumb, 0, 0, IntPtr.Zero);
            StretchBlt(hdcThumb, 0, 0, thumbW, thumbH, hdcMem, 0, 0, width, height, SRCCOPY);

            // 3. Convert via GDI+ Bitmap to solve the 32-bit GDI alpha=0 (invisible/black) WPF bug
            using var gdiBmp = System.Drawing.Image.FromHbitmap(hBmpThumb);

            // Strategy 5: Detect if PrintWindow returned empty black surface (common with GPU/DirectComposition apps)
            if (gdiBmp is System.Drawing.Bitmap bmpDirect)
            {
                var cCenter = bmpDirect.GetPixel(thumbW / 2, thumbH / 2);
                var c1 = bmpDirect.GetPixel(Math.Max(0, thumbW / 4), Math.Max(0, thumbH / 4));
                var c2 = bmpDirect.GetPixel(Math.Min(thumbW - 1, 3 * thumbW / 4), Math.Min(thumbH - 1, 3 * thumbH / 4));
                bool isAllBlack = (cCenter.R == 0 && cCenter.G == 0 && cCenter.B == 0 &&
                                   c1.R == 0 && c1.G == 0 && c1.B == 0 &&
                                   c2.R == 0 && c2.G == 0 && c2.B == 0);

                if (isAllBlack)
                {
                    // PrintWindow yielded black pixels; grab live screen pixels directly!
                    BitBlt(hdcMem, 0, 0, width, height, hdcScreen, rect.Left, rect.Top, SRCCOPY);
                    StretchBlt(hdcThumb, 0, 0, thumbW, thumbH, hdcMem, 0, 0, width, height, SRCCOPY);
                }
            }

            // 4. Create lossless PNG in memory and load into immutable WPF BitmapImage
            using var finalGdiBmp = System.Drawing.Image.FromHbitmap(hBmpThumb);
            using var ms = new MemoryStream();
            finalGdiBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = ms;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            _thumbnailCache[hwnd] = bitmapImage;
            return bitmapImage;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hOldThumb != IntPtr.Zero) SelectObject(hdcThumb, hOldThumb);
            if (hBmpThumb != IntPtr.Zero) DeleteObject(hBmpThumb);
            if (hdcThumb != IntPtr.Zero) DeleteDC(hdcThumb);

            if (hOldMem != IntPtr.Zero) SelectObject(hdcMem, hOldMem);
            if (hBmpMem != IntPtr.Zero) DeleteObject(hBmpMem);
            if (hdcMem != IntPtr.Zero) DeleteDC(hdcMem);

            if (hdcScreen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    private bool IsTaskbarWindow(IntPtr hwnd, IntPtr ignoreHwnd)
    {
        if (!IsWindowVisible(hwnd)) return false;
        if (hwnd == ignoreHwnd) return false;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == (uint)Environment.ProcessId) return false;

        if (NativeMethods.GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;
        if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return false;

        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0)
        {
            if (cloaked != 0) return false;
        }

        var cn = GetClassNameHelper(hwnd);
        if (cn is "Shell_TrayWnd" or "Progman" or "Button" or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow")
        {
            return false;
        }

        return true;
    }

    private string GetWindowTextHelper(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString().Trim();
    }

    private string GetClassNameHelper(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassNameW(hwnd, sb, sb.Capacity);
        return sb.ToString().Trim();
    }

    private ImageSource? GetWindowIcon(IntPtr hwnd, string processPath)
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

            if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
                if (ico != null)
                {
                    var bmp = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bmp.Freeze();
                    _iconCache[hwnd] = bmp;
                    return bmp;
                }
            }
        }
        catch { }

        return null;
    }

    private string GetProcessPath(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero) return string.Empty;
        try
        {
            var buf = new StringBuilder(1024);
            int sz = buf.Capacity;
            return QueryFullProcessImageNameW(handle, 0, buf, ref sz) ? buf.ToString() : string.Empty;
        }
        catch { return string.Empty; }
        finally { CloseHandle(handle); }
    }

    /// <summary>
    /// Computes a signature chromatic glow color matching the identity of the application.
    /// </summary>
    public static Color ComputeChromaticColor(string processName, string title)
    {
        string p = processName.ToLowerInvariant();
        string t = title.ToLowerInvariant();

        // Music & Media -> Emerald Green / Lime Glow
        if (p.Contains("spotify") || p.Contains("music") || t.Contains("spotify") || p.Contains("ytm") || t.Contains("youtube music"))
        {
            return Color.FromRgb(29, 185, 84); // Spotify green
        }

        // Code, Dev & Engineering -> Deep Electric Cyan / Azure Glow
        if (p.Contains("code") || p.Contains("devenv") || p.Contains("visualstudio") || p.Contains("rider") || p.Contains("sublime"))
        {
            return Color.FromRgb(0, 180, 255); // Neon Cyan
        }

        // Browsers & Web -> Warm Amber / Sunset Coral Glow
        if (p.Contains("chrome") || p.Contains("msedge") || p.Contains("brave") || p.Contains("firefox") || p.Contains("opera") || p.Contains("vivaldi"))
        {
            return Color.FromRgb(255, 122, 0); // Sunset Coral
        }

        // Terminal, Consoles & Dev Tools -> Ultraviolet / Neon Purple
        if (p.Contains("terminal") || p.Contains("powershell") || p.Contains("cmd") || p.Contains("wt") || p.Contains("bash"))
        {
            return Color.FromRgb(168, 85, 247); // Violet Neon
        }

        // File Explorer & OS -> Electric Sapphire Blue
        if (p.Contains("explorer") || t.Contains("explorador") || t.Contains("carpeta"))
        {
            return Color.FromRgb(59, 130, 246); // Royal Blue
        }

        // Communication & Social -> Indigo Glow
        if (p.Contains("discord") || p.Contains("slack") || p.Contains("telegram") || p.Contains("whatsapp") || p.Contains("teams"))
        {
            return Color.FromRgb(99, 102, 241); // Indigo
        }

        // Notes & Knowledge -> Mint / Emerald Teal
        if (p.Contains("obsidian") || p.Contains("notion") || p.Contains("onenote") || p.Contains("word") || p.Contains("excel"))
        {
            return Color.FromRgb(16, 185, 129); // Emerald Mint
        }

        // Gaming & Entertainment -> Crimson / Fire Glow
        if (p.Contains("steam") || p.Contains("epic") || p.Contains("riot") || p.Contains("game"))
        {
            return Color.FromRgb(244, 63, 94); // Crimson
        }

        // Default: Ethereal Frost Ice Cyan
        return Color.FromRgb(56, 189, 248);
    }
}
