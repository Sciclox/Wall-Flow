using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace WallFlow;

public static class WallpaperTransitionService
{
    private const int SPI_GETDESKWALLPAPER = 0x0073;
    private const int SPI_SETDESKWALLPAPER = 20;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, StringBuilder lpvParam, int fuWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public static string GetCurrentWallpaperPath()
    {
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

    private static Window? _currentFadeOverlay;
    private static readonly object _lockObj = new();

    public static void ApplyWallpaperWithFade(string newFilePath, Action? onCompleted = null)
    {
        if (string.IsNullOrEmpty(newFilePath) || !File.Exists(newFilePath))
            return;

        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(() => ApplyWallpaperWithFade(newFilePath, onCompleted));
            return;
        }

        string currentWallpaper = GetCurrentWallpaperPath();

        if (string.Equals(currentWallpaper, newFilePath, StringComparison.OrdinalIgnoreCase))
        {
            onCompleted?.Invoke();
            return;
        }

        lock (_lockObj)
        {
            if (_currentFadeOverlay != null)
            {
                try
                {
                    _currentFadeOverlay.Close();
                }
                catch { }
                _currentFadeOverlay = null;
            }
        }

        Window? fadeOverlay = null;

        try
        {
            if (!string.IsNullOrEmpty(currentWallpaper) && File.Exists(currentWallpaper))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(currentWallpaper);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 1920; // Decode at reasonable resolution to avoid massive memory allocations
                bitmap.EndInit();
                bitmap.Freeze();

                var image = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                fadeOverlay = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = false,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Left = SystemParameters.VirtualScreenLeft,
                    Top = SystemParameters.VirtualScreenTop,
                    Width = SystemParameters.VirtualScreenWidth,
                    Height = SystemParameters.VirtualScreenHeight,
                    Content = image,
                    Opacity = 1.0
                };

                fadeOverlay.SourceInitialized += (s, e) =>
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(fadeOverlay);
                    int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                    SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
                };

                lock (_lockObj)
                {
                    _currentFadeOverlay = fadeOverlay;
                }

                fadeOverlay.Show();
            }
        }
        catch { }

        Task.Run(() =>
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, newFilePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

            if (Application.Current != null)
            {
                Application.Current.Dispatcher.BeginInvoke(async () =>
                {
                    await Task.Delay(50);

                    if (fadeOverlay != null)
                    {
                        var fadeAnim = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(250)))
                        {
                            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
                        };

                        fadeAnim.Completed += (s, e) =>
                        {
                            try
                            {
                                lock (_lockObj)
                                {
                                    if (_currentFadeOverlay == fadeOverlay)
                                    {
                                        _currentFadeOverlay = null;
                                    }
                                }
                                fadeOverlay.Close();
                            }
                            catch { }
                            onCompleted?.Invoke();
                        };

                        fadeOverlay.BeginAnimation(Window.OpacityProperty, fadeAnim);
                    }
                    else
                    {
                        onCompleted?.Invoke();
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            else
            {
                onCompleted?.Invoke();
            }
        });
    }
}
