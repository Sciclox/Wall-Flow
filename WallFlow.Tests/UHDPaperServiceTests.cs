using System.Collections.Generic;
using WallFlow.Services;
using Xunit;

namespace WallFlow.Tests;

public class UHDPaperServiceTests
{
    [Fact]
    public void OnlineWallpaper_Initialization_PropertiesAreConsistent()
    {
        var wallpaper = new OnlineWallpaper
        {
            Title = "Cyberpunk City",
            PostUrl = "https://www.uhdpaper.com/2024/01/cyberpunk-city.html",
            ThumbnailUrl = "https://img.uhdpaper.com/wallpaper/cyberpunk-city-preview.jpg",
            Slug = "cyberpunk-city",
            Resolutions = new Dictionary<string, string>
            {
                ["4k"] = "https://img.uhdpaper.com/wallpaper/cyberpunk-city-pc-4k.jpg",
                ["hd"] = "https://img.uhdpaper.com/wallpaper/cyberpunk-city-pc-hd.jpg"
            }
        };

        Assert.Equal("Cyberpunk City", wallpaper.Title);
        Assert.Equal("cyberpunk-city", wallpaper.Slug);
        Assert.Equal(2, wallpaper.Resolutions.Count);
        Assert.True(wallpaper.Resolutions.ContainsKey("4k"));
        Assert.True(wallpaper.Resolutions.ContainsKey("hd"));
    }

    [Fact]
    public void ListingResult_InitialState_ItemsListIsNotNull()
    {
        var result = new ListingResult();

        Assert.NotNull(result.Items);
        Assert.Empty(result.Items);
        Assert.Null(result.NextPageUrl);
    }
}
