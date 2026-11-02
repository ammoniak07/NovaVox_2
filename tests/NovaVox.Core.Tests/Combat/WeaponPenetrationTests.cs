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

public class MyShipsTests
{
    [Fact]
    public void Mounts_AreLoadedWithStockWeapons()
    {
        var cutlass = CombatDatabase.FindShip("Cutlass Black")!;
        Assert.Contains(cutlass.MountList, m => m.IsGun && m.MaxSize == 3 && m.Stock == "CF-337 Panther Repeater");
        Assert.Contains(cutlass.MountList, m => !m.IsGun && m.Count == 4);
    }

    [Fact]
    public void Mount_AcceptsOnlyRightKindAndSize()
    {
        var mount = new CombatMount("x", "gun nose", "gun", 3, 3, 1, null, true);
        Assert.All(CombatDatabase.WeaponsFor(mount), w => Assert.True(w.IsGun && w.Size == 3));
        Assert.Contains(CombatDatabase.WeaponsFor(mount), w => w.Name == "NDB-30 Repeater");
        var rack = new CombatMount("y", "missile rack", "missile", 3, 3, 2, null, true);
        Assert.All(CombatDatabase.WeaponsFor(rack), w => Assert.False(w.IsGun));
    }

    [Fact]
    public void EquippedWeapons_UsesChoicesThenStock_AndCountsMissiles()
    {
        var cutlass = CombatDatabase.FindShip("Cutlass Black")!;
        var loadout = new MyShipLoadout { Name = "Mon Cutlass", Ship = cutlass.Name };
        foreach (var m in cutlass.MountList.Where(m => m.IsGun)) loadout.Weapons[m.Id] = "NDB-30 Repeater";
        var equipped = loadout.EquippedWeapons(cutlass);
        Assert.Equal(6, equipped.Single(e => e.Weapon.Name == "NDB-30 Repeater").Count);
        Assert.DoesNotContain(equipped, e => e.Weapon.Name == "CF-337 Panther Repeater");
        Assert.Equal(8, equipped.Single(e => e.Weapon.Name == "Arrester III Missile").Count);
    }

    [Fact]
    public void Store_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nv_myships_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new MyShipsStore(dir);
            Assert.Empty(store.Load());
            store.Save(new[] { new MyShipLoadout { Name = "Chasseur « PvP »", Ship = "Gladius", Weapons = { ["loadout.53.loadout.0"] = "NDB-30 Repeater" } } });
            var loaded = Assert.Single(store.Load());
            Assert.Equal("Chasseur « PvP »", loaded.Name);
            Assert.Equal("NDB-30 Repeater", loaded.Weapons["loadout.53.loadout.0"]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Labels_AreTranslated()
    {
        Assert.Equal("Tourelle dessus › arme gauche haut", CombatLabels.French("turret top › weapon left upper"));
        Assert.Equal("Lance-missiles gauche", CombatLabels.French("missile rack left"));
    }
}
