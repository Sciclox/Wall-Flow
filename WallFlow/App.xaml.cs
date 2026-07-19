using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace WallFlow;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;
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

        base.OnStartup(e);

        _mainWindow = new MainWindow();
        // Force window handle (HWND) creation in background to hook hotkeys/IPC without flashing
        new System.Windows.Interop.WindowInteropHelper(_mainWindow).EnsureHandle();

        _dockWindow = new DockWindow();
        _dockWindow.Show();

        // If we have an initial URL to open, open it immediately
        if (!string.IsNullOrEmpty(InitialUrl))
        {
            try
            {
                _dockWindow.OpenNewBrowserWindow(InitialUrl);
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
                "Outfit", "Share Tech Mono", "Orbitron", "Lobster Two", "Dancing Script",
                "Pacifico", "Cinzel", "Montserrat", "Playfair Display", "Comfortaa",
                "Caveat", "Roboto Mono", "Michroma", "Syncopate", "Inter", "Quicksand", "Varela Round"
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
