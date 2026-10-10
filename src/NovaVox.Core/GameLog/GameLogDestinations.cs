using System.Text.RegularExpressions;

namespace NovaVox.Core.GameLog;

/// <summary>
/// Port de la résolution de noms de destination de game_log_watcher.py
/// (identifiants bruts du Game.log de Star Citizen -> noms prononçables).
/// Logique pure, sans dépendance à Windows.
/// </summary>
public static partial class GameLogDestinations
{
    [GeneratedRegex("objectcontainer[_\\s]*", RegexOptions.IgnoreCase)]
    private static partial Regex ObjectContainerPrefixRegex();

    [GeneratedRegex(@"^rs[_-][a-z]+[_-](?<sys1>[a-z]+)[_-](?<sys2>[a-z]+)[_-]jp\d*$")]
    private static partial Regex JumpPointIdRegex();

    [GeneratedRegex(@"^OOC_(?<system>[A-Za-z]+)_\d+[a-z]?_(?<body>[A-Za-z0-9]+)$")]
    private static partial Regex OocLocationRegex();

    [GeneratedRegex(@"_\d{10,}$")]
    private static partial Regex InstanceSuffixLongRegex();

    [GeneratedRegex(@"_\d{6,}$")]
    private static partial Regex InstanceSuffixRegex();

    [GeneratedRegex(@"\s\d{6,}$")]
    private static partial Regex InstanceSuffixSpaceRegex();

    [GeneratedRegex("^MISSION_QT_")]
    private static partial Regex MissionQtPrefixRegex();

    /// <summary>
    /// Format d'identifiant "RR_P{système}_L{point}" (ex. "RR_P6_L5") vu dans
    /// la ligne RequestLocationInventory (vrai Game.log, 30/09/2026) —
    /// désigne le même lieu que "rs ext pyro{système} l{point}" déjà connu
    /// (ex. Megumi Ravitaillement = pyro6 l5), mais avec une abréviation
    /// différente selon le sous-système du jeu qui l'émet. Sans cette
    /// équivalence, ces identifiants ne correspondaient à aucune entrée du
    /// catalogue et retombaient sur un repli générique.
    /// </summary>
    [GeneratedRegex(@"^rr p(?<system>\d+) l(?<point>\d+)$")]
    private static partial Regex RrPyroStationIdRegex();

    /// <summary>
    /// Même sous-système que RrPyroStationIdRegex, mais pour les stations
    /// "Leo" nommées par système plutôt que par numéro (ex. "RR_CRU_LEO" vu
    /// dans un vrai Game.log, 30/09/2026, alors que le joueur était à
    /// Seraphim Station = "rs ext cru leo1") — le numéro de station (souvent
    /// implicite "1", seule variante rencontrée jusqu'ici) est parfois omis
    /// par ce sous-système alors qu'il fait partie de la clé canonique.
    /// </summary>
    [GeneratedRegex(@"^rr (?<system>[a-z]+) leo(?<num>\d*)$")]
    private static partial Regex RrLeoStationIdRegex();

    /// <summary>Réécrit un identifiant déjà normalisé (espaces) vers la forme canonique attendue par le reste de la résolution, si un format alternatif connu le désigne.</summary>
    [GeneratedRegex(@"^stanton\d+[a-z]? shubin(?:mining)? (?<site>.+)$")]
    private static partial Regex ShubinMineIdRegex();

    [GeneratedRegex(@"^stanton\d+[a-z]? distributioncentre covalex (?<site>.+)$")]
    private static partial Regex CovalexDistributionCentreIdRegex();

    [GeneratedRegex(@"^stanton\d+[a-z]? arccorp (?<site>.+)$")]
    private static partial Regex ArcCorpMineIdRegex();

