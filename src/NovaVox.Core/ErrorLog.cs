namespace NovaVox.Core;

/// <summary>
/// Fichier d'erreurs BaseDirectory/erreurs.log — port simplifié de
/// _setup_error_logger/error_logger (app.py) : chaque erreur affichée dans
/// le journal système (kind "error") y est aussi écrite, avec un
/// timestamp, pour rester consultable après coup. Python utilise une
/// rotation par taille (RotatingFileHandler, 1 Mo, 2 sauvegardes) ; ce
/// portage se contente de repartir de zéro une fois le fichier trop
/// gros, plutôt que de porter la rotation multi-fichiers à l'identique.
/// </summary>
public static class ErrorLog
{
    private const long MaxSizeBytes = 1_000_000;

    public static void Append(string baseDir, string message)
    {
        try
        {
            var path = Path.Combine(baseDir, "erreurs.log");
            if (File.Exists(path) && new FileInfo(path).Length > MaxSizeBytes)
                File.Delete(path);

            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // Best effort : ne doit jamais empêcher l'affichage du message à l'utilisateur.
        }
    }
}
