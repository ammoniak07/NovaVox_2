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
    /// Fenêtre hébergeant chaque ligne : 0 = la fenêtre principale, tout
    /// autre entier = une fenêtre détachée ("satellite") créée en glissant
    /// une ligne hors des limites de sa fenêtre actuelle — voir
    /// OverlayWindow.DetachRowToNewWindow/ApplyLayout. Ces identifiants ne
    /// sont pas bornés (contrairement à RowColumns) : une nouvelle fenêtre
    /// prend toujours max(id existants)+1, sans limite de nombre.
    /// </summary>
    public Dictionary<string, int> RowWindow { get; set; } = RowKeys.ToDictionary(k => k, _ => 0);

    /// <summary>
    /// Position écran (X, Y) de chaque fenêtre détachée, indexée par son
    /// identifiant (jamais 0 : la position de la fenêtre principale reste
    /// dans X/Y ci-dessus). Uniquement mémorisée à la fermeture de l'overlay
    /// (OverlayWindow.OnClosing), comme X/Y — une fenêtre détachée qui perd
    /// sa dernière ligne se ferme automatiquement et disparaît d'ici.
    /// </summary>
    public Dictionary<int, (int X, int Y)> SatelliteWindows { get; set; } = new();

    /// <summary>
    /// Nombre maximum de colonnes affichables côte à côte — une par ligne
    /// existante (RowKeys.Length), pour permettre de toutes les mettre côte
    /// à côte si l'utilisateur le souhaite. Chaque colonne se dimensionne à
    /// son propre contenu (voir OverlayWindow.RefreshColumnEditingStrips) :
    /// une colonne vide n'occupe aucune place hors mode édition, donc ce
    /// nombre n'impose aucune largeur inutile même au maximum.
    /// </summary>
    public static readonly int MaxColumns = RowKeys.Length;

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

    /// <summary>
    /// Filtre <paramref name="candidate"/> aux seules clés connues ; un
    /// identifiant de fenêtre négatif (jamais valide, corruption probable)
    /// retombe silencieusement en 0 (fenêtre principale) plutôt que de faire
    /// disparaître la ligne — AUCUNE borne supérieure ici (contrairement à
    /// NormalizeRowColumns), le nombre de fenêtres détachées n'étant pas
    /// limité. Ne vérifie PAS qu'une fenêtre détachée référencée existe
    /// réellement : c'est à OverlayWindow.ApplyLayout d'en (re)créer une au
    /// besoin pour chaque identifiant >0 rencontré ici.
    /// </summary>
    public static Dictionary<string, int> NormalizeRowWindow(IEnumerable<KeyValuePair<string, int>>? candidate)
    {
        var result = new Dictionary<string, int>();
        if (candidate is not null)
        {
            foreach (var (key, windowId) in candidate)
            {
                if (Array.IndexOf(RowKeys, key) >= 0)
                    result[key] = windowId >= 0 ? windowId : 0;
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

            config.RowWindow = OverlayConfig.NormalizeRowWindow(
                data?["row_window"] is JsonObject savedWindow
                    ? OverlayConfig.RowKeys
                        .Where(k => savedWindow[k] is not null)
                        .Select(k => new KeyValuePair<string, int>(k, GetInt(savedWindow[k]) ?? 0))
                    : null);

            config.SatelliteWindows = ParseSatelliteWindows(data?["satellite_windows"] as JsonObject);
        }
        catch
        {
            return new OverlayConfig();
        }
        return config;
    }

    /// <summary>
    /// Les clés JSON sont toujours des chaînes (jamais des entiers), d'où
    /// l'analyse manuelle ici (contrairement à RowColumns/RowWindow, dont
    /// les clés sont les noms de ligne, déjà des chaînes) — une entrée dont
    /// la clé n'est pas un entier positif, ou dont x/y est absent/invalide,
    /// est ignorée plutôt que de faire planter le chargement.
    /// </summary>
    private static Dictionary<int, (int X, int Y)> ParseSatelliteWindows(JsonObject? saved)
    {
        var result = new Dictionary<int, (int X, int Y)>();
        if (saved is null) return result;
        foreach (var (idText, node) in saved)
        {
            if (node is not JsonObject posObject) continue;
            if (!int.TryParse(idText, out var id) || id <= 0) continue;
            var x = GetInt(posObject["x"]);
            var y = GetInt(posObject["y"]);
            if (x is not null && y is not null)
                result[id] = (x.Value, y.Value);
        }
        return result;
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
        double? scale = null, List<string>? rowOrder = null, Dictionary<string, int>? rowColumns = null,
        Dictionary<string, int>? rowWindow = null, Dictionary<int, (int X, int Y)>? satelliteWindows = null)
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

        var normalizedWindow = rowWindow is not null
            ? OverlayConfig.NormalizeRowWindow(rowWindow)
            : OverlayConfig.NormalizeRowWindow(
                existing?["row_window"] is JsonObject existingWindow
                    ? OverlayConfig.RowKeys
                        .Where(k => existingWindow[k] is not null)
                        .Select(k => new KeyValuePair<string, int>(k, GetInt(existingWindow[k]) ?? 0))
                    : null);
        var windowObject = new JsonObject();
        foreach (var (key, windowId) in normalizedWindow) windowObject[key] = JsonValue.Create(windowId);
        data["row_window"] = windowObject;

        var normalizedSatellites = satelliteWindows ?? ParseSatelliteWindows(existing?["satellite_windows"] as JsonObject);
        var satellitesObject = new JsonObject();
        foreach (var (id, pos) in normalizedSatellites)
        {
            if (id <= 0) continue;
            satellitesObject[id.ToString()] = new JsonObject { ["x"] = JsonValue.Create(pos.X), ["y"] = JsonValue.Create(pos.Y) };
        }
        data["satellite_windows"] = satellitesObject;

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