    /// <summary>Sites numérotés d'une même famille ("stanton4a shubin smca 6" -> "Mine Shubin SMCA6") : un seul motif plutôt qu'une entrée de catalogue par site.</summary>
    private static string? HumanizeNumberedSite(string normalized)
    {
        foreach (var (regex, prefix, upperCase) in new (Regex, string, bool)[]
        {
            (ShubinMineIdRegex(), "Mine Shubin", true),
            (CovalexDistributionCentreIdRegex(), "Centre de Distribution Covalex", true),
            (ArcCorpMineIdRegex(), "Mine ArcCorp", false),
        })
        {
            var match = regex.Match(normalized);
            if (!match.Success) continue;
            var site = match.Groups["site"].Value.Replace(" ", "");
            return $"{prefix} {(upperCase ? site.ToUpperInvariant() : char.ToUpperInvariant(site[0]) + site[1..])}";
        }
        return null;
    }

    private static string NormalizeKnownIdSynonyms(string normalized)
    {
        var rrPyroMatch = RrPyroStationIdRegex().Match(normalized);
        if (rrPyroMatch.Success)
            return $"rs ext pyro{rrPyroMatch.Groups["system"].Value} l{rrPyroMatch.Groups["point"].Value}";

        var rrLeoMatch = RrLeoStationIdRegex().Match(normalized);
        if (rrLeoMatch.Success)
        {
            var num = rrLeoMatch.Groups["num"].Value;
            return $"rs ext {rrLeoMatch.Groups["system"].Value} leo{(num.Length == 0 ? "1" : num)}";
        }

        return normalized;
    }

