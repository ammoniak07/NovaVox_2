using System.Collections.Concurrent;
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

public sealed record SchemaStat(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("value")] string Value);

/// <summary>Fiche d'un schéma de fabrication — une entrée de SchemaDatabase.json, Description éventuellement traduite (voir SchemaDatabase.Find).</summary>
public sealed record SchemaInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("subtype")] string? Subtype,
    [property: JsonPropertyName("manufacturer")] string? Manufacturer,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("craftTimeSeconds")] int? CraftTimeSeconds,
    [property: JsonPropertyName("damageReduction")] int? DamageReductionPercent,
    [property: JsonPropertyName("size")] int? Size,
    [property: JsonPropertyName("grade")] string? Grade,
    [property: JsonPropertyName("componentClass")] string? ComponentClass,
    [property: JsonPropertyName("capacityMicroScu")] int? CapacityMicroScu,
    [property: JsonPropertyName("itemType")] string? ItemType,
    [property: JsonPropertyName("stats")] IReadOnlyList<SchemaStat>? Stats,
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
/// nom canonique anglais correspondant. Liste figée construite/complétée
/// à la main au fil des remontées utilisateur — ne couvre donc qu'UNE
/// traduction communautaire précise (celle déjà constatée), jamais toutes.
///
/// RegisterLiveNameAliases comble cette limite autrement : au lieu d'une
/// liste figée, elle lit le global.ini LOCAL du joueur (voir
/// GameLogLocalization, appelé par l'App au démarrage) et le croise, clé
/// par clé (SchemaNameLocalizationKeys.json, nom canonique -> clé
/// global.ini, construit une fois depuis scunpacked-data), avec SA
/// traduction installée — quelle qu'elle soit, SCEFRA ou une autre,
/// jamais redistribuée par NovaVox lui-même (juste lue localement, comme
/// le Game.log).
/// </summary>
public static class SchemaDatabase
{
    private static readonly Lazy<IReadOnlyDictionary<string, SchemaInfo>> ByName = new(LoadEntries);
    private static readonly Lazy<IReadOnlyDictionary<string, SchemaInfo>> ByNormalizedName =
        new(() => BuildNormalizedIndex(ByName.Value));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> NameAliases = new(LoadNameAliases);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> NameToLocalizationKey = new(LoadNameLocalizationKeys);
    private static readonly Lazy<IReadOnlyDictionary<string, string>> LocalizationKeyToName =
        new(() => NameToLocalizationKey.Value
            .GroupBy(kv => kv.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal));
    private static readonly Lazy<HashSet<string>> LocalizationKeySet =
        new(() => new HashSet<string>(NameToLocalizationKey.Value.Values, StringComparer.Ordinal));

