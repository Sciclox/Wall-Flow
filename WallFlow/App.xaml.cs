using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using WallFlow.ViewModels;
using WallFlow.Services;

namespace WallFlow;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    private TrayIcon? _trayIcon;
    private LocalGalleryWindow? _mainWindow;
    private DockWindow? _dockWindow;
    private static Mutex? _appMutex;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

    private static readonly IntPtr HWND_BROADCAST = (IntPtr)0xffff;
    private const uint WM_COPYDATA = 0x004A;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    public static string? InitialUrl { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) => LogCrash(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (sender, args) => {
            LogCrash(args.Exception);
            // Suppress Storyboard namescope resolution errors from ControlTemplate triggers
            // (.NET 8 does not always register x:Name within nested ControlTemplate visual trees)
            if (args.Exception is InvalidOperationException ioe &&
                ioe.StackTrace != null &&
                ioe.StackTrace.Contains("Storyboard.ResolveTargetName"))
            {
                args.Handled = true;
            }
        };

        // Parse command line URL/file path if present
        string? targetUrl = null;
        if (e.Args.Length > 0)
        {
            string arg = e.Args[0].Trim();
            if (!string.IsNullOrEmpty(arg) && !arg.Equals("--startup", StringComparison.OrdinalIgnoreCase))
            {
                targetUrl = arg;
                // If it is a local file path, convert to file:/// URI
                try
                {
                    if (File.Exists(targetUrl))
                    {
                        targetUrl = new Uri(Path.GetFullPath(targetUrl)).AbsoluteUri;
                    }
                }
                catch { }
            }
        }

        const string mutexName = "Global\\WallFlow_SingleInstance_Mutex";
        _appMutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            // If there's an argument to pass, send it via WM_COPYDATA to the running instance
            if (!string.IsNullOrEmpty(targetUrl))
            {
                try
                {
                    IntPtr targetHWnd = FindWindow(null, "WallFlow Dock");
                    if (targetHWnd == IntPtr.Zero)
                    {
                        targetHWnd = FindWindow(null, "WallFlow");
                    }

                    if (targetHWnd != IntPtr.Zero)
                    {
                        int numBytes = (targetUrl.Length + 1) * 2;
                        IntPtr ptr = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(targetUrl);
                        var cds = new COPYDATASTRUCT
                        {
                            dwData = IntPtr.Zero,
                            cbData = numBytes,
                            lpData = ptr
                        };
                        SendMessage(targetHWnd, WM_COPYDATA, IntPtr.Zero, ref cds);
                        System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
                    }
                }
                catch { }
            }

            uint msgId = RegisterWindowMessage("WallFlow_Activate_Instance");
            PostMessage(HWND_BROADCAST, msgId, IntPtr.Zero, IntPtr.Zero);
            Shutdown();
            return;
        }

        if (!string.IsNullOrEmpty(targetUrl))
        {
            InitialUrl = targetUrl;
        }

        // Apply saved font family
        try { ApplyFont(Services.SettingsService.Instance.AppFontFamily); } catch { }

        // Preload sounds asynchronously on startup to prevent UI lag on hover
        Services.SoundService.PreloadAsync();

        // Preload pinned app icons asynchronously on startup to prevent empty circles on first open
        try { WallFlow.Windows.PinnedAppsWindow.PreloadPinnedIcons(); } catch { }

        base.OnStartup(e);

        // Configure Dependency Injection Services
        var services = new ServiceCollection();
        services.AddSingleton<DockViewModel>();
        services.AddSingleton<WidgetManager>();
        services.AddSingleton<DockWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<Windows.SettingsWindow>();
        ServiceProvider = services.BuildServiceProvider();

        // Resolve core window logic
        var widgetManager = ServiceProvider.GetRequiredService<WidgetManager>();
        var dockViewModel = ServiceProvider.GetRequiredService<DockViewModel>();
        widgetManager.SetViewModel(dockViewModel);

        // Register Dynamic Widgets in the factory dictionary
        widgetManager.RegisterWidget("Calendar", (left) => new Windows.CalendarWindow(left));
        widgetManager.RegisterWidget("Weather", (left) => new Windows.WeatherWindow(left));
        widgetManager.RegisterWidget("Media", (left) => new Windows.MediaFlyoutWindow(DockWindow.Instance?.CurrentMediaSession, left));
        widgetManager.RegisterWidget("Settings", (left) => WallFlow.Windows.SettingsWindow.Instance ?? ServiceProvider.GetRequiredService<WallFlow.Windows.SettingsWindow>());
        widgetManager.RegisterWidget("OnlineGallery", (left) => new Windows.OnlineGalleryWindow());
        widgetManager.RegisterWidget("YouTube", (left) => WallFlow.Windows.YouTubeMusicWindow.Instance ?? new WallFlow.Windows.YouTubeMusicWindow());
        widgetManager.RegisterWidget("ChatGPT", (left) => WallFlow.Windows.AiAssistantWindow.Instance ?? new WallFlow.Windows.AiAssistantWindow());
        widgetManager.RegisterWidget("Gemini", (left) => WallFlow.Windows.AiAssistantWindow.Instance ?? new WallFlow.Windows.AiAssistantWindow());
        widgetManager.RegisterWidget("AiAssistant", (left) => WallFlow.Windows.AiAssistantWindow.Instance ?? new WallFlow.Windows.AiAssistantWindow());
        widgetManager.RegisterWidget("Power", (left) => new Windows.PowerWindow());
        widgetManager.RegisterWidget("PinnedApps", (left) => {
            var win = new Windows.PinnedAppsWindow();
            win.SetSearchMode("Apps");
            return win;
        });
        widgetManager.RegisterWidget("Clipboard", (left) => {
            var win = new Windows.PinnedAppsWindow();
            win.SetSearchMode("Clipboard");
            return win;
        });

        _mainWindow = new LocalGalleryWindow();
        // Force window handle (HWND) creation in background to hook hotkeys/IPC without flashing
        var mainHwnd = new System.Windows.Interop.WindowInteropHelper(_mainWindow).EnsureHandle();

        // Initialize System Coordinator with LocalGalleryWindow's HWND
        SystemCoordinator.Instance.Initialize(mainHwnd);

        _dockWindow = ServiceProvider.GetRequiredService<DockWindow>();
        _dockWindow.Show();

        // Initialize dynamic colors from wallpaper (Auto-Theming)
        WallpaperThemeService.Instance.UpdateThemeFromWallpaper();

        // If we have an initial URL to open, open it immediately
        if (!string.IsNullOrEmpty(InitialUrl))
        {
            try
            {
                using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = InitialUrl,
                    UseShellExecute = true
                });
                InitialUrl = null;
            }
            catch { }
        }

        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("WallFlow.logofondo.ico");

        var icon = stream != null ? new Icon(stream) : SystemIcons.Application;

        _trayIcon = new TrayIcon();
        _trayIcon.SetIcon(icon);
        _trayIcon.Text = "WallFlow";
        _trayIcon.LeftClick += () =>
        {
            _dockWindow?.ToggleDockVisibility();
        };
        _trayIcon.RightClick += ShowContextMenu;
        _trayIcon.Show();
    }

    private void ShowContextMenu()
    {
        var menu = new ContextMenuWindow();
        menu.ShowAtCursor();
    }

    public static void ApplyFont(string fontName)
    {
        try
        {
            var bundledFonts = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Outfit", "Orbitron", "Google Sans Flex"
            };

            if (bundledFonts.Contains(fontName))
            {
                var baseUri = new Uri("pack://application:,,,/");
                var relativePath = "./Fonts/#" + fontName;
                Current.Resources["AppFontFamily"] = new System.Windows.Media.FontFamily(baseUri, relativePath);
                return;
            }

            // Check custom fonts folder
            var customFontsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallFlow", "CustomFonts");

            bool isCustomFont = false;
            if (Directory.Exists(customFontsDir))
            {
                var files = Directory.GetFiles(customFontsDir);
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (ext == ".ttf" || ext == ".otf")
                    {
                        try
                        {
                            var glyphTypeface = new GlyphTypeface(new Uri(file));
                            string? familyName = null;
                            if (glyphTypeface.Win32FamilyNames.TryGetValue(System.Globalization.CultureInfo.GetCultureInfo("en-us"), out var name))
                            {
                                familyName = name;
                            }
                            else
                            {
                                foreach (var val in glyphTypeface.Win32FamilyNames.Values)
                                {
                                    familyName = val;
                                    break;
                                }
                            }

                            if (string.Equals(familyName, fontName, StringComparison.OrdinalIgnoreCase))
                            {
                                isCustomFont = true;
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }

            if (isCustomFont)
            {
                var baseUri = new Uri(customFontsDir + "\\");
                var relativePath = "./#" + fontName;
                Current.Resources["AppFontFamily"] = new System.Windows.Media.FontFamily(baseUri, relativePath);
                return;
            }

            // Fallback to system font
            Current.Resources["AppFontFamily"] = new System.Windows.Media.FontFamily(fontName);
        }
        catch
        {
            try
            {
                Current.Resources["AppFontFamily"] = new System.Windows.Media.FontFamily("Segoe UI");
            }
            catch { }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemCoordinator.Instance.Shutdown();
        _trayIcon?.Dispose();
        if (_appMutex != null)
        {
            try
            {
                _appMutex.ReleaseMutex();
                _appMutex.Dispose();
            }
            catch { }
        }
        base.OnExit(e);
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallFlow");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "crash.txt"), ex.ToString());
        }
        catch { }
    }
}