    /// <summary>Alias connus pour des identifiants internes qui ne ressemblent à rien une fois "underscore -> espace".</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownLocationAliases = new Dictionary<string, string>
    {
        ["rs ext cru leo1"] = "Seraphim Station",
        ["rs ext hur leo1"] = "Everus Harbor",

        ["loc rr s1 l1"] = "Green Glade Station",
        ["loc rr s1 l2"] = "Faithful Dream Station",
        ["loc rr s1 l3"] = "Thundering Express Station",
        ["loc rr s1 l4"] = "Melodic Fields Station",
        ["loc rr s1 l5"] = "High Course Station",
        ["loc rr s2 l5"] = "Beautiful Glen Station",
        ["loc rr s3 l1"] = "Wide Forest Station",
        ["loc rr s3 l3"] = "Modern Express Station",
        ["loc rr s3 l4"] = "Faint Glen Station",
        ["loc rr s3 l5"] = "yellow core station",
        ["loc rr s4 l1"] = "Shallow Frontier Station",
        ["loc rr s4 l2"] = "Long Forest Station",
        ["loc rr s4 l4"] = "Crossroads Station",
        ["loc rr s4 l5"] = "Modern Icarus Station MIC L5",
        ["rr mic l5"] = "Modern Icarus Station MIC L5",
        ["rr arc l4"] = "Faint Glen Station",
        ["rr hur l2"] = "Faithful Dream Station",

        ["loc rs ext stan terra jp1"] = "Terra Gateway",
        ["loc rs ext stan magnus jp1"] = "Nyx Gateway",
        ["loc rs ext stan pyro jp1"] = "Pyro Gateway",
        ["rr jp stantonpyro"] = "Pyro Gateway",
        ["rr jp pyrostanton"] = "Stanton Gateway",
        ["rr jp stantonmagnus"] = "Nyx Gateway",
        // Plusieurs portes distinctes portent le même nom affiché "Stanton
        // Gateway" (une par système relié à Stanton -- confirmé par
        // l'utilisateur : une dans Nyx, une dans Pyro ci-dessus) ; "nyxcastra"
        // est l'identifiant interne de celle du système Nyx, malgré "castra"
        // dans son nom.
        ["rr jp nyxcastra"] = "Stanton Gateway",
        ["rr jp pyronyx"] = "Nyx Gateway",
        ["rr jp nyxpyro"] = "Pyro Gateway",

        ["ooc stanton"] = "l'étoile Stanton",
        ["ooc stanton2 l2"] = "CRU L 2",
        ["ooc stanton2 l3"] = "CRU L3",
        ["ab collector gas stanton1"] = "Wikelo's Emporium - Dasi Station",
        ["ab collector gas stanton4"] = "Wikelo's Emporium - Kinga Station",
        ["ab collector gas stanton2"] = "Wikelo emporium selo station",
        ["ab mine stanton3 med 005"] = "Base minière DYV-JKE",
        ["rs ext arc l001"] = "Lively Pathway Station",

        ["ooc stanton 1 hurston"] = "Hurston",
        ["ooc stanton 1a ariel"] = "Ariel",
        ["ooc stanton 1b aberdeen"] = "Aberdeen",
        ["ooc stanton 1c magda"] = "Magda",
        ["ooc stanton 1d ita"] = "Ita",
        ["ooc stanton 2 crusader"] = "Crusader",
        ["ooc stanton 2a cellin"] = "Cellin",
        ["ooc stanton 2b daymar"] = "Daymar",
        ["ooc stanton 2c yela"] = "Yela",
        ["ooc stanton 3 arccorp"] = "ArcCorp",
        ["ooc stanton 3a lyria"] = "Lyria",
        ["ooc stanton 3b wala"] = "Wala",
        ["ooc stanton 4 microtech"] = "microTech",
        ["ooc stanton 4a calliope"] = "Calliope",
        ["ooc stanton 4b clio"] = "Clio",
        ["ooc stanton 4c euterpe"] = "Euterpe",

        ["lorville city"] = "Lorville",
        ["stanton1 lorville"] = "Lorville",
        ["grimhex"] = "GrimHEX",
        ["area18 city"] = "Area 18",
        ["stanton3 area18"] = "Area 18",
        ["orison loc"] = "Orison",
        ["stanton2 orison"] = "Orison",
        ["stanton4 newbabbage"] = "NewBabbage",
        ["newbabbage loc"] = "NewBabbage",
        ["ooc stanton1 commarray"] = "Réseau de communications Hurston",
        ["ooc stanton2 commarray"] = "Antenne de communication Crusader",
        ["ooc stanton3 commarray"] = "Réseau de communications ArcCorp",
        ["ooc stanton4 commarray"] = "Réseau de communications microTech",

        ["pyrostar"] = "l'étoile Pyro",
        ["pyro1"] = "Pyro I",
        ["pyro2"] = "Monox",
        ["pyro3"] = "Bloom",
        ["pyro5"] = "Pyro V",
        ["pyro5b"] = "Vatra",
        ["pyro5c"] = "Adir",
        ["pyro5e"] = "Fuego",
        ["pyro5f"] = "Vuur",
        ["pyro6"] = "Terminus",

        ["rs ext pyro6 leo"] = "Ruine Station",
        ["rs ext pyro3 leo"] = "Orbituary",

        ["rs ext pyro2 l4"] = "Checkmate",
        ["rs ext pyro3 l1"] = "Station-service Starlight",
        ["rs ext pyro3 l3"] = "Patch City",
        ["rs ext pyro5 l2"] = "Gaslight",
        ["rs ext pyro5 l4"] = "Rod's Fuel 'N Supplies",
        ["rs ext pyro5 l5"] = "Rat's Nest",
        ["p5 l3"] = "Pyro 5 L3",
        ["rs ext pyro6 l3"] = "Endgame",
        ["rs ext pyro6 l4"] = "Nyx Gateway",
        ["rs ext pyro6 l5"] = "Megumi Ravitaillement",

        ["social 001 keeger segment rckcrk 095"] = "Qv Breaker Station",
        ["social 001 keeger segment rckcrk 101"] = "Qv Breaker Station",
        ["social 001 keeger segment rckcrk 102"] = "Qv Breaker Station",
        ["social 001 keeger segment rckcrk 105"] = "Qv Breaker Station",
        ["social 001 keeger segment rckcrk 112"] = "Qv Breaker Station",
        ["asteroidclusterbase nyx social keeger 002"] = "Qv Breaker Station BRK 425",
        ["rs asmbl keeger 01"] = "Station-service Alpha de l'Alliance du Peuple",
        ["rs asmbl keeger 02"] = "Station-service Delta de l'Alliance du Peuple",
        ["rs asmbl keeger 03"] = "Station-service Theta de l'Alliance du Peuple",
        ["rs asmbl keeger 04"] = "Station-service Lambda de l'Alliance du Peuple",

        ["glaciemring transitpoint alpha"] = "point de transit Glaciem Alpha",
        ["glaciemring transitpoint bravo"] = "point de transit Glaciem Bravo",
        ["glaciemring transitpoint charlie"] = "point de transit Glaciem Charlie",

        ["nyxstar"] = "l'étoile Nyx",
        ["levski all 001"] = "Levski",
        ["nyx levski"] = "Levski",
    };

