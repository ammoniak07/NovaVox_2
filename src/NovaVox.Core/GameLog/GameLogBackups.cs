namespace NovaVox.Core.GameLog;

/// <summary>
/// Résultat d'un scan GameLogBackups.ScanForStats : les statistiques
/// trouvées sur les fichiers effectivement parcourus cette fois-ci, et les
/// noms de ces fichiers — à fusionner/persister par l'appelant (AiConfig,
/// panneau "📊 Statistiques"), pour qu'un second scan ne recompte jamais
/// une archive déjà prise en compte (voir AiConfig.StatsScannedBackupFiles).
/// </summary>
public sealed record GameLogBackupStatsResult(
    IReadOnlyDictionary<string, double> ShipSecondsByShip,
    double PlayTimeSeconds,
    double AuecSent,
    IReadOnlyDictionary<string, int> DestinationVisitCounts,
    IReadOnlyList<string> ScannedFileNames);

/// <summary>
/// Scan rétroactif des archives Game.log ("logbackups", sessions passées
/// roulées automatiquement par le jeu lui-même au lancement) pour y
/// retrouver des notifications "Schémas reçu : {nom}" manquées — NovaVox
/// pas encore lancé, ou lancé après l'obtention du schéma. Complète la
/// détection en direct de GameLogWatcher/GameLogAnnouncer, qui ne voit
/// que les lignes écrites pendant que l'appli tourne.
/// </summary>
public static class GameLogBackups
{
    /// <summary>
    /// Dossier "logbackups", voisin du Game.log en cours (même
    /// installation, même canal LIVE/PTU/EPTU) — null si le Game.log
    /// lui-même est introuvable ou si ce dossier n'existe pas (aucune
    /// archive encore produite, ou jeu jamais relancé depuis
    /// l'installation).
    /// </summary>
    public static string? FindBackupsFolder(string? liveGameLogPath = null)
    {
        var logPath = liveGameLogPath ?? GameLogPaths.FindGameLogPath();
        if (logPath is null) return null;

        var dir = Path.GetDirectoryName(logPath);
        if (string.IsNullOrEmpty(dir)) return null;

        var backups = Path.Combine(dir, "logbackups");
        return Directory.Exists(backups) ? backups : null;
    }

