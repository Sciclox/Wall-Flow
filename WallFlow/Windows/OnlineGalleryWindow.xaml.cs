using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using WallFlow.Services;

namespace WallFlow.Windows;

public partial class OnlineGalleryWindow : Window
{
    private readonly UHDPaperService _service = new();
    private readonly string _wallpaperDir;
    private readonly ObservableCollection<StackPanel> _rows = new();
    private readonly SemaphoreSlim _thumbnailThrottle = new(2, 2);
    private readonly ConcurrentDictionary<string, (long Size, DateTime LastWrite)> _fileInfoCache = new();

    private bool _isOnline;
    private string _currentQuery = "";
    private bool _isDetailOpen;
    private bool _loaded;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private OnlineWallpaper? _selectedWallpaper;
    private ScrollViewer? _listBoxScrollViewer;

    private CancellationTokenSource? _cts;
    private DispatcherTimer? _searchDebounce;
    private double _savedScrollOffset;

    private List<OnlineWallpaper> _filteredWallpapers = new();
    private List<WallpaperEntry> _localWallpapers = new();
    private List<WallpaperEntry> _filteredLocal = new();

    private static readonly string[] SupportedExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    private const int SPI_SETDESKWALLPAPER = 20;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    private const int PAGE_SIZE = 20;
    private const int TILES_PER_ROW = 4;

    public OnlineGalleryWindow()
    {
        InitializeComponent();
        _wallpaperDir = GetWallpaperDirectory();
        WallpaperListBox.ItemsSource = _rows;
        InitSearchDebounce();
    }

    private void InitSearchDebounce()
    {
        _searchDebounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            _ = ExecuteSearchAsync();
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _listBoxScrollViewer = FindScrollViewer(WallpaperListBox);

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fadeIn);
        SearchBox.Focus();