    /// <summary>
    /// Retire de <paramref name="userAliases"/> (AiConfig.GameLogDestinationAliases)
    /// toute entrée dont la clé existe déjà dans KnownLocationAliases, ou
    /// est résoluble par l'algorithme de point de saut (rs_..._jp...) — ces
    /// entrées personnelles ne faisaient que dupliquer un nom déjà géré
    /// d'origine (parfois avec un texte devenu périmé, voire faux pour un
    /// point de saut ajouté à SystemNames après coup), sans plus jamais en
    /// être la source une fois supprimées : GameLogAnnouncer.MaybeRegisterDestinationAlias
    /// n'en recrée plus pour un lieu déjà couvert par le catalogue ou
    /// l'algorithme (voir ce correctif). Retourne true si quelque chose a
    /// été supprimé.
    /// </summary>
    public static bool PruneAliasesCoveredByCatalog(Dictionary<string, string> userAliases)
    {
        var toRemove = userAliases.Keys.Where(k => KnownLocationAliases.ContainsKey(k) || IsJumpPointResolvable(k) || HumanizeNumberedSite(k) is not null).ToList();
        foreach (var key in toRemove) userAliases.Remove(key);
        return toRemove.Count > 0;
    }

    /// <summary>
    /// True si <paramref name="normalizedKey"/> (déjà passé par NormalizeForAliasLookup,
    /// donc séparé par des espaces) correspond au format d'un point de saut
    /// dont le système d'arrivée est connu — reconstruit la forme à
    /// underscore attendue par JumpPointIdRegex plutôt que de dupliquer le motif.
    /// </summary>
    private static bool IsJumpPointResolvable(string normalizedKey)
    {
        var jumpMatch = JumpPointIdRegex().Match(normalizedKey.Replace(' ', '_'));
        return jumpMatch.Success && SystemNames.ContainsKey(jumpMatch.Groups["sys2"].Value);
    }

    /// <summary>
    /// Fusionne dans <paramref name="userAliases"/> les entrées dont la clé
    /// ne diffère que par un numéro d'instance aléatoire final (6 chiffres
    /// ou plus, ex. "mission qt bounty beacon 816657711603") — chaque
    /// nouvelle rencontre du même lieu générique créait sa propre entrée
    /// avant que NormalizeForAliasLookup ne retire ce suffixe de la clé.
    /// Retourne true si quelque chose a changé.
    /// </summary>
    public static bool CollapseInstanceSuffixedAliases(Dictionary<string, string> userAliases)
    {
        var changed = false;
        foreach (var key in userAliases.Keys.ToList())
        {
            var collapsed = InstanceSuffixSpaceRegex().Replace(key, "");
            if (collapsed == key) continue;
            if (!userAliases.ContainsKey(collapsed))
                userAliases[collapsed] = userAliases[key];
            userAliases.Remove(key);
            changed = true;
        }
        return changed;
    }

    public static readonly IReadOnlyDictionary<string, string> SystemNames = new Dictionary<string, string>
    {
        ["arc"] = "ArcCorp",
        ["cru"] = "Crusader",
        ["hur"] = "Hurston",
        ["mic"] = "microTech",
        ["pyro"] = "Pyro",
        ["stan"] = "Stanton",
        ["nyx"] = "Nyx",
        ["magnus"] = "Magnus",
        ["terra"] = "Terra",
        ["castra"] = "Castra",
    };

