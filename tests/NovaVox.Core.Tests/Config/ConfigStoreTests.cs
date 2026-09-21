using NovaVox.Core.Config;
using Xunit;

namespace NovaVox.Core.Tests.Config;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novavox-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void AudioConfig_ClampsAndValidatesOnLoad()
    {
        File.WriteAllText(Path.Combine(_dir, "audio_config.json"), """
        {"mic_gain": 999, "mic_gate": -5, "listen_mode": "bogus", "listen_hotkey": "Ctrl+F9",
         "kb_layout": "qwerty", "aec_enabled": true, "tts_volume": 5}
        """);
        var config = new AudioConfigStore(_dir).Load();
        Assert.Equal(3.0, config.MicGain);
        Assert.Equal(0, config.MicGate);
        Assert.Equal(AudioConfig.DefaultListenMode, config.ListenMode); // "bogus" rejeté
        Assert.Equal("ctrl+f9", config.ListenHotkey);
        Assert.Equal("qwerty", config.KbLayout);
        Assert.True(config.AecEnabled);
        Assert.Equal(1.5, config.TtsVolume);
    }

    [Fact]
    public void AudioConfig_JoystickHotkeyKeepsCase()
    {
        File.WriteAllText(Path.Combine(_dir, "audio_config.json"), """
        {"listen_hotkey": "joy:{\"name\":\"XInput Controller\",\"guid\":\"ABC\",\"button\":3}"}
        """);
        var config = new AudioConfigStore(_dir).Load();
        Assert.StartsWith("joy:", config.ListenHotkey);
        Assert.Contains("XInput Controller", config.ListenHotkey!);
    }

    [Fact]
    public void AudioConfig_RoundTrips()
    {
        var store = new AudioConfigStore(_dir);
        var config = new AudioConfig { InputDevice = "Micro USB", MicGain = 1.5, ListenMode = "toggle_key" };
        store.Save(config);
        var reloaded = new AudioConfigStore(_dir).Load();
        Assert.Equal("Micro USB", reloaded.InputDevice);
        Assert.Equal(1.5, reloaded.MicGain);
        Assert.Equal("toggle_key", reloaded.ListenMode);
    }

    [Fact]
    public void WindowConfig_DefaultsWhenNoFile()
    {
        var bounds = new WindowConfigStore(_dir).Load();
        Assert.Equal(WindowConfigStore.DefaultWidth, bounds.Width);
        Assert.Equal(WindowConfigStore.DefaultHeight, bounds.Height);
        Assert.Null(bounds.X);
    }

    [Fact]
    public void WindowConfig_IgnoresPositionOffAllScreens()
    {
        var store = new WindowConfigStore(_dir);
        store.Save(1000, 900, x: 5000, y: 5000);
        var bounds = store.Load(virtualScreenBounds: (0, 0, 1920, 1080));
        Assert.Null(bounds.X);
        Assert.Null(bounds.Y);
    }

    [Fact]
    public void WindowConfig_KeepsPositionWithinScreen()
    {
        var store = new WindowConfigStore(_dir);
        store.Save(1000, 900, x: 100, y: 100);
        var bounds = store.Load(virtualScreenBounds: (0, 0, 1920, 1080));
        Assert.Equal(100, bounds.X);
        Assert.Equal(100, bounds.Y);
    }

    [Fact]
    public void OverlayConfig_ValidatesHexColorAndOpacity()
    {
        File.WriteAllText(Path.Combine(_dir, "overlay_config.json"), """
        {"enabled": true, "bg_color": "not-a-color", "bg_opacity": 250, "text_opacity": -10}
        """);
        var config = new OverlayConfigStore(_dir).Load();
        Assert.True(config.Enabled);
        Assert.Equal(OverlayConfig.DefaultBgColor, config.BgColor);
        Assert.Equal(100, config.BgOpacity);
        Assert.Equal(0, config.TextOpacity);
    }

    [Fact]
    public void OverlayConfig_PreservesUnknownRowsDefaultVisible()
    {
        File.WriteAllText(Path.Combine(_dir, "overlay_config.json"), """{"visible_rows": {"mic": false}}""");
        var config = new OverlayConfigStore(_dir).Load();
        Assert.False(config.VisibleRows["mic"]);
        Assert.True(config.VisibleRows["zone"]);
    }

    /// <summary>
    /// Régression : OverlayWindow.OnClosing ne sauvegarde que la position
    /// (enabled + x/y), sans repasser bgColor/bgOpacity/textColor/
    /// textOpacity — un Save() qui reconstruisait le JSON à partir de
    /// rien effaçait alors l'opacité/couleur choisies par l'utilisateur à
    /// chaque fermeture de l'overlay (donc à chaque fermeture de
    /// NovaVox), qui revenait à l'opacité par défaut au lancement suivant.
    /// </summary>
    [Fact]
    public void OverlayConfig_SaveWithoutAppearance_PreservesPreviouslySavedAppearance()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, bgColor: "#123456", bgOpacity: 40, textColor: "#abcdef", textOpacity: 55);

        store.Save(enabled: true, x: 100, y: 200); // ex. OverlayWindow.OnClosing : position seulement

        var config = store.Load();
        Assert.Equal(100, config.X);
        Assert.Equal(200, config.Y);
        Assert.Equal("#123456", config.BgColor);
        Assert.Equal(40, config.BgOpacity);
        Assert.Equal("#abcdef", config.TextColor);
        Assert.Equal(55, config.TextOpacity);
    }

    [Fact]
    public void AiConfig_FallsBackWhenModelUnknown()
    {
        File.WriteAllText(Path.Combine(_dir, "ai_config.json"), """{"gemini_model": "gemini-retired-model"}""");
        var config = new AiConfigStore(_dir).Load(knownGeminiModels: new[] { "gemini-3.6-flash" });
        Assert.Equal(AiConfig.DefaultGeminiModel, config.GeminiModel);
    }

    [Fact]
    public void AiConfig_UiThemeDefaultsToDarkAndRejectsUnknownValue()
    {
        File.WriteAllText(Path.Combine(_dir, "ai_config.json"), """{"ui_theme": "purple"}""");
        var config = new AiConfigStore(_dir).Load();
        Assert.Equal("dark", config.UiTheme);
    }

    [Fact]
    public void AiConfig_UiThemeRoundTripsLight()
    {
        var store = new AiConfigStore(_dir);
        store.Save(new AiConfig { UiTheme = "light" });
        Assert.Equal("light", new AiConfigStore(_dir).Load().UiTheme);
    }

    [Theory]
    [InlineData("military")]
    [InlineData("cyberpunk")]
    [InlineData("amber")]
    [InlineData("ocean")]
    public void AiConfig_UiThemeRoundTripsNewPalettes(string theme)
    {
        var store = new AiConfigStore(_dir);
        store.Save(new AiConfig { UiTheme = theme });
        Assert.Equal(theme, new AiConfigStore(_dir).Load().UiTheme);
    }

    [Fact]
    public void AiConfig_ShowSystemLogDefaultsToTrue()
    {
        var config = new AiConfigStore(_dir).Load();
        Assert.True(config.ShowSystemLog);
    }

    [Fact]
    public void AiConfig_ShowSystemLogRoundTripsFalse()
    {
        var store = new AiConfigStore(_dir);
        store.Save(new AiConfig { ShowSystemLog = false });
        Assert.False(new AiConfigStore(_dir).Load().ShowSystemLog);
    }

    [Fact]
    public void AiConfig_GeminiWikiEnabledDefaultsToTrue()
    {
        var config = new AiConfigStore(_dir).Load();
        Assert.True(config.GeminiWikiEnabled);
    }

    [Fact]
    public void AiConfig_GeminiWikiEnabledRoundTripsFalse()
    {
        var store = new AiConfigStore(_dir);
        store.Save(new AiConfig { GeminiWikiEnabled = false });
        Assert.False(new AiConfigStore(_dir).Load().GeminiWikiEnabled);
    }

    [Fact]
    public void AiConfig_RoundTripsGameLogDictionaries()
    {
        var store = new AiConfigStore(_dir);
        var config = new AiConfig();
        config.GameLogHudOverrides["Pyro I"] = "Pyro Un";
        store.Save(config);
        var reloaded = new AiConfigStore(_dir).Load();
        Assert.Equal("Pyro Un", reloaded.GameLogHudOverrides["Pyro I"]);
    }

    [Fact]
    public void AiConfig_RoundTripsGameLogCustomPath()
    {
        var store = new AiConfigStore(_dir);
        store.Save(new AiConfig { GameLogCustomPath = @"D:\Jeux\StarCitizen\LIVE\Game.log" });
        Assert.Equal(@"D:\Jeux\StarCitizen\LIVE\Game.log", new AiConfigStore(_dir).Load().GameLogCustomPath);
    }

    [Fact]
    public void AiConfig_GameLogCustomPathDefaultsToEmpty()
    {
        Assert.Equal("", new AiConfigStore(_dir).Load().GameLogCustomPath);
    }

    [Fact]
    public void AiConfig_RoundTripsShipCheatSheets()
    {
        var store = new AiConfigStore(_dir);
        var config = new AiConfig { ActiveShipCheatSheet = "Perseus" };
        config.ShipCheatSheets["Perseus"] = new Dictionary<string, string>
        {
            ["Tourelle dorsale"] = "Sur le dessus, accès par l'échelle centrale",
            ["Tourelle ventrale"] = "Sous la coque, accès par la soute",
        };
        config.ShipCheatSheets["Constellation Taurus"] = new Dictionary<string, string>
        {
            ["Tribord"] = "Côté droit en regardant vers l'avant",
        };
        // Couleur réglée seulement pour "Tourelle dorsale" : "Tourelle
        // ventrale" reste sans entrée, comme un repère jamais recoloré
        // (voir AiConfig_ShipCheatSheetsDefaultToEmpty pour le cas d'une
        // config antérieure à cette fonctionnalité).
        config.ShipCheatSheetColors["Perseus"] = new Dictionary<string, string>
        {
            ["Tourelle dorsale"] = "#FF4D4D",
        };
        store.Save(config);

        var reloaded = new AiConfigStore(_dir).Load();

        Assert.Equal("Perseus", reloaded.ActiveShipCheatSheet);
        Assert.Equal(2, reloaded.ShipCheatSheets.Count);
        Assert.Equal("Sur le dessus, accès par l'échelle centrale", reloaded.ShipCheatSheets["Perseus"]["Tourelle dorsale"]);
        Assert.Equal("Sous la coque, accès par la soute", reloaded.ShipCheatSheets["Perseus"]["Tourelle ventrale"]);
        Assert.Equal("Côté droit en regardant vers l'avant", reloaded.ShipCheatSheets["Constellation Taurus"]["Tribord"]);
        Assert.Equal("#FF4D4D", reloaded.ShipCheatSheetColors["Perseus"]["Tourelle dorsale"]);
        Assert.False(reloaded.ShipCheatSheetColors["Perseus"].ContainsKey("Tourelle ventrale"));
        Assert.False(reloaded.ShipCheatSheetColors.ContainsKey("Constellation Taurus"));
    }

    [Fact]
    public void AiConfig_ShipCheatSheetsDefaultToEmpty()
    {
        var config = new AiConfigStore(_dir).Load();
        Assert.Empty(config.ShipCheatSheets);
        Assert.Empty(config.ShipCheatSheetColors);
        Assert.Equal("", config.ActiveShipCheatSheet);
    }

    [Fact]
    public void AiConfig_LoadMergesLegacyNameTemplateHudOverrides()
    {
        // Corrections HUD enregistrées avant le regroupement par gabarit
        // ({name}...) : une entrée séparée par nom de joueur rencontré,
        // pour le même type d'événement — doivent être fusionnées au
        // chargement plutôt que de rester des doublons pour toujours.
        File.WriteAllText(Path.Combine(_dir, "ai_config.json"), """
        {
            "game_log_hud_overrides": {
                "MAEDAYMAEDAY a commis Blessures corporelles graves contre vous.": "{name} a commis Blessures corporelles graves contre vous.",
                "Brick_Century a commis Vol de biens contre vous.": "{name} a commis Vol de biens contre vous."
            }
        }
        """);

        var config = new AiConfigStore(_dir).Load();

        Assert.Equal(2, config.GameLogHudOverrides.Count);
        Assert.True(config.GameLogHudOverrides.ContainsKey("{name} a commis Blessures corporelles graves contre vous."));
        Assert.True(config.GameLogHudOverrides.ContainsKey("{name} a commis Vol de biens contre vous."));

        // La fusion doit être réécrite sur disque tout de suite (pas seulement
        // gardée en mémoire) : sinon ai_config.json garde les vieilles entrées
        // tant que l'utilisateur ne change aucun autre réglage — ce qui, vu de
        // l'extérieur (ou en rouvrant juste le fichier), donne l'impression
        // que la fusion "n'a pas marché" alors qu'elle a bien eu lieu en mémoire.
        var onDisk = File.ReadAllText(Path.Combine(_dir, "ai_config.json"));
        Assert.Contains("{name} a commis Blessures corporelles graves contre vous.", onDisk);
        Assert.DoesNotContain("MAEDAYMAEDAY", onDisk);
    }

    [Fact]
    public void AiConfig_LoadPrunesDestinationAliasesAlreadyInBuiltInCatalog()
    {
        // "ooc stanton 1 hurston" est déjà dans GameLogDestinations.KnownLocationAliases :
        // une entrée personnelle pour ce lieu ne fait que dupliquer (parfois avec un
        // texte périmé) ce que le catalogue intégré sait déjà annoncer — doit être
        // supprimée au chargement, contrairement à un lieu vraiment inconnu.
        File.WriteAllText(Path.Combine(_dir, "ai_config.json"), """
        {
            "game_log_destination_aliases": {
                "ooc stanton 1 hurston": "Hurston",
                "ma station perso": "Ma station perso"
            }
        }
        """);

        var config = new AiConfigStore(_dir).Load();

        Assert.Single(config.GameLogDestinationAliases);
        Assert.True(config.GameLogDestinationAliases.ContainsKey("ma station perso"));

        var onDisk = File.ReadAllText(Path.Combine(_dir, "ai_config.json"));
        Assert.DoesNotContain("ooc stanton 1 hurston", onDisk);
        Assert.Contains("ma station perso", onDisk);
    }

    [Fact]
    public void AiConfig_LoadPrunesJumpPointResolvableDestinationAliasesAndCollapsesInstanceSuffixes()
    {
        // Deux cas restés visibles après le premier correctif : des points de
        // saut déjà résolubles par l'algorithme (absents de KnownLocationAliases
        // mais couverts quand même), et des lieux génériques dupliqués parce que
        // seul le numéro d'instance aléatoire final changeait d'une rencontre à
        // l'autre — les deux doivent être nettoyés au chargement.
        File.WriteAllText(Path.Combine(_dir, "ai_config.json"), """
        {
            "game_log_destination_aliases": {
                "rs ext pyro stan jp1": "Stanton Gateway",
                "mission qt bounty beacon 816657711603": "Bounty Beacon",
                "mission qt bounty beacon 816663687671": "Bounty Beacon",
                "ma station perso": "Ma station perso"
            }
        }
        """);

        var config = new AiConfigStore(_dir).Load();

        Assert.Equal(2, config.GameLogDestinationAliases.Count);
        Assert.True(config.GameLogDestinationAliases.ContainsKey("mission qt bounty beacon"));
        Assert.True(config.GameLogDestinationAliases.ContainsKey("ma station perso"));

        var onDisk = File.ReadAllText(Path.Combine(_dir, "ai_config.json"));
        Assert.DoesNotContain("rs ext pyro stan jp1", onDisk);
        Assert.DoesNotContain("816657711603", onDisk);
        Assert.DoesNotContain("816663687671", onDisk);
        Assert.Contains("mission qt bounty beacon", onDisk);
    }
}
