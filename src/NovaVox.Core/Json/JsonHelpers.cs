using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NovaVox.Core.Json;

/// <summary>
/// Petits utilitaires de lecture JSON tolérante, dans l'esprit du code
/// Python d'origine : une valeur du mauvais type ou absente retombe
/// silencieusement sur une valeur par défaut plutôt que de lever une
/// exception qui ferait planter le chargement de la configuration.
/// </summary>
public static class JsonHelpers
{
    public static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string StripBom(string text) =>
        text.Length > 0 && text[0] == '﻿' ? text[1..] : text;

    public static JsonNode? ParseFile(string path) => JsonNode.Parse(StripBom(File.ReadAllText(path)));

    public static string GetString(JsonNode? node, string fallback = "") =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : fallback;

    public static string? GetStringOrNull(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static bool GetBool(JsonNode? node, bool fallback = false) =>
        node is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    public static int? GetInt(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (int)d;
        if (v.TryGetValue<string>(out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var si)) return si;
        return null;
    }

    public static double? GetDouble(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var ds)) return ds;
        return null;
    }

    public static int ClampInt(JsonNode? node, int fallback, int min, int max) =>
        Math.Clamp(GetInt(node) ?? fallback, min, max);

    public static double ClampDouble(JsonNode? node, double fallback, double min, double max) =>
        Math.Clamp(GetDouble(node) ?? fallback, min, max);
}
