using System.Text.RegularExpressions;
using NovaVox.Core.Config;

namespace NovaVox.Core.GameLog;

/// <summary>
/// Résultat prêt à afficher/annoncer pour un GameLogEvent — port du corps
/// de Api._gamelog_announce (app.py), qui combine l'événement brut avec
/// les gabarits/alias/corrections personnalisés de l'utilisateur.
/// </summary>
public sealed record GameLogAnnouncement(
    string Key,
    string Text,
    string Emoji,
    /// <summary>Texte brut détecté dans le jeu, uniquement si différent du texte annoncé (correction de lecture appliquée) — affiché en plus dans le journal, jamais à la place.</summary>
    string? RawHudText,
    /// <summary>true si cet identifiant de destination vient d'être ajouté à AiConfig.GameLogDestinationAliases (première rencontre).</summary>
    bool IsNewDestinationAlias,
    string? DestinationAliasKey,
    /// <summary>true si ce texte HUD exact vient d'être ajouté à AiConfig.GameLogHudOverrides (première rencontre).</summary>
    bool IsNewHudOverride,
    string? HudOverrideKey,
    /// <summary>true si raw_id ne correspond à AUCUN mécanisme de résolution automatique connu (à signaler dans le journal).</summary>
    bool UnresolvedDestinationWarning,
    string? UnresolvedDestinationRawId);

/// <summary>
/// Port de Api._gamelog_announce / _maybe_register_destination_alias /
/// _register_hud_text_seen (app.py) : décide quoi annoncer pour un
/// évènement Game.log, et enregistre au passage (dans AiConfig, à
/// persister par l'appelant) toute nouvelle destination/notification HUD
/// jamais rencontrée, prête à être personnalisée depuis les réglages.
/// </summary>
public static partial class GameLogAnnouncer
{
    [GeneratedRegex(@"\s*:\s*$")]
    private static partial Regex TrailingColonRegex();

    [GeneratedRegex(@"\s*\n\s*")]
    private static partial Regex InternalNewlineRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();

    /// <summary>Port de _clean_hud_notification_text.</summary>
    public static string CleanHudNotificationText(string? text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return "";
        text = TrailingColonRegex().Replace(text, "");
        text = InternalNewlineRegex().Replace(text, " ");
        return MultiSpaceRegex().Replace(text, " ").Trim();
    }

    /// <summary>
    /// Construit l'annonce pour un évènement, ou null pour un évènement qui
    /// n'en produit pas (nickname_detected, watcher_started/error, texte
    /// HUD vide après nettoyage...). Mute directement les dictionnaires de
    /// <paramref name="config"/> lors d'une première rencontre — à
    /// l'appelant de persister la config si le résultat le signale.
    /// </summary>
    public static GameLogAnnouncement? Build(GameLogEvent evt, AiConfig config) => evt.Type switch
    {
        GameLogEventTypes.RouteSet => BuildDestinationAnnouncement(evt, config, "route_set", "route_set_no_dest", evt.Destination, isZone: false),
        GameLogEventTypes.JumpStart => BuildDestinationAnnouncement(evt, config, "jump_start", "jump_start_no_dest", evt.Destination, isZone: false),
        GameLogEventTypes.ZoneChange => BuildDestinationAnnouncement(evt, config, "zone_change", "zone_change_no_zone", evt.Zone, isZone: true),
        GameLogEventTypes.HudNotification => BuildHudAnnouncement(evt, config),
        _ => null,
    };

    private static GameLogAnnouncement? BuildDestinationAnnouncement(
        GameLogEvent evt, AiConfig config, string knownKey, string unknownKey, string? rawDestination, bool isZone)
    {
        var resolved = GameLogDestinations.ResolveDestinationLabel(rawDestination, evt.ObstructionLabel, config.GameLogDestinationAliases, evt.StartLocation);
        var hasDest = !string.IsNullOrEmpty(resolved);
        var key = hasDest ? knownKey : unknownKey;
        var args = hasDest
            ? new Dictionary<string, string> { [isZone ? "zone" : "dest"] = resolved! }
            : new Dictionary<string, string>();
        var text = GameLogPhraseCatalog.Format(key, config.GameLogPhrases, args);

        var (isNewAlias, aliasKey, unresolvedWarning) = MaybeRegisterDestinationAlias(rawDestination, resolved, evt.ObstructionLabel, config);

        return new GameLogAnnouncement(
            key, text, GameLogPhraseCatalog.Emoji.GetValueOrDefault(key, ""), RawHudText: null,
            IsNewDestinationAlias: isNewAlias, DestinationAliasKey: aliasKey,
            IsNewHudOverride: false, HudOverrideKey: null,
            UnresolvedDestinationWarning: unresolvedWarning, UnresolvedDestinationRawId: unresolvedWarning ? aliasKey : null);
    }

    /// <summary>Port de _maybe_register_destination_alias.</summary>
    private static (bool IsNew, string? Key, bool UnresolvedWarning) MaybeRegisterDestinationAlias(
        string? rawId, string? currentName, string? obstructionLabel, AiConfig config)
    {
        if (!string.IsNullOrEmpty(obstructionLabel) && !GameLogDestinations.ObstructionLabelIsGenericGuess(obstructionLabel))
            return (false, null, false);

        var key = GameLogDestinations.DestinationAliasKey(rawId, config.GameLogDestinationAliases);
        if (key is null || config.GameLogDestinationAliases.ContainsKey(key)) return (false, null, false);

        var unresolvedWarning = GameLogDestinations.DestinationIsUnresolved(rawId, config.GameLogDestinationAliases);
        config.GameLogDestinationAliases[key] = (currentName ?? "").Trim();
        return (true, key, unresolvedWarning);
    }

    /// <summary>Port de la branche hud_notification de _gamelog_announce + _register_hud_text_seen.</summary>
    private static GameLogAnnouncement? BuildHudAnnouncement(GameLogEvent evt, AiConfig config)
    {
        var rawText = CleanHudNotificationText(evt.Text);
        if (rawText.Length == 0) return null;

        var trimmedKey = rawText.Trim();
        var isNew = false;
        if (!config.GameLogHudOverrides.ContainsKey(trimmedKey))
        {
            config.GameLogHudOverrides[trimmedKey] = rawText;
            isNew = true;
        }

        var spokenText = config.GameLogHudOverrides.GetValueOrDefault(trimmedKey, rawText);
        const string key = "hud_notification";
        var text = GameLogPhraseCatalog.Format(key, config.GameLogPhrases, new Dictionary<string, string> { ["text"] = spokenText });
        var rawHudTextForLog = spokenText != rawText ? rawText : null;

        return new GameLogAnnouncement(
            key, text, GameLogPhraseCatalog.Emoji.GetValueOrDefault(key, ""), RawHudText: rawHudTextForLog,
            IsNewDestinationAlias: false, DestinationAliasKey: null,
            IsNewHudOverride: isNew, HudOverrideKey: isNew ? trimmedKey : null,
            UnresolvedDestinationWarning: false, UnresolvedDestinationRawId: null);
    }
}
