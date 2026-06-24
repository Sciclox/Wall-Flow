using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace WallFlow;

public partial class LauncherWindow : Window
{
    private readonly List<WallpaperEntry> _allWallpapers = new();
    private readonly string _wallpaperDir;
    private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);
    private const int SPI_SETDESKWALLPAPER = 20;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    public LauncherWindow()
    {
        InitializeComponent();
        _wallpaperDir = GetWallpaperDirectory();
        LoadWallpapers();
        SearchBox.Focus();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = PresentationSource.FromVisual(this) as HwndSource;
        var handle = source?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            var exStyle = GetWindowLong(handle, -20);
            SetWindowLong(handle, -20, exStyle | 0x00000080);
        }
    }

    private static string GetWallpaperDirectory()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wallflow.txt");
        if (File.Exists(configPath))
        {
            var dir = File.ReadAllText(configPath).Trim();
            if (Directory.Exists(dir))
                return dir;
        }

        var defaultDir = @"C:\Users\Lenovo\OneDrive\Imágenes\Wallpapers";
        if (!Directory.Exists(defaultDir))
        {
            var picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (Directory.Exists(picturesDir))
                return picturesDir;
        }
        return defaultDir;
    }

    private void LoadWallpapers()
    {
        if (!Directory.Exists(_wallpaperDir))
            return;

        var files = Directory.EnumerateFiles(_wallpaperDir, "*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f)
            .ToList();

        foreach (var file in files)
        {
            _allWallpapers.Add(new WallpaperEntry
            {
                FilePath = file,
                FileName = Path.GetFileNameWithoutExtension(file)
            });
        }

        ResultsList.ItemsSource = _allWallpapers;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(query))
        {
            ResultsList.ItemsSource = _allWallpapers;
        }
        else
        {
            ResultsList.ItemsSource = _allWallpapers
                .Where(w => w.FileName.ToLowerInvariant().Contains(query))
                .ToList();
        }

        if (ResultsList.Items.Count > 0 && ResultsList.SelectedIndex < 0)
            ResultsList.SelectedIndex = 0;
    }

    private void SetSelectedWallpaper()
    {
        if (ResultsList.SelectedItem is WallpaperEntry entry)
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, entry.FilePath,
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            CloseWithAnimation();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseWithAnimation();
                e.Handled = true;
                break;
            case Key.Enter:
                SetSelectedWallpaper();
                e.Handled = true;
                break;
            case Key.Down:
                if (ResultsList.SelectedIndex < ResultsList.Items.Count - 1)
                    ResultsList.SelectedIndex++;
                e.Handled = true;
                break;
            case Key.Up:
                if (ResultsList.SelectedIndex > 0)
                    ResultsList.SelectedIndex--;
                e.Handled = true;
                break;
        }
    }

    private void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem != null)
            SetSelectedWallpaper();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        Close();
    }

    private void CloseWithAnimation()
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int dwNewLong);
}
