using System.IO.Compression;
using System.Text.Json.Nodes;
using NovaVox.Core.Commands;

namespace NovaVox.Core.Config;

public sealed record ConfigImportResult(bool Ok, List<string> Imported, int ImportedProfileCount, string? Error = null);

/// <summary>
/// Export/import des fichiers de configuration modifiables (commandes,
/// réglages IA/audio/overlay, et optionnellement tous les profils) dans
/// une archive .zip unique — port de Api.export_config/import_config
/// (app.py). Ne dépend d'aucune boîte de dialogue : les chemins source/
/// destination sont fournis par l'appelant (WPF SaveFileDialog/
/// OpenFileDialog côté App), pour rester testable ici.
/// </summary>
public static class ConfigArchive
{
    private static readonly (string ArcName, string FileName)[] ExportableFiles =
    {
        ("commands.json", "commands.json"),
        ("ai_config.json", "ai_config.json"),
        ("audio_config.json", "audio_config.json"),
        ("overlay_config.json", "overlay_config.json"),
    };

    public static void Export(string baseDir, string destZipPath, bool allProfiles)
    {
        using var zip = ZipFile.Open(destZipPath, ZipArchiveMode.Create);

        foreach (var (arcName, fileName) in ExportableFiles)
        {
            var path = Path.Combine(baseDir, fileName);
            if (File.Exists(path)) zip.CreateEntryFromFile(path, arcName);
        }

        if (!allProfiles) return;

        var profilesDir = Path.Combine(baseDir, "profiles");
        if (Directory.Exists(profilesDir))
        {
            foreach (var file in Directory.EnumerateFiles(profilesDir, "*.json"))
            {
                if (file.EndsWith(".tmp", StringComparison.Ordinal)) continue;
                zip.CreateEntryFromFile(file, $"profiles/{Path.GetFileName(file)}");
            }
        }

        var metaPath = Path.Combine(baseDir, "profiles_config.json");
        if (File.Exists(metaPath)) zip.CreateEntryFromFile(metaPath, "profiles_config.json");
    }

    /// <summary>
    /// Extrait une archive produite par <see cref="Export"/>. Sécurité :
    /// n'extrait que les noms de fichiers attendus (jamais un membre
    /// arbitraire — protège contre le "zip slip"). Une entrée de config
    /// principale invalide (pas du JSON) fait échouer tout l'import ;
    /// une entrée de profil invalide est simplement ignorée.
    /// </summary>
    public static ConfigImportResult Import(string baseDir, string sourceZipPath, CommandStore commandStore)
    {
        var imported = new List<string>();
        var importedProfiles = new List<string>();
        try
        {
            using var zip = ZipFile.OpenRead(sourceZipPath);
            var entriesByName = zip.Entries.ToDictionary(e => e.FullName, e => e);

            foreach (var (arcName, fileName) in ExportableFiles)
            {
                if (!entriesByName.TryGetValue(arcName, out var entry)) continue;
                var text = ReadEntryText(entry);
                JsonNode.Parse(text); // lève si invalide -> abandonne tout l'import, comme côté Python
                File.WriteAllText(Path.Combine(baseDir, fileName), text);
                imported.Add(arcName);
            }

            var profilesDir = Path.Combine(baseDir, "profiles");
            Directory.CreateDirectory(profilesDir);
            foreach (var name in entriesByName.Keys.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (!name.StartsWith("profiles/", StringComparison.Ordinal)) continue;
                var baseName = name["profiles/".Length..];
                if (baseName.Length == 0 || baseName.Contains('/') || baseName.Contains('\\') ||
                    !baseName.EndsWith(".json", StringComparison.Ordinal) || baseName.EndsWith(".tmp", StringComparison.Ordinal))
                    continue;

                var text = ReadEntryText(entriesByName[name]);
                try
                {
                    JsonNode.Parse(text);
                }
                catch
                {
                    continue; // membre corrompu : ignoré plutôt que de faire échouer tout l'import
                }
                File.WriteAllText(Path.Combine(profilesDir, baseName), text);
                importedProfiles.Add(baseName);
            }

            if (importedProfiles.Count > 0 && entriesByName.TryGetValue("profiles_config.json", out var metaEntry))
            {
                try
                {
                    var text = ReadEntryText(metaEntry);
                    JsonNode.Parse(text);
                    File.WriteAllText(Path.Combine(baseDir, "profiles_config.json"), text);
                }
                catch
                {
                    // Best effort, comme côté Python.
                }
            }
        }
        catch (Exception e)
        {
            return new ConfigImportResult(false, imported, importedProfiles.Count, e.Message);
        }

        if (imported.Count == 0 && importedProfiles.Count == 0)
            return new ConfigImportResult(false, imported, 0, "Aucun fichier de configuration reconnu dans cette archive.");

        // commands.json vient d'être écrasé directement par l'archive, en
        // contournant save_commands() : sans cette resynchronisation, le
        // profil actif garderait ses anciennes commandes sur disque. Pas
        // nécessaire si l'archive contenait déjà des profils complets.
        if (imported.Contains("commands.json") && commandStore.ActiveProfileId is not null && importedProfiles.Count == 0)
        {
            try
            {
                var importedCommands = commandStore.LoadCommands();
                var (name, _, gameMode) = commandStore.ReadProfile(commandStore.ActiveProfileId);
                commandStore.WriteProfile(commandStore.ActiveProfileId, name, importedCommands, gameMode);
            }
            catch
            {
                // Best effort : l'import principal a déjà réussi à ce stade.
            }
        }

        return new ConfigImportResult(true, imported, importedProfiles.Count);
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
