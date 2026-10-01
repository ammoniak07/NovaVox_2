namespace NovaVox.Core.GameLog;

/// <summary>
/// Lit le global.ini LOCAL du joueur (jamais redistribué — voir le résumé
/// de SchemaDatabase) pour reconnaître les schémas quelle que soit la
/// traduction communautaire installée (SCEFRA via Multitool, ou une autre),
/// plutôt qu'une liste figée d'alias français construite à la main. Star
/// Citizen charge ses textes depuis un fichier
/// data/Localization/english/global.ini à côté du Game.log — une
/// traduction communautaire fonctionne en remplaçant le CONTENU (en
/// anglais à l'origine) de ce fichier par du texte traduit, sans toucher à
/// son nom ni à ses clés (ex. "item_Name_qrt_specialist_heavy_arms_01_01_01
/// =Bras Antium" au lieu de "...=Antium Arms") — CIG autorise explicitement
/// ce remplacement pour la traduction communautaire.
/// </summary>
public static class GameLogLocalization
{
    /// <summary>
    /// Chemin du global.ini actif, voisin du Game.log en cours (même
    /// installation, même canal LIVE/PTU/EPTU) — null si le Game.log
    /// lui-même est introuvable ou si ce fichier n'existe pas encore à cet
    /// emplacement.
    /// </summary>
    public static string? FindGlobalIniPath(string? liveGameLogPath = null)
    {
        var logPath = liveGameLogPath ?? GameLogPaths.FindGameLogPath();
        if (logPath is null) return null;

        var dir = Path.GetDirectoryName(logPath);
        if (string.IsNullOrEmpty(dir)) return null;

        var iniPath = Path.Combine(dir, "data", "Localization", "english", "global.ini");
        return File.Exists(iniPath) ? iniPath : null;
    }

    /// <summary>
    /// Lit <paramref name="globalIniPath"/> et retourne, pour chaque clé
    /// présente dans <paramref name="keysOfInterest"/>, le texte associé —
    /// quelle que soit la langue réellement installée (anglais d'origine,
    /// ou une traduction communautaire qui a remplacé ce fichier). Ignore
    /// silencieusement les clés absentes (traduction partielle/en retard
    /// sur un patch récent) et les lignes mal formées, plutôt que de faire
    /// échouer toute la lecture pour une seule ligne corrompue.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReadKeyedValues(string globalIniPath, IReadOnlySet<string> keysOfInterest)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (keysOfInterest.Count == 0) return result;

        try
        {
            using var stream = new FileStream(globalIniPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var rawKey = line[..eq];
                // Certaines entrées (texte avec pluriel) portent un suffixe
                // ",P" sur la clé (ex. "ma_cle,P=...") — jamais présent sur
                // les clés de nom d'objet qui nous intéressent ici, mais
                // retiré par precaution pour rester robuste au format.
                var key = rawKey.EndsWith(",P", StringComparison.Ordinal) ? rawKey[..^2] : rawKey;
                if (!keysOfInterest.Contains(key)) continue;

                result[key] = line[(eq + 1)..];
            }
        }
        catch (IOException)
        {
            // Fichier verrouillé par le jeu en cours d'écriture : on retente
            // simplement au prochain démarrage plutôt que de faire planter l'appli.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem pour un problème de permissions.
        }
        return result;
    }
}
