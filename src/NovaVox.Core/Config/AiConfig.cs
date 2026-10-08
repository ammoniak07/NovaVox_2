using System.Text.Json.Nodes;
using NovaVox.Core.GameLog;
using static NovaVox.Core.Json.JsonHelpers;

namespace NovaVox.Core.Config;

public sealed class AiConfig
{
    public static readonly string[] SupportedLanguages = { "fr", "en", "nl", "es", "it", "de" };
    public const string DefaultUiLanguage = "fr";
    public const double DefaultTriggerCooldown = 3.0;
    public const double DefaultPiperLengthScale = 1.0;
    public const double DefaultPiperNoiseScale = 0.667;
    public const bool DefaultRadioEffect = false;
    public const string DefaultFrenchNumberStyle = "france";
    public const string DefaultGeminiModel = "gemini-3.6-flash";
    public const string DefaultGeminiName = "Gemini";
    public const string DefaultResponseLength = "normal";
    public const string DefaultUiTheme = "dark";
    /// <summary>Palettes disponibles pour le sélecteur de thème (en-tête) — une ResourceDictionary par id dans NovaVox.App/Resources (Theme.<Id>.xaml, voir ThemeManager).</summary>
    public static readonly string[] ValidThemes = { "dark", "light", "military", "cyberpunk", "amber", "ocean" };

