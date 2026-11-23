using WallFlow.Spotlight.Services;
using Xunit;

namespace WallFlow.Tests;

public class GeminiSpotlightServiceTests
{
    private readonly GeminiSpotlightService _service = new();

    [Theory]
    [InlineData("Response: Hola mundo", "Hola mundo")]
    [InlineData("Answer: 42", "42")]
    [InlineData("Output: Este es el resultado", "Este es el resultado")]
    [InlineData("\"Texto entre comillas\"", "Texto entre comillas")]
    [InlineData("   Respuesta limpia   ", "Respuesta limpia")]
    public void CleanAiResponse_StripsLabelsAndQuotes(string input, string expected)
    {
        string cleaned = _service.CleanAiResponse(input);
        Assert.Equal(expected, cleaned);
    }
}
