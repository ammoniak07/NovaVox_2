using NovaVox.Core.Combat;

namespace NovaVox.Core.Tests.Combat;

public class WeaponPenetrationTests
{
    private static readonly double[][] StandardShield = { new[] { 0.0, 0.45 }, new[] { 1.0, 1.0 }, new[] { 1.0, 1.0 } };

    private static CombatShip Ship(double physical, double energy, double distortion = 0) =>
        new("Test", "Test Ship", "Test", 1, new[] { physical, energy, distortion }, 1000, new[] { 1.0, 1.0, 1.0 }, 1000, StandardShield, Array.Empty<string>());

    private static CombatWeapon Weapon(double physical, double energy, double distortion = 0, int pellets = 1) =>
        new("Gun", "gun", 3, "Test", "Repeater", new[] { physical, energy, distortion }, pellets, 500, 700, 1200, 2000);

    [Fact]
    public void EnergyAboveDeflection_PenetratesArmorButNotShield()
    {
        var r = WeaponPenetration.Evaluate(Weapon(0, 85.5), Ship(41, 43));
        Assert.True(r.ArmorPenetrated);
        Assert.False(r.LeaksThroughShield);
        Assert.False(r.ShieldAndArmorPenetrated);
    }

    [Fact]
    public void EnergyBelowDeflection_IsDeflected()
    {
        var r = WeaponPenetration.Evaluate(Weapon(0, 85.5), Ship(554, 485));
        Assert.False(r.ArmorPenetrated);
        Assert.True(r.ArmorMargin < 1);
    }

    [Fact]
    public void EqualToDeflection_IsDeflected()
    {
        Assert.False(WeaponPenetration.Evaluate(Weapon(0, 40), Ship(50, 40)).ArmorPenetrated);
    }

    [Fact]
    public void Physical_LeaksThroughFullShield_At55Percent()
    {
        var r = WeaponPenetration.Evaluate(Weapon(100, 0), Ship(50, 50));
        var check = Assert.Single(r.Checks);
        Assert.Equal(0.55, check.ShieldLeak, 3);
        Assert.Equal(55, check.AlphaThroughShield, 3);
        Assert.True(r.ShieldAndArmorPenetrated);
    }

    [Fact]
    public void Physical_LeakBelowDeflection_PassesShieldButBouncesOffArmor()
    {
        var r = WeaponPenetration.Evaluate(Weapon(100, 0), Ship(60, 60));
        Assert.True(r.LeaksThroughShield);
        Assert.False(r.ShieldAndArmorPenetrated);
        Assert.True(r.ArmorPenetrated);
    }

    [Fact]
    public void MixedDamage_PenetratesIfAnyTypePasses()
    {
        var r = WeaponPenetration.Evaluate(Weapon(30, 200), Ship(50, 100));
        Assert.Equal(2, r.Checks.Count);
        Assert.True(r.ArmorPenetrated);
    }

    [Fact]
    public void ZeroDeflection_MarginIsInfinite()
    {
        var r = WeaponPenetration.Evaluate(Weapon(0, 0, 90), Ship(10, 10, 0));
        Assert.True(r.ArmorPenetrated);
        Assert.True(double.IsPositiveInfinity(r.ArmorMargin));
    }

    [Fact]
    public void Scattergun_AlphaTotalCountsPellets()
    {
        Assert.Equal(840, Weapon(105, 0, pellets: 8).AlphaTotal, 3);
    }

    [Fact]
    public void EmbeddedDatabase_LoadsShipsAndWeapons()
    {
        Assert.NotEmpty(CombatDatabase.Ships);
        Assert.NotEmpty(CombatDatabase.Weapons);
        var ndb = CombatDatabase.FindWeapon("NDB-30 Repeater");
        Assert.NotNull(ndb);
        Assert.Equal(85.5, ndb!.Alpha(DamageKind.Energy), 3);
        var idris = CombatDatabase.Ships.First(s => s.Name == "Idris-M");
        Assert.Equal(485, idris.DeflectionOf(DamageKind.Energy));
        Assert.False(WeaponPenetration.Evaluate(ndb, idris).ArmorPenetrated);
        var mis = CombatDatabase.Ships.First(s => s.Name == "Freelancer MIS");
        Assert.True(WeaponPenetration.Evaluate(ndb, mis).ArmorPenetrated);
        Assert.NotEmpty(CombatDatabase.StockWeaponsOf(CombatDatabase.Ships.First(s => s.Name == "Gladius")));
    }
}
