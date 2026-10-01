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
///
/// SchemaNameAliases.&lt;locale&gt;.json (indépendant de la langue
/// d'interface ci-dessus) couvre un tout autre problème : le CLIENT
/// Star Citizen de l'utilisateur peut tourner dans une langue différente
/// de celle de NovaVox, et c'est LUI qui détermine le texte écrit dans
/// Game.log — "Bras Antium" au lieu de "Antium Arms" quand le jeu est en
/// français, par exemple. Comme ce nom traduit ne correspond plus à
/// aucune clé de SchemaDatabase.json (toujours en anglais, langue de la
/// source communautaire), <see cref="Find"/> consulte TOUS les fichiers
/// d'alias disponibles (quel que soit le paramètre <c>language</c>, qui
/// ne concerne que la traduction de la description) pour retrouver le
/// nom canonique anglais correspondant.
/// </summary>
public static class SchemaDatabase
{
    private static readonly Lazy<IReadOnlyDictionary<string, SchemaInfo>> ByName = new(LoadEntries);
    private static readonly Lazy<IReadOnlyDictionary<string, SchemaInfo>> ByNormalizedName =
        new(() => BuildNormalizedIndex(ByName.Value));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> NameAliases = new(LoadNameAliases);

    private static readonly string[] TranslatedLanguages = { "fr", "nl", "es", "it", "de" };
    private static readonly Dictionary<string, Lazy<IReadOnlyDictionary<string, string>>> TranslationsByLanguage =
        TranslatedLanguages.ToDictionary(lang => lang, lang => new Lazy<IReadOnlyDictionary<string, string>>(() => LoadTranslations(lang)), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Recherche insensible à la casse/aux espaces/au style de guillemets
    /// (voir NormalizeForMatch) — null si ce nom ne correspond à aucun
    /// schéma connu de la base locale, même via un alias (voir
    /// SchemaNameAliases.&lt;locale&gt;.json). La description est traduite
    /// dans <paramref name="language"/> (code "fr"/"nl"/"es"/"it"/"de")
    /// quand une traduction existe pour ce nom précis, sinon elle reste en
    /// anglais — jamais null juste parce que la traduction manque tant que
    /// l'entrée elle-même existe.
    /// </summary>
    public static SchemaInfo? Find(string? name, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();

        var info = LookupEntry(trimmed);
        if (info is null)
        {
            // Pas trouvé tel quel (même après normalisation des guillemets) :
            // peut-être un nom traduit par le client du jeu — voir le
            // résumé de la classe. L'alias résout vers le nom canonique
            // anglais, qu'on relance alors dans la même recherche.
            if (NameAliases.Value.TryGetValue(NormalizeForMatch(trimmed), out var canonicalName))
                info = LookupEntry(canonicalName);
            if (info is null) return null;
        }

        if (!string.IsNullOrEmpty(language)
            && TranslationsByLanguage.TryGetValue(language, out var translations)
            && translations.Value.TryGetValue(info.Name, out var translated))
        {
            info = info with { Description = translated };
        }
        return info;
    }

    private static SchemaInfo? LookupEntry(string trimmedName)
    {
        if (ByName.Value.TryGetValue(trimmedName, out var exact)) return exact;
        return ByNormalizedName.Value.GetValueOrDefault(NormalizeForMatch(trimmedName));
    }

    /// <summary>
    /// Nivelle les variantes purement typographiques d'un même nom avant
    /// comparaison : toute forme de guillemet (apostrophes/guillemets
    /// courbes ’‘, chevrons français « », avec ou sans espace insécable à
    /// l'intérieur, ou simple vs double ' / ") ramenée à UN SEUL caractère
    /// canonique ("), espaces multiples réduits à un seul. Simple/double
    /// confondus à dessein : un guillemet dans un nom de schéma ne marque
    /// jamais une vraie apostrophe (pas de contraction anglaise dans ces
    /// noms), seulement un surnom entre guillemets — et le jeu lui-même
    /// n'est pas cohérent d'une entrée à l'autre (7CA 'Nargun' en simples
    /// vs Demeco "Purgatory Camo" LMG en doubles dans la base communautaire
    /// elle-même), tandis qu'un client en français écrit « Nom » avec des
    /// chevrons — sans ce nivellement, ces variantes ne se retrouveraient
    /// jamais, alors que c'est littéralement le même schéma.
    /// </summary>
    private static string NormalizeForMatch(string s)
    {
        var chars = s
            .Replace('’', '"').Replace('‘', '"')
            .Replace('“', '"').Replace('”', '"')
            .Replace('«', '"').Replace('»', '"')
            .Replace('\'', '"');
        return string.Join(' ', chars.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static IReadOnlyDictionary<string, SchemaInfo> BuildNormalizedIndex(IReadOnlyDictionary<string, SchemaInfo> byName)
    {
        var result = new Dictionary<string, SchemaInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in byName.Values)
            result.TryAdd(NormalizeForMatch(info.Name), info);
        return result;
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

    /// <summary>
    /// Charge et fusionne TOUS les SchemaNameAliases.&lt;locale&gt;.json
    /// embarqués (nom tel qu'écrit par le client du jeu dans cette locale
    /// -> nom canonique anglais de SchemaDatabase.json), quel que soit le
    /// nombre de locales couvertes — un seul client de jeu actif à la
    /// fois, pas besoin de cibler un fichier précis. Clé indexée via
    /// NormalizeForMatch pour rester robuste aux guillemets (voir Find).
    /// </summary>
    private static IReadOnlyDictionary<string, string> LoadNameAliases()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var assembly = typeof(SchemaDatabase).Assembly;
            var resourceNames = assembly.GetManifestResourceNames()
                .Where(n => n.Contains("SchemaNameAliases.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal));

            foreach (var resourceName in resourceNames)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null) continue;

                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
                foreach (var (localizedName, canonicalName) in raw)
                    result.TryAdd(NormalizeForMatch(localizedName.Trim()), canonicalName.Trim());
            }
        }
        catch
        {
            // Un fichier d'alias corrompu ne doit jamais empêcher le reste de
            // la base de fonctionner (repli sur une recherche sans alias).
        }
        return result;
    }
}