        if (!_loaded)
        {
            _loaded = true;
            UpdateSourceToggle();
            _ = LoadLocalWallpapersAsync();
        }
    }

    private void Window_Deactivated(object sender, EventArgs e) { }

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
        else if (e.Key == Key.Enter && _isDetailOpen)
        {
            ApplyBtn_Click(null, null);
            e.Handled = true;
        }
    }

    // ---- Helpers ----

    private static ScrollViewer? FindScrollViewer(DependencyObject dep)
    {
        if (dep is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dep); i++)
        {
            var result = FindScrollViewer(VisualTreeHelper.GetChild(dep, i));
            if (result != null) return result;
        }
        return null;
    }

    // ---- Cancellation ----

    private void CancelPending()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
    }

    private CancellationToken GetToken() => _cts?.Token ?? CancellationToken.None;

    // ---- Source toggle ----

    private void SourceToggle_Click(object sender, MouseButtonEventArgs e)
    {
        CancelPending();
        _currentPage = 1;
        _isOnline = !_isOnline;
        UpdateSourceToggle();
        _currentQuery = "";
        SearchBox.Text = "";

        if (_isDetailOpen)
            GalleryView.Visibility = Visibility.Visible;
        _isDetailOpen = false;
        DetailView.Visibility = Visibility.Collapsed;

        _rows.Clear();
        _listBoxScrollViewer?.ScrollToTop();

        if (_isOnline)
            _ = LoadOnlineAsync();
        else
            _ = LoadLocalWallpapersAsync();
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
            SortCombo.Visibility = Visibility.Collapsed;
        }
        else
        {
            OnlineTab.Background = Brushes.Transparent;
            OnlineText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            OnlineText.FontWeight = FontWeights.Normal;
            LocalTab.Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D));
            LocalText.Foreground = Brushes.White;
            LocalText.FontWeight = FontWeights.SemiBold;
            SortCombo.Visibility = Visibility.Visible;
        }
    }

    // ---- Search ----

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _currentQuery = SearchBox.Text.Trim().ToLowerInvariant();
        SearchClearBtn.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Collapsed : Visibility.Visible;
        _searchDebounce?.Stop();
        _searchDebounce?.Start();
    }

    private void SearchClearBtn_Click(object sender, MouseButtonEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private async Task ExecuteSearchAsync()
    {
        _currentPage = 1;
        if (!_isOnline)
        {
            FilterLocalWallpapers();
            return;
        }

        CancelPending();
        _service.ResetPagination();
        _rows.Clear();
        _listBoxScrollViewer?.ScrollToTop();

        if (string.IsNullOrEmpty(_currentQuery))
            await LoadOnlineAsync();
        else
            await SearchOnlineAsync(_currentQuery);
    }

    // ---- Online loading ----

    private async Task LoadOnlineAsync()
    {
        ShowLoading(true);
        try
        {
            var result = await _service.GetLatestAsync(GetToken());
            _filteredWallpapers = result.Items;
            RenderWallpapers(result.Items);
            UpdatePagination();
        }
        catch (OperationCanceledException) { }
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
            var result = await _service.SearchAsync(query, GetToken());
            _filteredWallpapers = result.Items;
            RenderWallpapers(result.Items);
            UpdatePagination();
        }
        catch (OperationCanceledException) { }
        catch
        {
            StatusText.Text = "Error al buscar";
        }
        ShowLoading(false);
    }

    // ---- Local loading ----

    private async Task LoadLocalWallpapersAsync()
    {
        _localWallpapers.Clear();
        _fileInfoCache.Clear();
        if (!Directory.Exists(_wallpaperDir))
        {
            UpdateEmptyState();
            return;
        }

        var results = await Task.Run(() =>
        {
            var files = Directory.EnumerateFiles(_wallpaperDir, "*", SearchOption.AllDirectories)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f)
                .ToList();

            var wallpapers = new List<WallpaperEntry>();
            var cache = new ConcurrentDictionary<string, (long Size, DateTime LastWrite)>();

            foreach (var file in files)
            {
                wallpapers.Add(new WallpaperEntry
                {
                    FilePath = file,
                    FileName = Path.GetFileNameWithoutExtension(file)
                });
                try
                {
                    var fi = new FileInfo(file);
                    cache[file] = (fi.Length, fi.LastWriteTime);
                }
                catch { }
            }

            return (wallpapers, cache);
        });

        _localWallpapers = results.wallpapers;
        foreach (var kvp in results.cache)
            _fileInfoCache[kvp.Key] = kvp.Value;

        _filteredLocal = new List<WallpaperEntry>(_localWallpapers);
        ApplySort();
        _currentPage = 1;
        RenderLocalWallpapers();
    }

    private void FilterLocalWallpapers()
    {
        if (string.IsNullOrEmpty(_currentQuery))
            _filteredLocal = new List<WallpaperEntry>(_localWallpapers);
        else
            _filteredLocal = _localWallpapers
                .Where(w => w.FileName.ToLowerInvariant().Contains(_currentQuery))
                .ToList();

        ApplySort();
        _currentPage = 1;
        _rows.Clear();
        RenderLocalWallpapers();
    }

    // ---- Rendering ----

    private void ShowLoading(bool loading)
    {
        LoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateEmptyState()
    {
        var hasItems = _isOnline
            ? _filteredWallpapers.Count > 0
            : _filteredLocal.Count > 0;

        if (hasItems)
            EmptyOverlay.Visibility = Visibility.Collapsed;
        else
        {
            EmptyOverlay.Visibility = Visibility.Visible;
            EmptyText.Text = _isOnline
                ? "No se encontraron wallpapers online"
                : "No hay wallpapers locales en esta carpeta";
        }
    }

    private void RenderWallpapers(List<OnlineWallpaper> items)
    {
        UpdateEmptyState();
        _rows.Clear();
        var tiles = items.Select(CreateOnlineTile).ToList();
        AddTilesToRows(tiles);
    }

    private void RenderLocalWallpapers()
    {
        UpdateEmptyState();
        _rows.Clear();

        _totalPages = Math.Max(1, (int)Math.Ceiling((double)_filteredLocal.Count / PAGE_SIZE));
        _currentPage = Math.Clamp(_currentPage, 1, _totalPages);

        var batch = _filteredLocal
            .Skip((_currentPage - 1) * PAGE_SIZE)
            .Take(PAGE_SIZE)
            .ToList();

        var borders = batch.Select(CreateLocalTile).ToList();
        AddTilesToRows(borders);
        _ = LoadLocalThumbnailsAsync(batch, borders);
        UpdatePagination();
    }

    private void AddTilesToRows(List<Border> tiles)
    {
        StackPanel? row = null;
        foreach (var tile in tiles)
        {
            if (row == null || row.Children.Count >= TILES_PER_ROW)
            {
                row = new StackPanel { Orientation = Orientation.Horizontal };
                _rows.Add(row);
            }
            row.Children.Add(tile);
        }
    }

    // ---- Tile creation ----

    private static readonly SolidColorBrush AccentGlow = new(Color.FromRgb(0x5B, 0x7F, 0xB5));
    private static readonly DropShadowEffect TileShadow = new()
    { BlurRadius = 8, Opacity = 0.35, ShadowDepth = 2, Color = Colors.Black };

    private static Grid CreateTileWithPlaceholder(string title)
    {
        var grid = new Grid();
        grid.Children.Add(new TextBlock
        {
            Text = "\uE722",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 28,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });
        grid.Children.Add(CreateTitleOverlay(title));
        return grid;
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
            Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(2),
            Effect = TileShadow,
        };
        AddTileHover(border);
        border.Child = CreateTileWithPlaceholder(wp.Title);
        border.MouseLeftButtonUp += async (_, _) =>
        {
            try { if (!_isDetailOpen) await OpenDetailAsync(wp); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Gallery click error: {ex.Message}"); }
        };
        border.MouseRightButtonUp += (_, _) =>
        {
            if (border.ContextMenu != null) return;
            var browserItem = new MenuItem { Header = "Abrir en navegador" };
            browserItem.Click += (_, _) => OpenInBrowser(wp.PostUrl);
            var copyPostItem = new MenuItem { Header = "Copiar URL del post" };
            copyPostItem.Click += (_, _) => CopyToClipboard(wp.PostUrl);
            var copyImgItem = new MenuItem { Header = "Copiar URL de imagen" };
            copyImgItem.Click += (_, _) =>
            {
                if (!string.IsNullOrEmpty(wp.ThumbnailUrl))
                    CopyToClipboard(wp.ThumbnailUrl);
            };
            border.ContextMenu = CreateDarkContextMenu([browserItem, copyPostItem, copyImgItem]);
        };
        _ = LoadOnlineTileThumbnailAsync(wp, border);
        return border;
    }

    private Border CreateLocalTile(WallpaperEntry entry)
    {
        var border = new Border
        {
            Width = 190,
            Height = 140,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Cursor = Cursors.Hand,
            Tag = entry,
            Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(2),
            Effect = TileShadow,
        };
        AddTileHover(border);
        border.Child = CreateTileWithPlaceholder(entry.FileName);
        border.MouseLeftButtonUp += (_, _) => ApplyLocalWallpaper(entry);
        border.MouseRightButtonUp += (_, _) =>
        {
            if (border.ContextMenu != null) return;
            var applyItem = new MenuItem { Header = "Aplicar" };
            applyItem.Click += (_, _) => ApplyLocalWallpaper(entry);
            var openItem = new MenuItem { Header = "Abrir ubicación" };
            openItem.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{entry.FilePath}\""); }
                catch { }
            };
            border.ContextMenu = CreateDarkContextMenu([applyItem, openItem]);
        };
        return border;
    }

    private static void AddTileHover(Border border)
    {
        border.MouseEnter += (_, _) =>
            border.BorderBrush = AccentGlow;
        border.MouseLeave += (_, _) =>
            border.BorderBrush = Brushes.Transparent;
    }

    private static Border CreateTitleOverlay(string text)
    {
        return new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(0xAA, 0x00, 0x00, 0x00)),
            Padding = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(0, 0, 8, 8),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 178,
            }
        };
    }

    private static ContextMenu CreateDarkContextMenu(MenuItem[] items)
    {
        var bg = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
        var fg = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
        var borderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50));
        foreach (var item in items)
        {
            item.Background = bg;
            item.Foreground = fg;
            item.BorderBrush = borderBrush;
        }
        return new ContextMenu
        {
            Background = bg,
            Foreground = fg,
            BorderBrush = borderBrush,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            ItemsSource = items,
        };
    }

    // ---- Thumbnail loading ----

    private async Task LoadOnlineTileThumbnailAsync(OnlineWallpaper wp, Border target)
    {
        await _thumbnailThrottle.WaitAsync();
        try
        {
            var bytes = await GetThumbnailBytesAsync(wp);
            if (bytes == null) return;

            var bitmap = await Task.Run(() =>
            {
                try
                {
                    var b = new BitmapImage();
                    b.BeginInit();
                    b.StreamSource = new MemoryStream(bytes);
                    b.DecodePixelWidth = 380;
                    b.DecodePixelHeight = 280;
                    b.CacheOption = BitmapCacheOption.OnLoad;
                    b.EndInit();
                    b.Freeze();
                    return b;
                }
                catch
                {
                    System.Diagnostics.Debug.WriteLine($"Thumbnail decode failed for {wp.Slug}");
                    return null;
                }
            });
            if (bitmap == null) return;

            await Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    target.Child = CreateTitleOverlay(wp.Title);
                    var brush = new ImageBrush(bitmap)
                    {
                        Stretch = Stretch.UniformToFill,
                        AlignmentX = AlignmentX.Center,
                        AlignmentY = AlignmentY.Center,
                        Opacity = 0
                    };
                    target.Background = brush;
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    brush.BeginAnimation(Brush.OpacityProperty, fadeIn);
                }
                catch { }
            });
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            _thumbnailThrottle.Release();
        }
    }

    private async Task<byte[]?> GetThumbnailBytesAsync(OnlineWallpaper wp)
    {
        // 1) Cached thumbnail
        var cachedPath = _service.GetCachedThumbPath(wp.Slug);
        if (!string.IsNullOrEmpty(cachedPath))
            return await Task.Run(() => File.ReadAllBytes(cachedPath));

        // 2) Try thumbnail URL
        if (!string.IsNullOrEmpty(wp.ThumbnailUrl))
        {
            var path = await _service.CacheThumbnailAsync(wp.ThumbnailUrl, wp.Slug, GetToken());
            if (!string.IsNullOrEmpty(path))
                return await Task.Run(() => File.ReadAllBytes(path));
            System.Diagnostics.Debug.WriteLine($"Thumbnail cache failed for {wp.Slug}: {wp.ThumbnailUrl}");
        }

        // 3) Fallback: use GetDetailAsync + CacheThumbnailAsync with a resolution URL
        System.Diagnostics.Debug.WriteLine($"Thumbnail fallback for {wp.Slug}: fetching detail...");
        try
        {
            var detail = await _service.GetDetailAsync(wp, GetToken());
            if (detail?.Resolutions.Count > 0)
            {
                var url = detail.Resolutions.Values.First();
                var path = await _service.CacheThumbnailAsync(url, wp.Slug, GetToken());
                if (!string.IsNullOrEmpty(path))
                {
                    System.Diagnostics.Debug.WriteLine($"Thumbnail fallback OK for {wp.Slug}");
                    return await Task.Run(() => File.ReadAllBytes(path));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Thumbnail fallback failed for {wp.Slug}: {ex.Message}");
        }

        return null;
    }

    private Task LoadLocalThumbnailsAsync(List<WallpaperEntry> entries, List<Border> borders)
    {
        for (int i = 0; i < entries.Count && i < borders.Count; i++)
            _ = LoadLocalTileThumbnailAsync(entries[i], borders[i]);
        return Task.CompletedTask;
    }

    private async Task LoadLocalTileThumbnailAsync(WallpaperEntry entry, Border target)
    {
        await _thumbnailThrottle.WaitAsync();
        try
        {
            byte[] bytes = await Task.Run(() => File.ReadAllBytes(entry.FilePath));
            var bitmap = await Task.Run(() =>
            {
                try
                {
                    var b = new BitmapImage();
                    b.BeginInit();
                    b.StreamSource = new MemoryStream(bytes);
                    b.DecodePixelWidth = 380;
                    b.DecodePixelHeight = 280;
                    b.CacheOption = BitmapCacheOption.OnLoad;
                    b.EndInit();
                    b.Freeze();
                    return b;
                }
                catch { return null; }
            });
            if (bitmap == null) return;

            await Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    target.Child = CreateTitleOverlay(entry.FileName);
                    var brush = new ImageBrush(bitmap)
                    {
                        Stretch = Stretch.UniformToFill,
                        AlignmentX = AlignmentX.Center,
                        AlignmentY = AlignmentY.Center,
                        Opacity = 0
                    };
                    target.Background = brush;
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    brush.BeginAnimation(Brush.OpacityProperty, fadeIn);
                }
                catch { }
            });
        }
        catch { }
        finally
        {
            _thumbnailThrottle.Release();
        }
    }

    // ---- Pagination ----

    private async void PrevBtn_Click(object sender, MouseButtonEventArgs e)
    {
        if (_currentPage <= 1) return;

        if (_isOnline)
        {
            CancelPending();
            _currentPage = 1;
            _service.ResetPagination();
            _rows.Clear();
            _listBoxScrollViewer?.ScrollToTop();
            await (string.IsNullOrEmpty(_currentQuery)
                ? LoadOnlineAsync()
                : SearchOnlineAsync(_currentQuery));
        }
        else
        {
            _currentPage--;
            RenderLocalWallpapers();
        }
    }

    private async void NextBtn_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isOnline)
        {
            CancelPending();
            try
            {
                ShowLoading(true);
                var result = await _service.GetLatestAsync(GetToken());
                if (result.Items.Count > 0)
                {
                    _currentPage++;
                    _filteredWallpapers = result.Items;
                    RenderWallpapers(result.Items);
                    UpdatePagination();
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                ShowLoading(false);
            }
        }
        else
        {
            if (_currentPage >= _totalPages) return;
            _currentPage++;
            RenderLocalWallpapers();
        }
    }

    private void UpdatePagination()
    {
        if (_isOnline)
        {
            PageText.Text = $"Página {_currentPage}";
            SetPagerState(PrevBtn, _currentPage > 1 && _filteredWallpapers.Count > 0);
            SetPagerState(NextBtn, _filteredWallpapers.Count > 0);
        }
        else
        {
            _totalPages = Math.Max(1, (int)Math.Ceiling((double)_filteredLocal.Count / PAGE_SIZE));
            PageText.Text = $"Página {_currentPage} de {_totalPages}";
            SetPagerState(PrevBtn, _currentPage > 1);
            SetPagerState(NextBtn, _currentPage < _totalPages);
        }
    }

    private static void SetPagerState(Border btn, bool enabled)
    {
        btn.IsEnabled = enabled;
        btn.Opacity = enabled ? 1.0 : 0.35;
    }

    // ---- Detail view ----

    private async Task OpenDetailAsync(OnlineWallpaper wp)
    {
        try
        {
            _savedScrollOffset = _listBoxScrollViewer?.VerticalOffset ?? 0;
            _selectedWallpaper = wp;
            _isDetailOpen = true;
            GalleryView.Visibility = Visibility.Collapsed;
            DetailView.Visibility = Visibility.Visible;
            FooterBar.Visibility = Visibility.Collapsed;
            DetailTitle.Text = wp.Title;
            StatusText.Text = "Cargando resolución...";
            PreviewBrush.ImageSource = null;

            var detail = await _service.GetDetailAsync(wp, GetToken());
            if (detail == null || detail.Resolutions.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"No resolutions for: {wp.PostUrl} | slug: {wp.Slug}");
                // Retry once (cache might be stale or page load was partial)
                _service.ClearCaches();
                detail = await _service.GetDetailAsync(wp, GetToken());
            }
            if (detail == null || detail.Resolutions.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"Still no resolutions after retry: {wp.PostUrl}");
                StatusText.Text = "No se encontraron resoluciones disponibles";
                return;
            }

            _selectedWallpaper = detail;
            UpdateAvailableResolutions(detail.Resolutions);

            var previewUrl = GetSelectedResolutionUrl();
            if (string.IsNullOrEmpty(previewUrl)) return;

            StatusText.Text = "Cargando vista previa...";
            var key = GetSelectedResolutionKey();

            var cachedPreview = _service.GetPreviewCachePath(detail.Slug, key);
            if (!string.IsNullOrEmpty(cachedPreview) && File.Exists(cachedPreview))
            {
                var brush = CreateImageBrush(cachedPreview, 800, 600);
                PreviewBrush.ImageSource = brush.ImageSource;
                StatusText.Text = "";
                return;
            }

            var downloaded = await _service.DownloadPreviewAsync(previewUrl, detail.Slug, key, GetToken());
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
                await Dispatcher.BeginInvoke(() => { StatusText.Text = "Error al cargar vista previa"; });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenDetail error: {ex.Message}");
            await Dispatcher.BeginInvoke(() => { StatusText.Text = "Error al cargar el wallpaper"; });
        }
    }

    private void UpdateAvailableResolutions(Dictionary<string, string> resolutions)
    {
        foreach (ComboBoxItem item in ResolutionCombo.Items)
        {
            var key = item.Tag.ToString();
            item.IsEnabled = key != null && resolutions.ContainsKey(key);
        }

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

    private async void ApplyBtn_Click(object? sender, MouseButtonEventArgs? e)
    {
        try
        {
            if (_selectedWallpaper == null) return;
            var url = GetSelectedResolutionUrl();
            if (string.IsNullOrEmpty(url)) { StatusText.Text = "Resolución no disponible"; return; }

            StatusText.Text = "Descargando...";
            var key = GetSelectedResolutionKey();
            var tempPath = _service.GetTempPath(_selectedWallpaper.Slug, key);
            var downloaded = await _service.DownloadImageAsync(url,
                Path.GetDirectoryName(tempPath)!, Path.GetFileName(tempPath), GetToken());

            if (string.IsNullOrEmpty(downloaded) || !File.Exists(downloaded))
            { StatusText.Text = "Error al descargar"; return; }

            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, downloaded,
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            StatusText.Text = "¡Aplicado como wallpaper!";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Apply error: {ex.Message}");
            StatusText.Text = "Error al aplicar";
        }
    }

    private async void SaveBtn_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (_selectedWallpaper == null) return;
            var url = GetSelectedResolutionUrl();
            if (string.IsNullOrEmpty(url)) { StatusText.Text = "Resolución no disponible"; return; }

            if (!Directory.Exists(_wallpaperDir))
            { StatusText.Text = "El directorio de wallpapers no existe"; return; }

            StatusText.Text = "Guardando...";
            var key = GetSelectedResolutionKey();
            var fileName = $"{_selectedWallpaper.Slug}-{key}.jpg";
            var path = await _service.DownloadImageAsync(url, _wallpaperDir, fileName, GetToken());

            if (string.IsNullOrEmpty(path)) { StatusText.Text = "Error al guardar"; return; }

            StatusText.Text = "¡Guardado en la biblioteca local!";
            MainWindow.NotifyRefreshRequested();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Save error: {ex.Message}");
            StatusText.Text = "Error al guardar";
        }
    }

    private void ViewOnline_Click(object sender, MouseButtonEventArgs e)
    {
        if (_selectedWallpaper != null && !string.IsNullOrEmpty(_selectedWallpaper.PostUrl))
            OpenInBrowser(_selectedWallpaper.PostUrl);
    }

    private void BackBtn_Click(object? sender, MouseButtonEventArgs? e)
    {
        _isDetailOpen = false;
        _selectedWallpaper = null;
        DetailView.Visibility = Visibility.Collapsed;
        GalleryView.Visibility = Visibility.Visible;
        FooterBar.Visibility = Visibility.Visible;
        StatusText.Text = "";
        Dispatcher.BeginInvoke(new Action(() =>
            _listBoxScrollViewer?.ScrollToVerticalOffset(_savedScrollOffset)),
            DispatcherPriority.Background);
    }

    private static void OpenInBrowser(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            { FileName = url, UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }

    private static void CopyToClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch { }
    }

    private void ApplyLocalWallpaper(WallpaperEntry entry)
    {
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, entry.FilePath,
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        StatusText.Text = $"Aplicado: {entry.FileName}";
    }

    // ---- Sort ----

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isOnline) return;
        ApplySort();
        _currentPage = 1;
        _rows.Clear();
        _listBoxScrollViewer?.ScrollToTop();
        RenderLocalWallpapers();
    }

    private void ApplySort()
    {
        if (SortCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string sortKey)
            return;

        _filteredLocal = sortKey switch
        {
            "name-asc" => [.. _filteredLocal.OrderBy(w => w.FileName)],
            "name-desc" => [.. _filteredLocal.OrderByDescending(w => w.FileName)],
            "date-asc" => [.. _filteredLocal.OrderBy(w => _fileInfoCache.TryGetValue(w.FilePath, out var info) ? info.LastWrite : DateTime.MinValue)],
            "date-desc" => [.. _filteredLocal.OrderByDescending(w => _fileInfoCache.TryGetValue(w.FilePath, out var info) ? info.LastWrite : DateTime.MinValue)],
            "size-asc" => [.. _filteredLocal.OrderBy(w => _fileInfoCache.TryGetValue(w.FilePath, out var info) ? info.Size : 0L)],
            "size-desc" => [.. _filteredLocal.OrderByDescending(w => _fileInfoCache.TryGetValue(w.FilePath, out var info) ? info.Size : 0L)],
            _ => _filteredLocal
        };
    }

    // ---- Helpers ----

    private static ImageBrush CreateImageBrush(string filePath, int decodeWidth, int decodeHeight)
    {
        try
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
        catch
        {
            return new ImageBrush
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
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

    private void CloseBtn_Click(object sender, MouseButtonEventArgs e) => CloseWithAnimation();

    private void CloseBtn_MouseEnter(object sender, MouseEventArgs e)
    {
        CloseBtn.Background = new SolidColorBrush(Color.FromRgb(0xC7, 0x4B, 0x4B));
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
        CancelPending();
        _cts?.Dispose();
        _searchDebounce?.Stop();
        _thumbnailThrottle.Dispose();
        _service.Dispose();
        base.OnClosed(e);
    }
}
