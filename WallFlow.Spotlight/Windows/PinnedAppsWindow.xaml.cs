using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WallFlow.Native;
using WallFlow.Services;
using WallFlow.Spotlight.Models;
using WallFlow.Spotlight.Services;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Gaming.Input;
using Windows.Media.Control;
using Windows.Media.Devices;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace WallFlow.Windows;

public partial class PinnedAppsWindow : Window, INotifyPropertyChanged, IWidget
{
	private List<ActiveWindowModel> _allActiveWindows = new List<ActiveWindowModel>();

	private Visibility _activeWindowsVisibility;

	private string _activeWindowsCountText = "0 en ejecución";

	private Brush _modeWindowsBrush = Brushes.Transparent;

	private Brush _modeWindowsForegroundBrush = Brushes.White;

	private string _satelliteMediaTitle = "Reproductor";

	private string _satelliteMediaArtist = string.Empty;

	private string _satelliteMediaGlyph = "\ue13c";

	private ImageSource? _satelliteMediaArtwork;

	private bool _isMediaPlaying;

	private Visibility _isPlayerExpandedVisibility = Visibility.Collapsed;

	private Visibility _notificationsCardVisibility = Visibility.Collapsed;

	private string _notificationsCountText = "0 notificaciones";

	private bool _hasUnreadNotifications;

	private bool _hasNoNotifications = true;

	private bool _isNotificationsLoading;

	private Visibility _connectedDevicesCardVisibility = Visibility.Collapsed;

	private string _connectedDevicesCountText = "0 dispositivos";

	private bool _hasConnectedDevices;

	private bool _hasNoConnectedDevices = true;

	private bool _isConnectedDevicesLoading;

	private Visibility _wallpaperFanCardVisibility = Visibility.Collapsed;

	private int _fanCurrentIndex;
	private string _lastLoadedFanDir = string.Empty;
	private int _lastLoadedFanCount = 0;
	private readonly HashSet<string> _inFlightThumbnails = new(StringComparer.OrdinalIgnoreCase);
	private long _lastWheelTicks;

	private string _fanWallpapersCountText = "0 Fondos";

	private string _activeFanTitleText = "Selecciona un fondo";

	private string _activeFanMetaText = "";

	private string _fanCounterText = "0 / 0";

	private bool _hasFanWallpapers;

	private bool _isOpeningSubWidget;

	private GlobalSystemMediaTransportControlsSessionManager? _mediaSessionManager;

	private GlobalSystemMediaTransportControlsSession? _currentMediaSession;

	private CancellationTokenSource? _mediaCts;

	private string _spotlightCurrentTime = DateTime.Now.ToString("HH:mm");

	private string _spotlightCurrentDate = "";

	private string _spotlightWeatherSnippet = "";

	private bool _hasWeatherSnippet;

	private double _spotlightRamPercent;

	private string _spotlightRamPercentText = "0%";

	private string _spotlightRamUsageText = "-- / -- GB";

	private double _spotlightDiskPercent;

	private string _spotlightDiskPercentText = "0%";

	private string _spotlightDiskUsageText = "-- libres";

	private string _spotlightDiskName = "DISCO (C:)";

	private string _spotlightBatterySnippet = "";

	private bool _hasBatterySnippet;

	private readonly List<PinnedApp> _allInstalledApps = new List<PinnedApp>();

	private readonly IIconExtractionService _iconService = IconExtractionService.Instance;

	private readonly IAppDiscoveryService _discoveryService = AppDiscoveryService.Instance;

	private readonly ISpotlightSearchService _searchService = SpotlightSearchService.Instance;

	private readonly IGeminiSpotlightService _geminiService = GeminiSpotlightService.Instance;

	private readonly ISpotlightThemeService _themeService = SpotlightThemeService.Instance;

	private CancellationTokenSource? _fileSearchCts;

	private DispatcherTimer? _searchDebounceTimer;

	private DispatcherTimer? _musicDebounceTimer;

	private DispatcherTimer? _clockTimer;

	private DateTime _loadedTime = DateTime.MinValue;

	private int _selectedIndex = -1;

	private bool _isClosing;

	private bool _isShowingDialog;

	private bool _isChangingMode;

	private string _searchMode = "Apps";

	private Brush _modeAppsBrush = Brushes.Transparent;

	private Brush _modeFilesBrush = Brushes.Transparent;

	private Brush _modeClipboardBrush = Brushes.Transparent;

	private Brush _modeAiBrush = Brushes.Transparent;

	private Brush _modeAppsForegroundBrush = Brushes.White;

	private Brush _modeFilesForegroundBrush = Brushes.White;

	private Brush _modeClipboardForegroundBrush = Brushes.White;

	private Brush _modeAiForegroundBrush = Brushes.White;

	private Brush _spotlightBgBrush = Brushes.Transparent;

	private Brush _spotlightBorderBrush = Brushes.Transparent;

	private Thickness _spotlightBorderThickness = new Thickness(0.0);

	private Brush _spotlightTextBrush = Brushes.White;

	private Brush _spotlightPlaceholderBrush = new SolidColorBrush(Color.FromArgb(80, byte.MaxValue, byte.MaxValue, byte.MaxValue));

	private Brush _spotlightIconBrush = new SolidColorBrush(Color.FromArgb(96, byte.MaxValue, byte.MaxValue, byte.MaxValue));

	private Brush _spotlightSelectedBorderBrush = new SolidColorBrush(Color.FromRgb(0, 122, byte.MaxValue));

	private Brush _spotlightSelectedBackgroundBrush = new SolidColorBrush(Color.FromArgb(37, byte.MaxValue, byte.MaxValue, byte.MaxValue));

	private Brush _spotlightHoverBackgroundBrush = new SolidColorBrush(Color.FromArgb(21, byte.MaxValue, byte.MaxValue, byte.MaxValue));

	private Brush _spotlightHoverBorderBrush = new SolidColorBrush(Color.FromArgb(32, byte.MaxValue, byte.MaxValue, byte.MaxValue));

	private Brush _spotlightItemBackgroundBrush = new SolidColorBrush(Color.FromArgb(18, byte.MaxValue, byte.MaxValue, byte.MaxValue));

	private Brush _spotlightItemForegroundBrush = new SolidColorBrush(Color.FromArgb(165, byte.MaxValue, byte.MaxValue, byte.MaxValue));

	private Brush _spotlightSecondaryTextBrush = new SolidColorBrush(Color.FromArgb(160, byte.MaxValue, byte.MaxValue, byte.MaxValue));

	private Brush _spotlightSeparatorBrush = new SolidColorBrush(Color.FromArgb(21, byte.MaxValue, byte.MaxValue, byte.MaxValue));

	private Brush _spotlightSearchFocusBrush = new SolidColorBrush(Color.FromArgb(230, 34, 34, 38));

	private double _spotlightWidth = 600.0;

	private CornerRadius _spotlightCornerRadius = new CornerRadius(27.0);

	private Visibility _spotlightFavoritesVisibility;

	private bool _isWebSearchSelected;

	private double _spotlightContainerWidth = 980.0;

	private Visibility _bentoVisibility;

	private string _bentoWeatherTemp = "--°C";

	private string _bentoWeatherCity = "Clima local";

	private string _bentoWeatherCondition = "Cargando...";

	private string _bentoWeatherGlyph = "\ue706";

	private string _bentoWeatherDate = "";

	private string _bentoClipboardSnippet = "Portapapeles vacío";

	private string _bentoClipboardType = "Texto";

	private string _currentWallpaperPath = "";

	private ImageSource? _bentoWallpaperSource;

	private string _bentoWallpaperName = "Fondo de Windows";

	private string _bentoWallpaperResolution = "";

	private PinnedApp? _inspectorSelectedItem;

	private string _inspectorTitle = "";

	private string _inspectorSubtitle = "";

	private string _inspectorPath = "";

	private ImageSource? _inspectorImageSource;

	private string _inspectorFileSize = "";

	private string _inspectorDimensions = "";

	private string _inspectorModifiedDate = "";

	private string _inspectorCodeSnippet = "";

	private string _inspectorCodeLines = "";

	private string _inspectorCodeLanguage = "";

	private ImageSource? _inspectorAppIcon;

	private string _inspectorAppName = "";

	private string _inspectorAppStatus = "Listo para iniciar";

	private string _inspectorAppRamUsage = "—";

	private string _inspectorAppPid = "—";

	private string _inspectorWebQuery = "";

	private bool _inspectorIsAppRunning;

	private string _inspectorFullClipboardText = "";

	private string _inspectorClipboardWordCount = "";

	private Brush? _inspectorColorPreviewBrush;

	private bool _inspectorIsColor;

	private bool _inspectorIsLink;

	private ImageSource? _inspectorClipboardImageSource;

	private bool _inspectorHasClipboardImage;

	private Visibility _inspectorClipboardImageViewVisibility = Visibility.Collapsed;

	private Visibility _inspectorClipboardTextViewVisibility = Visibility.Visible;

	private string _inspectorClipboardImageDimensions = "";

	private string _inspectorClipboardImageDetails = "";

	private string _inspectorClipboardCopyButtonText = "Copiar (Enter)";

	private string _inspectorAiResponse = "";

	private Visibility _inspectorFileViewVisibility = Visibility.Collapsed;

	private Visibility _inspectorCodeViewVisibility = Visibility.Collapsed;

	private Visibility _inspectorAppViewVisibility = Visibility.Collapsed;

	private Visibility _inspectorClipboardViewVisibility = Visibility.Collapsed;

	private Visibility _inspectorAiViewVisibility = Visibility.Collapsed;

	private Visibility _inspectorWebViewVisibility = Visibility.Collapsed;

	private Visibility _inspectorDefaultViewVisibility;

	private Visibility _inspectorMusicViewVisibility = Visibility.Collapsed;

	private string _inspectorMusicTitle = "";

	private string _inspectorMusicSubtitle = "";

	private string _inspectorMusicQuery = "";

	private string _inspectorMusicStatus = "";

	private ImageSource? _inspectorMusicArtwork;

	private string _inspectorMusicDuration = "";

	private Visibility _inspectorMusicDurationVisibility = Visibility.Collapsed;

	private Visibility _inspectorMusicEqualizerVisibility = Visibility.Collapsed;

	private const int WM_SYSCOMMAND = 274;

	private const int SC_KEYMENU = 61696;

	private CancellationTokenSource? _musicSearchCts;

	private Point _favDragStartPoint;

	private PinnedApp? _draggedFavApp;

	private bool _isFavDragging;

	private CancellationTokenSource? _inspectorCts;


	string IWidget.Name => "PinnedApps";

	public bool IsOpen
	{
		get
		{
			if (IsLoaded)
			{
				return IsVisible;
			}
			return false;
		}
	}

	public ObservableCollection<PinnedApp> PinnedAppsList { get; } = new ObservableCollection<PinnedApp>();

	public ObservableCollection<AiChatMessage> AiChatMessages { get; } = new ObservableCollection<AiChatMessage>();

	public Visibility AiChatViewVisibility => _searchMode == "AI" ? Visibility.Visible : Visibility.Collapsed;
	public Visibility StandardResultsViewVisibility => _searchMode != "AI" ? Visibility.Visible : Visibility.Collapsed;
	public Visibility AiChatEmptyStateVisibility => (_searchMode == "AI" && AiChatMessages.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
	public Visibility AiChatThreadVisibility => (_searchMode == "AI" && AiChatMessages.Count > 0) ? Visibility.Visible : Visibility.Collapsed;

	public string ActiveAiModelName
	{
		get
		{
			string m = SettingsService.Instance.GeminiModel ?? "gemini-3.5-flash-lite";
			return m switch
			{
				"gemini-3.5-flash-lite" => "Gemini 3.5 Flash Lite",
				"gemini-3.6-flash" => "Gemini 3.6 Flash",
				"gemini-3.7-flash" => "Gemini 3.7 Flash",
				"gemini-3.8-flash" => "Gemini 3.8 Flash",
				_ => m
			};
		}
	}

	public void UpdateAiChatVisibilities()
	{
		OnPropertyChanged(nameof(AiChatViewVisibility));
		OnPropertyChanged(nameof(StandardResultsViewVisibility));
		OnPropertyChanged(nameof(AiChatEmptyStateVisibility));
		OnPropertyChanged(nameof(AiChatThreadVisibility));
		OnPropertyChanged(nameof(ActiveAiModelName));
	}

	public ObservableCollection<PinnedApp> FavoritesList { get; } = new ObservableCollection<PinnedApp>();

	public ObservableCollection<ActiveWindowModel> ActiveWindowsList { get; } = new ObservableCollection<ActiveWindowModel>();

	public Visibility ActiveWindowsVisibility
	{
		get
		{
			return _activeWindowsVisibility;
		}
		set
		{
			_activeWindowsVisibility = value;
			OnPropertyChanged("ActiveWindowsVisibility");
		}
	}

	public string ActiveWindowsCountText
	{
		get
		{
			return _activeWindowsCountText;
		}
		set
		{
			_activeWindowsCountText = value;
			OnPropertyChanged("ActiveWindowsCountText");
		}
	}

	public Brush ModeWindowsBrush
	{
		get
		{
			return _modeWindowsBrush;
		}
		set
		{
			_modeWindowsBrush = value;
			OnPropertyChanged("ModeWindowsBrush");
		}
	}

	public Brush ModeWindowsForegroundBrush
	{
		get
		{
			return _modeWindowsForegroundBrush;
		}
		set
		{
			_modeWindowsForegroundBrush = value;
			OnPropertyChanged("ModeWindowsForegroundBrush");
		}
	}

	public string SatelliteMediaTitle
	{
		get
		{
			return _satelliteMediaTitle;
		}
		set
		{
			_satelliteMediaTitle = value;
			OnPropertyChanged("SatelliteMediaTitle");
		}
	}

	public bool HasMediaArtist => !string.IsNullOrWhiteSpace(_satelliteMediaArtist);

	public string SatelliteMediaArtist
	{
		get
		{
			return _satelliteMediaArtist;
		}
		set
		{
			_satelliteMediaArtist = value;
			OnPropertyChanged("SatelliteMediaArtist");
			OnPropertyChanged("HasMediaArtist");
		}
	}

	public string SatelliteMediaGlyph
	{
		get
		{
			return _satelliteMediaGlyph;
		}
		set
		{
			_satelliteMediaGlyph = value;
			OnPropertyChanged("SatelliteMediaGlyph");
		}
	}

	public ImageSource? SatelliteMediaArtwork
	{
		get
		{
			return _satelliteMediaArtwork;
		}
		set
		{
			_satelliteMediaArtwork = value;
			OnPropertyChanged("SatelliteMediaArtwork");
			OnPropertyChanged("HasMediaArtwork");
			OnPropertyChanged("HasNoMediaArtwork");
		}
	}

	public bool HasMediaArtwork => _satelliteMediaArtwork != null;

	public bool HasNoMediaArtwork => _satelliteMediaArtwork == null;

	public bool IsMediaPlaying
	{
		get
		{
			return _isMediaPlaying;
		}
		set
		{
			_isMediaPlaying = value;
			OnPropertyChanged("IsMediaPlaying");
			OnPropertyChanged("PlayGlyphMargin");
		}
	}

	public Thickness PlayGlyphMargin
	{
		get
		{
			if (!_isMediaPlaying)
			{
				return new Thickness(1.5, 0.0, 0.0, 0.0);
			}
			return new Thickness(0.0, 0.0, 0.0, 0.0);
		}
	}

	public Visibility IsPlayerExpandedVisibility
	{
		get
		{
			return _isPlayerExpandedVisibility;
		}
		set
		{
			_isPlayerExpandedVisibility = value;
			OnPropertyChanged("IsPlayerExpandedVisibility");
		}
	}

	public Visibility NotificationsCardVisibility
	{
		get
		{
			return _notificationsCardVisibility;
		}
		set
		{
			_notificationsCardVisibility = value;
			OnPropertyChanged("NotificationsCardVisibility");
		}
	}

	public ObservableCollection<SpotlightNotificationItem> NotificationsList { get; } = new ObservableCollection<SpotlightNotificationItem>();

	public string NotificationsCountText
	{
		get
		{
			return _notificationsCountText;
		}
		set
		{
			_notificationsCountText = value;
			OnPropertyChanged("NotificationsCountText");
		}
	}

	public bool HasUnreadNotifications
	{
		get
		{
			return _hasUnreadNotifications;
		}
		set
		{
			_hasUnreadNotifications = value;
			OnPropertyChanged("HasUnreadNotifications");
		}
	}

	public bool HasNoNotifications
	{
		get
		{
			return _hasNoNotifications;
		}
		set
		{
			_hasNoNotifications = value;
			OnPropertyChanged("HasNoNotifications");
		}
	}

	public bool IsNotificationsLoading
	{
		get
		{
			return _isNotificationsLoading;
		}
		set
		{
			_isNotificationsLoading = value;
			OnPropertyChanged("IsNotificationsLoading");
		}
	}

	public Visibility ConnectedDevicesCardVisibility
	{
		get
		{
			return _connectedDevicesCardVisibility;
		}
		set
		{
			_connectedDevicesCardVisibility = value;
			OnPropertyChanged("ConnectedDevicesCardVisibility");
		}
	}

	public ObservableCollection<SpotlightConnectedDeviceItem> ConnectedDevicesList { get; } = new ObservableCollection<SpotlightConnectedDeviceItem>();

	public string ConnectedDevicesCountText
	{
		get
		{
			return _connectedDevicesCountText;
		}
		set
		{
			_connectedDevicesCountText = value;
			OnPropertyChanged("ConnectedDevicesCountText");
		}
	}

	public bool HasConnectedDevices
	{
		get
		{
			return _hasConnectedDevices;
		}
		set
		{
			_hasConnectedDevices = value;
			OnPropertyChanged("HasConnectedDevices");
		}
	}

	public bool HasNoConnectedDevices
	{
		get
		{
			return _hasNoConnectedDevices;
		}
		set
		{
			_hasNoConnectedDevices = value;
			OnPropertyChanged("HasNoConnectedDevices");
		}
	}

	public bool IsConnectedDevicesLoading
	{
		get
		{
			return _isConnectedDevicesLoading;
		}
		set
		{
			_isConnectedDevicesLoading = value;
			OnPropertyChanged("IsConnectedDevicesLoading");
		}
	}

	public Visibility WallpaperFanCardVisibility
	{
		get
		{
			return _wallpaperFanCardVisibility;
		}
		set
		{
			_wallpaperFanCardVisibility = value;
			OnPropertyChanged("WallpaperFanCardVisibility");
		}
	}

	public ObservableCollection<WallpaperFanItem> FanWallpapersList { get; } = new ObservableCollection<WallpaperFanItem>();

	public int FanCurrentIndex => _fanCurrentIndex;

	public string FanWallpapersCountText
	{
		get
		{
			return _fanWallpapersCountText;
		}
		set
		{
			_fanWallpapersCountText = value;
			OnPropertyChanged("FanWallpapersCountText");
		}
	}

	public string ActiveFanTitleText
	{
		get
		{
			return _activeFanTitleText;
		}
		set
		{
			_activeFanTitleText = value;
			OnPropertyChanged("ActiveFanTitleText");
		}
	}

	public string ActiveFanMetaText
	{
		get
		{
			return _activeFanMetaText;
		}
		set
		{
			_activeFanMetaText = value;
			OnPropertyChanged("ActiveFanMetaText");
		}
	}

	public string FanCounterText
	{
		get
		{
			return _fanCounterText;
		}
		set
		{
			_fanCounterText = value;
			OnPropertyChanged("FanCounterText");
		}
	}

	public bool HasFanWallpapers
	{
		get
		{
			return _hasFanWallpapers;
		}
		set
		{
			_hasFanWallpapers = value;
			OnPropertyChanged("HasFanWallpapers");
			OnPropertyChanged("HasNoFanWallpapers");
		}
	}

	public bool HasNoFanWallpapers => !HasFanWallpapers;

	public string SpotlightCurrentTime
	{
		get
		{
			return _spotlightCurrentTime;
		}
		set
		{
			_spotlightCurrentTime = value;
			OnPropertyChanged("SpotlightCurrentTime");
		}
	}

	public string SpotlightCurrentDate
	{
		get
		{
			return _spotlightCurrentDate;
		}
		set
		{
			_spotlightCurrentDate = value;
			OnPropertyChanged("SpotlightCurrentDate");
		}
	}

	public string SpotlightWeatherSnippet
	{
		get
		{
			return _spotlightWeatherSnippet;
		}
		set
		{
			_spotlightWeatherSnippet = value;
			OnPropertyChanged("SpotlightWeatherSnippet");
		}
	}

	public bool HasWeatherSnippet
	{
		get
		{
			return _hasWeatherSnippet;
		}
		set
		{
			_hasWeatherSnippet = value;
			OnPropertyChanged("HasWeatherSnippet");
		}
	}

	public double SpotlightRamPercent
	{
		get
		{
			return _spotlightRamPercent;
		}
		set
		{
			_spotlightRamPercent = value;
			OnPropertyChanged("SpotlightRamPercent");
		}
	}

	public string SpotlightRamPercentText
	{
		get
		{
			return _spotlightRamPercentText;
		}
		set
		{
			_spotlightRamPercentText = value;
			OnPropertyChanged("SpotlightRamPercentText");
		}
	}

	public string SpotlightRamUsageText
	{
		get
		{
			return _spotlightRamUsageText;
		}
		set
		{
			_spotlightRamUsageText = value;
			OnPropertyChanged("SpotlightRamUsageText");
		}
	}

	public double SpotlightDiskPercent
	{
		get
		{
			return _spotlightDiskPercent;
		}
		set
		{
			_spotlightDiskPercent = value;
			OnPropertyChanged("SpotlightDiskPercent");
		}
	}

	public string SpotlightDiskPercentText
	{
		get
		{
			return _spotlightDiskPercentText;
		}
		set
		{
			_spotlightDiskPercentText = value;
			OnPropertyChanged("SpotlightDiskPercentText");
		}
	}

	public string SpotlightDiskUsageText
	{
		get
		{
			return _spotlightDiskUsageText;
		}
		set
		{
			_spotlightDiskUsageText = value;
			OnPropertyChanged("SpotlightDiskUsageText");
		}
	}

	public string SpotlightDiskName
	{
		get
		{
			return _spotlightDiskName;
		}
		set
		{
			_spotlightDiskName = value;
			OnPropertyChanged("SpotlightDiskName");
		}
	}

	public string SpotlightBatterySnippet
	{
		get
		{
			return _spotlightBatterySnippet;
		}
		set
		{
			_spotlightBatterySnippet = value;
			OnPropertyChanged("SpotlightBatterySnippet");
		}
	}

	public bool HasBatterySnippet
	{
		get
		{
			return _hasBatterySnippet;
		}
		set
		{
			_hasBatterySnippet = value;
			OnPropertyChanged("HasBatterySnippet");
		}
	}

	private string _spotlightBatteryGlyph = "\uE83F";
	public string SpotlightBatteryGlyph
	{
		get => _spotlightBatteryGlyph;
		set { _spotlightBatteryGlyph = value; OnPropertyChanged(nameof(SpotlightBatteryGlyph)); }
	}

	private string _spotlightBatteryPercentText = "100%";
	public string SpotlightBatteryPercentText
	{
		get => _spotlightBatteryPercentText;
		set { _spotlightBatteryPercentText = value; OnPropertyChanged(nameof(SpotlightBatteryPercentText)); }
	}

	private bool _hasActiveMusicTrack = false;
	public bool HasActiveMusicTrack
	{
		get => _hasActiveMusicTrack;
		set
		{
			if (_hasActiveMusicTrack != value)
			{
				_hasActiveMusicTrack = value;
				OnPropertyChanged(nameof(HasActiveMusicTrack));
				OnPropertyChanged(nameof(DynamicIslandVisibility));
				OnPropertyChanged(nameof(AmbientClockVisibility));
			}
		}
	}

	private bool _userForcedClockView = false;
	public Visibility DynamicIslandVisibility => (_hasActiveMusicTrack && !_userForcedClockView) ? Visibility.Visible : Visibility.Collapsed;
	public Visibility AmbientClockVisibility => (!(_hasActiveMusicTrack && !_userForcedClockView)) ? Visibility.Visible : Visibility.Collapsed;

	private void ToggleDynamicIsland_Click(object sender, MouseButtonEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			_userForcedClockView = !_userForcedClockView;
			OnPropertyChanged(nameof(DynamicIslandVisibility));
			OnPropertyChanged(nameof(AmbientClockVisibility));
			e.Handled = true;
		}
		catch { }
	}

	public string CurrentSearchMode => _searchMode;

	public Brush ModeAppsBrush
	{
		get
		{
			return _modeAppsBrush;
		}
		set
		{
			_modeAppsBrush = value;
			OnPropertyChanged("ModeAppsBrush");
		}
	}

	public Brush ModeFilesBrush
	{
		get
		{
			return _modeFilesBrush;
		}
		set
		{
			_modeFilesBrush = value;
			OnPropertyChanged("ModeFilesBrush");
		}
	}

