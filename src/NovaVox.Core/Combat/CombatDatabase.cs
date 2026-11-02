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
    [property: JsonPropertyName("stockWeapons")] IReadOnlyList<string> StockWeapons)
{
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

public sealed record CombatData(
    [property: JsonPropertyName("gameVersion")] string GameVersion,
    [property: JsonPropertyName("ships")] IReadOnlyList<CombatShip> Ships,
    [property: JsonPropertyName("weapons")] IReadOnlyList<CombatWeapon> Weapons);

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

    public static CombatWeapon? FindWeapon(string name) =>
        Weapons.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Armes montées d'origine sur ce vaisseau (celles connues de la base).</summary>
    public static IReadOnlyList<CombatWeapon> StockWeaponsOf(CombatShip ship) =>
        ship.StockWeapons.Select(FindWeapon).OfType<CombatWeapon>().ToList();

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
