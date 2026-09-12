namespace NovaVox.Core;

/// <summary>
/// Emplacement des fichiers modifiables (commands.json, ai_config.json,
/// model/...) : le dossier contenant l'exécutable, comme BASE_DIR côté
/// app.py (application "portable", pas de dossier %APPDATA%).
/// </summary>
public static class NovaVoxPaths
{
    public static string BaseDirectory { get; set; } = AppContext.BaseDirectory;
}