	public Brush ModeClipboardBrush
	{
		get
		{
			return _modeClipboardBrush;
		}
		set
		{
			_modeClipboardBrush = value;
			OnPropertyChanged("ModeClipboardBrush");
		}
	}

	public Brush ModeAiBrush
	{
		get
		{
			return _modeAiBrush;
		}
		set
		{
			_modeAiBrush = value;
			OnPropertyChanged("ModeAiBrush");
		}
	}

	public Brush ModeAppsForegroundBrush
	{
		get
		{
			return _modeAppsForegroundBrush;
		}
		set
		{
			_modeAppsForegroundBrush = value;
			OnPropertyChanged("ModeAppsForegroundBrush");
		}
	}

	public Brush ModeFilesForegroundBrush
	{
		get
		{
			return _modeFilesForegroundBrush;
		}
		set
		{
			_modeFilesForegroundBrush = value;
			OnPropertyChanged("ModeFilesForegroundBrush");
		}
	}

	public Brush ModeClipboardForegroundBrush
	{
		get
		{
			return _modeClipboardForegroundBrush;
		}
		set
		{
			_modeClipboardForegroundBrush = value;
			OnPropertyChanged("ModeClipboardForegroundBrush");
		}
	}

	public Brush ModeAiForegroundBrush
	{
		get
		{
			return _modeAiForegroundBrush;
		}
		set
		{
			_modeAiForegroundBrush = value;
			OnPropertyChanged("ModeAiForegroundBrush");
		}
	}

	public Brush SpotlightBgBrush
	{
		get
		{
			return _spotlightBgBrush;
		}
		set
		{
			_spotlightBgBrush = value;
			OnPropertyChanged("SpotlightBgBrush");
		}
	}

	public Brush SpotlightBorderBrush
	{
		get
		{
			return _spotlightBorderBrush;
		}
		set
		{
			_spotlightBorderBrush = value;
			OnPropertyChanged("SpotlightBorderBrush");
		}
	}

	public Thickness SpotlightBorderThickness
	{
		get
		{
			return _spotlightBorderThickness;
		}
		set
		{
			_spotlightBorderThickness = value;
			OnPropertyChanged("SpotlightBorderThickness");
		}
	}

	public Brush SpotlightTextBrush
	{
		get
		{
			return _spotlightTextBrush;
		}
		set
		{
			_spotlightTextBrush = value;
			OnPropertyChanged("SpotlightTextBrush");
		}
	}

	public Brush SpotlightPlaceholderBrush
	{
		get
		{
			return _spotlightPlaceholderBrush;
		}
		set
		{
			_spotlightPlaceholderBrush = value;
			OnPropertyChanged("SpotlightPlaceholderBrush");
		}
	}

	public Brush SpotlightIconBrush
	{
		get
		{
			return _spotlightIconBrush;
		}
		set
		{
			_spotlightIconBrush = value;
			OnPropertyChanged("SpotlightIconBrush");
		}
	}

	public Brush SpotlightSelectedBorderBrush
	{
		get
		{
			return _spotlightSelectedBorderBrush;
		}
		set
		{
			_spotlightSelectedBorderBrush = value;
			OnPropertyChanged("SpotlightSelectedBorderBrush");
		}
	}

	public Brush SpotlightSelectedBackgroundBrush
	{
		get
		{
			return _spotlightSelectedBackgroundBrush;
		}
		set
		{
			_spotlightSelectedBackgroundBrush = value;
			OnPropertyChanged("SpotlightSelectedBackgroundBrush");
		}
	}

	public Brush SpotlightHoverBackgroundBrush
	{
		get
		{
			return _spotlightHoverBackgroundBrush;
		}
		set
		{
			_spotlightHoverBackgroundBrush = value;
			OnPropertyChanged("SpotlightHoverBackgroundBrush");
		}
	}

	public Brush SpotlightHoverBorderBrush
	{
		get
		{
			return _spotlightHoverBorderBrush;
		}
		set
		{
			_spotlightHoverBorderBrush = value;
			OnPropertyChanged("SpotlightHoverBorderBrush");
		}
	}

	public Brush SpotlightItemBackgroundBrush
	{
		get
		{
			return _spotlightItemBackgroundBrush;
		}
		set
		{
			_spotlightItemBackgroundBrush = value;
			OnPropertyChanged("SpotlightItemBackgroundBrush");
		}
	}

	public Brush SpotlightItemForegroundBrush
	{
		get
		{
			return _spotlightItemForegroundBrush;
		}
		set
		{
			_spotlightItemForegroundBrush = value;
			OnPropertyChanged("SpotlightItemForegroundBrush");
		}
	}

	public Brush SpotlightSecondaryTextBrush
	{
		get
		{
			return _spotlightSecondaryTextBrush;
		}
		set
		{
			_spotlightSecondaryTextBrush = value;
			OnPropertyChanged("SpotlightSecondaryTextBrush");
		}
	}

	public Brush SpotlightSeparatorBrush
	{
		get
		{
			return _spotlightSeparatorBrush;
		}
		set
		{
			_spotlightSeparatorBrush = value;
			OnPropertyChanged("SpotlightSeparatorBrush");
		}
	}

	public Brush SpotlightSearchFocusBrush
	{
		get
		{
			return _spotlightSearchFocusBrush;
		}
		set
		{
			_spotlightSearchFocusBrush = value;
			OnPropertyChanged("SpotlightSearchFocusBrush");
		}
	}

	public double SpotlightWidth
	{
		get
		{
			return _spotlightWidth;
		}
		set
		{
			_spotlightWidth = value;
			OnPropertyChanged("SpotlightWidth");
		}
	}

	public CornerRadius SpotlightCornerRadius
	{
		get
		{
			return _spotlightCornerRadius;
		}
		set
		{
			_spotlightCornerRadius = value;
			OnPropertyChanged("SpotlightCornerRadius");
		}
	}

	public Visibility SpotlightFavoritesVisibility
	{
		get
		{
			return _spotlightFavoritesVisibility;
		}
		set
		{
			_spotlightFavoritesVisibility = value;
			OnPropertyChanged("SpotlightFavoritesVisibility");
		}
	}

	public bool IsWebSearchSelected
	{
		get
		{
			return _isWebSearchSelected;
		}
		set
		{
			if (_isWebSearchSelected != value)
			{
				_isWebSearchSelected = value;
				OnPropertyChanged("IsWebSearchSelected");
			}
		}
	}

	public double SpotlightContainerWidth
	{
		get
		{
			return _spotlightContainerWidth;
		}
		set
		{
			_spotlightContainerWidth = value;
			OnPropertyChanged("SpotlightContainerWidth");
		}
	}

	public Visibility BentoVisibility
	{
		get
		{
			return _bentoVisibility;
		}
		set
		{
			_bentoVisibility = value;
			OnPropertyChanged("BentoVisibility");
		}
	}

	public string BentoWeatherTemp
	{
		get
		{
			return _bentoWeatherTemp;
		}
		set
		{
			_bentoWeatherTemp = value;
			OnPropertyChanged("BentoWeatherTemp");
		}
	}

	public string BentoWeatherCity
	{
		get
		{
			return _bentoWeatherCity;
		}
		set
		{
			_bentoWeatherCity = value;
			OnPropertyChanged("BentoWeatherCity");
		}
	}

	public string BentoWeatherCondition
	{
		get
		{
			return _bentoWeatherCondition;
		}
		set
		{
			_bentoWeatherCondition = value;
			OnPropertyChanged("BentoWeatherCondition");
		}
	}

	public string BentoWeatherGlyph
	{
		get
		{
			return _bentoWeatherGlyph;
		}
		set
		{
			_bentoWeatherGlyph = value;
			OnPropertyChanged("BentoWeatherGlyph");
		}
	}

	public string BentoWeatherDate
	{
		get
		{
			return _bentoWeatherDate;
		}
		set
		{
			_bentoWeatherDate = value;
			OnPropertyChanged("BentoWeatherDate");
		}
	}

	public string BentoClipboardSnippet
	{
		get
		{
			return _bentoClipboardSnippet;
		}
		set
		{
			_bentoClipboardSnippet = value;
			OnPropertyChanged("BentoClipboardSnippet");
		}
	}

	public string BentoClipboardType
	{
		get
		{
			return _bentoClipboardType;
		}
		set
		{
			_bentoClipboardType = value;
			OnPropertyChanged("BentoClipboardType");
		}
	}

	public ImageSource? BentoWallpaperSource
	{
		get
		{
			return _bentoWallpaperSource;
		}
		set
		{
			_bentoWallpaperSource = value;
			OnPropertyChanged("BentoWallpaperSource");
		}
	}

	public string BentoWallpaperName
	{
		get
		{
			return _bentoWallpaperName;
		}
		set
		{
			_bentoWallpaperName = value;
			OnPropertyChanged("BentoWallpaperName");
		}
	}

	public string BentoWallpaperResolution
	{
		get
		{
			return _bentoWallpaperResolution;
		}
		set
		{
			_bentoWallpaperResolution = value;
			OnPropertyChanged("BentoWallpaperResolution");
		}
	}

	public PinnedApp? InspectorSelectedItem
	{
		get
		{
			return _inspectorSelectedItem;
		}
		set
		{
			_inspectorSelectedItem = value;
			OnPropertyChanged("InspectorSelectedItem");
		}
	}

	public string InspectorTitle
	{
		get
		{
			return _inspectorTitle;
		}
		set
		{
			_inspectorTitle = value;
			OnPropertyChanged("InspectorTitle");
		}
	}

	public string InspectorSubtitle
	{
		get
		{
			return _inspectorSubtitle;
		}
		set
		{
			_inspectorSubtitle = value;
			OnPropertyChanged("InspectorSubtitle");
		}
	}

	public string InspectorPath
	{
		get
		{
			return _inspectorPath;
		}
		set
		{
			_inspectorPath = value;
			OnPropertyChanged("InspectorPath");
		}
	}

	public ImageSource? InspectorImageSource
	{
		get
		{
			return _inspectorImageSource;
		}
		set
		{
			_inspectorImageSource = value;
			OnPropertyChanged("InspectorImageSource");
		}
	}

	public string InspectorFileSize
	{
		get
		{
			return _inspectorFileSize;
		}
		set
		{
			_inspectorFileSize = value;
			OnPropertyChanged("InspectorFileSize");
		}
	}

	public string InspectorDimensions
	{
		get
		{
			return _inspectorDimensions;
		}
		set
		{
			_inspectorDimensions = value;
			OnPropertyChanged("InspectorDimensions");
			OnPropertyChanged("InspectorImageDimensions");
		}
	}

	public string InspectorImageDimensions => InspectorDimensions;

	public string InspectorModifiedDate
	{
		get
		{
			return _inspectorModifiedDate;
		}
		set
		{
			_inspectorModifiedDate = value;
			OnPropertyChanged("InspectorModifiedDate");
		}
	}

	public string InspectorCodeSnippet
	{
		get
		{
			return _inspectorCodeSnippet;
		}
		set
		{
			_inspectorCodeSnippet = value;
			OnPropertyChanged("InspectorCodeSnippet");
		}
	}

	public string InspectorCodeLines
	{
		get
		{
			return _inspectorCodeLines;
		}
		set
		{
			_inspectorCodeLines = value;
			OnPropertyChanged("InspectorCodeLines");
			OnPropertyChanged("InspectorCodeLinesCount");
		}
	}

	public string InspectorCodeLinesCount => InspectorCodeLines;

	public string InspectorCodeLanguage
	{
		get
		{
			return _inspectorCodeLanguage;
		}
		set
		{
			_inspectorCodeLanguage = value;
			OnPropertyChanged("InspectorCodeLanguage");
		}
	}

	public ImageSource? InspectorAppIcon
	{
		get
		{
			ImageSource? imageSource = _inspectorAppIcon;
			if (imageSource == null)
			{
				PinnedApp? inspectorSelectedItem = InspectorSelectedItem;
				if (inspectorSelectedItem == null)
				{
					return null;
				}
				imageSource = inspectorSelectedItem.Icon;
			}
			return imageSource;
		}
		set
		{
			_inspectorAppIcon = value;
			OnPropertyChanged("InspectorAppIcon");
		}
	}

	public string InspectorAppName
	{
		get
		{
			if (!string.IsNullOrEmpty(_inspectorAppName))
			{
				return _inspectorAppName;
			}
			return InspectorTitle;
		}
		set
		{
			_inspectorAppName = value;
			OnPropertyChanged("InspectorAppName");
		}
	}

	public string InspectorAppStatus
	{
		get
		{
			return _inspectorAppStatus;
		}
		set
		{
			_inspectorAppStatus = value;
			OnPropertyChanged("InspectorAppStatus");
		}
	}

	public string InspectorAppRunningStatus
	{
		get
		{
			return _inspectorAppStatus;
		}
		set
		{
			_inspectorAppStatus = value;
			OnPropertyChanged("InspectorAppRunningStatus");
			OnPropertyChanged("InspectorAppStatus");
		}
	}

	public string InspectorAppRamUsage
	{
		get
		{
			return _inspectorAppRamUsage;
		}
		set
		{
			_inspectorAppRamUsage = value;
			OnPropertyChanged("InspectorAppRamUsage");
		}
	}

	public string InspectorAppPid
	{
		get
		{
			return _inspectorAppPid;
		}
		set
		{
			_inspectorAppPid = value;
			OnPropertyChanged("InspectorAppPid");
		}
	}

	public string InspectorWebQuery
	{
		get
		{
			return _inspectorWebQuery;
		}
		set
		{
			_inspectorWebQuery = value;
			OnPropertyChanged("InspectorWebQuery");
		}
	}

	public bool InspectorIsAppRunning
	{
		get
		{
			return _inspectorIsAppRunning;
		}
		set
		{
			_inspectorIsAppRunning = value;
			OnPropertyChanged("InspectorIsAppRunning");
		}
	}

	public string InspectorFullClipboardText
	{
		get
		{
			return _inspectorFullClipboardText;
		}
		set
		{
			_inspectorFullClipboardText = value;
			OnPropertyChanged("InspectorFullClipboardText");
		}
	}

	public string InspectorClipboardWordCount
	{
		get
		{
			return _inspectorClipboardWordCount;
		}
		set
		{
			_inspectorClipboardWordCount = value;
			OnPropertyChanged("InspectorClipboardWordCount");
		}
	}

	public Brush? InspectorColorPreviewBrush
	{
		get
		{
			return _inspectorColorPreviewBrush;
		}
		set
		{
			_inspectorColorPreviewBrush = value;
			OnPropertyChanged("InspectorColorPreviewBrush");
		}
	}

	public bool InspectorIsColor
	{
		get
		{
			return _inspectorIsColor;
		}
		set
		{
			_inspectorIsColor = value;
			OnPropertyChanged("InspectorIsColor");
		}
	}

	public bool InspectorIsLink
	{
		get
		{
			return _inspectorIsLink;
		}
		set
		{
			_inspectorIsLink = value;
			OnPropertyChanged("InspectorIsLink");
		}
	}

	public ImageSource? InspectorClipboardImageSource
	{
		get => _inspectorClipboardImageSource;
		set
		{
			_inspectorClipboardImageSource = value;
			OnPropertyChanged(nameof(InspectorClipboardImageSource));
		}
	}

	public bool InspectorHasClipboardImage
	{
		get => _inspectorHasClipboardImage;
		set
		{
			_inspectorHasClipboardImage = value;
			OnPropertyChanged(nameof(InspectorHasClipboardImage));
		}
	}

	public Visibility InspectorClipboardImageViewVisibility
	{
		get => _inspectorClipboardImageViewVisibility;
		set
		{
			_inspectorClipboardImageViewVisibility = value;
			OnPropertyChanged(nameof(InspectorClipboardImageViewVisibility));
		}
	}

	public Visibility InspectorClipboardTextViewVisibility
	{
		get => _inspectorClipboardTextViewVisibility;
		set
		{
			_inspectorClipboardTextViewVisibility = value;
			OnPropertyChanged(nameof(InspectorClipboardTextViewVisibility));
		}
	}

	public string InspectorClipboardImageDimensions
	{
		get => _inspectorClipboardImageDimensions;
		set
		{
			_inspectorClipboardImageDimensions = value;
			OnPropertyChanged(nameof(InspectorClipboardImageDimensions));
		}
	}

	public string InspectorClipboardImageDetails
	{
		get => _inspectorClipboardImageDetails;
		set
		{
			_inspectorClipboardImageDetails = value;
			OnPropertyChanged(nameof(InspectorClipboardImageDetails));
		}
	}

	public string InspectorClipboardCopyButtonText
	{
		get => _inspectorClipboardCopyButtonText;
		set
		{
			_inspectorClipboardCopyButtonText = value;
			OnPropertyChanged(nameof(InspectorClipboardCopyButtonText));
		}
	}

	public string InspectorAiResponse
	{
		get
		{
			return _inspectorAiResponse;
		}
		set
		{
			_inspectorAiResponse = value;
			OnPropertyChanged("InspectorAiResponse");
		}
	}

	public Visibility InspectorFileViewVisibility
	{
		get
		{
			return _inspectorFileViewVisibility;
		}
		set
		{
			_inspectorFileViewVisibility = value;
			OnPropertyChanged("InspectorFileViewVisibility");
			OnPropertyChanged("InspectorImageViewVisibility");
		}
	}

	public Visibility InspectorImageViewVisibility => InspectorFileViewVisibility;

	public Visibility InspectorCodeViewVisibility
	{
		get
		{
			return _inspectorCodeViewVisibility;
		}
		set
		{
			_inspectorCodeViewVisibility = value;
			OnPropertyChanged("InspectorCodeViewVisibility");
		}
	}

	public Visibility InspectorAppViewVisibility
	{
		get
		{
			return _inspectorAppViewVisibility;
		}
		set
		{
			_inspectorAppViewVisibility = value;
			OnPropertyChanged("InspectorAppViewVisibility");
		}
	}

	public Visibility InspectorClipboardViewVisibility
	{
		get
		{
			return _inspectorClipboardViewVisibility;
		}
		set
		{
			_inspectorClipboardViewVisibility = value;
			OnPropertyChanged("InspectorClipboardViewVisibility");
		}
	}

	public Visibility InspectorAiViewVisibility
	{
		get
		{
			return _inspectorAiViewVisibility;
		}
		set
		{
			_inspectorAiViewVisibility = value;
			OnPropertyChanged("InspectorAiViewVisibility");
		}
	}

	public Visibility InspectorWebViewVisibility
	{
		get
		{
			return _inspectorWebViewVisibility;
		}
		set
		{
			_inspectorWebViewVisibility = value;
			OnPropertyChanged("InspectorWebViewVisibility");
		}
	}

	public Visibility InspectorDefaultViewVisibility
	{
		get
		{
			return _inspectorDefaultViewVisibility;
		}
		set
		{
			_inspectorDefaultViewVisibility = value;
			OnPropertyChanged("InspectorDefaultViewVisibility");
		}
	}

	public Visibility InspectorMusicViewVisibility
	{
		get
		{
			return _inspectorMusicViewVisibility;
		}
		set
		{
			_inspectorMusicViewVisibility = value;
			OnPropertyChanged("InspectorMusicViewVisibility");
		}
	}

	public string InspectorMusicTitle
	{
		get
		{
			return _inspectorMusicTitle;
		}
		set
		{
			_inspectorMusicTitle = value;
			OnPropertyChanged("InspectorMusicTitle");
		}
	}

	public string InspectorMusicSubtitle
	{
		get
		{
			return _inspectorMusicSubtitle;
		}
		set
		{
			_inspectorMusicSubtitle = value;
			OnPropertyChanged("InspectorMusicSubtitle");
		}
	}

	public string InspectorMusicQuery
	{
		get
		{
			return _inspectorMusicQuery;
		}
		set
		{
			_inspectorMusicQuery = value;
			OnPropertyChanged("InspectorMusicQuery");
		}
	}

	public string InspectorMusicStatus
	{
		get
		{
			return _inspectorMusicStatus;
		}
		set
		{
			_inspectorMusicStatus = value;
			OnPropertyChanged("InspectorMusicStatus");
		}
	}

	public ImageSource? InspectorMusicArtwork
	{
		get
		{
			return _inspectorMusicArtwork;
		}
		set
		{
			_inspectorMusicArtwork = value;
			OnPropertyChanged("InspectorMusicArtwork");
		}
	}

	public string InspectorMusicDuration
	{
		get
		{
			return _inspectorMusicDuration;
		}
		set
		{
			_inspectorMusicDuration = value;
			OnPropertyChanged("InspectorMusicDuration");
		}
	}

	public Visibility InspectorMusicDurationVisibility
	{
		get
		{
			return _inspectorMusicDurationVisibility;
		}
		set
		{
			_inspectorMusicDurationVisibility = value;
			OnPropertyChanged("InspectorMusicDurationVisibility");
		}
	}

	public Visibility InspectorMusicEqualizerVisibility
	{
		get
		{
			return _inspectorMusicEqualizerVisibility;
		}
		set
		{
			_inspectorMusicEqualizerVisibility = value;
			OnPropertyChanged("InspectorMusicEqualizerVisibility");
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public void Open()
	{
		if (FavoritesList.Count <= 1)
		{
			LoadFavoritesImmediately();
		}
		Show();
		Activate();
		ForceForeground();
		FocusSearchBox();
		UpdateClockAndDate();
		LoadActiveWindows();
		UpdateSatelliteMediaInfo();
		_ = LoadWeatherSnippetAsync();
		_ = LoadNotificationsAsync();
		_ = LoadConnectedDevicesAsync();
	}

	public void CloseWidget()
	{
		SafeClose();
	}

	private bool IsWebSearchAvailable()
	{
		if (WebSearchButton != null && WebSearchButton.Visibility == Visibility.Visible && (_searchMode == "Apps" || _searchMode == "Files"))
		{
			return !string.IsNullOrWhiteSpace(SearchBox?.Text);
		}
		return false;
	}

	public PinnedAppsWindow()
	{
		InitializeComponent();
		ApplySettings();
		UpdateWindowSize();
		DataContext = this;
		Activated += Window_Activated;
		LoadFavoritesImmediately();
		_searchDebounceTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
		{
			Interval = TimeSpan.FromMilliseconds(40.0)
		};
		_searchDebounceTimer.Tick += (object? s, EventArgs args) =>
		{
			_searchDebounceTimer.Stop();
			if (!(_searchMode == "AI"))
			{
				PerformSearch(SearchBox.Text.Trim());
			}
		};
		_musicDebounceTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
		{
			Interval = TimeSpan.FromMilliseconds(220.0)
		};
		_musicDebounceTimer.Tick += (object? s, EventArgs args) =>
		{
			_musicDebounceTimer.Stop();
			PerformSearch(SearchBox.Text.Trim());
		};
		UpdateModeButtonsVisuals();
		ClipboardService.Instance.HistoryChanged += OnClipboardHistoryChanged;
		EventBus.Subscribe<WallpaperChangedEvent>(OnWallpaperChanged);
		UpdateClockAndDate();
		_clockTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1.0)
		};
		_clockTimer.Tick += (object? s, EventArgs args) =>
		{
			UpdateClockAndDate();
		};
		_clockTimer.Start();
		Closed += (object? s, EventArgs ev) =>
		{
			_clockTimer?.Stop();
			ClipboardService.Instance.HistoryChanged -= OnClipboardHistoryChanged;
			EventBus.Unsubscribe<WallpaperChangedEvent>(OnWallpaperChanged);
		};
		Task.Run(() => _discoveryService.DiscoverInstalledAppsAsync());
	}

	public static void PreloadPinnedIcons()
	{
		IconExtractionService.Instance.PreloadPinnedIcons();
	}

	public void ApplySettings()
	{
		SpotlightThemeBrushes spotlightThemeBrushes = _themeService.ComputeTheme(SettingsService.Instance, System.Windows.Application.Current?.Resources);
		SpotlightWidth = spotlightThemeBrushes.Width;
		SpotlightCornerRadius = spotlightThemeBrushes.CornerRadius;
		SpotlightFavoritesVisibility = spotlightThemeBrushes.FavoritesVisibility;
		SpotlightBgBrush = spotlightThemeBrushes.BgBrush;
		SpotlightBorderBrush = spotlightThemeBrushes.BorderBrush;
		SpotlightBorderThickness = spotlightThemeBrushes.BorderThickness;
		SpotlightTextBrush = spotlightThemeBrushes.TextBrush;
		SpotlightPlaceholderBrush = spotlightThemeBrushes.PlaceholderBrush;
		SpotlightIconBrush = spotlightThemeBrushes.IconBrush;
		SpotlightSelectedBorderBrush = spotlightThemeBrushes.SelectedBorderBrush;
		SpotlightSelectedBackgroundBrush = spotlightThemeBrushes.SelectedBackgroundBrush;
		SpotlightHoverBackgroundBrush = spotlightThemeBrushes.HoverBackgroundBrush;
		SpotlightHoverBorderBrush = spotlightThemeBrushes.HoverBorderBrush;
		SpotlightItemBackgroundBrush = spotlightThemeBrushes.ItemBackgroundBrush;
		SpotlightItemForegroundBrush = spotlightThemeBrushes.ItemForegroundBrush;
		SpotlightSecondaryTextBrush = spotlightThemeBrushes.SecondaryTextBrush;
		SpotlightSeparatorBrush = spotlightThemeBrushes.SeparatorBrush;
		SpotlightSearchFocusBrush = spotlightThemeBrushes.SearchFocusBrush;
		UpdateModeButtonsVisuals();
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
	}

