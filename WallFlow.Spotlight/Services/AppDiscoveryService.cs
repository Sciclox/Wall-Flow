using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using WallFlow.Windows;

namespace WallFlow.Spotlight.Services;

public interface IAppDiscoveryService
{
    PinnedApp CreatePinnedAppFromPath(string path);
    Task<List<PinnedApp>> DiscoverInstalledAppsAsync(CancellationToken cancellationToken = default);
    string TranslateAppName(string name);
}

/// <summary>
/// Service that comprehensively discovers and enumerates installed desktop, Start Menu,
/// Registry App Paths, Uninstall entries, System Tools, Control Panel applets, Windows Settings, and UWP apps.
/// </summary>
public class AppDiscoveryService : IAppDiscoveryService
{
    private static AppDiscoveryService? _instance;
    public static AppDiscoveryService Instance => _instance ??= new AppDiscoveryService();

    private readonly List<PinnedApp> _cachedInstalledApps = new();
    private bool _hasLoadedOnce;
    private readonly object _cacheLock = new();

    public PinnedApp CreatePinnedAppFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new PinnedApp { Name = "Desconocido", Path = path ?? string.Empty, IconGlyph = "\uE30C" };

        if (path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
        {
            return new PinnedApp
            {
                Name = "Configuración de Windows",
                Path = path,
                Category = "Settings",
                IconGlyph = "\uE480"
            };
        }

        string targetPath = path;
        bool isUwp = path.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase);
        if (isUwp)
        {
            var parts = path.Substring(4).Split('|');
            targetPath = parts.Length > 1 ? parts[1] : "";
        }

        var resolved = IconExtractionService.ResolvePath(targetPath);
        var name = TranslateAppName(Path.GetFileNameWithoutExtension(resolved));

        // Fast load icon if already cached
        System.Windows.Media.ImageSource? icon = null;
        try
        {
            icon = IconExtractionService.Instance.LoadIconForApp(path);
        }
        catch { }

        string glyph = GetFallbackGlyphForApp(resolved, name);

