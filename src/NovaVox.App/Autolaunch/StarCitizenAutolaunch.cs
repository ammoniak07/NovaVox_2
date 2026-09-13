using System.Diagnostics;
using System.Text;
using NovaVox.Core;

namespace NovaVox.App.Autolaunch;

/// <summary>
/// Lancement automatique de NovaVox au démarrage de Star Citizen — port
/// de set_star_citizen_autolaunch_enabled/_wait_for_star_citizen_if_
/// requested (app.py). Réutilise la même technique que Python (un
/// raccourci .lnk créé via WScript.Shell en PowerShell) plutôt que
/// d'écrire un binding COM IShellLink : déjà éprouvée, sans dépendance
/// supplémentaire.
/// </summary>
public static class StarCitizenAutolaunch
{
    // Nom DISTINCT de celui de la version Python ("NOVAVOX (veille Star
    // Citizen).lnk", même dossier Démarrage) : les deux éditions peuvent
    // coexister installées côte à côte (AppId/dossier d'installation déjà
    // distincts, voir installer.iss) — avec le même nom de raccourci,
    // activer la veille dans l'une aurait silencieusement écrasé le
    // raccourci de l'autre (et le désactiver dans l'une aurait supprimé
    // le fichier dont l'autre dépendait aussi), cassant sa propre veille
    // sans aucun message d'erreur.
    private static string ShortcutPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", "Startup",
        "NOVAVOX V2 (veille Star Citizen).lnk");

    public static bool IsEnabled() => File.Exists(ShortcutPath());

    /// <summary>Reste silencieux en cas d'échec : ce réglage ne doit jamais empêcher l'appli de fonctionner normalement.</summary>
    public static bool SetEnabled(bool enabled)
    {
        var shortcutPath = ShortcutPath();
        try
        {
            if (!enabled)
            {
                if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                return true;
            }

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return false;

            var script =
                "$shell = New-Object -ComObject WScript.Shell; " +
                $"$sc = $shell.CreateShortcut('{PsEscape(shortcutPath)}'); " +
                $"$sc.TargetPath = '{PsEscape(exePath)}'; " +
                "$sc.Arguments = '--wait-for-sc'; " +
                $"$sc.WorkingDirectory = '{PsEscape(NovaVoxPaths.BaseDirectory)}'; " +
                "$sc.IconLocation = $sc.TargetPath + ',0'; " +
                "$sc.Save()";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process?.WaitForExit(10_000);
            return File.Exists(shortcutPath);
        }
        catch (Exception ex)
        {
            AppLog.Append(NovaVoxPaths.BaseDirectory, $"[Autolaunch] Création/suppression du raccourci de veille échouée ({ex.Message}).", "diagnostic");
            return false;
        }
    }

    private static string PsEscape(string value) => value.Replace("'", "''");

    /// <summary>
    /// Si déjà activé, recrée le raccourci avec le chemin ACTUEL de
    /// l'exécutable (ex. après une mise à jour installée dans un autre
    /// dossier) — sinon le raccourci pointerait vers un .exe qui n'existe
    /// plus, sans erreur visible. Appelée à chaque lancement normal.
    /// </summary>
    public static void RefreshShortcut()
    {
        try
        {
            if (IsEnabled()) SetEnabled(true);
        }
        catch (Exception ex)
        {
            AppLog.Append(NovaVoxPaths.BaseDirectory, $"[Autolaunch] Rafraîchissement du raccourci de veille échoué ({ex.Message}).", "diagnostic");
        }
    }

    /// <summary>
    /// Si lancé avec --wait-for-sc (raccourci créé par SetEnabled), reste
    /// en veille silencieuse jusqu'à détecter StarCitizen.exe. Vérifie
    /// toutes les 5 secondes ; empreinte CPU/mémoire quasi nulle en attendant.
    /// </summary>
    public static void WaitForStarCitizenIfRequested(string[] args)
    {
        if (!args.Contains("--wait-for-sc")) return;
        while (true)
        {
            try
            {
                if (Process.GetProcessesByName("StarCitizen").Length > 0) return;
            }
            catch
            {
                // Ignoré : nouvelle tentative au prochain passage.
            }
            Thread.Sleep(5000);
        }
    }
}
