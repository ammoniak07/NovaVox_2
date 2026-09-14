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
    string? UnresolvedDestinationRawId,
    /// <summary>Nom de zone résolu (alias/HUD), uniquement pour un ZoneChange dont la zone a pu être identifiée — à afficher dans l'overlay (voir _overlay_set_zone côté Python : jamais écrasé par une zone inconnue).</summary>
    string? ResolvedZone = null);

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

    /// <summary>
    /// Marqueurs de mise en emphase du HUD ("&lt;EM4&gt;[SP]&lt;/EM4&gt;",
    /// "&lt;EM3&gt;[1000 xp]&lt;/EM3&gt;"...) accolés à la fin de beaucoup de
    /// notifications de contrat — toujours du bruit, jamais à lire à voix
    /// haute, et identiques d'une mission à l'autre : sans ce nettoyage,
    /// chaque mission finissait par une correction HUD manuelle rien que
    /// pour retirer ce même tag.
    /// </summary>
    [GeneratedRegex(@"<EM\d+>.*?</EM\d+>", RegexOptions.IgnoreCase)]
    private static partial Regex EmphasisTagRegex();

    /// <summary>
    /// Rapports de délit du HUD ("X a commis Y contre vous...") : seul le
    /// nom du joueur en tête de phrase change d'une rencontre à l'autre
    /// pour un même type de délit.
    /// </summary>
    [GeneratedRegex(@"^(?<name>\S+) a commis (?<rest>.+)$")]
    private static partial Regex CrimeReportRegex();

    [GeneratedRegex(@"^AMI AJOUTÉ ! (?<name>.+)$")]
    private static partial Regex FriendAddedRegex();

    [GeneratedRegex(@"^Calibration du voyage quantique démarrée par (?<name>.+)\.$")]
    private static partial Regex QuantumCalibrationStartedRegex();

    [GeneratedRegex(@"^Calibration du voyage quantique terminée par (?<name>.+)\.$")]
    private static partial Regex QuantumCalibrationFinishedRegex();

    // Notifications d'objectif/contrat : un préfixe fixe toujours identique,
    // suivi du nom de la mission/l'objectif du moment — c'est CE nom qui
    // change à chaque nouvelle mission, jamais le préfixe. Sans regrouper
    // par préfixe, chaque mission crée une correction HUD séparée pour
    // toujours (voir HudTemplates ci-dessous).
    [GeneratedRegex(@"^Nouvel objectif\s*:\s*(?<name>.+)$")]
    private static partial Regex NewObjectiveRegex();

    [GeneratedRegex(@"^Objectif terminé\s*:\s*(?<name>.+)$")]
    private static partial Regex ObjectiveCompletedRegex();

    [GeneratedRegex(@"^Objectif retiré\s*:\s*(?<name>.+)$")]
    private static partial Regex ObjectiveRemovedRegex();

    [GeneratedRegex(@"^CONTRAT PARTAGÉ\s*:\s*(?<name>.+)$")]
    private static partial Regex ContractSharedRegex();

    [GeneratedRegex(@"^Contrat accepté\s*:\s*(?<name>.+)$")]
    private static partial Regex ContractAcceptedRegex();

    [GeneratedRegex(@"^CONTRAT TERMINÉ\s*:\s*(?<name>.+)$")]
    private static partial Regex ContractCompletedRegex();

    [GeneratedRegex(@"^CONTRAT ÉCHOUÉ\s*:\s*(?<name>.+)$")]
    private static partial Regex ContractFailedRegex();

    [GeneratedRegex(@"^ENTRÉE DU JOURNAL AJOUTÉE\s*:\s*(?<name>.+)$")]
    private static partial Regex JournalEntryAddedRegex();

    // Canal de discussion d'un vaisseau ("CANAL 'Drake Cutter : Ammoniak'
    // rejoint.") : seul le nom du vaisseau change d'un vaisseau à l'autre —
    // le nom du pilote qui suit (généralement le joueur lui-même) n'a pas
    // d'intérêt à être annoncé et est donc purement ignoré ici (ni capturé,
    // ni reproduit dans le gabarit), plutôt que de faire partie d'un {name}
    // qui inclurait aussi bien le vaisseau que le pilote.
    [GeneratedRegex(@"^CANAL '(?<name>.+) : [^']+' rejoint\.$")]
    private static partial Regex ShipChannelJoinedRegex();

    [GeneratedRegex(@"^Vous avez quitté le CANAL '(?<name>.+) : [^']+'\.$")]
    private static partial Regex ShipChannelLeftRegex();

    /// <summary>
    /// Motifs de texte HUD connus où seule une partie variable (nom de
    /// joueur/pilote/ami/vaisseau, ou nom de mission/objectif) change d'une
    /// rencontre à l'autre pour un même type de notification — essayés
    /// dans l'ordre par ExtractHudTemplate. Étendre la détection revient à
    /// ajouter une entrée ici, sans toucher au reste du mécanisme
    /// (BuildHudAnnouncement, MergeLegacyNameTemplateOverrides).
    /// </summary>
    private static readonly (Regex Pattern, Func<Match, string> BuildTemplateKey)[] HudTemplates =
    {
        (CrimeReportRegex(), m => $"{{name}} a commis {m.Groups["rest"].Value}"),
        (FriendAddedRegex(), _ => "AMI AJOUTÉ ! {name}"),
        (QuantumCalibrationStartedRegex(), _ => "Calibration du voyage quantique démarrée par {name}."),
        (QuantumCalibrationFinishedRegex(), _ => "Calibration du voyage quantique terminée par {name}."),
        (NewObjectiveRegex(), _ => "Nouvel objectif : {name}"),
        (ObjectiveCompletedRegex(), _ => "Objectif terminé : {name}"),
        (ObjectiveRemovedRegex(), _ => "Objectif retiré : {name}"),
        (ContractSharedRegex(), _ => "CONTRAT PARTAGÉ : {name}"),
        (ContractAcceptedRegex(), _ => "Contrat accepté : {name}"),
        (ContractCompletedRegex(), _ => "CONTRAT TERMINÉ : {name}"),
        (ContractFailedRegex(), _ => "CONTRAT ÉCHOUÉ : {name}"),
        (JournalEntryAddedRegex(), _ => "ENTRÉE DU JOURNAL AJOUTÉE : {name}"),
        (ShipChannelJoinedRegex(), _ => "CANAL '{name}' rejoint."),
        (ShipChannelLeftRegex(), _ => "Vous avez quitté le CANAL '{name}'."),
    };

    /// <summary>Port de _clean_hud_notification_text.</summary>
    public static string CleanHudNotificationText(string? text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return "";
        text = EmphasisTagRegex().Replace(text, "");
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
            UnresolvedDestinationWarning: unresolvedWarning, UnresolvedDestinationRawId: unresolvedWarning ? aliasKey : null,
            ResolvedZone: isZone && hasDest ? resolved : null);
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

        var (templateKey, capturedValue) = ExtractHudTemplate(rawText);

        var isNew = false;
        if (!config.GameLogHudOverrides.ContainsKey(templateKey))
        {
            config.GameLogHudOverrides[templateKey] = templateKey;
            isNew = true;
        }

        var storedTemplate = config.GameLogHudOverrides.GetValueOrDefault(templateKey, templateKey);
        var spokenText = capturedValue is not null ? storedTemplate.Replace("{name}", capturedValue) : storedTemplate;

        const string key = "hud_notification";
        var text = GameLogPhraseCatalog.Format(key, config.GameLogPhrases, new Dictionary<string, string> { ["text"] = spokenText });
        var rawHudTextForLog = spokenText != rawText ? rawText : null;

        return new GameLogAnnouncement(
            key, text, GameLogPhraseCatalog.Emoji.GetValueOrDefault(key, ""), RawHudText: rawHudTextForLog,
            IsNewDestinationAlias: false, DestinationAliasKey: null,
            IsNewHudOverride: isNew, HudOverrideKey: isNew ? templateKey : null,
            UnresolvedDestinationWarning: false, UnresolvedDestinationRawId: null);
    }

    /// <summary>
    /// Remplace la partie variable détectée (nom de joueur/pilote/ami, ou
    /// nom de mission/objectif) dans un texte HUD connu (voir HudTemplates)
    /// par l'espace réservé {name}, pour qu'une seule correction de
    /// lecture couvre toutes les rencontres quelle que soit cette valeur —
    /// donc une seule entrée "Nouvel objectif : {name}" pour TOUTES les
    /// missions plutôt qu'une par mission. Tout autre texte HUD n'a pas de
    /// motif reconnu et reste inchangé (clé = son propre texte, comme avant).
    /// </summary>
    private static (string TemplateKey, string? CapturedValue) ExtractHudTemplate(string rawText)
    {
        foreach (var (pattern, buildTemplateKey) in HudTemplates)
        {
            var match = pattern.Match(rawText);
            if (match.Success) return (buildTemplateKey(match), match.Groups["name"].Value);
        }
        return (rawText, null);
    }

    /// <summary>
    /// Fusionne dans <paramref name="overrides"/> (AiConfig.GameLogHudOverrides)
    /// toute correction HUD enregistrée sous une forme dépassée : soit
    /// avec des tags de mise en emphase du HUD toujours présents dans la
    /// clé (avant l'introduction du nettoyage EmphasisTagRegex — même
    /// texte de contrat à chaque fois, mais un tag bruit en plus qui
    /// suffisait à en faire une clé différente), soit avec un nom de
    /// joueur, de mission/objectif ou de vaisseau inclus dans la clé
    /// (avant le regroupement par gabarit "{name}...", voir HudTemplates).
    /// Appelée au chargement de la config (AiConfigStore.Load) pour
    /// nettoyer les doublons déjà accumulés, en plus d'empêcher
    /// BuildHudAnnouncement d'en recréer de nouveaux.
    ///
    /// Quand plusieurs entrées héritées se regroupent sous la même clé
    /// canonique — typiquement un texte personnalisé DIFFÉREMMENT pour
    /// chaque vaisseau ("Bienvenue à bord du Hull C.", "... du Corsair.")
    /// plutôt qu'un texte par défaut identique à la clé — une seule peut
    /// survivre comme personnalisation commune à toutes les rencontres
    /// futures. Choisit alors en priorité une valeur qui contient bien le
    /// réservoir {name} (donc capable de réintégrer le vaisseau/nom réel à
    /// chaque nouvelle rencontre) ; si aucune n'en contient, retombe sur la
    /// forme par défaut plutôt que de figer arbitrairement le texte d'un
    /// seul vaisseau pour tous les autres, ce qui annoncerait alors
    /// systématiquement le mauvais vaisseau.
    ///
    /// Retourne true si quelque chose a changé — l'appelant
    /// (AiConfigStore.Load) doit alors réécrire tout de suite
    /// ai_config.json, sinon le fichier sur disque resterait avec les
    /// anciennes entrées tant qu'aucun autre réglage n'a déclenché de
    /// sauvegarde.
    /// </summary>
    public static bool MergeLegacyNameTemplateOverrides(Dictionary<string, string> overrides)
    {
        var changed = false;
        foreach (var rawKey in overrides.Keys.ToList())
        {
            var (templateKey, _) = ExtractHudTemplate(CleanHudNotificationText(rawKey));
            if (templateKey == rawKey) continue;

            var rawValue = overrides[rawKey];
            var currentValue = overrides.GetValueOrDefault(templateKey, templateKey);
            // Ne remplace la valeur en place que par une STRICTEMENT meilleure
            // (voir RankMergeCandidate) — sinon, sur une entrée déjà présente
            // avec une meilleure valeur, rawValue perdrait pour de bonnes
            // raisons mais la clé ne serait alors jamais créée du tout côté
            // "premier candidat rencontré" : on l'insère donc quand même une
            // fois, au pire avec la forme par défaut (jamais avec une valeur
            // pire que le défaut).
            if (RankMergeCandidate(rawValue, templateKey) > RankMergeCandidate(currentValue, templateKey))
                overrides[templateKey] = rawValue;
            else if (!overrides.ContainsKey(templateKey))
                overrides[templateKey] = currentValue;

            overrides.Remove(rawKey);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Départage plusieurs valeurs héritées candidates à devenir LA
    /// personnalisation commune d'une même clé canonique (voir
    /// MergeLegacyNameTemplateOverrides) : 2 = personnalisée ET réutilisable
    /// pour toute rencontre future (contient {name}, donc jamais figée sur
    /// une seule rencontre) ; 1 = pas encore personnalisée (valeur = clé,
    /// le repli sûr par défaut) ; 0 = personnalisée mais figée sur UNE seule
    /// rencontre passée (un vaisseau, un joueur...) — le pire cas, puisque
    /// la garder telle quelle annoncerait alors la même chose à tort pour
    /// toutes les autres rencontres, jamais préférée à la valeur par défaut.
    /// </summary>
    private static int RankMergeCandidate(string value, string templateKey) =>
        value != templateKey && value.Contains("{name}") ? 2 :
        value == templateKey ? 1 :
        0;
}
