using System.Text.Json.Nodes;
using static NovaVox.Core.Json.JsonHelpers;

namespace NovaVox.Core.Update;

public sealed record UpdateCheckResult(bool Available, string? Version = null, string? Url = null);

/// <summary>
/// Analyse la réponse du manifest de mise à jour — port de
/// Api.check_for_update (app.py), sans l'appel réseau lui-même (voir
/// NovaVox.App pour l'appel HTTP réel) pour rester testable. Gère les
/// deux formats : manifest simple {"version", "url"} et réponse native de
/// l'API GitHub Releases ("tag_name" + "assets").
/// </summary>
public static class UpdateManifest
{
    public static UpdateCheckResult Parse(string json, string localVersion)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch
        {
            return new UpdateCheckResult(false);
        }
        if (parsed is not JsonObject data) return new UpdateCheckResult(false);

        string remoteVersion;
        string downloadUrl;

        if (data.ContainsKey("tag_name"))
        {
            remoteVersion = GetString(data["tag_name"]).Trim().TrimStart('v', 'V');
            downloadUrl = "";
            if (data["assets"] is JsonArray assets)
            {
                foreach (var asset in assets)
                {
                    var name = GetString(asset?["name"]).Trim().ToLowerInvariant();
                    if (name.EndsWith(".exe", StringComparison.Ordinal))
                    {
                        downloadUrl = GetString(asset?["browser_download_url"]).Trim();
                        break;
                    }
                }
            }
        }
        else
        {
            remoteVersion = GetString(data["version"]).Trim();
            downloadUrl = GetString(data["url"]).Trim();
        }

        if (!IsTrustedDownloadUrl(downloadUrl)) downloadUrl = "";

        return remoteVersion.Length > 0 && VersionUtil.IsNewer(remoteVersion, localVersion)
            ? new UpdateCheckResult(true, remoteVersion, downloadUrl)
            : new UpdateCheckResult(false);
    }

    /// <summary>
    /// Domaines d'où peut venir l'installateur : le site officiel et GitHub
    /// (page de release et serveur de fichiers des releases).
    /// </summary>
    private static readonly string[] TrustedHosts =
    {
        "novanox.1ercorpscolonial.fr",
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

    /// <summary>
    /// Le lien de mise à jour vient d'un fichier téléchargé : avant de
    /// l'ouvrir, on vérifie que c'est bien un lien https vers un domaine
    /// connu. Sinon (fichier de version modifié par un tiers, lien vers un
    /// fichier local ou un autre site), il est ignoré.
    /// </summary>
    public static bool IsTrustedDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return false;
        var host = uri.IdnHost.ToLowerInvariant();
        return TrustedHosts.Contains(host);
    }
}
