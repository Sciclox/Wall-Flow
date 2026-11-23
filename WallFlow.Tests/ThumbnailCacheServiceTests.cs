using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using WallFlow.Services;
using Xunit;

namespace WallFlow.Tests;

public class ThumbnailCacheServiceTests
{
    [Fact]
    public async Task GetThumbnailAsync_ReturnsNull_WhenFilePathEmptyOrNull()
    {
        var service = new ThumbnailCacheService();

        var result1 = await service.GetThumbnailAsync(string.Empty);
        var result2 = await service.GetThumbnailAsync("   ");

        Assert.Null(result1);
        Assert.Null(result2);
    }

    [Fact]
    public async Task GetThumbnailAsync_ReturnsNull_WhenFileNotFound()
    {
        var service = new ThumbnailCacheService();
        string nonExistent = Path.Combine(Path.GetTempPath(), $"not_found_{Guid.NewGuid():N}.jpg");

        var result = await service.GetThumbnailAsync(nonExistent);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetThumbnailAsync_GeneratesAndCachesThumbnail_ForValidImage()
    {
        var service = new ThumbnailCacheService();
        string tempDir = Path.Combine(Path.GetTempPath(), $"WF_ThumbTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string imagePath = Path.Combine(tempDir, "sample.jpg");

        try
        {
            // Create a small valid test JPEG bitmap
            int width = 100;
            int height = 100;
            var pixelFormat = System.Windows.Media.PixelFormats.Bgr32;
            int stride = width * 4;
            byte[] pixelData = new byte[height * stride];
            // Fill with blue color
            for (int i = 0; i < pixelData.Length; i += 4)
            {
                pixelData[i] = 255; // B
                pixelData[i + 1] = 0; // G
                pixelData[i + 2] = 0; // R
                pixelData[i + 3] = 255; // A
            }

            var writeableBmp = BitmapSource.Create(width, height, 96, 96, pixelFormat, null, pixelData, stride);
            using (var fs = new FileStream(imagePath, FileMode.Create))
            {
                var encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(writeableBmp));
                encoder.Save(fs);
            }

            // 1. Initial retrieval (generates disk thumbnail and stores in memory)
            var thumb1 = await service.GetThumbnailAsync(imagePath, targetWidth: 50);
            Assert.NotNull(thumb1);
            Assert.True(thumb1.PixelWidth > 0);

            // 2. Second retrieval should come instantly from memory cache
            var thumb2 = await service.GetThumbnailAsync(imagePath, targetWidth: 50);
            Assert.NotNull(thumb2);
            Assert.Same(thumb1, thumb2);

            // 3. Clear cache
            service.ClearCache();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
