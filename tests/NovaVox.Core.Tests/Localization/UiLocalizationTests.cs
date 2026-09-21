using NovaVox.Core.Localization;
using Xunit;

namespace NovaVox.Core.Tests.Localization;

public class UiLocalizationTests
{
    [Fact]
    public void AllLanguages_HaveTheExactSameKeySet()
    {
        // Une clé présente dans une langue mais oubliée dans une autre
        // retombe silencieusement sur le français (voir T()) — utile pour
        // ne jamais planter l'affichage, mais ça peut aussi masquer un
        // oubli de traduction ou (comme vérifié une fois) une clé ajoutée
        // deux fois à la même langue avec un nom différent. Ce test rend
        // tout écart visible immédiatement plutôt que de compter sur une
        // relecture manuelle des 6 blocs.
        var referenceKeys = UiLocalization.Strings[UiLocalization.DefaultLanguage].Keys.OrderBy(k => k).ToList();

        foreach (var (lang, dict) in UiLocalization.Strings)
        {
            var keys = dict.Keys.OrderBy(k => k).ToList();
            Assert.True(referenceKeys.SequenceEqual(keys),
                $"La langue '{lang}' n'a pas le même jeu de clés que '{UiLocalization.DefaultLanguage}'. " +
                $"Manquantes : [{string.Join(", ", referenceKeys.Except(keys))}]. " +
                $"En trop : [{string.Join(", ", keys.Except(referenceKeys))}].");
        }
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    [InlineData("nl")]
    [InlineData("es")]
    [InlineData("it")]
    [InlineData("de")]
    public void T_KnownKey_ReturnsThatLanguagesValue(string lang)
    {
        Assert.Equal(UiLocalization.Strings[lang]["settings.sons.mic.label"], UiLocalization.T(lang, "settings.sons.mic.label"));
    }

    [Fact]
    public void T_UnknownLanguage_FallsBackToFrench()
    {
        Assert.Equal(UiLocalization.Strings["fr"]["settings.sons.mic.label"], UiLocalization.T("xx", "settings.sons.mic.label"));
    }

    [Fact]
    public void T_UnknownKey_ReturnsKeyItself()
    {
        Assert.Equal("does.not.exist", UiLocalization.T("en", "does.not.exist"));
    }
}
