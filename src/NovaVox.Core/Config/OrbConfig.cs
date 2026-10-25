using System.Text.Json;
using System.Text.Json.Nodes;
using static NovaVox.Core.Json.JsonHelpers;

namespace NovaVox.Core.Config;

/// <summary>
/// Réglages de la bulle animée : deuxième overlay, indépendant de l'overlay
/// d'informations, qui s'anime quand NovaVox parle (voir OrbWindow).
/// </summary>
public sealed class OrbConfig
{
    public const double DefaultSize = 160;
    public const double MinSize = 60;
    public const double MaxSize = 400;
    /// <summary>Couleur au repos (bleu-cyan).</summary>
    public const string DefaultIdleColor = "#38bdf8";
    /// <summary>Couleur quand NovaVox parle (orange-rouge).</summary>
    public const string DefaultSpeakingColor = "#f39252";
    /// <summary>Ancienne couleur par défaut (trop vive) : remplacée par la nouvelle au chargement.</summary>
    private const string OldDefaultSpeakingColor = "#ff5a1f";
    internal static string MigrateSpeakingColor(string color) =>
        string.Equals(color, OldDefaultSpeakingColor, StringComparison.OrdinalIgnoreCase) ? DefaultSpeakingColor : color;
    /// <summary>Couleur pendant que Gemini prépare sa réponse (violet).</summary>
    public const string DefaultThinkingColor = "#a855f7";
    /// <summary>Couleur quand la bulle entend le joueur parler au micro (vert).</summary>
    public const string DefaultListeningColor = "#34d399";

    /// <summary>Taille au repos en % de la taille normale (écoute arrêtée, ou bulle endormie).</summary>
    public const int DefaultRestSize = 60;
    public const int MinRestSize = 30;

    /// <summary>Plage de vitesse de balade en % : chaque trajet tire une vitesse au hasard entre les deux.</summary>
    public const int DefaultRoamSpeedMin = 60;
    public const int DefaultRoamSpeedMax = 150;
    public const int MinRoamSpeed = 25;
    public const int MaxRoamSpeed = 1000;

    public const int DefaultOpacity = 100;
    public const int MinOpacity = 10;

    public bool Enabled { get; set; }
    /// <summary>Opacité de la bulle en % (10 = presque transparente, 100 = pleine).</summary>
    public int Opacity { get; set; } = DefaultOpacity;
    public int? X { get; set; }
    public int? Y { get; set; }
    public double Size { get; set; } = DefaultSize;
    /// <summary>Taille au repos en % (30..100) : la bulle rétrécit quand elle est au repos (ni écoute du joueur, ni parole, ni réflexion).</summary>
    public int RestSize { get; set; } = DefaultRestSize;
    public string IdleColor { get; set; } = DefaultIdleColor;
    public string SpeakingColor { get; set; } = DefaultSpeakingColor;
    public string ThinkingColor { get; set; } = DefaultThinkingColor;
    public string ListeningColor { get; set; } = DefaultListeningColor;
    /// <summary>Affiche "Je réfléchis…" sous la bulle pendant une question à Gemini.</summary>
    public bool ShowCaption { get; set; } = true;
    /// <summary>(Test) Bouche qui s'ouvre et se ferme au rythme de la voix quand NovaVox parle.</summary>
    public bool ShowMouth { get; set; }
    /// <summary>(Test) Oreilles qui sortent quand la bulle entend le joueur parler.</summary>
    public bool ShowEars { get; set; }
    /// <summary>Noyau lumineux au centre (la "présence" de Nova) : pulse, regarde autour d'elle, cligne.</summary>
    public bool ShowCore { get; set; } = true;
    /// <summary>Onde sonore dans la bulle, animée par la voix de Nova.</summary>
    public bool ShowVoiceWave { get; set; } = true;
    /// <summary>Ondes type sonar autour de la bulle quand elle entend le joueur.</summary>
    public bool ShowListenRings { get; set; } = true;
    /// <summary>(Test) Ondes d'écoute à l'intérieur de la bulle (du noyau vers le bord) au lieu de l'extérieur.</summary>
    public bool ListenRingsInside { get; set; } = true;
    /// <summary>Réseau de particules et satellites en orbite pendant qu'elle réfléchit.</summary>
    public bool ShowThinkingFx { get; set; } = true;
    /// <summary>Reflet lumineux quand une commande est exécutée, glitch holographique en cas d'erreur.</summary>
    public bool ShowReactions { get; set; } = true;
    /// <summary>La bulle se balade dans l'écran (hors zones interdites) au lieu de rester à sa place.</summary>
    public bool Roam { get; set; }
    /// <summary>Vitesse de balade minimale en % (25..1000).</summary>
    public int RoamSpeedMin { get; set; } = DefaultRoamSpeedMin;
    /// <summary>Vitesse de balade maximale en % (25..1000, jamais sous le minimum).</summary>
    public int RoamSpeedMax { get; set; } = DefaultRoamSpeedMax;
    /// <summary>La bulle disparaît au repos et réapparaît dès qu'elle t'entend, parle ou réfléchit.</summary>
    public bool HideAtRest { get; set; }
    /// <summary>Cadres de l'écran où la bulle ne va jamais (fractions de l'écran).</summary>
    public List<OrbZone> ForbiddenZones { get; set; } = new();
}

public sealed class OrbConfigStore
{
    public const string FileName = "orb_config.json";
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private readonly string _path;

    public OrbConfigStore(string baseDir)
    {
        _path = Path.Combine(baseDir, FileName);
    }

