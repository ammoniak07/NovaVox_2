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

    [Fact]
    public void Of_NoType_IsUnknown() => Assert.Equal(SchemaCategories.Unknown, SchemaCategories.Of(null));

    [Fact]
    public void Of_UnmappedType_IsMisc() => Assert.Equal(SchemaCategories.Misc, SchemaCategories.Of("SomethingNew"));
}
