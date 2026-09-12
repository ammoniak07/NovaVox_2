namespace NovaVox.Core;

/// <summary>
/// Journal texte persistant, un nouveau fichier à chaque lancement de
/// l'appli dans BaseDirectory/Log/novavox_AAAA-MM-JJ_HH-mm-ss.txt (fermer
/// puis rouvrir l'appli crée un fichier distinct, plutôt qu'un seul
/// fichier continu ou une rotation par jour) — reçoit en direct tout ce
/// qui s'affiche dans le journal système, plus les messages "diagnostic"
/// (détails techniques verbeux jamais affichés dans le journal système
/// lui-même, voir AppendLog dans MainWindow.xaml.cs) et toute exception
/// non gérée qui autrement disparaîtrait avec le plantage de l'appli
/// (voir les gestionnaires globaux dans App.xaml.cs).
/// </summary>
public static class AppLog
{
    // Calculé une seule fois, à la première écriture de la session — pas à
    // chaque appel — pour que tous les messages d'un même lancement
    // atterrissent dans le même fichier.
    private static readonly Lazy<string> SessionFileName = new(() => $"novavox_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");

    public static string LogDirectory(string baseDir) => Path.Combine(baseDir, "Log");

    private static string CurrentFilePath(string baseDir) => Path.Combine(LogDirectory(baseDir), SessionFileName.Value);

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
