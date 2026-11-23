using WallFlow.Services;
using Xunit;

namespace WallFlow.Tests;

public class WeatherServiceTests
{
    [Theory]
    [InlineData(0, "Despejado")]
    [InlineData(1, "Mayormente Despejado")]
    [InlineData(3, "Cubierto")]
    [InlineData(61, "Lluvia")]
    [InlineData(95, "Tormenta")]
    public void MapWeatherCode_MapsCorrectConditionText(int code, string expected)
    {
        string condition = WeatherService.MapWeatherCode(code);
        Assert.Equal(expected, condition);
    }

    [Theory]
    [InlineData(0, true, "\uE706")]  // Sun
    [InlineData(0, false, "\uE708")] // Moon
    [InlineData(3, true, "\uE753")]  // Cloud
    [InlineData(61, true, "\uE774")] // Rain
    public void MapWeatherGlyph_ReturnsExpectedGlyph(int code, bool isDay, string expectedGlyph)
    {
        string glyph = WeatherService.MapWeatherGlyph(code, isDay);
        Assert.Equal(expectedGlyph, glyph);
    }

    [Theory]
    [InlineData("-11.8489,-77.0060", -11.8489, -77.0060)]
    [InlineData("40.7128, -74.0060", 40.7128, -74.0060)]
    [InlineData("0,0", 0.0, 0.0)]
    public void TryParseCoordinates_ValidDecimalCoordinates_ParsesCorrectly(string input, double expectedLat, double expectedLon)
    {
        bool success = WeatherService.TryParseCoordinates(input, out double lat, out double lon);

        Assert.True(success);
        Assert.Equal(expectedLat, lat, 4);
        Assert.Equal(expectedLon, lon, 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("123")]
    [InlineData("abc,def")]
    public void TryParseCoordinates_InvalidInput_ReturnsFalse(string input)
    {
        bool success = WeatherService.TryParseCoordinates(input, out _, out _);
        Assert.False(success);
    }
}
