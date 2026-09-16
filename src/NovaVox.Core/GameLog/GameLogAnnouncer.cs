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

    /// <summary>Trouve les réservoirs "{xxx}" littéralement présents dans une clé de gabarit — voir RankMergeCandidate.</summary>
    [GeneratedRegex(@"\{[A-Za-z0-9_]+\}")]
    private static partial Regex PlaceholderRegex();

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

    [GeneratedRegex(@"^Nouveau chef de groupe\s*:\s*(?<name>.+)$")]
    private static partial Regex NewGroupLeaderRegex();

    [GeneratedRegex(@"^Un joueur a rejoint (?<name>.+) a rejoint le Groupe\.$")]
    private static partial Regex PlayerJoinedGroupRegex();

    [GeneratedRegex(@"^A quitté le groupe\s*:\s*(?<name>.+) a quitté le Groupe$")]
    private static partial Regex PlayerLeftGroupRegex();

    // Rejoindre/quitter le canal d'un vaisseau EN PASSANT PAR un mouvement de
    // groupe ("Un joueur a rejoint Bistic a rejoint le CANAL 'RSI
    // Constellation Taurus : Tinou214'.") : contrairement à
    // ShipChannelJoinedRegex/ShipChannelLeftRegex (son propre vaisseau), TROIS
    // parties varient ici indépendamment — le membre qui bouge, le vaisseau,
    // et son pilote/propriétaire — d'où 3 réservoirs {member}/{ship}/{owner}
    // plutôt qu'un seul {name} (voir ExtractHudTemplate, qui gère
    // désormais un nombre quelconque de groupes nommés par motif).
    [GeneratedRegex(@"^Un joueur a rejoint (?<member>.+) a rejoint le CANAL '(?<ship>.+) : (?<owner>[^']+)'\.$")]
    private static partial Regex PlayerJoinedShipChannelViaGroupRegex();

    [GeneratedRegex(@"^A quitté le groupe\s*:\s*(?<member>.+) a quitté le CANAL '(?<ship>.+) : (?<owner>[^']+)'$")]
    private static partial Regex PlayerLeftShipChannelViaGroupRegex();

    [GeneratedRegex(@"^(?<name>.+) ! INVITATION À UN GROUPE REÇUE\s*:\s*Accepter l'invitation \?$")]
    private static partial Regex GroupInviteReceivedRegex();

    [GeneratedRegex(@"^Groupe\s*:\s*(?<name>.+) s'est connecté\.?$")]
    private static partial Regex GroupMemberConnectedRegex();

    [GeneratedRegex(@"^Groupe\s*:\s*(?<name>.+) s'est déconnecté\.?$")]
    private static partial Regex GroupMemberDisconnectedRegex();

    /// <summary>
    /// Motifs de texte HUD connus où une ou plusieurs parties variables (nom
    /// de joueur/pilote/ami/vaisseau, ou nom de mission/objectif) changent
    /// d'une rencontre à l'autre pour un même type de notification — essayés
    /// dans l'ordre par ExtractHudTemplate. Chaque groupe nommé du motif
    /// (autre que "rest", ci-dessous) devient un réservoir "{nomDuGroupe}"
    /// dans le texte annoncé, réintégré à chaque rencontre — un motif peut
    /// donc en définir plusieurs (voir PlayerJoinedShipChannelViaGroupRegex).
    /// Étendre la détection revient à ajouter une entrée ici, sans toucher
    /// au reste du mécanisme (BuildHudAnnouncement, MergeLegacyNameTemplateOverrides).
    /// </summary>
    private static readonly (Regex Pattern, Func<Match, string> BuildTemplateKey)[] HudTemplates =
    {
        // "rest" n'est PAS un réservoir substitué au moment de l'annonce : il
        // sert seulement ici à construire une clé de gabarit distincte par
        // type de délit (voir ExtractHudTemplate/NamedCaptures, qui l'ignore).
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
        (NewGroupLeaderRegex(), _ => "Nouveau chef de groupe : {name}"),
        (PlayerJoinedGroupRegex(), _ => "Un joueur a rejoint {name} a rejoint le Groupe."),
        (PlayerLeftGroupRegex(), _ => "A quitté le groupe : {name} a quitté le Groupe"),
        (PlayerJoinedShipChannelViaGroupRegex(), _ => "Un joueur a rejoint {member} a rejoint le CANAL '{ship} : {owner}'."),
        (PlayerLeftShipChannelViaGroupRegex(), _ => "A quitté le groupe : {member} a quitté le CANAL '{ship} : {owner}'"),
        (GroupInviteReceivedRegex(), _ => "{name} ! INVITATION À UN GROUPE REÇUE : Accepter l'invitation ?"),
        (GroupMemberConnectedRegex(), _ => "Groupe : {name} s'est connecté."),
        (GroupMemberDisconnectedRegex(), _ => "Groupe : {name} s'est déconnecté."),
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

        var (templateKey, captures) = ExtractHudTemplate(rawText);

        var isNew = false;
        if (!config.GameLogHudOverrides.ContainsKey(templateKey))
        {
            config.GameLogHudOverrides[templateKey] = templateKey;
            isNew = true;
        }

        var storedTemplate = config.GameLogHudOverrides.GetValueOrDefault(templateKey, templateKey);
        var spokenText = storedTemplate;
        foreach (var (placeholder, value) in captures)
            spokenText = spokenText.Replace($"{{{placeholder}}}", value);

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
    /// Remplace la ou les parties variables détectées (nom de joueur/pilote/
    /// ami/vaisseau, ou nom de mission/objectif) dans un texte HUD connu
    /// (voir HudTemplates) par leurs réservoirs "{xxx}", pour qu'une seule
    /// correction de lecture couvre toutes les rencontres quelles que
    /// soient ces valeurs — donc une seule entrée "Nouvel objectif : {name}"
    /// pour TOUTES les missions plutôt qu'une par mission, ou une seule
    /// entrée à trois réservoirs pour "Un joueur a rejoint {member} a
    /// rejoint le CANAL '{ship} : {owner}'." quel que soit le membre, le
    /// vaisseau ou son propriétaire. Tout autre texte HUD n'a pas de motif
    /// reconnu et reste inchangé (clé = son propre texte, comme avant).
    /// </summary>
    private static (string TemplateKey, IReadOnlyDictionary<string, string> Captures) ExtractHudTemplate(string rawText)
    {
        foreach (var (pattern, buildTemplateKey) in HudTemplates)
        {
            var match = pattern.Match(rawText);
            if (match.Success) return (buildTemplateKey(match), NamedCaptures(match));
        }
        return (rawText, new Dictionary<string, string>());
    }

    /// <summary>
    /// Tous les groupes NOMMÉS d'un Match, sauf "rest" (CrimeReportRegex) qui
    /// sert seulement à construire une clé de gabarit distincte par type de
    /// délit — jamais un réservoir "{rest}" réellement présent dans le texte
    /// annoncé, donc jamais à substituer ici.
    /// </summary>
    private static Dictionary<string, string> NamedCaptures(Match match) =>
        match.Groups.Cast<Group>()
            .Where(g => g.Success && g.Name != "rest" && !int.TryParse(g.Name, out _))
            .ToDictionary(g => g.Name, g => g.Value);

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
    /// pour toute rencontre future (contient TOUS les réservoirs "{xxx}" de
    /// templateKey — un gabarit à plusieurs réservoirs, voir
    /// PlayerJoinedShipChannelViaGroupRegex, doit tous les retrouver pour
    /// être sûr, pas juste un seul) ; 1 = pas encore personnalisée (valeur =
    /// clé, le repli sûr par défaut) ; 0 = personnalisée mais figée sur UNE
    /// seule rencontre passée (un vaisseau, un joueur...) — le pire cas,
    /// puisque la garder telle quelle annoncerait alors la même chose à
    /// tort pour toutes les autres rencontres, jamais préférée à la valeur
    /// par défaut.
    /// </summary>
    private static int RankMergeCandidate(string value, string templateKey)
    {
        if (value == templateKey) return 1;
        var placeholders = PlaceholderRegex().Matches(templateKey).Select(m => m.Value).Distinct().ToList();
        return placeholders.Count > 0 && placeholders.All(value.Contains) ? 2 : 0;
    }
}
