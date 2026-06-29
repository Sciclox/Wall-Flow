using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
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
    public string CachedThumbPath { get; set; } = "";
}

public partial class UHDPaperService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;
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

    private static readonly string[] SupportedExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    public UHDPaperService()
    {
        var rng = Random.Shared.Next(UserAgents.Length);
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgents[rng]);
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        _http.Timeout = TimeSpan.FromSeconds(30);

        _cacheDir = Path.Combine(Path.GetTempPath(), "WallFlow", "cache", "uhdpaper");
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<List<OnlineWallpaper>> GetLatestAsync(int page = 1)
    {
        var url = page == 1
            ? "https://www.uhdpaper.com/"
            : $"https://www.uhdpaper.com/search?updated-max=2026-06-28T00:00:00%2B08:00&max-results=20";

        return await ParseListingAsync(url);
    }

    public async Task<List<OnlineWallpaper>> SearchAsync(string query, int page = 1)
    {
        var encoded = WebUtility.UrlEncode(query);
        var url = $"https://www.uhdpaper.com/search?q={encoded}&by-date=true&max-results=20";
        if (page > 1)
            url += $"&start={20 * (page - 1)}";

        return await ParseListingAsync(url);
    }

    public async Task<OnlineWallpaper?> GetDetailAsync(OnlineWallpaper wallpaper)
    {
        try
        {
            var html = await _http.GetStringAsync(wallpaper.PostUrl);
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
            return wallpaper;
        }
        catch
        {
            return null;
        }
    }

    public string? GetCachedThumbPath(string slug)
    {
        var path = Path.Combine(_cacheDir, slug + ".jpg");
        return File.Exists(path) ? path : null;
    }

    public async Task<string> CacheThumbnailAsync(string thumbUrl, string slug)
    {
        var path = Path.Combine(_cacheDir, slug + ".jpg");
        if (File.Exists(path))
            return path;

        try
        {
            var data = await _http.GetByteArrayAsync(thumbUrl);
            await File.WriteAllBytesAsync(path, data);
            return path;
        }
        catch
        {
            return "";
        }
    }

    public async Task<string> DownloadImageAsync(string url, string destDir, string fileName)
    {
        Directory.CreateDirectory(destDir);
        var path = Path.Combine(destDir, fileName);

        if (File.Exists(path))
            return path;

        try
        {
            var data = await _http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(path, data);
            return path;
        }
        catch
        {
            return "";
        }
    }

    public string GetTempPath(string slug, string resolution)
    {
        var dir = Path.Combine(Path.GetTempPath(), "WallFlow", "online");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{slug}-{resolution}.jpg");
    }

    private async Task<List<OnlineWallpaper>> ParseListingAsync(string url)
    {
        var results = new List<OnlineWallpaper>();

        try
        {
            var html = await _http.GetStringAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var postLinks = doc.DocumentNode.SelectNodes("//a[contains(@href,'/202') and contains(@href,'.html')]");
            if (postLinks == null)
                return results;

            var seen = new HashSet<string>();

            foreach (var link in postLinks)
            {
                var href = link.GetAttributeValue("href", "");
                if (string.IsNullOrEmpty(href)) continue;

                var cleanUrl = href.Split('?')[0];
                if (!cleanUrl.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) continue;
                if (cleanUrl.Contains("search") || cleanUrl.Contains("/p/")) continue;
                if (!seen.Add(cleanUrl)) continue;

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

                results.Add(new OnlineWallpaper
                {
                    Title = title,
                    PostUrl = cleanUrl,
                    ThumbnailUrl = thumbUrl,
                    Slug = slug,
                });
            }
        }
        catch
        {
        }

        return results;
    }

    private static string GetSlugFromUrl(string url)
    {
        var path = new Uri(url).AbsolutePath;
        var name = path.TrimEnd('/').Replace(".html", "");
        return name.Contains('/') ? name.Split('/').Last() : name;
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
