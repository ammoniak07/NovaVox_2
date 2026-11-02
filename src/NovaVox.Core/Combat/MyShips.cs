using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaVox.Core.Combat;

/// <summary>
/// Un vaisseau du joueur avec ses armes choisies (« Mes vaisseaux ») :
/// nom libre, modèle de vaisseau, et pour chaque emplacement modifié
/// l'arme montée (Id d'emplacement -> nom d'arme). Les emplacements absents
/// gardent l'arme d'origine.
/// </summary>
public sealed class MyShipLoadout
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("ship")] public string Ship { get; set; } = "";
    [JsonPropertyName("weapons")] public Dictionary<string, string> Weapons { get; set; } = new();

    /// <summary>Arme montée sur cet emplacement : celle choisie, sinon celle d'origine (null = vide).</summary>
    public string? WeaponOn(CombatMount mount) =>
        Weapons.TryGetValue(mount.Id, out var chosen) ? (chosen.Length == 0 ? null : chosen) : mount.Stock;

    /// <summary>
    /// Armes montées, regroupées par arme : (arme, nombre d'armes ou de
    /// missiles). Ignore les noms inconnus de la base (ex. après un patch).
    /// </summary>
    public IReadOnlyList<(CombatWeapon Weapon, int Count)> EquippedWeapons(CombatShip ship, bool pilotOnly = false)
    {
        var counts = new Dictionary<string, (CombatWeapon Weapon, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var mount in ship.MountList)
        {
            if (pilotOnly && !mount.Pilot) continue;
            var name = WeaponOn(mount);
            if (name is null || CombatDatabase.FindWeapon(name) is not { } weapon) continue;
            counts[weapon.Name] = counts.TryGetValue(weapon.Name, out var c) ? (weapon, c.Count + mount.Count) : (weapon, mount.Count);
        }
        return counts.Values.OrderBy(v => v.Weapon.IsGun ? 0 : 1).ThenBy(v => v.Weapon.Size ?? 0).ThenBy(v => v.Weapon.Name).ToList();
    }
}

/// <summary>Enregistre « Mes vaisseaux » dans my_ships.json (exporté avec la configuration).</summary>
public sealed class MyShipsStore
{
    public const string FileName = "my_ships.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;

    public MyShipsStore(string baseDir)
    {
        _path = Path.Combine(baseDir, FileName);
    }

    public List<MyShipLoadout> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new List<MyShipLoadout>();
            var file = JsonSerializer.Deserialize<MyShipsFile>(File.ReadAllText(_path), Options);
            return (file?.Ships ?? new List<MyShipLoadout>())
                .Where(s => !string.IsNullOrWhiteSpace(s.Ship))
                .Select(s =>
                {
                    s.Weapons ??= new Dictionary<string, string>();
                    if (string.IsNullOrWhiteSpace(s.Name)) s.Name = s.Ship;
                    return s;
                })
                .ToList();
        }
        catch
        {
            return new List<MyShipLoadout>();
        }
    }

    public void Save(IEnumerable<MyShipLoadout> ships)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new MyShipsFile { Ships = ships.ToList() }, Options));
        File.Move(tmp, _path, overwrite: true);
    }

    private sealed class MyShipsFile
    {
        [JsonPropertyName("ships")] public List<MyShipLoadout> Ships { get; set; } = new();
    }
}
