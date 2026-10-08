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
        Assert.Equal(OverlayConfig.DefaultScale, config.Scale);
    }

    [Fact]
    public void OverlayConfig_RoundTripsScale()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, scale: 1.3);
        Assert.Equal(1.3, store.Load().Scale);
    }

    [Fact]
    public void OverlayConfig_ClampsScaleToValidRange()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, scale: 99.0);
        Assert.Equal(OverlayConfig.MaxScale, store.Load().Scale);

        store.Save(enabled: true, scale: 0.01);
        Assert.Equal(OverlayConfig.MinScale, store.Load().Scale);
    }

    // Largeur de base de la colonne principale (Réglages > 🖥 Overlay,
    // curseur "Largeur" -> OverlayWindow.ApplyBaseWidth) — une largeur
    // MINIMALE (voir OverlayConfig.BaseWidth), pas fixe.
    [Fact]
    public void OverlayConfig_RoundTripsBaseWidth()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, baseWidth: 420);
        Assert.Equal(420, store.Load().BaseWidth);
    }

    [Fact]
    public void OverlayConfig_ClampsBaseWidthToValidRange()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, baseWidth: 9999);
        Assert.Equal(OverlayConfig.MaxBaseWidth, store.Load().BaseWidth);

        store.Save(enabled: true, baseWidth: 1);
        Assert.Equal(OverlayConfig.MinBaseWidth, store.Load().BaseWidth);
    }

    [Fact]
    public void OverlayConfig_SaveWithoutBaseWidth_PreservesPreviouslySavedWidth()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, baseWidth: 400);

        store.Save(enabled: true, scale: 1.1); // ne touche pas baseWidth

        var loaded = store.Load();
        Assert.Equal(1.1, loaded.Scale);
        Assert.Equal(400, loaded.BaseWidth);
    }

    // Couleur de texte par ligne (pastille dans l'overlay en mode édition,
    // voir OverlayWindow.SetRowTextColor) — une ligne absente du dictionnaire
    // utilise la couleur globale (TextColor), jamais de valeur null stockée.
    [Fact]
    public void OverlayConfig_RoundTripsRowTextColors()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, rowTextColors: new Dictionary<string, string> { ["mic"] = "#ff0000", ["zone"] = "#00ff00" });

        var loaded = store.Load().RowTextColors;

        Assert.Equal("#ff0000", loaded["mic"]);
        Assert.Equal("#00ff00", loaded["zone"]);
        Assert.False(loaded.ContainsKey("time")); // absente de l'appel : pas de couleur personnalisée -> couleur globale
        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public void OverlayConfig_RowTextColors_DropsInvalidHexAndUnknownKeys()
    {
        File.WriteAllText(Path.Combine(_dir, "overlay_config.json"),
            """{"row_text_colors": {"mic": "not-a-color", "zone": "#00ff00", "some_removed_row": "#123456"}}""");
        var loaded = new OverlayConfigStore(_dir).Load().RowTextColors;

        Assert.Equal("#00ff00", loaded["zone"]);
        Assert.False(loaded.ContainsKey("mic"));
        Assert.False(loaded.ContainsKey("some_removed_row"));
        Assert.Single(loaded);
    }

    [Fact]
    public void OverlayConfig_SaveWithoutRowTextColors_PreservesPreviouslySavedColors()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, rowTextColors: new Dictionary<string, string> { ["mic"] = "#abcdef" });

        store.Save(enabled: true, scale: 1.1); // ne touche pas rowTextColors

        var loaded = store.Load();
        Assert.Equal(1.1, loaded.Scale);
        Assert.Equal("#abcdef", loaded.RowTextColors["mic"]);
    }

    [Fact]
    public void OverlayConfig_PreservesUnknownRowsDefaultVisible()
    {
        File.WriteAllText(Path.Combine(_dir, "overlay_config.json"), """{"visible_rows": {"mic": false}}""");
        var config = new OverlayConfigStore(_dir).Load();
        Assert.False(config.VisibleRows["mic"]);
        Assert.True(config.VisibleRows["zone"]);
    }

    // Glisser-déposer en mode édition (OverlayWindow.ApplyRowOrder) : l'ordre
    // choisi par l'utilisateur doit survivre à un redémarrage.
    [Fact]
    public void OverlayConfig_RoundTripsRowOrder()
    {
        var store = new OverlayConfigStore(_dir);
        var customOrder = new List<string> { "mic", "zone", "time" };
        store.Save(enabled: true, rowOrder: customOrder);

        var loaded = store.Load().RowOrder;

        // Les 3 clés choisies arrivent en premier, dans l'ordre demandé ;
        // toutes les autres clés connues suivent (jamais perdues) pour que
        // rien ne disparaisse de l'overlay si RowOrder ne les mentionne pas.
        Assert.Equal(new[] { "mic", "zone", "time" }, loaded.Take(3));
        Assert.Equal(OverlayConfig.RowKeys.Length, loaded.Count);
        Assert.Equal(OverlayConfig.RowKeys.OrderBy(k => k), loaded.OrderBy(k => k));
    }

    [Fact]
    public void OverlayConfig_RowOrder_IgnoresUnknownKeysAndDuplicates()
    {
        File.WriteAllText(Path.Combine(_dir, "overlay_config.json"),
            """{"row_order": ["mic", "mic", "some_removed_row", "zone"]}""");
        var loaded = new OverlayConfigStore(_dir).Load().RowOrder;

        Assert.Equal(new[] { "mic", "zone" }, loaded.Take(2));
        Assert.Equal(OverlayConfig.RowKeys.Length, loaded.Count);
        Assert.Equal(loaded.Count, loaded.Distinct().Count());
    }

    [Fact]
    public void OverlayConfig_SaveWithoutRowOrder_PreservesPreviouslySavedOrder()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, rowOrder: new List<string> { "shipSheet", "time" });

        store.Save(enabled: true, scale: 1.2); // ne touche pas rowOrder

        var loaded = store.Load();
        Assert.Equal(1.2, loaded.Scale);
        Assert.Equal(new[] { "shipSheet", "time" }, loaded.RowOrder.Take(2));
    }

    // Colonnes de l'overlay (glisser-déposer horizontal en mode édition,
    // OverlayWindow.ApplyLayout) : chaque ligne peut être assignée à une
    // colonne, persistée séparément de RowOrder (l'ordre reste global,
    // toutes colonnes confondues — voir OverlayConfig.RowColumns).
    [Fact]
    public void OverlayConfig_RoundTripsRowColumns()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, rowColumns: new Dictionary<string, int> { ["mic"] = 1, ["zone"] = 2 });

        var loaded = store.Load().RowColumns;

        Assert.Equal(1, loaded["mic"]);
        Assert.Equal(2, loaded["zone"]);
        Assert.Equal(0, loaded["time"]); // absente de l'appel : colonne 0 par défaut
        Assert.Equal(OverlayConfig.RowKeys.Length, loaded.Count);
    }

    [Fact]
    public void OverlayConfig_RowColumns_ClampsOutOfRangeAndIgnoresUnknownKeys()
    {
        File.WriteAllText(Path.Combine(_dir, "overlay_config.json"),
            """{"row_columns": {"mic": 99, "zone": -1, "some_removed_row": 2}}""");
        var loaded = new OverlayConfigStore(_dir).Load().RowColumns;

        Assert.Equal(OverlayConfig.MaxColumns - 1, loaded["mic"]);
        Assert.Equal(0, loaded["zone"]);
        Assert.False(loaded.ContainsKey("some_removed_row"));
        Assert.Equal(OverlayConfig.RowKeys.Length, loaded.Count);
    }

    [Fact]
    public void OverlayConfig_SaveWithoutRowColumns_PreservesPreviouslySavedColumns()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, rowColumns: new Dictionary<string, int> { ["mic"] = 2 });

        store.Save(enabled: true, scale: 1.1); // ne touche pas rowColumns

        var loaded = store.Load();
        Assert.Equal(1.1, loaded.Scale);
        Assert.Equal(2, loaded.RowColumns["mic"]);
    }

    // Fenêtres détachées (glisser une ligne hors de l'overlay, voir
    // OverlayWindow.DetachRowToNewWindow) : quelle fenêtre (0 = principale,
    // sinon un identifiant de fenêtre satellite) héberge chaque ligne, et où
    // se trouve chaque fenêtre satellite à l'écran.
    [Fact]
    public void OverlayConfig_RoundTripsRowWindow()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, rowWindow: new Dictionary<string, int> { ["mic"] = 1, ["zone"] = 2 });

        var loaded = store.Load().RowWindow;

        Assert.Equal(1, loaded["mic"]);
        Assert.Equal(2, loaded["zone"]);
        Assert.Equal(0, loaded["time"]); // absente de l'appel : fenêtre principale par défaut
        Assert.Equal(OverlayConfig.RowKeys.Length, loaded.Count);
    }

    [Fact]
    public void OverlayConfig_RowWindow_NegativeFallsBackToMainAndIgnoresUnknownKeys()
    {
        File.WriteAllText(Path.Combine(_dir, "overlay_config.json"),
            """{"row_window": {"mic": 3, "zone": -1, "some_removed_row": 2}}""");
        var loaded = new OverlayConfigStore(_dir).Load().RowWindow;

        Assert.Equal(3, loaded["mic"]); // aucune borne supérieure, contrairement à RowColumns
        Assert.Equal(0, loaded["zone"]);
        Assert.False(loaded.ContainsKey("some_removed_row"));
        Assert.Equal(OverlayConfig.RowKeys.Length, loaded.Count);
    }

    [Fact]
    public void OverlayConfig_SaveWithoutRowWindow_PreservesPreviouslySavedWindow()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, rowWindow: new Dictionary<string, int> { ["mic"] = 2 });

        store.Save(enabled: true, scale: 1.1); // ne touche pas rowWindow

        var loaded = store.Load();
        Assert.Equal(1.1, loaded.Scale);
        Assert.Equal(2, loaded.RowWindow["mic"]);
    }

    [Fact]
    public void OverlayConfig_RoundTripsSatelliteWindowPositions()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, satelliteWindows: new Dictionary<int, (int X, int Y)> { [1] = (100, 200), [2] = (300, 400) });

        var loaded = store.Load().SatelliteWindows;

        Assert.Equal((100, 200), loaded[1]);
        Assert.Equal((300, 400), loaded[2]);
        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public void OverlayConfig_SatelliteWindowPositions_IgnoresNonPositiveOrMalformedIds()
    {
        File.WriteAllText(Path.Combine(_dir, "overlay_config.json"),
            """{"satellite_windows": {"1": {"x": 10, "y": 20}, "0": {"x": 1, "y": 1}, "-2": {"x": 1, "y": 1}, "abc": {"x": 1, "y": 1}, "3": {"x": 1}}}""");
        var loaded = new OverlayConfigStore(_dir).Load().SatelliteWindows;

        Assert.Equal((10, 20), loaded[1]);
        Assert.Single(loaded); // 0/-2 (identifiants invalides), "abc" (non numérique) et "3" (y manquant) ignorés
    }

    [Fact]
    public void OverlayConfig_SaveWithoutSatelliteWindows_PreservesPreviouslySavedPositions()
    {
        var store = new OverlayConfigStore(_dir);
        store.Save(enabled: true, satelliteWindows: new Dictionary<int, (int X, int Y)> { [1] = (50, 60) });

        store.Save(enabled: true, scale: 1.1); // ne touche pas satelliteWindows

        var loaded = store.Load();
        Assert.Equal(1.1, loaded.Scale);
        Assert.Equal((50, 60), loaded.SatelliteWindows[1]);
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
        store.Save(enabled: true, bgColor: "#123456", bgOpacity: 40, textColor: "#abcdef", textOpacity: 55, scale: 1.4);

        store.Save(enabled: true, x: 100, y: 200); // ex. OverlayWindow.OnClosing : position seulement

        var config = store.Load();
        Assert.Equal(100, config.X);
        Assert.Equal(200, config.Y);
        Assert.Equal("#123456", config.BgColor);
        Assert.Equal(40, config.BgOpacity);
        Assert.Equal("#abcdef", config.TextColor);
        Assert.Equal(55, config.TextOpacity);
        Assert.Equal(1.4, config.Scale);
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
    public void AiConfig_SchemasUnseenRoundTripsAndIgnoresCase()
    {
        var store = new AiConfigStore(_dir);
        var config = new AiConfig();
        config.SchemasUnseen.Add("Ezra");
        store.Save(config);

        Assert.Contains("ezra", new AiConfigStore(_dir).Load().SchemasUnseen);
    }

    [Fact]
    public void AiConfig_ConnectedGroupMembersRoundTripsAndIgnoresCase()
    {
        var store = new AiConfigStore(_dir);
        var config = new AiConfig();
        config.ConnectedGroupMembers.Add("Dionico31");
        store.Save(config);

        var reloaded = new AiConfigStore(_dir).Load();
        Assert.Contains("Dionico31", reloaded.ConnectedGroupMembers);
        Assert.Contains("dionico31", reloaded.ConnectedGroupMembers); // comparaison insensible à la casse préservée au rechargement
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