        return new PinnedApp
        {
            Name = name,
            Path = path,
            Icon = icon,
            IconGlyph = glyph
        };
    }

    public static string GetFallbackGlyphForApp(string path, string name)
    {
        var lower = (name + " " + path).ToLowerInvariant();
        if (lower.Contains("explorer") || lower.Contains("archivos") || lower.Contains("folder"))
            return "\uE339"; // FolderOpen
        if (lower.Contains("chrome") || lower.Contains("edge") || lower.Contains("firefox") || lower.Contains("brave") || lower.Contains("browser"))
            return "\uE1E0"; // Globe / Web
        if (lower.Contains("notepad") || lower.Contains("bloc") || lower.Contains("text") || lower.Contains("word") || lower.Contains("edit"))
            return "\uE154"; // FileText
        if (lower.Contains("cmd") || lower.Contains("terminal") || lower.Contains("powershell") || lower.Contains("wt") || lower.Contains("bash"))
            return "\uE1E4"; // Terminal
        if (lower.Contains("taskmgr") || lower.Contains("tareas") || lower.Contains("activity") || lower.Contains("perf"))
            return "\uE008"; // Activity
        if (lower.Contains("code") || lower.Contains("devenv") || lower.Contains("visual studio"))
            return "\uE104"; // Code / Dev
        if (lower.Contains("calc") || lower.Contains("calculadora"))
            return "\uE09F"; // Calculator
        if (lower.Contains("paint") || lower.Contains("draw") || lower.Contains("photo") || lower.Contains("image"))
            return "\uE14C"; // Image
        if (lower.Contains("spotify") || lower.Contains("music") || lower.Contains("audio") || lower.Contains("música"))
            return "\uE122"; // Music

        return "\uE30C"; // AppWindow default
    }

    public string TranslateAppName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        if (string.Equals(name, "explorer", StringComparison.OrdinalIgnoreCase)) return "Explorador de Archivos";
        if (string.Equals(name, "chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (string.Equals(name, "notepad", StringComparison.OrdinalIgnoreCase)) return "Bloc de Notas";
        if (string.Equals(name, "cmd", StringComparison.OrdinalIgnoreCase)) return "Símbolo del Sistema";
        if (string.Equals(name, "taskmgr", StringComparison.OrdinalIgnoreCase)) return "Administrador de Tareas";
        if (string.Equals(name, "calc", StringComparison.OrdinalIgnoreCase)) return "Calculadora";
        if (string.Equals(name, "mspaint", StringComparison.OrdinalIgnoreCase)) return "Paint";
        if (string.Equals(name, "regedit", StringComparison.OrdinalIgnoreCase)) return "Editor del Registro";
        if (string.Equals(name, "code", StringComparison.OrdinalIgnoreCase)) return "Visual Studio Code";
        if (string.Equals(name, "devenv", StringComparison.OrdinalIgnoreCase)) return "Visual Studio";
        if (string.Equals(name, "wt", StringComparison.OrdinalIgnoreCase)) return "Windows Terminal";
        if (string.Equals(name, "powershell", StringComparison.OrdinalIgnoreCase)) return "Windows PowerShell";
        return name;
    }

    public async Task<List<PinnedApp>> DiscoverInstalledAppsAsync(CancellationToken cancellationToken = default)
    {
        lock (_cacheLock)
        {
            if (_hasLoadedOnce && _cachedInstalledApps.Count > 0)
            {
                return new List<PinnedApp>(_cachedInstalledApps);
            }
        }

        return await Task.Run(() =>
        {
            var apps = new List<PinnedApp>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Scan System Tools & Administrative Consoles
            ScanSystemTools(apps, seenPaths, seenNames);

            // 2. Scan Windows Settings (ms-settings:)
            ScanWindowsSettings(apps, seenPaths, seenNames);

            // 3. Scan Start Menu Shortcuts & Desktop Folders
            var userPrograms = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            var userDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");

            void ScanDirectory(string dir, bool includeExe = false, SearchOption searchOption = SearchOption.AllDirectories)
            {
                if (!Directory.Exists(dir)) return;
                try
                {
                    var files = Directory.GetFiles(dir, "*.*", searchOption)
                        .Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".url", StringComparison.OrdinalIgnoreCase) ||
                                    (includeExe && f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)));

                    foreach (var file in files)
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        try
                        {
                            var name = Path.GetFileNameWithoutExtension(file);
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            if (IsIgnoredAppName(name)) continue;

                            if (seenPaths.Add(file))
                            {
                                var app = new PinnedApp
                                {
                                    Name = name,
                                    Path = file,
                                    Category = "App"
                                };
                                app.Keywords.Add(name);
                                apps.Add(app);
                                seenNames.Add(name);
                            }
                        }
                        catch { /* ignored */ }
                    }
                }
                catch { /* ignored */ }
            }

            ScanDirectory(userPrograms);
            ScanDirectory(commonPrograms);
            ScanDirectory(userDesktop, includeExe: true);
            ScanDirectory(commonDesktop, includeExe: true);
            ScanDirectory(localPrograms, includeExe: true, searchOption: SearchOption.AllDirectories);
            ScanDirectory(userDownloads, includeExe: true, searchOption: SearchOption.TopDirectoryOnly);

            // 4. Scan Registry App Paths
            ScanRegistryAppPaths(apps, seenPaths, seenNames, cancellationToken);

            // 5. Scan Registry Uninstall Keys
            ScanRegistryUninstall(apps, seenPaths, seenNames, cancellationToken);

            // 6. Scan UWP / Windows Store Apps
            ScanUwpApps(apps, seenPaths, seenNames, cancellationToken);
            ScanStartApps(apps, seenPaths, seenNames, cancellationToken);

            apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

            lock (_cacheLock)
            {
                _cachedInstalledApps.Clear();
                _cachedInstalledApps.AddRange(apps);
                _hasLoadedOnce = true;
            }

            return apps;
        }, cancellationToken);
    }

    private void ScanSystemTools(List<PinnedApp> apps, HashSet<string> seenPaths, HashSet<string> seenNames)
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);

        var tools = new (string FileName, string DisplayName, string[] Keywords)[]
        {
            ("cmd.exe", "Símbolo del sistema", new[] { "cmd", "consola", "terminal", "command prompt", "prompt" }),
            ("powershell.exe", "Windows PowerShell", new[] { "powershell", "ps", "pwsh", "terminal", "consola" }),
            ("taskmgr.exe", "Administrador de tareas", new[] { "taskmgr", "procesos", "rendimiento", "cpu", "memoria", "task manager" }),
            ("regedit.exe", "Editor del Registro", new[] { "regedit", "registro", "registry", "reg" }),
            ("calc.exe", "Calculadora", new[] { "calc", "calculator", "matematicas", "sumar" }),
            ("notepad.exe", "Bloc de notas", new[] { "notepad", "texto", "notas", "txt", "editor" }),
            ("mspaint.exe", "Paint", new[] { "paint", "dibujo", "mspaint", "imagen" }),
            ("snippingtool.exe", "Herramienta Recortes", new[] { "recortes", "snipping", "captura", "screenshot", "snip" }),
            ("cleanmgr.exe", "Liberador de espacio en disco", new[] { "cleanmgr", "limpiar", "espacio", "disco", "temporales" }),
            ("resmon.exe", "Monitor de recursos", new[] { "resmon", "recursos", "red", "disco", "cpu" }),
            ("perfmon.exe", "Monitor de rendimiento", new[] { "perfmon", "rendimiento", "performance" }),
            ("dxdiag.exe", "Herramienta de diagnóstico de DirectX", new[] { "dxdiag", "directx", "graficos", "gpu", "video" }),
            ("msinfo32.exe", "Información del sistema", new[] { "msinfo", "sistema", "especificaciones", "hardware" }),
            ("charmap.exe", "Mapa de caracteres", new[] { "charmap", "caracteres", "simbolos", "fuentes", "emojis" }),
            ("osk.exe", "Teclado en pantalla", new[] { "osk", "teclado", "keyboard" }),
            ("magnify.exe", "Lupa", new[] { "lupa", "magnifier", "zoom", "accesibilidad" }),
            ("control.exe", "Panel de control", new[] { "control", "panel", "ajustes", "configuracion" }),

            // Administrative Consoles (.msc)
            ("services.msc", "Servicios de Windows", new[] { "servicios", "services", "demonios", "segundo plano" }),
            ("devmgmt.msc", "Administrador de dispositivos", new[] { "devmgmt", "dispositivos", "drivers", "controladores", "hardware" }),
            ("diskmgmt.msc", "Administración de discos", new[] { "diskmgmt", "discos", "particiones", "formatear", "volumenes" }),
            ("compmgmt.msc", "Administración de equipos", new[] { "compmgmt", "administracion", "computer management" }),
            ("eventvwr.msc", "Visor de eventos", new[] { "eventvwr", "eventos", "logs", "errores", "visor" }),
            ("taskschd.msc", "Programador de tareas", new[] { "taskschd", "tareas", "programador", "cron", "automatizar" }),
            ("gpedit.msc", "Editor de directivas de grupo", new[] { "gpedit", "directivas", "politicas", "group policy" }),

            // Control Panel Applets (.cpl)
            ("appwiz.cpl", "Programas y características (Desinstalar)", new[] { "desinstalar", "programas", "eliminar", "quitar programas", "appwiz" }),
            ("sysdm.cpl", "Propiedades del sistema (Variables de entorno)", new[] { "variables de entorno", "sistema", "path", "nombre de equipo", "sysdm" }),
            ("ncpa.cpl", "Conexiones de red", new[] { "adaptadores de red", "ethernet", "wifi", "ip", "dns", "ncpa", "tarjeta de red" }),
            ("main.cpl", "Propiedades del mouse", new[] { "mouse", "raton", "cursor", "puntero", "touchpad" }),
            ("mmsys.cpl", "Dispositivos de sonido y audio", new[] { "sonido", "audio", "altavoces", "microfono", "mmsys", "auriculares" }),
            ("firewall.cpl", "Firewall de Windows Defender", new[] { "firewall", "puertos", "seguridad", "red", "bloqueo" }),
            ("powercfg.cpl", "Opciones de energía", new[] { "energia", "bateria", "suspension", "plan de energia", "rendimiento" })
        };

        foreach (var tool in tools)
        {
            string fullPath = Path.Combine(system32, tool.FileName);
            if (File.Exists(fullPath) && seenPaths.Add(fullPath))
            {
                var app = new PinnedApp
                {
                    Name = tool.DisplayName,
                    Path = fullPath,
                    Category = "System"
                };
                app.Keywords.AddRange(tool.Keywords);
                apps.Add(app);
                seenNames.Add(tool.DisplayName);
            }
        }
    }

    private void ScanWindowsSettings(List<PinnedApp> apps, HashSet<string> seenPaths, HashSet<string> seenNames)
    {
        var settingsList = new (string Uri, string Name, string[] Keywords)[]
        {
            ("ms-settings:bluetooth", "Bluetooth y otros dispositivos", new[] { "bluetooth", "raton", "teclado", "mouse", "auriculares", "audio", "bt" }),
            ("ms-settings:display", "Configuración de pantalla", new[] { "pantalla", "monitor", "resolucion", "brillo", "escala", "display", "hdr" }),
            ("ms-settings:sound", "Configuración de sonido", new[] { "sonido", "volumen", "audio", "microfono", "altavoces", "sound", "salida" }),
            ("ms-settings:network", "Red e Internet (Wi-Fi y Ethernet)", new[] { "wifi", "red", "internet", "ethernet", "vpn", "proxy", "ip", "wi-fi" }),
            ("ms-settings:windowsupdate", "Windows Update", new[] { "actualizar", "actualizaciones", "update", "parches", "reiniciar", "buscar actualizaciones" }),
            ("ms-settings:appsfeatures", "Aplicaciones instaladas", new[] { "programas", "desinstalar", "apps", "aplicaciones", "instalar" }),
            ("ms-settings:powersleep", "Energía y batería", new[] { "bateria", "energia", "suspension", "sleep", "power", "consumo", "ahorro" }),
            ("ms-settings:storagesense", "Almacenamiento del sistema", new[] { "almacenamiento", "disco", "espacio", "limpiar", "sensor", "temporales" }),
            ("ms-settings:personalization", "Personalización y temas", new[] { "fondo", "tema", "colores", "wallpaper", "pantalla de bloqueo", "personalizacion" }),
            ("ms-settings:colors", "Colores y modo oscuro", new[] { "modo oscuro", "tema oscuro", "color de enfasis", "claro", "oscuro", "colores" }),
            ("ms-settings:dateandtime", "Fecha y hora", new[] { "hora", "fecha", "zona horaria", "reloj", "sincronizar hora", "tiempo" }),
            ("ms-settings:regionlanguage", "Idioma y región", new[] { "idioma", "region", "teclado", "language", "español", "ingles" }),
            ("ms-settings:privacy", "Privacidad y seguridad", new[] { "privacidad", "camara", "microfono", "ubicacion", "seguridad", "permisos" }),
            ("ms-settings:taskbar", "Configuración de la barra de tareas", new[] { "barra de tareas", "taskbar", "iconos", "ocultar barra" }),
            ("ms-settings:defaultapps", "Aplicaciones predeterminadas", new[] { "predeterminado", "navegador por defecto", "abrir con", "asociaciones" }),
            ("ms-settings:printers", "Impresoras y escáneres", new[] { "impresora", "escaner", "print", "scanner" }),
            ("ms-settings:gaming-gamebar", "Xbox Game Bar", new[] { "juegos", "game bar", "xbox", "captura de pantalla", "fps" }),
            ("ms-settings:notifications", "Notificaciones y acciones", new[] { "notificaciones", "avisos", "no molestar", "asistente de concentracion" }),
            ("ms-settings:about", "Acerca de tu PC (Especificaciones)", new[] { "acerca de", "especificaciones", "procesador", "ram", "windows version", "sistema" })
        };

        foreach (var s in settingsList)
        {
            if (seenPaths.Add(s.Uri))
            {
                var app = new PinnedApp
                {
                    Name = s.Name,
                    Path = s.Uri,
                    Category = "Settings",
                    IconGlyph = "\uE480"
                };
                app.Keywords.AddRange(s.Keywords);
                apps.Add(app);
                seenNames.Add(s.Name);
            }
        }
    }

    private void ScanRegistryAppPaths(List<PinnedApp> apps, HashSet<string> seenPaths, HashSet<string> seenNames, CancellationToken ct)
    {
        try
        {
            var roots = new[]
            {
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
                Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths")
            };

            foreach (var root in roots)
            {
                if (root == null) continue;
                using (root)
                {
                    foreach (var subKeyName in root.GetSubKeyNames())
                    {
                        if (ct.IsCancellationRequested) return;
                        try
                        {
                            using var subKey = root.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            string? rawPath = subKey.GetValue("") as string;
                            if (string.IsNullOrWhiteSpace(rawPath))
                            {
                                rawPath = subKey.GetValue("Path") as string;
                            }

                            if (string.IsNullOrWhiteSpace(rawPath)) continue;

                            string cleanPath = rawPath.Trim('\"', ' ');
                            if (!File.Exists(cleanPath)) continue;

                            string exeName = Path.GetFileNameWithoutExtension(cleanPath);
                            if (IsIgnoredAppName(exeName)) continue;

                            if (!seenPaths.Add(cleanPath)) continue;

                            string friendlyName = TranslateAppName(exeName);
                            try
                            {
                                var vi = FileVersionInfo.GetVersionInfo(cleanPath);
                                if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                                {
                                    friendlyName = vi.FileDescription.Trim();
                                }
                            }
                            catch { /* ignored */ }

                            var app = new PinnedApp
                            {
                                Name = friendlyName,
                                Path = cleanPath,
                                Category = "App"
                            };

                            app.Keywords.Add(exeName);
                            string rawBase = Path.GetFileNameWithoutExtension(subKeyName);
                            if (!rawBase.Equals(exeName, StringComparison.OrdinalIgnoreCase))
                            {
                                app.Keywords.Add(rawBase);
                            }

                            apps.Add(app);
                            seenNames.Add(friendlyName);
                        }
                        catch { /* ignored */ }
                    }
                }
            }
        }
        catch { /* ignored */ }
    }

    private void ScanRegistryUninstall(List<PinnedApp> apps, HashSet<string> seenPaths, HashSet<string> seenNames, CancellationToken ct)
    {
        try
        {
            var roots = new[]
            {
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            foreach (var root in roots)
            {
                if (root == null) continue;
                using (root)
                {
                    foreach (var subKeyName in root.GetSubKeyNames())
                    {
                        if (ct.IsCancellationRequested) return;
                        try
                        {
                            using var subKey = root.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            int isSystemComponent = Convert.ToInt32(subKey.GetValue("SystemComponent") ?? 0);
                            if (isSystemComponent == 1) continue;
                            if (subKey.GetValue("ParentKeyName") != null) continue;

                            string? displayName = subKey.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName) || IsIgnoredAppName(displayName)) continue;

                            if (seenNames.Contains(displayName.Trim())) continue;

                            string? displayIcon = subKey.GetValue("DisplayIcon") as string;
                            string? installLocation = subKey.GetValue("InstallLocation") as string;

                            string? targetExe = null;
                            if (!string.IsNullOrWhiteSpace(displayIcon))
                            {
                                string candidate = displayIcon.Split(',')[0].Trim('\"', ' ');
                                if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                                {
                                    targetExe = candidate;
                                }
                            }

                            if (targetExe == null && !string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
                            {
                                var exes = Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly);
                                foreach (var exe in exes)
                                {
                                    string exeName = Path.GetFileNameWithoutExtension(exe);
                                    if (!IsIgnoredAppName(exeName))
                                    {
                                        targetExe = exe;
                                        break;
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(targetExe) && seenPaths.Add(targetExe))
                            {
                                var app = new PinnedApp
                                {
                                    Name = displayName.Trim(),
                                    Path = targetExe,
                                    Category = "App"
                                };
                                app.Keywords.Add(Path.GetFileNameWithoutExtension(targetExe));
                                apps.Add(app);
                                seenNames.Add(displayName.Trim());
                            }
                        }
                        catch { /* ignored */ }
                    }
                }
            }
        }
        catch { /* ignored */ }
    }

    private void ScanUwpApps(List<PinnedApp> apps, HashSet<string> seenPaths, HashSet<string> seenNames, CancellationToken ct)
    {
        try
        {
            var shellType = Type.GetTypeFromCLSID(new Guid("13709620-C279-11CE-A49E-00AA00C1485B"));
            if (shellType == null) return;

            object? shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            object? appsFolder = shellType.InvokeMember("NameSpace",
                BindingFlags.InvokeMethod,
                null, shell, new object[] { "shell:AppsFolder" });

            if (appsFolder == null) return;

            Type folderType = appsFolder.GetType();
            object? items = folderType.InvokeMember("Items",
                BindingFlags.InvokeMethod,
                null, appsFolder, null);

            if (items is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    if (ct.IsCancellationRequested) return;
                    try
                    {
                        Type itemType = item.GetType();
                        string? name = itemType.InvokeMember("Name",
                            BindingFlags.GetProperty,
                            null, item, null) as string;

                        string? path = itemType.InvokeMember("Path",
                            BindingFlags.GetProperty,
                            null, item, null) as string;

                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                        if (IsIgnoredAppName(name)) continue;

                        string uwpPath = path.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase)
                            ? path
                            : "uwp:" + path;

                        if (seenPaths.Add(uwpPath))
                        {
                            var app = new PinnedApp
                            {
                                Name = name,
                                Path = uwpPath,
                                Category = "App"
                            };
                            app.Keywords.Add(name);
                            apps.Add(app);
                            seenNames.Add(name);
                        }
                    }
                    catch { /* ignored */ }
                }
            }
        }
        catch { /* ignored */ }
    }

    private void ScanStartApps(List<PinnedApp> apps, HashSet<string> seenPaths, HashSet<string> seenNames, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-StartApps | ConvertTo-Json -Compress\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string json = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in root.EnumerateArray())
                        {
                            if (ct.IsCancellationRequested) return;
                            if (!elem.TryGetProperty("Name", out var nameProp) || !elem.TryGetProperty("AppID", out var appIdProp))
                                continue;

                            string name = nameProp.GetString() ?? "";
                            string appId = appIdProp.GetString() ?? "";

                            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appId))
                                continue;

                            if (IsIgnoredAppName(name)) continue;

                            string itemPath = appId;
                            if (!appId.Contains('\\') || appId.Contains('!') || appId.StartsWith("Microsoft."))
                            {
                                itemPath = "uwp:" + appId;
                            }

                            if (seenPaths.Add(itemPath))
                            {
                                var app = new PinnedApp
                                {
                                    Name = name,
                                    Path = itemPath,
                                    Category = "App"
                                };
                                app.Keywords.Add(name);
                                apps.Add(app);
                                seenNames.Add(name);
                            }
                        }
                    }
                }
            }
        }
        catch { /* ignored */ }
    }

    private static bool IsIgnoredAppName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        string n = name.Trim();

        return n.Equals("Uninstall", StringComparison.OrdinalIgnoreCase) ||
               n.StartsWith("Uninstall ", StringComparison.OrdinalIgnoreCase) ||
               n.EndsWith(" Uninstaller", StringComparison.OrdinalIgnoreCase) ||
               n.StartsWith("Unins0", StringComparison.OrdinalIgnoreCase) ||
               n.Equals("ReadMe", StringComparison.OrdinalIgnoreCase) ||
               n.Equals("Help", StringComparison.OrdinalIgnoreCase) ||
               n.Equals("Documentation", StringComparison.OrdinalIgnoreCase) ||
               n.EndsWith(" Setup", StringComparison.OrdinalIgnoreCase) ||
               n.EndsWith(" Installer", StringComparison.OrdinalIgnoreCase);
    }
}
