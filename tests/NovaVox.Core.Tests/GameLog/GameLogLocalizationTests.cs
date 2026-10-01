using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class GameLogLocalizationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novavox-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void FindGlobalIniPath_SiblingExists_ReturnsItsPath()
    {
        var gameLogPath = Path.Combine(_dir, "Game.log");
        File.WriteAllText(gameLogPath, "");
        var iniDir = Directory.CreateDirectory(Path.Combine(_dir, "data", "Localization", "english"));
        var iniPath = Path.Combine(iniDir.FullName, "global.ini");
        File.WriteAllText(iniPath, "");

        Assert.Equal(iniPath, GameLogLocalization.FindGlobalIniPath(gameLogPath));
    }

    [Fact]
    public void FindGlobalIniPath_NoIniFile_ReturnsNull()
    {
        var gameLogPath = Path.Combine(_dir, "Game.log");
        File.WriteAllText(gameLogPath, "");

        Assert.Null(GameLogLocalization.FindGlobalIniPath(gameLogPath));
    }

    [Fact]
    public void FindGlobalIniPath_NullLiveLogPath_ReturnsNull()
    {
        Assert.Null(GameLogLocalization.FindGlobalIniPath(null));
    }

    [Fact]
    public void ReadKeyedValues_ReturnsOnlyRequestedKeys()
    {
        var iniPath = Path.Combine(_dir, "global.ini");
        File.WriteAllLines(iniPath, new[]
        {
            "wanted_key_1=Valeur un",
            "unrelated_key=Ignoré",
            "wanted_key_2=Valeur deux",
        });

        var result = GameLogLocalization.ReadKeyedValues(iniPath, new HashSet<string> { "wanted_key_1", "wanted_key_2" });

        Assert.Equal(2, result.Count);
        Assert.Equal("Valeur un", result["wanted_key_1"]);
        Assert.Equal("Valeur deux", result["wanted_key_2"]);
    }

    [Fact]
    public void ReadKeyedValues_StripsTrailingCommaPSuffixFromKey()
    {
        var iniPath = Path.Combine(_dir, "global.ini");
        File.WriteAllLines(iniPath, new[] { "plural_key,P=Texte avec pluriel" });

        var result = GameLogLocalization.ReadKeyedValues(iniPath, new HashSet<string> { "plural_key" });

        Assert.Equal("Texte avec pluriel", result["plural_key"]);
    }

    [Fact]
    public void ReadKeyedValues_IgnoresMalformedLinesWithoutEquals()
    {
        var iniPath = Path.Combine(_dir, "global.ini");
        File.WriteAllLines(iniPath, new[] { "no equals sign here", "wanted_key=Valeur" });

        var result = GameLogLocalization.ReadKeyedValues(iniPath, new HashSet<string> { "wanted_key" });

        Assert.Single(result);
        Assert.Equal("Valeur", result["wanted_key"]);
    }

    [Fact]
    public void ReadKeyedValues_EmptyKeysOfInterest_ReturnsEmptyWithoutReadingFile()
    {
        var iniPath = Path.Combine(_dir, "does-not-exist.ini");
        Assert.Empty(GameLogLocalization.ReadKeyedValues(iniPath, new HashSet<string>()));
    }

    [Fact]
    public void ReadKeyedValues_MissingFile_ReturnsEmptyRatherThanThrowing()
    {
        var iniPath = Path.Combine(_dir, "does-not-exist.ini");
        Assert.Empty(GameLogLocalization.ReadKeyedValues(iniPath, new HashSet<string> { "any_key" }));
    }
}
