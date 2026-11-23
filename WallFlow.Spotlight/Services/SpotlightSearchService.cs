using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using WallFlow.Search;
using WallFlow.Services;
using WallFlow.Windows;

namespace WallFlow.Spotlight.Services;

public record QueryRouteResult(string TargetMode, string FilteredQuery);

public interface ISpotlightSearchService
{
    QueryRouteResult RouteQuery(string query, string currentMode);
    List<PinnedApp> SearchApps(string query, IEnumerable<PinnedApp> allApps, Dictionary<string, int>? launchCounts, int maxResults = 25);
    Task<List<PinnedApp>> SearchFilesAsync(string query, int maxResults = 25, CancellationToken cancellationToken = default);
    List<PinnedApp> SearchClipboard(string query, IEnumerable<ClipboardItem> history);
    PinnedApp? TryEvaluateMath(string query);
    List<PinnedApp> GetSystemActions(string query);
    Task<List<PinnedApp>> SearchMusicAsync(string query, CancellationToken cancellationToken = default, int maxResults = 20);
    Task<List<PinnedApp>> SearchMusicAsync(string query, Action<List<PinnedApp>>? onSuggestionsReady, CancellationToken cancellationToken = default, int maxResults = 20);
}

/// <summary>
/// Service responsible for routing and executing search queries across Apps, Files, Clipboard, Math, and System commands.
/// </summary>
public class SpotlightSearchService : ISpotlightSearchService
{
    private static SpotlightSearchService? _instance;
    public static SpotlightSearchService Instance => _instance ??= new SpotlightSearchService();

