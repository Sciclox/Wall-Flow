using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using WallFlow.Services;
using System.Linq;

namespace WallFlow.Windows;

public partial class PinnedAppsWindow : Window, System.ComponentModel.INotifyPropertyChanged, IWidget
{
    string IWidget.Name => "PinnedApps";
    public void Open() { Show(); Activate(); }
    public bool IsOpen => IsLoaded && IsVisible;
    public class PinnedApp : System.ComponentModel.INotifyPropertyChanged
    {
        private ImageSource? _icon;
        private bool _isSelected;
        private bool _isPinned;
        private bool _isDragging;

        public bool IsDragging
        {
            get => _isDragging;
            set
            {
                _isDragging = value;
                OnPropertyChanged(nameof(IsDragging));
                OnPropertyChanged(nameof(Opacity));
            }
        }

        public double Opacity => _isDragging ? 0.4 : 1.0;

        public string Name { get; set; } = string.Empty;

        private string _path = string.Empty;
        public string Path
        {
            get => _path;
            set
            {
                _path = value;
                OnPropertyChanged(nameof(Path));
                OnPropertyChanged(nameof(IsActionVisible));
            }
        }
        
        public ImageSource? Icon
        {
            get => _icon;
            set
            {
                _icon = value;
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(IsIconVisible));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                _isPinned = value;
                OnPropertyChanged(nameof(IsPinned));
            }
        }

