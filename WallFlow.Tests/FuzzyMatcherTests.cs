using WallFlow.Search;
using Xunit;

namespace WallFlow.Tests;

public class FuzzyMatcherTests
{
    [Fact]
    public void CalculateScore_ExactMatch_ReturnsMaxScore()
    {
        int score = FuzzyMatcher.CalculateScore("Spotify", "Spotify");
        Assert.True(score >= 150);
    }

    [Fact]
    public void CalculateScore_PrefixMatch_ReturnsHighScore()
    {
        int score = FuzzyMatcher.CalculateScore("Calculadora", "calc");
        Assert.True(score >= 100);
    }

    [Fact]
    public void CalculateScore_AliasCalc_MatchesCalculadora()
    {
        int score = FuzzyMatcher.CalculateScore("Calculadora", "calc");
        Assert.True(score >= 100);
    }

    [Fact]
    public void CalculateScore_AliasCmd_MatchesSimboloDelSistema()
    {
        int score = FuzzyMatcher.CalculateScore("Símbolo del sistema", "cmd");
        Assert.True(score >= 90);
    }

    [Fact]
    public void CalculateScore_AliasTaskmgr_MatchesAdministradorDeTareas()
    {
        int score = FuzzyMatcher.CalculateScore("Administrador de tareas", "taskmgr");
        Assert.True(score >= 90);
    }

    [Fact]
    public void CalculateScore_InitialsVsc_MatchesVisualStudioCode()
    {
        int score = FuzzyMatcher.CalculateScore("Visual Studio Code", "vsc");
        Assert.True(score >= 90);
    }

    [Fact]
    public void CalculateScore_TypoTolerance_ChrimeMatchesGoogleChrome()
    {
        int score = FuzzyMatcher.CalculateScore("Google Chrome", "chrime");
        Assert.True(score > 30);
    }

    [Fact]
    public void CalculateScore_TypoTolerance_SpotifiMatchesSpotify()
    {
        int score = FuzzyMatcher.CalculateScore("Spotify", "spotifi");
        Assert.True(score > 30);
    }

    [Fact]
    public void CalculateScore_DiacriticsNormalization_MatchesAccentedText()
    {
        int score = FuzzyMatcher.CalculateScore("Configuración de pantalla", "configuracion");
        Assert.True(score >= 90);
    }

    [Fact]
    public void CalculateScore_UnrelatedQuery_ReturnsZero()
    {
        int score = FuzzyMatcher.CalculateScore("Calculadora", "photoshop");
        Assert.Equal(0, score);
    }

    [Fact]
    public void RemoveDiacritics_StripsAccentsCorrectly()
    {
        string normalized = FuzzyMatcher.RemoveDiacritics("áéíóúÁÉÍÓÚñÑ");
        Assert.Equal("aeiouAEIOUnN", normalized);
    }

    [Fact]
    public void LevenshteinDistance_CalculatesAccurateDistance()
    {
        Assert.Equal(0, FuzzyMatcher.LevenshteinDistance("test", "test"));
        Assert.Equal(1, FuzzyMatcher.LevenshteinDistance("test", "tent"));
        Assert.Equal(1, FuzzyMatcher.LevenshteinDistance("test", "tests"));
        Assert.Equal(3, FuzzyMatcher.LevenshteinDistance("kitten", "sitting"));
    }

    [Fact]
    public void CalculateScore_WhatsApTypo_MatchesWhatsApp()
    {
        int scoreWithOneP = FuzzyMatcher.CalculateScore("WhatsApp", "whatsap");
        Assert.True(scoreWithOneP > 30, $"whatsap score was {scoreWithOneP}");

        int scoreFull = FuzzyMatcher.CalculateScore("WhatsApp", "whatsapp");
        Assert.True(scoreFull >= 100, $"whatsapp score was {scoreFull}");
    }

    [Fact]
    public void CalculateScore_WithKeywords_MatchesKeywordDirectly()
    {
        var keywords = new[] { "wifi", "ethernet", "internet" };
        int score = FuzzyMatcher.CalculateScore("Red e Internet", "wifi", keywords: keywords);
        Assert.True(score >= 100, $"wifi keyword score was {score}");
    }

    [Fact]
    public void CalculateScore_SystemToolAliases_MatchesServicesAndDiskManagement()
    {
        int scoreServices = FuzzyMatcher.CalculateScore("Servicios de Windows", "services");
        Assert.True(scoreServices >= 90, $"services score was {scoreServices}");

        int scoreDisks = FuzzyMatcher.CalculateScore("Administración de discos", "diskmgmt");
        Assert.True(scoreDisks >= 90, $"diskmgmt score was {scoreDisks}");
    }

    [Fact]
    public void CalculateScore_BilingualAliases_MatchesBrowserAndMusic()
    {
        int scoreBrowser = FuzzyMatcher.CalculateScore("Google Chrome", "navegador");
        Assert.True(scoreBrowser >= 90, $"navegador score was {scoreBrowser}");

        int scoreMusic = FuzzyMatcher.CalculateScore("Spotify", "musica");
        Assert.True(scoreMusic >= 90, $"musica score was {scoreMusic}");
    }
}