    /// <summary>
    /// Dossier "logbackups" à utiliser pour le scan (Réglages > 🛰 Game.log
    /// > "Emplacement du dossier logbackups") : priorité à
    /// <paramref name="customBackupsPath"/> s'il est renseigné (archives
    /// copiées ailleurs, ex. sur un autre disque — n'a alors plus besoin
    /// d'être voisin du Game.log), sinon dérivé automatiquement via
    /// <see cref="FindBackupsFolder"/>. Un chemin manuel renseigné mais
    /// introuvable (dossier déplacé/supprimé depuis) retourne null plutôt
    /// que de retomber silencieusement sur la détection automatique — le
    /// réglage explicite de l'utilisateur ne doit jamais être court-circuité
    /// sans qu'il le sache.
    /// </summary>
    public static string? ResolveBackupsFolder(string? customBackupsPath, string? liveGameLogPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customBackupsPath))
            return Directory.Exists(customBackupsPath) ? customBackupsPath : null;

        return FindBackupsFolder(liveGameLogPath);
    }

    /// <summary>
    /// Parcourt tous les .log de <paramref name="backupsFolder"/> et
    /// retourne, dans l'ordre de première rencontre, les noms de schémas
    /// détectés — SANS dédoublonnage contre AiConfig.SchemasReceived (à la
    /// charge de l'appelant, qui connaît la liste déjà enregistrée). Un
    /// fichier illisible (verrouillé par le jeu, corrompu) est simplement
    /// ignoré plutôt que de faire échouer tout le scan.
    /// </summary>
    /// <param name="backupsFolder">Dossier "logbackups" à scanner.</param>
    /// <param name="progress">
    /// Optionnel : notifié après chaque fichier (fichiers traités, total) —
    /// permet à l'appelant (UI) d'afficher une progression. Le scan lui-même
    /// reste synchrone/bloquant : c'est à l'appelant de l'exécuter hors du
    /// thread UI (ex. Task.Run) pour ne pas geler l'application.
    /// </param>
    public static IReadOnlyList<string> ScanForReceivedSchemas(string backupsFolder, IProgress<(int Done, int Total)>? progress = null)
    {
        var found = new List<string>();
        if (!Directory.Exists(backupsFolder)) return found;

        var files = Directory.EnumerateFiles(backupsFolder, "*.log", SearchOption.TopDirectoryOnly).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < files.Count; i++)
        {
            foreach (var name in ScanFile(files[i]))
            {
                if (seen.Add(name)) found.Add(name);
            }
            progress?.Report((i + 1, files.Count));
        }
        return found;
    }

    // Chaque archive rejoue sa propre machine à états (GameLogLineProcessor) :
    // les archives sont des sessions indépendantes, pas la suite les unes
    // des autres (continuation de notification multi-lignes, fenêtre
    // anti-rafale... ne doivent pas franchir une frontière de fichier).
    private static List<string> ScanFile(string path)
    {
        var result = new List<string>();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var processor = new GameLogLineProcessor();
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var evt = processor.ProcessLine(line);
                if (evt is null || evt.Type != GameLogEventTypes.HudNotification || evt.Text is null) continue;

                // Même nettoyage que la détection en direct (BuildHudAnnouncement) :
                // sans lui, les balises d'emphase accolées au nom par le jeu (ex.
                // "Ezra <EM4>[SP]</EM4>") restaient dans le nom extrait, qui ne
                // correspondait alors plus jamais à la clé propre de SchemaDatabase
                // — le schéma était bien ajouté à la liste, mais sans fabricant ni
                // description (SchemaDatabase.Find ne le reconnaissait pas).
                var cleanText = GameLogAnnouncer.CleanHudNotificationText(evt.Text);
                var name = GameLogAnnouncer.TryExtractReceivedSchemaName(cleanText);
                if (name is not null) result.Add(name);
            }
        }
        catch (IOException)
        {
            // Archive verrouillée/en cours d'écriture : ignorée, le reste du scan continue.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem pour un problème de permissions sur ce fichier précis.
        }
        return result;
    }

    /// <summary>
    /// Parcourt tous les .log de <paramref name="backupsFolder"/> en une
    /// seule passe par fichier et retourne les 4 statistiques du panneau
    /// "📊 Statistiques" : temps par vaisseau (voir
    /// GameLogAnnouncer.TryExtractShipChannelEvent et ShipTimeTracker),
    /// temps de jeu total (span entre le premier et le dernier horodatage
    /// lus dans le fichier — voir GameLogState.LastLineTimestamp pour
    /// l'équivalent en direct), aUEC envoyés (voir
    /// GameLogAnnouncer.TryExtractAuecSent) et nombre de visites par
    /// destination (ZoneChange, résolu avec <paramref name="destinationAliases"/>
    /// comme en direct) — SANS fusion avec un éventuel total déjà
    /// enregistré (à la charge de l'appelant, comme ScanForReceivedSchemas).
    /// Un intervalle de vaisseau encore ouvert à la fin d'un fichier est
    /// crédité jusqu'au dernier horodatage lu plutôt que d'être perdu.
    /// </summary>
    /// <param name="destinationAliases">AiConfig.GameLogDestinationAliases de l'utilisateur, pour résoudre les destinations exactement comme en direct — jamais modifié ici (lecture seule, contrairement à MaybeRegisterDestinationAlias côté direct).</param>
    /// <param name="alreadyScannedFileNames">
    /// Noms de fichiers (voir GameLogBackupStatsResult.ScannedFileNames d'un
    /// appel précédent, persistés par l'appelant) à ignorer — sans ça,
    /// relancer le scan une deuxième fois recompterait tout ce qui a déjà
    /// été trouvé la première fois en plus de l'existant. Une archive une
    /// fois roulée par le jeu n'est jamais réécrite, donc son nom suffit à
    /// l'identifier de façon stable d'un scan à l'autre.
    /// </param>
    public static GameLogBackupStatsResult ScanForStats(
        string backupsFolder,
        IReadOnlyDictionary<string, string>? destinationAliases = null,
        IReadOnlySet<string>? alreadyScannedFileNames = null,
        IProgress<(int Done, int Total)>? progress = null)
    {
        var shipTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var destinationCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        double playTimeSeconds = 0;
        double auecSent = 0;
        var scannedNow = new List<string>();
        if (!Directory.Exists(backupsFolder))
            return new GameLogBackupStatsResult(shipTotals, 0, 0, destinationCounts, scannedNow);

        var files = Directory.EnumerateFiles(backupsFolder, "*.log", SearchOption.TopDirectoryOnly).ToList();
        for (var i = 0; i < files.Count; i++)
        {
            var fileName = Path.GetFileName(files[i]);
            if (alreadyScannedFileNames is null || !alreadyScannedFileNames.Contains(fileName))
            {
                var fileStats = ScanFileForStats(files[i], destinationAliases);
                foreach (var (ship, seconds) in fileStats.ShipSeconds)
                    shipTotals[ship] = shipTotals.GetValueOrDefault(ship) + seconds;
                foreach (var (destination, count) in fileStats.DestinationCounts)
                    destinationCounts[destination] = destinationCounts.GetValueOrDefault(destination) + count;
                playTimeSeconds += fileStats.PlayTimeSeconds;
                auecSent += fileStats.AuecSent;
                scannedNow.Add(fileName);
            }
            progress?.Report((i + 1, files.Count));
        }
        return new GameLogBackupStatsResult(shipTotals, playTimeSeconds, auecSent, destinationCounts, scannedNow);
    }

    private sealed record FileStats(
        List<(string Ship, double Seconds)> ShipSeconds,
        double PlayTimeSeconds,
        double AuecSent,
        Dictionary<string, int> DestinationCounts);

    // Même principe que ScanFile (machine à états indépendante par fichier :
    // les archives sont des sessions indépendantes, pas la suite les unes
    // des autres) — une seule passe sur le fichier calcule les 4
    // statistiques à la fois plutôt que de le relire une fois par
    // statistique.
    private static FileStats ScanFileForStats(string path, IReadOnlyDictionary<string, string>? destinationAliases)
    {
        var shipSeconds = new List<(string, double)>();
        var destinationCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        double auecSent = 0;
        DateTimeOffset? firstTs = null;
        DateTimeOffset? lastTs = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var processor = new GameLogLineProcessor();
            var shipTracker = new ShipTimeTracker();
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (GameLogLineProcessor.ParseLineTimestamp(line) is { } lineTs)
                {
                    firstTs ??= lineTs;
                    lastTs = lineTs;
                }

                var evt = processor.ProcessLine(line);
                if (evt is null) continue;

                if (evt.Type == GameLogEventTypes.ZoneChange)
                {
                    var resolved = GameLogDestinations.ResolveDestinationLabel(evt.Zone, evt.ObstructionLabel, destinationAliases, evt.StartLocation);
                    if (!string.IsNullOrEmpty(resolved))
                        destinationCounts[resolved] = destinationCounts.GetValueOrDefault(resolved) + 1;
                    continue;
                }

                if (evt.Type != GameLogEventTypes.HudNotification || evt.Text is null) continue;

                var cleanText = GameLogAnnouncer.CleanHudNotificationText(evt.Text);

                var shipChange = GameLogAnnouncer.TryExtractShipChannelEvent(cleanText);
                if (shipChange is not null)
                {
                    var eventTs = DateTimeOffset.FromUnixTimeMilliseconds((long)(evt.Ts * 1000));
                    if (shipTracker.Process(eventTs, shipChange.Value.ShipName, shipChange.Value.Entered) is { } closed)
                        shipSeconds.Add(closed);
                    continue;
                }

                var auec = GameLogAnnouncer.TryExtractAuecSent(cleanText);
                if (auec is { } amount) auecSent += amount;
            }
            if (lastTs is { } ts && shipTracker.Flush(ts) is { } finalClosed)
                shipSeconds.Add(finalClosed);
        }
        catch (IOException)
        {
            // Archive verrouillée/en cours d'écriture : ignorée, le reste du scan continue.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem pour un problème de permissions sur ce fichier précis.
        }

        var playTime = firstTs is { } first && lastTs is { } last ? Math.Max(0, (last - first).TotalSeconds) : 0;
        return new FileStats(shipSeconds, playTime, auecSent, destinationCounts);
    }
}