    public OrbConfig Load()
    {
        var config = new OrbConfig();
        try
        {
            if (!File.Exists(_path)) return config;
            if (JsonNode.Parse(File.ReadAllText(_path)) is not JsonObject data) return config;
            config.Enabled = GetBool(data["enabled"]);
            config.X = GetDouble(data["x"]) is { } x ? (int)Math.Round(x) : null;
            config.Y = GetDouble(data["y"]) is { } y ? (int)Math.Round(y) : null;
            config.Size = Math.Clamp(GetDouble(data["size"]) ?? OrbConfig.DefaultSize, OrbConfig.MinSize, OrbConfig.MaxSize);
            config.IdleColor = OverlayConfigStore.ValidateHexColor(GetStringOrNull(data["idle_color"]), OrbConfig.DefaultIdleColor);
            config.SpeakingColor = OrbConfig.MigrateSpeakingColor(OverlayConfigStore.ValidateHexColor(GetStringOrNull(data["speaking_color"]), OrbConfig.DefaultSpeakingColor));
            config.ThinkingColor = OverlayConfigStore.ValidateHexColor(GetStringOrNull(data["thinking_color"]), OrbConfig.DefaultThinkingColor);
            config.ListeningColor = OverlayConfigStore.ValidateHexColor(GetStringOrNull(data["listening_color"]), OrbConfig.DefaultListeningColor);
            config.ShowCaption = GetBool(data["show_caption"], true);
            config.ShowMouth = GetBool(data["show_mouth"]);
            config.ShowEars = GetBool(data["show_ears"]);
            config.RestSize = Math.Clamp((int)Math.Round(GetDouble(data["rest_size"]) ?? OrbConfig.DefaultRestSize), OrbConfig.MinRestSize, 100);
            config.Roam = GetBool(data["roam"]);
            // Ancien réglage unique "roam_speed" : la vitesse variait déjà de 60 à 150 % autour de lui.
            var legacySpeed = GetDouble(data["roam_speed"]);
            var speedMin = GetDouble(data["roam_speed_min"]) ?? legacySpeed * 0.6 ?? OrbConfig.DefaultRoamSpeedMin;
            var speedMax = GetDouble(data["roam_speed_max"]) ?? legacySpeed * 1.5 ?? OrbConfig.DefaultRoamSpeedMax;
            config.RoamSpeedMin = Math.Clamp((int)Math.Round(speedMin), OrbConfig.MinRoamSpeed, OrbConfig.MaxRoamSpeed);
            config.RoamSpeedMax = Math.Clamp((int)Math.Round(speedMax), config.RoamSpeedMin, OrbConfig.MaxRoamSpeed);
            config.HideAtRest = GetBool(data["hide_at_rest"]);
            if (data["forbidden_zones"] is JsonArray zones)
            {
                foreach (var item in zones.OfType<JsonObject>())
                {
                    var zone = OrbZone.FromScreen(new Box(GetDouble(item["x"]) ?? 0, GetDouble(item["y"]) ?? 0, GetDouble(item["w"]) ?? 0, GetDouble(item["h"]) ?? 0), new Box(0, 0, 1, 1));
                    if (zone is not null) config.ForbiddenZones.Add(new OrbZone(Math.Round(zone.X, 4), Math.Round(zone.Y, 4), Math.Round(zone.Width, 4), Math.Round(zone.Height, 4)));
                }
            }
            config.ShowCore = GetBool(data["show_core"], true);
            config.ShowVoiceWave = GetBool(data["show_voice_wave"], true);
            config.ShowListenRings = GetBool(data["show_listen_rings"], true);
            config.ListenRingsInside = GetBool(data["listen_rings_inside"], true);
            config.ShowThinkingFx = GetBool(data["show_thinking_fx"], true);
            config.ShowReactions = GetBool(data["show_reactions"], true);
            config.Opacity = Math.Clamp(OverlayConfigStore.ValidateOpacityPercent(data["opacity"], OrbConfig.DefaultOpacity), OrbConfig.MinOpacity, 100);
        }
        catch
        {
            return new OrbConfig();
        }
        return config;
    }

    public void Save(OrbConfig config)
    {
        var data = new JsonObject
        {
            ["enabled"] = config.Enabled,
            ["x"] = config.X,
            ["y"] = config.Y,
            ["size"] = config.Size,
            ["idle_color"] = config.IdleColor,
            ["speaking_color"] = config.SpeakingColor,
            ["thinking_color"] = config.ThinkingColor,
            ["listening_color"] = config.ListeningColor,
            ["show_caption"] = config.ShowCaption,
            ["show_mouth"] = config.ShowMouth,
            ["show_ears"] = config.ShowEars,
            ["rest_size"] = config.RestSize,
            ["roam"] = config.Roam,
            ["roam_speed_min"] = config.RoamSpeedMin,
            ["roam_speed_max"] = config.RoamSpeedMax,
            ["hide_at_rest"] = config.HideAtRest,
            ["forbidden_zones"] = new JsonArray(config.ForbiddenZones
                .Select(z => (JsonNode)new JsonObject { ["x"] = Math.Round(z.X, 4), ["y"] = Math.Round(z.Y, 4), ["w"] = Math.Round(z.Width, 4), ["h"] = Math.Round(z.Height, 4) })
                .ToArray()),
            ["show_core"] = config.ShowCore,
            ["show_voice_wave"] = config.ShowVoiceWave,
            ["show_listen_rings"] = config.ShowListenRings,
            ["listen_rings_inside"] = config.ListenRingsInside,
            ["show_thinking_fx"] = config.ShowThinkingFx,
            ["show_reactions"] = config.ShowReactions,
            ["opacity"] = config.Opacity,
        };
        File.WriteAllText(_path, data.ToJsonString(WriteOptions));
    }
}