    public static readonly IReadOnlyDictionary<string, string> StationByPlanet = new Dictionary<string, string>
    {
        ["hurston"] = "Everus Harbor",
        ["crusader"] = "Seraphim Station",
        ["arccorp"] = "Baijini Point",
        ["microtech"] = "Port Tressler",
    };

    /// <summary>Identifiants bruts partagés par plusieurs lieux réels différents (ex. "RestStop").</summary>
    public static readonly IReadOnlySet<string> AmbiguousSharedDestinationIds = new HashSet<string> { "reststop" };

    public static readonly IReadOnlyDictionary<string, string> LandingZoneToPlanet = new Dictionary<string, string>
    {
        ["lorville"] = "hurston",
        ["area18"] = "arccorp",
        ["orison"] = "crusader",
        ["newbabbage"] = "microtech",
    };

    public static string? ExtractShipName(string? rawId)
    {
        if (string.IsNullOrEmpty(rawId)) return rawId;
        var cleaned = InstanceSuffixLongRegex().Replace(rawId, "");
        return cleaned.Replace('_', ' ').Trim();
    }

    public static string NormalizeForAliasLookup(string rawId)
    {
        var s = Regex.Replace(rawId.ToLowerInvariant(), @"[_\-]+", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        // Retire un numéro d'instance aléatoire final (comme le fait déjà
        // HumanizeDestination pour le texte affiché) pour que deux
        // rencontres du même lieu générique partagent la même clé d'alias.
        return InstanceSuffixSpaceRegex().Replace(s, "");
    }

    private static string StripObjectContainerPrefix(string rawId) =>
        ObjectContainerPrefixRegex().Replace(rawId, "").Trim('_', ' ');

    /// <summary>
    /// Rend un identifiant de destination un peu plus prononçable à voix
    /// haute. Voir _humanize_destination (game_log_watcher.py) pour
    /// l'ordre de priorité complet (alias utilisateur, alias connu, point
    /// de saut, format OOC_..., repli générique).
    /// </summary>
    public static string? HumanizeDestination(string? rawId, IReadOnlyDictionary<string, string>? userAliases = null)
    {
        if (string.IsNullOrEmpty(rawId)) return rawId;

        var withoutOc = StripObjectContainerPrefix(rawId);
        var normalized = NormalizeKnownIdSynonyms(NormalizeForAliasLookup(withoutOc));

        if (userAliases is not null && userAliases.TryGetValue(normalized, out var custom) && !string.IsNullOrEmpty(custom))
            return custom;

        if (KnownLocationAliases.TryGetValue(normalized, out var alias))
            return alias;

        if (HumanizeNumberedSite(normalized) is { } site)
            return site;

        var jumpMatch = JumpPointIdRegex().Match(withoutOc.ToLowerInvariant());
        if (jumpMatch.Success)
        {
            if (SystemNames.TryGetValue(jumpMatch.Groups["sys2"].Value, out var systemName))
                return $"{systemName} Gateway";
            return "Endroit inconnu";
        }

        var oocMatch = OocLocationRegex().Match(withoutOc);
        if (oocMatch.Success)
            return $"{oocMatch.Groups["body"].Value} (système {oocMatch.Groups["system"].Value})";

        var cleaned = InstanceSuffixRegex().Replace(withoutOc, "");
        cleaned = MissionQtPrefixRegex().Replace(cleaned, "");
        return cleaned.Replace('_', ' ').Trim();
    }

    /// <summary>
    /// Clé normalisée d'un identifiant de destination brut, ou null s'il
    /// est vide, ambigu (partagé par plusieurs lieux), ou déjà aliasé par
    /// l'utilisateur.
    /// </summary>
    public static string? DestinationAliasKey(string? rawId, IReadOnlyDictionary<string, string>? userAliases = null)
    {
        if (string.IsNullOrEmpty(rawId)) return null;
        var withoutOc = StripObjectContainerPrefix(rawId);
        if (withoutOc.Length == 0) return null;
        var normalized = NormalizeKnownIdSynonyms(NormalizeForAliasLookup(withoutOc));
        if (AmbiguousSharedDestinationIds.Contains(normalized)) return null;
        if (userAliases is not null && userAliases.TryGetValue(normalized, out var existing) && !string.IsNullOrEmpty(existing))
            return null;
        return normalized;
    }

    /// <summary>True si rawId ne correspond à aucun mécanisme de reconnaissance connu.</summary>
    public static bool DestinationIsUnresolved(string? rawId, IReadOnlyDictionary<string, string>? userAliases = null)
    {
        if (string.IsNullOrEmpty(rawId)) return false;
        var withoutOc = StripObjectContainerPrefix(rawId);
        if (withoutOc.Length == 0) return false;
        var normalized = NormalizeKnownIdSynonyms(NormalizeForAliasLookup(withoutOc));
        if (AmbiguousSharedDestinationIds.Contains(normalized)) return false;
        if (userAliases is not null && userAliases.TryGetValue(normalized, out var existing) && !string.IsNullOrEmpty(existing))
            return false;
        if (KnownLocationAliases.ContainsKey(normalized)) return false;
        if (HumanizeNumberedSite(normalized) is not null) return false;

        var jumpMatch = JumpPointIdRegex().Match(withoutOc.ToLowerInvariant());
        if (jumpMatch.Success) return !SystemNames.ContainsKey(jumpMatch.Groups["sys2"].Value);

        if (OocLocationRegex().IsMatch(withoutOc)) return false;
        return true;
    }

    public static bool ObstructionLabelIsGenericGuess(string? obstructionLabel)
    {
        if (string.IsNullOrEmpty(obstructionLabel)) return false;
        var planetKey = Regex.Replace(obstructionLabel.Trim(), @"\s+", "").ToLowerInvariant();
        return StationByPlanet.ContainsKey(planetKey);
    }

    public static string? GuessPlanetStationFromStartLocation(string? startLocation)
    {
        if (string.IsNullOrEmpty(startLocation)) return null;
        var key = Regex.Replace(startLocation.Trim(), @"\s+", "").ToLowerInvariant();
        return LandingZoneToPlanet.TryGetValue(key, out var planet) && StationByPlanet.TryGetValue(planet, out var station)
            ? station : null;
    }

    /// <summary>
    /// Détermine le meilleur nom à annoncer pour une destination. Voir
    /// _resolve_destination_label (game_log_watcher.py) pour le détail
    /// des priorités.
    /// </summary>
    public static string? ResolveDestinationLabel(
        string? rawDestination, string? obstructionLabel = null,
        IReadOnlyDictionary<string, string>? userAliases = null, string? startLocation = null)
    {
        var withoutOc = StripObjectContainerPrefix(rawDestination ?? "");
        var normalized = withoutOc.Length > 0 ? NormalizeKnownIdSynonyms(NormalizeForAliasLookup(withoutOc)) : "";

        if (AmbiguousSharedDestinationIds.Contains(normalized))
        {
            if (!string.IsNullOrEmpty(obstructionLabel))
            {
                var label = obstructionLabel.Trim();
                var planetKey = Regex.Replace(label, @"\s+", "").ToLowerInvariant();
                return StationByPlanet.TryGetValue(planetKey, out var station) ? station : label;
            }
            var guessed = GuessPlanetStationFromStartLocation(startLocation);
            return guessed ?? withoutOc.Replace('_', ' ').Trim();
        }

        if (!string.IsNullOrEmpty(obstructionLabel))
        {
            var label = obstructionLabel.Trim();
            var planetKey = Regex.Replace(label, @"\s+", "").ToLowerInvariant();
            if (StationByPlanet.TryGetValue(planetKey, out var station))
            {
                if (userAliases is not null && !string.IsNullOrEmpty(rawDestination) &&
                    userAliases.TryGetValue(normalized, out var custom) && !string.IsNullOrEmpty(custom))
                    return custom;
                return station;
            }
            return label;
        }
        return HumanizeDestination(rawDestination, userAliases);
    }
}
