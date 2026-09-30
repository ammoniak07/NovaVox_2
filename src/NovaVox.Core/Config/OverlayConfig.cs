using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using static NovaVox.Core.Json.JsonHelpers;

namespace NovaVox.Core.Config;

public sealed class OverlayConfig
{
    /// <summary>
    /// Toutes les clés de ligne connues, dans l'ordre par défaut (celui du
    /// XAML). "armistice"/"juridiction" manquaient ici depuis leur ajout à
    /// l'overlay (OverlayWindow.xaml) — corrigé en même temps que RowOrder,
    /// car sans ça leur case "Afficher"/masquer ne se serait jamais
    /// persistée d'une session à l'autre (Load/Save n'itèrent que sur
    /// RowKeys pour "visible_rows").
    /// </summary>
    public static readonly string[] RowKeys =
        { "time", "listening", "mic", "phrase", "zone", "armistice", "juridiction", "lastCmd", "shipSheet" };

    public const string DefaultBgColor = "#0a0e14";
    public const int DefaultBgOpacity = 72;
    public const string DefaultTextColor = "#dbe4ee";
    public const int DefaultTextOpacity = 100;
    public const double DefaultScale = 1.0;
    public const double MinScale = 0.7;
    public const double MaxScale = 1.6;

    public bool Enabled { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public Dictionary<string, bool> VisibleRows { get; set; } = RowKeys.ToDictionary(k => k, _ => true);
    public string BgColor { get; set; } = DefaultBgColor;
    public int BgOpacity { get; set; } = DefaultBgOpacity;
    public string TextColor { get; set; } = DefaultTextColor;
    public int TextOpacity { get; set; } = DefaultTextOpacity;
    /// <summary>Facteur d'échelle de l'overlay entier (texte, icônes, espacements) — voir OverlayWindow.ApplyScale (ScaleTransform sur le Grid racine). 1.0 = taille d'origine.</summary>
    public double Scale { get; set; } = DefaultScale;
    /// <summary>
    /// Ordre GLOBAL des lignes (toutes colonnes confondues) — l'ordre
    /// RELATIF des lignes d'une même colonne (voir RowColumns) entre elles
    /// donne leur ordre d'affichage de haut en bas dans cette colonne. Voir
    /// OverlayWindow.ApplyLayout (glisser-déposer en mode édition).
    /// </summary>
    public List<string> RowOrder { get; set; } = RowKeys.ToList();

    /// <summary>Colonne (0 à MaxColumns-1, 0 = la plus à gauche) de chaque ligne — voir OverlayWindow.ApplyLayout.</summary>
    public Dictionary<string, int> RowColumns { get; set; } = RowKeys.ToDictionary(k => k, _ => 0);

    /// <summary>
    /// Nombre maximum de colonnes affichables côte à côte — au-delà, une
    /// fenêtre de 230px de large par colonne deviendrait vite plus large que
    /// l'écran pour peu d'utilité (l'overlay reste un aide-mémoire compact,
    /// pas un tableau de bord). Purement une borne technique : rien
    /// n'empêche d'en utiliser moins (colonnes vides = invisibles hors mode
    /// édition, voir OverlayWindow.RefreshColumnEditingStrips).
    /// </summary>
    public const int MaxColumns = 4;

    /// <summary>
    /// Filtre <paramref name="candidate"/> aux seules clés connues (une clé
    /// obsolète/inconnue dans un fichier de config ne doit jamais faire
    /// planter ou disparaître une ligne), retire les doublons en gardant la
    /// première occurrence, puis ajoute à la fin toute clé connue manquante
    /// (ex. une ligne ajoutée par une mise à jour après l'enregistrement du
    /// fichier) — jamais moins de RowKeys.Length éléments en sortie.
    /// </summary>
    public static List<string> NormalizeRowOrder(IEnumerable<string>? candidate)
    {
        var result = new List<string>();
        if (candidate is not null)
        {
            foreach (var key in candidate)
            {
                if (Array.IndexOf(RowKeys, key) >= 0 && !result.Contains(key))
                    result.Add(key);
            }
        }
        foreach (var key in RowKeys)
        {
            if (!result.Contains(key))
                result.Add(key);
        }
        return result;
    }

    /// <summary>
    /// Filtre <paramref name="candidate"/> aux seules clés connues et borne
    /// chaque valeur à [0, MaxColumns-1] (une colonne hors bornes — ex.
    /// fichier corrompu ou MaxColumns réduit dans une future version —
    /// retombe silencieusement en colonne 0 plutôt que de faire planter ou
    /// disparaître la ligne) ; toute clé connue absente est ajoutée en
    /// colonne 0 par défaut.
    /// </summary>
    public static Dictionary<string, int> NormalizeRowColumns(IEnumerable<KeyValuePair<string, int>>? candidate)
    {
        var result = new Dictionary<string, int>();
        if (candidate is not null)
        {
            foreach (var (key, column) in candidate)
            {
                if (Array.IndexOf(RowKeys, key) >= 0)
                    result[key] = Math.Clamp(column, 0, MaxColumns - 1);
            }
        }
        foreach (var key in RowKeys)
        {
            if (!result.ContainsKey(key))
                result[key] = 0;
        }
        return result;
    }
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

    public static double ValidateScale(JsonNode? value, double fallback)
    {
        var d = GetDouble(value);
        return d is null ? fallback : Math.Clamp(d.Value, OverlayConfig.MinScale, OverlayConfig.MaxScale);
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
            config.Scale = ValidateScale(data?["scale"], OverlayConfig.DefaultScale);

            config.RowOrder = OverlayConfig.NormalizeRowOrder(
                data?["row_order"] is JsonArray savedOrder
                    ? savedOrder.Select(GetStringOrNull).Where(k => k is not null).Select(k => k!)
                    : null);

            config.RowColumns = OverlayConfig.NormalizeRowColumns(
                data?["row_columns"] is JsonObject savedColumns
                    ? OverlayConfig.RowKeys
                        .Where(k => savedColumns[k] is not null)
                        .Select(k => new KeyValuePair<string, int>(k, GetInt(savedColumns[k]) ?? 0))
                    : null);
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
        string? bgColor = null, int? bgOpacity = null, string? textColor = null, int? textOpacity = null,
        double? scale = null, List<string>? rowOrder = null, Dictionary<string, int>? rowColumns = null)
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
        data["scale"] = scale is not null
            ? Math.Clamp(scale.Value, OverlayConfig.MinScale, OverlayConfig.MaxScale)
            : ValidateScale(existing?["scale"], OverlayConfig.DefaultScale);

        var normalizedOrder = rowOrder is not null
            ? OverlayConfig.NormalizeRowOrder(rowOrder)
            : OverlayConfig.NormalizeRowOrder(
                existing?["row_order"] is JsonArray existingOrder
                    ? existingOrder.Select(GetStringOrNull).Where(k => k is not null).Select(k => k!)
                    : null);
        var orderArray = new JsonArray();
        foreach (var key in normalizedOrder) orderArray.Add(JsonValue.Create(key));
        data["row_order"] = orderArray;

        var normalizedColumns = rowColumns is not null
            ? OverlayConfig.NormalizeRowColumns(rowColumns)
            : OverlayConfig.NormalizeRowColumns(
                existing?["row_columns"] is JsonObject existingColumns
                    ? OverlayConfig.RowKeys
                        .Where(k => existingColumns[k] is not null)
                        .Select(k => new KeyValuePair<string, int>(k, GetInt(existingColumns[k]) ?? 0))
                    : null);
        var columnsObject = new JsonObject();
        foreach (var (key, column) in normalizedColumns) columnsObject[key] = JsonValue.Create(column);
        data["row_columns"] = columnsObject;

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
