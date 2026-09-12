using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using NovaVox.Core;
using NovaVox.Core.Tts;

namespace NovaVox.App.Install;

/// <summary>
/// Installe le moteur Piper (piper.exe) depuis la dernière publication
/// GitHub officielle, et télécharge/supprime des voix curatées depuis
/// Hugging Face — port de piper_install/_piper_install_thread/
/// piper_download_voice/_piper_download_voice_thread/piper_delete_voice/
/// piper_get_status (app.py).
/// </summary>
public sealed class PiperInstaller
{
    private const string GitHubLatestReleaseApi = "https://api.github.com/repos/rhasspy/piper/releases/latest";
    private static readonly HttpClient Client = new();

    public static string PiperDir => Path.Combine(NovaVoxPaths.BaseDirectory, "piper");
    public static string PiperExePath => Path.Combine(PiperDir, "piper.exe");
    public static string PiperVoicesDir => Path.Combine(PiperDir, "voices");

    public event EventHandler<string>? EngineProgress;
    public event EventHandler<bool>? EngineInstallDone;
    public event EventHandler<(string VoiceId, string Message)>? VoiceProgress;
    public event EventHandler<(string VoiceId, bool Success)>? VoiceDownloadDone;

    public bool IsEngineInstalled => File.Exists(PiperExePath);

    public bool IsVoiceInstalled(string voiceId) => File.Exists(Path.Combine(PiperVoicesDir, $"{voiceId}.onnx"));

    public async Task InstallEngineAsync(CancellationToken cancellationToken = default)
    {
        var tmpZip = Path.Combine(NovaVoxPaths.BaseDirectory, $"piper_{Guid.NewGuid():N}.zip");
        var tmpExtract = Path.Combine(NovaVoxPaths.BaseDirectory, $"piper_extract_{Guid.NewGuid():N}");
        try
        {
            EngineProgress?.Invoke(this, "Recherche de la dernière version de Piper...");
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubLatestReleaseApi);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            using var response = await Client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var release = JsonDocument.Parse(body);

            string? assetUrl = null;
            if (release.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = (asset.TryGetProperty("name", out var n) ? n.GetString() : null)?.ToLowerInvariant() ?? "";
                    if (name.Contains("windows") && name.EndsWith(".zip")
                        && (name.Contains("amd64") || name.Contains("x64") || name.Contains("x86_64")))
                    {
                        assetUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(assetUrl))
                throw new InvalidOperationException("Version Windows de Piper introuvable dans la dernière publication GitHub (sa structure a peut-être changé).");

            EngineProgress?.Invoke(this, "Téléchargement de Piper...");
            await FileDownloader.DownloadAsync(assetUrl, tmpZip, (downloaded, total) =>
            {
                if (total is > 0)
                {
                    var mo = downloaded / (1024.0 * 1024.0);
                    var totalMo = total.Value / (1024.0 * 1024.0);
                    EngineProgress?.Invoke(this, $"Téléchargement... {mo:F0} / {totalMo:F0} Mo");
                }
            }, cancellationToken: cancellationToken);

            EngineProgress?.Invoke(this, "Extraction...");
            Directory.CreateDirectory(tmpExtract);
            ZipFile.ExtractToDirectory(tmpZip, tmpExtract);

            var foundDir = Directory.EnumerateFiles(tmpExtract, "piper.exe", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault();
            if (foundDir is null)
                throw new InvalidOperationException("piper.exe introuvable dans l'archive téléchargée.");

            if (Directory.Exists(PiperDir)) Directory.Delete(PiperDir, recursive: true);
            Directory.Move(foundDir, PiperDir);
            Directory.CreateDirectory(PiperVoicesDir);

            EngineProgress?.Invoke(this, "Piper installé avec succès.");
            EngineInstallDone?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            EngineProgress?.Invoke(this, $"[Erreur] {ex.Message}");
            EngineInstallDone?.Invoke(this, false);
        }
        finally
        {
            try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { /* best effort */ }
            try { if (Directory.Exists(tmpExtract)) Directory.Delete(tmpExtract, recursive: true); } catch { /* best effort */ }
        }
    }

    public async Task DownloadVoiceAsync(string voiceId, CancellationToken cancellationToken = default)
    {
        var info = PiperVoiceCatalog.Find(voiceId);
        if (info is null)
        {
            VoiceDownloadDone?.Invoke(this, (voiceId, false));
            return;
        }

        Directory.CreateDirectory(PiperVoicesDir);
        var modelPath = Path.Combine(PiperVoicesDir, $"{voiceId}.onnx");
        var configPath = Path.Combine(PiperVoicesDir, $"{voiceId}.onnx.json");
        var partialPaths = new[] { modelPath, configPath, modelPath + ".part", configPath + ".part" };
        try
        {
            foreach (var (suffix, dest) in new[] { (".onnx", modelPath), (".onnx.json", configPath) })
            {
                var url = info.UrlBase + suffix;
                VoiceProgress?.Invoke(this, (voiceId, $"Téléchargement de {Path.GetFileName(dest)}..."));
                var tmpDest = dest + ".part";

                await FileDownloader.DownloadAsync(url, tmpDest, (downloaded, total) =>
                {
                    if (total is > 0 && suffix == ".onnx")
                    {
                        var mo = downloaded / (1024.0 * 1024.0);
                        var totalMo = total.Value / (1024.0 * 1024.0);
                        VoiceProgress?.Invoke(this, (voiceId, $"Téléchargement... {mo:F0} / {totalMo:F0} Mo"));
                    }
                }, cancellationToken: cancellationToken);

                File.Move(tmpDest, dest, overwrite: true);
            }

            VoiceDownloadDone?.Invoke(this, (voiceId, true));
        }
        catch (Exception)
        {
            foreach (var p in partialPaths)
            {
                try { if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
            }
            VoiceDownloadDone?.Invoke(this, (voiceId, false));
        }
    }

    public void DeleteVoice(string voiceId)
    {
        var modelPath = Path.Combine(PiperVoicesDir, $"{voiceId}.onnx");
        var configPath = Path.Combine(PiperVoicesDir, $"{voiceId}.onnx.json");
        try { if (File.Exists(modelPath)) File.Delete(modelPath); } catch { /* best effort */ }
        try { if (File.Exists(configPath)) File.Delete(configPath); } catch { /* best effort */ }
    }
}