        public string FriendlyPath
        {
            get
            {
                if (Path.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase))
                {
                    return "Aplicación de Windows Store";
                }
                if (Path == "ADD_APP")
                {
                    return "Anclar una aplicación personalizada al menú";
                }
                try
                {
                    var dir = System.IO.Path.GetDirectoryName(Path);
                    if (string.IsNullOrEmpty(dir)) return Path;
                    return dir;
                }
                catch { return Path; }
            }
        }

        private bool _runAsAdmin;
        public bool RunAsAdmin
        {
            get => _runAsAdmin;
            set
            {
                _runAsAdmin = value;
                OnPropertyChanged(nameof(RunAsAdmin));
                OnPropertyChanged(nameof(ActionText));
            }
        }

        private string? _actionText;
        public string ActionText
        {
            get
            {
                if (!string.IsNullOrEmpty(_actionText)) return _actionText;
                return IsClipboardItem ? "Copiar" : (RunAsAdmin ? "Admin" : "Ejecutar");
            }
            set
            {
                _actionText = value;
                OnPropertyChanged(nameof(ActionText));
            }
        }

        public string IconGlyph { get; set; } = string.Empty;
        public bool IsGlyphVisible => !string.IsNullOrEmpty(IconGlyph);
        public bool IsIconVisible => string.IsNullOrEmpty(IconGlyph) && Icon != null;
        public bool IsActionVisible => Path != "ADD_APP" && !Path.StartsWith("clipboard:", StringComparison.OrdinalIgnoreCase);

        private string? _subtitle;
        public string? Subtitle
        {
            get => _subtitle;
            set
            {
                _subtitle = value;
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(IsSubtitleVisible));
            }
        }

        public bool IsSubtitleVisible => !string.IsNullOrEmpty(Subtitle);
        public DateTime? Timestamp { get; set; }

        public Guid ClipboardId { get; set; } = Guid.Empty;
        public string ClipboardType { get; set; } = string.Empty;
        public bool IsClipboardItem => Path.StartsWith("clipboard:", StringComparison.OrdinalIgnoreCase);
        public bool IsClipboardLink => IsClipboardItem && ClipboardType == "Link";
        public bool IsClipboardFile => IsClipboardItem && ClipboardType == "File";
        public bool IsClipboardText => IsClipboardItem && ClipboardType != "Link" && ClipboardType != "File" && ClipboardType != "Image";
        public bool IsClipboardImage => IsClipboardItem && ClipboardType == "Image";

        public bool IsAiItem => (ClipboardType != null && ClipboardType == "IA") ||
                               (Path != null && (Path == "AI_LOADING" || Path == "AI_ERROR" || Path == "AI_EMPTY")) ||
                               (Subtitle != null && Subtitle.StartsWith("Respuesta IA"));

        public bool IsActionBadgeVisible => !IsAiItem && !IsClipboardItem && !string.IsNullOrEmpty(ActionText);

        public bool IsPinnable => !IsAiItem && !string.IsNullOrEmpty(Path) && Path != "ADD_APP" && !Path.StartsWith("clipboard:", StringComparison.OrdinalIgnoreCase);

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    public ObservableCollection<PinnedApp> PinnedAppsList { get; } = new();
    public ObservableCollection<PinnedApp> FavoritesList { get; } = new();
    private System.Threading.CancellationTokenSource? _fileSearchCts;

    private string _searchMode = "Apps"; // Apps, Files, Clipboard
    public string CurrentSearchMode => _searchMode;

    private Brush _modeAppsBrush = Brushes.Transparent;
    public Brush ModeAppsBrush
    {
        get => _modeAppsBrush;
        set { _modeAppsBrush = value; OnPropertyChanged(nameof(ModeAppsBrush)); }
    }

    private Brush _modeFilesBrush = Brushes.Transparent;
    public Brush ModeFilesBrush
    {
        get => _modeFilesBrush;
        set { _modeFilesBrush = value; OnPropertyChanged(nameof(ModeFilesBrush)); }
    }

    private Brush _modeClipboardBrush = Brushes.Transparent;
    public Brush ModeClipboardBrush
    {
        get => _modeClipboardBrush;
        set { _modeClipboardBrush = value; OnPropertyChanged(nameof(ModeClipboardBrush)); }
    }

    private Brush _modeAppsForegroundBrush = Brushes.White;
    public Brush ModeAppsForegroundBrush
    {
        get => _modeAppsForegroundBrush;
        set { _modeAppsForegroundBrush = value; OnPropertyChanged(nameof(ModeAppsForegroundBrush)); }
    }

    private Brush _modeFilesForegroundBrush = Brushes.White;
    public Brush ModeFilesForegroundBrush
    {
        get => _modeFilesForegroundBrush;
        set { _modeFilesForegroundBrush = value; OnPropertyChanged(nameof(ModeFilesForegroundBrush)); }
    }

    private Brush _modeClipboardForegroundBrush = Brushes.White;
    public Brush ModeClipboardForegroundBrush
    {
        get => _modeClipboardForegroundBrush;
        set { _modeClipboardForegroundBrush = value; OnPropertyChanged(nameof(ModeClipboardForegroundBrush)); }
    }

    private Brush _modeAiBrush = Brushes.Transparent;
    public Brush ModeAiBrush
    {
        get => _modeAiBrush;
        set { _modeAiBrush = value; OnPropertyChanged(nameof(ModeAiBrush)); }
    }

    private Brush _modeAiForegroundBrush = Brushes.White;
    public Brush ModeAiForegroundBrush
    {
        get => _modeAiForegroundBrush;
        set { _modeAiForegroundBrush = value; OnPropertyChanged(nameof(ModeAiForegroundBrush)); }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxIconCacheSize = 200;
    private readonly List<PinnedApp> _allInstalledApps = new();
    private int _selectedIndex = -1;
    private bool _isClosing = false;
    private bool _isShowingDialog = false;
    private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;
    private DateTime _loadedTime = DateTime.MinValue;
    private bool _isChangingMode = false;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        IntPtr ppidl,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [System.Runtime.InteropServices.DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const uint SHGFI_PIDL = 0x000000008;
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW  = 0x00040000;

    private void ForceForeground()
    {
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            IntPtr hWnd = helper.Handle;
            if (hWnd == IntPtr.Zero) return;

            IntPtr foregroundHWnd = GetForegroundWindow();
            if (foregroundHWnd == hWnd) return;

            uint foregroundThreadId = GetWindowThreadProcessId(foregroundHWnd, IntPtr.Zero);
            uint currentThreadId = GetCurrentThreadId();

            if (foregroundThreadId != currentThreadId && foregroundThreadId != 0)
            {
                AttachThreadInput(foregroundThreadId, currentThreadId, true);
                SetForegroundWindow(hWnd);
                AttachThreadInput(foregroundThreadId, currentThreadId, false);
            }
            else
            {
                SetForegroundWindow(hWnd);
            }
        }
        catch { /* ignored */ }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    // Properties for Spotlight custom design
    private Brush _spotlightBgBrush = Brushes.Transparent;
    public Brush SpotlightBgBrush
    {
        get => _spotlightBgBrush;
        set { _spotlightBgBrush = value; OnPropertyChanged(nameof(SpotlightBgBrush)); }
    }

    private Brush _spotlightBorderBrush = Brushes.Transparent;
    public Brush SpotlightBorderBrush
    {
        get => _spotlightBorderBrush;
        set { _spotlightBorderBrush = value; OnPropertyChanged(nameof(SpotlightBorderBrush)); }
    }

    private Thickness _spotlightBorderThickness = new Thickness(0);
    public Thickness SpotlightBorderThickness
    {
        get => _spotlightBorderThickness;
        set { _spotlightBorderThickness = value; OnPropertyChanged(nameof(SpotlightBorderThickness)); }
    }

    private Brush _spotlightTextBrush = Brushes.White;
    public Brush SpotlightTextBrush
    {
        get => _spotlightTextBrush;
        set { _spotlightTextBrush = value; OnPropertyChanged(nameof(SpotlightTextBrush)); }
    }

    private Brush _spotlightPlaceholderBrush = Brushes.Gray;
    public Brush SpotlightPlaceholderBrush
    {
        get => _spotlightPlaceholderBrush;
        set { _spotlightPlaceholderBrush = value; OnPropertyChanged(nameof(SpotlightPlaceholderBrush)); }
    }

    private Brush _spotlightIconBrush = Brushes.Gray;
    public Brush SpotlightIconBrush
    {
        get => _spotlightIconBrush;
        set { _spotlightIconBrush = value; OnPropertyChanged(nameof(SpotlightIconBrush)); }
    }

    private double _spotlightWidth = 600.0;
    public double SpotlightWidth
    {
        get => _spotlightWidth;
        set { _spotlightWidth = value; OnPropertyChanged(nameof(SpotlightWidth)); }
    }

    private CornerRadius _spotlightCornerRadius = new CornerRadius(27);
    public CornerRadius SpotlightCornerRadius
    {
        get => _spotlightCornerRadius;
        set { _spotlightCornerRadius = value; OnPropertyChanged(nameof(SpotlightCornerRadius)); }
    }

    private Visibility _spotlightFavoritesVisibility = Visibility.Visible;
    public Visibility SpotlightFavoritesVisibility
    {
        get => _spotlightFavoritesVisibility;
        set { _spotlightFavoritesVisibility = value; OnPropertyChanged(nameof(SpotlightFavoritesVisibility)); }
    }

    private Brush _spotlightSelectedBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
    public Brush SpotlightSelectedBackgroundBrush
    {
        get => _spotlightSelectedBackgroundBrush;
        set { _spotlightSelectedBackgroundBrush = value; OnPropertyChanged(nameof(SpotlightSelectedBackgroundBrush)); }
    }

    private Brush _spotlightSelectedBorderBrush = Brushes.Transparent;
    public Brush SpotlightSelectedBorderBrush
    {
        get => _spotlightSelectedBorderBrush;
        set { _spotlightSelectedBorderBrush = value; OnPropertyChanged(nameof(SpotlightSelectedBorderBrush)); }
    }

    private Brush _spotlightHoverBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
    public Brush SpotlightHoverBackgroundBrush
    {
        get => _spotlightHoverBackgroundBrush;
        set { _spotlightHoverBackgroundBrush = value; OnPropertyChanged(nameof(SpotlightHoverBackgroundBrush)); }
    }

    private Brush _spotlightHoverBorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
    public Brush SpotlightHoverBorderBrush
    {
        get => _spotlightHoverBorderBrush;
        set { _spotlightHoverBorderBrush = value; OnPropertyChanged(nameof(SpotlightHoverBorderBrush)); }
    }

    private Brush _spotlightItemBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
    public Brush SpotlightItemBackgroundBrush
    {
        get => _spotlightItemBackgroundBrush;
        set { _spotlightItemBackgroundBrush = value; OnPropertyChanged(nameof(SpotlightItemBackgroundBrush)); }
    }

    private Brush _spotlightItemForegroundBrush = new SolidColorBrush(Color.FromArgb(0xA5, 0xFF, 0xFF, 0xFF));
    public Brush SpotlightItemForegroundBrush
    {
        get => _spotlightItemForegroundBrush;
        set { _spotlightItemForegroundBrush = value; OnPropertyChanged(nameof(SpotlightItemForegroundBrush)); }
    }

    private Brush _spotlightSecondaryTextBrush = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));
    public Brush SpotlightSecondaryTextBrush
    {
        get => _spotlightSecondaryTextBrush;
        set { _spotlightSecondaryTextBrush = value; OnPropertyChanged(nameof(SpotlightSecondaryTextBrush)); }
    }

    private Brush _spotlightSeparatorBrush = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
    public Brush SpotlightSeparatorBrush
    {
        get => _spotlightSeparatorBrush;
        set { _spotlightSeparatorBrush = value; OnPropertyChanged(nameof(SpotlightSeparatorBrush)); }
    }

    private Brush _spotlightSearchFocusBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0x22, 0x22, 0x26));
    public Brush SpotlightSearchFocusBrush
    {
        get => _spotlightSearchFocusBrush;
        set { _spotlightSearchFocusBrush = value; OnPropertyChanged(nameof(SpotlightSearchFocusBrush)); }
    }

    public void ApplySettings()
    {
        var s = SettingsService.Instance;

        SpotlightWidth = s.SpotlightWidth > 0 ? s.SpotlightWidth : 600.0;
        SpotlightCornerRadius = new CornerRadius(s.SpotlightCornerRadius >= 0 ? s.SpotlightCornerRadius : 27.0);
        SpotlightFavoritesVisibility = s.SpotlightShowFavorites ? Visibility.Visible : Visibility.Collapsed;

        if (s.SpotlightThemeMode == "Dock")
        {
            SpotlightBgBrush = Application.Current.Resources["DockBackgroundBrush"] as Brush 
                               ?? TryFindResource("DockBackgroundBrush") as Brush 
                               ?? new SolidColorBrush(Color.FromArgb(0xB4, 0x0B, 0x0B, 0x0B));

            SpotlightBorderBrush = Application.Current.Resources["DockBorderBrush"] as Brush 
                                   ?? TryFindResource("DockBorderBrush") as Brush 
                                   ?? Brushes.Transparent;

            SpotlightBorderThickness = new Thickness(s.SpotlightBorderThickness >= 0 ? s.SpotlightBorderThickness : 0.0);

            SpotlightSelectedBorderBrush = Application.Current.Resources["AccentBrush"] as Brush 
                                           ?? TryFindResource("AccentBrush") as Brush 
                                           ?? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF));

            SpotlightSelectedBackgroundBrush = Application.Current.Resources["AccentActiveBrush"] as Brush 
                                               ?? new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));

            SpotlightTextBrush = Brushes.White;
            SpotlightPlaceholderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
            SpotlightIconBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));

            ApplySubBrushes(isLight: false);
            SpotlightSearchFocusBrush = SpotlightBgBrush;
        }
        else if (s.SpotlightThemeMode == "Personalizado")
        {
            if (!string.IsNullOrEmpty(s.SpotlightBackgroundColor) && TryParseColor(s.SpotlightBackgroundColor, out var bgColor))
                SpotlightBgBrush = new SolidColorBrush(bgColor);
            else
                SpotlightBgBrush = TryFindResource("DockBackgroundBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(0xB4, 0x0B, 0x0B, 0x0B));

            if (!string.IsNullOrEmpty(s.SpotlightBorderColor) && TryParseColor(s.SpotlightBorderColor, out var borderColor))
                SpotlightBorderBrush = new SolidColorBrush(borderColor);
            else
                SpotlightBorderBrush = Brushes.Transparent;
            SpotlightBorderThickness = new Thickness(s.SpotlightBorderThickness >= 0 ? s.SpotlightBorderThickness : 0.0);

            if (!string.IsNullOrEmpty(s.SpotlightTextColor) && TryParseColor(s.SpotlightTextColor, out var textColor))
                SpotlightTextBrush = new SolidColorBrush(textColor);
            else
                SpotlightTextBrush = Brushes.White;

            if (!string.IsNullOrEmpty(s.SpotlightPlaceholderColor) && TryParseColor(s.SpotlightPlaceholderColor, out var placeholderColor))
                SpotlightPlaceholderBrush = new SolidColorBrush(placeholderColor);
            else
                SpotlightPlaceholderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));

            if (!string.IsNullOrEmpty(s.SpotlightIconColor) && TryParseColor(s.SpotlightIconColor, out var iconColor))
                SpotlightIconBrush = new SolidColorBrush(iconColor);
            else
                SpotlightIconBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));

            if (!string.IsNullOrEmpty(s.SpotlightAccentColor) && TryParseColor(s.SpotlightAccentColor, out var accentColor))
            {
                SpotlightSelectedBorderBrush = new SolidColorBrush(accentColor);
                SpotlightSelectedBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x25, accentColor.R, accentColor.G, accentColor.B));
            }
            else
            {
                SpotlightSelectedBorderBrush = TryFindResource("AccentBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF));
                SpotlightSelectedBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
            }

            ApplyPersonalizadoBrush(s.SpotlightHoverBackgroundColor, v => SpotlightHoverBackgroundBrush = v, () => SpotlightHoverBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)));
            ApplyPersonalizadoBrush(s.SpotlightHoverBorderColor, v => SpotlightHoverBorderBrush = v, () => SpotlightHoverBorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)));
            ApplyPersonalizadoBrush(s.SpotlightItemBackgroundColor, v => SpotlightItemBackgroundBrush = v, () => SpotlightItemBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)));
            ApplyPersonalizadoBrush(s.SpotlightItemForegroundColor, v => SpotlightItemForegroundBrush = v, () => SpotlightItemForegroundBrush = new SolidColorBrush(Color.FromArgb(0xA5, 0xFF, 0xFF, 0xFF)));
            ApplyPersonalizadoBrush(s.SpotlightSecondaryTextColor, v => SpotlightSecondaryTextBrush = v, () => SpotlightSecondaryTextBrush = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)));
            ApplyPersonalizadoBrush(s.SpotlightSeparatorColor, v => SpotlightSeparatorBrush = v, () => SpotlightSeparatorBrush = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)));
            ApplyPersonalizadoBrush(s.SpotlightSearchFocusColor, v => SpotlightSearchFocusBrush = v, () => SpotlightSearchFocusBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0x22, 0x22, 0x26)));
        }
        else
        {
            ApplyThemePreset(s.SpotlightThemeMode);
        }

        // Note: each property setter already calls OnPropertyChanged, no need to re-notify
    }

    private static void ApplyPersonalizadoBrush(string? hexValue, Action<Brush> setter, Action fallback)
    {
        if (!string.IsNullOrEmpty(hexValue) && TryParseColor(hexValue, out var color))
        {
            setter(new SolidColorBrush(color));
        }
        else
        {
            fallback();
        }
    }

    private void ApplySubBrushes(bool isLight)
    {
        if (isLight)
        {
            SpotlightHoverBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x15, 0x00, 0x00, 0x00));
            SpotlightHoverBorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x00, 0x00));
            SpotlightItemBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x12, 0x00, 0x00, 0x00));
            SpotlightItemForegroundBrush = new SolidColorBrush(Color.FromArgb(0xA5, 0x00, 0x00, 0x00));
            SpotlightSecondaryTextBrush = new SolidColorBrush(Color.FromArgb(0xA0, 0x00, 0x00, 0x00));
            SpotlightSeparatorBrush = new SolidColorBrush(Color.FromArgb(0x15, 0x00, 0x00, 0x00));
            SpotlightSearchFocusBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xE6, 0xE6, 0xE6));
        }
        else
        {
            SpotlightHoverBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
            SpotlightHoverBorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            SpotlightItemBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
            SpotlightItemForegroundBrush = new SolidColorBrush(Color.FromArgb(0xA5, 0xFF, 0xFF, 0xFF));
            SpotlightSecondaryTextBrush = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));
            SpotlightSeparatorBrush = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
            SpotlightSearchFocusBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0x22, 0x22, 0x26));
        }
    }

    private record ThemePreset(
        Color Background,
        Color Border,
        double BorderThickness,
        Color Text,
        Color Placeholder,
        Color Icon,
        Color Accent,
        double HoverBackgroundA,
        double HoverBorderA,
        double ItemBackgroundA,
        double ItemForegroundA,
        double SecondaryTextA,
        double SeparatorA,
        Color SearchFocus,
        bool IsLight = false
    );

    private static readonly Dictionary<string, ThemePreset> ThemePresets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Oscuro"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x0B, 0x0B, 0x0B),
            Border: Colors.Transparent,
            BorderThickness: 0,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0x00, 0x7A, 0xFF),
            HoverBackgroundA: 0x15, HoverBorderA: 0x20,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x22, 0x22, 0x26)
        ),
        ["Claro"] = new ThemePreset(
            Background: Color.FromArgb(0xE6, 0xF0, 0xF0, 0xF0),
            Border: Color.FromArgb(0x40, 0x00, 0x00, 0x00),
            BorderThickness: 1,
            Text: Color.FromRgb(0x1A, 0x1A, 0x1A),
            Placeholder: Color.FromArgb(0x50, 0x00, 0x00, 0x00),
            Icon: Color.FromArgb(0x60, 0x00, 0x00, 0x00),
            Accent: Color.FromRgb(0x00, 0x7A, 0xFF),
            HoverBackgroundA: 0x15, HoverBorderA: 0x20,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0xE6, 0xE6, 0xE6),
            IsLight: true
        ),
        ["Midnight"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x0A, 0x0E, 0x1A),
            Border: Color.FromArgb(0x30, 0x4A, 0x6C, 0xF7),
            BorderThickness: 1,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0x4A, 0x6C, 0xF7),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x0F, 0x13, 0x20)
        ),
        ["Ocean"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x06, 0x1A, 0x2E),
            Border: Color.FromArgb(0x30, 0x00, 0xB4, 0xD8),
            BorderThickness: 1,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0x00, 0xB4, 0xD8),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x08, 0x1E, 0x32)
        ),
        ["Forest"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x06, 0x18, 0x0C),
            Border: Colors.Transparent,
            BorderThickness: 0,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0x22, 0xC5, 0x5E),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x0A, 0x1E, 0x10)
        ),
        ["Sunset"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x1A, 0x0E, 0x06),
            Border: Color.FromArgb(0x30, 0xF9, 0x73, 0x16),
            BorderThickness: 1,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0xF9, 0x73, 0x16),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x20, 0x12, 0x0A)
        ),
        ["Crimson"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x1A, 0x06, 0x0A),
            Border: Colors.Transparent,
            BorderThickness: 0,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0xEF, 0x44, 0x44),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x20, 0x0A, 0x0E)
        ),
        ["Teal"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x06, 0x1A, 0x1A),
            Border: Colors.Transparent,
            BorderThickness: 0,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0x14, 0xB8, 0xA6),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x0A, 0x20, 0x20)
        ),
        ["Warm"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x1A, 0x14, 0x06),
            Border: Color.FromArgb(0x30, 0xF5, 0x9E, 0x0B),
            BorderThickness: 1,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0xF5, 0x9E, 0x0B),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x20, 0x1A, 0x0A)
        ),
        ["Rose"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x1A, 0x06, 0x12),
            Border: Colors.Transparent,
            BorderThickness: 0,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0xEC, 0x48, 0x99),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x20, 0x0A, 0x18)
        ),
        ["Slate"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x1E, 0x29, 0x3B),
            Border: Color.FromArgb(0x30, 0x94, 0xA3, 0xB8),
            BorderThickness: 1,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0x94, 0xA3, 0xB8),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x28, 0x35, 0x45)
        ),
        ["Violet"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x12, 0x06, 0x1E),
            Border: Colors.Transparent,
            BorderThickness: 0,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0x8B, 0x5C, 0xF6),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x18, 0x0A, 0x24)
        )
    };

    private void ApplyThemePreset(string mode)
    {
        if (!ThemePresets.TryGetValue(mode, out var p)) return;

        SpotlightBgBrush = new SolidColorBrush(p.Background);
        SpotlightBorderBrush = new SolidColorBrush(p.Border);
        SpotlightBorderThickness = new Thickness(p.BorderThickness);
        SpotlightTextBrush = new SolidColorBrush(p.Text);
        SpotlightPlaceholderBrush = new SolidColorBrush(p.Placeholder);
        SpotlightIconBrush = new SolidColorBrush(p.Icon);
        SpotlightSelectedBorderBrush = new SolidColorBrush(p.Accent);
        SpotlightSelectedBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x25, p.Accent.R, p.Accent.G, p.Accent.B));
        SpotlightHoverBackgroundBrush = new SolidColorBrush(Color.FromArgb((byte)p.HoverBackgroundA, p.Accent.R, p.Accent.G, p.Accent.B));
        SpotlightHoverBorderBrush = new SolidColorBrush(Color.FromArgb((byte)p.HoverBorderA, p.Accent.R, p.Accent.G, p.Accent.B));
        byte baseC = p.IsLight ? (byte)0x00 : (byte)0xFF;
        SpotlightItemBackgroundBrush = new SolidColorBrush(Color.FromArgb((byte)p.ItemBackgroundA, baseC, baseC, baseC));
        SpotlightItemForegroundBrush = new SolidColorBrush(Color.FromArgb((byte)p.ItemForegroundA, baseC, baseC, baseC));
        SpotlightSecondaryTextBrush = new SolidColorBrush(Color.FromArgb((byte)p.SecondaryTextA, baseC, baseC, baseC));
        SpotlightSeparatorBrush = new SolidColorBrush(Color.FromArgb((byte)p.SeparatorA, baseC, baseC, baseC));
        SpotlightSearchFocusBrush = new SolidColorBrush(p.Background);
        UpdateModeButtonsVisuals();
    }

    private static bool TryParseColor(string hex, out System.Windows.Media.Color color)
    {
        color = System.Windows.Media.Colors.Transparent;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        hex = hex.Trim();
        if (!hex.StartsWith("#")) hex = "#" + hex;
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted != null)
            {
                color = (System.Windows.Media.Color)converted;
                return true;
            }
        }
        catch (FormatException) { /* ignored */ }
        catch (NotSupportedException) { /* ignored */ }
        return false;
    }

    private static readonly System.Net.Http.HttpClient _sharedHttpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    public PinnedAppsWindow()
    {
        InitializeComponent();
        ApplySettings();
        DataContext = this;
        this.Activated += Window_Activated;

        _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _searchDebounceTimer.Tick += (s, args) =>
        {
            _searchDebounceTimer.Stop();
            if (_searchMode == "AI") return;
            PerformSearch(SearchBox.Text.Trim());
        };

        UpdateModeButtonsVisuals();
        ClipboardService.Instance.HistoryChanged += OnClipboardHistoryChanged;
        this.Closed += (s, ev) =>
        {
            ClipboardService.Instance.HistoryChanged -= OnClipboardHistoryChanged;
        };
    }

    private void OnClipboardHistoryChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnClipboardHistoryChanged));
            return;
        }
        if (_searchMode == "Clipboard")
        {
            SearchClipboard(SearchBox != null ? SearchBox.Text.Trim() : "");
        }
        else if (_searchMode == "AI" && (SearchBox == null || string.IsNullOrWhiteSpace(SearchBox.Text)))
        {
            LoadAiHistory();
        }
    }

    public void SetSearchMode(string mode)
    {
        if (_searchMode == mode) return;

        _isChangingMode = true;
        try
        {
            _searchMode = mode;
            _searchDebounceTimer?.Stop();
            
            if (SearchBox != null)
            {
                SearchBox.Text = string.Empty;
            }

            UpdateModeButtonsVisuals();
            
            if (_searchMode == "Clipboard")
            {
                SearchClipboard(string.Empty);
            }
            else if (_searchMode == "AI")
            {
                LoadAiHistory();
            }
            else
            {
                PinnedAppsList.Clear();
                _selectedIndex = -1;
                if (_searchMode == "Apps")
                {
                    PopulateDefaultList();
                }
                else
                {
                    UpdateWindowSize();
                }
            }
            
            if (SearchBox != null)
            {
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
            }
        }
        finally
        {
            _isChangingMode = false;
        }
    }

    private void CycleSearchMode()
    {
        if (_searchMode == "Apps") SetSearchMode("Files");
        else if (_searchMode == "Files") SetSearchMode("Clipboard");
        else if (_searchMode == "Clipboard") SetSearchMode("AI");
        else SetSearchMode("Apps");
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button != null && button.Tag is string mode)
        {
            SetSearchMode(mode);
        }
    }

    private void UpdateModeButtonsVisuals()
    {
        var activeBg = SpotlightSelectedBorderBrush ?? Brushes.Teal;
        var inactiveBg = SpotlightBgBrush ?? Brushes.Transparent;
        
        var activeFg = Brushes.White;
        var inactiveFg = SpotlightTextBrush ?? Brushes.White;

        ModeAppsBrush = _searchMode == "Apps" ? activeBg : inactiveBg;
        ModeAppsForegroundBrush = _searchMode == "Apps" ? activeFg : inactiveFg;

        ModeFilesBrush = _searchMode == "Files" ? activeBg : inactiveBg;
        ModeFilesForegroundBrush = _searchMode == "Files" ? activeFg : inactiveFg;

        ModeClipboardBrush = _searchMode == "Clipboard" ? activeBg : inactiveBg;
        ModeClipboardForegroundBrush = _searchMode == "Clipboard" ? activeFg : inactiveFg;

        ModeAiBrush = _searchMode == "AI" ? activeBg : inactiveBg;
        ModeAiForegroundBrush = _searchMode == "AI" ? activeFg : inactiveFg;

        if (PlaceholderText != null)
        {
            if (_searchMode == "Apps") PlaceholderText.Text = "Buscar aplicación...";
            else if (_searchMode == "Files") PlaceholderText.Text = "Buscar archivos...";
            else if (_searchMode == "Clipboard") PlaceholderText.Text = "Buscar en el portapapeles...";
            else if (_searchMode == "AI") PlaceholderText.Text = "Pregunta algo a Gemini AI...";
        }
    }

    private void Window_Activated(object? sender, EventArgs ev)
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Exclude from Alt+Tab and Windows Task View before the window is visible
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
        }
        catch { /* ignored */ }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _loadedTime = DateTime.Now;

        // Force window to foreground to prevent activation beep and missing cursor hand
        ForceForeground();

        // Calculate initial window size to prevent cut-off search box
        UpdateWindowSize();

        // Center window on active monitor
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            var screenIndex = Math.Clamp(SettingsService.Instance.DockMonitorIndex, 0, screens.Length - 1);
            var screen = screens[screenIndex];
            var dpi = VisualTreeHelper.GetDpi(this);

            // Use the Top coordinate already calculated and set by UpdateWindowSize
            double finalTop = Top;
            if (double.IsNaN(finalTop) || double.IsInfinity(finalTop))
            {
                double actualScreenHeight = screen.WorkingArea.Height / dpi.DpiScaleY;
                double actualScreenTop = screen.WorkingArea.Y / dpi.DpiScaleY;
                finalTop = actualScreenTop + (actualScreenHeight * 0.15);
            }
            Top = finalTop;
            Opacity = 0;

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)) 
            { 
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } 
            };

            BeginAnimation(OpacityProperty, fadeIn);
        }
        catch
        {
            Left = 20;
            Top = 64;
            Opacity = 1;
        }

        // Notify DockWindow that the flyout is open
        if (ApplicationHost.Instance != null)
        {
            ApplicationHost.Instance.IsPinnedFlyoutOpen = true;
        }

        // Load all applications sequentially to trigger entrance animations
        await LoadAppsStaggeredAsync();

        // Ensure focus is still set after loading
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private PinnedApp CreatePinnedAppFromPath(string path)
    {
        string targetPath = path;
        bool isUwp = path.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase);
        if (isUwp)
        {
            var parts = path.Substring(4).Split('|');
            targetPath = parts.Length > 1 ? parts[1] : "";
        }

        var resolved = ResolvePath(targetPath);
        var name = TranslateAppName(Path.GetFileNameWithoutExtension(resolved));

        return new PinnedApp
        {
            Name = name,
            Path = path,
            Icon = null
        };
    }

    private static string TranslateAppName(string name)
    {
        if (string.Equals(name, "explorer", StringComparison.OrdinalIgnoreCase)) return "Explorador de Archivos";
        if (string.Equals(name, "chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (string.Equals(name, "notepad", StringComparison.OrdinalIgnoreCase)) return "Bloc de Notas";
        if (string.Equals(name, "cmd", StringComparison.OrdinalIgnoreCase)) return "Símbolo del Sistema";
        if (string.Equals(name, "taskmgr", StringComparison.OrdinalIgnoreCase)) return "Administrador de Tareas";
        return name;
    }

    private async Task LoadAppsStaggeredAsync()
    {
        PinnedAppsList.Clear();
        FavoritesList.Clear();
        _allInstalledApps.Clear();
        _selectedIndex = -1;

        var paths = SettingsService.Instance.PinnedAppPaths;
        var appsToLoad = new List<PinnedApp>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Load Pinned Apps instantly on the UI thread to display them without delay
        if (paths != null)
        {
            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path) || path == "ADD_APP") continue;
                try
                {
                    var app = CreatePinnedAppFromPath(path);
                    FavoritesList.Add(app);
                    _allInstalledApps.Add(app);
                    seenPaths.Add(path);
                }
                catch { /* ignored */ }
            }
        }

        // Add the "Anclar Aplicación" button at the end
        FavoritesList.Add(new PinnedApp
        {
            Name = "Anclar Aplicación",
            Path = "ADD_APP",
            IconGlyph = "\uE6E6"
        });

        // Trigger loading of icons for these initial favorites in the background immediately
        StartLoadingIcons();

        // Also trigger default list layout update
        PopulateDefaultList();

        // Load Start Menu & Desktop apps
        await Task.Run(() =>
        {
            var userPrograms = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

            void ScanDirectory(string dir)
            {
                if (!Directory.Exists(dir)) return;
                try
                {
                    var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase));

                    foreach (var file in files)
                    {
                        if (_isClosing) return;
                        try
                        {
                            var name = Path.GetFileNameWithoutExtension(file);
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            if (name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Unins", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("ReadMe", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Help", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Website", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Documentation", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (seenPaths.Add(file))
                            {
                                appsToLoad.Add(new PinnedApp
                                {
                                    Name = name,
                                    Path = file,
                                    Icon = null
                                });
                            }
                        }
                        catch { /* ignored */ }
                    }
                }
                catch { /* ignored */ }
            }

            void ScanUwpApps()
            {
                try
                {
                    Type? shellType = Type.GetTypeFromCLSID(new Guid("13709620-C279-11CE-A49E-00AA00C1485B"));
                    if (shellType == null) return;

                    object? shell = Activator.CreateInstance(shellType);
                    if (shell == null) return;

                    object? appsFolder = shellType.InvokeMember("NameSpace",
                        System.Reflection.BindingFlags.InvokeMethod,
                        null, shell, new object[] { "shell:AppsFolder" });

                    if (appsFolder == null) return;

                    Type folderType = appsFolder.GetType();
                    object? items = folderType.InvokeMember("Items",
                        System.Reflection.BindingFlags.InvokeMethod,
                        null, appsFolder, null);

                    if (items is System.Collections.IEnumerable enumerable)
                    {
                        foreach (object item in enumerable)
                        {
                            if (_isClosing) return;
                            try
                            {
                                Type itemType = item.GetType();
                                string? name = itemType.InvokeMember("Name",
                                    System.Reflection.BindingFlags.GetProperty,
                                    null, item, null) as string;

                                string? path = itemType.InvokeMember("Path",
                                    System.Reflection.BindingFlags.GetProperty,
                                    null, item, null) as string;

                                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;

                                if (name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("Unins", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                string uwpPath = path.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase) 
                                    ? path 
                                    : "uwp:" + path;

                                if (seenPaths.Add(uwpPath))
                                {
                                    appsToLoad.Add(new PinnedApp
                                    {
                                        Name = name,
                                        Path = uwpPath,
                                        Icon = null
                                    });
                                }
                            }
                            catch { /* ignored */ }
                        }
                    }
                }
                catch { /* ignored */ }
            }

            void ScanStartApps()
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-StartApps | ConvertTo-Json -Compress\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8
                    };

                    using var proc = System.Diagnostics.Process.Start(psi);
                    if (proc != null)
                    {
                        string json = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit();

                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(json);
                            var root = doc.RootElement;

                            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var elem in root.EnumerateArray())
                                {
                                    if (_isClosing) return;
                                    if (!elem.TryGetProperty("Name", out var nameProp) || !elem.TryGetProperty("AppID", out var appIdProp))
                                        continue;

                                    string name = nameProp.GetString() ?? "";
                                    string appId = appIdProp.GetString() ?? "";

                                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appId))
                                        continue;

                                    if (name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                                        name.Contains("Unins", StringComparison.OrdinalIgnoreCase) ||
                                        name.Contains("ReadMe", StringComparison.OrdinalIgnoreCase) ||
                                        name.Contains("Help", StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    string itemPath = appId;
                                    if (!appId.Contains("\\") || appId.Contains("!") || appId.StartsWith("Microsoft."))
                                    {
                                        itemPath = "uwp:" + appId;
                                    }

                                    if (seenPaths.Add(itemPath))
                                    {
                                        appsToLoad.Add(new PinnedApp
                                        {
                                            Name = name,
                                            Path = itemPath,
                                            Icon = null
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
                catch { /* ignored */ }
            }

            ScanStartApps();
            ScanDirectory(userPrograms);
            ScanDirectory(commonPrograms);
            ScanDirectory(userDesktop);
            ScanDirectory(commonDesktop);
            ScanUwpApps();

            appsToLoad.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        });

        if (_isClosing) return;

        _allInstalledApps.AddRange(appsToLoad);

        // Update Pinned States and Populate Favorites
        UpdatePinsState();
        PopulateFavoritesList();

        // Populate default list when query is empty (which will collapse the results panel)
        PopulateDefaultList();

        // Start loading icons in background
        StartLoadingIcons();
    }

    private void PopulateDefaultList()
    {
        PinnedAppsList.Clear();
        _selectedIndex = -1;

        if (ResultsScrollViewer != null)
        {
            ResultsScrollViewer.Visibility = Visibility.Collapsed;
        }

        UpdateWindowSize();
    }

    private void StartLoadingIcons()
    {
        // Snapshot collections on the UI thread — no lock needed since
        // StartLoadingIcons is always called from the UI thread
        var favorites = new List<PinnedApp>(FavoritesList);
        var apps = new List<PinnedApp>(_allInstalledApps);

        Task.Run(async () =>
        {
            foreach (var app in favorites)
            {
                if (_isClosing) return;
                if (app.Path == "ADD_APP" || app.Icon != null || app.IsGlyphVisible) continue;

                var icon = LoadIconForApp(app.Path);
                if (icon != null)
                {
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        app.Icon = icon;
                    }));
                }
            }

            foreach (var app in apps)
            {
                if (_isClosing) return;
                if (app.Icon != null || app.IsGlyphVisible) continue;

                var icon = LoadIconForApp(app.Path);
                if (icon != null)
                {
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        app.Icon = icon;
                    }));
                }

                await Task.Delay(5);
            }
        });
    }

    public static void PreloadPinnedIcons()
    {
        var paths = SettingsService.Instance.PinnedAppPaths;
        if (paths == null) return;

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

    private static ImageSource? LoadIconForApp(string appPath)
    {
        try
        {
            return appPath.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase)
                ? GetUwpIcon(appPath.Substring(4).Split('|')[0])
                : GetAppIcon(ResolvePath(appPath));
        }
        catch { /* ignored */ return null; }
    }

    private static string ResolvePath(string path)
    {
        if (File.Exists(path)) return path;

        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

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

        return path;
    }

    private static ImageSource? GetAppIcon(string path)
    {
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
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(targetPath))
                {
                    if (icon != null)
                    {
                        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
                        var imageSource = dispatcher.Invoke(() =>
                        {
                            var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                icon.Handle,
                                Int32Rect.Empty,
                                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                            var cloned = CloneBitmapSource(bs);
                            if (_iconCache.Count < MaxIconCacheSize) _iconCache[path] = cloned;
                            return cloned;
                        });
                        return imageSource;
                    }
                }
            }
        }
        catch { /* ignored */ }
        return null;
    }

    private static ImageSource? GetUwpIcon(string aumid)
    {
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
                    uint shfiSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(shfi);
                    
                    // SHGFI_PIDL | SHGFI_ICON | SHGFI_LARGEICON
                    IntPtr res = SHGetFileInfo(pidl, 0, ref shfi, shfiSize, SHGFI_PIDL | SHGFI_ICON | SHGFI_LARGEICON);
                    if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                    {
                        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
                        var bitmapSource = dispatcher.Invoke(() =>
                        {
                            var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                shfi.hIcon,
                                Int32Rect.Empty,
                                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                            var cloned = CloneBitmapSource(bs);
                            DestroyIcon(shfi.hIcon);
                            if (_iconCache.Count < MaxIconCacheSize) _iconCache[aumid] = cloned;
                            return cloned;
                        });
                        return bitmapSource;
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

    private static System.Windows.Media.Imaging.BitmapSource CloneBitmapSource(System.Windows.Media.Imaging.BitmapSource source)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        int bitsPerPixel = source.Format.BitsPerPixel;
        int stride = ((width * bitsPerPixel + 31) / 32) * 4;
        byte[] bits = new byte[height * stride];
        source.CopyPixels(bits, stride, 0);
        
        var clone = System.Windows.Media.Imaging.BitmapSource.Create(
            width, height, 
            source.DpiX, source.DpiY, 
            source.Format, 
            source.Palette, 
            bits, stride);
        clone.Freeze();
        return clone;
    }

    private void AppButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isFavDragging)
        {
            _isFavDragging = false;
            e.Handled = true;
            return;
        }

        if (sender is System.Windows.Controls.Button btn && btn.Tag is string path)
        {
            if (path == "ADD_APP")
            {
                AddNewApp();
                return;
            }

            bool runAsAdmin = false;
            foreach (var app in PinnedAppsList)
            {
                if (string.Equals(app.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    runAsAdmin = app.RunAsAdmin;
                    break;
                }
            }

            LaunchApp(path, runAsAdmin);
        }
    }

    private bool ActivateExistingWindow(string exeName)
    {
        var procName = Path.GetFileNameWithoutExtension(exeName);

        if (string.Equals(procName, "explorer", StringComparison.OrdinalIgnoreCase))
        {
            var hwnd = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "CabinetWClass", null);
            if (hwnd != IntPtr.Zero)
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
                return true;
            }
            return false;
        }

        try
        {
            var processes = Process.GetProcessesByName(procName);
            foreach (var proc in processes)
            {
                var hwnd = proc.MainWindowHandle;
                if (hwnd != IntPtr.Zero)
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
                    return true;
                }
            }
        }
        catch { /* ignored */ }
        return false;
    }

    private void AddNewApp()
    {
        _isShowingDialog = true;
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Seleccionar aplicación para anclar",
                Filter = "Aplicaciones (*.exe, *.lnk)|*.exe;*.lnk|Todos los archivos (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };

            if (dialog.ShowDialog() == true)
            {
                var selectedPath = dialog.FileName;
                if (!string.IsNullOrEmpty(selectedPath) && File.Exists(selectedPath))
                {
                    if (SettingsService.Instance.PinnedAppPaths == null)
                    {
                        SettingsService.Instance.PinnedAppPaths = new List<string>();
                    }

                    if (!SettingsService.Instance.PinnedAppPaths.Contains(selectedPath))
                    {
                        SettingsService.Instance.PinnedAppPaths.Add(selectedPath);
                        SettingsService.Save();
                    }

                    // Reload layout sequentially
                    _ = LoadAppsStaggeredAsync();
                }
            }
        }
        finally
        {
            _isShowingDialog = false;
            // Reactivate this window after the modal dialog is closed
            try { this.Activate(); } catch { /* ignored */ }
        }
    }

    private void UnpinMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string path)
        {
            if (SettingsService.Instance.PinnedAppPaths != null && SettingsService.Instance.PinnedAppPaths.Contains(path))
            {
                SettingsService.Instance.PinnedAppPaths.Remove(path);
                SettingsService.Save();
            }

            // Reset launch count
            if (SettingsService.Instance.AppLaunchCounts != null && SettingsService.Instance.AppLaunchCounts.ContainsKey(path))
            {
                SettingsService.Instance.AppLaunchCounts.Remove(path);
                SettingsService.Save();
            }

            // Reload layout
            _ = LoadAppsStaggeredAsync();
        }
    }

    private void DeleteClipboardItemMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is Guid id)
        {
            ClipboardService.Instance.DeleteItem(id);
        }
    }

    private void SearchGoogleMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string path)
        {
            var content = path.Substring("clipboard:".Length);
            var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(content);
            try
            {
                using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
            SafeClose();
        }
    }

    private void OpenLinkMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string path)
        {
            var content = path.Substring("clipboard:".Length);
            if (!content.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !content.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                content = "https://" + content;
            }
            try
            {
                using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = content,
                    UseShellExecute = true
                });
            }
            catch { }
            SafeClose();
        }
    }

    private void OpenFilesMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string path)
        {
            var content = path.Substring("clipboard:".Length);
            var files = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var file in files)
            {
                if (System.IO.File.Exists(file) || System.IO.Directory.Exists(file))
                {
                    try
                    {
                        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = file,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }
            SafeClose();
        }
    }

    private void OpenFilesLocationMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string path)
        {
            var content = path.Substring("clipboard:".Length);
            var files = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (files.Length > 0)
            {
                var file = files[0];
                if (System.IO.File.Exists(file))
                {
                    var dir = System.IO.Path.GetDirectoryName(file);
                    if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                    {
                        try
                        {
                            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = $"/select,\"{file}\"",
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }
                }
                else if (System.IO.Directory.Exists(file))
                {
                    try
                    {
                        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = file,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }
            SafeClose();
        }
    }

    private void PinMenu_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string path)
        {
            TogglePinApp(path);
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_isShowingDialog) return;

        // Ignore deactivation within the first 800ms of loading to prevent focus-stealing
        // issues (e.g., during PC startup / when other background apps launch).
        if ((DateTime.Now - _loadedTime).TotalMilliseconds < 800)
        {
            ForceForeground();
            return;
        }

        SafeClose();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { SafeClose(); e.Handled = true; return; }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Key == Key.D1 || e.Key == Key.NumPad1)
            {
                SetSearchMode("Apps");
                e.Handled = true;
                return;
            }
            if (e.Key == Key.D2 || e.Key == Key.NumPad2)
            {
                SetSearchMode("Files");
                e.Handled = true;
                return;
            }
            if (e.Key == Key.D3 || e.Key == Key.NumPad3)
            {
                SetSearchMode("Clipboard");
                e.Handled = true;
                return;
            }
            if (e.Key == Key.D4 || e.Key == Key.NumPad4)
            {
                SetSearchMode("AI");
                e.Handled = true;
                return;
            }
        }

        if (SearchBox != null && !SearchBox.IsFocused)
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }
    }



    private void SafeClose()
    {
        if (_isClosing) return;
        _isClosing = true;

        if (ApplicationHost.Instance != null)
        {
            ApplicationHost.Instance.IsPinnedFlyoutOpen = false;
            ApplicationHost.Instance.RecordWidgetClose("PinnedApps");
        }

        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) 
        { 
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } 
        };

        fadeOut.Completed += (snd, args) =>
        {
            try { Close(); }
            catch { _isClosing = false; }
        };

        BeginAnimation(OpacityProperty, fadeOut);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (ApplicationHost.Instance != null)
        {
            ApplicationHost.Instance.IsPinnedFlyoutOpen = false;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
    }

    private void UpdateWindowSize()
    {
        double screenWidth = 1920;
        double screenHeight = 1080;
        double screenLeft = 0;
        double screenTop = 0;
        double actualScreenHeight = 0;
        double actualScreenTop = 0;

        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            var screenIndex = Math.Clamp(SettingsService.Instance.DockMonitorIndex, 0, screens.Length - 1);
            var screen = screens[screenIndex];
            var dpi = VisualTreeHelper.GetDpi(this);

            screenWidth = screen.WorkingArea.Width / dpi.DpiScaleX;
            screenHeight = screen.WorkingArea.Height / dpi.DpiScaleY;
            screenLeft = screen.WorkingArea.X / dpi.DpiScaleX;
            screenTop = screen.WorkingArea.Y / dpi.DpiScaleY;
            actualScreenHeight = screenHeight;
            actualScreenTop = screenTop;
        }
        catch { /* ignored */ }

        Width = screenWidth;
        Left = screenLeft;

        string query = SearchBox != null ? SearchBox.Text.Trim() : "";
        if (string.IsNullOrEmpty(query) && _searchMode != "Clipboard" && _searchMode != "AI")
        {
            SpotlightFavoritesVisibility = SettingsService.Instance.SpotlightShowFavorites ? Visibility.Visible : Visibility.Collapsed;
            if (ResultsScrollViewer != null) ResultsScrollViewer.Visibility = Visibility.Collapsed;
            if (ResultsCardBorder != null) ResultsCardBorder.Visibility = Visibility.Collapsed;

            // Calculate WrapPanel rows for Favorites
            int favCount = 0;
            if (SettingsService.Instance.SpotlightShowFavorites)
            {
                if (FavoritesList != null && FavoritesList.Count > 0)
                {
                    favCount = FavoritesList.Count;
                }
                else if (SettingsService.Instance.PinnedAppPaths != null)
                {
                    int pathsCount = 0;
                    foreach (var p in SettingsService.Instance.PinnedAppPaths)
                    {
                        if (p != "ADD_APP" && !string.IsNullOrEmpty(p)) pathsCount++;
                    }
                    favCount = pathsCount + 1; // +1 for the ADD_APP button
                }
            }

            // Each button: Width 54 + Margin 6 on left and right = 66px
            // Available width: screenWidth - 40
            double availableWidth = Math.Max(200, screenWidth - 40);
            int settingAppsPerRow = SettingsService.Instance.SpotlightAppsPerRow;
            double maxItemsPerRow = settingAppsPerRow > 0 
                ? settingAppsPerRow 
                : Math.Max(1, Math.Floor(availableWidth / 66));

            if (FavoritesControl != null)
            {
                FavoritesControl.MaxWidth = maxItemsPerRow * 66;
            }

            double rowCount = favCount > 0 
                ? Math.Max(1, Math.Ceiling(favCount / maxItemsPerRow))
                : 0;

            double searchBoxH = SearchBox != null && SearchBox.ActualHeight > 0 
                ? Math.Max(54, SearchBox.ActualHeight) 
                : 54;

            // Height formula:
            // 10 (Grid top) + searchBoxH + 10 (Searchbox bottom margin)
            // + (rowCount * (54 + 8)) [each row is 54px high + 8px vertical margin]
            // + 10 (Grid bottom margin) + 24 (shadow bleed room)
            Height = 10 + searchBoxH + 10 + (rowCount * 62) + 10 + 24;
        }
        else
        {
            SpotlightFavoritesVisibility = Visibility.Collapsed;
            if (ResultsCardBorder != null) ResultsCardBorder.Visibility = Visibility.Visible;

            int itemsCount = PinnedAppsList != null ? PinnedAppsList.Count : 0;
            bool hasResults = itemsCount > 0;

            // Show/hide the results list
            if (ResultsScrollViewer != null)
                ResultsScrollViewer.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;

            // In Clipboard mode, we do NOT search the web
            bool showWebSearch = !string.IsNullOrEmpty(query) && (_searchMode == "Apps" || _searchMode == "Files");

            if (WebSearchSeparator != null)
                WebSearchSeparator.Visibility = (hasResults && showWebSearch) ? Visibility.Visible : Visibility.Collapsed;

            if (WebSearchButton != null)
                WebSearchButton.Visibility = showWebSearch ? Visibility.Visible : Visibility.Collapsed;

            // Update web search label — detect if query is a URL
            if (WebSearchText != null && showWebSearch)
            {
                WebSearchText.Text = IsUrl(query)
                    ? $"Abrir \"{query}\""
                    : $"Buscar \"{query}\" en la web";
            }

            // Manage empty clipboard placeholder
            bool showEmptyPlaceholder = (_searchMode == "Clipboard" && !hasResults);
            if (NoResultsPlaceholder != null)
            {
                if (showEmptyPlaceholder)
                {
                    NoResultsPlaceholder.Visibility = Visibility.Visible;
                    if (NoResultsTitle != null && NoResultsSubtitle != null)
                    {
                        if (string.IsNullOrEmpty(query))
                        {
                            NoResultsTitle.Text = "El portapapeles está vacío";
                            NoResultsSubtitle.Text = "Copia texto o imágenes para ver tu historial aquí.";
                        }
                        else
                        {
                            NoResultsTitle.Text = "Sin coincidencias";
                            NoResultsSubtitle.Text = "No se encontraron elementos en el portapapeles.";
                        }
                    }
                }
                else
                {
                    NoResultsPlaceholder.Visibility = Visibility.Collapsed;
                }
            }

            double searchBoxH = SearchBox != null && SearchBox.ActualHeight > 0 
                ? Math.Max(54, SearchBox.ActualHeight) 
                : 54;

            double cardHeight = 0;
            if (ResultsCardBorder != null && ResultsCardBorder.Visibility == Visibility.Visible)
            {
                UpdateLayout();
                ResultsCardBorder.UpdateLayout();
                ResultsCardBorder.Measure(new Size(SpotlightWidth, double.PositiveInfinity));
                cardHeight = ResultsCardBorder.DesiredSize.Height;
                double maxAllowedCardH = actualScreenHeight > 0 ? actualScreenHeight * 0.70 : 540;
                cardHeight = Math.Min(cardHeight, maxAllowedCardH);
            }

            Height = 10 + searchBoxH + 10 + cardHeight + 24;
        }

        double targetHeight = Height;
        if (double.IsNaN(targetHeight) || double.IsInfinity(targetHeight) || targetHeight <= 0)
        {
            targetHeight = 360;
        }

        double calculatedTop = actualScreenTop + (actualScreenHeight * 0.15);
        if (!double.IsNaN(calculatedTop) && !double.IsInfinity(calculatedTop))
        {
            Top = calculatedTop;
        }
    }

    private void WebSearchButton_Click(object sender, RoutedEventArgs e)
    {
        OpenWebSearch();
    }

    private void OpenWebSearch()
    {
        string query = SearchBox?.Text.Trim() ?? "";
        if (string.IsNullOrEmpty(query)) return;
        try
        {
            string url;
            if (IsUrl(query))
            {
                // Navigate directly — add https:// if no scheme present
                url = (query.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                       query.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    ? query
                    : "https://" + query;
            }
            else
            {
                url = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";
            }

            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = url,
                UseShellExecute = true
            });
            SafeClose();
        }
        catch { /* ignored */ }
    }

    /// <summary>
    /// Returns true if the query looks like a navigable URL.
    /// Matches: http(s)://, www., bare domains (youtube.com), localhost:3000, etc.
    /// </summary>
    private static bool IsUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains(' ')) return false;

        // Explicit schemes or www prefix
        if (text.StartsWith("http://",  StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("www.",     StringComparison.OrdinalIgnoreCase))
            return true;

        // Bare domain pattern: something.tld  /  something.tld/path  /  localhost:port
        return System.Text.RegularExpressions.Regex.IsMatch(text,
            @"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z]{2,})(:\d+)?(/\S*)?$");
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string path)
        {
            TogglePinApp(path);
        }
    }

    private void TogglePinApp(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "ADD_APP") return;

        if (SettingsService.Instance.PinnedAppPaths == null)
        {
            SettingsService.Instance.PinnedAppPaths = new List<string>();
        }

        var paths = SettingsService.Instance.PinnedAppPaths;
        if (paths.Contains(path))
        {
            paths.Remove(path);
        }
        else
        {
            paths.Add(path);
        }

        SettingsService.Save();

        // Refresh states and collections
        UpdatePinsState();
        PopulateFavoritesList();

        if (string.IsNullOrEmpty(SearchBox.Text.Trim()))
        {
            PopulateDefaultList();
        }
        else
        {
            UpdateWindowSize();
        }
    }

    private void UpdatePinsState()
    {
        var paths = SettingsService.Instance.PinnedAppPaths ?? new List<string>();
        var pathSet = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);

        foreach (var app in PinnedAppsList)
        {
            app.IsPinned = pathSet.Contains(app.Path);
        }

        foreach (var app in _allInstalledApps)
        {
            app.IsPinned = pathSet.Contains(app.Path);
        }
    }

    private void PopulateFavoritesList()
    {
        FavoritesList.Clear();
        var paths = SettingsService.Instance.PinnedAppPaths;
        if (paths != null)
        {
            foreach (var favPath in paths)
            {
                if (string.IsNullOrEmpty(favPath) || favPath == "ADD_APP") continue;
                bool found = false;
                foreach (var app in _allInstalledApps)
                {
                    if (string.Equals(app.Path, favPath, StringComparison.OrdinalIgnoreCase))
                    {
                        FavoritesList.Add(app);
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    try
                    {
                        var app = CreatePinnedAppFromPath(favPath);
                        FavoritesList.Add(app);
                    }
                    catch { /* ignored */ }
                }
            }
        }

        FavoritesList.Add(new PinnedApp
        {
            Name = "Anclar Aplicación",
            Path = "ADD_APP",
            IconGlyph = "\uE6E6" // Lucide layers-plus
        });
    }

    // ===== Explorer Drag & Drop to Pin =====

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            bool addedAny = false;
            
            if (SettingsService.Instance.PinnedAppPaths == null)
            {
                SettingsService.Instance.PinnedAppPaths = new List<string>();
            }

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLower();
                if (ext == ".exe" || ext == ".lnk" || Directory.Exists(file) || file.StartsWith("shell:AppsFolder\\"))
                {
                    if (!SettingsService.Instance.PinnedAppPaths.Contains(file))
                    {
                        SettingsService.Instance.PinnedAppPaths.Add(file);
                        addedAny = true;
                    }
                }
            }

            if (addedAny)
            {
                SettingsService.Save();
                _ = LoadAppsStaggeredAsync();
            }
            e.Handled = true;
        }
    }

    // ===== Favorites Drag & Drop Reordering =====

    private Point _favDragStartPoint;
    private PinnedApp? _draggedFavApp;
    private bool _isFavDragging;

    private void FavButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isFavDragging = false;
        if (sender is FrameworkElement element && element.DataContext is PinnedApp app)
        {
            if (app.Path == "ADD_APP") return;
            _favDragStartPoint = e.GetPosition(this);
            _draggedFavApp = app;
        }
    }

    private void FavButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedFavApp == null) return;

        Point currentPos = e.GetPosition(this);
        Vector diff = _favDragStartPoint - currentPos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _isFavDragging = true;
            _draggedFavApp.IsDragging = true;

            if (sender is FrameworkElement element)
            {
                DataObject data = new DataObject("PinnedAppDrag", _draggedFavApp);
                DragDrop.DoDragDrop(element, data, DragDropEffects.Move);

                if (_draggedFavApp != null)
                {
                    _draggedFavApp.IsDragging = false;
                    _draggedFavApp = null;
                }
                e.Handled = true;
            }
        }
    }

    private void FavButton_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("PinnedAppDrag"))
        {
            var draggedApp = e.Data.GetData("PinnedAppDrag") as PinnedApp;
            if (sender is FrameworkElement element && element.DataContext is PinnedApp targetApp)
            {
                if (targetApp.Path != "ADD_APP" && draggedApp != null && draggedApp != targetApp)
                {
                    int oldIndex = FavoritesList.IndexOf(draggedApp);
                    int newIndex = FavoritesList.IndexOf(targetApp);

                    if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
                    {
                        FavoritesList.Move(oldIndex, newIndex);
                    }
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }
            }
        }
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void FavButton_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("PinnedAppDrag"))
        {
            var draggedApp = e.Data.GetData("PinnedAppDrag") as PinnedApp;
            if (draggedApp != null)
            {
                draggedApp.IsDragging = false;
            }

            var newPaths = new List<string>();
            foreach (var fav in FavoritesList)
            {
                if (!string.IsNullOrEmpty(fav.Path) && fav.Path != "ADD_APP")
                {
                    newPaths.Add(fav.Path);
                }
            }

            SettingsService.Instance.PinnedAppPaths = newPaths;
            SettingsService.Save();
            e.Handled = true;
        }
    }

    // ===== Context Menu click actions =====

    private void RunAsAdminMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string path)
        {
            try
            {
                var fullPath = ResolvePath(path);
                using var _ = Process.Start(new ProcessStartInfo
                {
                    FileName = fullPath,
                    Verb = "runas",
                    UseShellExecute = true
                });
                SafeClose();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo ejecutar como administrador: {ex.Message}", "Error Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OpenLocationMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string path)
        {
            try
            {
                var fullPath = ResolvePath(path);
                if (File.Exists(fullPath))
                {
                    using var _ = Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                }
                else
                {
                    MessageBox.Show("No se encontró el archivo.", "Error Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir la ubicación del archivo: {ex.Message}", "Error Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isChangingMode) return;

        PlaceholderText.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        string query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            _searchDebounceTimer?.Stop();
            foreach (var app in PinnedAppsList) app.IsSelected = false;
            
            if (_searchMode == "Apps")
            {
                PopulateDefaultList();
            }
            else if (_searchMode == "Files")
            {
                PinnedAppsList.Clear();
                _selectedIndex = -1;
                UpdateWindowSize();
            }
            else if (_searchMode == "Clipboard")
            {
                SearchClipboard(string.Empty);
            }
            else if (_searchMode == "AI")
            {
                LoadAiHistory();
            }
        }
        else
        {
            // Restart debounce timer — search will fire after 150ms of no typing
            _searchDebounceTimer?.Stop();
            _searchDebounceTimer?.Start();
        }
    }

    private void PerformSearch(string query)
    {
        if (_searchMode == "Apps")
        {
            PerformAppsSearch(query);
        }
        else if (_searchMode == "Files")
        {
            _ = SearchFilesAsync(query);
        }
        else if (_searchMode == "Clipboard")
        {
            SearchClipboard(query);
        }
        else if (_searchMode == "AI")
        {
            _ = PerformAiSearchAsync(query);
        }
    }

    private async Task PerformAiSearchAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            LoadAiHistory();
            return;
        }

        string apiKey = SettingsService.Instance.GeminiApiKey ?? "";
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            PinnedAppsList.Clear();
            PinnedAppsList.Add(new PinnedApp
            {
                Name = "Configura tu API Key gratuita de Gemini AI para consultar la IA.",
                Path = "https://aistudio.google.com/app/apikey",
                ActionText = "Haz clic aquí para abrir Google AI Studio y obtener tu clave gratis",
                IconGlyph = "\uE4A6"
            });
            _selectedIndex = 0;
            PinnedAppsList[0].IsSelected = true;
            UpdateWindowSize();
            return;
        }

        PinnedAppsList.Clear();
        PinnedAppsList.Add(new PinnedApp
        {
            Name = "Consultando a Gemini AI...",
            Path = "AI_LOADING",
            ActionText = "Procesando tu pregunta en tiempo real...",
            IconGlyph = "\uE1BB"
        });
        _selectedIndex = 0;
        PinnedAppsList[0].IsSelected = true;
        UpdateWindowSize();

        try
        {
            string primaryModel = string.IsNullOrWhiteSpace(SettingsService.Instance.GeminiModel) 
                ? "gemini-2.5-flash" 
                : SettingsService.Instance.GeminiModel;

            var modelsToTry = new List<string> { primaryModel, "gemini-2.5-flash", "gemini-2.0-flash", "gemini-1.5-flash" };
            var uniqueModels = modelsToTry.Distinct().ToArray();

            var requestBody = new
            {
                systemInstruction = new
                {
                    parts = new[]
                    {
                        new { text = "Actúa como un asistente directo. Proporciona la respuesta final en español de forma directa, concisa y rápida, sin introducciones ni rodeos." }
                    }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    maxOutputTokens = 1024
                }
            };
            string jsonContent = System.Text.Json.JsonSerializer.Serialize(requestBody);

            System.Net.Http.HttpResponseMessage? response = null;
            string responseJson = "";
            string lastErrDetail = "";

            foreach (var model in uniqueModels)
            {
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";
                using var content = new System.Net.Http.StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                response = await _sharedHttpClient.PostAsync(url, content);
                responseJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    SettingsService.Instance.GeminiModel = model;
                    SettingsService.Save();
                    break;
                }
                else
                {
                    try
                    {
                        using var docErr = System.Text.Json.JsonDocument.Parse(responseJson);
                        if (docErr.RootElement.TryGetProperty("error", out var errObj) &&
                            errObj.TryGetProperty("message", out var msgProp))
                        {
                            lastErrDetail = msgProp.GetString() ?? "";
                        }
                    }
                    catch { }
                }
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                string statusInfo = response != null ? response.StatusCode.ToString() : "Error";
                string actionInfo = string.IsNullOrWhiteSpace(lastErrDetail) 
                    ? "Verifica tu API Key o conexión a internet" 
                    : lastErrDetail;

                PinnedAppsList.Clear();
                PinnedAppsList.Add(new PinnedApp
                {
                    Name = $"Error al consultar Gemini ({statusInfo})",
                    Path = "https://aistudio.google.com/app/apikey",
                    ActionText = actionInfo,
                    IconGlyph = "\uE1BB"
                });
                _selectedIndex = 0;
                PinnedAppsList[0].IsSelected = true;
                UpdateWindowSize();
                return;
            }

            // --- Extract ONLY candidates[0].content.parts[*].text (skipping 'thought': true parts) ---
            using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
            string aiAnswer = "";

            if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == System.Text.Json.JsonValueKind.Array &&
                candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                    contentObj.TryGetProperty("parts", out var partsArr) &&
                    partsArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var part in partsArr.EnumerateArray())
                    {
                        // Skip internal chain-of-thought blocks ("thought": true)
                        if (part.TryGetProperty("thought", out var thoughtElem) && thoughtElem.GetBoolean())
                        {
                            continue;
                        }

                        if (part.TryGetProperty("text", out var textElem))
                        {
                            sb.Append(textElem.GetString() ?? "");
                        }
                    }
                    aiAnswer = sb.ToString();
                }
            }

            if (string.IsNullOrWhiteSpace(aiAnswer))
            {
                aiAnswer = "No se obtuvo respuesta de la IA.";
            }

            PinnedAppsList.Clear();

            string cleanAnswer = aiAnswer.Trim();

            // 1. Strip label prefixes like "Response:", "Greeting:", "Output:", "Answer:"
            int colonIdx = cleanAnswer.IndexOf(':');
            if (colonIdx > 0 && colonIdx <= 12)
            {
                string prefix = cleanAnswer.Substring(0, colonIdx).Trim();
                if (prefix.Equals("Response", StringComparison.OrdinalIgnoreCase) ||
                    prefix.Equals("Greeting", StringComparison.OrdinalIgnoreCase) ||
                    prefix.Equals("Output", StringComparison.OrdinalIgnoreCase) ||
                    prefix.Equals("Answer", StringComparison.OrdinalIgnoreCase) ||
                    prefix.Equals("Result", StringComparison.OrdinalIgnoreCase))
                {
                    cleanAnswer = cleanAnswer.Substring(colonIdx + 1).Trim();
                }
            }

            // 2. Handle repeated quoted patterns e.g. "Hola"Hola or "Text"
            if (cleanAnswer.StartsWith("\""))
            {
                int lastQuoteIdx = cleanAnswer.LastIndexOf('"');
                if (lastQuoteIdx > 0)
                {
                    string afterQuote = cleanAnswer.Substring(lastQuoteIdx + 1).Trim();
                    if (!string.IsNullOrWhiteSpace(afterQuote))
                    {
                        cleanAnswer = afterQuote;
                    }
                    else
                    {
                        cleanAnswer = cleanAnswer.Substring(1, lastQuoteIdx - 1).Trim();
                    }
                }
            }

            // 3. Strip any surrounding quotes or backticks
            cleanAnswer = cleanAnswer.Trim('"', '\'', ' ');

            if (!string.IsNullOrWhiteSpace(cleanAnswer))
            {
                try { WallFlow.Services.ClipboardService.Instance.AddAiItem(cleanAnswer); } catch { }

                PinnedAppsList.Add(new PinnedApp
                {
                    Name = cleanAnswer,
                    Path = "clipboard:" + cleanAnswer,
                    ActionText = "Copiar este texto al portapapeles",
                    IconGlyph = "\uE1BB"
                });
            }

            if (PinnedAppsList.Count > 0)
            {
                _selectedIndex = 0;
                PinnedAppsList[0].IsSelected = true;
            }
            UpdateWindowSize();
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateLayout();
                UpdateWindowSize();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            PinnedAppsList.Clear();
            PinnedAppsList.Add(new PinnedApp
            {
                Name = "Error al conectar con Gemini AI: " + ex.Message,
                Path = "AI_ERROR",
                ActionText = "Verifica tu conexión a internet",
                IconGlyph = "\uE1BB"
            });
            _selectedIndex = 0;
            PinnedAppsList[0].IsSelected = true;
            UpdateWindowSize();
        }
    }

    private void PerformAppsSearch(string query)
    {
        // Clear previous selection
        foreach (var app in PinnedAppsList) app.IsSelected = false;

        PinnedAppsList.Clear();
        _selectedIndex = -1;

        if (string.IsNullOrEmpty(query)) { UpdateWindowSize(); return; }

        var counts = SettingsService.Instance.AppLaunchCounts;
        var scored = new List<(PinnedApp app, int score)>();

        foreach (var app in _allInstalledApps)
        {
            int score = 0;
            if (app.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                score = 100;
            else if (app.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                score = 50;
            else if (app.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                score = 40;
            else if (MatchInitials(app.Name, query))
                score = 30;
            else
                continue;

            // Boost by launch frequency
            if (counts != null && counts.TryGetValue(app.Path, out int count))
                score += Math.Min(count * 2, 20);

            // Boost pinned apps
            if (app.IsPinned) score += 10;

            scored.Add((app, score));
        }

        scored.Sort((a, b) => string.Compare(a.app.Name, b.app.Name, StringComparison.CurrentCultureIgnoreCase));

        var seenResultNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (app, _) in scored)
        {
            if (seenResultNames.Add(app.Name))
            {
                PinnedAppsList.Add(app);
            }
        }

        // Select first result
        if (PinnedAppsList.Count > 0)
        {
            _selectedIndex = 0;
            PinnedAppsList[0].IsSelected = true;
        }

        UpdateWindowSize();
    }

    private static bool MatchInitials(string name, string query)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || query.Length < 2) return false;

        var initials = new System.Text.StringBuilder();
        foreach (var w in words)
            if (w.Length > 0) initials.Append(char.ToLowerInvariant(w[0]));

        return initials.ToString().Contains(query.ToLowerInvariant());
    }

    private void MapFileExtension(string ext, PinnedApp item)
    {
        switch (ext)
        {
            // Images
            case ".png":
            case ".jpg":
            case ".jpeg":
            case ".gif":
            case ".bmp":
            case ".svg":
            case ".webp":
            case ".ico":
            case ".tiff":
                item.IconGlyph = "\uE5C4"; // Lucide images
                item.ActionText = "Imagen";
                break;

            // Audio
            case ".mp3":
            case ".wav":
            case ".wma":
            case ".ogg":
            case ".flac":
            case ".aac":
            case ".m4a":
                item.IconGlyph = "\uE31A"; // Lucide file-headphone
                item.ActionText = "Audio";
                break;

            // Video
            case ".mp4":
            case ".mkv":
            case ".avi":
            case ".mov":
            case ".wmv":
            case ".flv":
            case ".webm":
            case ".mpeg":
                item.IconGlyph = "\uE321"; // Lucide file-video
                item.ActionText = "Video";
                break;

            // Documents / Office / PDF
            case ".pdf":
                item.IconGlyph = "\uE329"; // Lucide file-type
                item.ActionText = "Documento PDF";
                break;
            case ".docx":
            case ".doc":
                item.IconGlyph = "\uE329";
                item.ActionText = "Documento de Word";
                break;
            case ".xlsx":
            case ".xls":
                item.IconGlyph = "\uE329";
                item.ActionText = "Hoja de cálculo";
                break;
            case ".pptx":
            case ".ppt":
                item.IconGlyph = "\uE329";
                item.ActionText = "Presentación";
                break;
            case ".txt":
            case ".rtf":
            case ".md":
                item.IconGlyph = "\uE329";
                item.ActionText = "Archivo de texto";
                break;

            // Archives
            case ".zip":
            case ".rar":
            case ".7z":
            case ".tar":
            case ".gz":
                item.IconGlyph = "\uE129"; // Lucide package
                item.ActionText = "Archivo comprimido";
                break;

            // Default
            default:
                item.IconGlyph = "\uE329"; // Lucide file-type
                item.ActionText = "Archivo";
                break;
        }
    }

    private async Task SearchFilesAsync(string query)
    {
        // Cancel previous running search to prevent lag
        _fileSearchCts?.Cancel();
        _fileSearchCts = new System.Threading.CancellationTokenSource();
        var token = _fileSearchCts.Token;

        if (string.IsNullOrWhiteSpace(query))
        {
            Dispatcher.Invoke(() => {
                PinnedAppsList.Clear();
                _selectedIndex = -1;
                UpdateWindowSize();
            });
            return;
        }

        List<string> searchDirs = new()
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        var matchedFiles = new List<PinnedApp>();

        await Task.Run(() =>
        {
            try
            {
                foreach (var dir in searchDirs)
                {
                    if (token.IsCancellationRequested) return;
                    if (!Directory.Exists(dir)) continue;

                    // Support deep search (up to 3 levels) only if query is long enough (3+ chars)
                    // Short queries (1-2 chars) only scan the root of folders to keep it instant
                    var options = new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = query.Length >= 3,
                        MaxRecursionDepth = query.Length < 3 ? 1 : 3
                    };

                    foreach (var path in Directory.EnumerateFileSystemEntries(dir, "*", options))
                    {
                        if (token.IsCancellationRequested) return;

                        bool isDir = Directory.Exists(path);
                        if (isDir)
                        {
                            string folderName = Path.GetFileName(path);
                            if (string.IsNullOrEmpty(folderName)) continue;

                            if (folderName.Contains(query, StringComparison.OrdinalIgnoreCase))
                            {
                                var appItem = new PinnedApp
                                {
                                    Name = folderName,
                                    Subtitle = path,
                                    Path = path,
                                    RunAsAdmin = false,
                                    IconGlyph = "\uE247", // Lucide folder-open glyph
                                    ActionText = "Carpeta"
                                };
                                matchedFiles.Add(appItem);
                            }
                        }
                        else
                        {
                            string fileName = Path.GetFileName(path);
                            string ext = Path.GetExtension(path).ToLower();
                            
                            // Exclude apps/executables/installers/shortcuts
                            if (ext == ".exe" || ext == ".lnk" || ext == ".com" || ext == ".bat" || 
                                ext == ".cmd" || ext == ".msi" || ext == ".msp" || ext == ".scr" || 
                                ext == ".pif" || ext == ".appref-ms")
                            {
                                continue;
                            }

                            if (fileName.Contains(query, StringComparison.OrdinalIgnoreCase))
                            {
                                var appItem = new PinnedApp
                                {
                                    Name = fileName,
                                    Subtitle = path,
                                    Path = path,
                                    RunAsAdmin = false
                                };
                                
                                // Map extension to custom glyph and category badge
                                MapFileExtension(ext, appItem);

                                matchedFiles.Add(appItem);
                            }
                        }

                        if (matchedFiles.Count >= 20) break;
                    }

                    if (matchedFiles.Count >= 20) break;
                }
            }
            catch { }
        }, token);

        Dispatcher.Invoke(() =>
        {
            if (token.IsCancellationRequested) return;
            if (SearchBox == null || SearchBox.Text.Trim() != query) return;

            PinnedAppsList.Clear();
            foreach (var item in matchedFiles)
            {
                PinnedAppsList.Add(item);
            }
            
            if (PinnedAppsList.Count > 0)
            {
                _selectedIndex = 0;
                PinnedAppsList[0].IsSelected = true;
            }
            else
            {
                _selectedIndex = -1;
            }
            
            UpdateWindowSize();
        });
    }

    private static string GetFormattedCopyTime(DateTime timestamp)
    {
        var now = DateTime.Now;
        var diff = now - timestamp;

        string timeStr = timestamp.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);

        if (diff.TotalSeconds < 60 && diff.TotalSeconds >= 0)
        {
            return "Se copió justo ahora";
        }
        else if (timestamp.Date == now.Date)
        {
            return $"Se copió a las {timeStr}";
        }
        else if (timestamp.Date == now.Date.AddDays(-1))
        {
            return $"Se copió ayer a las {timeStr}";
        }
        else
        {
            return $"Se copió el {timestamp:dd/MM} a las {timeStr}";
        }
    }

    private void SearchClipboard(string query)
    {
        var history = ClipboardService.Instance.History;
        var matchedItems = new List<PinnedApp>();

        foreach (var item in history)
        {
            if (string.IsNullOrEmpty(query) || item.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                string displayName = item.Content;
                if (item.Type == "Image" && !string.IsNullOrEmpty(item.ImagePath))
                {
                    displayName = "[Imagen] " + Path.GetFileName(item.ImagePath);
                }
                else if (item.Type == "File")
                {
                    displayName = "[Archivos] " + item.Content.Replace("\r", " ").Replace("\n", ", ");
                }

                string typeLabel = item.Type switch
                {
                    "IA" => "Respuesta IA",
                    "Image" => "Imagen",
                    "Link" => "URL",
                    "Code" => "Código",
                    "File" => "Archivo",
                    _ => (item.Tags != null && item.Tags.Contains("#IA")) ? "Respuesta IA" : ((item.Colors != null && item.Colors.Count > 0 && item.Content.StartsWith("#")) ? "Color" : "Texto")
                };

                string timeLabel = GetFormattedCopyTime(item.Timestamp);

                var appItem = new PinnedApp
                {
                    Name = displayName.Length > 80 ? displayName.Substring(0, 77).Replace("\r", " ").Replace("\n", " ") + "..." : displayName.Replace("\r", " ").Replace("\n", " "),
                    Subtitle = $"{typeLabel} • {timeLabel}",
                    Path = "clipboard:" + item.Content,
                    RunAsAdmin = false,
                    ClipboardId = item.Id,
                    ClipboardType = item.Type,
                    Timestamp = item.Timestamp
                };

                if (item.Type == "IA" || (item.Tags != null && item.Tags.Contains("#IA")))
                {
                    appItem.IconGlyph = "\uE1BB"; // Lucide Bot
                }
                else if (item.Type == "Image" && !string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
                {
                    try
                    {
                        var bmi = new BitmapImage();
                        bmi.BeginInit();
                        bmi.CacheOption = BitmapCacheOption.OnLoad;
                        bmi.UriSource = new Uri(item.ImagePath, UriKind.Absolute);
                        bmi.EndInit();
                        bmi.Freeze();
                        appItem.Icon = bmi;
                    }
                    catch { appItem.IconGlyph = "\uE5C4"; } // Lucide images
                }
                else if (item.Type == "Code")
                {
                    appItem.IconGlyph = "\uE0C3"; // Lucide file-code
                }
                else if (item.Type == "Link")
                {
                    appItem.IconGlyph = "\uE102"; // Lucide link
                }
                else if (item.Type == "File")
                {
                    string pathStr = item.Content.Trim();
                    if (Directory.Exists(pathStr))
                    {
                        appItem.IconGlyph = "\uE247"; // Lucide folder-open
                    }
                    else
                    {
                        string ext = Path.GetExtension(pathStr).ToLowerInvariant();
                        switch (ext)
                        {
                            case ".png": case ".jpg": case ".jpeg": case ".gif": case ".bmp": case ".svg": case ".webp":
                                appItem.IconGlyph = "\uE5C4"; // Lucide images
                                break;
                            case ".mp3": case ".wav": case ".wma": case ".ogg": case ".flac": case ".aac": case ".m4a":
                                appItem.IconGlyph = "\uE31A"; // Lucide file-headphone
                                break;
                            case ".mp4": case ".mkv": case ".avi": case ".mov": case ".wmv": case ".flv": case ".webm":
                                appItem.IconGlyph = "\uE321"; // Lucide file-video
                                break;
                            case ".zip": case ".rar": case ".7z": case ".tar": case ".gz":
                                appItem.IconGlyph = "\uE129"; // Lucide package
                                break;
                            default:
                                appItem.IconGlyph = "\uE329"; // Lucide file-type
                                break;
                        }
                    }
                }
                else
                {
                    appItem.IconGlyph = "\uE329"; // Lucide file-type
                }

                matchedItems.Add(appItem);
            }
        }

        PinnedAppsList.Clear();
        foreach (var item in matchedItems)
        {
            PinnedAppsList.Add(item);
        }

        if (PinnedAppsList.Count > 0)
        {
            _selectedIndex = 0;
            PinnedAppsList[0].IsSelected = true;
        }
        else
        {
            _selectedIndex = -1;
        }

        UpdateWindowSize();
    }

    private void LoadAiHistory()
    {
        var history = ClipboardService.Instance.History;
        var matchedItems = new List<PinnedApp>();

        var aiItems = history.Where(item => item.Type == "IA" || (item.Tags != null && item.Tags.Contains("#IA")))
                             .OrderByDescending(item => item.Timestamp)
                             .ToList();

        foreach (var item in aiItems)
        {
            string timeLabel = GetFormattedCopyTime(item.Timestamp);

            var appItem = new PinnedApp
            {
                Name = item.Content,
                Subtitle = $"Respuesta IA • {timeLabel}",
                Path = "clipboard:" + item.Content,
                RunAsAdmin = false,
                ClipboardId = item.Id,
                ClipboardType = "IA",
                Timestamp = item.Timestamp,
                IconGlyph = "\uE1BB" // Lucide Bot
            };

            matchedItems.Add(appItem);
        }

        PinnedAppsList.Clear();
        if (matchedItems.Count > 0)
        {
            foreach (var item in matchedItems)
            {
                PinnedAppsList.Add(item);
            }
            _selectedIndex = 0;
            PinnedAppsList[0].IsSelected = true;
        }
        else
        {
            PinnedAppsList.Add(new PinnedApp
            {
                Name = "Consulta a Gemini AI",
                Subtitle = "Escribe tu pregunta en el cuadro de búsqueda y presiona Enter",
                Path = "AI_EMPTY",
                IconGlyph = "\uE1BB"
            });
            _selectedIndex = 0;
            PinnedAppsList[0].IsSelected = true;
        }

        UpdateWindowSize();
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateLayout();
            UpdateWindowSize();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Check custom toggle pin hotkey first
        if (IsHotkeyTriggered(e, SettingsService.Instance.Hotkey_TogglePinApp))
        {
            if (_selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
            {
                var selectedApp = PinnedAppsList[_selectedIndex];
                TogglePinApp(selectedApp.Path);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab)
        {
            CycleSearchMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (_searchMode == "AI")
            {
                _ = PerformAiSearchAsync(SearchBox?.Text.Trim() ?? "");
                e.Handled = true;
                return;
            }

            bool webVisible = WebSearchButton != null && WebSearchButton.IsVisible;
            int  webIndex   = PinnedAppsList.Count;

            if (webVisible && _selectedIndex == webIndex)
            {
                OpenWebSearch();
            }
            else if (_selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
            {
                var selectedApp = PinnedAppsList[_selectedIndex];
                LaunchApp(selectedApp.Path, selectedApp.RunAsAdmin);
            }
            else if (PinnedAppsList.Count == 0 && !string.IsNullOrEmpty(SearchBox?.Text.Trim()))
            {
                OpenWebSearch();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            MoveSelection(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            MoveSelection(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Right
                 && _selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count
                 && SearchBox.CaretIndex >= SearchBox.Text.Length)
        {
            SetRunAsAdmin(true);
            e.Handled = true;
        }
        else if (e.Key == Key.Left
                 && _selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count
                 && SearchBox.CaretIndex == 0)
        {
            SetRunAsAdmin(false);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SafeClose();
            e.Handled = true;
        }
    }

    private bool IsHotkeyTriggered(KeyEventArgs e, string hotkeyText)
    {
        if (string.IsNullOrWhiteSpace(hotkeyText)) return false;

        var parts = hotkeyText.Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        bool ctrlRequired = false;
        bool shiftRequired = false;
        bool altRequired = false;
        bool winRequired = false;

        string keyPart = parts[parts.Length - 1].Trim();

        for (int i = 0; i < parts.Length - 1; i++)
        {
            string mod = parts[i].Trim().ToLower();
            if (mod == "ctrl" || mod == "control") ctrlRequired = true;
            else if (mod == "shift") shiftRequired = true;
            else if (mod == "alt") altRequired = true;
            else if (mod == "win" || mod == "windows") winRequired = true;
        }

        bool ctrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool altPressed = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        bool winPressed = (Keyboard.Modifiers & ModifierKeys.Windows) != 0;

        if (ctrlPressed != ctrlRequired) return false;
        if (shiftPressed != shiftRequired) return false;
        if (altPressed != altRequired) return false;
        if (winPressed != winRequired) return false;

        if (Enum.TryParse<Key>(keyPart, true, out Key targetKey))
        {
            Key actualKey = (e.Key == Key.System) ? e.SystemKey : e.Key;
            return actualKey == targetKey;
        }

        if (keyPart.Equals("Esc", StringComparison.OrdinalIgnoreCase) || keyPart.Equals("Escape", StringComparison.OrdinalIgnoreCase))
            return e.Key == Key.Escape;
        if (keyPart.Equals("Enter", StringComparison.OrdinalIgnoreCase) || keyPart.Equals("Return", StringComparison.OrdinalIgnoreCase))
            return e.Key == Key.Enter;
        if (keyPart.Equals("Tab", StringComparison.OrdinalIgnoreCase))
            return e.Key == Key.Tab;
        if (keyPart.Equals("Space", StringComparison.OrdinalIgnoreCase))
            return e.Key == Key.Space;

        return false;
    }

    private void SetRunAsAdmin(bool admin)
    {
        if (_selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
        {
            PinnedAppsList[_selectedIndex].RunAsAdmin = admin;
        }
    }

    private void LaunchApp(string path, bool asAdmin)
    {
        if (string.IsNullOrEmpty(path) || path == "AI_ERROR" || path == "AI_LOADING" || path == "AI_EMPTY")
        {
            return;
        }

        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var _ = Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch { }
            SafeClose();
            return;
        }

        if (path.StartsWith("clipboard:", StringComparison.OrdinalIgnoreCase))
        {
            var appItem = PinnedAppsList.FirstOrDefault(a => a.Path == path);
            if (appItem != null && appItem.ClipboardId != Guid.Empty)
            {
                ClipboardService.Instance.CopyToClipboardInternal(appItem.ClipboardId);
            }
            else
            {
                var content = path.Substring("clipboard:".Length);
                ClipboardService.Instance.CopyToClipboardInternal(content);
            }
            SoundService.Play(SoundType.Click);
            SafeClose();
            return;
        }

        if (path == "ADD_APP")
        {
            AddNewApp();
            return;
        }

        try
        {
            // Increment launch count
            var counts = SettingsService.Instance.AppLaunchCounts;
            if (counts == null)
            {
                SettingsService.Instance.AppLaunchCounts = new Dictionary<string, int>();
                counts = SettingsService.Instance.AppLaunchCounts;
            }
            if (!counts.ContainsKey(path))
            {
                counts[path] = 0;
            }
            counts[path]++;
            SettingsService.Save();

            string launchPath = path;
            string exeName = "";
            bool isUwp = path.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase);

            if (isUwp)
            {
                var parts = path.Substring(4).Split('|');
                var aumid = parts[0];
                var exePath = parts.Length > 1 ? parts[1] : "";
                exeName = Path.GetFileName(exePath);
                launchPath = $@"shell:AppsFolder\{aumid}";
            }
            else
            {
                var fullPath = ResolvePath(path);
                exeName = Path.GetFileName(fullPath);
                launchPath = fullPath;
            }

            // Try to activate existing window first
            if (!string.IsNullOrEmpty(exeName) && ActivateExistingWindow(exeName))
            {
                SafeClose();
                return;
            }

            // Hide the launcher window immediately so that Windows directs activation focus
            // to the newly launched application rather than backing into other WallFlow windows.
            try { Visibility = Visibility.Collapsed; } catch { }

            if (isUwp)
            {
                using var _ = Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = launchPath,
                    UseShellExecute = true
                });
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName = launchPath,
                    UseShellExecute = true
                };
                if (asAdmin)
                {
                    psi.Verb = "runas";
                }
                using var _ = Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir la aplicación: {ex.Message}", "Error Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        SafeClose();
    }

    private void MoveSelection(int direction)
    {
        bool webVisible = WebSearchButton != null && WebSearchButton.IsVisible;
        bool hasApps   = PinnedAppsList.Count > 0;
        int  webIndex  = PinnedAppsList.Count; // virtual index for the web search row
        int  total     = webIndex + (webVisible ? 1 : 0);

        if (total == 0) return;

        // Clear current highlight
        if (_selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
            PinnedAppsList[_selectedIndex].IsSelected = false;
        SetWebSearchHighlight(false);

        _selectedIndex += direction;

        // Wrap around
        if (_selectedIndex < 0)
            _selectedIndex = total - 1;
        else if (_selectedIndex >= total)
            _selectedIndex = 0;

        // Apply new highlight
        if (webVisible && _selectedIndex == webIndex)
        {
            SetWebSearchHighlight(true);
        }
        else if (_selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
        {
            PinnedAppsList[_selectedIndex].IsSelected = true;
            try
            {
                var container = AppsControl.ItemContainerGenerator.ContainerFromIndex(_selectedIndex) as FrameworkElement;
                container?.BringIntoView();
            }
            catch { /* ignored */ }
        }
    }

    private void SetWebSearchHighlight(bool selected)
    {
        if (WebRowBorder == null) return;
        if (selected)
        {
            WebRowBorder.Background  = SpotlightSelectedBackgroundBrush;
            WebRowBorder.BorderBrush = SpotlightSelectedBorderBrush;
        }
        else
        {
            WebRowBorder.Background  = System.Windows.Media.Brushes.Transparent;
            WebRowBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
        }
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e) { }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e) { }
}

public static class MarkdownHelper
{
    public static readonly DependencyProperty FormattedTextProperty =
        DependencyProperty.RegisterAttached(
            "FormattedText",
            typeof(string),
            typeof(MarkdownHelper),
            new PropertyMetadata(string.Empty, OnFormattedTextChanged));

    public static string GetFormattedText(DependencyObject obj)
    {
        return (string)obj.GetValue(FormattedTextProperty);
    }

    public static void SetFormattedText(DependencyObject obj, string value)
    {
        obj.SetValue(FormattedTextProperty, value);
    }

    private static void OnFormattedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
        {
            textBlock.Inlines.Clear();
            string text = e.NewValue as string ?? string.Empty;
            if (string.IsNullOrEmpty(text)) return;

            ParseMarkdownToInlines(text, textBlock.Inlines, textBlock.FontSize);
        }
    }

    public static void ParseMarkdownToInlines(string text, System.Windows.Documents.InlineCollection inlines, double baseFontSize)
    {
        string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        bool inCodeBlock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (line.TrimStart().StartsWith("```"))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (i > 0)
            {
                inlines.Add(new System.Windows.Documents.LineBreak());
            }

            if (inCodeBlock)
            {
                var codeSpan = new System.Windows.Documents.Span(new System.Windows.Documents.Run(line))
                {
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 203, 107)),
                    Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255))
                };
                inlines.Add(codeSpan);
                continue;
            }

            if (line.StartsWith("# "))
            {
                AddFormattedText(line.Substring(2), inlines, isBold: true, fontSize: baseFontSize + 4);
            }
            else if (line.StartsWith("## "))
            {
                AddFormattedText(line.Substring(3), inlines, isBold: true, fontSize: baseFontSize + 3);
            }
            else if (line.StartsWith("### "))
            {
                AddFormattedText(line.Substring(4), inlines, isBold: true, fontSize: baseFontSize + 2);
            }
            else if (line.StartsWith("* ") || line.StartsWith("- ") || line.StartsWith("• "))
            {
                inlines.Add(new System.Windows.Documents.Run("• "));
                string content = line.Length > 2 ? line.Substring(2) : "";
                AddFormattedText(content, inlines, isBold: false, fontSize: baseFontSize);
            }
            else
            {
                AddFormattedText(line, inlines, isBold: false, fontSize: baseFontSize);
            }
        }
    }

    private static void AddFormattedText(string text, System.Windows.Documents.InlineCollection inlines, bool isBold, double fontSize)
    {
        var regex = new System.Text.RegularExpressions.Regex(@"(\*\*.*?\*\*|__.*?__|`.*?`|\*.*?\*|_.*?_)");
        var parts = regex.Split(text);

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            if ((part.StartsWith("**") && part.EndsWith("**") && part.Length >= 4) ||
                (part.StartsWith("__") && part.EndsWith("__") && part.Length >= 4))
            {
                string inner = part.Substring(2, part.Length - 4);
                var boldRun = new System.Windows.Documents.Run(inner) { FontWeight = FontWeights.Bold, FontSize = fontSize };
                inlines.Add(boldRun);
            }
            else if ((part.StartsWith("*") && part.EndsWith("*") && part.Length >= 2) ||
                     (part.StartsWith("_") && part.EndsWith("_") && part.Length >= 2))
            {
                string inner = part.Substring(1, part.Length - 2);
                var italicRun = new System.Windows.Documents.Run(inner) { FontStyle = FontStyles.Italic, FontSize = fontSize };
                if (isBold) italicRun.FontWeight = FontWeights.Bold;
                inlines.Add(italicRun);
            }
            else if (part.StartsWith("`") && part.EndsWith("`") && part.Length >= 2)
            {
                string inner = part.Substring(1, part.Length - 2);
                var codeRun = new System.Windows.Documents.Run(" " + inner + " ")
                {
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 203, 107)),
                    Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
                    FontSize = fontSize - 1
                };
                inlines.Add(codeRun);
            }
            else
            {
                var normalRun = new System.Windows.Documents.Run(part) { FontSize = fontSize };
                if (isBold) normalRun.FontWeight = FontWeights.Bold;
                inlines.Add(normalRun);
            }
        }
    }
}
