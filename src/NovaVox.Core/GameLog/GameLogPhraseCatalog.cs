using System.Text.RegularExpressions;

namespace NovaVox.Core.GameLog;

/// <summary>
/// Gabarits de phrases annoncées par type d'événement Game.log — port de
/// DEFAULT_GAME_LOG_PHRASES / GAME_LOG_PHRASE_META / GAME_LOG_PHRASE_EMOJI
/// et _format_game_log_phrase (app.py).
/// </summary>
public static partial class GameLogPhraseCatalog
{
    public sealed record PhraseMeta(string Label, IReadOnlyList<string> Placeholders);

    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["route_set"] = "Route tracée vers {dest}",
        ["route_set_no_dest"] = "Route tracée",
        ["jump_start"] = "Départ vers {dest}",
        ["jump_start_no_dest"] = "Départ en saut quantique",
        ["zone_change"] = "Arrivée à destination : {zone}",
        ["zone_change_no_zone"] = "Arrivée à destination",
        ["hud_notification"] = "{text}",
    };

    public static readonly IReadOnlyDictionary<string, string> Emoji = new Dictionary<string, string>
    {
        ["route_set"] = "🧭",
        ["route_set_no_dest"] = "🧭",
        ["jump_start"] = "🚀",
        ["jump_start_no_dest"] = "🚀",
        ["zone_change"] = "🧭",
        ["zone_change_no_zone"] = "🧭",
        ["hud_notification"] = "📢",
    };

    public static readonly IReadOnlyDictionary<string, PhraseMeta> Meta = new Dictionary<string, PhraseMeta>
    {
        ["route_set"] = new("Route tracée (destination connue)", new[] { "dest" }),
        ["route_set_no_dest"] = new("Route tracée (destination inconnue)", Array.Empty<string>()),
        ["jump_start"] = new("Départ en saut quantique (destination connue)", new[] { "dest" }),
        ["jump_start_no_dest"] = new("Départ en saut quantique (destination inconnue)", Array.Empty<string>()),
        ["zone_change"] = new("Arrivée à destination (connue)", new[] { "zone" }),
        ["zone_change_no_zone"] = new("Arrivée à destination (inconnue)", Array.Empty<string>()),
        ["hud_notification"] = new("Notification affichée à l'écran (HUD)", new[] { "text" }),
    };

    public static readonly IReadOnlyList<string> OrderedKeys = new[]
    {
        "route_set", "route_set_no_dest", "jump_start", "jump_start_no_dest",
        "zone_change", "zone_change_no_zone", "hud_notification",
    };

    [GeneratedRegex(@"\{(\w*)\}")]
    private static partial Regex PlaceholderRegex();

    /// <summary>
    /// Construit le texte annoncé pour un événement à partir du gabarit
    /// personnalisé de l'utilisateur (ou du défaut si absent/cassé) — port
    /// de _format_game_log_phrase : un gabarit personnalisé invalide (champ
    /// inconnu) ne doit jamais faire échouer l'annonce, il retombe
    /// silencieusement sur le gabarit par défaut.
    /// </summary>
    public static string Format(string key, IReadOnlyDictionary<string, string> customTemplates, IReadOnlyDictionary<string, string> args)
    {
        var template = customTemplates.TryGetValue(key, out var custom) && !string.IsNullOrWhiteSpace(custom)
            ? custom
            : Defaults.GetValueOrDefault(key, "");

        return TryFormat(template, args) ?? TryFormat(Defaults.GetValueOrDefault(key, ""), args) ?? "";
    }

    private static string? TryFormat(string template, IReadOnlyDictionary<string, string> args)
    {
        var ok = true;
        var result = PlaceholderRegex().Replace(template, m =>
        {
            if (args.TryGetValue(m.Groups[1].Value, out var value)) return value;
            ok = false;
            return "";
        });
        return ok ? result : null;
    }
}
