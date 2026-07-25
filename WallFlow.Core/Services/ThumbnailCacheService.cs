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

    public async Task<BitmapImage?> GetThumbnailAsync(string filePath, int targetWidth = 500)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        await _semaphore.WaitAsync();
        try
        {
            var fileInfo = new FileInfo(filePath);
            string keyRaw = $"{filePath}_{fileInfo.LastWriteTimeUtc.Ticks}_{fileInfo.Length}_{targetWidth}";
            string cacheKey = ComputeMd5Hash(keyRaw);
            string cachedFilePath = Path.Combine(_cacheDir, $"{cacheKey}.jpg");

            if (File.Exists(cachedFilePath))
            {
                try
                {
                    byte[] cachedBytes = await File.ReadAllBytesAsync(cachedFilePath);
                    var cachedBmp = new BitmapImage();
                    cachedBmp.BeginInit();
                    using (var ms = new MemoryStream(cachedBytes))
                    {
                        cachedBmp.StreamSource = ms;
                        cachedBmp.CacheOption = BitmapCacheOption.OnLoad;
                        cachedBmp.EndInit();
                    }
                    cachedBmp.Freeze();
                    return cachedBmp;
                }
                catch
                {
                    // If cached file is corrupted, delete it and regenerate
                    try { File.Delete(cachedFilePath); } catch { }
                }
            }

            // Generate thumbnail from original file
            byte[] originalBytes = await File.ReadAllBytesAsync(filePath);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            using (var ms = new MemoryStream(originalBytes))
            {
                bmp.StreamSource = ms;
                bmp.DecodePixelWidth = targetWidth;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
            }
            bmp.Freeze();

            // Save thumbnail to cache asynchronously
            _ = Task.Run(() =>
            {
                try
                {
                    var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    using var fs = new FileStream(cachedFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    encoder.Save(fs);
                }
                catch { }
            });

            return bmp;
        }
        catch
        {
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void ClearCache()
    {
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
        using var md5 = MD5.Create();
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = md5.ComputeHash(inputBytes);
        return Convert.ToHexString(hashBytes);
    }
}