    private readonly DataTable _mathTable = new();
    private static readonly HttpClientHandler _httpHandler = new()
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli
    };
    private static readonly HttpClient _httpClient = new(_httpHandler)
    {
        Timeout = TimeSpan.FromSeconds(8.0)
    };

    public QueryRouteResult RouteQuery(string query, string currentMode)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new QueryRouteResult(currentMode, string.Empty);

        string q = query.Trim();

        // Files prefix: f: or file: or /f 
        if (q.StartsWith("f:", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("/f ", StringComparison.OrdinalIgnoreCase))
        {
            int prefixLen = q.IndexOfAny(new[] { ':', ' ' }) + 1;
            return new QueryRouteResult("Files", q.Substring(prefixLen).Trim());
        }

        // Apps prefix: a: or app: or /a 
        if (q.StartsWith("a:", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("app:", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("/a ", StringComparison.OrdinalIgnoreCase))
        {
            int prefixLen = q.IndexOfAny(new[] { ':', ' ' }) + 1;
            return new QueryRouteResult("Apps", q.Substring(prefixLen).Trim());
        }

        // AI prefix: ? or @ai or ai: or /ai
        if (q.StartsWith("?") ||
            q.StartsWith("@ai ", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("ai:", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("/ai ", StringComparison.OrdinalIgnoreCase))
        {
            string aiPrompt = q.StartsWith("?")
                ? q.Substring(1).Trim()
                : q.Substring(q.IndexOfAny(new[] { ':', ' ' }) + 1).Trim();
            return new QueryRouteResult("AI", aiPrompt);
        }

        // Clipboard prefix: c: or clip: or /c
        if (q.StartsWith("c:", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("clip:", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("/c ", StringComparison.OrdinalIgnoreCase))
        {
            int prefixLen = q.IndexOfAny(new[] { ':', ' ' }) + 1;
            return new QueryRouteResult("Clipboard", q.Substring(prefixLen).Trim());
        }

        // Music prefix: m: or music: or musica: or play: or /m 
        if (q.StartsWith("m:", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("music:", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("musica:", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("play:", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("/m ", StringComparison.OrdinalIgnoreCase))
        {
            int prefixLen = q.IndexOfAny(new[] { ':', ' ' }) + 1;
            return new QueryRouteResult("Music", q.Substring(prefixLen).Trim());
        }

        if (q.StartsWith("play ", StringComparison.OrdinalIgnoreCase))
        {
            return new QueryRouteResult("Music", q.Substring(5).Trim());
        }

        // Direct playback control single-word commands
        if (q.Equals("play", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("pause", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("reproducir", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("pausar", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("next", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("siguiente", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("prev", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("previous", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("anterior", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("skip", StringComparison.OrdinalIgnoreCase))
        {
            return new QueryRouteResult("Music", q);
        }

        return new QueryRouteResult(currentMode, q);
    }

    public List<PinnedApp> SearchApps(string query, IEnumerable<PinnedApp> allApps, Dictionary<string, int>? launchCounts, int maxResults = 25)
    {
        var results = new List<PinnedApp>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        // 1. Check for quick calculator match
        var mathResult = TryEvaluateMath(query);
        if (mathResult != null)
        {
            results.Add(mathResult);
        }

        // 2. Check for system commands (shutdown, restart, lock, recycle bin)
        var sysActions = GetSystemActions(query);
        results.AddRange(sysActions);

        // 3. Search apps using FuzzyMatcher with keyword/alias support
        var scored = new List<(PinnedApp app, int score)>();

        foreach (var app in allApps)
        {
            int score = FuzzyMatcher.CalculateScore(app.Name, query, app.Path, app.Keywords);
            if (score <= 0) continue;

            if (launchCounts != null && launchCounts.TryGetValue(app.Path, out int count))
                score += Math.Min(count * 2, 25);

            if (app.IsPinned)
                score += 15;

            scored.Add((app, score));
        }

        scored.Sort((a, b) =>
        {
            int cmp = b.score.CompareTo(a.score);
            return cmp != 0 ? cmp : string.Compare(a.app.Name, b.app.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in results)
        {
            seenNames.Add(r.Name);
        }

        foreach (var (app, _) in scored)
        {
            if (seenNames.Add(app.Name))
            {
                results.Add(app);
                if (results.Count >= maxResults) break;
            }
        }

        // 4. Command/Path execution fallback
        if (results.Count == 0 || IsCommandOrPath(query))
        {
            var cmdItem = TryCreateCommandAction(query);
            if (cmdItem != null && seenNames.Add(cmdItem.Name))
            {
                results.Add(cmdItem);
            }
        }

        return results;
    }

    private static bool IsCommandOrPath(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        string q = query.Trim();
        return q.Contains('/') || q.Contains('\\') || q.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
               q.StartsWith("cmd ", StringComparison.OrdinalIgnoreCase) || q.StartsWith("ping ", StringComparison.OrdinalIgnoreCase) ||
               q.StartsWith("ipconfig", StringComparison.OrdinalIgnoreCase) || q.StartsWith("http", StringComparison.OrdinalIgnoreCase);
    }

    private static PinnedApp? TryCreateCommandAction(string query)
    {
        string q = query.Trim();
        if (q.Length < 2) return null;

        return new PinnedApp
        {
            Name = $"Ejecutar: {q}",
            Subtitle = "Ejecutar comando del sistema o abrir ruta",
            Path = "cmd:" + q,
            ActionText = "Ejecutar",
            IconGlyph = "\uE1B8"
        };
    }

    public PinnedApp? TryEvaluateMath(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        string expr = query.Trim();
        // Simple heuristic: expression must contain at least one math operator and numbers
        if (!expr.Any(c => c is '+' or '-' or '*' or '/' or '%' or '^') || !expr.Any(char.IsDigit))
            return null;

        // Discard if contains forbidden chars
        if (expr.Any(c => !char.IsDigit(c) && c is not ('+' or '-' or '*' or '/' or '%' or '^' or '(' or ')' or '.' or ' ')))
            return null;

        try
        {
            string computeExpr = expr.Replace("^", ""); // DataTable compute doesn't do ^ directly
            object result = _mathTable.Compute(computeExpr, null);
            if (result != null && result != DBNull.Value && double.TryParse(result.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
            {
                string formatted = val.ToString("G15", CultureInfo.InvariantCulture);
                return new PinnedApp
                {
                    Name = formatted,
                    Subtitle = $"Cálculo: {expr} = {formatted}",
                    Path = "clipboard:" + formatted,
                    ActionText = "Copiar resultado",
                    IconGlyph = "\uE0B9" // Lucide calculator
                };
            }
        }
        catch { /* Not a valid mathematical expression */ }

        return null;
    }

    public List<PinnedApp> GetSystemActions(string query)
    {
        var actions = new List<PinnedApp>();
        if (string.IsNullOrWhiteSpace(query)) return actions;
        string q = query.Trim().ToLowerInvariant();

        if ("apagar".StartsWith(q) || "shutdown".StartsWith(q))
        {
            actions.Add(new PinnedApp
            {
                Name = "Apagar el equipo",
                Subtitle = "Cierra todas las aplicaciones y apaga la computadora",
                Path = "system:shutdown",
                ActionText = "Apagar",
                IconGlyph = "\uE124" // Lucide power
            });
        }

        if ("reiniciar".StartsWith(q) || "restart".StartsWith(q) || "reboot".StartsWith(q))
        {
            actions.Add(new PinnedApp
            {
                Name = "Reiniciar el equipo",
                Subtitle = "Cierra las aplicaciones y reinicia Windows",
                Path = "system:restart",
                ActionText = "Reiniciar",
                IconGlyph = "\uE400" // Lucide rotate-cw
            });
        }

        if ("bloquear".StartsWith(q) || "lock".StartsWith(q))
        {
            actions.Add(new PinnedApp
            {
                Name = "Bloquear equipo",
                Subtitle = "Bloquea la sesión actual de Windows",
                Path = "system:lock",
                ActionText = "Bloquear",
                IconGlyph = "\uE18A" // Lucide lock
            });
        }

        if ("suspender".StartsWith(q) || "sleep".StartsWith(q) || "hibernar".StartsWith(q))
        {
            actions.Add(new PinnedApp
            {
                Name = "Suspender equipo",
                Subtitle = "Pone la computadora en modo de suspensión",
                Path = "system:sleep",
                ActionText = "Suspender",
                IconGlyph = "\uE11F" // Lucide moon
            });
        }

        if ("papelera".StartsWith(q) || "vaciar papelera".StartsWith(q) || "recycle bin".StartsWith(q))
        {
            actions.Add(new PinnedApp
            {
                Name = "Vaciar Papelera de reciclaje",
                Subtitle = "Elimina permanentemente los archivos en la papelera",
                Path = "system:recyclebin",
                ActionText = "Vaciar",
                IconGlyph = "\uE42E" // Lucide trash
            });
        }

        return actions;
    }

    public async Task<List<PinnedApp>> SearchFilesAsync(string query, int maxResults = 25, CancellationToken cancellationToken = default)
    {
        var matchedFiles = new List<PinnedApp>();
        if (string.IsNullOrWhiteSpace(query)) return matchedFiles;

        var indexedResults = await WindowsSearchIndexer.SearchFilesAsync(query, maxResults, cancellationToken);
        foreach (var item in indexedResults)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var appItem = new PinnedApp
            {
                Name = item.Name,
                Subtitle = item.FormattedSubtitle,
                Path = item.Path,
                RunAsAdmin = false
            };

            if (item.IsDirectory)
            {
                appItem.IconGlyph = "\uE247"; // Lucide folder-open glyph
                appItem.ActionText = "Carpeta";
            }
            else
            {
                MapFileExtension(item.Extension, appItem);
            }

            matchedFiles.Add(appItem);
        }

        return matchedFiles;
    }

    public List<PinnedApp> SearchClipboard(string query, IEnumerable<ClipboardItem> history)
    {
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
                    _ => (item.Tags != null && item.Tags.Contains("#IA"))
                        ? "Respuesta IA"
                        : ((item.Colors != null && item.Colors.Count > 0 && item.Content.StartsWith("#")) ? "Color" : "Texto")
                };

                string timeLabel = GetFormattedCopyTime(item.Timestamp);

                var appItem = new PinnedApp
                {
                    Name = displayName.Length > 80
                        ? displayName.Substring(0, 77).Replace("\r", " ").Replace("\n", " ") + "..."
                        : displayName.Replace("\r", " ").Replace("\n", " "),
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
                else if (item.Type == "Image" && !string.IsNullOrEmpty(item.ImagePath))
                {
                    appItem.IconGlyph = "\uE5C4"; // Lucide images glyph de inmediato (0ms lag)
                    string imgPath = item.ImagePath;
                    Task.Run(() =>
                    {
                        try
                        {
                            if (File.Exists(imgPath))
                            {
                                var bmi = new BitmapImage();
                                bmi.BeginInit();
                                bmi.DecodePixelWidth = 56;
                                bmi.CacheOption = BitmapCacheOption.OnLoad;
                                bmi.UriSource = new Uri(imgPath, UriKind.Absolute);
                                bmi.EndInit();
                                bmi.Freeze();

                                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    appItem.Icon = bmi;
                                    appItem.IconGlyph = string.Empty;
                                }), System.Windows.Threading.DispatcherPriority.Background);
                            }
                        }
                        catch { }
                    });
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
                        MapFileExtension(Path.GetExtension(pathStr).ToLowerInvariant(), appItem);
                    }
                }
                else
                {
                    appItem.IconGlyph = "\uE329"; // Lucide file-type
                }

                matchedItems.Add(appItem);
            }
        }

        return matchedItems;
    }

    private static void MapFileExtension(string ext, PinnedApp item)
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
                item.IconGlyph = "\uE329";
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

            default:
                item.IconGlyph = "\uE329";
                item.ActionText = "Archivo";
                break;
        }
    }

    public static string GetFormattedCopyTime(DateTime timestamp)
    {
        var now = DateTime.Now;
        var diff = now - timestamp;
        string timeStr = timestamp.ToString("h:mm tt", CultureInfo.InvariantCulture);

        if (diff.TotalSeconds < 60 && diff.TotalSeconds >= 0)
        {
            return "Se copió justo ahora";
        }
        if (timestamp.Date == now.Date)
        {
            return $"Se copió a las {timeStr}";
        }
        if (timestamp.Date == now.Date.AddDays(-1))
        {
            return $"Se copió ayer a las {timeStr}";
        }
        return $"Se copió el {timestamp:dd/MM} a las {timeStr}";
    }

    public Task<List<PinnedApp>> SearchMusicAsync(string query, CancellationToken cancellationToken = default, int maxResults = 20)
    {
        return SearchMusicAsync(query, onSuggestionsReady: null, cancellationToken, maxResults);
    }

    public async Task<List<PinnedApp>> SearchMusicAsync(string query, Action<List<PinnedApp>>? onSuggestionsReady, CancellationToken cancellationToken = default, int maxResults = 20)
    {
        var results = new List<PinnedApp>();
        string q = (query ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(q))
        {
            results.Add(new PinnedApp
            {
                Name = "Alternar Reproducir / Pausar",
                Subtitle = "YouTube Music",
                Path = "ytmusic:toggle",
                ActionText = "Control",
                IconGlyph = "\uE197",
                ToolTip = "Alternar estado de reproducción"
            });
            results.Add(new PinnedApp
            {
                Name = "Siguiente Canción",
                Subtitle = "YouTube Music",
                Path = "ytmusic:next",
                ActionText = "Control",
                IconGlyph = "\uE19B",
                ToolTip = "Avanzar a la siguiente pista"
            });
            results.Add(new PinnedApp
            {
                Name = "Canción Anterior",
                Subtitle = "YouTube Music",
                Path = "ytmusic:prev",
                ActionText = "Control",
                IconGlyph = "\uE19A",
                ToolTip = "Volver a la pista previa"
            });
            results.Add(new PinnedApp
            {
                Name = "Abrir YouTube Music",
                Subtitle = "Reproductor flotante",
                Path = "ytmusic:open",
                ActionText = "Ventana",
                IconGlyph = "\uE217",
                ToolTip = "Abrir o enfocar el reproductor"
            });
            return results;
        }

        // Check if query is a single-word playback control
        if (q.Equals("play", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("pause", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("reproducir", StringComparison.OrdinalIgnoreCase) ||
            q.Equals("pausar", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new PinnedApp
            {
                Name = "Alternar Reproducir / Pausar",
                Subtitle = "YouTube Music",
                Path = "ytmusic:toggle",
                ActionText = "Control",
                IconGlyph = "\uE197",
                ToolTip = "Pausar o reanudar reproducción en YouTube Music"
            });
        }
        else if (q.Equals("next", StringComparison.OrdinalIgnoreCase) ||
                 q.Equals("siguiente", StringComparison.OrdinalIgnoreCase) ||
                 q.Equals("skip", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new PinnedApp
            {
                Name = "Siguiente Canción",
                Subtitle = "YouTube Music",
                Path = "ytmusic:next",
                ActionText = "Control",
                IconGlyph = "\uE19B",
                ToolTip = "Pasar a la siguiente canción"
            });
        }
        else if (q.Equals("prev", StringComparison.OrdinalIgnoreCase) ||
                 q.Equals("previous", StringComparison.OrdinalIgnoreCase) ||
                 q.Equals("anterior", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new PinnedApp
            {
                Name = "Canción Anterior",
                Subtitle = "YouTube Music",
                Path = "ytmusic:prev",
                ActionText = "Control",
                IconGlyph = "\uE19A",
                ToolTip = "Volver a la canción previa"
            });
        }

        // Direct action button: Play {q} immediately
        var directPlayItem = new PinnedApp
        {
            Name = $"Reproducir \"{q}\"",
            Subtitle = "YouTube Music",
            Path = $"ytmusic:play:{q}",
            ActionText = "Reproducir",
            IconGlyph = "\uE197",
            ToolTip = $"Buscar y reproducir {q} en YouTube Music"
        };
        results.Add(directPlayItem);

        var webFallbackItem = new PinnedApp
        {
            Name = $"Buscar \"{q}\" en YouTube Web",
            Subtitle = "youtube.com",
            Path = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(q)}",
            ActionText = "Web",
            IconGlyph = "\uE0E8",
            ToolTip = "Abrir resultados en el navegador predeterminado"
        };

        // 1. Fetch suggestions in parallel (instant response, < 50ms)
        var suggestionsTask = FetchSuggestionsAsync(q, cancellationToken);

        // 2. Fetch real YouTube video tracks
        var youtubeVideosTask = FetchYouTubeVideosAsync(q, cancellationToken, maxResults);

        // Phase 1 (Instant response): Notify caller via callback as soon as fast suggestions arrive
        if (onSuggestionsReady != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var suggestions = await suggestionsTask;
                    if (suggestions.Count > 0 && !cancellationToken.IsCancellationRequested)
                    {
                        var fastList = new List<PinnedApp> { directPlayItem };
                        fastList.AddRange(suggestions);
                        fastList.Add(webFallbackItem);
                        onSuggestionsReady(fastList);
                    }
                }
                catch { }
            }, cancellationToken);
        }

        // Phase 2: Await real YouTube videos (generous 5000ms timeout for network headroom)
        try
        {
            var firstCompleted = await Task.WhenAny(youtubeVideosTask, Task.Delay(5000, cancellationToken));
            if (firstCompleted == youtubeVideosTask && youtubeVideosTask.IsCompletedSuccessfully && youtubeVideosTask.Result.Count > 0)
            {
                // Attach top video's thumbnail to directPlayItem so Inspector displays artwork immediately
                if (string.IsNullOrEmpty(directPlayItem.ThumbnailUrl))
                {
                    directPlayItem.ThumbnailUrl = youtubeVideosTask.Result[0].ThumbnailUrl;
                }
                if (!string.IsNullOrEmpty(directPlayItem.ThumbnailUrl))
                {
                    string thumb = directPlayItem.ThumbnailUrl;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var bmp = await LoadBitmapFromUrlAsync(thumb, decodeWidth: 96, CancellationToken.None);
                            if (bmp != null)
                            {
                                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() => directPlayItem.Icon = bmp);
                            }
                        }
                        catch { }
                    });
                }
                results.AddRange(youtubeVideosTask.Result);
            }
            else
            {
                // Fallback: If YouTube timed out (> 5s) or had no video results, populate with suggestions
                var suggestions = await suggestionsTask;
                if (suggestions.Count > 0)
                {
                    results.AddRange(suggestions);
                }
            }
        }
        catch { }

        // Web Browser fallback
        results.Add(webFallbackItem);

        return results;
    }

    private static async Task<List<PinnedApp>> FetchSuggestionsAsync(string q, CancellationToken cancellationToken)
    {
        var list = new List<PinnedApp>();
        try
        {
            string url = $"https://suggestqueries.google.com/complete/search?client=firefox&ds=yt&q={Uri.EscapeDataString(q)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                var content = await resp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.GetArrayLength() > 1)
                {
                    var suggestions = doc.RootElement[1];
                    int count = 0;
                    foreach (var item in suggestions.EnumerateArray())
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrWhiteSpace(s) && !s.Equals(q, StringComparison.OrdinalIgnoreCase))
                        {
                            list.Add(new PinnedApp
                            {
                                Name = s,
                                Subtitle = "YouTube Music Sugerencia",
                                Path = $"ytmusic:play:{s}",
                                ActionText = "Música",
                                IconGlyph = "\uE217",
                                ToolTip = $"Reproducir {s} en YouTube Music"
                            });
                            count++;
                            if (count >= 6) break;
                        }
                    }
                }
            }
        }
        catch { }
        return list;
    }

    private static async Task<List<PinnedApp>> FetchYouTubeVideosAsync(string query, CancellationToken cancellationToken, int maxResults = 20)
    {
        var list = new List<PinnedApp>();
        try
        {
            string url = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            req.Headers.TryAddWithoutValidation("Accept-Language", "es-ES,es;q=0.9,en;q=0.8");

            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!resp.IsSuccessStatusCode) return list;

            var html = await resp.Content.ReadAsStringAsync(cancellationToken);
            string startKey = "ytInitialData = ";
            int idx = html.IndexOf(startKey, StringComparison.Ordinal);
            if (idx < 0) return list;

            int jsonStart = idx + startKey.Length;
            int jsonEnd = html.IndexOf(";</script>", jsonStart, StringComparison.Ordinal);
            if (jsonEnd < 0) return list;

            string jsonStr = html.Substring(jsonStart, jsonEnd - jsonStart);
            using var doc = JsonDocument.Parse(jsonStr);

            if (!doc.RootElement.TryGetProperty("contents", out var contentsProp)) return list;
            if (!contentsProp.TryGetProperty("twoColumnSearchResultsRenderer", out var twoCol)) return list;
            if (!twoCol.TryGetProperty("primaryContents", out var primary)) return list;
            if (!primary.TryGetProperty("sectionListRenderer", out var sectionList)) return list;
            if (!sectionList.TryGetProperty("contents", out var sections)) return list;

            foreach (var section in sections.EnumerateArray())
            {
                if (section.TryGetProperty("itemSectionRenderer", out var isr) &&
                    isr.TryGetProperty("contents", out var itemContents))
                {
                    foreach (var item in itemContents.EnumerateArray())
                    {
                        if (item.TryGetProperty("videoRenderer", out var vr))
                        {
                            var app = ParseVideoRenderer(vr);
                            if (app != null)
                            {
                                list.Add(app);
                                if (list.Count >= maxResults) break;
                            }
                        }
                        else if (item.TryGetProperty("shelfRenderer", out var shelf) &&
                                 shelf.TryGetProperty("content", out var shelfContent) &&
                                 shelfContent.TryGetProperty("verticalListRenderer", out var vlr) &&
                                 vlr.TryGetProperty("items", out var shelfItems))
                        {
                            foreach (var sItem in shelfItems.EnumerateArray())
                            {
                                if (sItem.TryGetProperty("videoRenderer", out var sVr))
                                {
                                    var app = ParseVideoRenderer(sVr);
                                    if (app != null)
                                    {
                                        list.Add(app);
                                        if (list.Count >= maxResults) break;
                                    }
                                }
                            }
                        }
                    }
                }
                if (list.Count >= maxResults) break;
            }

            // Download thumbnails asynchronously in the background so search returns immediately
            _ = Task.Run(async () =>
            {
                try
                {
                    var tasks = list.Where(i => i.Icon == null && !string.IsNullOrEmpty(i.ThumbnailUrl)).Select(async item =>
                    {
                        var bmp = await LoadBitmapFromUrlAsync(item.ThumbnailUrl!, decodeWidth: 96, CancellationToken.None);
                        if (bmp != null)
                        {
                            System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() => item.Icon = bmp);
                        }
                    });
                    await Task.WhenAll(tasks);
                }
                catch { }
            });
        }
        catch (OperationCanceledException) { }
        catch { }

        return list;
    }

    private static PinnedApp? ParseVideoRenderer(JsonElement vr)
    {
        if (!vr.TryGetProperty("videoId", out var vidElem)) return null;
        string videoId = vidElem.GetString() ?? "";
        if (string.IsNullOrEmpty(videoId)) return null;

        string title = "";
        if (vr.TryGetProperty("title", out var titleElem))
        {
            if (titleElem.TryGetProperty("runs", out var runs) && runs.GetArrayLength() > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var run in runs.EnumerateArray())
                {
                    if (run.TryGetProperty("text", out var tProp))
                        sb.Append(tProp.GetString());
                }
                title = sb.ToString();
            }
            else if (titleElem.TryGetProperty("simpleText", out var st))
            {
                title = st.GetString() ?? "";
            }
        }
        if (string.IsNullOrWhiteSpace(title)) return null;

        string channel = "";
        if (vr.TryGetProperty("ownerText", out var ot) &&
            ot.TryGetProperty("runs", out var chRuns) && chRuns.GetArrayLength() > 0)
        {
            channel = chRuns[0].GetProperty("text").GetString() ?? "";
        }
        else if (vr.TryGetProperty("shortBylineText", out var sbt) &&
                 sbt.TryGetProperty("runs", out var sbtRuns) && sbtRuns.GetArrayLength() > 0)
        {
            channel = sbtRuns[0].GetProperty("text").GetString() ?? "";
        }
        else if (vr.TryGetProperty("longBylineText", out var lbt) &&
                 lbt.TryGetProperty("runs", out var lbtRuns) && lbtRuns.GetArrayLength() > 0)
        {
            channel = lbtRuns[0].GetProperty("text").GetString() ?? "";
        }

        string duration = "";
        if (vr.TryGetProperty("lengthText", out var lt))
        {
            if (lt.TryGetProperty("simpleText", out var durSt))
                duration = durSt.GetString() ?? "";
            else if (lt.TryGetProperty("runs", out var durRuns) && durRuns.GetArrayLength() > 0)
                duration = durRuns[0].GetProperty("text").GetString() ?? "";
        }

        // Always use standard JPEG mqdefault thumbnail (100% reliable, fast, zero WebP decoding failures)
        string thumbUrl = $"https://i.ytimg.com/vi/{videoId}/mqdefault.jpg";

        return new PinnedApp
        {
            Name = title,
            Subtitle = string.IsNullOrEmpty(duration) ? channel : $"{channel} • {duration}",
            Path = $"ytmusic:play:{videoId}",
            ActionText = string.IsNullOrEmpty(duration) ? "Música" : duration,
            IconGlyph = "\uE217",
            ThumbnailUrl = thumbUrl,
            VideoId = videoId,
            Duration = duration,
            Channel = channel,
            ToolTip = $"Reproducir {title} ({duration})"
        };
    }

    public static async Task<BitmapImage?> LoadBitmapFromUrlAsync(string url, int decodeWidth = 100, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!resp.IsSuccessStatusCode) return null;

            var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes == null || bytes.Length == 0) return null;

            using var ms = new MemoryStream(bytes);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }
}
