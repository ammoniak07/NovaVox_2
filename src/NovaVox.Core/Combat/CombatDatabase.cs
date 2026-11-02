using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaVox.Core.Combat;

/// <summary>Types de dégâts pris en compte, dans l'ordre des tableaux de CombatData.json.</summary>
public enum DamageKind
{
    Physical = 0,
    Energy = 1,
    Distortion = 2,
}

/// <summary>Arme de vaisseau (canon, missile ou torpille) — une entrée de CombatData.json.</summary>
public sealed record CombatWeapon(
    [property: JsonPropertyName("name")] string Name,
    /// <summary>"gun", "missile" ou "torpedo".</summary>
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("size")] int? Size,
    [property: JsonPropertyName("manufacturer")] string? Manufacturer,
    [property: JsonPropertyName("itemType")] string? ItemType,
    /// <summary>Dégâts d'UN projectile (une seule bille pour un fusil à pompe), par type : physique, énergie, distorsion.</summary>
    [property: JsonPropertyName("alphaPerProjectile")] double[] AlphaPerProjectile,
    [property: JsonPropertyName("pellets")] int Pellets,
    [property: JsonPropertyName("rpm")] double Rpm,
    [property: JsonPropertyName("dps")] double Dps,
    [property: JsonPropertyName("speed")] double Speed,
    [property: JsonPropertyName("range")] double Range)
{
    public double Alpha(DamageKind kind) => (int)kind < AlphaPerProjectile.Length ? AlphaPerProjectile[(int)kind] : 0;

    /// <summary>Dégâts d'un tir complet (toutes les billes), tous types confondus.</summary>
    public double AlphaTotal => AlphaPerProjectile.Sum() * Math.Max(1, Pellets);

    public bool IsGun => Kind == "gun";
}

/// <summary>
/// Emplacement d'arme d'un vaisseau : un canon (Kind "gun") ou un
/// lance-missiles entier (Kind "missile", Count missiles ou torpilles
/// identiques). Id = identifiant stable du port dans l'équipement du jeu.
/// </summary>
public sealed record CombatMount(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("minSize")] int MinSize,
    [property: JsonPropertyName("maxSize")] int MaxSize,
    [property: JsonPropertyName("count")] int Count,
    /// <summary>Arme montée d'origine (null si l'emplacement est vide).</summary>
    [property: JsonPropertyName("stock")] string? Stock,
    /// <summary>Faux pour les armes que le jeu ne laisse pas changer (ex. défense rapprochée).</summary>
    [property: JsonPropertyName("editable")] bool Editable)
{
    public bool IsGun => Kind == "gun";

    /// <summary>Emplacement de générateur de bouclier.</summary>
    public bool IsShield => Kind == "shield";

    private bool SizeOk(int size) =>
        size >= (MinSize > 0 ? MinSize : 0) && size <= (MaxSize > 0 ? MaxSize : int.MaxValue);

    /// <summary>Ce générateur de bouclier peut-il aller sur cet emplacement ?</summary>
    public bool Accepts(CombatShield shield) => IsShield && SizeOk(shield.Size);

    /// <summary>L'arme peut-elle aller sur cet emplacement (bon type, taille entre min et max) ?</summary>
    public bool Accepts(CombatWeapon weapon)
    {
        if (IsShield) return false;
        var typeOk = IsGun ? weapon.IsGun : !weapon.IsGun;
        var size = weapon.Size ?? 0;
        var min = MinSize > 0 ? MinSize : 0;
        var max = MaxSize > 0 ? MaxSize : int.MaxValue;
        return typeOk && size >= min && size <= max;
    }

    /// <summary>Nom lisible en français (« turret top › weapon left » -> « tourelle dessus › arme gauche »).</summary>
    public string FrenchLabel => CombatLabels.French(Label);
}

/// <summary>Vaisseau attaquable — blindage et bouclier, une entrée de CombatData.json.</summary>
public sealed record CombatShip(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("manufacturer")] string? Manufacturer,
    [property: JsonPropertyName("size")] int? Size,
    /// <summary>Seuil de déflexion du blindage par type : un projectile qui ne le dépasse pas ricoche sans dégâts.</summary>
    [property: JsonPropertyName("deflection")] double[] Deflection,
    [property: JsonPropertyName("armorHp")] double ArmorHp,
    [property: JsonPropertyName("armorDamageMultiplier")] double[] ArmorDamageMultiplier,
    [property: JsonPropertyName("shieldHp")] double ShieldHp,
    /// <summary>Absorption du bouclier par type, [min, max] : max quand il est plein, min quand il est presque vide.</summary>
    [property: JsonPropertyName("shieldAbsorption")] double[][] ShieldAbsorption,
    [property: JsonPropertyName("stockWeapons")] IReadOnlyList<string> StockWeapons,
    /// <summary>Emplacements d'armes (canons et lance-missiles), pour équiper « Mes vaisseaux ».</summary>
    [property: JsonPropertyName("mounts")] IReadOnlyList<CombatMount>? Mounts = null,
    /// <summary>Régénération totale du bouclier d'origine (PV/s), quand il ne prend plus de coups.</summary>
    [property: JsonPropertyName("shieldRegen")] double ShieldRegen = 0,
    /// <summary>Nombre de générateurs de bouclier qui fonctionnent en même temps (2 dans le jeu actuel).</summary>
    [property: JsonPropertyName("activeShields")] int ActiveShields = 2,
    /// <summary>Résistance du bouclier par type, [min, max] : part des dégâts absorbés qui n'entame pas ses PV.</summary>
    [property: JsonPropertyName("shieldResistance")] double[][]? ShieldResistance = null)
{
    /// <summary>Résistance du bouclier plein à ce type de dégâts (0 à 1).</summary>
    public double ShieldResistanceOf(DamageKind kind)
    {
        var i = (int)kind;
        if (ShieldResistance is null || i >= ShieldResistance.Length || ShieldResistance[i].Length < 2) return 0;
        return Math.Clamp(ShieldResistance[i][1], 0, 1);
    }

    public double ArmorMultiplierOf(DamageKind kind) =>
        (int)kind < ArmorDamageMultiplier.Length ? ArmorDamageMultiplier[(int)kind] : 1;

    public IReadOnlyList<CombatMount> MountList => Mounts ?? Array.Empty<CombatMount>();

    public double DeflectionOf(DamageKind kind) => (int)kind < Deflection.Length ? Deflection[(int)kind] : 0;

    /// <summary>Part des dégâts de ce type que le bouclier PLEIN laisse passer (0 à 1).</summary>
    public double ShieldLeak(DamageKind kind)
    {
        var i = (int)kind;
        if (i >= ShieldAbsorption.Length || ShieldAbsorption[i].Length < 2) return 0;
        return Math.Clamp(1 - ShieldAbsorption[i][1], 0, 1);
    }

    /// <summary>Part des dégâts de ce type qui passe quand le bouclier est presque vide (0 à 1).</summary>
    public double ShieldLeakWhenWeak(DamageKind kind)
    {
        var i = (int)kind;
        if (i >= ShieldAbsorption.Length || ShieldAbsorption[i].Length < 1) return 0;
        return Math.Clamp(1 - ShieldAbsorption[i][0], 0, 1);
    }
}

