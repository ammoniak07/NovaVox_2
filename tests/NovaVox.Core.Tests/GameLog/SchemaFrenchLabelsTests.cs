using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class SchemaFrenchLabelsTests
{
    [Theory]
    [InlineData("Heavy Armor", "Armure lourde")]
    [InlineData("Sniper Rifle", "Fusil de précision")]
    [InlineData("Unknown Thing", "Unknown Thing")]
    public void ItemType_TranslatesKnownTypesAndKeepsOthers(string itemType, string expected) =>
        Assert.Equal(expected, SchemaFrenchLabels.ItemType(itemType));

    [Theory]
    [InlineData("Radiation Scrub Rate", "251.1 REM/s", "Élimination radiations : 251,1 REM/s")]
    [InlineData("Rate Of Fire", "120 rpm", "Cadence : 120 coups/min")]
    [InlineData("Class", "Energy (Laser)", "Classe : Énergie (laser)")]
    [InlineData("Attachments", "Optics (S3), Barrel (S2), Underbarrel (S2)", "Accessoires : Optique (S3), Canon (S2), Sous le canon (S2)")]
    [InlineData("Core Compatibility", "Medium & Heavy", "Compatibilité : armures moyennes et lourdes")]
    public void Stat_TranslatesLabelAndValue(string label, string value, string expected) =>
        Assert.Equal(expected, SchemaFrenchLabels.Stat(new SchemaStat(label, value)));

    [Fact]
    public void Database_PersonalWeapon_HasItemTypeAndStats()
    {
        var info = SchemaDatabase.Find("A03 \"Canuto\" Sniper Rifle")!;

        Assert.Equal("Sniper Rifle", info.ItemType);
        Assert.Contains(info.Stats!, s => s.Label == "Effective Range");
    }

    [Fact]
    public void Recipe_SumsRepeatedIngredientsAndTranslatesCommonOnes()
    {
        // QuikCool : du fer pour la coque (0,16) et pour la pompe (0,10).
        var info = SchemaDatabase.Find("QuikCool")!;
        Assert.Equal("Fer 0,26 SCU · Glace pressurisée 0,16 SCU", SchemaFrenchLabels.Recipe(info.Ingredients));
    }

    [Fact]
    public void Recipe_CountsDiscreteItemsAndIsNullWhenEmpty()
    {
        var recipe = SchemaFrenchLabels.Recipe(SchemaDatabase.Find("M3A Cannon")!.Ingredients);
        Assert.Contains("Hadanite ×7", recipe);
        Assert.StartsWith("Agricium 0,36 SCU", recipe);
        Assert.Null(SchemaFrenchLabels.Recipe(Array.Empty<SchemaIngredient>()));
    }

    [Fact]
    public void Recipe_ShowsMinimumQuality()
    {
        var recipe = SchemaFrenchLabels.Recipe(SchemaDatabase.Find("TH-01 Propulsor")!.Ingredients);
        Assert.Contains("Aluminium", recipe);
        Assert.Contains("(qualité ≥ 600)", recipe);
    }

    [Fact]
    public void RecipeLines_OneLinePerIngredient_WithQualityEffectOrNone()
    {
        var info = SchemaDatabase.Find("Monde Helmet Delta Camo")!;
        var lines = SchemaFrenchLabels.RecipeLines(info.Ingredients, info.QualityEffects)!.Select(l => l.Text);
        Assert.Equal(new[]
        {
            "• Ouratite 0,03 SCU : Réduction des dégâts −3,8 % → +10 %",
            "• Aslarite 0,01 SCU : Températures supportées −8,5 % → +20 %",
            "• Savrilium 0,03 SCU : qualité sans effet",
        }, lines);
    }

    [Fact]
    public void RecipeLines_IngredientUsedInTwoParts_ListsBothEffects()
    {
        var info = SchemaDatabase.Find("QuikCool")!;
        var lines = SchemaFrenchLabels.RecipeLines(info.Ingredients, info.QualityEffects)!.Select(l => l.Text).ToList();
        Assert.Equal("• Fer 0,26 SCU : Intégrité −7 % → +20 % ; Refroidissement −1,8 % → +5 %", lines[0]);
        Assert.Equal("• Glace pressurisée 0,16 SCU : Refroidissement −5,3 % → +15 %", lines[1]);
    }

    [Fact]
    public void QualityEffect_ValueAt_FollowsExactCurve()
    {
        // Linéaire de 0 à 1000 : 125 -> -7,5 %, 520 -> +0,4 %.
        var omnisky = SchemaDatabase.Find("Omnisky III Cannon")!.QualityEffects!;
        var integrity = omnisky.First(e => e.Stat == "Integrity");
        Assert.Equal(0.925, integrity.ValueAt(125), 6);
        Assert.Equal(1.004, integrity.ValueAt(520), 6);
        // Paliers (points d'énergie du LumaCore, Laranite).
        var pips = SchemaDatabase.Find("LumaCore")!.QualityEffects!.First(e => e.Ingredients.Contains("Laranite"));
        Assert.Equal(-2, pips.ValueAt(125));
        Assert.Equal(-1, pips.ValueAt(300));
        Assert.Equal(0, pips.ValueAt(520));
        Assert.Equal(2, pips.ValueAt(950));
        // Effet seulement au-dessus de 500 (températures du torse Aves, Aslarite).
        var temp = SchemaDatabase.Find("Aves Core")!.QualityEffects!.First(e => e.Stat == "Min Temp");
        Assert.Equal(1.0, temp.ValueAt(125), 6);
        Assert.Equal(1.1, temp.ValueAt(750), 6);
    }

    [Fact]
    public void RecipeLines_UseGameQualityBands()
    {
        // Aslarite : 0-399 compte comme 287, 400-599 comme 575... (quantization_aslarite.xml).
        Assert.Equal(287, ResourceQualityBands.Retained("Aslarite", 125));
        Assert.Equal(575, ResourceQualityBands.Retained("Aslarite", 520));
        Assert.Equal(1000, ResourceQualityBands.Retained("Aslarite", 999));
        Assert.NotNull(ResourceQualityBands.For("Pressurized Ice"));
        Assert.NotNull(ResourceQualityBands.For("Yormandi Eye"));

        var info = SchemaDatabase.Find("Monde Helmet Delta Camo")!;
        var lines = SchemaFrenchLabels.RecipeLines(info.Ingredients, info.QualityEffects)!;
        var aslarite = lines[1].ToolTip!;
        Assert.Contains("par tranches", aslarite);
        Assert.Matches(@"0–399\s+287\s+−8,5 %", aslarite);
        Assert.Matches(@"400–599\s+575\s+\+3 %", aslarite);
        Assert.Matches(@"999–1000\s+1000\s+\+20 %", aslarite);
        Assert.Contains("ne change rien", lines[2].ToolTip);

        // Paliers des points d'énergie appliqués à la qualité retenue : la Laranite
        // la plus basse compte comme 298, donc −1 (jamais −2).
        var luma = SchemaDatabase.Find("LumaCore")!;
        var laranite = SchemaFrenchLabels.RecipeLines(luma.Ingredients, luma.QualityEffects)!.First(l => l.Text.Contains("Laranite"));
        Assert.Contains("Points d'énergie −1 → +2", laranite.Text);
        Assert.Matches(@"400–599\s+510\s+0", laranite.ToolTip!);

        // Effet seulement au-dessus de 500 (torse Aves) : la tranche 0-399 (287) ne donne rien.
        var aves = SchemaDatabase.Find("Aves Core")!;
        var avesAslarite = SchemaFrenchLabels.RecipeLines(aves.Ingredients, aves.QualityEffects)!.First(l => l.Text.Contains("Aslarite"));
        Assert.Contains("Températures supportées 0 % → +20 %", avesAslarite.Text);
        Assert.Matches(@"0–399\s+287\s+0 %", avesAslarite.ToolTip!);
    }
}
