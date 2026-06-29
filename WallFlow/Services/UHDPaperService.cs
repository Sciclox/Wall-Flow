using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace WallFlow.Services;

public class OnlineWallpaper
{
    public string Title { get; set; } = "";
    public string PostUrl { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public string Slug { get; set; } = "";
    public Dictionary<string, string> Resolutions { get; set; } = new();
}

public class ListingResult
{
    public List<OnlineWallpaper> Items { get; set; } = new();
    public string? NextPageUrl { get; set; }
}

public partial class UHDPaperService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly string _previewCacheDir;
    private readonly ConcurrentDictionary<string, ListingResult> _listingCache = new();
    private readonly ConcurrentDictionary<string, OnlineWallpaper> _detailCache = new();
    private string? _nextPageUrl;
    private bool _disposed;

    private static readonly string[] UserAgents =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:127.0) Gecko/20100101 Firefox/127.0",
    ];

    private static readonly Dictionary<string, string> ResolutionSuffixes = new()
    {
        ["4k"] = "pc-4k",
        ["2k"] = "pc-2k",
        ["hd"] = "pc-hd",
        ["phone-4k"] = "phone-4k",
        ["phone-hd"] = "phone-hd",
    };

    public UHDPaperService()
    {
        var rng = Random.Shared.Next(UserAgents.Length);
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgents[rng]);
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        _http.Timeout = TimeSpan.FromSeconds(30);

        _cacheDir = Path.Combine(Path.GetTempPath(), "WallFlow", "cache", "uhdpaper");
        _previewCacheDir = Path.Combine(_cacheDir, "previews");
        Directory.CreateDirectory(_cacheDir);
    }

    // ---- Pagination ----

    public void ResetPagination() => _nextPageUrl = null;

    public async Task<ListingResult> GetLatestAsync(CancellationToken ct = default)
    {
        var url = _nextPageUrl ?? "https://www.uhdpaper.com/";

        if (_listingCache.TryGetValue(url, out var cached))
            return cached;

        var result = await ParseListingAsync(url, ct);

        if (result.Items.Count > 0)
            _listingCache[url] = result;

        return result;
    }

    public async Task<ListingResult> SearchAsync(string query, CancellationToken ct = default)
    {
        var encoded = WebUtility.UrlEncode(query);
        var url = $"https://www.uhdpaper.com/search?q={encoded}&by-date=true&max-results=20";

        if (_listingCache.TryGetValue(url, out var cached))
            return cached;

        var result = await ParseListingAsync(url, ct);

        if (result.Items.Count > 0)
            _listingCache[url] = result;

        return result;
    }

    // ---- Detail ----

    public async Task<OnlineWallpaper?> GetDetailAsync(OnlineWallpaper wallpaper, CancellationToken ct = default)
    {
        if (_detailCache.TryGetValue(wallpaper.PostUrl, out var cached))
        {
            wallpaper.Resolutions = cached.Resolutions;
            return wallpaper;
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            var html = await _http.GetStringAsync(wallpaper.PostUrl, ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var resolutions = new Dictionary<string, string>();

            var links = doc.DocumentNode.SelectNodes("//a[contains(@href,'img.uhdpaper.com/wallpaper/')]");
            if (links != null)
            {
                foreach (var link in links)
                {
                    var href = link.GetAttributeValue("href", "");
                    if (!href.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var kv in ResolutionSuffixes)
                    {
                        var suffix = $"-{kv.Value}.";
                        if (href.Contains(suffix) && !resolutions.ContainsKey(kv.Key))
                        {
                            resolutions[kv.Key] = href;
                            break;
                        }
                    }
                }
            }

            if (resolutions.Count == 0)
            {
                var imgs = doc.DocumentNode.SelectNodes("//img[contains(@src,'img.uhdpaper.com/wallpaper/')]");
                if (imgs != null)
                {
                    foreach (var img in imgs)
                    {
                        var src = img.GetAttributeValue("src", "");
                        if (src.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) && !src.Contains("thumb"))
                        {
                            var clean = src.StartsWith("//") ? "https:" + src : src;
                            resolutions["original"] = clean;
                            break;
                        }
                    }
                }
            }

            wallpaper.Resolutions = resolutions;
            _detailCache[wallpaper.PostUrl] = wallpaper;
            return wallpaper;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    // ---- Download / Cache ----

    public string GetCachedThumbPath(string slug)
    {
        var path = Path.Combine(_cacheDir, slug + ".jpg");
        return File.Exists(path) ? path : "";
    }

    public string GetPreviewCachePath(string slug, string resolution)
    {
        var path = Path.Combine(_previewCacheDir, $"{slug}-{resolution}.jpg");
        return File.Exists(path) ? path : "";
    }

    public async Task<string> CacheThumbnailAsync(string thumbUrl, string slug, CancellationToken ct = default)
    {
        var path = Path.Combine(_cacheDir, slug + ".jpg");
        if (File.Exists(path))
            return path;

        try
        {
            var data = await DownloadWithRefererAsync(thumbUrl, ct);
            if (data == null || !IsValidImage(data))
                return "";
            await File.WriteAllBytesAsync(path, data, ct);
            return path;
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            return "";
        }
    }

    public async Task<string> DownloadImageAsync(string url, string destDir, string fileName, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destDir);
        var path = Path.Combine(destDir, fileName);

        if (File.Exists(path))
            return path;

        try
        {
            var data = await DownloadWithRefererAsync(url, ct);
            if (data == null || !IsValidImage(data))
                return "";
            await File.WriteAllBytesAsync(path, data, ct);
            return path;
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            return "";
        }
    }

    public async Task<string> DownloadPreviewAsync(string url, string slug, string resolution, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_previewCacheDir);
        var path = Path.Combine(_previewCacheDir, $"{slug}-{resolution}.jpg");

        if (File.Exists(path))
            return path;

        try
        {
            var data = await DownloadWithRefererAsync(url, ct);
            if (data == null || !IsValidImage(data))
                return "";
            await File.WriteAllBytesAsync(path, data, ct);
            return path;
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            return "";
        }
    }

    public string GetTempPath(string slug, string resolution)
    {
        var dir = Path.Combine(Path.GetTempPath(), "WallFlow", "online");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{slug}-{resolution}.jpg");
    }

    // ---- Internal ----

    private async Task<ListingResult> ParseListingAsync(string url, CancellationToken ct = default)
    {
        var result = new ListingResult();

        try
        {
            ct.ThrowIfCancellationRequested();

            var html = await _http.GetStringAsync(url, ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var postLinks = doc.DocumentNode.SelectNodes("//a[contains(@href,'/202') and contains(@href,'.html')]");
            if (postLinks == null)
                return result;

            var seen = new HashSet<string>();

            foreach (var link in postLinks)
            {
                var href = link.GetAttributeValue("href", "");
                if (string.IsNullOrEmpty(href)) continue;

                var cleanUrl = href.Split('?')[0];
                if (!cleanUrl.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) continue;
                if (cleanUrl.Contains("search") || cleanUrl.Contains("/p/")) continue;
                if (!seen.Add(cleanUrl)) continue;

                if (!cleanUrl.StartsWith("http"))
                    cleanUrl = "https://www.uhdpaper.com" + cleanUrl;

                var title = WebUtility.HtmlDecode(link.GetAttributeValue("title", "")).Trim();
                if (string.IsNullOrEmpty(title))
                    title = WebUtility.HtmlDecode(link.InnerText).Trim();
                if (string.IsNullOrEmpty(title))
                    title = GetSlugFromUrl(cleanUrl);

                var parentSection = link.Ancestors("div").FirstOrDefault() ?? link.ParentNode;
                var imgNode = parentSection?.SelectSingleNode(".//img[contains(@src,'img.uhdpaper.com/wallpaper/')]");
                var thumbUrl = imgNode?.GetAttributeValue("src", "") ?? "";

                if (thumbUrl.StartsWith("//"))
                    thumbUrl = "https:" + thumbUrl;

                var slug = GetSlugFromUrl(cleanUrl);

                result.Items.Add(new OnlineWallpaper
                {
                    Title = title,
                    PostUrl = cleanUrl,
                    ThumbnailUrl = thumbUrl,
                    Slug = slug,
                });
            }

            // Extract next page URL
            var nextLink = doc.DocumentNode.SelectSingleNode("//a[contains(text(),'Next') or contains(text(),'next')]");
            if (nextLink != null)
            {
                var nextHref = nextLink.GetAttributeValue("href", "");
                if (!string.IsNullOrEmpty(nextHref))
                {
                    if (!nextHref.StartsWith("http"))
                        nextHref = "https://www.uhdpaper.com" + nextHref;
                    result.NextPageUrl = nextHref;
                    _nextPageUrl = nextHref;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        return result;
    }

    private async Task<byte[]?> DownloadWithRefererAsync(string url, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri("https://www.uhdpaper.com/");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadAsByteArrayAsync(ct);
        return data;
    }

    private static bool IsValidImage(byte[] data)
    {
        if (data.Length < 4) return false;

        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return true;

        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return true;

        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
            return true;

        if (data[0] == 0x42 && data[1] == 0x4D)
            return true;

        if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46)
            return data.Length > 8 && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50;

        return false;
    }

    private static string GetSlugFromUrl(string url)
    {
        var path = url.Contains("://") ? new Uri(url).AbsolutePath : url;
        var name = path.TrimEnd('/').Replace(".html", "");
        return name.Contains('/') ? name.Split('/').Last() : name;
    }

    public void ClearCaches()
    {
        _listingCache.Clear();
        _detailCache.Clear();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _http.Dispose();
            _disposed = true;
        }
    }
}
