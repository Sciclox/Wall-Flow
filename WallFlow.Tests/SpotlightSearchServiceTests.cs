using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WallFlow.Spotlight.Services;
using WallFlow.Windows;
using Xunit;

namespace WallFlow.Tests;

public class SpotlightSearchServiceTests
{
    private readonly SpotlightSearchService _service = new();

    [Theory]
    [InlineData("f:document.pdf", "Files", "document.pdf")]
    [InlineData("file:report", "Files", "report")]
    [InlineData("/f notes.txt", "Files", "notes.txt")]
    [InlineData("a:chrome", "Apps", "chrome")]
    [InlineData("app:notepad", "Apps", "notepad")]
    [InlineData("/a cmd", "Apps", "cmd")]
    [InlineData("?quien fue Alan Turing", "AI", "quien fue Alan Turing")]
    [InlineData("@ai escribe un haiku", "AI", "escribe un haiku")]
    [InlineData("ai:resume esto", "AI", "resume esto")]
    [InlineData("/ai ayuda", "AI", "ayuda")]
    [InlineData("c:token", "Clipboard", "token")]
    [InlineData("clip:pass", "Clipboard", "pass")]
    [InlineData("/c copia", "Clipboard", "copia")]
    [InlineData("m:coldplay", "Music", "coldplay")]
    [InlineData("music:queen", "Music", "queen")]
    [InlineData("musica:daft punk", "Music", "daft punk")]
    [InlineData("play:imagine", "Music", "imagine")]
    [InlineData("/m radiohead", "Music", "radiohead")]
    [InlineData("play viva la vida", "Music", "viva la vida")]
    [InlineData("play", "Music", "play")]
    [InlineData("pause", "Music", "pause")]
    [InlineData("next", "Music", "next")]
    [InlineData("prev", "Music", "prev")]
    [InlineData("simple query", "Apps", "simple query")]
    public void RouteQuery_ResolvesCorrectTargetModeAndFilteredQuery(string input, string expectedMode, string expectedQuery)
    {
        var result = _service.RouteQuery(input, "Apps");

        Assert.Equal(expectedMode, result.TargetMode);
        Assert.Equal(expectedQuery, result.FilteredQuery);
    }

    [Theory]
    [InlineData("2 + 2", "4")]
    [InlineData("10 * 5", "50")]
    [InlineData("100 / 4", "25")]
    [InlineData("(15 + 5) * 2", "40")]
    public void TryEvaluateMath_ValidExpressions_ReturnCalculatedResult(string expr, string expected)
    {
        var result = _service.TryEvaluateMath(expr);

        Assert.NotNull(result);
        Assert.Equal(expected, result!.Name);
        Assert.Equal("clipboard:" + expected, result.Path);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("not a math expr")]
    [InlineData("2")]
    public void TryEvaluateMath_NonMathExpressions_ReturnNull(string expr)
    {
        var result = _service.TryEvaluateMath(expr);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("apagar", "system:shutdown")]
    [InlineData("shutdown", "system:shutdown")]
    [InlineData("reiniciar", "system:restart")]
    [InlineData("restart", "system:restart")]
    [InlineData("bloquear", "system:lock")]
    [InlineData("lock", "system:lock")]
    [InlineData("suspender", "system:sleep")]
    [InlineData("sleep", "system:sleep")]
    [InlineData("papelera", "system:recyclebin")]
    public void GetSystemActions_MatchesKnownCommands(string query, string expectedPath)
    {
        var actions = _service.GetSystemActions(query);

        Assert.NotEmpty(actions);
        Assert.Contains(actions, a => a.Path == expectedPath);
    }

    [Fact]
    public void SearchApps_RanksMatchingAppsByScoreAndLaunchCounts()
    {
        var apps = new List<PinnedApp>
        {
            new() { Name = "Google Chrome", Path = "C:\\chrome.exe" },
            new() { Name = "Visual Studio Code", Path = "C:\\code.exe" },
            new() { Name = "Notepad", Path = "C:\\notepad.exe" }
        };

        var counts = new Dictionary<string, int>
        {
            ["C:\\code.exe"] = 20
        };

        var results = _service.SearchApps("code", apps, counts);

        Assert.NotEmpty(results);
        Assert.Equal("Visual Studio Code", results[0].Name);
    }

    [Fact]
    public void SearchApps_MatchesAppByKeywords()
    {
        var apps = new List<PinnedApp>
        {
            new() { Name = "Bluetooth y otros dispositivos", Path = "ms-settings:bluetooth", Keywords = new List<string> { "bluetooth", "raton", "teclado" } },
            new() { Name = "Calculadora", Path = "calc.exe", Keywords = new List<string> { "calc", "calculator" } }
        };

        var results = _service.SearchApps("bluetooth", apps, null);

        Assert.NotEmpty(results);
        Assert.Equal("Bluetooth y otros dispositivos", results[0].Name);
    }

    [Fact]
    public void SearchApps_CommandQuery_OffersExecutionAction()
    {
        var apps = new List<PinnedApp>();

        var results = _service.SearchApps("ping 8.8.8.8", apps, null);

        Assert.NotEmpty(results);
        Assert.StartsWith("Ejecutar:", results[0].Name);
        Assert.Equal("cmd:ping 8.8.8.8", results[0].Path);
    }

    [Fact]
    public async Task SearchMusicAsync_EmptyQuery_ReturnsQuickControls()
    {
        var results = await _service.SearchMusicAsync("");
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Path == "ytmusic:toggle");
        Assert.Contains(results, r => r.Path == "ytmusic:next");
        Assert.Contains(results, r => r.Path == "ytmusic:prev");
        Assert.Contains(results, r => r.Path == "ytmusic:open");
    }

    [Fact]
    public async Task SearchMusicAsync_WithQuery_ReturnsPlayActionAndWebFallback()
    {
        var results = await _service.SearchMusicAsync("coldplay");
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Path.StartsWith("ytmusic:play:") || r.Path.StartsWith("ytmusic:search:"));
        Assert.Contains(results, r => r.Path.Contains("youtube.com"));
    }

    [Fact]
    public async Task SearchMusicAsync_WithArtist_ReturnsVideosWithChannelAndDuration()
    {
        var results = await _service.SearchMusicAsync("gianmarco");
        Assert.NotEmpty(results);
        // Debe incluir direct play, videos extraídos y web fallback
        Assert.True(results.Count > 5);
        var videoItem = results.FirstOrDefault(r => !string.IsNullOrEmpty(r.VideoId));
        Assert.NotNull(videoItem);
        Assert.False(string.IsNullOrWhiteSpace(videoItem!.Name));
        Assert.False(string.IsNullOrWhiteSpace(videoItem.ThumbnailUrl));
    }
}
