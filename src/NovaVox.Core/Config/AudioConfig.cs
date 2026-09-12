using System.Text.Json.Nodes;
using static NovaVox.Core.Json.JsonHelpers;

namespace NovaVox.Core.Config;

public sealed class AudioConfig
{
    public static readonly string[] ListenModes = { "always", "toggle_key", "push_to_talk" };
    public static readonly string[] KbLayouts = { "qwerty", "azerty_fr", "azerty_be" };

    public const double DefaultMicGain = 1.0;
    public const int DefaultMicGate = 0;
    public const int MicGateMaxRaw = 4000;
    public const string DefaultListenMode = "always";
    public const string DefaultKbLayout = "azerty_fr";
    public const bool DefaultAecEnabled = false;
    public const double DefaultTtsVolume = 0.6;

    public string? InputDevice { get; set; }
    public double MicGain { get; set; } = DefaultMicGain;
    public int MicGate { get; set; } = DefaultMicGate;
    public string ListenMode { get; set; } = DefaultListenMode;
    public string? ListenHotkey { get; set; }
    public string KbLayout { get; set; } = DefaultKbLayout;
    public string? ProfileCycleHotkey { get; set; }
    public bool AecEnabled { get; set; } = DefaultAecEnabled;
    public string? OutputDevice { get; set; }
    public double TtsVolume { get; set; } = DefaultTtsVolume;

    /// <summary>Dossier du modèle Vosk sélectionné (voir browse_model côté Python) — pas encore de flux de téléchargement automatique dans ce portage.</summary>
    public string? ModelPath { get; set; }
}

/// <summary>
/// Port de load_audio_config/save_audio_config (app.py). Stocké par nom de
/// périphérique plutôt que par index : l'index système peut changer d'un
/// lancement à l'autre selon les périphériques branchés, alors que le nom
/// reste stable.
/// </summary>
public sealed class AudioConfigStore
{
    private readonly string _path;

    public AudioConfigStore(string baseDir)
    {
        _path = Path.Combine(baseDir, "audio_config.json");
    }

    public AudioConfig Load()
    {
        var config = new AudioConfig();
        if (!File.Exists(_path)) return config;
        try
        {
            var data = ParseFile(_path) as JsonObject;
            if (data is null) return config;

            if (data["input_device"] is { } inputNode) config.InputDevice = GetStringOrNull(inputNode);
            if (data["mic_gain"] is { } gainNode)
            {
                var v = GetDouble(gainNode);
                if (v is not null) config.MicGain = Math.Clamp(v.Value, 0.1, 3.0);
            }
            if (data["mic_gate"] is { } gateNode)
            {
                var v = GetInt(gateNode);
                if (v is not null) config.MicGate = Math.Clamp(v.Value, 0, AudioConfig.MicGateMaxRaw);
            }
            var listenMode = GetStringOrNull(data["listen_mode"]);
            if (listenMode is not null && AudioConfig.ListenModes.Contains(listenMode)) config.ListenMode = listenMode;

            if (data["listen_hotkey"] is { } hotkeyNode)
            {
                var hotkey = (GetString(hotkeyNode)).Trim();
                // Le ToLower ne s'applique qu'aux combinaisons clavier : un
                // bouton de joystick (préfixe "joy:") doit garder la casse
                // exacte du nom/GUID du périphérique.
                if (hotkey.Length > 0 && !hotkey.StartsWith("joy:", StringComparison.Ordinal))
                    hotkey = hotkey.ToLowerInvariant();
                config.ListenHotkey = hotkey.Length == 0 ? null : hotkey;
            }
            var kbLayout = GetStringOrNull(data["kb_layout"]);
            if (kbLayout is not null && AudioConfig.KbLayouts.Contains(kbLayout)) config.KbLayout = kbLayout;

            if (data["profile_cycle_hotkey"] is { } cycleNode)
            {
                var cycle = GetString(cycleNode).Trim().ToLowerInvariant();
                config.ProfileCycleHotkey = cycle.Length == 0 ? null : cycle;
            }
            if (data["aec_enabled"] is { } aecNode) config.AecEnabled = GetBool(aecNode);
            if (data["output_device"] is { } outputNode) config.OutputDevice = GetStringOrNull(outputNode);
            if (data["model_path"] is { } modelPathNode) config.ModelPath = GetStringOrNull(modelPathNode);
            if (data["tts_volume"] is { } volNode)
            {
                var v = GetDouble(volNode);
                if (v is not null) config.TtsVolume = Math.Clamp(v.Value, 0.1, 1.5);
            }
        }
        catch
        {
            return new AudioConfig();
        }
        return config;
    }

    public void Save(AudioConfig config)
    {
        var data = new JsonObject
        {
            ["input_device"] = config.InputDevice,
            ["mic_gain"] = config.MicGain,
            ["mic_gate"] = config.MicGate,
            ["listen_mode"] = config.ListenMode,
            ["listen_hotkey"] = config.ListenHotkey,
            ["kb_layout"] = config.KbLayout,
            ["profile_cycle_hotkey"] = config.ProfileCycleHotkey,
            ["aec_enabled"] = config.AecEnabled,
            ["output_device"] = config.OutputDevice,
            ["tts_volume"] = config.TtsVolume,
            ["model_path"] = config.ModelPath,
        };
        File.WriteAllText(_path, data.ToJsonString(WriteOptions));
    }
}
