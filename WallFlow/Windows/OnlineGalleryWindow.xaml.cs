using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WallFlow.Services;

namespace WallFlow.Windows;

public partial class OnlineGalleryWindow : Window
{
    private readonly UHDPaperService _service = new();
    private readonly string _wallpaperDir;
    private bool _isOnline = true;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private List<OnlineWallpaper> _onlineWallpapers = new();
    private List<OnlineWallpaper> _filteredWallpapers = new();
    private OnlineWallpaper? _selectedWallpaper;
    private string _currentQuery = "";
    private bool _isDetailOpen;

    private static readonly string[] SupportedExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    private const int SPI_SETDESKWALLPAPER = 20;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    public OnlineGalleryWindow()
    {
        InitializeComponent();
        _wallpaperDir = GetWallpaperDirectory();
        _ = LoadOnlineAsync();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        try
        {
            var exStyle = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }
        catch { }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fadeIn);
        SearchBox.Focus();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        CloseWithAnimation();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_isDetailOpen)
                BackBtn_Click(null, null);
            else
                CloseWithAnimation();
            e.Handled = true;
        }
    }

    // ---- Source toggle ----

    private void SourceToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _isOnline = !_isOnline;
        UpdateSourceToggle();
        _currentPage = 1;
        _currentQuery = "";

        if (_isDetailOpen)
            GalleryView.Visibility = Visibility.Visible;
        _isDetailOpen = false;
        DetailView.Visibility = Visibility.Collapsed;

        if (_isOnline)
        {
            _ = LoadOnlineAsync();
        }
        else
        {
            LoadLocalWallpapers();
        }
    }

    private void UpdateSourceToggle()
    {
        if (_isOnline)
        {
            LocalTab.Background = Brushes.Transparent;
            LocalText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            LocalText.FontWeight = FontWeights.Normal;
            OnlineTab.Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D));
            OnlineText.Foreground = Brushes.White;
            OnlineText.FontWeight = FontWeights.SemiBold;
            SearchBox.Text = _currentQuery;
        }
        else
        {
            OnlineTab.Background = Brushes.Transparent;
            OnlineText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            OnlineText.FontWeight = FontWeights.Normal;
            LocalTab.Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D));
            LocalText.Foreground = Brushes.White;
            LocalText.FontWeight = FontWeights.SemiBold;
            SearchBox.Text = "";
        }
    }

    // ---- Search ----

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _currentQuery = SearchBox.Text.Trim().ToLowerInvariant();

        if (_isOnline)
        {
            _currentPage = 1;
            if (string.IsNullOrEmpty(_currentQuery))
            {
                await LoadOnlineAsync();
            }
            else
            {
                await SearchOnlineAsync(_currentQuery);
            }
        }
        else
        {
            FilterLocalWallpapers();
        }
    }

    // ---- Online loading ----

    private async Task LoadOnlineAsync()
    {
        ShowLoading(true);
        try
        {
            var items = await _service.GetLatestAsync(_currentPage);
            _onlineWallpapers = items;
            _filteredWallpapers = items;
            _totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / 20.0));
            RenderWallpapers(items);
            UpdatePagination();
        }
        catch
        {
            StatusText.Text = "Error al cargar wallpapers online";
        }
        ShowLoading(false);
    }

    private async Task SearchOnlineAsync(string query)
    {
        ShowLoading(true);
        try
        {
            var items = await _service.SearchAsync(query, _currentPage);
            _onlineWallpapers = items;
            _filteredWallpapers = items;
            _totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / 20.0));
            RenderWallpapers(items);
            UpdatePagination();
        }
        catch
        {
            StatusText.Text = "Error al buscar";
        }
        ShowLoading(false);
    }

    // ---- Local loading ----

    private List<WallpaperEntry> _localWallpapers = new();
    private List<WallpaperEntry> _filteredLocal = new();

    private void LoadLocalWallpapers()
    {
        _localWallpapers.Clear();
        if (!Directory.Exists(_wallpaperDir))
            return;

        var files = Directory.EnumerateFiles(_wallpaperDir, "*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f)
            .ToList();

        foreach (var file in files)
        {
            _localWallpapers.Add(new WallpaperEntry
            {
                FilePath = file,
                FileName = Path.GetFileNameWithoutExtension(file)
            });
        }

        _filteredLocal = new List<WallpaperEntry>(_localWallpapers);
        RenderLocalWallpapers();
        _totalPages = 1;
        UpdatePagination();
    }

    private void FilterLocalWallpapers()
    {
        if (string.IsNullOrEmpty(_currentQuery))
        {
            _filteredLocal = new List<WallpaperEntry>(_localWallpapers);
        }
        else
        {
            _filteredLocal = _localWallpapers
                .Where(w => w.FileName.ToLowerInvariant().Contains(_currentQuery))
                .ToList();
        }
        RenderLocalWallpapers();
    }

    // ---- Rendering ----

    private void ShowLoading(bool loading)
    {
        LoadingText.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderWallpapers(List<OnlineWallpaper> items)
    {
        var tiles = new List<UIElement>();
        var pageSize = 20;
        var pageItems = items.Take(pageSize).ToList();

        foreach (var wp in pageItems)
        {
            var tile = CreateOnlineTile(wp);
            tiles.Add(tile);
        }

        WallpaperGrid.ItemsSource = tiles;
    }

    private void RenderLocalWallpapers()
    {
        var tiles = new List<UIElement>();

        foreach (var entry in _filteredLocal)
        {
            var tile = CreateLocalTile(entry);
            tiles.Add(tile);
        }

        WallpaperGrid.ItemsSource = tiles;
    }

    private Border CreateOnlineTile(OnlineWallpaper wp)
    {
        var border = new Border
        {
            Width = 190,
            Height = 140,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Cursor = Cursors.Hand,
            Tag = wp,
        };

        var cachedPath = _service.GetCachedThumbPath(wp.Slug);
        if (!string.IsNullOrEmpty(cachedPath))
        {
            var brush = CreateImageBrush(cachedPath, 380, 280);
            border.Background = brush;
        }
        else
        {
            border.Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D));
            _ = LoadThumbnailAsync(wp, border);
        }

        var titleOverlay = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(0xAA, 0x00, 0x00, 0x00)),
            Padding = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(0, 0, 8, 8),
            Child = new TextBlock
            {
                Text = wp.Title,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 178,
            }
        };
        border.Child = titleOverlay;

        border.MouseLeftButtonUp += async (_, _) =>
        {
            await OpenDetailAsync(wp);
        };

        return border;
    }

    private Border CreateLocalTile(WallpaperEntry entry)
    {
        ImageBrush brush;
        try
        {
            brush = CreateImageBrush(entry.FilePath, 380, 280);
        }
        catch
        {
            brush = new ImageBrush();
        }

        var border = new Border
        {
            Width = 190,
            Height = 140,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Cursor = Cursors.Hand,
            Tag = entry,
            Background = brush,
        };

        var titleOverlay = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(0xAA, 0x00, 0x00, 0x00)),
            Padding = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(0, 0, 8, 8),
            Child = new TextBlock
            {
                Text = entry.FileName,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 178,
            }
        };
        border.Child = titleOverlay;

        border.MouseLeftButtonUp += (_, _) =>
        {
            ApplyLocalWallpaper(entry);
        };

        return border;
    }

    private async Task LoadThumbnailAsync(OnlineWallpaper wp, Border target)
    {
        try
        {
            if (string.IsNullOrEmpty(wp.ThumbnailUrl))
                return;

            var cached = await _service.CacheThumbnailAsync(wp.ThumbnailUrl, wp.Slug);
            if (string.IsNullOrEmpty(cached))
                return;

            var brush = CreateImageBrush(cached, 380, 280);
            await Dispatcher.BeginInvoke(() =>
            {
                target.Background = brush;
            });
        }
        catch { }
    }

    // ---- Detail view ----

    private async Task OpenDetailAsync(OnlineWallpaper wp)
    {
        _selectedWallpaper = wp;
        _isDetailOpen = true;
        GalleryView.Visibility = Visibility.Collapsed;
        DetailView.Visibility = Visibility.Visible;
        FooterBar.Visibility = Visibility.Collapsed;
        DetailTitle.Text = wp.Title;
        StatusText.Text = "Cargando resolución...";
        PreviewBrush.ImageSource = null;

        var detail = await _service.GetDetailAsync(wp);
        if (detail == null || detail.Resolutions.Count == 0)
        {
            StatusText.Text = "No se encontraron resoluciones disponibles";
            return;
        }

        _selectedWallpaper = detail;

        UpdateAvailableResolutions(detail.Resolutions);

        var previewUrl = GetSelectedResolutionUrl();
        if (!string.IsNullOrEmpty(previewUrl))
        {
            StatusText.Text = "Cargando vista previa...";
            var tempPath = _service.GetTempPath(detail.Slug, GetSelectedResolutionKey());
            var downloaded = await _service.DownloadImageAsync(previewUrl,
                Path.GetDirectoryName(tempPath)!,
                Path.GetFileName(tempPath));

            if (!string.IsNullOrEmpty(downloaded) && File.Exists(downloaded))
            {
                var brush = CreateImageBrush(downloaded, 800, 600);
                await Dispatcher.BeginInvoke(() =>
                {
                    PreviewBrush.ImageSource = brush.ImageSource;
                    StatusText.Text = "";
                });
            }
            else
            {
                await Dispatcher.BeginInvoke(() =>
                {
                    StatusText.Text = "Error al cargar vista previa";
                });
            }
        }
    }

    private void UpdateAvailableResolutions(Dictionary<string, string> resolutions)
    {
        foreach (ComboBoxItem item in ResolutionCombo.Items)
        {
            var key = item.Tag.ToString();
            item.IsEnabled = key != null && resolutions.ContainsKey(key);
        }

        // Select the best available
        var preferred = new[] { "4k", "2k", "hd", "phone-4k", "phone-hd" };
        for (int i = 0; i < preferred.Length; i++)
        {
            if (resolutions.ContainsKey(preferred[i]))
            {
                ResolutionCombo.SelectedIndex = i;
                return;
            }
        }
        ResolutionCombo.SelectedIndex = 0;
    }

    private string GetSelectedResolutionUrl()
    {
        if (_selectedWallpaper == null) return "";
        var key = GetSelectedResolutionKey();
        return _selectedWallpaper.Resolutions.TryGetValue(key, out var url) ? url : "";
    }

    private string GetSelectedResolutionKey()
    {
        if (ResolutionCombo.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? "4k";
        return "4k";
    }

    // ---- Actions ----

    private async void ApplyBtn_Click(object sender, MouseButtonEventArgs e)
    {
        if (_selectedWallpaper == null) return;
        var url = GetSelectedResolutionUrl();
        if (string.IsNullOrEmpty(url))
        {
            StatusText.Text = "Resolución no disponible";
            return;
        }

        StatusText.Text = "Descargando...";
        var key = GetSelectedResolutionKey();
        var tempPath = _service.GetTempPath(_selectedWallpaper.Slug, key);

        var downloaded = await _service.DownloadImageAsync(url,
            Path.GetDirectoryName(tempPath)!,
            Path.GetFileName(tempPath));

        if (string.IsNullOrEmpty(downloaded) || !File.Exists(downloaded))
        {
            StatusText.Text = "Error al descargar";
            return;
        }

        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, downloaded,
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        StatusText.Text = "¡Aplicado como wallpaper!";
    }

    private async void SaveBtn_Click(object sender, MouseButtonEventArgs e)
    {
        if (_selectedWallpaper == null) return;
        var url = GetSelectedResolutionUrl();
        if (string.IsNullOrEmpty(url))
        {
            StatusText.Text = "Resolución no disponible";
            return;
        }

        if (!Directory.Exists(_wallpaperDir))
        {
            StatusText.Text = "El directorio de wallpapers no existe";
            return;
        }

        StatusText.Text = "Guardando...";
        var key = GetSelectedResolutionKey();
        var fileName = $"{_selectedWallpaper.Slug}-{key}.jpg";
        var path = await _service.DownloadImageAsync(url, _wallpaperDir, fileName);

        if (string.IsNullOrEmpty(path))
        {
            StatusText.Text = "Error al guardar";
            return;
        }

        StatusText.Text = "¡Guardado en la biblioteca local!";
    }

    private void ViewOnline_Click(object sender, MouseButtonEventArgs e)
    {
        if (_selectedWallpaper != null && !string.IsNullOrEmpty(_selectedWallpaper.PostUrl))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _selectedWallpaper.PostUrl,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }
    }

    private void BackBtn_Click(object? sender, MouseButtonEventArgs? e)
    {
        _isDetailOpen = false;
        _selectedWallpaper = null;
        DetailView.Visibility = Visibility.Collapsed;
        GalleryView.Visibility = Visibility.Visible;
        FooterBar.Visibility = Visibility.Visible;
        StatusText.Text = "";
    }

    private void ApplyLocalWallpaper(WallpaperEntry entry)
    {
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, entry.FilePath,
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        StatusText.Text = $"Aplicado: {entry.FileName}";
    }

    // ---- Pagination ----

    private void PrevBtn_Click(object sender, MouseButtonEventArgs e)
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            _ = ReloadCurrentViewAsync();
        }
    }

    private void NextBtn_Click(object sender, MouseButtonEventArgs e)
    {
        if (_currentPage < _totalPages)
        {
            _currentPage++;
            _ = ReloadCurrentViewAsync();
        }
    }

    private async Task ReloadCurrentViewAsync()
    {
        if (_isOnline)
        {
            if (string.IsNullOrEmpty(_currentQuery))
                await LoadOnlineAsync();
            else
                await SearchOnlineAsync(_currentQuery);
        }
        else
        {
            FilterLocalWallpapers();
        }
    }

    private void UpdatePagination()
    {
        PageText.Text = $"Página {_currentPage} de {_totalPages}";
        PrevBtn.IsEnabled = _currentPage > 1;
        NextBtn.IsEnabled = _currentPage < _totalPages;
        PrevBtn.Opacity = PrevBtn.IsEnabled ? 1.0 : 0.4;
        NextBtn.Opacity = NextBtn.IsEnabled ? 1.0 : 0.4;
    }

    // ---- Helpers ----

    private static ImageBrush CreateImageBrush(string filePath, int decodeWidth, int decodeHeight)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(filePath);
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.DecodePixelHeight = decodeHeight;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        return new ImageBrush(bitmap)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };
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

    private void CloseBtn_Click(object sender, MouseButtonEventArgs e)
    {
        CloseWithAnimation();
    }

    private void CloseBtn_MouseEnter(object sender, MouseEventArgs e)
    {
        CloseBtn.Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A));
    }

    private void CloseBtn_MouseLeave(object sender, MouseEventArgs e)
    {
        CloseBtn.Background = Brushes.Transparent;
    }

    private void CloseWithAnimation()
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    protected override void OnClosed(EventArgs e)
    {
        _service.Dispose();
        base.OnClosed(e);
    }
}
