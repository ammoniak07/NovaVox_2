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

        return remoteVersion.Length > 0 && VersionUtil.IsNewer(remoteVersion, localVersion)
            ? new UpdateCheckResult(true, remoteVersion, downloadUrl)
            : new UpdateCheckResult(false);
    }
}