/// <summary>Générateur de bouclier (même absorption/résistance pour tous : seuls PV, régénération et délais changent).</summary>
public sealed record CombatShield(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] int Size,
    [property: JsonPropertyName("grade")] string? Grade,
    [property: JsonPropertyName("class")] string? Class,
    [property: JsonPropertyName("manufacturer")] string? Manufacturer,
    [property: JsonPropertyName("hp")] double Hp,
    [property: JsonPropertyName("regen")] double Regen,
    /// <summary>Secondes sans dégâts avant que la régénération reprenne.</summary>
    [property: JsonPropertyName("damagedDelay")] double DamagedDelay,
    /// <summary>Secondes avant de remonter une fois le bouclier tombé.</summary>
    [property: JsonPropertyName("downedDelay")] double DownedDelay);

public sealed record CombatData(
    [property: JsonPropertyName("gameVersion")] string GameVersion,
    [property: JsonPropertyName("ships")] IReadOnlyList<CombatShip> Ships,
    [property: JsonPropertyName("weapons")] IReadOnlyList<CombatWeapon> Weapons,
    [property: JsonPropertyName("shields")] IReadOnlyList<CombatShield>? Shields = null);

/// <summary>
/// Base locale (hors-ligne) des armes de vaisseau et du blindage/bouclier
/// des vaisseaux, pour le tableau « 🎯 Pénétration ». Instantané généré à
/// partir des données extraites des fichiers du jeu (projet
/// StarCitizenWiki/scunpacked-data : ships.json et ship-items.json), à
/// régénérer après un patch qui change les armes ou le blindage.
/// </summary>
public static class CombatDatabase
{
    private static readonly Lazy<CombatData> Data = new(Load);

    public static string GameVersion => Data.Value.GameVersion;

    /// <summary>Vaisseaux, du plus blindé au moins blindé.</summary>
    public static IReadOnlyList<CombatShip> Ships => Data.Value.Ships;

    public static IReadOnlyList<CombatWeapon> Weapons => Data.Value.Weapons;

    public static IReadOnlyList<CombatShield> Shields => Data.Value.Shields ?? Array.Empty<CombatShield>();

    public static CombatShield? FindShield(string name) =>
        Shields.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Générateurs qui peuvent aller sur cet emplacement, du plus solide au moins solide.</summary>
    public static IReadOnlyList<CombatShield> ShieldsFor(CombatMount mount) =>
        Shields.Where(mount.Accepts).OrderByDescending(s => s.Hp).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public static CombatWeapon? FindWeapon(string name) =>
        Weapons.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Armes montées d'origine sur ce vaisseau (celles connues de la base).</summary>
    public static IReadOnlyList<CombatWeapon> StockWeaponsOf(CombatShip ship) =>
        ship.StockWeapons.Select(FindWeapon).OfType<CombatWeapon>().ToList();

    public static CombatShip? FindShip(string name) =>
        Ships.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Armes qui peuvent aller sur cet emplacement, de la plus petite à la plus grande.</summary>
    public static IReadOnlyList<CombatWeapon> WeaponsFor(CombatMount mount) =>
        Weapons.Where(mount.Accepts).OrderBy(w => w.Size ?? 0).ThenBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public static CombatData Parse(Stream stream) =>
        JsonSerializer.Deserialize<CombatData>(stream) ?? new CombatData("?", Array.Empty<CombatShip>(), Array.Empty<CombatWeapon>());

    private static CombatData Load()
    {
        try
        {
            var assembly = typeof(CombatDatabase).Assembly;
            var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("CombatData.json", StringComparison.Ordinal));
            using var stream = resourceName is null ? null : assembly.GetManifestResourceStream(resourceName);
            if (stream is not null) return Parse(stream);
        }
        catch
        {
            // Base absente ou corrompue : tableau vide plutôt qu'un plantage.
        }
        return new CombatData("?", Array.Empty<CombatShip>(), Array.Empty<CombatWeapon>());
    }
}