    public string UiLanguage { get; set; } = DefaultUiLanguage;
    /// <summary>Thème de couleurs de l'interface WPF — un des id de <see cref="ValidThemes"/> (sélecteur d'en-tête, n'existait pas côté Python, ajout propre à ce portage).</summary>
    public string UiTheme { get; set; } = DefaultUiTheme;
    /// <summary>Affiche ou masque la carte "Journal système" (colonne droite) de la fenêtre principale — n'existait pas côté Python, ajout propre à ce portage.</summary>
    public bool ShowSystemLog { get; set; } = true;
    public string? Voice { get; set; }
    public bool ConfirmCommands { get; set; }
    public double TriggerCooldown { get; set; } = DefaultTriggerCooldown;
    public string UserName { get; set; } = "";
    public string? PiperVoice { get; set; }
    public double PiperLengthScale { get; set; } = DefaultPiperLengthScale;
    public double PiperNoiseScale { get; set; } = DefaultPiperNoiseScale;
    public bool RadioEffect { get; set; } = DefaultRadioEffect;
    /// <summary>"france" (soixante-dix/quatre-vingt-dix) ou "belgique" (septante/nonante) — voir FrenchNumberExpander.</summary>
    public string FrenchNumberStyle { get; set; } = DefaultFrenchNumberStyle;
    public bool GameLogEnabled { get; set; }
    public bool GameLogAnnounceEvents { get; set; } = true;
    public string GameLogPlayerHandle { get; set; } = "";
    /// <summary>Chemin manuel vers Game.log — vide par défaut (détection automatique, voir GameLogPaths.FindGameLogPath), à renseigner quand Star Citizen est installé ailleurs qu'un des emplacements standards.</summary>
    public string GameLogCustomPath { get; set; } = "";
    /// <summary>Dossier "logbackups" manuel — vide par défaut (dérivé automatiquement du Game.log courant, voir GameLogBackups.FindBackupsFolder), à renseigner si les archives sont ailleurs (ex. copiées sur un autre disque).</summary>
    public string GameLogBackupsCustomPath { get; set; } = "";
    public Dictionary<string, string> GameLogPhrases { get; set; } = new();
    public Dictionary<string, string> GameLogHudOverrides { get; set; } = new();
    public Dictionary<string, string> GameLogDestinationAliases { get; set; } = new();
    public bool GeminiEnabled { get; set; } = true;
    /// <summary>Recherche de contexte sur le wiki Star Citizen (starcitizen.tools) avant de répondre — pertinent seulement pour ce jeu, décoché automatiquement par le sélecteur "Mode de jeu" (en-tête) sur "Autre jeu".</summary>
    public bool GeminiWikiEnabled { get; set; } = true;
    public string GeminiApiKey { get; set; } = "";
    public string GeminiModel { get; set; } = DefaultGeminiModel;
    public string GeminiName { get; set; } = DefaultGeminiName;
    public string GeminiResponseLength { get; set; } = DefaultResponseLength;
    public string GeminiCustomContext { get; set; } = "";
    public int GeminiRequestCount { get; set; }
    public string GeminiRequestDay { get; set; } = "";
    /// <summary>Aide-mémoire vaisseaux (Réglages > 🚀 Vaisseaux) : nom de vaisseau -> repère -> description (ex. "Tourelle dorsale" -> "sur le dessus, accès par l'échelle centrale"). Le vaisseau affiché dans l'overlay est désigné par ActiveShipCheatSheet.</summary>
    public Dictionary<string, Dictionary<string, string>> ShipCheatSheets { get; set; } = new();
    /// <summary>Couleur (hex, ex. "#2DD4FF") de chaque repère de ShipCheatSheets, indépendante d'un repère à l'autre — nom de vaisseau -> repère -> couleur. Une entrée absente (repère jamais recoloré, ou config antérieure à cette fonctionnalité) retombe sur ShipCheatSheetPointRowVm.DefaultColor côté UI/overlay.</summary>
    public Dictionary<string, Dictionary<string, string>> ShipCheatSheetColors { get; set; } = new();
    /// <summary>Nom du vaisseau (clé de ShipCheatSheets) actuellement affiché dans l'overlay — vide ou absent de ShipCheatSheets = rien affiché.</summary>
    public string ActiveShipCheatSheet { get; set; } = "";
    /// <summary>Schémas de fabrication reçus (Réglages > 📐 Schémas) — ajoutés automatiquement à la détection d'une notification HUD "Schémas reçu : {nom}" dans le Game.log (voir GameLogAnnouncer), ou manuellement depuis le panneau.</summary>
    public List<string> SchemasReceived { get; set; } = new();
    public HashSet<string> SchemasUnseen { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Date de départ (format "yyyy-MM-dd", vide = aucun filtre) pour "🔍 Scanner les archives" (panneau "📐 Schémas") : ignore toute notification antérieure — utile après un wipe des schémas en jeu, pour ne recharger que ce qui a été obtenu depuis. Voir GameLogBackups.ScanForReceivedSchemas.</summary>
    public string SchemaScanStartDate { get; set; } = "";
    /// <summary>Temps total passé (secondes) dans chaque vaisseau, nom de vaisseau -> secondes — voir ShipTimeTracker (suivi en direct, panneau "📊 Statistiques") et GameLogBackups.ScanForStats (complété depuis les archives).</summary>
    public Dictionary<string, double> ShipTimeSeconds { get; set; } = new();
    /// <summary>Temps de jeu total (secondes), déduit de la dernière activité connue du Game.log (voir GameLogState.LastLineTimestamp) plutôt que de l'horloge de la machine — n'avance que tant que le jeu écrit réellement dans son journal. Panneau "📊 Statistiques".</summary>
    public double PlayTimeSeconds { get; set; }
    /// <summary>Total aUEC envoyés à d'autres joueurs, cumulé depuis les notifications HUD "Vous avez envoyé : ... aUEC" (voir GameLogAnnouncer.TryExtractAuecSent). Panneau "📊 Statistiques".</summary>
    public double AuecSent { get; set; }
    /// <summary>Nombre de visites par destination (nom résolu, voir GameLogDestinations.ResolveDestinationLabel), déduit des changements de zone (ZoneChange). Panneau "📊 Statistiques".</summary>
    public Dictionary<string, int> DestinationVisitCounts { get; set; } = new();
    /// <summary>Nombre de sessions de jeu où chaque pseudo a été dans le même groupe que le joueur local (voir GameLogAnnouncer.TryExtractGroupMember) — classement "joueurs les plus groupés", panneau "📊 Statistiques".</summary>
    public Dictionary<string, int> GroupPlayerCounts { get; set; } = new();
    /// <summary>Noms des archives Game.log déjà prises en compte dans les statistiques ci-dessus (voir GameLogBackups.ScanForStats/GameLogBackupStatsResult) — une archive une fois roulée par le jeu n'est jamais réécrite, donc son nom suffit à ne jamais la recompter sur un scan ultérieur.</summary>
    public HashSet<string> StatsScannedBackupFiles { get; set; } = new();
    public List<TimeInterval> StatsLiveIntervals { get; set; } = new();
    /// <summary>
    /// Pseudos actuellement connus comme connectés au groupe (voir
    /// GameLogAnnouncer.BuildHudAnnouncement) — vérifié en vrai Game.log
    /// (remontée utilisateur, 02/10/2026) : dès qu'UN membre rejoint/quitte
    /// le groupe, le jeu réémet une notification HUD "Groupe : {nom} s'est
    /// connecté." pour TOUS les membres déjà connus comme connectés, pas
    /// seulement celui qui vient de bouger — sans ce suivi, chaque
    /// changement de composition du groupe réannonçait à voix haute tous
    /// les membres déjà annoncés. Un pseudo déjà présent ici n'est donc
    /// réannoncé "connecté" que s'il a d'abord été retiré par un "s'est
    /// déconnecté" correspondant (vraie déconnexion, ou lui-même supprimé
    /// une fois s'il n'y figurait pas déjà — jamais réannoncé en double).
    /// </summary>
    public HashSet<string> ConnectedGroupMembers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Port de load_ai_config/save_ai_config (app.py). Les ensembles de valeurs
/// valides qui appartiennent à d'autres sous-systèmes (modèles Gemini
/// connus, voix Piper connues, longueurs de réponse) sont passés en
/// paramètre plutôt que dupliqués ici, pour rester la seule source de
/// vérité une fois ces sous-systèmes portés.
/// </summary>
public sealed class AiConfigStore
{
    private readonly string _path;

    public AiConfigStore(string baseDir)
    {
        _path = Path.Combine(baseDir, "ai_config.json");
    }

    public AiConfig Load(
        IReadOnlyCollection<string>? knownGeminiModels = null,
        IReadOnlyCollection<string>? knownPiperVoices = null,
        IReadOnlyCollection<string>? knownResponseLengths = null)
    {
        var config = new AiConfig();
        if (!File.Exists(_path)) return config;
        var hudOverridesMigrated = false;
        var destinationAliasesMigrated = false;
        var schemasMigrated = false;
        try
        {
            var data = ParseFile(_path) as JsonObject;
            if (data is null) return config;

            config.UiLanguage = GetStringOrNull(data["ui_language"]) is { } lang && AiConfig.SupportedLanguages.Contains(lang)
                ? lang : AiConfig.DefaultUiLanguage;
            config.UiTheme = GetStringOrNull(data["ui_theme"]) is { } theme && AiConfig.ValidThemes.Contains(theme)
                ? theme : AiConfig.DefaultUiTheme;
            config.ShowSystemLog = GetBool(data["ui_show_system_log"], true);
            config.Voice = GetStringOrNull(data["voice"]);
            config.ConfirmCommands = GetBool(data["confirm_commands"]);
            config.TriggerCooldown = GetDouble(data["trigger_cooldown"]) ?? AiConfig.DefaultTriggerCooldown;
            config.UserName = GetString(data["user_name"]);

            var piperVoice = GetStringOrNull(data["piper_voice"]);
            config.PiperVoice = knownPiperVoices is null || (piperVoice is not null && knownPiperVoices.Contains(piperVoice))
                ? piperVoice : null;

            config.PiperLengthScale = Math.Clamp(
                GetDouble(data["piper_length_scale"]) ?? AiConfig.DefaultPiperLengthScale, 0.5, 2.0);
            config.PiperNoiseScale = Math.Clamp(
                GetDouble(data["piper_noise_scale"]) ?? AiConfig.DefaultPiperNoiseScale, 0.0, 1.5);
            config.RadioEffect = GetBool(data["radio_effect"], AiConfig.DefaultRadioEffect);
            config.FrenchNumberStyle = GetStringOrNull(data["french_number_style"]) == "belgique" ? "belgique" : AiConfig.DefaultFrenchNumberStyle;
            config.GeminiEnabled = GetBool(data["gemini_enabled"], true);
            config.GeminiWikiEnabled = GetBool(data["gemini_wiki_enabled"], true);
            config.GameLogEnabled = GetBool(data["game_log_enabled"]);
            config.GameLogAnnounceEvents = GetBool(data["game_log_announce_events"], true);
            config.GameLogPlayerHandle = GetString(data["game_log_player_handle"]).Trim();
            config.GameLogCustomPath = GetString(data["game_log_custom_path"]).Trim();
            config.GameLogBackupsCustomPath = GetString(data["game_log_backups_custom_path"]).Trim();
            config.GameLogPhrases = ToStringDict(data["game_log_phrases"] as JsonObject);
            config.GameLogHudOverrides = ToStringDict(data["game_log_hud_overrides"] as JsonObject);
            // Nettoie les corrections HUD enregistrées avant le regroupement par
            // gabarit ({name}...) — sans ça, une correction faite du temps où la
            // clé était le texte brut (nom de joueur ou de mission inclus) reste
            // un doublon séparé pour toujours, jamais fusionné avec la forme
            // canonique. Persisté tout de suite (pas seulement en mémoire) :
            // sinon ai_config.json sur disque garde les anciennes entrées tant
            // qu'aucun autre réglage n'a par ailleurs déclenché une sauvegarde.
            hudOverridesMigrated = GameLogAnnouncer.MergeLegacyNameTemplateOverrides(config.GameLogHudOverrides);
            config.GameLogDestinationAliases = ToStringDict(data["game_log_destination_aliases"] as JsonObject);
            // Nettoie les alias de destinations qui ne faisaient que dupliquer un
            // lieu déjà dans le catalogue intégré ou résoluble par l'algorithme
            // de point de saut (GameLogDestinations.KnownLocationAliases /
            // PruneAliasesCoveredByCatalog) — accumulés avant que
            // MaybeRegisterDestinationAlias n'arrête d'en recréer pour ces
            // lieux-là. Fusionne aussi les doublons qui ne différaient que par
            // un numéro d'instance aléatoire final (CollapseInstanceSuffixedAliases).
            // Ne PAS court-circuiter avec ||: les deux doivent s'exécuter.
            // Persisté tout de suite, comme la fusion des corrections HUD ci-dessus.
            var destinationAliasesPruned = GameLogDestinations.PruneAliasesCoveredByCatalog(config.GameLogDestinationAliases);
            var destinationAliasesCollapsed = GameLogDestinations.CollapseInstanceSuffixedAliases(config.GameLogDestinationAliases);
            destinationAliasesMigrated = destinationAliasesPruned || destinationAliasesCollapsed;

            config.GeminiApiKey = GetString(data["gemini_api_key"]).Trim();
            var model = GetString(data["gemini_model"]).Trim();
            if (model.Length == 0) model = AiConfig.DefaultGeminiModel;
            if (knownGeminiModels is not null && !knownGeminiModels.Contains(model))
                model = AiConfig.DefaultGeminiModel; // modèle retiré/renommé côté Google depuis
            config.GeminiModel = model;

            var name = GetString(data["gemini_name"]).Trim();
            config.GeminiName = name.Length == 0 ? AiConfig.DefaultGeminiName : name;

            var responseLength = GetStringOrNull(data["gemini_response_length"]);
            config.GeminiResponseLength = knownResponseLengths is null || (responseLength is not null && knownResponseLengths.Contains(responseLength))
                ? responseLength ?? AiConfig.DefaultResponseLength : AiConfig.DefaultResponseLength;

            config.GeminiCustomContext = GetString(data["gemini_custom_context"]);
            config.GeminiRequestCount = Math.Max(0, GetInt(data["gemini_request_count"]) ?? 0);
            config.GeminiRequestDay = GetString(data["gemini_request_day"]).Trim();

            config.ShipCheatSheets = ToNestedStringDict(data["ship_cheat_sheets"] as JsonObject);
            config.ShipCheatSheetColors = ToNestedStringDict(data["ship_cheat_sheet_colors"] as JsonObject);
            config.ActiveShipCheatSheet = GetString(data["active_ship_cheat_sheet"]).Trim();

            config.SchemasReceived = ToStringList(data["schemas_received"] as JsonArray);
            // Nettoie les noms enregistrés avant que le scan rétroactif des
            // archives Game.log (GameLogBackups) n'applique le même
            // nettoyage que la détection en direct — voir
            // MigrateLegacySchemaNames pour le symptôme (balises d'emphase
            // ou ":" final jamais retirés, donc plus aucune correspondance
            // avec la base locale de schémas : nom affiché brut, sans
            // fabricant ni description). Persisté tout de suite, comme les
            // deux migrations ci-dessus.
            schemasMigrated = GameLogAnnouncer.MigrateLegacySchemaNames(config.SchemasReceived);

            config.SchemasUnseen = new HashSet<string>(
                ToStringList(data["schemas_unseen"] as JsonArray), StringComparer.OrdinalIgnoreCase);

            var scanStartDate = GetString(data["schema_scan_start_date"]).Trim();
            config.SchemaScanStartDate = DateOnly.TryParse(scanStartDate, System.Globalization.CultureInfo.InvariantCulture, out _) ? scanStartDate : "";

            config.ShipTimeSeconds = ToStringDoubleDict(data["ship_time_seconds"] as JsonObject);
            config.PlayTimeSeconds = Math.Max(0, GetDouble(data["play_time_seconds"]) ?? 0);
            config.AuecSent = Math.Max(0, GetDouble(data["auec_sent"]) ?? 0);
            config.DestinationVisitCounts = ToStringIntDict(data["destination_visit_counts"] as JsonObject);
            config.GroupPlayerCounts = ToStringIntDict(data["group_player_counts"] as JsonObject);
            // Renommé de "ship_time_scanned_backup_files" : ce même ensemble couvre
            // désormais toutes les statistiques du scan d'archives (temps par
            // vaisseau, temps de jeu, aUEC, destinations), pas seulement les
            // vaisseaux — lu depuis les deux noms pour ne pas perdre une
            // progression de scan déjà enregistrée sous l'ancien nom.
            config.StatsScannedBackupFiles = ToStringList(data["stats_scanned_backup_files"] as JsonArray).ToHashSet();
            if (config.StatsScannedBackupFiles.Count == 0)
                config.StatsScannedBackupFiles = ToStringList(data["ship_time_scanned_backup_files"] as JsonArray).ToHashSet();
            config.StatsLiveIntervals = ToIntervals(data["stats_live_intervals"] as JsonArray);
            config.ConnectedGroupMembers = new HashSet<string>(
                ToStringList(data["connected_group_members"] as JsonArray), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new AiConfig();
        }
        if (hudOverridesMigrated || destinationAliasesMigrated || schemasMigrated) Save(config);
        return config;
    }

    public void Save(AiConfig config)
    {
        var data = new JsonObject
        {
            ["ui_language"] = config.UiLanguage,
            ["ui_theme"] = config.UiTheme,
            ["ui_show_system_log"] = config.ShowSystemLog,
            ["voice"] = config.Voice,
            ["confirm_commands"] = config.ConfirmCommands,
            ["trigger_cooldown"] = config.TriggerCooldown,
            ["user_name"] = config.UserName,
            ["piper_voice"] = config.PiperVoice,
            ["piper_length_scale"] = config.PiperLengthScale,
            ["piper_noise_scale"] = config.PiperNoiseScale,
            ["radio_effect"] = config.RadioEffect,
            ["french_number_style"] = config.FrenchNumberStyle,
            ["game_log_enabled"] = config.GameLogEnabled,
            ["game_log_announce_events"] = config.GameLogAnnounceEvents,
            ["game_log_player_handle"] = config.GameLogPlayerHandle,
            ["game_log_custom_path"] = config.GameLogCustomPath,
            ["game_log_backups_custom_path"] = config.GameLogBackupsCustomPath,
            ["game_log_phrases"] = FromStringDict(config.GameLogPhrases),
            ["game_log_hud_overrides"] = FromStringDict(config.GameLogHudOverrides),
            ["game_log_destination_aliases"] = FromStringDict(config.GameLogDestinationAliases),
            ["gemini_enabled"] = config.GeminiEnabled,
            ["gemini_wiki_enabled"] = config.GeminiWikiEnabled,
            ["gemini_api_key"] = config.GeminiApiKey,
            ["gemini_model"] = config.GeminiModel,
            ["gemini_name"] = config.GeminiName,
            ["gemini_response_length"] = config.GeminiResponseLength,
            ["gemini_custom_context"] = config.GeminiCustomContext,
            ["gemini_request_count"] = config.GeminiRequestCount,
            ["gemini_request_day"] = config.GeminiRequestDay,
            ["ship_cheat_sheets"] = FromNestedStringDict(config.ShipCheatSheets),
            ["ship_cheat_sheet_colors"] = FromNestedStringDict(config.ShipCheatSheetColors),
            ["active_ship_cheat_sheet"] = config.ActiveShipCheatSheet,
            ["schemas_received"] = FromStringList(config.SchemasReceived),
            ["schemas_unseen"] = FromStringList(config.SchemasUnseen.ToList()),
            ["schema_scan_start_date"] = config.SchemaScanStartDate,
            ["ship_time_seconds"] = FromStringDoubleDict(config.ShipTimeSeconds),
            ["play_time_seconds"] = config.PlayTimeSeconds,
            ["auec_sent"] = config.AuecSent,
            ["destination_visit_counts"] = FromStringIntDict(config.DestinationVisitCounts),
            ["group_player_counts"] = FromStringIntDict(config.GroupPlayerCounts),
            ["stats_scanned_backup_files"] = FromStringList(config.StatsScannedBackupFiles.ToList()),
            ["stats_live_intervals"] = FromIntervals(config.StatsLiveIntervals),
            ["connected_group_members"] = FromStringList(config.ConnectedGroupMembers.ToList()),
        };
        File.WriteAllText(_path, data.ToJsonString(WriteOptions));
    }

    private static Dictionary<string, string> ToStringDict(JsonObject? obj)
    {
        var result = new Dictionary<string, string>();
        if (obj is null) return result;
        foreach (var (key, value) in obj)
            result[key] = GetString(value);
        return result;
    }

    private static JsonObject FromStringDict(Dictionary<string, string> dict)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in dict)
            obj[key] = value;
        return obj;
    }

    private static Dictionary<string, double> ToStringDoubleDict(JsonObject? obj)
    {
        var result = new Dictionary<string, double>();
        if (obj is null) return result;
        foreach (var (key, value) in obj)
        {
            var seconds = GetDouble(value);
            if (seconds is > 0) result[key] = seconds.Value;
        }
        return result;
    }

    private static JsonObject FromStringDoubleDict(Dictionary<string, double> dict)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in dict)
            obj[key] = value;
        return obj;
    }

    private static Dictionary<string, int> ToStringIntDict(JsonObject? obj)
    {
        var result = new Dictionary<string, int>();
        if (obj is null) return result;
        foreach (var (key, value) in obj)
        {
            var count = GetInt(value);
            if (count is > 0) result[key] = count.Value;
        }
        return result;
    }

    private static JsonObject FromStringIntDict(Dictionary<string, int> dict)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in dict)
            obj[key] = value;
        return obj;
    }

    private static Dictionary<string, Dictionary<string, string>> ToNestedStringDict(JsonObject? obj)
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        if (obj is null) return result;
        foreach (var (key, value) in obj)
            result[key] = ToStringDict(value as JsonObject);
        return result;
    }

    private static JsonObject FromNestedStringDict(Dictionary<string, Dictionary<string, string>> dict)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in dict)
            obj[key] = FromStringDict(value);
        return obj;
    }

    private static List<TimeInterval> ToIntervals(JsonArray? array)
    {
        var result = new List<TimeInterval>();
        if (array is null) return result;
        foreach (var item in array)
        {
            if (item is not JsonObject obj) continue;
            if (DateTimeOffset.TryParse(GetString(obj["start"]), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var start)
                && DateTimeOffset.TryParse(GetString(obj["end"]), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var end))
                result.Add(new TimeInterval(start, end));
        }
        return result;
    }

    private static JsonArray FromIntervals(List<TimeInterval> intervals)
    {
        var array = new JsonArray();
        foreach (var interval in intervals)
            array.Add(new JsonObject
            {
                ["start"] = interval.Start.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["end"] = interval.End.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            });
        return array;
    }

    private static List<string> ToStringList(JsonArray? array)
    {
        var result = new List<string>();
        if (array is null) return result;
        foreach (var item in array)
        {
            var value = GetStringOrNull(item);
            if (!string.IsNullOrEmpty(value)) result.Add(value);
        }
        return result;
    }

    private static JsonArray FromStringList(List<string> list)
    {
        // JsonValue.Create(value) (overload dédiée string), PAS array.Add(value) directement
        // -- ce dernier résout vers JsonArray.Add<T>(T) (générique), qui construit la valeur
        // via le chemin générique JsonValue.Create<T> plutôt que l'overload dédiée "string" :
        // ce chemin générique exige un TypeInfoResolver sur JsonSerializerOptions (absent ici,
        // voir WriteOptions) même pour un type aussi simple qu'une string, et plantait Save()
        // dès qu'une LISTE de chaînes (SchemasReceived, StatsScannedBackupFiles,
        // ConnectedGroupMembers...) contenait ne serait-ce qu'un élément -- jamais remarqué
        // jusqu'ici faute de test exerçant cette sauvegarde avec un contenu réel (voir
        // ConfigStoreTests.AiConfig_ConnectedGroupMembersRoundTripsAndIgnoresCase).
        var array = new JsonArray();
        foreach (var value in list)
            array.Add(JsonValue.Create(value));
        return array;
    }
}
