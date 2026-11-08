using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaVox.Core.Targeting;

/// <summary>
/// Réglages de la lecture du vaisseau ciblé dans le HUD : activée ou non,
/// zone de l'écran à lire (en pixels réels de l'écran), fréquence.
/// Fichier target_scan.json (inclus dans l'export de configuration).
/// </summary>
public sealed class TargetScanConfig
{
    public const string FileName = "target_scan.json";
    public const int DefaultIntervalMs = 1500;
    public const int MinIntervalMs = 500;
    public const int MaxIntervalMs = 5000;

    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("left")] public int Left { get; set; }
    [JsonPropertyName("top")] public int Top { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("interval_ms")] public int IntervalMs { get; set; } = DefaultIntervalMs;
    /// <summary>Ne lit l'écran que lorsque Star Citizen est la fenêtre active (évite de lire le bureau).</summary>
    [JsonPropertyName("only_when_game_focused")] public bool OnlyWhenGameFocused { get; set; } = true;
    /// <summary>Nova annonce aussi le vaisseau à voix haute quand la cible change.</summary>
    [JsonPropertyName("speak")] public bool Speak { get; set; }

    [JsonIgnore] public bool HasZone => Width >= 8 && Height >= 8;
}

public sealed class TargetScanConfigStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;

    public TargetScanConfigStore(string baseDir)
    {
        _path = Path.Combine(baseDir, TargetScanConfig.FileName);
    }

    public TargetScanConfig Load()
    {
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<TargetScanConfig>(File.ReadAllText(_path), Options) is { } config)
            {
                config.IntervalMs = Math.Clamp(config.IntervalMs, TargetScanConfig.MinIntervalMs, TargetScanConfig.MaxIntervalMs);
                config.Width = Math.Max(0, config.Width);
                config.Height = Math.Max(0, config.Height);
                return config;
            }
        }
        catch
        {
            // Fichier abîmé : réglages par défaut (désactivé).
        }
        return new TargetScanConfig();
    }

    public void Save(TargetScanConfig config)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(config, Options));
    }
}
