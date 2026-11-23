using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WallFlow.Services;

public class ClipboardItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "Text"; // Text, Code, Link, Image, File
    public string Content { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public List<string> Tags { get; set; } = new();
    public List<string> Colors { get; set; } = new();
}

public class ClipboardService
{
    private static ClipboardService? _instance;
    public static ClipboardService Instance => _instance ??= new ClipboardService();

    private readonly string _cacheFolder;
    private readonly string _historyFilePath;
    private readonly List<ClipboardItem> _history = new();
    private readonly object _lock = new();
    private bool _isInternalCopy = false;

    public List<ClipboardItem> History
    {
        get
        {
            lock (_lock)
            {
                return new List<ClipboardItem>(_history);
            }
        }
    }

    public event Action? HistoryChanged;

    private ClipboardService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WallFlow");
        _cacheFolder = Path.Combine(appData, "clipboard_cache");
        _historyFilePath = Path.Combine(appData, "clipboard_history.json");

        try
        {
            Directory.CreateDirectory(_cacheFolder);
            LoadHistory();
            CleanOrphanImages();
        }
        catch { }
    }

    private void LoadHistory()
    {
        try
        {
            if (File.Exists(_historyFilePath))
            {
                var json = File.ReadAllText(_historyFilePath);
                var items = System.Text.Json.JsonSerializer.Deserialize<List<ClipboardItem>>(json);
                if (items != null)
                {
                    lock (_lock)
                    {
                        _history.Clear();
                        foreach (var item in items)
                        {
                            if (item.Type == "Image" && (string.IsNullOrEmpty(item.ImagePath) || !File.Exists(item.ImagePath)))
                            {
                                continue;
                            }
                            _history.Add(item);
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void SaveHistoryAsync()
    {
        Task.Run(() =>
        {
            try
            {
                List<ClipboardItem> snapshot;
                lock (_lock)
                {
                    snapshot = new List<ClipboardItem>(_history);
                }

                var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
                string tempFile = _historyFilePath + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, _historyFilePath, overwrite: true);
            }
            catch { }
        });
    }

    private void CleanOrphanImages()
    {
        try
        {
            HashSet<string> referencedFiles;
            lock (_lock)
            {
                referencedFiles = new HashSet<string>(
                    _history.Where(h => h.Type == "Image" && !string.IsNullOrEmpty(h.ImagePath))
                            .Select(h => Path.GetFullPath(h.ImagePath)),
                    StringComparer.OrdinalIgnoreCase);
            }

            if (Directory.Exists(_cacheFolder))
            {
                var di = new DirectoryInfo(_cacheFolder);
                foreach (var file in di.GetFiles())
                {
                    if (!referencedFiles.Contains(file.FullName))
                    {
                        try { file.Delete(); } catch { }
                    }
                }
            }
        }
        catch { }
    }

    public void ClearAllHistory()
    {
        lock (_lock)
        {
            foreach (var item in _history)
            {
                if (item.Type == "Image" && !string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
                {
                    try { File.Delete(item.ImagePath); } catch { }
                }
            }
            _history.Clear();
        }

        SaveHistoryAsync();
        HistoryChanged?.Invoke();
    }

    /// <summary>
    /// Copy content back to clipboard internally, avoiding triggering the listener loop.
    /// </summary>
    public void CopyToClipboardInternal(string text)
    {
        _isInternalCopy = true;
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            _isInternalCopy = false;
        }
    }

    /// <summary>
    /// Add AI/ChatGPT generated response directly to history with 'IA' type and '#IA' tag.
    /// </summary>
    public void AddAiItem(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var newItem = new ClipboardItem
        {
            Type = "IA",
            Content = text,
            Timestamp = DateTime.Now,
            Tags = new List<string> { "#IA", "#Texto" }
        };

        lock (_lock)
        {
            var existing = _history.FirstOrDefault(h => h.Content == text);
            if (existing != null)
            {
                _history.Remove(existing);
                existing.Timestamp = DateTime.Now;
                _history.Insert(0, existing);
            }
            else
            {
                _history.Insert(0, newItem);
            }

            while (_history.Count > 50)
            {
                var old = _history[_history.Count - 1];
                if (old.Type == "Image" && File.Exists(old.ImagePath))
                {
                    try { File.Delete(old.ImagePath); } catch { }
                }
                _history.RemoveAt(_history.Count - 1);
            }
        }

        try
        {
            _isInternalCopy = true;
            Clipboard.SetText(text);
        }
        catch
        {
            _isInternalCopy = false;
        }

        HistoryChanged?.Invoke();
        SaveHistoryAsync();
    }

    public void CopyToClipboardInternal(Guid id)
    {
        _isInternalCopy = true;
        try
        {
            ClipboardItem? item = null;
            lock (_lock)
            {
                item = _history.FirstOrDefault(i => i.Id == id);
            }
            if (item == null)
            {
                _isInternalCopy = false;
                return;
            }

            if (item.Type == "Image" && !string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
            {
                var uri = new Uri(item.ImagePath, UriKind.Absolute);
                var bmi = new BitmapImage(uri);
                Clipboard.SetImage(bmi);
            }
            else if (item.Type == "File")
            {
                var files = item.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                var sc = new System.Collections.Specialized.StringCollection();
                foreach (var f in files) sc.Add(f);
                Clipboard.SetFileDropList(sc);
            }
            else
            {
                Clipboard.SetText(item.Content);
            }
        }
        catch
        {
            _isInternalCopy = false;
        }
    }

    public void DeleteItem(Guid id)
    {
        lock (_lock)
        {
            var item = _history.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                if (item.Type == "Image" && File.Exists(item.ImagePath))
                {
                    try { File.Delete(item.ImagePath); } catch { }
                }
                _history.Remove(item);
                HistoryChanged?.Invoke();
                SaveHistoryAsync();
            }
        }
    }

    public void OnClipboardUpdated()
    {
        if (_isInternalCopy)
        {
            _isInternalCopy = false;
            return;
        }

        // Perform clipboard reading on STA thread with retries
        Task.Run(() =>
        {
            // Wait slightly for copy process to finish locking clipboard
            Thread.Sleep(50);
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                int retries = 5;
                while (retries > 0)
                {
                    try
                    {
                        var dataObject = Clipboard.GetDataObject();
                        if (dataObject == null) return;

                        ClipboardItem? newItem = null;

                        // 1. Check for Image
                        if (Clipboard.ContainsImage())
                        {
                            var bitmap = Clipboard.GetImage();
                            if (bitmap != null)
                            {
                                bitmap.Freeze();
                                Task.Run(() =>
                                {
                                    try
                                    {
                                        string fileName = Guid.NewGuid().ToString() + ".png";
                                        string filePath = Path.Combine(_cacheFolder, fileName);

                                        using (var fs = new FileStream(filePath, FileMode.Create))
                                        {
                                            var encoder = new PngBitmapEncoder();
                                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                                            encoder.Save(fs);
                                        }

                                        var colors = ExtractDominantColors(bitmap);

                                        var imageItem = new ClipboardItem
                                        {
                                            Type = "Image",
                                            ImagePath = filePath,
                                            Content = filePath,
                                            Colors = colors,
                                            Tags = new List<string> { "#Imágenes", "#Color" }
                                        };

                                        AddItemToHistory(imageItem);
                                    }
                                    catch { }
                                });
                                break;
                            }
                        }
                        // 2. Check for File Drop List
                        else if (Clipboard.ContainsFileDropList())
                        {
                            var files = Clipboard.GetFileDropList();
                            if (files != null && files.Count > 0)
                            {
                                var fileList = new List<string>();
                                foreach (var f in files) if (!string.IsNullOrEmpty(f)) fileList.Add(f);

                                string content = string.Join(Environment.NewLine, fileList);
                                if (IsDuplicate(content)) return;

                                newItem = new ClipboardItem
                                {
                                    Type = "File",
                                    Content = content,
                                    Tags = new List<string> { "#Archivos" }
                                };
                            }
                        }
                        // 3. Check for Text
                        else if (Clipboard.ContainsText())
                        {
                            string text = Clipboard.GetText();
                            if (string.IsNullOrWhiteSpace(text)) return;
                            if (IsDuplicate(text)) return;

                            newItem = ParseTextClipboard(text);
                        }

                        if (newItem != null)
                        {
                            AddItemToHistory(newItem);
                        }

                        break; // Success
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        retries--;
                        Thread.Sleep(30);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Clipboard read error: {ex.Message}");
                        break;
                    }
                }
            });
        });
    }

    private void AddItemToHistory(ClipboardItem newItem)
    {
        lock (_lock)
        {
            var existing = _history.FirstOrDefault(h => 
                h.Type == newItem.Type && 
                (h.Content == newItem.Content || 
                 (h.Type == "Image" && !string.IsNullOrEmpty(h.ImagePath) && !string.IsNullOrEmpty(newItem.ImagePath) && 
                  Path.GetFileName(h.ImagePath) == Path.GetFileName(newItem.ImagePath)))
            );

            if (existing != null)
            {
                _history.Remove(existing);
                existing.Timestamp = DateTime.Now;
                _history.Insert(0, existing);

                if (newItem.Type == "Image" && File.Exists(newItem.ImagePath))
                {
                    try { File.Delete(newItem.ImagePath); } catch { }
                }
            }
            else
            {
                _history.Insert(0, newItem);
            }

            // Limit history size to 50
            while (_history.Count > 50)
            {
                var old = _history[_history.Count - 1];
                if (old.Type == "Image" && File.Exists(old.ImagePath))
                {
                    try { File.Delete(old.ImagePath); } catch { }
                }
                _history.RemoveAt(_history.Count - 1);
            }
        }

        HistoryChanged?.Invoke();
        SaveHistoryAsync();
        SoundService.Play(SoundType.Ping);
    }

    private bool IsDuplicate(string content)
    {
        lock (_lock)
        {
            if (_history.Count > 0)
            {
                var latest = _history[0];
                if (latest.Content == content) return true;
            }
        }
        return false;
    }

    private ClipboardItem ParseTextClipboard(string text)
    {
        var item = new ClipboardItem
        {
            Content = text,
            Timestamp = DateTime.Now
        };

        string trimmed = text.Trim();

        // Check for Hex color code (e.g. #FF00FF, #00FF55)
        var colorRegex = new Regex(@"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$");
        if (colorRegex.IsMatch(trimmed))
        {
            item.Type = "Text";
            item.Colors = new List<string> { trimmed };
            item.Tags = new List<string> { "#Color", "#Texto" };
            return item;
        }

        // Check for Link/URL
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && 
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            item.Type = "Link";
            item.Tags = new List<string> { "#Enlaces" };
            return item;
        }

        // Check for Code Snippet
        if (IsCodeSnippet(text))
        {
            item.Type = "Code";
            item.Tags = new List<string> { "#Código" };
            return item;
        }

        // Default: Plain Text
        item.Type = "Text";
        item.Tags = new List<string> { "#Texto" };
        return item;
    }

    private bool IsCodeSnippet(string text)
    {
        // Code markers and syntax heuristics
        string[] keywords = {
            "public class", "private void", "using System", "namespace ", "import ", 
            "const ", "let ", "function ", "def ", "class ", "elif ", "struct ",
            "SELECT ", "FROM ", "WHERE ", "</html>", "</div>", "Console.Write",
            "console.log", "void ", "int ", "string ", "double ", "float ", "bool "
        };

        int score = 0;
        foreach (var kw in keywords)
        {
            if (text.Contains(kw)) score += 2;
        }

        // Structural check: brackets, semicolons, indents
        if (text.Contains("{") && text.Contains("}")) score += 2;
        if (text.Contains(";") && text.Count(c => c == ';') > 1) score += 1;
        if (text.Contains("\t") || text.Contains("    ")) score += 1;

        return score >= 3 && text.Length > 10;
    }

    private List<string> ExtractDominantColors(BitmapSource image)
    {
        var colors = new List<string>();
        try
        {
            // Scale down to 12x12 to average color spaces
            int width = 12;
            int height = 12;
            var scaled = new TransformedBitmap(
                image,
                new ScaleTransform((double)width / image.PixelWidth, (double)height / image.PixelHeight));

            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            scaled.CopyPixels(pixels, stride, 0);

            var colorCounts = new Dictionary<uint, int>();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];

                if (a < 128) continue; // Ignore transparent/faint pixels

                // Quantize: group into bins of size 32
                uint qr = (uint)(Math.Round(r / 32.0) * 32);
                uint qg = (uint)(Math.Round(g / 32.0) * 32);
                uint qb = (uint)(Math.Round(b / 32.0) * 32);

                qr = Math.Min(qr, 255);
                qg = Math.Min(qg, 255);
                qb = Math.Min(qb, 255);

                uint key = (qr << 16) | (qg << 8) | qb;
                if (colorCounts.ContainsKey(key))
                    colorCounts[key]++;
                else
                    colorCounts[key] = 1;
            }

            var sorted = colorCounts.OrderByDescending(kvp => kvp.Value).Take(4);
            foreach (var kvp in sorted)
            {
                uint key = kvp.Key;
                byte r = (byte)((key >> 16) & 0xFF);
                byte g = (byte)((key >> 8) & 0xFF);
                byte b = (byte)(key & 0xFF);
                colors.Add($"#{r:X2}{g:X2}{b:X2}");
            }
        }
        catch { }

        if (colors.Count == 0)
        {
            colors.Add("#AF52DE"); // Default purple accent
        }

        return colors;
    }
}
