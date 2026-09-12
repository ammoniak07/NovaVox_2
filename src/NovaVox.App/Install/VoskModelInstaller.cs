using System.IO.Compression;
using NovaVox.Core;
using NovaVox.Core.Speech;

namespace NovaVox.App.Install;

/// <summary>
/// Télécharge et installe un modèle Vosk choisi par l'utilisateur — port de
/// download_vosk_model / _download_vosk_model_thread (app.py). Installé
/// dans BaseDirectory/model, quel que soit le modèle (un seul modèle
/// installé à la fois, comme en Python).
/// </summary>
public sealed class VoskModelInstaller
{
    public static string TargetDir => Path.Combine(NovaVoxPaths.BaseDirectory, "model");

    // Marqueur écrit dans le dossier du modèle après une installation
    // depuis le catalogue (pas après un "Parcourir un dossier déjà
    // téléchargé...") : seul moyen de savoir ensuite QUEL modèle du
    // catalogue est actuellement installé, puisque le dossier lui-même
    // (nom imposé par le zip Vosk) ne le dit pas une fois renommé en "model".
    private const string InstalledMarkerFileName = ".novavox_model_id.txt";

    public event EventHandler<(int Percent, string Message)>? Progress;
    public event EventHandler<(bool Success, string Message)>? Done;

    public bool IsModelFolderValid(string? path) =>
        !string.IsNullOrEmpty(path) && Directory.Exists(path)
        && Directory.Exists(Path.Combine(path, "am")) && Directory.Exists(Path.Combine(path, "conf"));

    /// <summary>Id (catalogue) du modèle actuellement installé dans TargetDir, ou null si aucun/installé manuellement (dossier parcouru à la main).</summary>
    public string? GetInstalledModelId()
    {
        var markerPath = Path.Combine(TargetDir, InstalledMarkerFileName);
        try
        {
            return File.Exists(markerPath) ? File.ReadAllText(markerPath).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Supprime le modèle installé (TargetDir). Sans effet si aucun modèle n'y est installé.</summary>
    public void Uninstall()
    {
        if (Directory.Exists(TargetDir)) Directory.Delete(TargetDir, recursive: true);
    }

    public async Task<string?> InstallAsync(VoskModelInfo model, CancellationToken cancellationToken = default)
    {
        var tmpZip = Path.Combine(NovaVoxPaths.BaseDirectory, $"vosk_{Guid.NewGuid():N}.zip");
        var tmpExtractDir = Path.Combine(NovaVoxPaths.BaseDirectory, $"vosk_extract_{Guid.NewGuid():N}");
        try
        {
            RaiseProgress(0, $"Téléchargement de « {model.Label} »...");
            await FileDownloader.DownloadAsync(model.Url, tmpZip, (downloaded, total) =>
            {
                if (total is > 0)
                {
                    var percent = (int)(downloaded * 90 / total.Value);
                    var mo = downloaded / (1024.0 * 1024.0);
                    var totalMo = total.Value / (1024.0 * 1024.0);
                    RaiseProgress(percent, $"Téléchargement... {mo:F0} / {totalMo:F0} Mo");
                }
                else
                {
                    RaiseProgress(5, $"Téléchargement... {downloaded / (1024.0 * 1024.0):F0} Mo");
                }
            }, cancellationToken: cancellationToken);

            RaiseProgress(92, "Extraction de l'archive...");
            Directory.CreateDirectory(tmpExtractDir);
            ZipFile.ExtractToDirectory(tmpZip, tmpExtractDir);

            var entries = Directory.GetFileSystemEntries(tmpExtractDir)
                .Where(e => !Path.GetFileName(e).StartsWith('.'))
                .ToList();
            var extractedModelDir = entries.Count == 1 && Directory.Exists(entries[0]) ? entries[0] : tmpExtractDir;

            if (!IsModelFolderValid(extractedModelDir))
                throw new InvalidOperationException("L'archive téléchargée ne ressemble pas à un modèle Vosk valide.");

            RaiseProgress(96, "Installation du modèle...");
            if (Directory.Exists(TargetDir)) Directory.Delete(TargetDir, recursive: true);
            Directory.Move(extractedModelDir, TargetDir);
            File.WriteAllText(Path.Combine(TargetDir, InstalledMarkerFileName), model.Id);

            RaiseProgress(100, "Modèle installé avec succès.");
            Done?.Invoke(this, (true, TargetDir));
            return TargetDir;
        }
        catch (Exception ex)
        {
            Done?.Invoke(this, (false, ex.Message));
            return null;
        }
        finally
        {
            try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { /* best effort */ }
            try { if (Directory.Exists(tmpExtractDir)) Directory.Delete(tmpExtractDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private void RaiseProgress(int percent, string message) => Progress?.Invoke(this, (percent, message));
}
