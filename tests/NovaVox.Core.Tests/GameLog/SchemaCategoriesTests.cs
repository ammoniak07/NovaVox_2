using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class SchemaCategoriesTests
{
    [Theory]
    [InlineData("A03 \"Canuto\" Sniper Rifle", SchemaCategories.Weapon)]
    [InlineData("A03 Sniper Rifle Magazine (15 cap)", SchemaCategories.Ammo)]
    [InlineData("10-Series Greatsword Cannon", SchemaCategories.ShipWeapon)]
    [InlineData("AbsoluteZero", SchemaCategories.ShipComponent)]
    [InlineData("Ezra", SchemaCategories.ShipComponent)]
    [InlineData("Arbor MH1 Mining Laser", SchemaCategories.Tool)]
    [InlineData("SureGrip HV-S1 Tractor Beam", SchemaCategories.Tool)]
    [InlineData("Bellator Trousers", SchemaCategories.Clothing)]
    [InlineData("Antium Arms", SchemaCategories.Armor)]
    public void Of_KnownSchema_ReturnsExpectedCategory(string name, string expected)
    {
        Assert.Equal(expected, SchemaCategories.Of(SchemaDatabase.Find(name)?.Type));
    }

    [Theory]
    [InlineData("Char_Armor_Helmet", SchemaCategories.Helmet)]
    [InlineData("Char_Armor_Torso", SchemaCategories.Torso)]
    [InlineData("Char_Armor_Arms", SchemaCategories.Arms)]
    [InlineData("Char_Armor_Legs", SchemaCategories.Legs)]
    [InlineData("Char_Armor_Backpack", SchemaCategories.Backpack)]
    [InlineData("Char_Armor_Undersuit", SchemaCategories.Undersuit)]
    [InlineData("Char_Clothing_Torso_0", SchemaCategories.Torso)]
    [InlineData("Char_Clothing_Torso_1", SchemaCategories.Torso)]
    [InlineData("Char_Clothing_Legs", SchemaCategories.Legs)]
    [InlineData("Char_Clothing_Feet", SchemaCategories.Feet)]
    [InlineData("Char_Clothing_Hat", SchemaCategories.Head)]
    [InlineData("Char_Armor_Something", SchemaCategories.OtherPart)]
    [InlineData("WeaponPersonal", null)]
    [InlineData(null, null)]
    public void SubcategoryOf_BodyPart(string? type, string? expected) =>
        Assert.Equal(expected, SchemaCategories.SubcategoryOf(type));

    [Theory]
    [InlineData("Antium Arms", SchemaCategories.Arms)]
    [InlineData("Bellator Trousers", SchemaCategories.Legs)]
    public void SubcategoryOf_KnownSchema(string name, string expected) =>
        Assert.Equal(expected, SchemaCategories.SubcategoryOf(SchemaDatabase.Find(name)?.Type));

    [Fact]
    public void SubcategoryOf_EveryDatabaseArmorAndClothingType_IsListedInFilter()
    {
        foreach (var type in new[] { "Char_Armor_Arms", "Char_Armor_Backpack", "Char_Armor_Helmet", "Char_Armor_Legs", "Char_Armor_Torso", "Char_Armor_Undersuit",
                                     "Char_Clothing_Feet", "Char_Clothing_Legs", "Char_Clothing_Torso_0", "Char_Clothing_Torso_1" })
            Assert.Contains(SchemaCategories.SubcategoryOf(type), SchemaCategories.SubcategoriesByCategory[SchemaCategories.Of(type)]);
    }

    [Fact]
    public void Of_NoType_IsUnknown() => Assert.Equal(SchemaCategories.Unknown, SchemaCategories.Of(null));

    [Fact]
    public void Of_UnmappedType_IsMisc() => Assert.Equal(SchemaCategories.Misc, SchemaCategories.Of("SomethingNew"));
}
