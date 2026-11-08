using NovaVox.Core.Targeting;

namespace NovaVox.Core.Tests.Targeting;

public class ShipNameMatcherTests
{
    [Theory]
    [InlineData("DRAKE CUTLASS BLACK", "Cutlass Black")]
    [InlineData("Cutlass Black", "Cutlass Black")]
    [InlineData("AEGIS IDRIS-M", "Idris-M")]
    [InlineData("ANVIL F7C-M SUPER HORNET MK II", "F7C-M Super Hornet Mk II")]
    [InlineData("AEGIS GLADIUS PIRATE", "Gladius Pirate")]
    [InlineData("MISC FREELANCER MIS", "Freelancer MIS")]
    [InlineData("RSI P0LARIS", "Polaris")]           // 0 lu à la place de O
    [InlineData("AEG1S HAMMERHEAD", "Hammerhead")]    // 1 lu à la place de I
    [InlineData("HAMMERHFAD", "Hammerhead")]          // une lettre mal lue
    [InlineData("C2 HERCULES STARLIFTER", "C2 Hercules Starlifter")]
    public void Recognizes(string ocr, string expected)
    {
        var match = ShipNameMatcher.Match(ocr);
        Assert.NotNull(match);
        Assert.Equal(expected, match!.Ship.Name);
    }

    [Fact]
    public void PrefersTheLongestName()
    {
        Assert.Equal("Gladius Pirate", ShipNameMatcher.Match("Gladius Pirate")!.Ship.Name);
        Assert.Equal("Gladius", ShipNameMatcher.Match("AEGIS GLADIUS")!.Ship.Name);
    }

    [Fact]
    public void NameSplitOnTwoLines()
    {
        var match = ShipNameMatcher.Match(new[] { "ANVIL", "CARRACK", "1 250 m" });
        Assert.Equal("Carrack", match!.Ship.Name);
    }

    [Fact]
    public void IgnoresUnrelatedText()
    {
        Assert.Null(ShipNameMatcher.Match("SHIELDS 100% POWER"));
        Assert.Null(ShipNameMatcher.Match("1 250 m"));
        Assert.Null(ShipNameMatcher.Match(""));
    }

    [Fact]
    public void Canonical_TreatsConfusableCharactersAlike()
    {
        Assert.Equal(ShipNameMatcher.Canonical("P0LARIS"), ShipNameMatcher.Canonical("POLARIS"));
        Assert.Equal(ShipNameMatcher.Canonical("F7C-M"), ShipNameMatcher.Canonical("F7C M"));
    }
}

public class ShipNameMatcherTargetPanelTests
{
    // Texte tel que l'OCR le lit dans le panneau de cible du HUD (captures en jeu, HUD en français).
    [Fact]
    public void TakesTheLineAboveFaction()
    {
        var lines = new[] { "7.0K 52.7K 24.7K", "208.9 0.0 0.0", "CRUSADER C1 SPIRIT", "FACTION ▸ INCONNU", "VITESSE ▸ 0M/S (7.77KM)" };
        Assert.Equal("C1 Spirit", ShipNameMatcher.MatchTargetPanel(lines)?.Ship.Name);
    }

    [Fact]
    public void ToleratesOcrMistakes()
    {
        var lines = new[] { "289.0", "0RIGIN 125A", "FACTI0N > UEE NAVY", "VITESSE > 0M/S (9.88KM)" };
        Assert.Equal("125a", ShipNameMatcher.MatchTargetPanel(lines)?.Ship.Name);
    }

    [Fact]
    public void IgnoresShipNamesElsewhereInTheZone()
    {
        // Un autre nom de vaisseau ailleurs dans la grande zone ne doit pas l'emporter sur la ligne au-dessus de FACTION.
        var lines = new[] { "DRAKE CUTLASS BLACK", "12.1K", "ANVIL ARROW", "FACTION ▸ INCONNU", "VITESSE ▸ 120M/S" };
        Assert.Equal("Arrow", ShipNameMatcher.MatchTargetPanel(lines)?.Ship.Name);
    }

    [Fact]
    public void NoNameAboveFaction_ReturnsNull()
    {
        Assert.Null(ShipNameMatcher.MatchTargetPanel(new[] { "7.0K 52.7K", "FACTION ▸ INCONNU" }));
        Assert.Null(ShipNameMatcher.MatchTargetPanel(new[] { "QUANTUM", "FACTION ▸ INCONNU" }));
    }

    [Fact]
    public void WithoutFactionLine_SearchesTheWholeZone()
    {
        Assert.Equal("C1 Spirit", ShipNameMatcher.MatchTargetPanel(new[] { "7.0K", "CRUSADER C1 SPIRIT" })?.Ship.Name);
    }

    [Theory]
    [InlineData("FACTION ▸ INCONNU", true)]
    [InlineData("FACT1ON > UEE NAVY", true)]
    [InlineData("VITESSE ▸ 0M/S", true)]
    [InlineData("CRUSADER C1 SPIRIT", false)]
    [InlineData("ORIGIN 125A", false)]
    public void RecognizesInfoLines(string line, bool expected) => Assert.Equal(expected, ShipNameMatcher.IsInfoLine(line));
}

public class ShipNameMatcherSpeedTests
{
    [Fact]
    public void IsFastEnoughForContinuousScanning()
    {
        var lines = new[] { "TARGET LOCKED", "UNKNOWN CONTACT", "1 250 m", "SHIELDS 100% POWER 87%", "HULL 54%" };
        ShipNameMatcher.Match(lines); // préchauffage
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 5; i++) ShipNameMatcher.Match(lines);
        Assert.True(sw.ElapsedMilliseconds / 5 < 150, $"{sw.ElapsedMilliseconds / 5} ms par lecture");
    }
}
