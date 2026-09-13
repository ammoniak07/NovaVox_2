using NovaVox.Core.Config;
using Xunit;

namespace NovaVox.Core.Tests.Config;

public class GameModeConfigTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novavox-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_NoFile_ReturnsStarCitizenDefaults()
    {
        var config = new GameModeConfigStore(_dir).Load();
        Assert.Equal(GameModeConfig.StarCitizen, config.CurrentMode);
        Assert.True(config.ForStarCitizen.GameLogEnabled);
        Assert.True(config.ForStarCitizen.GeminiWikiEnabled);
        Assert.False(config.ForOther.GameLogEnabled);
        Assert.False(config.ForOther.GeminiWikiEnabled);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsBothModeBundles()
    {
        var store = new GameModeConfigStore(_dir);
        var config = new GameModeConfig
        {
            CurrentMode = GameModeConfig.Other,
            ForStarCitizen = new GameModeSettings
            {
                ListenHotkey = "ctrl+f9", OverlayBgOpacity = 60, OverlayTextOpacity = 90,
                GameLogEnabled = true, GeminiWikiEnabled = true, CommandProfileId = "p1",
            },
            ForOther = new GameModeSettings
            {
                ListenHotkey = "ctrl+f10", OverlayBgOpacity = 40, OverlayTextOpacity = 80,
                GameLogEnabled = false, GeminiWikiEnabled = false, CommandProfileId = "p2",
            },
        };
        store.Save(config);

        var reloaded = new GameModeConfigStore(_dir).Load();
        Assert.Equal(GameModeConfig.Other, reloaded.CurrentMode);
        Assert.Equal("ctrl+f9", reloaded.ForStarCitizen.ListenHotkey);
        Assert.Equal(60, reloaded.ForStarCitizen.OverlayBgOpacity);
        Assert.Equal(90, reloaded.ForStarCitizen.OverlayTextOpacity);
        Assert.Equal("p1", reloaded.ForStarCitizen.CommandProfileId);
        Assert.Equal("ctrl+f10", reloaded.ForOther.ListenHotkey);
        Assert.Equal(40, reloaded.ForOther.OverlayBgOpacity);
        Assert.Equal(80, reloaded.ForOther.OverlayTextOpacity);
        Assert.False(reloaded.ForOther.GameLogEnabled);
        Assert.Equal("p2", reloaded.ForOther.CommandProfileId);
    }

    [Fact]
    public void ForMode_ReturnsMatchingBundle()
    {
        var config = new GameModeConfig();
        Assert.Same(config.ForStarCitizen, config.ForMode(GameModeConfig.StarCitizen));
        Assert.Same(config.ForOther, config.ForMode(GameModeConfig.Other));
        Assert.Same(config.ForStarCitizen, config.ForMode("unknown")); // repli sur Star Citizen
    }

    [Fact]
    public void Load_ClampsOutOfRangeOpacity()
    {
        File.WriteAllText(Path.Combine(_dir, "game_mode.json"), """
        {"current_mode": "sc", "sc": {"overlay_bg_opacity": 999, "overlay_text_opacity": -5}}
        """);
        var config = new GameModeConfigStore(_dir).Load();
        Assert.Equal(100, config.ForStarCitizen.OverlayBgOpacity);
        Assert.Equal(0, config.ForStarCitizen.OverlayTextOpacity);
    }
}
