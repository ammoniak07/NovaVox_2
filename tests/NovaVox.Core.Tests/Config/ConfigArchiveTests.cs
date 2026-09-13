using NovaVox.Core.Commands;
using NovaVox.Core.Config;
using Xunit;

namespace NovaVox.Core.Tests.Config;

public class ConfigArchiveTests : IDisposable
{
    private readonly string _sourceDir = Directory.CreateTempSubdirectory("novavox-tests-src-").FullName;
    private readonly string _destDir = Directory.CreateTempSubdirectory("novavox-tests-dst-").FullName;
    private readonly string _zipPath;

    public ConfigArchiveTests()
    {
        _zipPath = Path.Combine(Directory.CreateTempSubdirectory("novavox-tests-zip-").FullName, "export.zip");
    }

    public void Dispose()
    {
        Directory.Delete(_sourceDir, recursive: true);
        Directory.Delete(_destDir, recursive: true);
        if (File.Exists(_zipPath)) Directory.Delete(Path.GetDirectoryName(_zipPath)!, recursive: true);
    }

    [Fact]
    public void ExportThenImport_RoundTripsMainConfigFiles()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "commands.json"), """[{"type":"command","phrase":"x","keys":"n"}]""");
        File.WriteAllText(Path.Combine(_sourceDir, "ai_config.json"), """{"gemini_name":"Gemini"}""");

        ConfigArchive.Export(_sourceDir, _zipPath, allProfiles: false);

        var destStore = new CommandStore(_destDir);
        var result = ConfigArchive.Import(_destDir, _zipPath, destStore);

        Assert.True(result.Ok);
        Assert.Contains("commands.json", result.Imported);
        Assert.Contains("ai_config.json", result.Imported);
        Assert.Equal(
            File.ReadAllText(Path.Combine(_sourceDir, "commands.json")),
            File.ReadAllText(Path.Combine(_destDir, "commands.json")));
    }

    [Fact]
    public void Export_WithAllProfiles_IncludesProfilesDirectory()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "commands.json"), "[]");
        var sourceStore = new CommandStore(_sourceDir);
        var migratedId = sourceStore.EnsureProfilesMigrated();
        Assert.NotNull(migratedId);

        ConfigArchive.Export(_sourceDir, _zipPath, allProfiles: true);

        var destStore = new CommandStore(_destDir);
        var result = ConfigArchive.Import(_destDir, _zipPath, destStore);

        Assert.True(result.Ok);
        Assert.Equal(1, result.ImportedProfileCount);
        Assert.Single(destStore.ListProfiles());
    }

    [Fact]
    public void Import_FailsAndImportsNothingWhenMainConfigFileIsInvalidJson()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "commands.json"), "not json at all");
        ConfigArchive.Export(_sourceDir, _zipPath, allProfiles: false);

        var destStore = new CommandStore(_destDir);
        var result = ConfigArchive.Import(_destDir, _zipPath, destStore);

        Assert.False(result.Ok);
        Assert.False(File.Exists(Path.Combine(_destDir, "commands.json")));
    }

    [Fact]
    public void Import_ReturnsErrorWhenArchiveHasNoRecognizedFiles()
    {
        using (var zip = System.IO.Compression.ZipFile.Open(_zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("unrelated.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("hello");
        }

        var destStore = new CommandStore(_destDir);
        var result = ConfigArchive.Import(_destDir, _zipPath, destStore);

        Assert.False(result.Ok);
        Assert.Equal("Aucun fichier de configuration reconnu dans cette archive.", result.Error);
    }

    [Fact]
    public void Import_ResyncsActiveProfileWhenOnlyCommandsJsonImported()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "commands.json"),
            """[{"type":"command","phrase":"nouvelle","keys":"x"}]""");
        ConfigArchive.Export(_sourceDir, _zipPath, allProfiles: false);

        var destStore = new CommandStore(_destDir);
        destStore.SaveCommands(new List<VoiceCommand> { new() { Phrase = "ancienne", Keys = "y" } });
        var activeId = destStore.EnsureProfilesMigrated() ?? destStore.LoadActiveProfileId();
        Assert.NotNull(activeId);
        destStore.ActiveProfileId = activeId;

        var result = ConfigArchive.Import(_destDir, _zipPath, destStore);

        Assert.True(result.Ok);
        var (_, profileCommands) = destStore.ReadProfile(activeId!);
        Assert.Equal("nouvelle", profileCommands[0].Phrase);
    }
}
