using NovaVox.Core.Tts;
using Xunit;

namespace NovaVox.Core.Tests.Tts;

public class FrenchNumberExpanderTests
{
    // Références calculées directement avec _integer_to_french_words (app.py).
    [Theory]
    [InlineData(0, "zéro")]
    [InlineData(21, "vingt et un")]
    [InlineData(70, "soixante-dix")]
    [InlineData(71, "soixante et onze")]
    [InlineData(80, "quatre-vingts")]
    [InlineData(81, "quatre-vingt-un")]
    [InlineData(90, "quatre-vingt-dix")]
    [InlineData(91, "quatre-vingt-onze")]
    [InlineData(100, "cent")]
    [InlineData(180, "cent quatre-vingts")]
    [InlineData(200, "deux cents")]
    [InlineData(380, "trois cent quatre-vingts")]
    [InlineData(1000, "mille")]
    [InlineData(1001, "mille un")]
    [InlineData(1980, "mille neuf cent quatre-vingts")]
    [InlineData(40000, "quarante mille")]
    [InlineData(100000, "cent mille")]
    [InlineData(200000, "deux cent mille")]
    [InlineData(123456, "cent vingt-trois mille quatre cent cinquante-six")]
    [InlineData(1000000, "un million")]
    [InlineData(2000000, "deux millions")]
    [InlineData(2000500, "deux millions cinq cents")]
    [InlineData(1000000000, "un milliard")]
    [InlineData(-5, "moins cinq")]
    public void IntegerToWords_MatchesPythonReference(long n, string expected)
    {
        Assert.Equal(expected, FrenchNumberExpander.IntegerToWords(n));
    }

    // Références calculées directement avec _integer_to_french_words(n, "belgique") (app.py).
    [Theory]
    [InlineData(70, "septante")]
    [InlineData(71, "septante et un")]
    [InlineData(72, "septante-deux")]
    [InlineData(79, "septante-neuf")]
    [InlineData(80, "quatre-vingts")]
    [InlineData(81, "quatre-vingt-un")]
    [InlineData(90, "nonante")]
    [InlineData(91, "nonante et un")]
    [InlineData(99, "nonante-neuf")]
    [InlineData(171, "cent septante et un")]
    [InlineData(191, "cent nonante et un")]
    [InlineData(70000, "septante mille")]
    [InlineData(90000, "nonante mille")]
    [InlineData(1980, "mille neuf cent quatre-vingts")]
    public void IntegerToWords_BelgiqueStyle_UsesSeptanteNonante(long n, string expected)
    {
        Assert.Equal(expected, FrenchNumberExpander.IntegerToWords(n, FrenchNumberStyle.Belgique));
    }

    [Fact]
    public void Expand_BelgiqueStyle_IsUsedWhenPassedThrough()
    {
        var result = FrenchNumberExpander.Expand("Amende de 90 000 aUEC.", FrenchNumberStyle.Belgique);
        Assert.Equal("Amende de nonante mille aUEC.", result);
    }

    [Theory]
    [InlineData("france")]
    [InlineData("belgique")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseStyle_OnlyBelgiqueMapsToBelgique(string? value)
    {
        var expected = value == "belgique" ? FrenchNumberStyle.Belgique : FrenchNumberStyle.France;
        Assert.Equal(expected, FrenchNumberExpander.ParseStyle(value));
    }

    [Fact]
    public void Expand_PlainLargeNumber_IsSpokenAsWordsNotDigitByDigit()
    {
        var result = FrenchNumberExpander.Expand("Don de 40000 aUEC à PlayerX pour une amende de 1000000 aUEC.");
        Assert.Equal("Don de quarante mille aUEC à PlayerX pour une amende de un million aUEC.", result);
    }

    [Fact]
    public void Expand_SpaceGroupedThousands_IsTreatedAsOneNumber()
    {
        Assert.Equal("Amende de quarante mille aUEC.", FrenchNumberExpander.Expand("Amende de 40 000 aUEC."));
        Assert.Equal("Amende de quarante mille aUEC.", FrenchNumberExpander.Expand("Amende de 40 000 aUEC."));
    }

    [Fact]
    public void Expand_LeadingZeroNumber_IsLeftUntouched()
    {
        Assert.Equal("Code 007", FrenchNumberExpander.Expand("Code 007"));
    }

    [Fact]
    public void Expand_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Equal("", FrenchNumberExpander.Expand(""));
        Assert.Equal("", FrenchNumberExpander.Expand(null));
    }
}
