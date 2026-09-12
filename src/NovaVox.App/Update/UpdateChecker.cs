using System.Diagnostics;
using System.Net.Http;
using NovaVox.Core;
using NovaVox.Core.Update;

namespace NovaVox.App.Update;

/// <summary>
/// Vérification de mise à jour — port de Api.check_for_update/
/// open_update_url (app.py). Ne bloque jamais l'appli en cas d'échec
/// réseau : renvoie juste "rien de nouveau".
/// </summary>
public static class UpdateChecker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    public const string DefaultManifestUrl = "https://novanox.1ercorpscolonial.fr/version.json";

    /// <summary>
    /// URL du manifest — un fichier gui/update_source.txt à côté de
    /// l'exécutable (généré à la compilation pour une variante GitHub
    /// Releases, voir build_exe.bat côté Python) prend le pas sur le
    /// défaut si présent.
    /// </summary>
    public static string ResolveManifestUrl()
    {
        try
        {
            var overridePath = Path.Combine(NovaVoxPaths.BaseDirectory, "gui", "update_source.txt");
            if (File.Exists(overridePath))
            {
                var url = File.ReadAllText(overridePath).Trim();
                if (url.Length > 0) return url;
            }
        }
        catch
        {
            // Repli sur le défaut si le fichier est illisible.
        }
        return DefaultManifestUrl;
    }

    public static async Task<UpdateCheckResult> CheckForUpdateAsync(string patchNotesFilePath)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ResolveManifestUrl());
            request.Headers.Add("User-Agent", "NOVAVOX-updater");
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var localVersion = VersionUtil.GetAppVersion(patchNotesFilePath);
            return UpdateManifest.Parse(body, localVersion);
        }
        catch
        {
            return new UpdateCheckResult(false);
        }
    }

    public static void OpenUpdateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
