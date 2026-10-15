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

        var localizationDir = Path.Combine(dir, "data", "Localization");

        // 1) Langue choisie dans user.cfg ("g_language = french_(france)"),
        //    méthode d'installation des traductions depuis 2024 : le fichier
        //    est alors dans data/Localization/french_(france)/, pas english/.
        var language = ReadUserCfgLanguage(Path.Combine(dir, "user.cfg"));
        if (language is not null)
        {
            var chosen = Path.Combine(localizationDir, language, "global.ini");
            if (File.Exists(chosen)) return chosen;
        }

        // 2) Ancienne méthode : traduction copiée à la place du fichier anglais.
        var english = Path.Combine(localizationDir, "english", "global.ini");
        if (File.Exists(english)) return english;

        // 3) Repli : le global.ini le plus récent de n'importe quelle langue.
        try
        {
            return Directory.Exists(localizationDir)
                ? Directory.GetDirectories(localizationDir)
                    .Select(d => Path.Combine(d, "global.ini"))
                    .Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Valeur de g_language dans user.cfg (null si absent/illisible).</summary>
    private static string? ReadUserCfgLanguage(string userCfgPath)
    {
        try
        {
            if (!File.Exists(userCfgPath)) return null;
            foreach (var line in File.ReadLines(userCfgPath))
            {
                var eq = line.IndexOf('=');
                if (eq <= 0 || !line[..eq].Trim().Equals("g_language", StringComparison.OrdinalIgnoreCase)) continue;
                var value = line[(eq + 1)..].Trim().Trim('"');
                return value.Length > 0 && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 ? value : null;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
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
    public static IReadOnlyDictionary<string, string> ReadKeyedValues(string globalIniPath, IReadOnlySet<string> keysOfInterest) =>
        keysOfInterest.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ReadKeyedValues(globalIniPath, keysOfInterest.Contains);

    /// <summary>Même lecture, avec un filtre de clés plutôt qu'une liste figée (ex. GameLogDestinations.IsLiveLocationKey).</summary>
    public static IReadOnlyDictionary<string, string> ReadKeyedValues(string globalIniPath, Func<string, bool> isKeyOfInterest)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

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
                if (!isKeyOfInterest(key)) continue;

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
