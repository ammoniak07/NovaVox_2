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

/// <summary>
/// Test de cohérence avec un autre outil : valeurs relevées dans le tableur
/// de pénétration de la 1CC (source différente de scunpacked), jeu 4.10.2.
/// Si un patch ou un changement de parsing déplace la valeur du blindage
/// sur un autre champ, ces tests le signalent avant la sortie.
/// </summary>
public class CombatReferenceValuesTests
{
    [Theory]
    [InlineData("Idris-M", 485, 87516)]
    [InlineData("Polaris", 418, 48960)]
    [InlineData("Perseus", 418, 48672)]
    [InlineData("Reclaimer", 418, 32912)]
    [InlineData("890 Jump", 342, 32640)]
    [InlineData("Hammerhead", 380, 25740)]
    [InlineData("Asgard", 88, 23760)]
    [InlineData("Hermes", 110, 15840)]
    [InlineData("Freelancer MIS", 43, 15840)]
    [InlineData("Corsair", 100, 15120)]
    [InlineData("Vanguard Hoplite", 40, 14850)]
    [InlineData("Ares Star Fighter Inferno", 36, 14400)]
    [InlineData("Constellation Mk IV Taurus", 110, 13440)]
    [InlineData("C2 Hercules Starlifter", 300, 13000)]
    [InlineData("Prowler", 39, 12150)]
    [InlineData("Hull C", 360, 12100)]
    [InlineData("Zeus Mk II CL", 40, 11520)]
    [InlineData("MOLE", 40, 11220)]
    [InlineData("Prospector", 19, 8228)]
    [InlineData("Cutlass Black", 29, 7290)]
    [InlineData("Golem", 16, 6732)]
    [InlineData("Gladius", 9, 3300)]
    [InlineData("Arrow", 6, 3300)]
    [InlineData("Talon", 10, 3000)]
    [InlineData("Khartu-al", 9, 2400)]
    [InlineData("Mustang Alpha", 9, 2000)]
    [InlineData("Avenger Stalker", 9, 1232)]
    [InlineData("P-52 Merlin", 4, 360)]
    public void ShipArmor_MatchesOtherTool(string ship, double energyDeflection, double armorHp)
    {
        var s = CombatDatabase.FindShip(ship);
        Assert.NotNull(s);
        Assert.Equal(energyDeflection, s!.DeflectionOf(DamageKind.Energy));
        Assert.Equal(armorHp, s.ArmorHp);
    }

    [Fact]
    public void Ndb30_MatchesOtherTool()
    {
        var w = CombatDatabase.FindWeapon("NDB-30 Repeater")!;
        Assert.Equal(85.5, w.Alpha(DamageKind.Energy), 3);
        Assert.Equal(500, w.Rpm);
        Assert.Equal(1258, w.Speed);
        Assert.Equal(712.5, w.Dps, 1);
    }

    /// <summary>
    /// Dégâts par tir relevés sur CStone (finder.cstone.space), autre source
    /// que scunpacked. Vérifie aussi que l'arme « normale » est retenue et
    /// pas une variante de PNJ ou de l'exosquelette ATLS du même nom.
    /// </summary>
    [Theory]
    [InlineData("Tarantula GT-870 Mark 3 Cannon", 3, 136.62)]
    [InlineData("CF-117 Bulldog Repeater", 1, 17.496)]
    [InlineData("PyroBurst Scattergun", 3, 396)]
    [InlineData("GVSR Repeater", 2, 99.72)]
    [InlineData("Revenant Gatling", 4, 63.27)]
    [InlineData("CF-337 Panther Repeater", 3, 43.7)]
    [InlineData("NDB-30 Repeater", 3, 85.5)]
    public void WeaponAlpha_MatchesCStone(string name, int size, double alpha)
    {
        var w = CombatDatabase.FindWeapon(name);
        Assert.NotNull(w);
        Assert.Equal(size, w!.Size);
        Assert.InRange(w.AlphaTotal, alpha * 0.99, alpha * 1.01);
    }

    [Fact]
    public void Database_HasNoSuspiciousValues()
    {
        Assert.True(CombatDatabase.Ships.Count > 150);
        Assert.All(CombatDatabase.Ships, s =>
        {
            Assert.True(s.ArmorHp > 0, s.Name);
            Assert.Equal(3, s.Deflection.Length);
            Assert.All(s.Deflection, d => Assert.InRange(d, 0, 5000));
        });
        Assert.All(CombatDatabase.Weapons, w => Assert.True(w.AlphaTotal > 0, w.Name));
        Assert.All(CombatDatabase.Weapons.Where(w => w.IsGun), w => Assert.True(w.Rpm > 0 && w.Speed > 0, w.Name));
    }
}
