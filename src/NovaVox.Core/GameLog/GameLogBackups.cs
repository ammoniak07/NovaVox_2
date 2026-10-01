namespace NovaVox.Core.GameLog;

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
    public static IReadOnlyList<string> ScanForReceivedSchemas(string backupsFolder)
    {
        var found = new List<string>();
        if (!Directory.Exists(backupsFolder)) return found;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(backupsFolder, "*.log", SearchOption.TopDirectoryOnly))
        {
            foreach (var name in ScanFile(file))
            {
                if (seen.Add(name)) found.Add(name);
            }
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
}
