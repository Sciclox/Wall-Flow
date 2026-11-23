using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace WallFlow.Services;

public class ThumbnailCacheService
{
    private static ThumbnailCacheService? _instance;
    public static ThumbnailCacheService Instance => _instance ??= new ThumbnailCacheService();

    private readonly string _cacheDir;
    private readonly SemaphoreSlim _semaphore = new(4, 4);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapImage> _l1MemoryCache = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _l1Keys = new();
    private const int MaxMemoryCacheItems = 120;

    public ThumbnailCacheService()
    {
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WallFlow", "Cache", "Thumbnails");

        try
        {
            Directory.CreateDirectory(_cacheDir);
        }
        catch { }
    }

    private void PutInMemoryCache(string key, BitmapImage bmp)
    {
        if (_l1MemoryCache.TryAdd(key, bmp))
        {
            _l1Keys.Enqueue(key);
            while (_l1Keys.Count > MaxMemoryCacheItems && _l1Keys.TryDequeue(out var oldKey))
            {
                _l1MemoryCache.TryRemove(oldKey, out _);
            }
        }
    }

    public async Task<BitmapImage?> GetThumbnailAsync(string filePath, int targetWidth = 500)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        return await Task.Run(async () =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                FileInfo fileInfo;
                try
                {
                    fileInfo = new FileInfo(filePath);
                }
                catch
                {
                    return null;
                }

                string keyRaw = $"{filePath}_{fileInfo.LastWriteTimeUtc.Ticks}_{fileInfo.Length}_{targetWidth}";
                string cacheKey = ComputeMd5Hash(keyRaw);

                // Fast path: L1 memory cache check (0ms, no semaphore, no I/O)
                if (_l1MemoryCache.TryGetValue(cacheKey, out var memBmp))
                {
                    return memBmp;
                }

                await _semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    // Double check memory cache after entering semaphore
                    if (_l1MemoryCache.TryGetValue(cacheKey, out memBmp))
                    {
                        return memBmp;
                    }

                    string cachedFilePath = Path.Combine(_cacheDir, $"{cacheKey}.jpg");

                    if (File.Exists(cachedFilePath))
                    {
                        try
                        {
                            var cachedBmp = new BitmapImage();
                            cachedBmp.BeginInit();
                            using (var fs = new FileStream(cachedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                cachedBmp.StreamSource = fs;
                                cachedBmp.CacheOption = BitmapCacheOption.OnLoad;
                                cachedBmp.EndInit();
                            }
                            cachedBmp.Freeze();
                            PutInMemoryCache(cacheKey, cachedBmp);
                            return cachedBmp;
                        }
                        catch
                        {
                            // If cached file is corrupted, delete it and regenerate
                            try { File.Delete(cachedFilePath); } catch { }
                        }
                    }

                    // Generate thumbnail from original file using stream and DecodePixelWidth
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        bmp.StreamSource = fs;
                        bmp.DecodePixelWidth = targetWidth;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                    }
                    bmp.Freeze();
                    PutInMemoryCache(cacheKey, bmp);

                    // Save thumbnail to disk cache asynchronously without colliding filenames
                    _ = SaveThumbnailToDiskAsync(cachedFilePath, bmp);

                    return bmp;
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch
            {
                return null;
            }
        }).ConfigureAwait(false);
    }

    private static async Task SaveThumbnailToDiskAsync(string cachedFilePath, BitmapImage bmp)
    {
        await Task.Run(() =>
        {
            string tempSave = $"{cachedFilePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = new FileStream(tempSave, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(fs);
                }
                File.Move(tempSave, cachedFilePath, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tempSave)) File.Delete(tempSave); } catch { }
            }
        }).ConfigureAwait(false);
    }

    public void ClearCache()
    {
        _l1MemoryCache.Clear();
        while (_l1Keys.TryDequeue(out _)) { }

        try
        {
            if (Directory.Exists(_cacheDir))
            {
                var files = Directory.GetFiles(_cacheDir, "*.jpg");
                foreach (var file in files)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    private static string ComputeMd5Hash(string input)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = MD5.HashData(inputBytes);
        return Convert.ToHexString(hashBytes);
    }
}