	private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg == 274 && (((IntPtr)wParam).ToInt32() & 0xFFF0) == 61696)
		{
			handled = true;
			return IntPtr.Zero;
		}
		return IntPtr.Zero;
	}

	private void FocusSearchBox()
	{
		if (SearchBox != null && !SearchBox.IsFocused)
		{
			SearchBox.Focus();
			Keyboard.Focus(SearchBox);
		}
	}

	private async void Window_Loaded(object sender, RoutedEventArgs e)
	{
		try
		{
			FloatingMediaOrbWindow.HideOrb();
		}
		catch
		{
		}
		_loadedTime = DateTime.Now;
		try
		{
			BackdropHelper.ApplyAcrylic(this, roundCorners: false);
		}
		catch
		{
		}
		try
		{
			UpdateClockAndDate();
		}
		catch
		{
		}
		try
		{
			_ = LoadWeatherSnippetAsync();
		}
		catch
		{
		}
		try
		{
			LoadActiveWindows();
		}
		catch
		{
		}
		try
		{
			InitSatelliteMedia();
		}
		catch
		{
		}
		try
		{
			UpdateSatelliteMediaInfo();
		}
		catch
		{
		}
		try
		{
			UpdateWindowSize();
		}
		catch
		{
		}
		try
		{
			_ = LoadNotificationsAsync();
		}
		catch
		{
		}
		try
		{
			_ = LoadConnectedDevicesAsync();
		}
		catch
		{
		}
		try
		{
			ForceForeground();
		}
		catch
		{
		}
		try
		{
			FocusSearchBox();
		}
		catch
		{
		}
		try
		{
			SearchBox.CaretIndex = SearchBox.Text.Length;
		}
		catch
		{
		}
		try
		{
			DoubleAnimation animation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150.0))
			{
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			BeginAnimation(UIElement.OpacityProperty, animation);
		}
		catch
		{
		}
		if (ApplicationHost.Instance != null)
		{
			try
			{
				ApplicationHost.Instance.IsPinnedFlyoutOpen = true;
				ApplicationHost.Instance.InternalMusicStateChanged -= OnInternalMusicStateChanged;
				ApplicationHost.Instance.InternalMusicStateChanged += OnInternalMusicStateChanged;
			}
			catch
			{
			}
		}
		try
		{
			_ = LoadActiveWallpaperAsync();
		}
		catch
		{
		}
		try
		{
			await LoadAppsAsync();
		}
		catch
		{
		}
	}

	private void OnInternalMusicStateChanged(object? sender, EventArgs e)
	{
		Dispatcher.BeginInvoke(new Action(UpdateSatelliteMediaInfo));
	}

	private void Window_Activated(object? sender, EventArgs e)
	{
		if (FavoritesList.Count <= 1)
		{
			LoadFavoritesImmediately();
		}
		ForceForeground();
		FocusSearchBox();
		UpdateClockAndDate();
		_ = LoadWeatherSnippetAsync();
		if (string.IsNullOrWhiteSpace(SearchBox?.Text))
		{
			LoadActiveWindows();
			UpdateSatelliteMediaInfo();
		}
		_ = LoadActiveWallpaperAsync();
	}

	private void UpdateClockAndDate()
	{
		DateTime now = DateTime.Now;
		SpotlightCurrentTime = now.ToString("HH:mm");
		try
		{
			CultureInfo cultureInfo = new CultureInfo("es-ES");
			string text = now.ToString("dddd, d 'de' MMMM", cultureInfo);
			if (!string.IsNullOrEmpty(text))
			{
				SpotlightCurrentDate = char.ToUpper(text[0]) + text.Substring(1);
			}
		}
		catch
		{
			SpotlightCurrentDate = now.ToLongDateString();
		}
		UpdateSystemTelemetry();
	}

	private void UpdateSystemTelemetry()
	{
		try
		{
			MEMORYSTATUSEX mEMORYSTATUSEX = new MEMORYSTATUSEX();
			if (NativeMethods.GlobalMemoryStatusEx(mEMORYSTATUSEX))
			{
				double num = (double)mEMORYSTATUSEX.ullTotalPhys / 1073741824.0;
				double num2 = (double)mEMORYSTATUSEX.ullAvailPhys / 1073741824.0;
				double num3 = Math.Max(0.0, num - num2);
				int num4 = (int)Math.Clamp(Math.Round(num3 / num * 100.0), 0.0, 100.0);
				SpotlightRamPercent = num4;
				SpotlightRamPercentText = $"{num4}%";
				SpotlightRamUsageText = $"{num3:0.0} / {num:0.0} GB";
			}
		}
		catch
		{
		}
		try
		{
			string sysRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
			DriveInfo[] drives = DriveInfo.GetDrives();
			DriveInfo driveInfo = drives.FirstOrDefault((DriveInfo d) => d.IsReady && d.DriveType == DriveType.Fixed && d.RootDirectory.FullName.StartsWith(sysRoot.Substring(0, 1), StringComparison.OrdinalIgnoreCase)) ?? drives.FirstOrDefault((DriveInfo d) => d.IsReady && d.DriveType == DriveType.Fixed);
			if (driveInfo != null)
			{
				double num5 = (double)driveInfo.TotalSize / 1073741824.0;
				double num6 = (double)driveInfo.TotalFreeSpace / 1073741824.0;
				int num7 = (int)Math.Clamp(Math.Round(Math.Max(0.0, num5 - num6) / num5 * 100.0), 0.0, 100.0);
				string text = driveInfo.Name.TrimEnd('\\');
				SpotlightDiskName = "DISCO (" + text + ")";
				SpotlightDiskPercent = num7;
				SpotlightDiskPercentText = $"{num7}%";
				SpotlightDiskUsageText = $"{num6:0} GB libres de {num5:0} GB";
			}
		}
		catch
		{
		}
		try
		{
			if (NativeMethods.GetSystemPowerStatus(out var lpSystemPowerStatus))
			{
				if (lpSystemPowerStatus.BatteryLifePercent <= 100)
				{
					SpotlightBatteryGlyph = (lpSystemPowerStatus.ACLineStatus == 1) ? "\uE945" : "\uE83F";
					SpotlightBatteryPercentText = $"{lpSystemPowerStatus.BatteryLifePercent}%";
					SpotlightBatterySnippet = $"•  {lpSystemPowerStatus.BatteryLifePercent}%";
					HasBatterySnippet = true;
				}
				else
				{
					HasBatterySnippet = false;
				}
			}
		}
		catch
		{
		}
	}

	private async Task LoadWeatherSnippetAsync()
	{
		try
		{
			WeatherData weatherData = await WeatherService.Instance.GetWeatherAsync();
			if (weatherData != null && !string.IsNullOrEmpty(weatherData.Condition))
			{
				SpotlightWeatherSnippet = $"•  {Math.Round(weatherData.Temperature)}°C {weatherData.Condition}";
				HasWeatherSnippet = true;
			}
		}
		catch
		{
			HasWeatherSnippet = false;
		}
	}

	private void ForceForeground()
	{
		try
		{
			nint handle = new WindowInteropHelper(this).Handle;
			if (handle == IntPtr.Zero)
			{
				return;
			}
			nint foregroundWindow = NativeMethods.GetForegroundWindow();
			if (foregroundWindow != handle)
			{
				uint windowThreadProcessId = NativeMethods.GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
				uint currentThreadId = NativeMethods.GetCurrentThreadId();
				if (windowThreadProcessId != currentThreadId && windowThreadProcessId != 0)
				{
					NativeMethods.AttachThreadInput(currentThreadId, windowThreadProcessId, fAttach: true);
					NativeMethods.SetForegroundWindow(handle);
					NativeMethods.AttachThreadInput(currentThreadId, windowThreadProcessId, fAttach: false);
				}
				else
				{
					NativeMethods.SetForegroundWindow(handle);
				}
			}
		}
		catch
		{
		}
	}

	public void LoadFavoritesImmediately()
	{
		List<string> list = SettingsService.Instance.PinnedAppPaths;
		if (list == null || list.Count == 0)
		{
			list = new List<string> { "explorer.exe", "chrome.exe", "notepad.exe", "cmd.exe", "taskmgr.exe" };
			SettingsService.Instance.PinnedAppPaths = list;
			SettingsService.Save();
		}
		List<string> list2 = list.Where((string p) => !string.IsNullOrEmpty(p) && p != "ADD_APP").ToList();
		if ((from f in FavoritesList
			where !string.IsNullOrEmpty(f.Path) && f.Path != "ADD_APP"
			select f.Path).ToList().SequenceEqual(list2, StringComparer.OrdinalIgnoreCase) && FavoritesList.Any((PinnedApp f) => f.Path == "ADD_APP"))
		{
			StartLoadingIcons();
			return;
		}
		FavoritesList.Clear();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string path in list2)
		{
			try
			{
				PinnedApp pinnedApp = _discoveryService.CreatePinnedAppFromPath(path);
				pinnedApp.IsPinned = true;
				FavoritesList.Add(pinnedApp);
				if (hashSet.Add(path) && !_allInstalledApps.Any((PinnedApp a) => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase)))
				{
					_allInstalledApps.Add(pinnedApp);
				}
			}
			catch
			{
			}
		}
		FavoritesList.Add(new PinnedApp
		{
			Name = "Anclar Aplicación",
			Path = "ADD_APP",
			IconGlyph = "\ue6e6"
		});
		StartLoadingIcons();
	}

	private async Task LoadAppsAsync()
	{
		PinnedAppsList.Clear();
		_selectedIndex = -1;
		LoadFavoritesImmediately();
		PopulateDefaultList();
		List<PinnedApp> list = await _discoveryService.DiscoverInstalledAppsAsync();
		if (_isClosing)
		{
			return;
		}
		List<string> pinnedAppPaths = SettingsService.Instance.PinnedAppPaths;
		HashSet<string> hashSet = new HashSet<string>(_allInstalledApps.Select((PinnedApp a) => a.Path), StringComparer.OrdinalIgnoreCase);
		foreach (PinnedApp item in list)
		{
			if (hashSet.Add(item.Path))
			{
				item.IsPinned = pinnedAppPaths?.Contains(item.Path) ?? false;
				_allInstalledApps.Add(item);
			}
		}
		StartLoadingIcons();
	}

	private void StartLoadingIcons()
	{
		List<PinnedApp> favorites = new List<PinnedApp>(FavoritesList);
		List<PinnedApp> apps = new List<PinnedApp>(_allInstalledApps);
		Task.Run(async () =>
		{
			foreach (PinnedApp app in favorites)
			{
				if (_isClosing)
				{
					return;
				}
				if (!(app.Path == "ADD_APP") && app.Icon == null && !app.Path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
				{
					ImageSource? icon = _iconService.LoadIconForApp(app.Path);
					if (icon != null)
					{
						_ = Dispatcher.BeginInvoke((Action)(() =>
						{
							app.Icon = icon;
						}));
					}
				}
			}
			foreach (PinnedApp app2 in apps)
			{
				if (_isClosing)
				{
					return;
				}
				if (app2.Icon == null && !app2.Path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
				{
					ImageSource? icon2 = _iconService.LoadIconForApp(app2.Path);
					if (icon2 != null)
					{
						_ = Dispatcher.BeginInvoke((Action)(() =>
						{
							app2.Icon = icon2;
						}));
					}
					await Task.Delay(5);
				}
			}
		});
	}

	private void PopulateDefaultList()
	{
		PinnedAppsList.Clear();
		_selectedIndex = -1;
		IsWebSearchSelected = false;
		if (ResultsScrollViewer != null)
		{
			ResultsScrollViewer.Visibility = Visibility.Collapsed;
		}
		if (ResultsCardBorder != null)
		{
			ResultsCardBorder.Visibility = Visibility.Collapsed;
		}
		UpdateWindowSize();
	}

	private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_isChangingMode)
		{
			return;
		}
		PlaceholderText.Visibility = ((!string.IsNullOrEmpty(SearchBox.Text)) ? Visibility.Collapsed : Visibility.Visible);
		string value = SearchBox.Text.Trim();
		if (!string.IsNullOrEmpty(value) && WallpaperFanCardVisibility == Visibility.Visible)
		{
			WallpaperFanCardVisibility = Visibility.Collapsed;
		}
		if (BottomOrbitalZone != null)
		{
			BottomOrbitalZone.Opacity = string.IsNullOrEmpty(value) ? 1.0 : 0.35;
		}
		if (string.IsNullOrEmpty(value))
		{
			_searchDebounceTimer?.Stop();
			_musicDebounceTimer?.Stop();
			foreach (PinnedApp pinnedApps in PinnedAppsList)
			{
				pinnedApps.IsSelected = false;
			}
			IsWebSearchSelected = false;
			_selectedIndex = -1;
			UpdateInspector(null);
			if (_searchMode == "Apps")
			{
				PopulateDefaultList();
			}
			else if (_searchMode == "Files")
			{
				PinnedAppsList.Clear();
				_selectedIndex = -1;
				IsWebSearchSelected = false;
				UpdateWindowSize();
			}
			else if (_searchMode == "Clipboard")
			{
				ExecuteClipboardSearch(string.Empty);
			}
			else if (_searchMode == "AI")
			{
				LoadAiHistory();
			}
		}
		else
		{
			if (IsMusicSearchQuery(value))
			{
				_searchDebounceTimer?.Stop();
				_musicDebounceTimer?.Stop();
				_musicDebounceTimer?.Start();
			}
			else if (value.StartsWith("m:", StringComparison.OrdinalIgnoreCase) ||
			         value.StartsWith("music:", StringComparison.OrdinalIgnoreCase) ||
			         value.StartsWith("musica:", StringComparison.OrdinalIgnoreCase) ||
			         value.StartsWith("play:", StringComparison.OrdinalIgnoreCase) ||
			         value.StartsWith("/m ", StringComparison.OrdinalIgnoreCase) ||
			         value.StartsWith("f:", StringComparison.OrdinalIgnoreCase) ||
			         value.StartsWith("c:", StringComparison.OrdinalIgnoreCase) ||
			         value.StartsWith("a:", StringComparison.OrdinalIgnoreCase) ||
			         value.StartsWith("play ", StringComparison.OrdinalIgnoreCase))
			{
				_searchDebounceTimer?.Stop();
				_musicDebounceTimer?.Stop();
				PerformSearch(value);
			}
			else
			{
				_musicDebounceTimer?.Stop();
				_searchDebounceTimer?.Stop();
				_searchDebounceTimer?.Start();
			}
		}
	}

	private static bool IsMusicSearchQuery(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return false;
		string q = text.Trim();
		if (q.StartsWith("m:", StringComparison.OrdinalIgnoreCase)) return q.Length > 2 && !string.IsNullOrWhiteSpace(q.Substring(2));
		if (q.StartsWith("/m ", StringComparison.OrdinalIgnoreCase)) return q.Length > 3 && !string.IsNullOrWhiteSpace(q.Substring(3));
		if (q.StartsWith("music:", StringComparison.OrdinalIgnoreCase)) return q.Length > 6 && !string.IsNullOrWhiteSpace(q.Substring(6));
		if (q.StartsWith("musica:", StringComparison.OrdinalIgnoreCase)) return q.Length > 7 && !string.IsNullOrWhiteSpace(q.Substring(7));
		if (q.StartsWith("play:", StringComparison.OrdinalIgnoreCase)) return q.Length > 5 && !string.IsNullOrWhiteSpace(q.Substring(5));
		if (q.StartsWith("play ", StringComparison.OrdinalIgnoreCase)) return q.Length > 5 && !string.IsNullOrWhiteSpace(q.Substring(5));
		return false;
	}

	private void PerformSearch(string rawQuery)
	{
		if (!string.IsNullOrWhiteSpace(rawQuery))
		{
			QueryRouteResult queryRouteResult = _searchService.RouteQuery(rawQuery, _searchMode);
			if (queryRouteResult.TargetMode == "Files")
			{
				_ = ExecuteFileSearchAsync(queryRouteResult.FilteredQuery);
			}
			else if (queryRouteResult.TargetMode == "AI")
			{
				_ = ExecuteAiSearchAsync(queryRouteResult.FilteredQuery);
			}
			else if (queryRouteResult.TargetMode == "Clipboard")
			{
				ExecuteClipboardSearch(queryRouteResult.FilteredQuery);
			}
			else if (queryRouteResult.TargetMode == "Music")
			{
				_ = ExecuteMusicSearchAsync(queryRouteResult.FilteredQuery);
			}
			else
			{
				ExecuteAppsSearch(queryRouteResult.FilteredQuery);
			}
		}
	}

	private async Task ExecuteMusicSearchAsync(string query)
	{
		_musicSearchCts?.Cancel();
		_musicSearchCts = new CancellationTokenSource();
		CancellationToken token = _musicSearchCts.Token;

		// 1. Inmediatamente en el hilo de interfaz: limpiar selección y conmutar la vista de Spotlight
		foreach (PinnedApp pinnedApps in PinnedAppsList)
		{
			pinnedApps.IsSelected = false;
		}
		PinnedAppsList.Clear();
		_selectedIndex = -1;
		IsWebSearchSelected = false;

		string cleanQuery = (query ?? string.Empty).Trim();
		if (!string.IsNullOrWhiteSpace(cleanQuery))
		{
			PinnedAppsList.Add(new PinnedApp
			{
				Name = $"Reproducir \"{cleanQuery}\"",
				Subtitle = "YouTube Music",
				Path = $"ytmusic:play:{cleanQuery}",
				ActionText = "Reproducir",
				IconGlyph = "\uE197",
				ToolTip = $"Buscar y reproducir {cleanQuery} en YouTube Music"
			});
			PinnedAppsList.Add(new PinnedApp
			{
				Name = $"Buscar \"{cleanQuery}\" en YouTube Web",
				Subtitle = "youtube.com",
				Path = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(cleanQuery)}",
				ActionText = "Web",
				IconGlyph = "\uE0E8",
				ToolTip = "Abrir resultados en el navegador predeterminado"
			});
			_selectedIndex = 0;
			PinnedAppsList[0].IsSelected = true;
			UpdateInspector(PinnedAppsList[0]);
		}
		else
		{
			PinnedAppsList.Add(new PinnedApp
			{
				Name = "Alternar Reproducir / Pausar",
				Subtitle = "YouTube Music",
				Path = "ytmusic:toggle",
				ActionText = "Control",
				IconGlyph = "\uE197",
				ToolTip = "Alternar estado de reproducción"
			});
			PinnedAppsList.Add(new PinnedApp
			{
				Name = "Siguiente Canción",
				Subtitle = "YouTube Music",
				Path = "ytmusic:next",
				ActionText = "Control",
				IconGlyph = "\uE19B",
				ToolTip = "Avanzar a la siguiente pista"
			});
			PinnedAppsList.Add(new PinnedApp
			{
				Name = "Canción Anterior",
				Subtitle = "YouTube Music",
				Path = "ytmusic:prev",
				ActionText = "Control",
				IconGlyph = "\uE19A",
				ToolTip = "Volver a la pista previa"
			});
			PinnedAppsList.Add(new PinnedApp
			{
				Name = "Abrir YouTube Music",
				Subtitle = "Reproductor flotante",
				Path = "ytmusic:open",
				ActionText = "Ventana",
				IconGlyph = "\uE217",
				ToolTip = "Abrir o enfocar el reproductor"
			});
			_selectedIndex = 0;
			PinnedAppsList[0].IsSelected = true;
			UpdateInspector(PinnedAppsList[0]);
		}

		// Oculta "Ventanas Activas" y despliega la tarjeta de resultados al instante (0 ms de demora)
		UpdateWindowSize();

		try
		{
			bool hasFinalResults = false;
			List<PinnedApp> results = await _searchService.SearchMusicAsync(query, onSuggestionsReady: (suggestionsList) =>
			{
				Dispatcher.Invoke(() =>
				{
					if (token.IsCancellationRequested || hasFinalResults) return;
					foreach (PinnedApp pinnedApps in PinnedAppsList)
					{
						pinnedApps.IsSelected = false;
					}
					PinnedAppsList.Clear();
					_selectedIndex = -1;
					IsWebSearchSelected = false;
					foreach (PinnedApp item in suggestionsList)
					{
						PinnedAppsList.Add(item);
					}
					UpdateWindowSize();
					if (PinnedAppsList.Count > 0)
					{
						_selectedIndex = 0;
						PinnedAppsList[0].IsSelected = true;
						UpdateInspector(PinnedAppsList[0]);
					}
				});
			}, token);

			Dispatcher.Invoke(() =>
			{
				if (!token.IsCancellationRequested && results != null && results.Count > 0)
				{
					hasFinalResults = true;
					foreach (PinnedApp pinnedApps in PinnedAppsList)
					{
						pinnedApps.IsSelected = false;
					}
					PinnedAppsList.Clear();
					_selectedIndex = -1;
					IsWebSearchSelected = false;
					foreach (PinnedApp item in results)
					{
						PinnedAppsList.Add(item);
					}
					UpdateWindowSize();
					if (PinnedAppsList.Count > 0)
					{
						_selectedIndex = 0;
						PinnedAppsList[0].IsSelected = true;
						IsWebSearchSelected = false;
						UpdateInspector(PinnedAppsList[0]);
					}
					else
					{
						UpdateInspector(null);
					}
				}
			});
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
		}
	}

	private void ExecuteAppsSearch(string query)
	{
		foreach (PinnedApp pinnedApps in PinnedAppsList)
		{
			pinnedApps.IsSelected = false;
		}
		PinnedAppsList.Clear();
		_selectedIndex = -1;
		IsWebSearchSelected = false;
		if (string.IsNullOrEmpty(query))
		{
			UpdateWindowSize();
			return;
		}
		foreach (PinnedApp item in _searchService.SearchApps(query, _allInstalledApps, SettingsService.Instance.AppLaunchCounts))
		{
			PinnedAppsList.Add(item);
		}
		UpdateWindowSize();
		if (PinnedAppsList.Count > 0)
		{
			_selectedIndex = 0;
			PinnedAppsList[0].IsSelected = true;
			IsWebSearchSelected = false;
			UpdateInspector(PinnedAppsList[0]);
		}
		else if (IsWebSearchAvailable())
		{
			_selectedIndex = 0;
			IsWebSearchSelected = true;
			UpdateInspector(null);
		}
		else
		{
			UpdateInspector(null);
		}
	}

	private async Task ExecuteFileSearchAsync(string query)
	{
		_fileSearchCts?.Cancel();
		_fileSearchCts = new CancellationTokenSource();
		CancellationToken token = _fileSearchCts.Token;
		if (string.IsNullOrWhiteSpace(query))
		{
			Dispatcher.Invoke(() =>
			{
				PinnedAppsList.Clear();
				_selectedIndex = -1;
				IsWebSearchSelected = false;
				UpdateInspector(null);
				UpdateWindowSize();
			});
			return;
		}
		try
		{
			List<PinnedApp> matchedFiles = await _searchService.SearchFilesAsync(query, 25, token);
			Dispatcher.Invoke(() =>
			{
				if (!token.IsCancellationRequested)
				{
					foreach (PinnedApp pinnedApps in PinnedAppsList)
					{
						pinnedApps.IsSelected = false;
					}
					PinnedAppsList.Clear();
					_selectedIndex = -1;
					IsWebSearchSelected = false;
					foreach (PinnedApp item in matchedFiles)
					{
						PinnedAppsList.Add(item);
					}
					UpdateWindowSize();
					if (PinnedAppsList.Count > 0)
					{
						_selectedIndex = 0;
						PinnedAppsList[0].IsSelected = true;
						IsWebSearchSelected = false;
						UpdateInspector(PinnedAppsList[0]);
					}
					else if (IsWebSearchAvailable())
					{
						_selectedIndex = 0;
						IsWebSearchSelected = true;
						UpdateInspector(null);
					}
					else
					{
						UpdateInspector(null);
					}
				}
			});
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
		}
	}

	private void ExecuteClipboardSearch(string query)
	{
		foreach (PinnedApp pinnedApps in PinnedAppsList)
		{
			pinnedApps.IsSelected = false;
		}
		PinnedAppsList.Clear();
		_selectedIndex = -1;
		IsWebSearchSelected = false;
		foreach (PinnedApp item in _searchService.SearchClipboard(query, ClipboardService.Instance.History))
		{
			PinnedAppsList.Add(item);
		}
		UpdateWindowSize();
	}

	private async Task ExecuteAiSearchAsync(string prompt)
	{
		if (_searchMode != "AI")
		{
			SetSearchMode("AI");
		}
		if (!string.IsNullOrWhiteSpace(prompt))
		{
			await SendAiChatMessageAsync(prompt);
		}
	}

	private async Task SendAiChatMessageAsync(string prompt)
	{
		if (string.IsNullOrWhiteSpace(prompt))
		{
			return;
		}

		string apiKey = SettingsService.Instance.GeminiApiKey ?? string.Empty;
		if (string.IsNullOrWhiteSpace(apiKey))
		{
			AiChatMessages.Add(new AiChatMessage
			{
				IsUser = false,
				Content = "**API Key de Gemini no configurada**\n\nPara conversar con la IA en tiempo real, configura tu clave gratuita de Google AI Studio en la configuración de WallFlow."
			});
			UpdateAiChatVisibilities();
			ScrollAiChatToBottom();
			return;
		}

		AiChatMessage userMessage = new AiChatMessage
		{
			IsUser = true,
			Content = prompt.Trim(),
			Timestamp = DateTime.Now
		};
		AiChatMessages.Add(userMessage);

		AiChatMessage aiMessage = new AiChatMessage
		{
			IsUser = false,
			Content = string.Empty,
			IsLoading = true,
			Timestamp = DateTime.Now
		};
		AiChatMessages.Add(aiMessage);
		UpdateAiChatVisibilities();
		ScrollAiChatToBottom();
		SoundService.Play(SoundType.Click);

		try
		{
			List<(string role, string text)> history = new List<(string role, string text)>();
			foreach (AiChatMessage msg in AiChatMessages)
			{
				if (!msg.IsLoading && !string.IsNullOrWhiteSpace(msg.Content))
				{
					history.Add((msg.IsUser ? "user" : "model", msg.Content));
				}
			}

			string response = await _geminiService.QueryChatAsync(
				history,
				apiKey,
				SettingsService.Instance.GeminiModel
			);

			aiMessage.IsLoading = false;
			aiMessage.Content = response;
			aiMessage.Timestamp = DateTime.Now;

			SoundService.Play(SoundType.Ping);

			try
			{
				ClipboardService.Instance.AddAiItem(response);
			}
			catch { }
		}
		catch (Exception ex)
		{
			aiMessage.IsLoading = false;
			aiMessage.Content = "**Error al conectar con Gemini AI:** " + ex.Message + "\n\n*Verifica tu conexión a internet o tu API Key en la configuración.*";
		}

		UpdateAiChatVisibilities();
		ScrollAiChatToBottom();
	}

	private void ScrollAiChatToBottom()
	{
		Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
		{
			AiChatScrollViewer?.ScrollToEnd();
		}));
	}

	private void AiNewChat_Click(object sender, RoutedEventArgs e)
	{
		AiChatMessages.Clear();
		UpdateAiChatVisibilities();
		SoundService.Play(SoundType.Click);
		if (SearchBox != null)
		{
			SearchBox.Text = string.Empty;
			SearchBox.Focus();
		}
	}

	private void AiSuggestionChip_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.Button { Tag: string prompt })
		{
			if (SearchBox != null)
			{
				SearchBox.Text = string.Empty;
			}
			_ = SendAiChatMessageAsync(prompt);
		}
	}

	private void AiCopyMessage_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.Button { Tag: string text } && !string.IsNullOrWhiteSpace(text))
		{
			try
			{
				System.Windows.Clipboard.SetText(text);
				SoundService.Play(SoundType.Click);
			}
			catch { }
		}
	}

	private void LoadAiHistory()
	{
		IsWebSearchSelected = false;
		PinnedAppsList.Clear();
		_selectedIndex = -1;
		if (ResultsCardBorder != null)
		{
			ResultsCardBorder.Visibility = Visibility.Visible;
		}
		UpdateAiChatVisibilities();
		UpdateWindowSize();
	}

	private void OnClipboardHistoryChanged()
	{
		if (!Dispatcher.CheckAccess())
		{
			Dispatcher.BeginInvoke(new Action(OnClipboardHistoryChanged));
		}
		else if (_searchMode == "Clipboard")
		{
			ExecuteClipboardSearch((SearchBox != null) ? SearchBox.Text.Trim() : "");
		}
		else if (_searchMode == "AI" && (SearchBox == null || string.IsNullOrWhiteSpace(SearchBox.Text)))
		{
			LoadAiHistory();
		}
	}

	private void UpdateWindowSize()
	{
		double num = 1920.0;
		double num2 = 1080.0;
		double left = 0.0;
		double top = 0.0;
		try
		{
			Screen[] allScreens = Screen.AllScreens;
			int num3 = Math.Clamp(SettingsService.Instance.DockMonitorIndex, 0, allScreens.Length - 1);
			Screen obj = allScreens[num3];
			DpiScale dpi = VisualTreeHelper.GetDpi(this);
			num = (double)obj.Bounds.Width / dpi.DpiScaleX;
			num2 = (double)obj.Bounds.Height / dpi.DpiScaleY;
			left = (double)obj.Bounds.X / dpi.DpiScaleX;
			top = (double)obj.Bounds.Y / dpi.DpiScaleY;
		}
		catch
		{
		}
		Left = left;
		Top = top;
		Width = num;
		Height = num2;
		double spotlightContainerWidth = Math.Min(1020.0, Math.Max(720.0, num * 0.7));
		SpotlightContainerWidth = spotlightContainerWidth;
		if (SpotlightContentGrid != null)
		{
			SpotlightContentGrid.Margin = new Thickness(0.0);
		}
		string query = ((SearchBox != null) ? SearchBox.Text.Trim() : "");
		if (string.IsNullOrEmpty(query) && _searchMode != "Clipboard" && _searchMode != "AI")
		{
			SpotlightFavoritesVisibility = ((!SettingsService.Instance.SpotlightShowFavorites) ? Visibility.Collapsed : Visibility.Visible);
			ActiveWindowsVisibility = Visibility.Visible;
			LoadActiveWindows();
			UpdateSatelliteMediaInfo();
			if (ResultsScrollViewer != null)
			{
				ResultsScrollViewer.Visibility = Visibility.Collapsed;
			}
			if (ResultsCardBorder != null)
			{
				ResultsCardBorder.Visibility = Visibility.Collapsed;
			}
			if (WebSearchButton != null)
			{
				WebSearchButton.Visibility = Visibility.Collapsed;
			}
			if (WebSearchSeparator != null)
			{
				WebSearchSeparator.Visibility = Visibility.Collapsed;
			}
			IsWebSearchSelected = false;
			return;
		}
		SpotlightFavoritesVisibility = Visibility.Collapsed;
		List<ActiveWindowModel> list = _allActiveWindows.Where((ActiveWindowModel w) => w.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || w.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
		if (_searchMode == "Windows")
		{
			ActiveWindowsList.Clear();
			foreach (ActiveWindowModel item in list)
			{
				ActiveWindowsList.Add(item);
			}
			ActiveWindowsCountText = $"{list.Count} coincidentes";
			ActiveWindowsVisibility = Visibility.Visible;
			if (ResultsScrollViewer != null)
			{
				ResultsScrollViewer.Visibility = Visibility.Collapsed;
			}
			if (ResultsCardBorder != null)
			{
				ResultsCardBorder.Visibility = Visibility.Collapsed;
			}
		}
		else
		{
			ActiveWindowsVisibility = Visibility.Collapsed;
			if (ResultsScrollViewer != null)
			{
				ResultsScrollViewer.Visibility = Visibility.Visible;
			}
			if (ResultsCardBorder != null)
			{
				ResultsCardBorder.Visibility = Visibility.Visible;
			}
		}
		if (ResultsScrollViewer != null)
		{
			ResultsScrollViewer.MaxHeight = Math.Max(260.0, num2 * 0.58);
		}
		bool flag = (_searchMode == "Apps" || _searchMode == "Files") && !string.IsNullOrEmpty(query);
		if (WebSearchButton != null)
		{
			WebSearchButton.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		}
		if (WebSearchSeparator != null)
		{
			WebSearchSeparator.Visibility = ((!flag || PinnedAppsList.Count <= 0) ? Visibility.Collapsed : Visibility.Visible);
		}
		if (WebSearchText != null)
		{
			WebSearchText.Text = (flag ? ("Buscar \"" + query + "\" en la web") : "Buscar en la web");
		}
		if (_selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
		{
			UpdateInspector(PinnedAppsList[_selectedIndex]);
		}
		else if (IsWebSearchSelected)
		{
			UpdateInspector(null);
		}
		else if (PinnedAppsList.Count > 0)
		{
			_selectedIndex = 0;
			PinnedAppsList[0].IsSelected = true;
			UpdateInspector(PinnedAppsList[0]);
		}
		else
		{
			UpdateInspector(null);
		}
	}

	private void RootBorder_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton != MouseButton.Left)
		{
			return;
		}
		if (SpotlightContentGrid != null)
		{
			Point position = e.GetPosition(SpotlightContentGrid);
			if (!new Rect(0.0, 0.0, SpotlightContentGrid.ActualWidth, SpotlightContentGrid.ActualHeight).Contains(position))
			{
				SafeClose();
			}
		}
		else if (e.OriginalSource == RootBorder)
		{
			SafeClose();
		}
	}

	public void SetSearchMode(string mode)
	{
		if (_searchMode == mode)
		{
			return;
		}
		_isChangingMode = true;
		try
		{
			_searchMode = mode;
			_searchDebounceTimer?.Stop();
			_musicDebounceTimer?.Stop();
			_selectedIndex = -1;
			IsWebSearchSelected = false;
			if (SearchBox != null)
			{
				SearchBox.Text = string.Empty;
			}
			UpdateModeButtonsVisuals();
			UpdateSearchPlaceholder();
			switch (mode)
			{
			case "Apps":
				PopulateDefaultList();
				break;
			case "Windows":
				PinnedAppsList.Clear();
				_selectedIndex = -1;
				LoadActiveWindows();
				ActiveWindowsVisibility = Visibility.Visible;
				if (ResultsCardBorder != null)
				{
					ResultsCardBorder.Visibility = Visibility.Collapsed;
				}
				break;
			case "Files":
				PinnedAppsList.Clear();
				_selectedIndex = -1;
				UpdateWindowSize();
				break;
			case "Clipboard":
				ExecuteClipboardSearch(string.Empty);
				break;
			case "AI":
				LoadAiHistory();
				break;
			}
			UpdateAiChatVisibilities();
		}
		finally
		{
			_isChangingMode = false;
		}
	}

	private void CycleSearchMode()
	{
		SetSearchMode(_searchMode switch
		{
			"Apps" => "Windows", 
			"Windows" => "Files", 
			"Files" => "Clipboard", 
			"Clipboard" => "AI", 
			_ => "Apps", 
		});
	}

	private void UpdateModeButtonsVisuals()
	{
		Brush spotlightSelectedBackgroundBrush = SpotlightSelectedBackgroundBrush;
		Brush spotlightSelectedBorderBrush = SpotlightSelectedBorderBrush;
		SolidColorBrush transparent = Brushes.Transparent;
		Brush spotlightIconBrush = SpotlightIconBrush;
		ModeAppsBrush = ((_searchMode == "Apps") ? spotlightSelectedBackgroundBrush : transparent);
		ModeAppsForegroundBrush = ((_searchMode == "Apps") ? spotlightSelectedBorderBrush : spotlightIconBrush);
		ModeWindowsBrush = ((_searchMode == "Windows") ? spotlightSelectedBackgroundBrush : transparent);
		ModeWindowsForegroundBrush = ((_searchMode == "Windows") ? spotlightSelectedBorderBrush : spotlightIconBrush);
		ModeFilesBrush = ((_searchMode == "Files") ? spotlightSelectedBackgroundBrush : transparent);
		ModeFilesForegroundBrush = ((_searchMode == "Files") ? spotlightSelectedBorderBrush : spotlightIconBrush);
		ModeClipboardBrush = ((_searchMode == "Clipboard") ? spotlightSelectedBackgroundBrush : transparent);
		ModeClipboardForegroundBrush = ((_searchMode == "Clipboard") ? spotlightSelectedBorderBrush : spotlightIconBrush);
		ModeAiBrush = ((_searchMode == "AI") ? spotlightSelectedBackgroundBrush : transparent);
		ModeAiForegroundBrush = ((_searchMode == "AI") ? spotlightSelectedBorderBrush : spotlightIconBrush);
	}

	private void UpdateSearchPlaceholder()
	{
		if (PlaceholderText != null)
		{
			TextBlock placeholderText = PlaceholderText;
			placeholderText.Text = _searchMode switch
			{
				"Windows" => "Filtrar ventanas en ejecución...", 
				"Files" => "Buscar archivos...", 
				"Clipboard" => "Buscar en el portapapeles...", 
				"AI" => "Pregúntale a Gemini AI...", 
				_ => "Buscar aplicaciones, archivos, o filtrar ventanas activas...", 
			};
		}
	}

	private void ModeButton_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.Button { Tag: string tag })
		{
			SetSearchMode(tag);
			SearchBox?.Focus();
		}
	}

	private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			if (NotificationsCardVisibility == Visibility.Visible)
			{
				NotificationsCardVisibility = Visibility.Collapsed;
				e.Handled = true;
			}
			else if (ConnectedDevicesCardVisibility == Visibility.Visible)
			{
				ConnectedDevicesCardVisibility = Visibility.Collapsed;
				e.Handled = true;
			}
			else if (IsPlayerExpandedVisibility == Visibility.Visible)
			{
				IsPlayerExpandedVisibility = Visibility.Collapsed;
				e.Handled = true;
			}
			else if (WallpaperFanCardVisibility == Visibility.Visible)
			{
				WallpaperFanCardVisibility = Visibility.Collapsed;
				RestoreCenterContentView();
				e.Handled = true;
			}
			else
			{
				SafeClose();
				e.Handled = true;
			}
			return;
		}
		if (WallpaperFanCardVisibility == Visibility.Visible)
		{
			if (e.Key == Key.Left)
			{
				PrevFanWallpaper();
				e.Handled = true;
				return;
			}
			if (e.Key == Key.Right)
			{
				NextFanWallpaper();
				e.Handled = true;
				return;
			}
			if (e.Key == Key.Return)
			{
				ApplyActiveFanWallpaper();
				e.Handled = true;
				return;
			}
		}
		if (e.Key == Key.System)
		{
			if (e.SystemKey == Key.F4)
			{
				SafeClose();
				e.Handled = true;
				return;
			}
			if (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt || e.SystemKey == Key.Space || e.SystemKey == Key.None)
			{
				e.Handled = true;
				FocusSearchBox();
				return;
			}
			char c = ConvertKeyToChar(e.SystemKey);
			if (c != '\0' && SearchBox != null)
			{
				FocusSearchBox();
				int caretIndex = SearchBox.CaretIndex;
				SearchBox.Text = SearchBox.Text.Insert(caretIndex, c.ToString());
				SearchBox.CaretIndex = caretIndex + 1;
				e.Handled = true;
			}
			else
			{
				e.Handled = true;
				FocusSearchBox();
			}
			return;
		}
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
		if (SearchBox == null || SearchBox.IsFocused)
		{
			return;
		}
		if (e.Key == Key.Down || e.Key == Key.Up || e.Key == Key.Return || e.Key == Key.Return)
		{
			FocusSearchBox();
			SearchBox_PreviewKeyDown(sender, e);
			return;
		}
		if (e.Key == Key.Tab)
		{
			FocusSearchBox();
			CycleSearchMode();
			e.Handled = true;
			return;
		}
		FocusSearchBox();
		char c2 = ConvertKeyToChar(e.Key);
		if (c2 != '\0')
		{
			int caretIndex2 = SearchBox.CaretIndex;
			SearchBox.Text = SearchBox.Text.Insert(caretIndex2, c2.ToString());
			SearchBox.CaretIndex = caretIndex2 + 1;
			e.Handled = true;
		}
	}

	private static char ConvertKeyToChar(Key key)
	{
		try
		{
			int num = KeyInterop.VirtualKeyFromKey(key);
			if (num > 0)
			{
				byte[] array = new byte[256];
				NativeMethods.GetKeyboardState(array);
				array[18] = 0;
				array[164] = 0;
				array[165] = 0;
				uint wScanCode = NativeMethods.MapVirtualKey((uint)num, 0u);
				StringBuilder stringBuilder = new StringBuilder(2);
				if (NativeMethods.ToUnicode((uint)num, wScanCode, array, stringBuilder, stringBuilder.Capacity, 0u) > 0 && stringBuilder.Length > 0)
				{
					char c = stringBuilder[0];
					if (!char.IsControl(c))
					{
						return c;
					}
				}
			}
		}
		catch
		{
		}
		if (key >= Key.A && key <= Key.Z)
		{
			return (char)(((Keyboard.Modifiers & ModifierKeys.Shift) != 0) ? (65 + (key - 44)) : (97 + (key - 44)));
		}
		if (key >= Key.D0 && key <= Key.D9)
		{
			return (char)(48 + (key - 34));
		}
		if (key >= Key.NumPad0 && key <= Key.NumPad9)
		{
			return (char)(48 + (key - 74));
		}
		if (key == Key.Space)
		{
			return ' ';
		}
		return '\0';
	}

	private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (Keyboard.Modifiers == ModifierKeys.Alt && (e.SystemKey == Key.W || e.Key == Key.W) && _selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
		{
			PinnedApp pinnedApp = PinnedAppsList[_selectedIndex];
			string targetWp = pinnedApp.Path ?? "";
			if (targetWp.StartsWith("clipboard:", StringComparison.OrdinalIgnoreCase))
			{
				targetWp = targetWp.Substring("clipboard:".Length);
			}
			if (!File.Exists(targetWp) && !string.IsNullOrEmpty(InspectorPath) && File.Exists(InspectorPath))
			{
				targetWp = InspectorPath;
			}
			if (!string.IsNullOrEmpty(targetWp) && File.Exists(targetWp))
			{
				bool flag;
				switch (Path.GetExtension(targetWp).ToLowerInvariant())
				{
				case ".png":
				case ".jpg":
				case ".jpeg":
				case ".webp":
				case ".bmp":
					flag = true;
					break;
				default:
					flag = false;
					break;
				}
				if (flag)
				{
					try
					{
						SoundService.Play(SoundType.Ping);
						WallpaperTransitionService.ApplyWallpaper(targetWp, () =>
						{
							EventBus.Publish(new WallpaperChangedEvent(targetWp));
						});
						SafeClose();
						e.Handled = true;
						return;
					}
					catch
					{
					}
				}
			}
		}
		if (Keyboard.Modifiers == ModifierKeys.Alt && (e.SystemKey == Key.O || e.Key == Key.O) && _selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
		{
			PinnedApp pinnedApp2 = PinnedAppsList[_selectedIndex];
			string targetFolder = pinnedApp2.Path ?? "";
			if (targetFolder.StartsWith("clipboard:", StringComparison.OrdinalIgnoreCase))
			{
				targetFolder = targetFolder.Substring("clipboard:".Length);
			}
			if (!File.Exists(targetFolder) && !string.IsNullOrEmpty(InspectorPath) && File.Exists(InspectorPath))
			{
				targetFolder = InspectorPath;
			}
			if (!string.IsNullOrEmpty(targetFolder) && File.Exists(targetFolder))
			{
				try
				{
					string text = IconExtractionService.ResolvePath(targetFolder);
					using (Process.Start("explorer.exe", "/select,\"" + text + "\""))
					{
						SafeClose();
						e.Handled = true;
						return;
					}
				}
				catch
				{
				}
			}
		}
		if (Keyboard.Modifiers == ModifierKeys.Alt && (e.SystemKey == Key.C || e.Key == Key.C) && _selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
		{
			System.Windows.Clipboard.SetText(PinnedAppsList[_selectedIndex].Path);
			SoundService.Play(SoundType.Click);
			e.Handled = true;
		}
		else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && (e.Key == Key.Return || e.Key == Key.Return))
		{
			if (IsWebSearchSelected)
			{
				OpenWebSearch();
			}
			else if (_selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
			{
				LaunchApp(PinnedAppsList[_selectedIndex].Path, asAdmin: true);
			}
			e.Handled = true;
		}
		else if (e.Key == Key.Tab)
		{
			CycleSearchMode();
			e.Handled = true;
		}
		else if (e.Key == Key.Return || e.Key == Key.Return)
		{
			if (_searchMode == "AI")
			{
				string prompt = SearchBox?.Text.Trim() ?? "";
				if (!string.IsNullOrWhiteSpace(prompt))
				{
					if (SearchBox != null)
					{
						SearchBox.Text = string.Empty;
					}
					_ = SendAiChatMessageAsync(prompt);
				}
				e.Handled = true;
				return;
			}
			if (_searchMode == "Windows" || (ActiveWindowsVisibility == Visibility.Visible && _selectedIndex == -1 && ActiveWindowsList.Count > 0))
			{
				ActiveWindowModel activeWindowModel = ActiveWindowsList.FirstOrDefault();
				if (activeWindowModel != null)
				{
					ActiveWindowManager.Instance.SwitchToWindow(activeWindowModel.Hwnd);
					SoundService.Play(SoundType.Click);
					SafeClose();
					e.Handled = true;
					return;
				}
			}
			if (IsWebSearchSelected)
			{
				OpenWebSearch();
			}
			else if (_selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
			{
				PinnedApp pinnedApp3 = PinnedAppsList[_selectedIndex];
				LaunchApp(pinnedApp3.Path, pinnedApp3.RunAsAdmin);
			}
			else if (!string.IsNullOrEmpty(SearchBox?.Text.Trim()))
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
	}

	private void MoveSelection(int direction)
	{
		bool flag = IsWebSearchAvailable();
		int count = PinnedAppsList.Count;
		int num = count + (flag ? 1 : 0);
		if (num == 0)
		{
			return;
		}
		int num2 = (IsWebSearchSelected ? count : _selectedIndex);
		int num3 = ((num2 >= 0) ? (num2 + direction) : ((direction <= 0) ? (num - 1) : 0));
		if (num3 < 0)
		{
			num3 = 0;
		}
		if (num3 >= num)
		{
			num3 = num - 1;
		}
		if (num3 == num2 && (_selectedIndex >= 0 || IsWebSearchSelected))
		{
			return;
		}
		if (IsWebSearchSelected)
		{
			IsWebSearchSelected = false;
		}
		else if (_selectedIndex >= 0 && _selectedIndex < count)
		{
			PinnedAppsList[_selectedIndex].IsSelected = false;
		}
		if (flag && num3 == count)
		{
			_selectedIndex = count;
			IsWebSearchSelected = true;
			WebSearchButton?.BringIntoView();
			UpdateInspector(null);
		}
		else if (num3 >= 0 && num3 < count)
		{
			_selectedIndex = num3;
			IsWebSearchSelected = false;
			PinnedAppsList[_selectedIndex].IsSelected = true;
			if (AppsControl != null)
			{
				(AppsControl.ItemContainerGenerator.ContainerFromIndex(_selectedIndex) as FrameworkElement)?.BringIntoView();
			}
			UpdateInspector(PinnedAppsList[_selectedIndex]);
		}
	}

	private void LaunchApp(string path, bool asAdmin)
	{
		if (string.IsNullOrEmpty(path))
		{
			return;
		}
		switch (path)
		{
		case "AI_LOADING":
			return;
		case "AI_EMPTY":
			return;
		}
		if (path.StartsWith("ytmusic:", StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				if (path.StartsWith("ytmusic:play:", StringComparison.OrdinalIgnoreCase))
				{
					string trackIdOrQuery = path.Substring("ytmusic:play:".Length).Trim();
					ApplicationHost.Instance?.PlayMusic(trackIdOrQuery);
					FloatingMediaOrbWindow.ShowOrb(trackIdOrQuery, "YouTube Music", null, isPlaying: true);
					InspectorMusicStatus = "▶ Reproduciendo ahora...";
					InspectorMusicEqualizerVisibility = Visibility.Visible;
					IsMediaPlaying = true;
					SoundService.Play(SoundType.Click);
					Task.Delay(800).ContinueWith((Task _) =>
					{
						Dispatcher.Invoke(UpdateSatelliteMediaInfo);
					});
				}
				else if (path.StartsWith("ytmusic:search:", StringComparison.OrdinalIgnoreCase))
				{
					string trackIdOrQuery2 = path.Substring("ytmusic:search:".Length).Trim();
					ApplicationHost.Instance?.PlayMusic(trackIdOrQuery2);
					FloatingMediaOrbWindow.ShowOrb(trackIdOrQuery2, "YouTube Music", null, isPlaying: true);
					InspectorMusicStatus = "▶ Reproduciendo ahora...";
					InspectorMusicEqualizerVisibility = Visibility.Visible;
					IsMediaPlaying = true;
					SoundService.Play(SoundType.Click);
					Task.Delay(800).ContinueWith((Task _) =>
					{
						Dispatcher.Invoke(UpdateSatelliteMediaInfo);
					});
				}
				else if (path.Equals("ytmusic:toggle", StringComparison.OrdinalIgnoreCase))
				{
					SatelliteMedia_PlayPause_Click(this, new RoutedEventArgs());
				}
				else if (path.Equals("ytmusic:next", StringComparison.OrdinalIgnoreCase))
				{
					SatelliteMedia_Next_Click(this, new RoutedEventArgs());
				}
				else if (path.Equals("ytmusic:prev", StringComparison.OrdinalIgnoreCase))
				{
					SatelliteMedia_Prev_Click(this, new RoutedEventArgs());
				}
				else if (path.Equals("ytmusic:open", StringComparison.OrdinalIgnoreCase))
				{
					SatelliteMedia_OpenYtMusic_Click(this, new RoutedEventArgs());
					SafeClose();
				}
				return;
			}
			catch
			{
				return;
			}
		}
		if (path.StartsWith("system:", StringComparison.OrdinalIgnoreCase))
		{
			switch (path.Substring("system:".Length).ToLowerInvariant())
			{
			case "shutdown":
				Process.Start(new ProcessStartInfo
				{
					FileName = "shutdown.exe",
					Arguments = "/s /t 0",
					CreateNoWindow = true,
					UseShellExecute = false
				});
				break;
			case "restart":
				Process.Start(new ProcessStartInfo
				{
					FileName = "shutdown.exe",
					Arguments = "/r /t 0",
					CreateNoWindow = true,
					UseShellExecute = false
				});
				break;
			case "lock":
				Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
				break;
			case "sleep":
				Process.Start("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
				break;
			case "recyclebin":
				Process.Start(new ProcessStartInfo
				{
					FileName = "powershell.exe",
					Arguments = "-NoProfile -Command Clear-RecycleBin -Force -ErrorAction SilentlyContinue",
					CreateNoWindow = true,
					UseShellExecute = false
				});
				break;
			}
			SafeClose();
			return;
		}
		if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				using (Process.Start(new ProcessStartInfo
				{
					FileName = path,
					UseShellExecute = true
				}))
				{
				}
			}
			catch
			{
			}
			SafeClose();
			return;
		}
		if (path.StartsWith("clipboard:", StringComparison.OrdinalIgnoreCase))
		{
			string text = path.Substring("clipboard:".Length);
			if (File.Exists(text))
			{
				string ext = Path.GetExtension(text).ToLowerInvariant();
				if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".webp")
				{
					try
					{
						var uri = new Uri(text, UriKind.Absolute);
						var bmi = new BitmapImage();
						bmi.BeginInit();
						bmi.CacheOption = BitmapCacheOption.OnLoad;
						bmi.UriSource = uri;
						bmi.EndInit();
						bmi.Freeze();
						System.Windows.Clipboard.SetImage(bmi);
						SoundService.Play(SoundType.Click);
						SafeClose();
						return;
					}
					catch
					{
					}
				}
			}
			try
			{
				System.Windows.Clipboard.SetText(text);
				SoundService.Play(SoundType.Click);
			}
			catch
			{
			}
			SafeClose();
			return;
		}
		if (path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = path,
					UseShellExecute = true
				});
			}
			catch
			{
			}
			SafeClose();
			return;
		}
		if (path.StartsWith("cmd:", StringComparison.OrdinalIgnoreCase))
		{
			string text2 = path.Substring(4).Trim();
			try
			{
				ProcessStartInfo processStartInfo = new ProcessStartInfo
				{
					FileName = "cmd.exe",
					Arguments = "/c start \"\" " + text2,
					UseShellExecute = true,
					CreateNoWindow = true
				};
				if (asAdmin)
				{
					processStartInfo.Verb = "runas";
				}
				Process.Start(processStartInfo);
			}
			catch
			{
			}
			SafeClose();
			return;
		}
		Dictionary<string, int> appLaunchCounts = SettingsService.Instance.AppLaunchCounts;
		if (appLaunchCounts != null)
		{
			appLaunchCounts[path] = ((!appLaunchCounts.TryGetValue(path, out var value)) ? 1 : (value + 1));
			SettingsService.Save();
		}
		if (path.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase))
		{
			string text3 = path.Substring(4).Split('|')[0];
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = "shell:AppsFolder\\" + text3,
					UseShellExecute = true
				});
			}
			catch
			{
			}
			SafeClose();
			return;
		}
		try
		{
			string fileName = IconExtractionService.ResolvePath(path);
			ProcessStartInfo processStartInfo2 = new ProcessStartInfo
			{
				FileName = fileName,
				UseShellExecute = true
			};
			if (asAdmin)
			{
				processStartInfo2.Verb = "runas";
			}
			Process.Start(processStartInfo2);
		}
		catch
		{
		}
		SafeClose();
	}

	private void AppButton_Click(object sender, RoutedEventArgs e)
	{
		if (_isFavDragging)
		{
			_isFavDragging = false;
			e.Handled = true;
		}
		else
		{
			if (!(sender is System.Windows.Controls.Button { Tag: var tag }))
			{
				return;
			}
			string path = tag as string;
			if (path == null)
			{
				return;
			}
			if (path == "ADD_APP")
			{
				AddNewApp();
				return;
			}
			PinnedApp pinnedApp = PinnedAppsList.FirstOrDefault((PinnedApp a) => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
			LaunchApp(path, pinnedApp?.RunAsAdmin ?? false);
		}
	}

	private void WebSearchButton_Click(object sender, RoutedEventArgs e)
	{
		if (IsWebSearchSelected)
		{
			OpenWebSearch();
			return;
		}
		if (_selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
		{
			PinnedAppsList[_selectedIndex].IsSelected = false;
		}
		_selectedIndex = PinnedAppsList.Count;
		IsWebSearchSelected = true;
		UpdateInspector(null);
	}

	private void OpenWebSearch()
	{
		string text = SearchBox?.Text.Trim() ?? "";
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		string fileName;
		if (IsLikelyUrl(text))
		{
			fileName = ((text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ? text : ("https://" + text));
		}
		else
		{
			fileName = "https://www.google.com/search?q=" + Uri.EscapeDataString(text);
		}
		try
		{
			using (Process.Start(new ProcessStartInfo
			{
				FileName = fileName,
				UseShellExecute = true
			}))
			{
			}
		}
		catch
		{
		}
		SafeClose();
	}

	private static bool IsLikelyUrl(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return Regex.IsMatch(text, "^[a-zA-Z0-9]([a-zA-Z0-9\\-]{0,61}[a-zA-Z0-9])?(\\.[a-zA-Z]{2,})(:\\d+)?(/\\S*)?$");
	}

	private void AddNewApp()
	{
		_isShowingDialog = true;
		try
		{
			Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
			{
				Title = "Seleccionar aplicación para anclar",
				Filter = "Aplicaciones (*.exe, *.lnk)|*.exe;*.lnk|Todos los archivos (*.*)|*.*",
				InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
			};
			if (openFileDialog.ShowDialog() == true && File.Exists(openFileDialog.FileName))
			{
				AppSettings instance = SettingsService.Instance;
				List<string> list = instance.PinnedAppPaths ?? (instance.PinnedAppPaths = new List<string>());
				if (!list.Contains(openFileDialog.FileName))
				{
					list.Add(openFileDialog.FileName);
					SettingsService.Save();
					LoadFavoritesImmediately();
				}
				LoadAppsAsync();
			}
		}
		finally
		{
			_isShowingDialog = false;
			try
			{
				Activate();
			}
			catch
			{
			}
		}
	}

	private void PinButton_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		if (sender is System.Windows.Controls.Button { Tag: string tag })
		{
			TogglePinApp(tag);
		}
	}

	private void TogglePinApp(string path)
	{
		if (!string.IsNullOrEmpty(path) && !(path == "ADD_APP"))
		{
			AppSettings instance = SettingsService.Instance;
			List<string> list = instance.PinnedAppPaths ?? (instance.PinnedAppPaths = new List<string>());
			if (list.Contains(path))
			{
				list.Remove(path);
			}
			else
			{
				list.Add(path);
			}
			SettingsService.Save();
			LoadFavoritesImmediately();
			LoadAppsAsync();
		}
	}

	private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
	{
		if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
		{
			e.Effects = System.Windows.DragDropEffects.Copy;
			e.Handled = true;
		}
	}

	private void Window_Drop(object sender, System.Windows.DragEventArgs e)
	{
		if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
		{
			return;
		}
		string[] array = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
		AppSettings instance = SettingsService.Instance;
		List<string> list = instance.PinnedAppPaths ?? (instance.PinnedAppPaths = new List<string>());
		bool flag = false;
		string[] array2 = array;
		foreach (string text in array2)
		{
			string text2 = Path.GetExtension(text).ToLower();
			if ((text2 == ".exe" || text2 == ".lnk" || Directory.Exists(text) || text.StartsWith("shell:AppsFolder\\")) && !list.Contains(text))
			{
				list.Add(text);
				flag = true;
			}
		}
		if (flag)
		{
			SettingsService.Save();
			LoadFavoritesImmediately();
			LoadAppsAsync();
		}
		e.Handled = true;
	}

	private void FavButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_isFavDragging = false;
		if (sender is FrameworkElement { DataContext: PinnedApp dataContext } && dataContext.Path != "ADD_APP")
		{
			_favDragStartPoint = e.GetPosition(this);
			_draggedFavApp = dataContext;
		}
	}

	private void FavButton_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (e.LeftButton != MouseButtonState.Pressed || _draggedFavApp == null)
		{
			return;
		}
		Point position = e.GetPosition(this);
		Vector vector = _favDragStartPoint - position;
		if (!(Math.Abs(vector.X) > SystemParameters.MinimumHorizontalDragDistance) && !(Math.Abs(vector.Y) > SystemParameters.MinimumVerticalDragDistance))
		{
			return;
		}
		_isFavDragging = true;
		_draggedFavApp.IsDragging = true;
		if (sender is FrameworkElement dragSource)
		{
			System.Windows.DataObject data = new System.Windows.DataObject("PinnedAppDrag", _draggedFavApp);
			DragDrop.DoDragDrop(dragSource, data, System.Windows.DragDropEffects.Move);
			if (_draggedFavApp != null)
			{
				_draggedFavApp.IsDragging = false;
				_draggedFavApp = null;
			}
			e.Handled = true;
		}
	}

	private void FavButton_DragOver(object sender, System.Windows.DragEventArgs e)
	{
		if (e.Data.GetDataPresent("PinnedAppDrag"))
		{
			PinnedApp pinnedApp = e.Data.GetData("PinnedAppDrag") as PinnedApp;
			if (sender is FrameworkElement { DataContext: PinnedApp dataContext } && dataContext.Path != "ADD_APP" && pinnedApp != null && pinnedApp != dataContext)
			{
				int num = FavoritesList.IndexOf(pinnedApp);
				int num2 = FavoritesList.IndexOf(dataContext);
				if (num >= 0 && num2 >= 0 && num != num2)
				{
					FavoritesList.Move(num, num2);
				}
				e.Effects = System.Windows.DragDropEffects.Move;
				e.Handled = true;
				return;
			}
		}
		e.Effects = System.Windows.DragDropEffects.None;
		e.Handled = true;
	}

	private void FavButton_Drop(object sender, System.Windows.DragEventArgs e)
	{
		if (e.Data.GetDataPresent("PinnedAppDrag"))
		{
			if (e.Data.GetData("PinnedAppDrag") is PinnedApp pinnedApp)
			{
				pinnedApp.IsDragging = false;
			}
			List<string> pinnedAppPaths = (from f in FavoritesList
				where !string.IsNullOrEmpty(f.Path) && f.Path != "ADD_APP"
				select f.Path).ToList();
			SettingsService.Instance.PinnedAppPaths = pinnedAppPaths;
			SettingsService.Save();
			e.Handled = true;
		}
	}

	private void RunAsAdminMenu_Click(object sender, RoutedEventArgs e)
	{
		if (sender is MenuItem { Tag: string tag })
		{
			LaunchApp(tag, asAdmin: true);
		}
	}

	private void OpenLocationMenu_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is MenuItem { Tag: string tag }))
		{
			return;
		}
		try
		{
			string text = IconExtractionService.ResolvePath(tag);
			if (File.Exists(text))
			{
				using (Process.Start("explorer.exe", "/select,\"" + text + "\""))
				{
					return;
				}
			}
		}
		catch
		{
		}
	}

	private void PinMenu_Click(object sender, RoutedEventArgs e)
	{
		if (sender is MenuItem { Tag: string tag })
		{
			TogglePinApp(tag);
		}
	}

	private void UnpinMenu_Click(object sender, RoutedEventArgs e)
	{
		if (sender is MenuItem { Tag: string tag })
		{
			List<string> pinnedAppPaths = SettingsService.Instance.PinnedAppPaths;
			if (pinnedAppPaths != null && pinnedAppPaths.Contains(tag))
			{
				pinnedAppPaths.Remove(tag);
				SettingsService.Save();
				LoadFavoritesImmediately();
			}
			LoadAppsAsync();
		}
	}

	private void DeleteClipboardItemMenu_Click(object sender, RoutedEventArgs e)
	{
		if (sender is MenuItem { Tag: var tag } && tag is Guid id)
		{
			ClipboardService.Instance.DeleteItem(id);
		}
	}

	private void SearchGoogleMenu_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is MenuItem { Tag: string tag }))
		{
			return;
		}
		string stringToEscape = tag.Substring("clipboard:".Length);
		try
		{
			using (Process.Start(new ProcessStartInfo
			{
				FileName = "https://www.google.com/search?q=" + Uri.EscapeDataString(stringToEscape),
				UseShellExecute = true
			}))
			{
			}
		}
		catch
		{
		}
		SafeClose();
	}

	private void OpenLinkMenu_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is MenuItem { Tag: string tag }))
		{
			return;
		}
		string text = tag.Substring("clipboard:".Length);
		if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			text = "https://" + text;
		}
		try
		{
			using (Process.Start(new ProcessStartInfo
			{
				FileName = text,
				UseShellExecute = true
			}))
			{
			}
		}
		catch
		{
		}
		SafeClose();
	}

	private void OpenFilesMenu_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is MenuItem { Tag: string tag }))
		{
			return;
		}
		string[] array = tag.Substring("clipboard:".Length).Split(new string[3] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in array)
		{
			if (!File.Exists(text) && !Directory.Exists(text))
			{
				continue;
			}
			try
			{
				using (Process.Start(new ProcessStartInfo
				{
					FileName = text,
					UseShellExecute = true
				}))
				{
				}
			}
			catch
			{
			}
		}
		SafeClose();
	}

	private void OpenFilesLocationMenu_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is MenuItem { Tag: string tag }))
		{
			return;
		}
		string[] array = tag.Substring("clipboard:".Length).Split(new string[3] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length != 0 && File.Exists(array[0]))
		{
			try
			{
				using (Process.Start("explorer.exe", "/select,\"" + array[0] + "\""))
				{
				}
			}
			catch
			{
			}
		}
		SafeClose();
	}

	public async Task LoadBentoDataAsync()
	{
		LoadActiveWallpaperAsync();
		try
		{
			WeatherData weatherData = await WeatherService.Instance.GetWeatherAsync();
			if (weatherData != null)
			{
				BentoWeatherTemp = $"{Math.Round(weatherData.Temperature)}°C";
				BentoWeatherCity = ((!string.IsNullOrEmpty(weatherData.City)) ? weatherData.City : "Clima local");
				BentoWeatherCondition = weatherData.Condition;
				BentoWeatherGlyph = weatherData.Glyph;
			}
		}
		catch
		{
		}
		try
		{
			CultureInfo cultureInfo = new CultureInfo("es-ES");
			string text = DateTime.Now.ToString("dddd, d 'de' MMMM", cultureInfo);
			if (!string.IsNullOrEmpty(text))
			{
				BentoWeatherDate = char.ToUpper(text[0]) + text.Substring(1);
			}
		}
		catch
		{
		}
		try
		{
			ClipboardItem clipboardItem = ClipboardService.Instance.History?.FirstOrDefault();
			if (clipboardItem != null && !string.IsNullOrWhiteSpace(clipboardItem.Content))
			{
				string text3 = clipboardItem.Content.Trim();
				BentoClipboardSnippet = ((text3.Length > 90) ? (text3.Substring(0, 90) + "...") : text3);
				PinnedAppsWindow pinnedAppsWindow2 = this;
				string bentoClipboardType;
				if (clipboardItem.Type == "Link")
				{
					bentoClipboardType = "Enlace";
				}
				else
				{
					bentoClipboardType = ((clipboardItem.Type == "Image") ? "Imagen" : "Texto");
				}
				pinnedAppsWindow2.BentoClipboardType = bentoClipboardType;
			}
			else
			{
				BentoClipboardSnippet = "Portapapeles vacío";
				BentoClipboardType = "Sin elementos";
			}
		}
		catch
		{
		}
	}

	private void ShowInspectorView(string view)
	{
		InspectorFileViewVisibility = ((!(view == "File")) ? Visibility.Collapsed : Visibility.Visible);
		InspectorCodeViewVisibility = ((!(view == "Code")) ? Visibility.Collapsed : Visibility.Visible);
		InspectorAppViewVisibility = ((!(view == "App")) ? Visibility.Collapsed : Visibility.Visible);
		InspectorClipboardViewVisibility = ((!(view == "Clipboard")) ? Visibility.Collapsed : Visibility.Visible);
		InspectorAiViewVisibility = ((!(view == "AI")) ? Visibility.Collapsed : Visibility.Visible);
		InspectorWebViewVisibility = ((!(view == "Web")) ? Visibility.Collapsed : Visibility.Visible);
		InspectorMusicViewVisibility = ((!(view == "Music")) ? Visibility.Collapsed : Visibility.Visible);
		InspectorDefaultViewVisibility = ((!(view == "Default")) ? Visibility.Collapsed : Visibility.Visible);
	}

	public async void UpdateInspector(PinnedApp? item)
	{
		try
		{
			try
			{
				_inspectorCts?.Cancel();
				_inspectorCts?.Dispose();
			}
			catch
			{
			}
			_inspectorCts = new CancellationTokenSource();
			CancellationToken token = _inspectorCts.Token;
			if (IsWebSearchSelected)
			{
				InspectorTitle = "Buscar en la web";
				InspectorSubtitle = ((SearchBox != null) ? SearchBox.Text.Trim() : "");
				InspectorWebQuery = InspectorSubtitle;
				ShowInspectorView("Web");
				return;
			}
			if (item == null)
			{
				InspectorSelectedItem = null;
				ShowInspectorView("Default");
				return;
			}
			InspectorSelectedItem = item;
			InspectorTitle = item.Name ?? "";
			InspectorAppName = item.Name ?? "";
			InspectorAppIcon = item.Icon;
			InspectorSubtitle = ((!string.IsNullOrEmpty(item.Subtitle)) ? item.Subtitle : (item.FriendlyPath ?? ""));
			InspectorPath = item.Path ?? "";
			if (item.Path != null && item.Path.StartsWith("ytmusic:", StringComparison.OrdinalIgnoreCase))
			{
				string path = item.Path;
				InspectorMusicArtwork = item.Icon;
				if (!string.IsNullOrEmpty(item.ThumbnailUrl))
				{
					Task.Run(async () =>
					{
						BitmapImage bmp = await SpotlightSearchService.LoadBitmapFromUrlAsync(item.ThumbnailUrl, 640, token);
						if (bmp != null)
						{
							Dispatcher.Invoke(() =>
							{
								if (!token.IsCancellationRequested && InspectorSelectedItem == item)
								{
									InspectorMusicArtwork = bmp;
								}
							});
						}
					}, token);
				}
				if (!string.IsNullOrEmpty(item.Duration))
				{
					InspectorMusicDuration = item.Duration;
					InspectorMusicDurationVisibility = Visibility.Visible;
				}
				else
				{
					InspectorMusicDurationVisibility = Visibility.Collapsed;
				}
				if (path.StartsWith("ytmusic:play:", StringComparison.OrdinalIgnoreCase))
				{
					InspectorMusicTitle = item.Name ?? "Canción de YouTube";
					InspectorMusicSubtitle = ((!string.IsNullOrEmpty(item.Channel)) ? item.Channel : "YouTube");
					InspectorMusicQuery = item.Name ?? "";
					InspectorMusicStatus = "Listo para reproducir (↵)";
					InspectorMusicEqualizerVisibility = Visibility.Collapsed;
				}
				else if (path.StartsWith("ytmusic:search:", StringComparison.OrdinalIgnoreCase))
				{
					string text = path.Substring("ytmusic:search:".Length);
					InspectorMusicTitle = item.Name ?? text;
					InspectorMusicSubtitle = "Búsqueda en YouTube Music";
					InspectorMusicQuery = text;
					InspectorMusicStatus = "Listo para reproducir (↵)";
					InspectorMusicEqualizerVisibility = Visibility.Collapsed;
				}
				else if (path.Equals("ytmusic:toggle", StringComparison.OrdinalIgnoreCase))
				{
					InspectorMusicTitle = "Reproducir / Pausar";
					InspectorMusicSubtitle = "Control de Reproducción";
					InspectorMusicQuery = "Alternar estado del reproductor";
					InspectorMusicStatus = "Envía comando Play/Pause a YouTube Music";
					InspectorMusicEqualizerVisibility = Visibility.Collapsed;
				}
				else if (path.Equals("ytmusic:next", StringComparison.OrdinalIgnoreCase))
				{
					InspectorMusicTitle = "Siguiente Canción";
					InspectorMusicSubtitle = "Control de Reproducción";
					InspectorMusicQuery = "Avanzar a la siguiente pista";
					InspectorMusicStatus = "Envía comando Siguiente a YouTube Music";
					InspectorMusicEqualizerVisibility = Visibility.Collapsed;
				}
				else if (path.Equals("ytmusic:prev", StringComparison.OrdinalIgnoreCase))
				{
					InspectorMusicTitle = "Canción Anterior";
					InspectorMusicSubtitle = "Control de Reproducción";
					InspectorMusicQuery = "Retroceder a la pista anterior";
					InspectorMusicStatus = "Envía comando Anterior a YouTube Music";
					InspectorMusicEqualizerVisibility = Visibility.Collapsed;
				}
				else if (path.Equals("ytmusic:open", StringComparison.OrdinalIgnoreCase))
				{
					InspectorMusicTitle = "Abrir YouTube Music";
					InspectorMusicSubtitle = "Ventana de Reproductor";
					InspectorMusicQuery = "Visualizar ventana completa";
					InspectorMusicStatus = "Abre o enfoca la ventana de YouTube Music";
					InspectorMusicEqualizerVisibility = Visibility.Collapsed;
				}
				else
				{
					InspectorMusicTitle = item.Name ?? "Música";
					InspectorMusicSubtitle = "YouTube Music";
					InspectorMusicQuery = item.ToolTip ?? "";
					InspectorMusicStatus = "Listo";
					InspectorMusicEqualizerVisibility = Visibility.Collapsed;
				}
				ShowInspectorView("Music");
				return;
			}
			if (item.IsAiItem || _searchMode == "AI")
			{
				InspectorAiResponse = item.Name ?? "";
				ShowInspectorView("AI");
				return;
			}
			if (item.IsClipboardItem || _searchMode == "Clipboard")
			{
				string text2 = (InspectorFullClipboardText = item.Name ?? "");
				int length = text2.Length;
				int value = text2.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
				InspectorClipboardWordCount = $"{length} caracteres • {value} palabras";

				bool isClipboardImage = item.IsClipboardImage || item.ClipboardType == "Image" || (item.Name != null && item.Name.StartsWith("[Imagen]", StringComparison.OrdinalIgnoreCase));
				string? imgPath = null;
				if (!string.IsNullOrEmpty(item.Path) && item.Path.StartsWith("clipboard:", StringComparison.OrdinalIgnoreCase))
				{
					string candidate = item.Path.Substring("clipboard:".Length);
					if (File.Exists(candidate))
					{
						imgPath = candidate;
					}
				}
				if (string.IsNullOrEmpty(imgPath) && item.ClipboardId != Guid.Empty)
				{
					var clip = ClipboardService.Instance.History?.FirstOrDefault(c => c.Id == item.ClipboardId);
					if (clip != null && !string.IsNullOrEmpty(clip.ImagePath) && File.Exists(clip.ImagePath))
					{
						imgPath = clip.ImagePath;
					}
				}
				if (string.IsNullOrEmpty(imgPath) && !string.IsNullOrEmpty(item.Name) && item.Name.StartsWith("[Imagen]"))
				{
					string fname = item.Name.Substring("[Imagen]".Length).Trim();
					var clip = ClipboardService.Instance.History?.FirstOrDefault(c => !string.IsNullOrEmpty(c.ImagePath) && Path.GetFileName(c.ImagePath).Equals(fname, StringComparison.OrdinalIgnoreCase));
					if (clip != null && File.Exists(clip.ImagePath))
					{
						imgPath = clip.ImagePath;
					}
				}

				if (isClipboardImage || !string.IsNullOrEmpty(imgPath))
				{
					if (!string.IsNullOrEmpty(imgPath) && File.Exists(imgPath))
					{
						InspectorHasClipboardImage = true;
						InspectorClipboardImageViewVisibility = Visibility.Visible;
						InspectorClipboardTextViewVisibility = Visibility.Collapsed;
						InspectorPath = imgPath;
						InspectorClipboardCopyButtonText = "Copiar imagen (↵)";
						InspectorClipboardImageDimensions = "Imagen del portapapeles";
						InspectorClipboardImageDetails = Path.GetFileName(imgPath);

						if (item.Icon != null)
						{
							InspectorClipboardImageSource = item.Icon;
						}

						string previewPath = imgPath;
						Task.Run(() =>
						{
							try
							{
								var bmp = new BitmapImage();
								bmp.BeginInit();
								bmp.DecodePixelWidth = 720;
								bmp.CacheOption = BitmapCacheOption.OnLoad;
								bmp.UriSource = new Uri(previewPath, UriKind.Absolute);
								bmp.EndInit();
								bmp.Freeze();

								long fileLen = 0;
								try { fileLen = new FileInfo(previewPath).Length; } catch { }

								Dispatcher.BeginInvoke(new Action(() =>
								{
									if (!token.IsCancellationRequested && InspectorSelectedItem == item)
									{
										InspectorClipboardImageSource = bmp;
										InspectorClipboardImageDimensions = $"{bmp.PixelWidth} × {bmp.PixelHeight}";
										if (fileLen > 0)
										{
											InspectorClipboardImageDetails = $"{FormatBytes(fileLen)} • {Path.GetFileName(previewPath)}";
										}
									}
								}), System.Windows.Threading.DispatcherPriority.Background);
							}
							catch
							{
								if (item.Icon != null)
								{
									Dispatcher.BeginInvoke(new Action(() =>
									{
										if (!token.IsCancellationRequested && InspectorSelectedItem == item)
										{
											InspectorClipboardImageSource = item.Icon;
										}
									}), System.Windows.Threading.DispatcherPriority.Background);
								}
							}
						});
					}
					else if (item.Icon != null)
					{
						InspectorClipboardImageSource = item.Icon;
						InspectorHasClipboardImage = true;
						InspectorClipboardImageViewVisibility = Visibility.Visible;
						InspectorClipboardTextViewVisibility = Visibility.Collapsed;
						InspectorClipboardCopyButtonText = "Copiar imagen (↵)";
						InspectorClipboardImageDimensions = "Imagen del portapapeles";
						InspectorClipboardImageDetails = item.Subtitle ?? "";
					}
					else
					{
						InspectorHasClipboardImage = false;
						InspectorClipboardImageSource = null;
						InspectorClipboardImageViewVisibility = Visibility.Collapsed;
						InspectorClipboardTextViewVisibility = Visibility.Visible;
						InspectorClipboardCopyButtonText = "Copiar (Enter)";
					}
				}
				else
				{
					InspectorHasClipboardImage = false;
					InspectorClipboardImageSource = null;
					InspectorClipboardImageViewVisibility = Visibility.Collapsed;
					InspectorClipboardTextViewVisibility = Visibility.Visible;
					InspectorClipboardCopyButtonText = "Copiar (Enter)";
				}

				Match match = Regex.Match(text2.Trim(), "^#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$");
				if (match.Success)
				{
					try
					{
						Color color = (Color)ColorConverter.ConvertFromString(match.Value);
						InspectorColorPreviewBrush = new SolidColorBrush(color);
						InspectorIsColor = true;
					}
					catch
					{
						InspectorIsColor = false;
					}
				}
				else
				{
					InspectorIsColor = false;
				}
				InspectorIsLink = item.IsClipboardLink || text2.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text2.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
				ShowInspectorView("Clipboard");
				return;
			}
			string itemPath = item.Path ?? "";
			bool flag = false;
			bool flag2 = false;
			try
			{
				if (!string.IsNullOrEmpty(itemPath))
				{
					flag = File.Exists(itemPath);
					flag2 = !flag && Directory.Exists(itemPath);
				}
			}
			catch
			{
			}
			if (flag2)
			{
				InspectorImageSource = item.Icon;
				InspectorDimensions = itemPath;
				InspectorFileSize = "Carpeta de archivos";
				try
				{
					InspectorModifiedDate = Directory.GetLastWriteTime(itemPath).ToString("dd/MM/yyyy HH:mm");
				}
				catch
				{
				}
				ShowInspectorView("File");
				return;
			}
			string ext;
			string text4;
			if (flag)
			{
				ext = "";
				try
				{
					ext = Path.GetExtension(itemPath).ToLowerInvariant();
				}
				catch
				{
				}
				try
				{
					FileInfo fileInfo = new FileInfo(itemPath);
					InspectorFileSize = FormatBytes(fileInfo.Length);
					InspectorModifiedDate = fileInfo.LastWriteTime.ToString("dd/MM/yyyy HH:mm");
				}
				catch
				{
					InspectorFileSize = "";
					InspectorModifiedDate = "";
				}
				text4 = ext;
				if (text4 != null)
				{
					int length2 = text4.Length;
					if (length2 != 4)
					{
						if (length2 == 5)
						{
							char c = text4[1];
							if (c != 'j')
							{
								if (c == 'w' && text4 == ".webp")
								{
									goto IL_0908;
								}
							}
							else if (text4 == ".jpeg")
							{
								goto IL_0908;
							}
						}
					}
					else
					{
						switch (text4[1])
						{
						case 'p':
							break;
						case 'j':
							goto IL_08aa;
						case 'b':
							goto IL_08ba;
						case 'g':
							goto IL_08ca;
						case 'i':
							goto IL_08da;
						default:
							goto IL_090d;
						}
						if (text4 == ".png")
						{
							goto IL_0908;
						}
					}
				}
				goto IL_090d;
			}
			goto IL_10a7;
			IL_08ba:
			if (text4 == ".bmp")
			{
				goto IL_0908;
			}
			goto IL_090d;
			IL_08aa:
			if (text4 == ".jpg")
			{
				goto IL_0908;
			}
			goto IL_090d;
			IL_08da:
			if (text4 == ".ico")
			{
				goto IL_0908;
			}
			goto IL_090d;
			IL_10a7:
			if (!string.IsNullOrEmpty(itemPath) && itemPath.StartsWith("system:", StringComparison.OrdinalIgnoreCase))
			{
				InspectorAppIcon = item.Icon;
				InspectorAppName = item.Name ?? "";
				InspectorAppStatus = "Acción del Sistema";
				InspectorAppRamUsage = "—";
				InspectorAppPid = "—";
				InspectorIsAppRunning = false;
				ShowInspectorView("App");
				return;
			}
			string text5 = itemPath;
			if (text5.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
			{
				try
				{
					string text6 = IconExtractionService.ResolvePath(text5);
					if (!string.IsNullOrEmpty(text6) && File.Exists(text6))
					{
						text5 = text6;
					}
				}
				catch
				{
				}
			}
			string appProcessName = "";
			try
			{
				appProcessName = Path.GetFileNameWithoutExtension(text5);
			}
			catch
			{
			}
			bool isRunning = false;
			long memBytes = 0L;
			int primaryPid = 0;
			int procCount = 0;
			if (!string.IsNullOrEmpty(appProcessName))
			{
				await Task.Run(() =>
				{
					if (token.IsCancellationRequested)
					{
						return;
					}
					try
					{
						Process[] processesByName = Process.GetProcessesByName(appProcessName);
						if (processesByName.Length != 0)
						{
							isRunning = true;
							primaryPid = processesByName[0].Id;
							procCount = processesByName.Length;
							try
							{
								memBytes = processesByName.Sum((Process p) => p.WorkingSet64);
								return;
							}
							catch
							{
								return;
							}
						}
					}
					catch
					{
					}
				});
			}
			if (!token.IsCancellationRequested)
			{
				InspectorIsAppRunning = isRunning;
				InspectorAppIcon = item.Icon;
				InspectorAppName = item.Name ?? "";
				InspectorAppStatus = (isRunning ? "En ejecución" : "Listo para iniciar");
				InspectorAppRunningStatus = (isRunning ? ("En ejecución • " + FormatBytes(memBytes) + " RAM") : "Listo para iniciar");
				InspectorAppRamUsage = (isRunning ? FormatBytes(memBytes) : "No activo");
				PinnedAppsWindow pinnedAppsWindow = this;
				string inspectorAppPid;
				if (isRunning)
				{
					inspectorAppPid = ((procCount > 1) ? $"{primaryPid} (+{procCount - 1})" : $"{primaryPid}");
				}
				else
				{
					inspectorAppPid = "Inactivo";
				}
				pinnedAppsWindow.InspectorAppPid = inspectorAppPid;
				ShowInspectorView("App");
			}
			return;
			IL_0908:
			bool flag3 = true;
			goto IL_0910;
			IL_090d:
			flag3 = false;
			goto IL_0910;
			IL_08ca:
			if (text4 == ".gif")
			{
				goto IL_0908;
			}
			goto IL_090d;
			IL_0910:
			if (flag3)
			{
				try
				{
					BitmapImage bitmapImage = new BitmapImage();
					bitmapImage.BeginInit();
					bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage.DecodePixelWidth = 500;
					bitmapImage.UriSource = new Uri(itemPath);
					bitmapImage.EndInit();
					bitmapImage.Freeze();
					InspectorImageSource = bitmapImage;
					InspectorDimensions = $"{bitmapImage.PixelWidth} × {bitmapImage.PixelHeight} px";
				}
				catch
				{
					InspectorImageSource = item.Icon;
					InspectorDimensions = "";
				}
				ShowInspectorView("File");
				return;
			}
			switch (ext)
			{
			case ".cs":
			case ".py":
			case ".js":
			case ".ts":
			case ".md":
			case ".sh":
			case ".rs":
			case ".html":
			case ".json":
			case ".xaml":
			case ".xml":
			case ".css":
			case ".cpp":
			case ".txt":
			case ".sql":
			case ".ps1":
			case ".c":
			case ".h":
				flag3 = true;
				break;
			default:
				flag3 = false;
				break;
			}
			if (flag3)
			{
				try
				{
					List<string> list = await Task.Run(() =>
					{
						List<string> list2 = new List<string>();
						int num = 0;
						using StreamReader streamReader = new StreamReader(itemPath);
						while (!streamReader.EndOfStream && num < 35 && !token.IsCancellationRequested)
						{
							string text7 = streamReader.ReadLine();
							if (text7 != null)
							{
								list2.Add(text7);
								num++;
							}
						}
						return list2;
					});
					if (!token.IsCancellationRequested)
					{
						switch (ext)
						{
						case ".cs":
							text4 = "C# Source";
							break;
						case ".xaml":
							text4 = "XAML Markup";
							break;
						case ".json":
							text4 = "JSON Data";
							break;
						case ".py":
							text4 = "Python Script";
							break;
						case ".js":
						case ".ts":
							text4 = "JavaScript / TypeScript";
							break;
						case ".html":
						case ".css":
							text4 = "Web / Styling";
							break;
						case ".sh":
						case ".ps1":
							text4 = "Shell Script";
							break;
						case ".cpp":
						case ".c":
						case ".h":
							text4 = "C/C++ Code";
							break;
						case ".rs":
							text4 = "Rust Source";
							break;
						case ".sql":
							text4 = "SQL Query";
							break;
						case ".md":
						case ".txt":
							text4 = "Documento de Texto";
							break;
						default:
							text4 = ext.ToUpperInvariant().TrimStart('.');
							break;
						}
						string inspectorCodeLanguage = text4;
						InspectorCodeLanguage = inspectorCodeLanguage;
						InspectorCodeSnippet = string.Join(Environment.NewLine, list);
						InspectorCodeLines = $"{list.Count} líneas previsualizadas";
						ShowInspectorView("Code");
					}
					return;
				}
				catch
				{
				}
			}
			if (!(ext == ".exe") && !(ext == ".lnk"))
			{
				InspectorImageSource = item.Icon;
				InspectorDimensions = ext.ToUpperInvariant().TrimStart('.') + " Archivo";
				ShowInspectorView("File");
				return;
			}
			goto IL_10a7;
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception)
		{
		}
	}

	private static string FormatBytes(long bytes)
	{
		if (bytes >= 1024)
		{
			if (bytes >= 1048576)
			{
				if (bytes >= 1073741824)
				{
					return $"{(double)bytes / 1073741824.0:F2} GB";
				}
				return $"{(double)bytes / 1048576.0:F1} MB";
			}
			return $"{(double)bytes / 1024.0:F1} KB";
		}
		return $"{bytes} B";
	}

	private void OnWallpaperChanged(WallpaperChangedEvent @event)
	{
		Dispatcher.BeginInvoke((Func<Task>)(() => LoadActiveWallpaperAsync(force: true, @event.WallpaperPath)));
	}

	public async Task LoadActiveWallpaperAsync(bool force = false, string? explicitPath = null)
	{
		try
		{
			string path = !string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath)
				? explicitPath
				: WallpaperThemeService.GetCurrentWallpaperPath();
			if (string.IsNullOrEmpty(path) || !File.Exists(path) || (!force && path == _currentWallpaperPath && BentoWallpaperSource != null))
			{
				return;
			}
			_currentWallpaperPath = path;
			var (bitmapImage, bentoWallpaperName, bentoWallpaperResolution) = await Task.Run(() =>
			{
				try
				{
					using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
					BitmapDecoder bitmapDecoder = BitmapDecoder.Create(fileStream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
					int num = 0;
					int num2 = 0;
					if (bitmapDecoder.Frames.Count > 0)
					{
						num = bitmapDecoder.Frames[0].PixelWidth;
						num2 = bitmapDecoder.Frames[0].PixelHeight;
					}
					fileStream.Position = 0L;
					BitmapImage bitmapImage2 = new BitmapImage();
					bitmapImage2.BeginInit();
					bitmapImage2.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage2.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
					if (num > 384)
					{
						bitmapImage2.DecodePixelWidth = 384;
					}
					bitmapImage2.StreamSource = fileStream;
					bitmapImage2.EndInit();
					bitmapImage2.Freeze();
					string text = Path.GetFileNameWithoutExtension(path);
					if (text.Equals("TranscodedWallpaper", StringComparison.OrdinalIgnoreCase))
					{
						text = "Fondo de Windows";
					}
					else if (text.Length > 24)
					{
						text = text.Substring(0, 24) + "...";
					}
					string item = ((num > 0) ? $"{num}×{num2}" : "HD");
					if (num >= 3840 || num2 >= 2160)
					{
						item = "4K UHD";
					}
					else if (num >= 2560 || num2 >= 1440)
					{
						item = "2K QHD";
					}
					else if (num >= 1920 || num2 >= 1080)
					{
						item = "Full HD";
					}
					return (bitmapImage2, text, item);
				}
				catch
				{
					return ((BitmapImage?, string, string))(null, Path.GetFileName(path), "");
				}
			});
			if (bitmapImage != null)
			{
				BentoWallpaperSource = bitmapImage;
				BentoWallpaperName = bentoWallpaperName;
				BentoWallpaperResolution = bentoWallpaperResolution;
			}
		}
		catch
		{
		}
	}

	private void BentoWallpaperCard_Click(object sender, MouseButtonEventArgs e)
	{
		try
		{
			if (e.ClickCount >= 2 && !string.IsNullOrEmpty(_currentWallpaperPath) && File.Exists(_currentWallpaperPath))
			{
				Process.Start(new ProcessStartInfo(_currentWallpaperPath)
				{
					UseShellExecute = true
				});
				SoundService.Play(SoundType.Click);
				SafeClose();
			}
		}
		catch
		{
		}
	}

	private void BentoWallpaper_Next_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			BentoAction_Wallpaper_Click(sender, e);
			_ = LoadActiveWallpaperAsync(force: true);
		}
		catch
		{
		}
	}

	private void BentoWallpaper_OpenFolder_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!string.IsNullOrEmpty(_currentWallpaperPath) && File.Exists(_currentWallpaperPath))
			{
				string text = IconExtractionService.ResolvePath(_currentWallpaperPath);
				Process.Start("explorer.exe", "/select,\"" + text + "\"");
				SoundService.Play(SoundType.Click);
				SafeClose();
			}
		}
		catch
		{
		}
	}

	private void BentoAction_Terminal_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "powershell.exe",
				UseShellExecute = true
			});
			SoundService.Play(SoundType.Click);
			SafeClose();
		}
		catch
		{
		}
	}

	private void BentoWeatherCard_Click(object sender, MouseButtonEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Hover);
			WeatherService.Instance.ResetCache();
			_ = LoadBentoDataAsync();
		}
		catch
		{
		}
	}

	private void BentoClipboardCard_Click(object sender, MouseButtonEventArgs e)
	{
		try
		{
			ClipboardItem clipboardItem = ClipboardService.Instance.History?.FirstOrDefault();
			if (clipboardItem != null && !string.IsNullOrEmpty(clipboardItem.Content))
			{
				System.Windows.Clipboard.SetText(clipboardItem.Content);
				SoundService.Play(SoundType.Click);
				SafeClose();
			}
		}
		catch
		{
		}
	}

	private void BentoAction_Wallpaper_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			string text = SettingsService.Instance.WallpaperDirectory;
			if ((string.IsNullOrEmpty(text) || !Directory.Exists(text)) && !string.IsNullOrEmpty(_currentWallpaperPath) && File.Exists(_currentWallpaperPath))
			{
				string directoryName = Path.GetDirectoryName(_currentWallpaperPath);
				if (!string.IsNullOrEmpty(directoryName) && Directory.Exists(directoryName))
				{
					text = directoryName;
				}
			}
			if (!string.IsNullOrEmpty(text) && Directory.Exists(text))
			{
				HashSet<string> exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };
				List<string> list = (from f in Directory.GetFiles(text)
					where exts.Contains(Path.GetExtension(f))
					select f).ToList();
				if (list.Count > 0)
				{
					Random random = new Random();
					string text2 = list[random.Next(list.Count)];
					SoundService.Play(SoundType.Ping);
					WallpaperTransitionService.ApplyWallpaper(text2, () =>
					{
						EventBus.Publish(new WallpaperChangedEvent(text2));
					});
				}
			}
		}
		catch
		{
		}
	}

	private void BentoAction_RecycleBin_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "powershell.exe",
				Arguments = "-NoProfile -Command Clear-RecycleBin -Force -ErrorAction SilentlyContinue",
				CreateNoWindow = true,
				UseShellExecute = false
			});
			SoundService.Play(SoundType.Whoosh);
		}
		catch
		{
		}
	}

	private void BentoAction_Lock_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
			SafeClose();
		}
		catch
		{
		}
	}

	private void BentoAction_Focus_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Ping);
		}
		catch
		{
		}
	}

	private void InspectorSetWallpaper_Click(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrEmpty(InspectorPath) && File.Exists(InspectorPath))
		{
			try
			{
				string path = InspectorPath;
				SoundService.Play(SoundType.Ping);
				WallpaperTransitionService.ApplyWallpaper(path, () =>
				{
					EventBus.Publish(new WallpaperChangedEvent(path));
				});
				SafeClose();
			}
			catch
			{
			}
		}
	}

	private void InspectorOpenFolder_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrEmpty(InspectorPath))
		{
			return;
		}
		try
		{
			string text = InspectorPath;
			if (text.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
			{
				string text2 = IconExtractionService.ResolvePath(text);
				if (!string.IsNullOrEmpty(text2) && (File.Exists(text2) || Directory.Exists(text2)))
				{
					text = text2;
				}
			}
			if (File.Exists(text))
			{
				using (Process.Start("explorer.exe", "/select,\"" + text + "\""))
				{
					SafeClose();
					return;
				}
			}
			if (Directory.Exists(text))
			{
				using (Process.Start("explorer.exe", "\"" + text + "\""))
				{
					SafeClose();
					return;
				}
			}
		}
		catch
		{
		}
	}

	private void InspectorOpenWeb_Click(object sender, RoutedEventArgs e)
	{
		OpenWebSearch();
	}

	private void InspectorPlayMusic_Click(object sender, RoutedEventArgs e)
	{
		if (InspectorSelectedItem != null && !string.IsNullOrEmpty(InspectorSelectedItem.Path))
		{
			LaunchApp(InspectorSelectedItem.Path, asAdmin: false);
		}
	}

	private void InspectorOpenMusicWindow_Click(object sender, RoutedEventArgs e)
	{
		SatelliteMedia_OpenYtMusic_Click(sender, e);
	}

	private void InspectorOpenMusicBrowser_Click(object sender, RoutedEventArgs e)
	{
		string text = "";
		if (InspectorSelectedItem != null && !string.IsNullOrEmpty(InspectorSelectedItem.VideoId))
		{
			text = "https://www.youtube.com/watch?v=" + InspectorSelectedItem.VideoId;
		}
		else if (!string.IsNullOrWhiteSpace(InspectorMusicQuery))
		{
			text = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(InspectorMusicQuery);
		}
		if (!string.IsNullOrEmpty(text))
		{
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = text,
					UseShellExecute = true
				});
				SafeClose();
			}
			catch
			{
			}
		}
	}

	private void SearchResultRow_Click(object sender, RoutedEventArgs e)
	{
		if (!(sender is FrameworkElement { DataContext: PinnedApp dataContext }))
		{
			return;
		}
		int num = PinnedAppsList.IndexOf(dataContext);
		if (num >= 0)
		{
			if (_selectedIndex == num && dataContext.IsSelected)
			{
				LaunchApp(dataContext.Path, dataContext.RunAsAdmin);
			}
			else
			{
				SelectAppByIndex(num);
			}
		}
	}

	private void SearchResultRow_DoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: PinnedApp dataContext })
		{
			LaunchApp(dataContext.Path, dataContext.RunAsAdmin);
			e.Handled = true;
		}
	}

	private void SelectAppByIndex(int index)
	{
		if (index >= 0 && index < PinnedAppsList.Count)
		{
			if (_selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
			{
				PinnedAppsList[_selectedIndex].IsSelected = false;
			}
			_selectedIndex = index;
			IsWebSearchSelected = false;
			PinnedApp pinnedApp = PinnedAppsList[index];
			pinnedApp.IsSelected = true;
			UpdateInspector(pinnedApp);
		}
	}

	private void InspectorOpenFile_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrEmpty(InspectorPath))
		{
			return;
		}
		try
		{
			using (Process.Start(new ProcessStartInfo
			{
				FileName = InspectorPath,
				UseShellExecute = true
			}))
			{
				SafeClose();
			}
		}
		catch
		{
		}
	}

	private void InspectorCopyPath_Click(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrEmpty(InspectorPath))
		{
			System.Windows.Clipboard.SetText(InspectorPath);
			SoundService.Play(SoundType.Click);
		}
	}

	private void InspectorCopyContent_Click(object sender, RoutedEventArgs e)
	{
		if (InspectorHasClipboardImage && !string.IsNullOrEmpty(InspectorPath) && File.Exists(InspectorPath))
		{
			try
			{
				var uri = new Uri(InspectorPath, UriKind.Absolute);
				var bmi = new BitmapImage();
				bmi.BeginInit();
				bmi.CacheOption = BitmapCacheOption.OnLoad;
				bmi.UriSource = uri;
				bmi.EndInit();
				bmi.Freeze();
				System.Windows.Clipboard.SetImage(bmi);
				SoundService.Play(SoundType.Click);
				SafeClose();
				return;
			}
			catch
			{
			}
		}

		string text;
		if (string.IsNullOrEmpty(InspectorCodeSnippet))
		{
			text = ((!string.IsNullOrEmpty(InspectorFullClipboardText)) ? InspectorFullClipboardText : InspectorAiResponse);
		}
		else
		{
			text = InspectorCodeSnippet;
		}
		if (!string.IsNullOrEmpty(text))
		{
			System.Windows.Clipboard.SetText(text);
			SoundService.Play(SoundType.Click);
			SafeClose();
		}
	}

	private void InspectorLaunchApp_Click(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrEmpty(InspectorPath))
		{
			LaunchApp(InspectorPath, asAdmin: false);
		}
	}

	private void InspectorRunAsAdmin_Click(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrEmpty(InspectorPath))
		{
			LaunchApp(InspectorPath, asAdmin: true);
		}
	}

	private void InspectorKillProcess_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrEmpty(InspectorPath))
		{
			return;
		}
		try
		{
			Process[] processesByName = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(InspectorPath));
			foreach (Process process in processesByName)
			{
				try
				{
					process.Kill();
				}
				catch
				{
				}
			}
			InspectorIsAppRunning = false;
			InspectorAppRunningStatus = "Proceso finalizado";
			SoundService.Play(SoundType.Whoosh);
		}
		catch
		{
		}
	}

	private void InspectorTogglePin_Click(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrEmpty(InspectorPath))
		{
			TogglePinApp(InspectorPath);
		}
	}

	private void InspectorOpenLink_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrEmpty(InspectorFullClipboardText))
		{
			return;
		}
		try
		{
			using (Process.Start(new ProcessStartInfo
			{
				FileName = InspectorFullClipboardText,
				UseShellExecute = true
			}))
			{
				SafeClose();
			}
		}
		catch
		{
		}
	}

	private void AppButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (!(sender is FrameworkElement { DataContext: PinnedApp dataContext }))
		{
			return;
		}
		int num = PinnedAppsList.IndexOf(dataContext);
		if (num >= 0 && num != _selectedIndex)
		{
			if (_selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
			{
				PinnedAppsList[_selectedIndex].IsSelected = false;
			}
			_selectedIndex = num;
			IsWebSearchSelected = false;
			dataContext.IsSelected = true;
			UpdateInspector(dataContext);
		}
	}

	private void WebSearchButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (_selectedIndex >= 0 && _selectedIndex < PinnedAppsList.Count)
		{
			PinnedAppsList[_selectedIndex].IsSelected = false;
		}
		_selectedIndex = PinnedAppsList.Count;
		IsWebSearchSelected = true;
		UpdateInspector(null);
	}

	private bool IsFullscreenOrGameActive()
	{
		if (ApplicationHost.IsGameModeActive) return true;
		try
		{
			if (NativeMethods.SHQueryUserNotificationState(out int state) == 0)
			{
				return state == 2 || state == 3 || state == 4 || state == 6 || state == 7;
			}
		}
		catch { }
		return false;
	}

	private void Window_Deactivated(object? sender, EventArgs e)
	{
		if (!_isShowingDialog && !_isOpeningSubWidget)
		{
			if (IsFullscreenOrGameActive())
			{
				// En modo juego o pantalla completa, no desactivar ni cerrar la ventana por pérdida de foco.
				// Los juegos suelen disputar el foco en segundo plano; se mantiene abierto Spotlight
				// hasta que el usuario presione Escape o use el atajo de teclado para cerrarlo.
				return;
			}

			if ((DateTime.Now - _loadedTime).TotalMilliseconds < 800.0)
			{
				ForceForeground();
				FocusSearchBox();
			}
			else
			{
				SafeClose();
			}
		}
	}

	private void SafeClose()
	{
		if (_isClosing)
		{
			return;
		}
		_isClosing = true;
		_clockTimer?.Stop();
		_searchDebounceTimer?.Stop();
		_musicDebounceTimer?.Stop();
		_musicSearchCts?.Cancel();
		try
		{
			Topmost = false;
		}
		catch
		{
		}
		if (ApplicationHost.Instance != null)
		{
			ApplicationHost.Instance.IsPinnedFlyoutOpen = false;
			ApplicationHost.Instance.RecordWidgetClose("PinnedApps");
		}
		if (HasActiveMusicTrack || IsMediaPlaying || (ApplicationHost.Instance != null && ApplicationHost.Instance.IsInternalMusicActive))
		{
			try
			{
				FloatingMediaOrbWindow.ShowOrb(SatelliteMediaTitle, SatelliteMediaArtist, SatelliteMediaArtwork, IsMediaPlaying, _currentMediaSession);
			}
			catch
			{
			}
		}
		DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180.0))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseIn
			}
		};
		doubleAnimation.Completed += (object? snd, EventArgs args) =>
		{
			try
			{
				Close();
			}
			catch
			{
				_isClosing = false;
			}
		};
		BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
	}

	protected override void OnClosing(CancelEventArgs e)
	{
		if (ApplicationHost.Instance != null)
		{
			ApplicationHost.Instance.IsPinnedFlyoutOpen = false;
		}
		base.OnClosing(e);
	}

	private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
	{
	}

	private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
	{
	}

	public void LoadActiveWindows()
	{
		try
		{
			nint handle = new WindowInteropHelper(this).Handle;
			List<ActiveWindowModel> list = (_allActiveWindows = ActiveWindowManager.Instance.GetActiveWindows(handle));
			string query = SearchBox?.Text.Trim() ?? "";
			if (!string.IsNullOrEmpty(query))
			{
				list = list.Where((ActiveWindowModel w) => w.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || w.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
			}
			ActiveWindowsList.Clear();
			foreach (ActiveWindowModel item in list)
			{
				ActiveWindowsList.Add(item);
			}
			UpdateFavoritesRunningState(list);
			ActiveWindowsCountText = $"{list.Count} en ejecución";
			List<ActiveWindowModel> snapshot = list.ToList();
			Task.Run(() =>
			{
				foreach (ActiveWindowModel win in snapshot)
				{
					if (!win.IsMinimized)
					{
						try
						{
							ImageSource thumb = ActiveWindowManager.Instance.CaptureWindowThumbnail(win.Hwnd);
							if (thumb != null)
							{
								Dispatcher.InvokeAsync(() =>
								{
									win.Thumbnail = thumb;
								}, DispatcherPriority.Normal);
							}
						}
						catch
						{
						}
					}
				}
			});
		}
		catch
		{
		}
	}

	private void UpdateFavoritesRunningState(List<ActiveWindowModel> activeWindows)
	{
		try
		{
			if (FavoritesList == null || FavoritesList.Count == 0 || activeWindows == null) return;
			foreach (var fav in FavoritesList)
			{
				if (fav.Path == "ADD_APP") continue;
				string exeName = System.IO.Path.GetFileNameWithoutExtension(fav.Path);
				fav.IsRunning = activeWindows.Any(w =>
					(!string.IsNullOrEmpty(w.ProcessName) && string.Equals(w.ProcessName, exeName, StringComparison.OrdinalIgnoreCase)) ||
					(!string.IsNullOrEmpty(w.Title) && !string.IsNullOrEmpty(fav.Name) && w.Title.Contains(fav.Name, StringComparison.OrdinalIgnoreCase)));
			}
		}
		catch
		{
		}
	}

	public async void InitSatelliteMedia()
	{
		try
		{
			_mediaSessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
			if (_mediaSessionManager != null)
			{
				_mediaSessionManager.CurrentSessionChanged += (GlobalSystemMediaTransportControlsSessionManager s, CurrentSessionChangedEventArgs e) =>
				{
					Dispatcher.BeginInvoke(new Action(UpdateSatelliteMediaInfo));
				};
			}
		}
		catch
		{
		}
		UpdateSatelliteMediaInfo();
	}

	public async void UpdateSatelliteMediaInfo()
	{
		_mediaCts?.Cancel();
		CancellationToken token = (_mediaCts = new CancellationTokenSource()).Token;
		try
		{
			if (ApplicationHost.Instance?.IsInternalMusicActive ?? false)
			{
				string internalMusicTitle = ApplicationHost.Instance.InternalMusicTitle;
				if (!string.IsNullOrEmpty(internalMusicTitle))
				{
					string cleanInternalTitle = internalMusicTitle.Trim();
					if (cleanInternalTitle.EndsWith(" - YouTube", StringComparison.OrdinalIgnoreCase))
					{
						cleanInternalTitle = cleanInternalTitle.Substring(0, cleanInternalTitle.Length - " - YouTube".Length).Trim();
					}
					else if (cleanInternalTitle.EndsWith(" - YouTube Music", StringComparison.OrdinalIgnoreCase))
					{
						cleanInternalTitle = cleanInternalTitle.Substring(0, cleanInternalTitle.Length - " - YouTube Music".Length).Trim();
					}
					SatelliteMediaTitle = string.IsNullOrEmpty(cleanInternalTitle) ? "Reproductor" : cleanInternalTitle;

					string cleanInternalArtist = ApplicationHost.Instance.InternalMusicArtist?.Trim() ?? string.Empty;
					if (cleanInternalArtist.Contains("vistas", StringComparison.OrdinalIgnoreCase) ||
					    cleanInternalArtist.Contains("views", StringComparison.OrdinalIgnoreCase) ||
					    cleanInternalArtist.StartsWith("•"))
					{
						cleanInternalArtist = string.Empty;
					}
					SatelliteMediaArtist = cleanInternalArtist;
					IsMediaPlaying = ApplicationHost.Instance.IsInternalMusicPlaying;
					SatelliteMediaGlyph = (IsMediaPlaying ? "\ue12e" : "\ue13c");
					HasActiveMusicTrack = true;
					ImageSource internalMusicArtwork = ApplicationHost.Instance.InternalMusicArtwork;
					if (internalMusicArtwork != null)
					{
						SatelliteMediaArtwork = internalMusicArtwork;
					}
					if (IsMediaPlaying)
					{
						try
						{
							FloatingMediaOrbWindow.ShowOrb(SatelliteMediaTitle, SatelliteMediaArtist, SatelliteMediaArtwork, isPlaying: true);
						}
						catch { }
					}
					return;
				}
			}
			if (_mediaSessionManager != null)
			{
				_currentMediaSession = _mediaSessionManager.GetCurrentSession();
			}
			if (_currentMediaSession != null)
			{
				GlobalSystemMediaTransportControlsSessionMediaProperties globalSystemMediaTransportControlsSessionMediaProperties = await _currentMediaSession.TryGetMediaPropertiesAsync();
				if (token.IsCancellationRequested)
				{
					return;
				}
				GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo = _currentMediaSession.GetPlaybackInfo();
				bool flag = playbackInfo != null && playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
				if (globalSystemMediaTransportControlsSessionMediaProperties != null && !string.IsNullOrEmpty(globalSystemMediaTransportControlsSessionMediaProperties.Title))
				{
					string cleanTitle = globalSystemMediaTransportControlsSessionMediaProperties.Title.Trim();
					if (cleanTitle.EndsWith(" - YouTube", StringComparison.OrdinalIgnoreCase))
					{
						cleanTitle = cleanTitle.Substring(0, cleanTitle.Length - " - YouTube".Length).Trim();
					}
					else if (cleanTitle.EndsWith(" - YouTube Music", StringComparison.OrdinalIgnoreCase))
					{
						cleanTitle = cleanTitle.Substring(0, cleanTitle.Length - " - YouTube Music".Length).Trim();
					}
					SatelliteMediaTitle = string.IsNullOrEmpty(cleanTitle) ? "Reproductor" : cleanTitle;

					string cleanArtist = globalSystemMediaTransportControlsSessionMediaProperties.Artist?.Trim() ?? string.Empty;
					if (cleanArtist.Contains("vistas", StringComparison.OrdinalIgnoreCase) ||
					    cleanArtist.Contains("views", StringComparison.OrdinalIgnoreCase) ||
					    cleanArtist.Contains("suscriptor", StringComparison.OrdinalIgnoreCase) ||
					    cleanArtist.Contains("subscriber", StringComparison.OrdinalIgnoreCase) ||
					    cleanArtist.StartsWith("•"))
					{
						cleanArtist = string.Empty;
					}
					SatelliteMediaArtist = cleanArtist;
					IsMediaPlaying = flag;
					SatelliteMediaGlyph = (flag ? "\ue12e" : "\ue13c");
					HasActiveMusicTrack = true;
					if (flag)
					{
						try
						{
							FloatingMediaOrbWindow.ShowOrb(SatelliteMediaTitle, SatelliteMediaArtist, SatelliteMediaArtwork, isPlaying: true, _currentMediaSession);
						}
						catch { }
					}
					if (globalSystemMediaTransportControlsSessionMediaProperties.Thumbnail != null)
					{
						try
						{
							using (IRandomAccessStreamWithContentType stream = await globalSystemMediaTransportControlsSessionMediaProperties.Thumbnail.OpenReadAsync())
							{
								if (token.IsCancellationRequested)
								{
									return;
								}
								using MemoryStream memStream = new MemoryStream();
								await stream.AsStreamForRead().CopyToAsync(memStream);
								if (token.IsCancellationRequested)
								{
									return;
								}
								memStream.Position = 0L;
								BitmapImage bitmapImage = new BitmapImage();
								bitmapImage.BeginInit();
								bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
								bitmapImage.StreamSource = memStream;
								bitmapImage.EndInit();
								bitmapImage.Freeze();
								SatelliteMediaArtwork = bitmapImage;
							}
							return;
						}
						catch
						{
							SatelliteMediaArtwork = null;
							return;
						}
					}
					SatelliteMediaArtwork = null;
					return;
				}
			}
			SatelliteMediaTitle = "Reproductor";
			SatelliteMediaArtist = string.Empty;
			SatelliteMediaGlyph = "\ue13c";
			IsMediaPlaying = false;
			SatelliteMediaArtwork = null;
			HasActiveMusicTrack = false;
		}
		catch
		{
			SatelliteMediaTitle = "Reproductor";
			SatelliteMediaArtist = string.Empty;
			SatelliteMediaGlyph = "\ue13c";
			IsMediaPlaying = false;
			SatelliteMediaArtwork = null;
			HasActiveMusicTrack = false;
		}
	}

	private void ActiveWindow_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: ActiveWindowModel dataContext })
		{
			ActiveWindowManager.Instance.SwitchToWindow(dataContext.Hwnd);
			SoundService.Play(SoundType.Click);
			SafeClose();
		}
	}

	private void ActiveWindow_Minimize_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.Button { Tag: var tag } && tag is nint hwnd)
		{
			ActiveWindowManager.Instance.MinimizeWindow(hwnd);
			SoundService.Play(SoundType.Hover);
			LoadActiveWindows();
			e.Handled = true;
		}
	}

	private void ActiveWindow_Close_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.Button { Tag: var tag } && tag is nint)
		{
			nint hwnd = (nint)tag;
			ActiveWindowManager.Instance.CloseWindow(hwnd);
			SoundService.Play(SoundType.Click);
			ActiveWindowModel activeWindowModel = ActiveWindowsList.FirstOrDefault((ActiveWindowModel w) => w.Hwnd == hwnd);
			if (activeWindowModel != null)
			{
				ActiveWindowsList.Remove(activeWindowModel);
			}
			ActiveWindowsCountText = $"{ActiveWindowsList.Count} en ejecución";
			e.Handled = true;
		}
	}

	private async void SatelliteMedia_Prev_Click(object sender, RoutedEventArgs e)
	{
		_ = 1;
		try
		{
			SoundService.Play(SoundType.Click);
			if (ApplicationHost.Instance?.IsInternalMusicActive ?? false)
			{
				await ApplicationHost.Instance.PreviousMusicTrackAsync();
				await Task.Delay(400);
				UpdateSatelliteMediaInfo();
				return;
			}
			if (_currentMediaSession != null)
			{
				await _currentMediaSession.TrySkipPreviousAsync();
			}
			else
			{
				NativeMethods.keybd_event(177, 0, 0u, 0);
				NativeMethods.keybd_event(177, 0, 2u, 0);
			}
			await Task.Delay(350);
			UpdateSatelliteMediaInfo();
		}
		catch
		{
		}
	}

	private async void SatelliteMedia_PlayPause_Click(object sender, RoutedEventArgs e)
	{
		_ = 2;
		try
		{
			SoundService.Play(SoundType.Click);
			if (ApplicationHost.Instance?.IsInternalMusicActive ?? false)
			{
				bool flag = (IsMediaPlaying = await ApplicationHost.Instance.TogglePlayPauseMusicAsync());
				SatelliteMediaGlyph = (flag ? "\ue12e" : "\ue13c");
				await Task.Delay(350);
				UpdateSatelliteMediaInfo();
				return;
			}
			if (_currentMediaSession != null)
			{
				GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo = _currentMediaSession.GetPlaybackInfo();
				if (playbackInfo != null && playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
				{
					await _currentMediaSession.TryPauseAsync();
					IsMediaPlaying = false;
					SatelliteMediaGlyph = "\ue13c";
				}
				else
				{
					await _currentMediaSession.TryPlayAsync();
					IsMediaPlaying = true;
					SatelliteMediaGlyph = "\ue12e";
				}
			}
			else
			{
				NativeMethods.keybd_event(179, 0, 0u, 0);
				NativeMethods.keybd_event(179, 0, 2u, 0);
			}
			await Task.Delay(350);
			UpdateSatelliteMediaInfo();
		}
		catch
		{
		}
	}

	private async void SatelliteMedia_Next_Click(object sender, RoutedEventArgs e)
	{
		_ = 1;
		try
		{
			SoundService.Play(SoundType.Click);
			if (ApplicationHost.Instance?.IsInternalMusicActive ?? false)
			{
				await ApplicationHost.Instance.NextMusicTrackAsync();
				await Task.Delay(400);
				UpdateSatelliteMediaInfo();
				return;
			}
			if (_currentMediaSession != null)
			{
				await _currentMediaSession.TrySkipNextAsync();
			}
			else
			{
				NativeMethods.keybd_event(176, 0, 0u, 0);
				NativeMethods.keybd_event(176, 0, 2u, 0);
			}
			await Task.Delay(350);
			UpdateSatelliteMediaInfo();
		}
		catch
		{
		}
	}

	private void SatelliteMedia_OpenYtMusic_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			_isOpeningSubWidget = true;
			ApplicationHost.Instance?.ToggleWidget("YouTube");
			Task.Delay(1500).ContinueWith((Task _) => Dispatcher.Invoke(() => _isOpeningSubWidget = false));
		}
		catch
		{
		}
	}

	private void SatelliteMedia_ToggleCard_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			if (IsPlayerExpandedVisibility == Visibility.Visible)
			{
				IsPlayerExpandedVisibility = Visibility.Collapsed;
				return;
			}
			if (WallpaperFanCardVisibility == Visibility.Visible)
			{
				WallpaperFanCardVisibility = Visibility.Collapsed;
				RestoreCenterContentView();
			}
			NotificationsCardVisibility = Visibility.Collapsed;
			ConnectedDevicesCardVisibility = Visibility.Collapsed;
			IsPlayerExpandedVisibility = Visibility.Visible;
		}
		catch
		{
		}
	}

	private void SatelliteWallpaper_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			ToggleWallpaperFanCard();
		}
		catch
		{
		}
	}

	private void SatelliteWallpaper_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			BentoAction_Wallpaper_Click(sender, e);
			_ = LoadActiveWallpaperAsync(force: true);
			if (WallpaperFanCardVisibility == Visibility.Visible)
			{
				_ = LoadFanWallpapersAsync();
			}
			e.Handled = true;
		}
		catch
		{
		}
	}

	private void SatelliteNotifications_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			ToggleNotificationsCard();
		}
		catch
		{
		}
	}

	private void ToggleNotificationsCard()
	{
		if (NotificationsCardVisibility == Visibility.Visible)
		{
			NotificationsCardVisibility = Visibility.Collapsed;
			return;
		}
		if (WallpaperFanCardVisibility == Visibility.Visible)
		{
			WallpaperFanCardVisibility = Visibility.Collapsed;
			RestoreCenterContentView();
		}
		IsPlayerExpandedVisibility = Visibility.Collapsed;
		ConnectedDevicesCardVisibility = Visibility.Collapsed;
		NotificationsCardVisibility = Visibility.Visible;
		_ = LoadNotificationsAsync();
	}

	private void CloseNotificationsCard_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			NotificationsCardVisibility = Visibility.Collapsed;
		}
		catch
		{
		}
	}

	private void ClearAllNotifications_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			NotificationsList.Clear();
			HasUnreadNotifications = false;
			HasNoNotifications = true;
			NotificationsCountText = "0 notificaciones";
			try
			{
				UserNotificationListener.Current.ClearNotifications();
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void DismissNotification_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!(sender is System.Windows.Controls.Button { Tag: var tag }) || !(tag is uint))
			{
				return;
			}
			uint notifId = (uint)tag;
			SpotlightNotificationItem spotlightNotificationItem = NotificationsList.FirstOrDefault((SpotlightNotificationItem n) => n.Id == notifId);
			if (spotlightNotificationItem != null)
			{
				NotificationsList.Remove(spotlightNotificationItem);
				try
				{
					UserNotificationListener.Current.RemoveNotification(notifId);
				}
				catch
				{
				}
				HasUnreadNotifications = NotificationsList.Count > 0;
				HasNoNotifications = NotificationsList.Count == 0;
				NotificationsCountText = NotificationsList.Count switch
				{
					0 => "0 notificaciones", 
					1 => "1 notificación", 
					_ => $"{NotificationsList.Count} notificaciones", 
				};
			}
		}
		catch
		{
		}
	}

	private void OpenWindowsNotificationCenter_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			byte bVk = (byte)((Environment.OSVersion.Version.Build >= 22000) ? 78u : 65u);
			NativeMethods.keybd_event(91, 0, 0u, 0);
			NativeMethods.keybd_event(bVk, 0, 0u, 0);
			NativeMethods.keybd_event(bVk, 0, 2u, 0);
			NativeMethods.keybd_event(91, 0, 2u, 0);
			SafeClose();
		}
		catch
		{
		}
	}

	private async Task LoadNotificationsAsync()
	{
		_ = 1;
		try
		{
			IsNotificationsLoading = true;
			UserNotificationListener listener = UserNotificationListener.Current;
			if (await listener.RequestAccessAsync() != UserNotificationListenerAccessStatus.Allowed)
			{
				Dispatcher.Invoke(() =>
				{
					NotificationsCountText = "Sin acceso";
					HasUnreadNotifications = false;
					HasNoNotifications = true;
				});
				return;
			}
			IReadOnlyList<UserNotification> rawNotifs = await listener.GetNotificationsAsync(NotificationKinds.Toast);
			Dispatcher.Invoke(() =>
			{
				NotificationsList.Clear();
				if (rawNotifs != null && rawNotifs.Count > 0)
				{
					foreach (UserNotification item in rawNotifs.OrderByDescending((UserNotification n) => n.CreationTime))
					{
						string text = "Sistema";
						try
						{
							text = item.AppInfo?.DisplayInfo?.DisplayName ?? "Sistema";
						}
						catch
						{
						}
						string text2 = string.Empty;
						string text3 = string.Empty;
						try
						{
							NotificationBinding notificationBinding = item.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
							if (notificationBinding != null)
							{
								IReadOnlyList<AdaptiveNotificationText> textElements = notificationBinding.GetTextElements();
								if (textElements != null && textElements.Count > 0)
								{
									text2 = textElements[0]?.Text ?? string.Empty;
									if (textElements.Count > 1)
									{
										text3 = string.Join(" ", from t in textElements.Skip(1)
											select t?.Text ?? string.Empty into s
											where !string.IsNullOrWhiteSpace(s)
											select s);
									}
								}
							}
						}
						catch
						{
						}
						if (string.IsNullOrWhiteSpace(text2) && string.IsNullOrWhiteSpace(text3))
						{
							text2 = text;
						}
						TimeSpan timeSpan = DateTimeOffset.Now - item.CreationTime;
						string timeText = ((!(timeSpan.TotalMinutes < 1.0)) ? ((!(timeSpan.TotalMinutes < 60.0)) ? ((!(timeSpan.TotalHours < 24.0)) ? item.CreationTime.ToLocalTime().ToString("dd/MM HH:mm") : item.CreationTime.ToLocalTime().ToString("HH:mm")) : $"hace {(int)timeSpan.TotalMinutes}m") : "Ahora");
						string notificationGlyphForApp = GetNotificationGlyphForApp(text);
						NotificationsList.Add(new SpotlightNotificationItem
						{
							Id = item.Id,
							AppName = text,
							Title = text2,
							Body = text3,
							TimeText = timeText,
							AppGlyph = notificationGlyphForApp
						});
					}
				}
				HasUnreadNotifications = NotificationsList.Count > 0;
				HasNoNotifications = NotificationsList.Count == 0;
				string notificationsCountText = NotificationsList.Count switch
				{
					0 => "0 notificaciones", 
					1 => "1 notificación", 
					_ => $"{NotificationsList.Count} notificaciones", 
				};
				NotificationsCountText = notificationsCountText;
			});
		}
		catch (Exception)
		{
			Dispatcher.Invoke(() =>
			{
				HasUnreadNotifications = false;
				HasNoNotifications = true;
				NotificationsCountText = "0 notificaciones";
			});
		}
		finally
		{
			Dispatcher.Invoke(() => IsNotificationsLoading = false);
		}
	}

	private static string GetNotificationGlyphForApp(string appName)
	{
		string text = appName.ToLowerInvariant();
		if (text.Contains("chrome") || text.Contains("edge") || text.Contains("firefox") || text.Contains("brave") || text.Contains("browser"))
		{
			return "\uE774";
		}
		if (text.Contains("mail") || text.Contains("correo") || text.Contains("outlook") || text.Contains("thunderbird"))
		{
			return "\uE715";
		}
		if (text.Contains("whatsapp") || text.Contains("telegram") || text.Contains("discord") || text.Contains("slack") || text.Contains("chat") || text.Contains("teams"))
		{
			return "\uE8BD";
		}
		if (text.Contains("calendar") || text.Contains("calendario"))
		{
			return "\uE787";
		}
		if (text.Contains("spotify") || text.Contains("music") || text.Contains("música") || text.Contains("youtube"))
		{
			return "\uE8D6";
		}
		if (text.Contains("download") || text.Contains("descarga"))
		{
			return "\uE896";
		}
		if (text.Contains("seguridad") || text.Contains("defender") || text.Contains("security") || text.Contains("antivirus"))
		{
			return "\uEA18";
		}
		if (text.Contains("sistema") || text.Contains("system") || text.Contains("windows"))
		{
			return "\uE713";
		}
		return "\uEA8F";
	}

	private void SatelliteDevices_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			ToggleConnectedDevicesCard();
		}
		catch
		{
		}
	}

	private void ToggleConnectedDevicesCard()
	{
		if (ConnectedDevicesCardVisibility == Visibility.Visible)
		{
			ConnectedDevicesCardVisibility = Visibility.Collapsed;
			return;
		}
		if (WallpaperFanCardVisibility == Visibility.Visible)
		{
			WallpaperFanCardVisibility = Visibility.Collapsed;
			RestoreCenterContentView();
		}
		IsPlayerExpandedVisibility = Visibility.Collapsed;
		NotificationsCardVisibility = Visibility.Collapsed;
		ConnectedDevicesCardVisibility = Visibility.Visible;
		_ = LoadConnectedDevicesAsync();
	}

	private void CloseConnectedDevicesCard_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			ConnectedDevicesCardVisibility = Visibility.Collapsed;
		}
		catch
		{
		}
	}

	private void RefreshConnectedDevices_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			_ = LoadConnectedDevicesAsync();
		}
		catch
		{
		}
	}

	private void OpenBluetoothSettings_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			Process.Start(new ProcessStartInfo("ms-settings:bluetooth")
			{
				UseShellExecute = true
			});
			SafeClose();
		}
		catch
		{
		}
	}

	private void OpenSoundSettings_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			Process.Start(new ProcessStartInfo("ms-settings:sound")
			{
				UseShellExecute = true
			});
			SafeClose();
		}
		catch
		{
		}
	}

	private async Task LoadConnectedDevicesAsync()
	{
		_ = 3;
		try
		{
			IsConnectedDevicesLoading = true;
			List<SpotlightConnectedDeviceItem> items = new List<SpotlightConnectedDeviceItem>();
			HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				DeviceInformationCollection deviceInformationCollection = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
				string b = string.Empty;
				try
				{
					b = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
				}
				catch
				{
				}
				foreach (DeviceInformation item in deviceInformationCollection)
				{
					if (item.IsEnabled && !string.IsNullOrWhiteSpace(item.Name))
					{
						string text = item.Name.Trim();
						if (seenNames.Add(text))
						{
							bool flag = string.Equals(item.Id, b, StringComparison.OrdinalIgnoreCase);
							var (glyph, category) = ClassifyAudioDevice(text, isInput: false);
							items.Add(new SpotlightConnectedDeviceItem
							{
								Id = item.Id,
								Name = text,
								Category = category,
								Glyph = glyph,
								IsDefault = flag,
								StatusText = (flag ? "Predeterminado" : "Conectado"),
								DetailsText = (flag ? "Salida de audio principal" : "Salida de audio"),
								IsConnected = true
							});
						}
					}
				}
			}
			catch (Exception)
			{
			}
			try
			{
				DeviceInformationCollection deviceInformationCollection2 = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioCaptureSelector());
				string b2 = string.Empty;
				try
				{
					b2 = MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Default);
				}
				catch
				{
				}
				foreach (DeviceInformation item2 in deviceInformationCollection2)
				{
					if (item2.IsEnabled && !string.IsNullOrWhiteSpace(item2.Name))
					{
						string text2 = item2.Name.Trim();
						if (seenNames.Add(text2))
						{
							bool flag2 = string.Equals(item2.Id, b2, StringComparison.OrdinalIgnoreCase);
							items.Add(new SpotlightConnectedDeviceItem
							{
								Id = item2.Id,
								Name = text2,
								Category = "Micrófono",
								Glyph = "\uE720",
								IsDefault = flag2,
								StatusText = (flag2 ? "Predeterminado" : "Conectado"),
								DetailsText = (flag2 ? "Entrada de voz principal" : "Entrada de voz"),
								IsConnected = true
							});
						}
					}
				}
			}
			catch (Exception)
			{
			}
			try
			{
				foreach (DeviceInformation item3 in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected)))
				{
					if (!string.IsNullOrWhiteSpace(item3.Name))
					{
						string text3 = item3.Name.Trim();
						if (seenNames.Add(text3))
						{
							var (glyph2, category2) = ClassifyPeripheral(text3);
							items.Add(new SpotlightConnectedDeviceItem
							{
								Id = item3.Id,
								Name = text3,
								Category = category2,
								Glyph = glyph2,
								StatusText = "Conectado",
								DetailsText = "Bluetooth",
								IsConnected = true
							});
						}
					}
				}
			}
			catch (Exception)
			{
			}
			try
			{
				foreach (DeviceInformation item4 in await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected)))
				{
					if (!string.IsNullOrWhiteSpace(item4.Name))
					{
						string text4 = item4.Name.Trim();
						if (seenNames.Add(text4))
						{
							var (glyph3, category3) = ClassifyPeripheral(text4);
							items.Add(new SpotlightConnectedDeviceItem
							{
								Id = item4.Id,
								Name = text4,
								Category = category3,
								Glyph = glyph3,
								StatusText = "Conectado",
								DetailsText = "Bluetooth LE",
								IsConnected = true
							});
						}
					}
				}
			}
			catch (Exception)
			{
			}
			try
			{
				IReadOnlyList<RawGameController> rawGameControllers = RawGameController.RawGameControllers;
				if (rawGameControllers != null)
				{
					foreach (RawGameController item5 in rawGameControllers)
					{
						string text5 = (string.IsNullOrWhiteSpace(item5.DisplayName) ? "Mando de juego" : item5.DisplayName.Trim());
						if (seenNames.Add(text5))
						{
							items.Add(new SpotlightConnectedDeviceItem
							{
								Id = (item5.NonRoamableId ?? Guid.NewGuid().ToString()),
								Name = text5,
								Category = "Mando / Control",
								Glyph = "\uE7FC",
								StatusText = "Conectado",
								DetailsText = "Listo para jugar",
								IsConnected = true
							});
						}
					}
				}
			}
			catch (Exception)
			{
			}
			try
			{
				DriveInfo[] drives = DriveInfo.GetDrives();
				foreach (DriveInfo driveInfo in drives)
				{
					if (driveInfo.DriveType == DriveType.Removable && driveInfo.IsReady)
					{
						string text6 = driveInfo.Name.TrimEnd('\\');
						string text7 = (string.IsNullOrWhiteSpace(driveInfo.VolumeLabel) ? ("Disco Extraíble (" + text6 + ")") : (driveInfo.VolumeLabel + " (" + text6 + ")"));
						if (seenNames.Add(text7))
						{
							string text8 = FormatBytes(driveInfo.AvailableFreeSpace);
							string text9 = FormatBytes(driveInfo.TotalSize);
							items.Add(new SpotlightConnectedDeviceItem
							{
								Id = driveInfo.Name,
								Name = text7,
								Category = "Almacenamiento USB",
								Glyph = "\uE8B7",
								StatusText = "Listo",
								DetailsText = text8 + " libres de " + text9,
								IsConnected = true
							});
						}
					}
				}
			}
			catch (Exception)
			{
			}
			List<SpotlightConnectedDeviceItem> sorted = items.OrderBy((SpotlightConnectedDeviceItem item) => GetSortPriority(item)).ThenBy((SpotlightConnectedDeviceItem spotlightConnectedDeviceItem) => spotlightConnectedDeviceItem.Name).ToList();
			Dispatcher.Invoke(() =>
			{
				ConnectedDevicesList.Clear();
				foreach (SpotlightConnectedDeviceItem item6 in sorted)
				{
					ConnectedDevicesList.Add(item6);
				}
				HasConnectedDevices = ConnectedDevicesList.Count > 0;
				HasNoConnectedDevices = ConnectedDevicesList.Count == 0;
				string connectedDevicesCountText = ConnectedDevicesList.Count switch
				{
					0 => "0 dispositivos", 
					1 => "1 dispositivo", 
					_ => $"{ConnectedDevicesList.Count} dispositivos", 
				};
				ConnectedDevicesCountText = connectedDevicesCountText;
			});
		}
		catch (Exception)
		{
			Dispatcher.Invoke(() =>
			{
				HasConnectedDevices = false;
				HasNoConnectedDevices = true;
				ConnectedDevicesCountText = "0 dispositivos";
			});
		}
		finally
		{
			Dispatcher.Invoke(() => IsConnectedDevicesLoading = false);
		}
	}

	private static (string glyph, string category) ClassifyAudioDevice(string name, bool isInput)
	{
		if (isInput)
		{
			return (glyph: "\uE720", category: "Micrófono");
		}
		string text = name.ToLowerInvariant();
		if (text.Contains("headphone") || text.Contains("auricular") || text.Contains("auriculares") || text.Contains("headset") || text.Contains("earphone") || text.Contains("airpod") || text.Contains("buds") || text.Contains("freebuds") || text.Contains("wh-1000") || text.Contains("e-3105") || text.Contains("cascos") || text.Contains("in-ear"))
		{
			return (glyph: "\uE7F6", category: "Audio (Auriculares)");
		}
		return (glyph: "\uE767", category: "Audio (Altavoces)");
	}

	private static (string glyph, string category) ClassifyPeripheral(string name)
	{
		string text = name.ToLowerInvariant();
		if (text.Contains("headphone") || text.Contains("auricular") || text.Contains("auriculares") || text.Contains("headset") || text.Contains("airpod") || text.Contains("buds") || text.Contains("freebuds") || text.Contains("earphone") || text.Contains("e-3105"))
		{
			return (glyph: "\uE7F6", category: "Audio (Auriculares)");
		}
		if (text.Contains("teclado") || text.Contains("keyboard") || text.Contains("ak820") || text.Contains("keychron") || text.Contains("k380") || text.Contains("rk61") || text.Contains("keypad"))
		{
			return (glyph: "\uE765", category: "Teclado");
		}
		if (text.Contains("ratón") || text.Contains("raton") || text.Contains("mouse") || text.Contains("aj139") || text.Contains("trackpad") || text.Contains("touchpad") || text.Contains("mx master") || text.Contains("deathadder") || text.Contains("viper"))
		{
			return (glyph: "\uE962", category: "Ratón");
		}
		if (text.Contains("mando") || text.Contains("gamepad") || text.Contains("controller") || text.Contains("xbox") || text.Contains("gamesir") || text.Contains("dualsense") || text.Contains("dualshock") || text.Contains("joystick"))
		{
			return (glyph: "\uE7FC", category: "Mando / Control");
		}
		if (text.Contains("phone") || text.Contains("celular") || text.Contains("móvil") || text.Contains("movil") || text.Contains("galaxy") || text.Contains("iphone") || text.Contains("pixel") || text.Contains("xiaomi") || text.Contains("huawei"))
		{
			return (glyph: "\uE8EA", category: "Teléfono");
		}
		if (text.Contains("speaker") || text.Contains("altavoz") || text.Contains("bocina"))
		{
			return (glyph: "\uE767", category: "Audio (Altavoz)");
		}
		return (glyph: "\uE702", category: "Dispositivo Bluetooth");
	}

	private static int GetSortPriority(SpotlightConnectedDeviceItem item)
	{
		if (item.IsDefault)
		{
			return 0;
		}
		if (item.Category.StartsWith("Audio (Auriculares)"))
		{
			return 1;
		}
		if (item.Category.StartsWith("Audio"))
		{
			return 2;
		}
		if (item.Category.StartsWith("Mando"))
		{
			return 3;
		}
		if (item.Category == "Teclado")
		{
			return 4;
		}
		if (item.Category == "Ratón")
		{
			return 5;
		}
		if (item.Category == "Micrófono")
		{
			return 6;
		}
		if (item.Category.StartsWith("Almacenamiento"))
		{
			return 7;
		}
		return 8;
	}

	private void SatelliteVolume_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			NativeMethods.keybd_event(173, 0, 0u, 0);
			NativeMethods.keybd_event(173, 0, 2u, 0);
			SoundService.Play(SoundType.Click);
		}
		catch
		{
		}
	}

	private void SatelliteRecycleBin_Click(object sender, RoutedEventArgs e)
	{
		BentoAction_RecycleBin_Click(sender, e);
	}

	private void SatelliteLock_Click(object sender, RoutedEventArgs e)
	{
		BentoAction_Lock_Click(sender, e);
	}

	private void SatelliteAi_Click(object sender, RoutedEventArgs e)
	{
		SetSearchMode("AI");
		SearchBox?.Focus();
	}

	public void ToggleWallpaperFanCard()
	{
		if (WallpaperFanCardVisibility == Visibility.Visible)
		{
			WallpaperFanCardVisibility = Visibility.Collapsed;
			RestoreCenterContentView();
			return;
		}
		IsPlayerExpandedVisibility = Visibility.Collapsed;
		ConnectedDevicesCardVisibility = Visibility.Collapsed;
		NotificationsCardVisibility = Visibility.Collapsed;
		ActiveWindowsVisibility = Visibility.Collapsed;
		SpotlightFavoritesVisibility = Visibility.Collapsed;
		if (ResultsCardBorder != null)
		{
			ResultsCardBorder.Visibility = Visibility.Collapsed;
		}
		WallpaperFanCardVisibility = Visibility.Visible;
		_ = LoadFanWallpapersAsync();
	}

	private void RestoreCenterContentView()
	{
		if (string.IsNullOrEmpty((SearchBox != null) ? SearchBox.Text.Trim() : ""))
		{
			SpotlightFavoritesVisibility = ((!SettingsService.Instance.SpotlightShowFavorites) ? Visibility.Collapsed : Visibility.Visible);
			ActiveWindowsVisibility = Visibility.Visible;
			if (ResultsCardBorder != null)
			{
				ResultsCardBorder.Visibility = Visibility.Collapsed;
			}
		}
		else
		{
			ActiveWindowsVisibility = ((!(_searchMode == "Windows")) ? Visibility.Collapsed : Visibility.Visible);
			if (ResultsCardBorder != null)
			{
				ResultsCardBorder.Visibility = ((_searchMode == "Windows") ? Visibility.Collapsed : Visibility.Visible);
			}
		}
	}

	private void CloseWallpaperFanCard_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			WallpaperFanCardVisibility = Visibility.Collapsed;
			RestoreCenterContentView();
		}
		catch
		{
		}
	}

	private async Task LoadFanWallpapersAsync()
	{
		try
		{
			string dir = SettingsService.Instance.WallpaperDirectory;
			if ((string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) && !string.IsNullOrEmpty(_currentWallpaperPath) && File.Exists(_currentWallpaperPath))
			{
				string directoryName = Path.GetDirectoryName(_currentWallpaperPath);
				if (!string.IsNullOrEmpty(directoryName) && Directory.Exists(directoryName))
				{
					dir = directoryName;
				}
			}
			if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
			{
				FanWallpapersList.Clear();
				HasFanWallpapers = false;
				FanWallpapersCountText = "0 Fondos";
				ActiveFanTitleText = "Carpeta no configurada";
				ActiveFanMetaText = "Configura tu carpeta en Ajustes o descarga fondos 4K";
				FanCounterText = "0 / 0";
				return;
			}

			string currentWp = _currentWallpaperPath;
			HashSet<string> exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };
			var (items, targetIndex) = await Task.Run(() =>
			{
				try
				{
					var files = (from f in Directory.GetFiles(dir)
						where exts.Contains(Path.GetExtension(f))
						orderby File.GetLastWriteTimeUtc(f) descending
						select f).ToList();

					var list = new List<WallpaperFanItem>(files.Count);
					int selectedIdx = 0;

					for (int i = 0; i < files.Count; i++)
					{
						string text = files[i];
						var fileInfo = new FileInfo(text);
						string displayTitle = Path.GetFileNameWithoutExtension(text).Replace('_', ' ').Replace('-', ' ');
						string text2 = ((double)fileInfo.Length / 1048576.0).ToString("F1") + " MB";
						var item = new WallpaperFanItem
						{
							FilePath = text,
							FileName = fileInfo.Name,
							DisplayTitle = displayTitle,
							FileSizeText = text2,
							ResolutionText = "Ultra HD • " + text2,
							BadgeText = ((fileInfo.Length > 4194304) ? "4K UHD" : "HD"),
							Visibility = Visibility.Collapsed,
							Opacity = 0.0,
							Scale = 0.4
						};
						if (!string.IsNullOrEmpty(currentWp) && string.Equals(text, currentWp, StringComparison.OrdinalIgnoreCase))
						{
							selectedIdx = i;
						}
						list.Add(item);
					}
					return (list, selectedIdx);
				}
				catch
				{
					return (new List<WallpaperFanItem>(), 0);
				}
			});

			if (items.Count == 0)
			{
				FanWallpapersList.Clear();
				HasFanWallpapers = false;
				FanWallpapersCountText = "0 Fondos";
				ActiveFanTitleText = "Sin imágenes encontradas";
				ActiveFanMetaText = dir;
				FanCounterText = "0 / 0";
				return;
			}

			// If directory and count didn't change and we already loaded items, just sync the index
			if (_lastLoadedFanDir == dir && FanWallpapersList.Count == items.Count)
			{
				HasFanWallpapers = true;
				FanWallpapersCountText = $"{items.Count} Fondos";
				_fanCurrentIndex = Math.Clamp(targetIndex, 0, FanWallpapersList.Count - 1);
				UpdateFanDeckTransforms();
				EnsureVisibleThumbnails();
				return;
			}

			_lastLoadedFanDir = dir;
			_lastLoadedFanCount = items.Count;

			FanWallpapersList.Clear();
			foreach (var item in items)
			{
				FanWallpapersList.Add(item);
			}

			HasFanWallpapers = true;
			FanWallpapersCountText = $"{items.Count} Fondos";
			_fanCurrentIndex = Math.Clamp(targetIndex, 0, FanWallpapersList.Count - 1);
			UpdateFanDeckTransforms();
			EnsureVisibleThumbnails();
		}
		catch
		{
		}
	}

	private void UpdateFanDeckTransforms()
	{
		if (FanWallpapersList.Count == 0)
		{
			return;
		}
		for (int i = 0; i < FanWallpapersList.Count; i++)
		{
			WallpaperFanItem wallpaperFanItem = FanWallpapersList[i];
			int num = i - _fanCurrentIndex;
			int num2 = Math.Abs(num);
			if (num2 > 4)
			{
				wallpaperFanItem.Visibility = Visibility.Collapsed;
				wallpaperFanItem.Opacity = 0.0;
				wallpaperFanItem.Scale = 0.4;
				wallpaperFanItem.TranslateX = (double)Math.Sign(num) * 750.0;
				wallpaperFanItem.TranslateY = 160.0;
				wallpaperFanItem.RotationAngle = (double)Math.Sign(num) * 35.0;
				wallpaperFanItem.ZIndex = 0;
				wallpaperFanItem.IsHero = false;
			}
			else
			{
				wallpaperFanItem.Visibility = Visibility.Visible;
				wallpaperFanItem.RotationAngle = (double)num * 7.5;
				wallpaperFanItem.TranslateX = (double)num * 150.0;
				wallpaperFanItem.TranslateY = (double)num2 * 22.0 + (double)(num * num) * 2.0;
				wallpaperFanItem.Scale = ((num == 0) ? 1.15 : Math.Max(0.6, 0.92 - (double)num2 * 0.08));
				wallpaperFanItem.Opacity = ((num == 0) ? 1.0 : Math.Max(0.35, 0.95 - (double)num2 * 0.15));
				wallpaperFanItem.ZIndex = 80 - num2 * 5;
				wallpaperFanItem.IsHero = num == 0;
			}
		}
		if (_fanCurrentIndex >= 0 && _fanCurrentIndex < FanWallpapersList.Count)
		{
			WallpaperFanItem wallpaperFanItem2 = FanWallpapersList[_fanCurrentIndex];
			ActiveFanTitleText = wallpaperFanItem2.DisplayTitle;
			ActiveFanMetaText = $"{wallpaperFanItem2.BadgeText} • {wallpaperFanItem2.FileSizeText} • {wallpaperFanItem2.FileName}";
			FanCounterText = $"{_fanCurrentIndex + 1} / {FanWallpapersList.Count}";
		}
	}

	private void EnsureVisibleThumbnails()
	{
		if (FanWallpapersList.Count == 0)
		{
			return;
		}
		int num = Math.Max(0, _fanCurrentIndex - 5);
		int num2 = Math.Min(FanWallpapersList.Count - 1, _fanCurrentIndex + 5);
		for (int i = num; i <= num2; i++)
		{
			WallpaperFanItem wallpaperFanItem = FanWallpapersList[i];
			if (wallpaperFanItem.Thumbnail == null && !string.IsNullOrEmpty(wallpaperFanItem.FilePath))
			{
				bool shouldLoad = false;
				lock (_inFlightThumbnails)
				{
					if (_inFlightThumbnails.Add(wallpaperFanItem.FilePath))
					{
						shouldLoad = true;
					}
				}
				if (shouldLoad)
				{
					_ = LoadThumbnailForItemAsync(wallpaperFanItem);
				}
			}
		}
	}

	private async Task LoadThumbnailForItemAsync(WallpaperFanItem item)
	{
		try
		{
			BitmapImage? bmp = await ThumbnailCacheService.Instance.GetThumbnailAsync(item.FilePath, 500);
			if (bmp != null)
			{
				await Dispatcher.InvokeAsync(() =>
				{
					item.Thumbnail = bmp;
				}, DispatcherPriority.Background);
			}
		}
		catch
		{
		}
		finally
		{
			lock (_inFlightThumbnails)
			{
				_inFlightThumbnails.Remove(item.FilePath);
			}
		}
	}

	private void NextFanWallpaper()
	{
		if (FanWallpapersList.Count != 0)
		{
			SoundService.Play(SoundType.Click);
			_fanCurrentIndex = (_fanCurrentIndex + 1) % FanWallpapersList.Count;
			UpdateFanDeckTransforms();
			EnsureVisibleThumbnails();
		}
	}

	private void PrevFanWallpaper()
	{
		if (FanWallpapersList.Count != 0)
		{
			SoundService.Play(SoundType.Click);
			_fanCurrentIndex = (_fanCurrentIndex - 1 + FanWallpapersList.Count) % FanWallpapersList.Count;
			UpdateFanDeckTransforms();
			EnsureVisibleThumbnails();
		}
	}

	private void ApplyActiveFanWallpaper()
	{
		if (FanWallpapersList.Count == 0 || _fanCurrentIndex < 0 || _fanCurrentIndex >= FanWallpapersList.Count)
		{
			return;
		}
		WallpaperFanItem wallpaperFanItem = FanWallpapersList[_fanCurrentIndex];
		if (string.IsNullOrEmpty(wallpaperFanItem.FilePath) || !File.Exists(wallpaperFanItem.FilePath))
		{
			return;
		}
		try
		{
			SoundService.Play(SoundType.Ping);
			ActiveFanTitleText = "¡Aplicado al Escritorio! " + wallpaperFanItem.DisplayTitle;

			if (wallpaperFanItem.Thumbnail != null)
			{
				BentoWallpaperSource = wallpaperFanItem.Thumbnail;
				BentoWallpaperName = wallpaperFanItem.DisplayTitle;
			}

			string path = wallpaperFanItem.FilePath;
			WallpaperTransitionService.ApplyWallpaper(path, () =>
			{
				EventBus.Publish(new WallpaperChangedEvent(path));
			});
		}
		catch
		{
		}
	}

	private void ApplyActiveFanWallpaper_Click(object sender, RoutedEventArgs e)
	{
		ApplyActiveFanWallpaper();
	}

	private void FanCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: WallpaperFanItem dataContext })
		{
			int num = FanWallpapersList.IndexOf(dataContext);
			if (num >= 0)
			{
				SoundService.Play(SoundType.Click);
				_fanCurrentIndex = num;
				UpdateFanDeckTransforms();
				EnsureVisibleThumbnails();
				ApplyActiveFanWallpaper();
				e.Handled = true;
			}
		}
	}

	private void FanStage_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (FanWallpapersList.Count != 0)
		{
			long now = Environment.TickCount64;
			if (now - _lastWheelTicks < 50)
			{
				e.Handled = true;
				return;
			}
			_lastWheelTicks = now;

			if (e.Delta < 0)
			{
				NextFanWallpaper();
			}
			else if (e.Delta > 0)
			{
				PrevFanWallpaper();
			}
			e.Handled = true;
		}
	}

	private void FanPrev_Click(object sender, RoutedEventArgs e)
	{
		PrevFanWallpaper();
	}

	private void FanNext_Click(object sender, RoutedEventArgs e)
	{
		NextFanWallpaper();
	}

	private void FanRandomWallpaper_Click(object sender, RoutedEventArgs e)
	{
		if (FanWallpapersList.Count > 1)
		{
			Random random = new Random();
			int num;
			do
			{
				num = random.Next(FanWallpapersList.Count);
			}
			while (num == _fanCurrentIndex);
			SoundService.Play(SoundType.Whoosh);
			_fanCurrentIndex = num;
			UpdateFanDeckTransforms();
			EnsureVisibleThumbnails();
		}
	}

	private void FanLaunchAltW_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			_isOpeningSubWidget = true;
			ApplicationHost.Instance?.ToggleWidget("LocalGallery");
			SafeClose();
		}
		catch
		{
		}
	}

	private void FanLaunchUHDPaper_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			SoundService.Play(SoundType.Click);
			_isOpeningSubWidget = true;
			ApplicationHost.Instance?.ToggleWidget("OnlineGallery");
			SafeClose();
		}
		catch
		{
		}
	}

	private void FanOpenFolder_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			string wallpaperDirectory = SettingsService.Instance.WallpaperDirectory;
			if (!string.IsNullOrEmpty(wallpaperDirectory) && Directory.Exists(wallpaperDirectory))
			{
				Process.Start("explorer.exe", "\"" + wallpaperDirectory + "\"");
				SoundService.Play(SoundType.Click);
			}
		}
		catch
		{
		}
	}

	protected void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	void IWidget.Close()
	{
		Close();
	}
}
