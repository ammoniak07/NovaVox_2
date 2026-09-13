using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using static NovaVox.Core.Json.JsonHelpers;

namespace NovaVox.Core.Config;

public sealed class OverlayConfig
{
    public static readonly string[] RowKeys = { "time", "listening", "mic", "phrase", "zone", "lastCmd" };

    public const string DefaultBgColor = "#0a0e14";
    public const int DefaultBgOpacity = 72;
    public const string DefaultTextColor = "#dbe4ee";
    public const int DefaultTextOpacity = 100;

    public bool Enabled { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public Dictionary<string, bool> VisibleRows { get; set; } = RowKeys.ToDictionary(k => k, _ => true);
    public string BgColor { get; set; } = DefaultBgColor;
    public int BgOpacity { get; set; } = DefaultBgOpacity;
    public string TextColor { get; set; } = DefaultTextColor;
    public int TextOpacity { get; set; } = DefaultTextOpacity;
}

/// <summary>Port de load_overlay_config/save_overlay_config (app.py).</summary>
public sealed partial class OverlayConfigStore
{
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();

    private readonly string _path;

    public OverlayConfigStore(string baseDir)
    {
        _path = Path.Combine(baseDir, "overlay_config.json");
    }

    public static string ValidateHexColor(string? value, string fallback) =>
        value is not null && HexColorRegex().IsMatch(value) ? value : fallback;

    public static int ValidateOpacityPercent(JsonNode? value, int fallback)
    {
        var d = GetDouble(value);
        return d is null ? fallback : Math.Clamp((int)Math.Round(d.Value), 0, 100);
    }

    public OverlayConfig Load()
    {
        var config = new OverlayConfig();
        if (!File.Exists(_path)) return config;
        try
        {
            var data = ParseFile(_path) as JsonObject;
            config.Enabled = GetBool(data?["enabled"]);
            var rawX = GetInt(data?["x"]);
            var rawY = GetInt(data?["y"]);
            if (rawX is not null && rawY is not null) (config.X, config.Y) = (rawX, rawY);

            if (data?["visible_rows"] is JsonObject savedRows)
            {
                foreach (var k in OverlayConfig.RowKeys)
                {
                    if (savedRows[k] is { } node) config.VisibleRows[k] = GetBool(node, true);
                }
            }

            config.BgColor = ValidateHexColor(GetStringOrNull(data?["bg_color"]), OverlayConfig.DefaultBgColor);
            config.BgOpacity = ValidateOpacityPercent(data?["bg_opacity"], OverlayConfig.DefaultBgOpacity);
            config.TextColor = ValidateHexColor(GetStringOrNull(data?["text_color"]), OverlayConfig.DefaultTextColor);
            config.TextOpacity = ValidateOpacityPercent(data?["text_opacity"], OverlayConfig.DefaultTextOpacity);
        }
        catch
        {
            return new OverlayConfig();
        }
        return config;
    }

    /// <summary>
    /// N'importe quel paramètre laissé à null conserve la valeur déjà
    /// enregistrée sur disque plutôt que d'être remis à sa valeur par
    /// défaut — indispensable puisque plusieurs appelants ne mettent à
    /// jour qu'une partie des réglages (ex. OverlayWindow.OnClosing ne
    /// sauvegarde que la position) : sans cette fusion, un tel appel
    /// écrasait silencieusement la couleur/opacité déjà choisies par
    /// l'utilisateur à chaque fermeture de l'overlay.
    /// </summary>
    public void Save(
        bool enabled, int? x = null, int? y = null, Dictionary<string, bool>? visibleRows = null,
        string? bgColor = null, int? bgOpacity = null, string? textColor = null, int? textOpacity = null)
    {
        var existing = File.Exists(_path) ? TryParseFile(_path) : null;

        var data = new JsonObject { ["enabled"] = enabled };

        var savedX = x ?? GetInt(existing?["x"]);
        var savedY = y ?? GetInt(existing?["y"]);
        if (savedX is not null && savedY is not null)
        {
            data["x"] = savedX;
            data["y"] = savedY;
        }

        var rows = new JsonObject();
        foreach (var k in OverlayConfig.RowKeys)
        {
            rows[k] = visibleRows is not null
                ? (visibleRows.TryGetValue(k, out var v) ? v : true)
                : GetBool(existing?["visible_rows"]?[k], true);
        }
        data["visible_rows"] = rows;

        data["bg_color"] = ValidateHexColor(bgColor ?? GetStringOrNull(existing?["bg_color"]), OverlayConfig.DefaultBgColor);
        data["bg_opacity"] = bgOpacity is not null
            ? Math.Clamp(bgOpacity.Value, 0, 100)
            : ValidateOpacityPercent(existing?["bg_opacity"], OverlayConfig.DefaultBgOpacity);
        data["text_color"] = ValidateHexColor(textColor ?? GetStringOrNull(existing?["text_color"]), OverlayConfig.DefaultTextColor);
        data["text_opacity"] = textOpacity is not null
            ? Math.Clamp(textOpacity.Value, 0, 100)
            : ValidateOpacityPercent(existing?["text_opacity"], OverlayConfig.DefaultTextOpacity);

        try
        {
            File.WriteAllText(_path, data.ToJsonString(WriteOptions));
        }
        catch
        {
            // Confort seulement, jamais bloquant.
        }
    }

    private JsonObject? TryParseFile(string path)
    {
        try { return ParseFile(path) as JsonObject; }
        catch { return null; }
    }
}
