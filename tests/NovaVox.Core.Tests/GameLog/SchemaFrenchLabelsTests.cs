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
    public void QualityEffects_ShowRangeFromWorstToBestQuality()
    {
        var omnisky = SchemaFrenchLabels.QualityEffects(SchemaDatabase.Find("Omnisky III Cannon")!.QualityEffects);
        Assert.Equal("Intégrité −10 % → +10 % (Agricium) · Force d'impact −5 % → +5 % (Hadanite, Dolivine)", omnisky);
        // Points d'énergie : ajoutés, pas multipliés.
        var lumaCore = SchemaFrenchLabels.QualityEffects(SchemaDatabase.Find("LumaCore")!.QualityEffects)!;
        Assert.Contains("Points d'énergie −2 → +2 (Laranite)", lumaCore);
        Assert.Null(SchemaFrenchLabels.QualityEffects(null));
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
            "• Ouratite 0,03 SCU : Réduction des dégâts −10 % → +10 %",
            "• Aslarite 0,01 SCU : Températures supportées −20 % → +20 %",
            "• Savrilium 0,03 SCU : qualité sans effet",
        }, lines);
    }

    [Fact]
    public void RecipeLines_IngredientUsedInTwoParts_ListsBothEffects()
    {
        var info = SchemaDatabase.Find("QuikCool")!;
        var lines = SchemaFrenchLabels.RecipeLines(info.Ingredients, info.QualityEffects)!.Select(l => l.Text).ToList();
        Assert.Equal("• Fer 0,26 SCU : Intégrité −20 % → +20 % ; Refroidissement −5 % → +5 %", lines[0]);
        Assert.Equal("• Glace pressurisée 0,16 SCU : Refroidissement −15 % → +15 %", lines[1]);
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
    public void RecipeLines_ToolTipShowsQualityTable()
    {
        var info = SchemaDatabase.Find("Monde Helmet Delta Camo")!;
        var lines = SchemaFrenchLabels.RecipeLines(info.Ingredients, info.QualityEffects)!;
        var aslarite = lines[1].ToolTip!;
        Assert.Contains("Températures supportées", aslarite);
        Assert.Contains("  100   −16 %", aslarite);
        Assert.Contains("1000   +20 %", aslarite);
        Assert.Contains("ne change rien", lines[2].ToolTip);

        var aves = SchemaDatabase.Find("Aves Core")!;
        var avesLines = SchemaFrenchLabels.RecipeLines(aves.Ingredients, aves.QualityEffects)!;
        Assert.Contains(avesLines, l => l.Text.Contains("(à partir de 500)") && l.ToolTip!.Contains("aucun effet en dessous de 500"));

        var luma = SchemaDatabase.Find("LumaCore")!;
        var laranite = SchemaFrenchLabels.RecipeLines(luma.Ingredients, luma.QualityEffects)!.First(l => l.Text.Contains("Laranite"));
        Assert.DoesNotContain("à partir de", laranite.Text);
        Assert.Contains("par paliers : 0–249 : −2 · 250–499 : −1 · 500–699 : 0 · 700–899 : +1 · 900–1000 : +2", laranite.ToolTip);
    }
}
