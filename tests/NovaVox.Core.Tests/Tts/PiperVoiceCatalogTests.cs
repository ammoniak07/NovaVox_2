using NovaVox.Core.Tts;
using Xunit;

namespace NovaVox.Core.Tests.Tts;

public class PiperVoiceCatalogTests
{
    [Fact]
    public void Voices_HasExpectedCountAndUniqueIds()
    {
        Assert.Equal(20, PiperVoiceCatalog.Voices.Count);
        Assert.Equal(20, PiperVoiceCatalog.Voices.Select(v => v.Id).Distinct().Count());
    }

    [Fact]
    public void Voices_FrenchCountMatchesPython()
    {
        Assert.Equal(6, PiperVoiceCatalog.Voices.Count(v => v.Lang == "fr"));
    }

    [Fact]
    public void Find_KnownId_ReturnsMatchingUrlBase()
    {
        var voice = PiperVoiceCatalog.Find("fr_FR-siwis-medium");
        Assert.NotNull(voice);
        Assert.Equal("https://huggingface.co/rhasspy/piper-voices/resolve/main/fr/fr_FR/siwis/medium/fr_FR-siwis-medium", voice!.UrlBase);
    }

    [Fact]
    public void Find_UnknownId_ReturnsNull()
    {
        Assert.Null(PiperVoiceCatalog.Find("does-not-exist"));
    }
}
