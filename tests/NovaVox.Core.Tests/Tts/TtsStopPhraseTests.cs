using NovaVox.Core.Tts;
using Xunit;

namespace NovaVox.Core.Tests.Tts;

public class TtsStopPhraseTests
{
    [Theory]
    [InlineData("gemini stop", "Gemini", true)]
    [InlineData("gemini tais-toi", "Gemini", true)]
    [InlineData("stop", "Gemini", false)] // pas le nom de l'IA : jamais interprété comme un arrêt
    [InlineData("gemini raconte une histoire", "Gemini", false)]
    public void IsStopPhrase_RequiresWakeWordAndKeyword(string textNorm, string geminiName, bool expected)
    {
        Assert.Equal(expected, TtsStopPhrase.IsStopPhrase(textNorm, geminiName));
    }
}
