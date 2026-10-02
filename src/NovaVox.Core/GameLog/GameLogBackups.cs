namespace NovaVox.Core.GameLog;

/// <summary>
/// Résultat d'un scan GameLogBackups.ScanForShipTimes : le temps total
/// (secondes) trouvé par vaisseau sur les fichiers effectivement parcourus
/// cette fois-ci, et les noms de ces fichiers — à fusionner/persister par
/// l'appelant (AiConfig.ShipTimeSeconds / ShipTimeScannedBackupFiles), pour
/// qu'un second scan ne recompte jamais une archive déjà prise en compte.
/// </summary>
public sealed record ShipTimeScanResult(IReadOnlyDictionary<string, double> SecondsByShip, IReadOnlyList<string> ScannedFileNames);

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
    /// Parcourt tous les .log de <paramref name="backupsFolder"/> et
    /// retourne le temps total (secondes) passé dans chaque vaisseau,
    /// déduit des notifications d'entrée/sortie du canal de bord (voir
    /// GameLogAnnouncer.TryExtractShipChannelEvent et ShipTimeTracker) —
    /// SANS fusion avec un éventuel total déjà enregistré (à la charge de
    /// l'appelant, comme ScanForReceivedSchemas). Un intervalle encore
    /// ouvert à la fin d'un fichier (pas de notification de sortie avant
    /// la fin de l'archive, ex. jeu fermé brutalement) est crédité jusqu'au
    /// dernier horodatage lu dans ce fichier plutôt que d'être perdu.
    /// </summary>
    /// <param name="alreadyScannedFileNames">
    /// Noms de fichiers (voir ShipTimeScanResult.ScannedFileNames d'un appel
    /// précédent, persistés par l'appelant) à ignorer — sans ça, relancer le
    /// scan une deuxième fois recompterait tout le temps déjà trouvé la
    /// première fois en plus de l'existant. Une archive une fois roulée par
    /// le jeu n'est jamais réécrite, donc son nom suffit à l'identifier de
    /// façon stable d'un scan à l'autre.
    /// </param>
    public static ShipTimeScanResult ScanForShipTimes(
        string backupsFolder,
        IReadOnlySet<string>? alreadyScannedFileNames = null,
        IProgress<(int Done, int Total)>? progress = null)
    {
        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var scannedNow = new List<string>();
        if (!Directory.Exists(backupsFolder)) return new ShipTimeScanResult(totals, scannedNow);

        var files = Directory.EnumerateFiles(backupsFolder, "*.log", SearchOption.TopDirectoryOnly).ToList();
        for (var i = 0; i < files.Count; i++)
        {
            var fileName = Path.GetFileName(files[i]);
            if (alreadyScannedFileNames is null || !alreadyScannedFileNames.Contains(fileName))
            {
                foreach (var (ship, seconds) in ScanFileForShipTimes(files[i]))
                    totals[ship] = totals.GetValueOrDefault(ship) + seconds;
                scannedNow.Add(fileName);
            }
            progress?.Report((i + 1, files.Count));
        }
        return new ShipTimeScanResult(totals, scannedNow);
    }

    // Même principe que ScanFile (machine à états indépendante par fichier),
    // avec en plus le dernier horodatage lu (lastTs, sur TOUTE ligne —
    // pas seulement les notifications HUD) pour pouvoir créditer un
    // intervalle encore ouvert à la fin du fichier.
    private static List<(string Ship, double Seconds)> ScanFileForShipTimes(string path)
    {
        var result = new List<(string, double)>();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var processor = new GameLogLineProcessor();
            var tracker = new ShipTimeTracker();
            DateTimeOffset? lastTs = null;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (GameLogLineProcessor.ParseLineTimestamp(line) is { } lineTs) lastTs = lineTs;

                var evt = processor.ProcessLine(line);
                if (evt is null || evt.Type != GameLogEventTypes.HudNotification || evt.Text is null) continue;

                var cleanText = GameLogAnnouncer.CleanHudNotificationText(evt.Text);
                var change = GameLogAnnouncer.TryExtractShipChannelEvent(cleanText);
                if (change is null) continue;

                var eventTs = DateTimeOffset.FromUnixTimeMilliseconds((long)(evt.Ts * 1000));
                if (tracker.Process(eventTs, change.Value.ShipName, change.Value.Entered) is { } closed)
                    result.Add(closed);
            }
            if (lastTs is { } ts && tracker.Flush(ts) is { } finalClosed)
                result.Add(finalClosed);
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
}
