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
}
