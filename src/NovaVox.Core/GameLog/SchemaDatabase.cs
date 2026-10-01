using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaVox.Core.GameLog;

/// <summary>Un ingrédient (ressource ou objet) requis pour fabriquer un schéma — voir SchemaInfo.Ingredients.</summary>
public sealed record SchemaIngredient(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("amount")] double Amount,
    /// <summary>"SCU" (ressource brute, ex. Agricium) ou "pcs" (objet discret, ex. un autre composant déjà fabriqué).</summary>
    [property: JsonPropertyName("unit")] string Unit);

/// <summary>Fiche d'un schéma de fabrication — une entrée de SchemaDatabase.json.</summary>
public sealed record SchemaInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("subtype")] string? Subtype,
    [property: JsonPropertyName("manufacturer")] string? Manufacturer,
    /// <summary>Texte descriptif en anglais (langue de la source — voir SchemaDatabase, pas encore traduit).</summary>
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("craftTimeSeconds")] int? CraftTimeSeconds,
    [property: JsonPropertyName("ingredients")] IReadOnlyList<SchemaIngredient> Ingredients);

/// <summary>
/// Base de données locale (hors-ligne) des schémas de fabrication du jeu —
/// nom, fabricant, description, temps de fabrication, ingrédients. Permet
/// d'afficher ces informations dans Réglages > 📐 Schémas dès qu'un schéma
/// est débloqué, sans dépendre d'une connexion réseau (cohérent avec le
/// reste de l'appli — Vosk/Piper tournent déjà hors-ligne).
///
/// SchemaDatabase.json est un instantané généré une fois à partir de
/// données communautaires extraites des fichiers du jeu (projet
/// StarCitizenWiki/scunpacked-data, blueprints.json + items.json
/// recoupés par UUID) — à régénérer manuellement après un gros patch du
/// jeu qui changerait sensiblement les schémas (nouveaux objets,
/// descriptions mises à jour). Pas de traduction française disponible
/// dans la source : les descriptions restent en anglais.
/// </summary>
public static class SchemaDatabase
{
    private static readonly Lazy<IReadOnlyDictionary<string, SchemaInfo>> ByName = new(Load);

    /// <summary>Recherche insensible à la casse/aux espaces — null si ce nom ne correspond à aucun schéma connu de la base locale.</summary>
    public static SchemaInfo? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return ByName.Value.GetValueOrDefault(name.Trim());
    }

    private static IReadOnlyDictionary<string, SchemaInfo> Load()
    {
        try
        {
            var assembly = typeof(SchemaDatabase).Assembly;
            var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("SchemaDatabase.json", StringComparison.Ordinal));
            if (resourceName is null) return new Dictionary<string, SchemaInfo>();

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return new Dictionary<string, SchemaInfo>();

            var entries = JsonSerializer.Deserialize<List<SchemaInfo>>(stream) ?? new List<SchemaInfo>();
            var result = new Dictionary<string, SchemaInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
                result.TryAdd(entry.Name.Trim(), entry);
            return result;
        }
        catch
        {
            // Base locale corrompue/absente : les fiches resteront simplement
            // sans détail (nom seul) plutôt que de faire planter l'appli.
            return new Dictionary<string, SchemaInfo>();
        }
    }
}
