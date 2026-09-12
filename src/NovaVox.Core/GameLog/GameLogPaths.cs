namespace NovaVox.Core.GameLog;

public static class GameLogPaths
{
    // Emplacements standards possibles, testés dans l'ordre sur tous les
    // lecteurs disponibles (pas seulement C:), beaucoup de joueurs
    // installant le jeu sur un second disque.
    private static readonly string[] InstallSubpaths =
    {
        @"Roberts Space Industries\StarCitizen\LIVE\Game.log",
        @"Roberts Space Industries\StarCitizen\PTU\Game.log",
        @"Roberts Space Industries\StarCitizen\EPTU\Game.log",
        @"Program Files\Roberts Space Industries\StarCitizen\LIVE\Game.log",
    };

    /// <summary>
    /// Essaie de localiser le Game.log automatiquement. Retourne le
    /// premier chemin existant, ou null si rien n'est trouvé (l'utilisateur
    /// devra indiquer le chemin manuellement dans les réglages).
    /// </summary>
    public static string? FindGameLogPath(IEnumerable<string>? extraPaths = null)
    {
        var candidates = new List<string>(extraPaths ?? Enumerable.Empty<string>());
        for (char driveLetter = 'C'; driveLetter <= 'Z'; driveLetter++)
        {
            var drive = $"{driveLetter}:\\";
            if (!Directory.Exists(drive)) continue;
            foreach (var sub in InstallSubpaths)
                candidates.Add(Path.Combine(drive, sub));
        }
        return candidates.FirstOrDefault(File.Exists);
    }
}