    /// <summary>Noms traduits trouvés dans le global.ini du joueur pour la session en cours — voir RegisterLiveNameAliases.</summary>
    private static readonly ConcurrentDictionary<string, string> LiveNameAliases = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] TranslatedLanguages = { "fr", "nl", "es", "it", "de" };
    private static readonly Dictionary<string, Lazy<IReadOnlyDictionary<string, string>>> TranslationsByLanguage =
        TranslatedLanguages.ToDictionary(lang => lang, lang => new Lazy<IReadOnlyDictionary<string, string>>(() => LoadTranslations(lang)), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Clés global.ini à demander à GameLogLocalization.ReadKeyedValues
    /// (une par nom canonique de la base) — voir RegisterLiveNameAliases.
    /// </summary>
    public static IReadOnlySet<string> LocalizationKeysOfInterest => LocalizationKeySet.Value;

    /// <summary>
    /// Enregistre, pour la session en cours, les correspondances texte
    /// localisé -> nom canonique lues dans le global.ini LOCAL du joueur
    /// (voir le résumé de la classe) — à appeler une fois au démarrage de
    /// l'appli avec le résultat de GameLogLocalization.ReadKeyedValues(path,
    /// LocalizationKeysOfInterest). Idempotent et sans risque si le joueur
    /// n'a pas de traduction installée (les valeurs lues sont alors déjà en
    /// anglais, donc identiques au nom canonique — l'alias ne change rien).
    /// </summary>
    public static void RegisterLiveNameAliases(IReadOnlyDictionary<string, string> keyedValues)
    {
        foreach (var (key, value) in keyedValues)
        {
            if (!LocalizationKeyToName.Value.TryGetValue(key, out var canonicalName)) continue;
            LiveNameAliases[NormalizeForMatch(value.Trim())] = canonicalName;
        }
    }

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
            // anglais, qu'on relance alors dans la même recherche — d'abord
            // la liste figée (toujours disponible), puis celle déduite du
            // global.ini local du joueur le cas échéant (plus complète,
            // couvre n'importe quelle traduction installée).
            var normalized = NormalizeForMatch(trimmed);
            if (NameAliases.Value.TryGetValue(normalized, out var canonicalName))
                info = LookupEntry(canonicalName);
            if (info is null && LiveNameAliases.TryGetValue(normalized, out var liveCanonicalName))
                info = LookupEntry(liveCanonicalName);
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

    private static readonly Lazy<IReadOnlyList<string>> AllNames =
        new(() => ByName.Value.Values.Select(i => i.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());

    /// <summary>
    /// Noms de la base locale contenant <paramref name="query"/> (insensible
    /// à la casse), pour l'autocomplétion de l'ajout manuel (Réglages >
    /// 📐 Schémas) — ceux qui COMMENCENT par <paramref name="query"/>
    /// d'abord, puis les autres, alphabétique dans chaque groupe, au plus
    /// <paramref name="maxResults"/>. Vide/null -> liste vide : pas
    /// question de proposer les ~1600 noms d'un coup sans rien taper.
    /// </summary>
    public static IReadOnlyList<string> SearchNames(string? query, int maxResults = 15)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();
        var q = query.Trim();
        return AllNames.Value
            .Where(n => n.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    private static SchemaInfo? LookupEntry(string trimmedName)
    {
        if (ByName.Value.TryGetValue(trimmedName, out var exact)) return exact;
        return ByNormalizedName.Value.GetValueOrDefault(NormalizeForMatch(trimmedName));
    }

    /// <summary>
    /// Nivelle les variantes purement typographiques d'un même nom avant
    /// comparaison : toute forme de guillemet (apostrophes/guillemets
    /// courbes ’‘, chevrons français « », ou simple vs double ' / ") ramenée
    /// à UN SEUL caractère canonique ("), puis tout espace directement
    /// collé à un guillemet retiré (le français écrit « Mot » avec un
    /// espace insécable À L'INTÉRIEUR des chevrons, contrairement à
    /// l'anglais "Mot" qui colle le mot au guillemet — sans ce retrait,
    /// "Demeco « Purgatory Camo » LMG" ne retrouvait jamais 'Demeco
    /// "Purgatory Camo" LMG' dans la base, à cause de ce seul espace en
    /// trop de part et d'autre du texte entre guillemets), enfin espaces
    /// multiples restants réduits à un seul. Simple/double confondus à
    /// dessein : un guillemet dans un nom de schéma ne marque jamais une
    /// vraie apostrophe (pas de contraction anglaise dans ces noms),
    /// seulement un surnom entre guillemets — et le jeu lui-même n'est pas
    /// cohérent d'une entrée à l'autre (7CA 'Nargun' en simples vs Demeco
    /// "Purgatory Camo" LMG en doubles dans la base communautaire
    /// elle-même).
    /// </summary>
    private static string NormalizeForMatch(string s)
    {
        var unifiedQuotes = s
            .Replace('’', '"').Replace('‘', '"')
            .Replace('“', '"').Replace('”', '"')
            .Replace('«', '"').Replace('»', '"')
            .Replace('\'', '"');
        var noQuotePadding = QuotePaddingRegex.Replace(unifiedQuotes, "\"");
        return string.Join(' ', noQuotePadding.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static readonly System.Text.RegularExpressions.Regex QuotePaddingRegex = new(@"\s*""\s*");

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
    /// Charge SchemaNameLocalizationKeys.json (nom canonique -> clé
    /// global.ini, ex. "Antium Arms" -> "item_Name_qrt_specialist_heavy_
    /// arms_01_01_01") — construit une fois depuis scunpacked-data
    /// (labels.json, même source que SchemaDatabase.json), jamais modifié
    /// par l'utilisateur. Voir RegisterLiveNameAliases pour son usage.
    /// </summary>
    private static IReadOnlyDictionary<string, string> LoadNameLocalizationKeys()
    {
        try
        {
            var assembly = typeof(SchemaDatabase).Assembly;
            var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("SchemaNameLocalizationKeys.json", StringComparison.Ordinal));
            if (resourceName is null) return new Dictionary<string, string>();

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return new Dictionary<string, string>();

            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
        }
        catch
        {
            // Fichier corrompu/absent : RegisterLiveNameAliases n'aura
            // simplement aucun effet, repli sur SchemaNameAliases.<locale>.json.
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
