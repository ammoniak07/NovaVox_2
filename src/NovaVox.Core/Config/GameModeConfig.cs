using System.Text.Json.Nodes;
using static NovaVox.Core.Json.JsonHelpers;

namespace NovaVox.Core.Config;

/// <summary>
/// Réglages qui n'ont de sens que pour un jeu donné, rattachés au mode
/// plutôt qu'à un profil de commandes (CommandStore) : bascule d'un coup
/// avec le sélecteur "Mode de jeu" de l'en-tête (GameModeCombo,
/// MainWindow.xaml.cs). Le profil de commandes actif suit lui aussi le
/// mode via <see cref="CommandProfileId"/> — le dernier profil de
/// commandes utilisé pendant ce mode, retrouvé automatiquement au retour
/// dessus (voir AppState.SwitchGameMode).
/// </summary>
public sealed class GameModeSettings
{
    public string? ListenHotkey { get; set; }
    public int OverlayBgOpacity { get; set; } = OverlayConfig.DefaultBgOpacity;
    public int OverlayTextOpacity { get; set; } = OverlayConfig.DefaultTextOpacity;
    public bool GameLogEnabled { get; set; } = true;
    public bool GeminiWikiEnabled { get; set; } = true;
    public string? CommandProfileId { get; set; }
    /// <summary>Thème de couleurs de l'interface (un id de AiConfig.ValidThemes) propre à ce mode — voir AppState.SwitchGameMode.</summary>
    public string UiTheme { get; set; } = AiConfig.DefaultUiTheme;
}

public sealed class GameModeConfig
{
    public const string StarCitizen = "sc";
    public const string Other = "other";

    public string CurrentMode { get; set; } = StarCitizen;
    public GameModeSettings ForStarCitizen { get; set; } = new() { GameLogEnabled = true, GeminiWikiEnabled = true, UiTheme = AiConfig.DefaultUiTheme };
    // Thème "militaire" par défaut pour "Autre jeu" tant que l'utilisateur n'a pas choisi lui-même
    // (sélecteur d'en-tête) : distingue immédiatement ce mode de Star Citizen sans réglage préalable.
    public GameModeSettings ForOther { get; set; } = new() { GameLogEnabled = false, GeminiWikiEnabled = false, UiTheme = "military" };

    public GameModeSettings ForMode(string mode) => mode == Other ? ForOther : ForStarCitizen;
}

/// <summary>Port JSON de GameModeConfig — game_mode.json, nouveau fichier propre à ce portage .NET (n'existe pas côté Python).</summary>
public sealed class GameModeConfigStore
{
    private readonly string _path;

    public GameModeConfigStore(string baseDir)
    {
        _path = Path.Combine(baseDir, "game_mode.json");
    }

    public GameModeConfig Load()
    {
        if (!File.Exists(_path)) return new GameModeConfig();
        try
        {
            var data = ParseFile(_path) as JsonObject;
            if (data is null) return new GameModeConfig();
            return new GameModeConfig
            {
                CurrentMode = GetString(data["current_mode"]) == GameModeConfig.Other ? GameModeConfig.Other : GameModeConfig.StarCitizen,
                ForStarCitizen = ParseSettings(data["sc"] as JsonObject, defaultEnabled: true),
                ForOther = ParseSettings(data["other"] as JsonObject, defaultEnabled: false),
            };
        }
        catch
        {
            return new GameModeConfig();
        }
    }

    private static GameModeSettings ParseSettings(JsonObject? data, bool defaultEnabled)
    {
        var settings = new GameModeSettings { GameLogEnabled = defaultEnabled, GeminiWikiEnabled = defaultEnabled };
        if (data is null) return settings;
        settings.ListenHotkey = GetStringOrNull(data["listen_hotkey"]);
        settings.OverlayBgOpacity = Math.Clamp(GetInt(data["overlay_bg_opacity"]) ?? OverlayConfig.DefaultBgOpacity, 0, 100);
        settings.OverlayTextOpacity = Math.Clamp(GetInt(data["overlay_text_opacity"]) ?? OverlayConfig.DefaultTextOpacity, 0, 100);
        settings.GameLogEnabled = GetBool(data["game_log_enabled"], defaultEnabled);
        settings.GeminiWikiEnabled = GetBool(data["gemini_wiki_enabled"], defaultEnabled);
        settings.CommandProfileId = GetStringOrNull(data["command_profile_id"]);
        settings.UiTheme = GetStringOrNull(data["ui_theme"]) is { } theme && AiConfig.ValidThemes.Contains(theme)
            ? theme : AiConfig.DefaultUiTheme;
        return settings;
    }

    public void Save(GameModeConfig config)
    {
        var data = new JsonObject
        {
            ["current_mode"] = config.CurrentMode,
            ["sc"] = ToJson(config.ForStarCitizen),
            ["other"] = ToJson(config.ForOther),
        };
        try
        {
            File.WriteAllText(_path, data.ToJsonString(WriteOptions));
        }
        catch
        {
            // Confort seulement, jamais bloquant.
        }
    }

    private static JsonObject ToJson(GameModeSettings settings) => new()
    {
        ["listen_hotkey"] = settings.ListenHotkey,
        ["overlay_bg_opacity"] = settings.OverlayBgOpacity,
        ["overlay_text_opacity"] = settings.OverlayTextOpacity,
        ["game_log_enabled"] = settings.GameLogEnabled,
        ["gemini_wiki_enabled"] = settings.GeminiWikiEnabled,
        ["command_profile_id"] = settings.CommandProfileId,
        ["ui_theme"] = settings.UiTheme,
    };
}
