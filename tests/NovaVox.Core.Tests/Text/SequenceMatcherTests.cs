using NovaVox.Core.Text;
using Xunit;

namespace NovaVox.Core.Tests.Text;

public class SequenceMatcherTests
{
    // Valeurs de référence calculées avec difflib.SequenceMatcher (CPython)
    // pour garantir la parité de comportement avec app.py.
    [Theory]
    [InlineData("train d'atterrissage", "train d'atterrissage", 1.0)]
    [InlineData("train d atterrissage", "train d'atterisage", 0.8947368421052632)]
    [InlineData("scanner", "je scanne", 0.75)]
    [InlineData("", "", 1.0)]
    [InlineData("abc", "", 0.0)]
    public void Ratio_MatchesPythonDifflib(string a, string b, double expected)
    {
        var ratio = new SequenceMatcher(a, b).Ratio();
        Assert.Equal(expected, ratio, precision: 9);
    }
}
