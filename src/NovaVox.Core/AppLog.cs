namespace NovaVox.Core;

/// <summary>
/// Journal texte persistant, un fichier par jour dans
/// BaseDirectory/Log/novavox_AAAA-MM-JJ.txt — reçoit en direct tout ce
/// qui s'affiche dans le journal système (info/succès/avertissement/
/// erreur, voir AppendLog dans MainWindow.xaml.cs), plus toute exception
/// non gérée qui autrement disparaîtrait avec le plantage de l'appli
/// (voir les gestionnaires globaux dans App.xaml.cs). Remplace
/// l'ancien ErrorLog (limité aux seules erreurs déjà affichées, un
/// seul fichier tronqué arbitrairement une fois trop gros) : la
/// rotation quotidienne borne naturellement la taille de chaque
/// fichier sans jamais effacer d'historique.
/// </summary>
public static class AppLog
{
    public static string LogDirectory(string baseDir) => Path.Combine(baseDir, "Log");

    private static string CurrentFilePath(string baseDir) =>
        Path.Combine(LogDirectory(baseDir), $"novavox_{DateTime.Now:yyyy-MM-dd}.txt");

    /// <summary>Ajoute une ligne horodatée — appelé pour chaque message du journal système, quel que soit son "kind".</summary>
    public static void Append(string baseDir, string message, string kind = "info")
    {
        try
        {
            var dir = LogDirectory(baseDir);
            Directory.CreateDirectory(dir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{kind.ToUpperInvariant()}] {message}{Environment.NewLine}";
            File.AppendAllText(CurrentFilePath(baseDir), line);
        }
        catch
        {
            // Best effort : ne doit jamais empêcher l'affichage du message à l'utilisateur.
        }
    }

    /// <summary>Journalise une exception avec sa pile d'appel complète — pour les gestionnaires globaux (App.xaml.cs) qui n'ont pas d'autre moyen de la faire remonter à l'utilisateur.</summary>
    public static void AppendException(string baseDir, string context, Exception ex) =>
        Append(baseDir, $"{context} : {ex}", "error");
}
