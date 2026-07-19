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

namespace WallFlow.Windows;

public partial class PinnedAppsWindow : Window, System.ComponentModel.INotifyPropertyChanged
{
    public class PinnedApp : System.ComponentModel.INotifyPropertyChanged
    {
        private ImageSource? _icon;
        private bool _isSelected;
        private bool _isPinned;

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

        public string ActionText => RunAsAdmin ? "Admin" : "Ejecutar";

        public string IconGlyph { get; set; } = string.Empty;
        public bool IsGlyphVisible => !string.IsNullOrEmpty(IconGlyph);
        public bool IsIconVisible => string.IsNullOrEmpty(IconGlyph) && Icon != null;
        public bool IsActionVisible => Path != "ADD_APP";

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    public ObservableCollection<PinnedApp> PinnedAppsList { get; } = new();
    public ObservableCollection<PinnedApp> FavoritesList { get; } = new();

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

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxIconCacheSize = 200;
    private readonly List<PinnedApp> _allInstalledApps = new();
    private int _selectedIndex = -1;
    private bool _isClosing = false;
    private bool _isShowingDialog = false;
    private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;

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
        SpotlightSearchFocusBrush = new SolidColorBrush(p.SearchFocus);
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
            SearchClipboard(SearchBox.Text.Trim());
        }
    }

    public void SetSearchMode(string mode)
    {
        if (_searchMode == mode) return;
        _searchMode = mode;
        
        if (SearchBox != null)
        {
            SearchBox.Text = string.Empty;
        }

        UpdateModeButtonsVisuals();
        
        if (_searchMode == "Clipboard")
        {
            SearchClipboard(string.Empty);
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

    private void CycleSearchMode()
    {
        if (_searchMode == "Apps") SetSearchMode("Files");
        else if (_searchMode == "Files") SetSearchMode("Clipboard");
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

        if (PlaceholderText != null)
        {
            if (_searchMode == "Apps") PlaceholderText.Text = "Buscar aplicación...";
            else if (_searchMode == "Files") PlaceholderText.Text = "Buscar archivos...";
            else if (_searchMode == "Clipboard") PlaceholderText.Text = "Buscar en el portapapeles...";
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
            IconGlyph = "\uE710"
        });

        // Trigger loading of icons for these initial favorites in the background immediately
        StartLoadingIcons();

        // Also trigger default list layout update
        PopulateDefaultList();

        // Load Start Menu apps
        await Task.Run(() =>
        {
            var userPrograms = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

            void ScanDirectory(string dir)
            {
                if (!Directory.Exists(dir)) return;
                try
                {
                    foreach (var file in Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories))
                    {
                        if (_isClosing) return;
                        try
                        {
                            var name = Path.GetFileNameWithoutExtension(file);
                            if (name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Unins", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("ReadMe", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Help", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Website", StringComparison.OrdinalIgnoreCase))
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

            ScanDirectory(userPrograms);
            ScanDirectory(commonPrograms);

            if (paths != null)
            {
                foreach (var path in paths)
                {
                    if (_isClosing) return;
                    if (string.IsNullOrEmpty(path) || path == "ADD_APP") continue;

                    string targetPath = path;
                    string aumid = "";
                    bool isUwp = path.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase);
                    if (isUwp)
                    {
                        var parts = path.Substring(4).Split('|');
                        aumid = parts[0];
                        targetPath = parts.Length > 1 ? parts[1] : "";
                    }

                    var resolved = ResolvePath(targetPath);
                    if (File.Exists(resolved) || isUwp)
                    {
                        bool alreadyAdded = false;
                        foreach (var loaded in appsToLoad)
                        {
                            if (string.Equals(loaded.Path, path, StringComparison.OrdinalIgnoreCase) || 
                                string.Equals(loaded.Path, resolved, StringComparison.OrdinalIgnoreCase))
                            {
                                alreadyAdded = true;
                                break;
                            }
                        }

                        if (!alreadyAdded)
                        {
                            var name = TranslateAppName(Path.GetFileNameWithoutExtension(resolved));

                            appsToLoad.Add(new PinnedApp
                            {
                                Name = name,
                                Path = path,
                                Icon = null
                            });
                        }
                    }
                }
            }

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

                var icon = LoadIconForApp(app);
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

                var icon = LoadIconForApp(app);
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

    private ImageSource? LoadIconForApp(PinnedApp app)
    {
        try
        {
            return app.Path.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase)
                ? GetUwpIcon(app.Path.Substring(4).Split('|')[0])
                : GetAppIcon(ResolvePath(app.Path));
        }
        catch { /* ignored */ return null; }
    }

    private string ResolvePath(string path)
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

    private ImageSource? GetAppIcon(string path)
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
                        var imageSource = Dispatcher.Invoke(() =>
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

    private ImageSource? GetUwpIcon(string aumid)
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
                        var bitmapSource = Dispatcher.Invoke(() =>
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

    private void PinMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem item && item.Tag is string path)
        {
            if (SettingsService.Instance.PinnedAppPaths == null)
            {
                SettingsService.Instance.PinnedAppPaths = new List<string>();
            }

            if (!SettingsService.Instance.PinnedAppPaths.Contains(path))
            {
                SettingsService.Instance.PinnedAppPaths.Add(path);
                SettingsService.Save();
                
                // Reload layout
                _ = LoadAppsStaggeredAsync();
            }
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_isShowingDialog) return;
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
        if (string.IsNullOrEmpty(query) && _searchMode != "Clipboard")
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

            // Height formula:
            // 10 (Grid top) + 54 (Searchbox) + 10 (Searchbox bottom margin)
            // + (rowCount * (54 + 8)) [each row is 54px high + 8px vertical margin]
            // + 10 (Grid bottom margin) + 24 (shadow bleed room: BlurRadius 14 + ShadowDepth 4 + margin)
            Height = 10 + 54 + 10 + (rowCount * 62) + 10 + 24;
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

            // Height = searchbox + results rows + separator (9px if any) + web row (48px) + card padding + shadow
            int separatorH = (hasResults && showWebSearch) ? 9 : 0;
            int webSearchH = showWebSearch ? 48 : 0;
            Height = 10 + 54 + 10 + (Math.Min(6, itemsCount) * 48) + separatorH + webSearchH + 8 + 24;
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
                if (favPath == "ADD_APP") continue;
                foreach (var app in _allInstalledApps)
                {
                    if (string.Equals(app.Path, favPath, StringComparison.OrdinalIgnoreCase))
                    {
                        FavoritesList.Add(app);
                        break;
                    }
                }
            }
        }

        FavoritesList.Add(new PinnedApp
        {
            Name = "Anclar Aplicación",
            Path = "ADD_APP",
            IconGlyph = "\uE710"
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

        scored.Sort((a, b) => b.score.CompareTo(a.score));

        foreach (var (app, _) in scored)
            PinnedAppsList.Add(app);

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

    private async Task SearchFilesAsync(string query)
    {
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
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        };

        var matchedFiles = new List<PinnedApp>();

        await Task.Run(() =>
        {
            try
            {
                foreach (var dir in searchDirs)
                {
                    if (!Directory.Exists(dir)) continue;

                    var options = new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = false,
                        MaxRecursionDepth = 1
                    };

                    foreach (var filePath in Directory.EnumerateFiles(dir, "*", options))
                    {
                        string fileName = Path.GetFileName(filePath);
                        if (fileName.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            var appItem = new PinnedApp
                            {
                                Name = fileName,
                                Path = filePath,
                                RunAsAdmin = false
                            };
                            
                            // Load icon
                            var icon = GetAppIcon(filePath);
                            if (icon != null) appItem.Icon = icon;
                            else appItem.IconGlyph = "\xE8A5"; // File glyph

                            matchedFiles.Add(appItem);

                            if (matchedFiles.Count >= 20) break;
                        }
                    }

                    if (matchedFiles.Count >= 20) break;
                }
            }
            catch { }
        });

        Dispatcher.Invoke(() =>
        {
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

                var appItem = new PinnedApp
                {
                    Name = displayName.Length > 80 ? displayName.Substring(0, 77).Replace("\r", " ").Replace("\n", " ") + "..." : displayName.Replace("\r", " ").Replace("\n", " "),
                    Path = "clipboard:" + item.Content,
                    RunAsAdmin = false
                };

                if (item.Type == "Image" && !string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
                {
                    try
                    {
                        var uri = new Uri(item.ImagePath, UriKind.Absolute);
                        var bmi = new BitmapImage(uri);
                        appItem.Icon = bmi;
                    }
                    catch { appItem.IconGlyph = "\xE722"; } // Picture glyph
                }
                else if (item.Type == "Code")
                {
                    appItem.IconGlyph = "\xE943"; // Code glyph
                }
                else if (item.Type == "Link")
                {
                    appItem.IconGlyph = "\xE71B"; // Link glyph
                }
                else if (item.Type == "File")
                {
                    appItem.IconGlyph = "\xE8B7"; // Folder/File glyph
                }
                else
                {
                    appItem.IconGlyph = "\xE8C8"; // Copy/Clipboard glyph
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
        if (path.StartsWith("clipboard:", StringComparison.OrdinalIgnoreCase))
        {
            var content = path.Substring("clipboard:".Length);
            ClipboardService.Instance.CopyToClipboardInternal(content);
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
