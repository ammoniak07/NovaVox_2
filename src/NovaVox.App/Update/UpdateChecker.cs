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

    // Manifest DISTINCT de celui de la version Python (qui vit sur le
    // même site, à /version.json) : cette édition .NET a sa propre
    // numérotation de version (repartie à 0.0.1) et son propre
    // installeur — comparer sa version locale au manifest Python
    // signalerait une "mise à jour" en permanence (la version Python
    // est numériquement bien plus haute). Voir build.bat pour la
    // génération/le déploiement de novavoxnet_version.json.
    public const string DefaultManifestUrl = "https://novanox.1ercorpscolonial.fr/novavoxnet_version.json";

    /// <summary>
    /// URL du manifest — un fichier update_source.txt à côté de
    /// l'exécutable (généré à la compilation pour une variante GitHub
    /// Releases, voir build.bat) prend le pas sur le défaut si présent.
    /// </summary>
    public static string ResolveManifestUrl()
    {
        try
        {
            var overridePath = Path.Combine(NovaVoxPaths.BaseDirectory, "update_source.txt");
            if (File.Exists(overridePath))
            {
                var url = File.ReadAllText(overridePath).Trim();
                if (url.Length > 0) return url;
            }
        }
        catch (Exception ex)
        {
            AppLog.Append(NovaVoxPaths.BaseDirectory, $"[MàJ] Lecture de gui/update_source.txt échouée, repli sur l'URL par défaut ({ex.Message}).", "diagnostic");
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
        catch (Exception ex)
        {
            AppLog.Append(NovaVoxPaths.BaseDirectory, $"[MàJ] Vérification de mise à jour échouée ({ex.GetType().Name} : {ex.Message}).", "diagnostic");
            return new UpdateCheckResult(false);
        }
    }

    public static void OpenUpdateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
