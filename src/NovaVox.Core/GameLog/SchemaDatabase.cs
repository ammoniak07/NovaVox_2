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

/// <summary>Fiche d'un schéma de fabrication — une entrée de SchemaDatabase.json, Description éventuellement traduite (voir SchemaDatabase.Find).</summary>
public sealed record SchemaInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("subtype")] string? Subtype,
    [property: JsonPropertyName("manufacturer")] string? Manufacturer,
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
/// SchemaDatabase.json (anglais, langue de la source) est un instantané
/// généré une fois à partir de données communautaires extraites des
/// fichiers du jeu (projet StarCitizenWiki/scunpacked-data,
/// blueprints.json + items.json recoupés par UUID) — à régénérer
/// manuellement après un gros patch du jeu qui changerait sensiblement
/// les schémas. SchemaDatabase.&lt;lang&gt;.json (fr/nl/es/it/de) ne
/// contiennent QUE les descriptions traduites (nom -> texte), un par
/// langue de l'interface — <see cref="Find"/> retombe silencieusement
/// sur la description anglaise quand la langue demandée n'a pas (ou pas
/// encore) de traduction pour ce nom précis.
/// </summary>
public static class SchemaDatabase
{
    private static readonly Lazy<IReadOnlyDictionary<string, SchemaInfo>> ByName = new(LoadEntries);
    private static readonly string[] TranslatedLanguages = { "fr", "nl", "es", "it", "de" };
    private static readonly Dictionary<string, Lazy<IReadOnlyDictionary<string, string>>> TranslationsByLanguage =
        TranslatedLanguages.ToDictionary(lang => lang, lang => new Lazy<IReadOnlyDictionary<string, string>>(() => LoadTranslations(lang)), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Recherche insensible à la casse/aux espaces — null si ce nom ne
    /// correspond à aucun schéma connu de la base locale. La description
    /// est traduite dans <paramref name="language"/> (code "fr"/"nl"/"es"/
    /// "it"/"de") quand une traduction existe pour ce nom précis, sinon
    /// elle reste en anglais — jamais null juste parce que la traduction
    /// manque tant que l'entrée elle-même existe.
    /// </summary>
    public static SchemaInfo? Find(string? name, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (!ByName.Value.TryGetValue(name.Trim(), out var info)) return null;

        if (!string.IsNullOrEmpty(language)
            && TranslationsByLanguage.TryGetValue(language, out var translations)
            && translations.Value.TryGetValue(info.Name, out var translated))
        {
            info = info with { Description = translated };
        }
        return info;
    }

    private static IReadOnlyDictionary<string, SchemaInfo> LoadEntries()
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

    private static IReadOnlyDictionary<string, string> LoadTranslations(string language)
    {
        try
        {
            var assembly = typeof(SchemaDatabase).Assembly;
            var suffix = $"SchemaDatabase.{language}.json";
            var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal));
            if (resourceName is null) return new Dictionary<string, string>();

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return new Dictionary<string, string>();

            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
            return new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Fichier de traduction corrompu/absent pour cette langue : repli
            // silencieux sur l'anglais (voir Find), jamais bloquant.
            return new Dictionary<string, string>();
        }
    }
}
