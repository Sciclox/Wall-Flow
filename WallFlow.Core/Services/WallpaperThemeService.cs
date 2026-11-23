using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace WallFlow.Services;

public class WallpaperThemeService
{
    private static WallpaperThemeService? _instance;
    public static WallpaperThemeService Instance => _instance ??= new WallpaperThemeService();

    public WallpaperThemeService()
    {
        EventBus.Subscribe<WallpaperChangedEvent>(OnWallpaperChanged);
    }

    private void OnWallpaperChanged(WallpaperChangedEvent @event)
    {
        UpdateThemeFromWallpaper(true);
    }

    private string _lastProcessedWallpaper = "";

    public void UpdateThemeFromWallpaper(bool force = false)
    {
        try
        {
            var s = SettingsService.Instance;
            // Only extract if the user hasn't set a custom accent color
            if (!string.IsNullOrEmpty(s.UserAccentColor))
            {
                return;
            }

            string wallpaperPath = GetCurrentWallpaperPath();
            if (string.IsNullOrEmpty(wallpaperPath) || !File.Exists(wallpaperPath))
            {
                // Fallback to transcoded wallpaper path
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string transcoded = Path.Combine(appData, @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (File.Exists(transcoded))
                {
                    wallpaperPath = transcoded;
                }
                else
                {
                    return;
                }
            }

            if (!force && _lastProcessedWallpaper == wallpaperPath)
            {
                return; // Already processed
            }

            _lastProcessedWallpaper = wallpaperPath;

            // Load and analyze wallpaper colors asynchronously in background thread
            Task.Run(() =>
            {
                ExtractAndApplyColors(wallpaperPath);
            });
        }
        catch { /* ignored */ }
    }

    public static string GetCurrentWallpaperPath()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
            {
                if (key != null)
                {
                    string? path = key.GetValue("Wallpaper") as string;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
            }
        }
        catch { }

        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string transcoded = Path.Combine(appData, @"Microsoft\Windows\Themes\TranscodedWallpaper");
            if (File.Exists(transcoded))
            {
                return transcoded;
            }
        }
        catch { }

        return "";
    }

    private void ExtractAndApplyColors(string filePath)
    {
        try
        {
            // Load bitmap frame safely in background
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) return;
                var frame = decoder.Frames[0];

                // Scale down to 16x16 pixels using TransformedBitmap
                var scale = new ScaleTransform(16.0 / frame.PixelWidth, 16.0 / frame.PixelHeight);
                var resized = new TransformedBitmap(frame, scale);

                // Copy pixels
                int width = resized.PixelWidth;
                int height = resized.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[height * stride];
                resized.CopyPixels(pixels, stride, 0);

                // Find dominant color
                Color dominant = FindDominantColor(pixels);

                // Lighten for hover
                Color hover = LightenColor(dominant);

                // Dispatch UI updates to main thread
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplyThemeResources(dominant, hover);
                }));
            }
        }
        catch { /* ignored */ }
    }

    private static void ApplyThemeResources(Color dominant, Color hover)
    {
        if (Application.Current == null) return;

        // Apply resources dynamically
        Application.Current.Resources["AccentColor"] = dominant;
        Application.Current.Resources["AccentHoverColor"] = hover;
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush(dominant);
        Application.Current.Resources["AccentHoverBrush"] = new SolidColorBrush(hover);
        Application.Current.Resources["AccentGlowBrush"] = new SolidColorBrush(Color.FromArgb(0x18, dominant.R, dominant.G, dominant.B));
        Application.Current.Resources["AccentActiveBrush"] = new SolidColorBrush(Color.FromArgb(0x65, dominant.R, dominant.G, dominant.B));

        // Border colors (gradient stops)
        Application.Current.Resources["BorderColor1"] = dominant;
        Application.Current.Resources["BorderColor2"] = LightenColor(dominant, 20);
        Application.Current.Resources["BorderColor3"] = hover;

        // Visualizer wave brush
        var waveBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        waveBrush.GradientStops.Add(new GradientStop(hover, 0));
        waveBrush.GradientStops.Add(new GradientStop(dominant, 1));
        Application.Current.Resources["VisualizerWaveBrush"] = waveBrush;

        // DockBorderBrush
        var defaultBrush = Application.Current.Resources["AccentBrush"] as Brush ?? new SolidColorBrush(dominant);
        Application.Current.Resources["DockBorderBrush"] = defaultBrush;

        // Notify UI update
        EventBus.Publish(new PreviewColorsRequestedEvent());
    }

    private Color FindDominantColor(byte[] pixels)
    {
        double maxSaturation = -1;
        Color bestColor = Color.FromRgb(0xAF, 0x52, 0xDE); // default fallback (Zen purple)
        long sumR = 0, sumG = 0, sumB = 0;
        int count = 0;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            byte a = pixels[i + 3];

            if (a < 128) continue; // skip transparent pixels

            sumR += r;
            sumG += g;
            sumB += b;
            count++;

            // Convert to HSL
            RGBToHSL(r, g, b, out double h, out double s, out double l);

            // Prioritize colors that are colorful (saturation) and readable (lightness between 30% and 75%)
            if (l >= 0.3 && l <= 0.75)
            {
                if (s > maxSaturation)
                {
                    maxSaturation = s;
                    bestColor = Color.FromRgb(r, g, b);
                }
            }
        }

        // If no colorful pixel found, fallback to average color
        if (maxSaturation < 0.15 && count > 0)
        {
            byte avgR = (byte)(sumR / count);
            byte avgG = (byte)(sumG / count);
            byte avgB = (byte)(sumB / count);
            bestColor = Color.FromRgb(avgR, avgG, avgB);
        }

        return bestColor;
    }

    private void RGBToHSL(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;

        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));

        h = s = l = (max + min) / 2.0;

        if (max == min)
        {
            h = s = 0; // achromatic
        }
        else
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

            if (max == rd)
            {
                h = (gd - bd) / d + (gd < bd ? 6 : 0);
            }
            else if (max == gd)
            {
                h = (bd - rd) / d + 2;
            }
            else if (max == bd)
            {
                h = (rd - gd) / d + 4;
            }

            h /= 6.0;
        }
    }

    private static Color LightenColor(Color color, int amount = 35)
    {
        byte r = (byte)Math.Min(255, color.R + amount);
        byte g = (byte)Math.Min(255, color.G + amount);
        byte b = (byte)Math.Min(255, color.B + amount);
        return Color.FromRgb(r, g, b);
    }
}
