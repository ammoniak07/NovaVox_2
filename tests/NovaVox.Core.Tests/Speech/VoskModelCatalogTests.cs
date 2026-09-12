using NovaVox.Core.Speech;
using Xunit;

namespace NovaVox.Core.Tests.Speech;

public class VoskModelCatalogTests
{
    [Fact]
    public void Models_HasTwoPerSupportedLanguage()
    {
        Assert.Equal(12, VoskModelCatalog.Models.Count);
        Assert.Equal(12, VoskModelCatalog.Models.Select(m => m.Id).Distinct().Count());
    }

    [Fact]
    public void ModelsForLanguage_French_ReturnsSmallThenFullInOrder()
    {
        var models = VoskModelCatalog.ModelsForLanguage("fr");
        Assert.Equal(new[] { "vosk-model-small-fr-0.22", "vosk-model-fr-0.22" }, models.Select(m => m.Id));
    }

    [Fact]
    public void ModelsForLanguage_UnknownLanguage_FallsBackToFrench()
    {
        var models = VoskModelCatalog.ModelsForLanguage("xx");
        Assert.Equal(VoskModelCatalog.ModelsForLanguage("fr").Select(m => m.Id), models.Select(m => m.Id));
    }

    [Theory]
    [InlineData("vosk-model-small-fr-0.22", "https://alphacephei.com/vosk/models/vosk-model-small-fr-0.22.zip")]
    [InlineData("vosk-model-en-us-0.22", "https://alphacephei.com/vosk/models/vosk-model-en-us-0.22.zip")]
    public void Models_UrlsMatchAlphacephei(string id, string expectedUrl)
    {
        var model = VoskModelCatalog.Models.Single(m => m.Id == id);
        Assert.Equal(expectedUrl, model.Url);
    }
}
