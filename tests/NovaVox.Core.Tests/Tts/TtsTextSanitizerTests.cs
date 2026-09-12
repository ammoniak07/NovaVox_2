using NovaVox.Core.Tts;
using Xunit;

namespace NovaVox.Core.Tests.Tts;

public class TtsTextSanitizerTests
{
    // Références calculées directement avec _strip_markdown_for_speech
    // (app.py) — voir historique de session pour la commande utilisée.
    [Theory]
    [InlineData("# Titre\n**gras** et *italique* et `code`", "Titre\ngras et italique et code")]
    [InlineData("- item1\n- item2\n normal", "item1\nitem2\n normal")]
    [InlineData("un mot_isolé_ pas touché mais _oui_ ceci", "un motisolé pas touché mais oui ceci")]
    public void StripMarkdownForSpeech_MatchesPythonReference(string input, string expected)
    {
        Assert.Equal(expected, TtsTextSanitizer.StripMarkdownForSpeech(input));
    }

    [Fact]
    public void StripMarkdownForSpeech_EmptyStringUnchanged()
    {
        Assert.Equal("", TtsTextSanitizer.StripMarkdownForSpeech(""));
    }
}
