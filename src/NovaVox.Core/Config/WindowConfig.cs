using System.Text.Json.Nodes;
using static NovaVox.Core.Json.JsonHelpers;

namespace NovaVox.Core.Config;

public sealed record WindowBounds(int Width, int Height, int? X, int? Y);

/// <summary>Port de load_window_config/save_window_config (app.py).</summary>
public sealed class WindowConfigStore
{
    public const int DefaultWidth = 1000;
    public const int DefaultHeight = 930;
    public static readonly (int W, int H) MinSize = (860, 640);
    public static readonly (int W, int H) MaxSize = (6000, 6000);

    private readonly string _path;

    public WindowConfigStore(string baseDir)
    {
        _path = Path.Combine(baseDir, "window_config.json");
    }

    /// <param name="virtualScreenBounds">
    /// Limites du bureau virtuel (x, y, largeur, hauteur), pour ignorer une
    /// position qui laisserait la fenêtre hors de tout écran connecté.
    /// Passer null pour ignorer cette vérification (ex. tests).
    /// </param>
    public WindowBounds Load((int X, int Y, int W, int H)? virtualScreenBounds = null)
    {
        int width = DefaultWidth, height = DefaultHeight;
        int? x = null, y = null;

        if (File.Exists(_path))
        {
            try
            {
                var data = ParseFile(_path) as JsonObject;
                width = GetInt(data?["width"]) ?? width;
                height = GetInt(data?["height"]) ?? height;
                var rawX = GetInt(data?["x"]);
                var rawY = GetInt(data?["y"]);
                if (rawX is not null && rawY is not null) (x, y) = (rawX, rawY);
            }
            catch
            {
                (width, height, x, y) = (DefaultWidth, DefaultHeight, null, null);
            }
        }

        width = Math.Clamp(width, MinSize.W, MaxSize.W);
        height = Math.Clamp(height, MinSize.H, MaxSize.H);

        if (x is not null && y is not null && virtualScreenBounds is { } bounds)
        {
            const int margin = 80;
            if (x + width < bounds.X + margin || x > bounds.X + bounds.W - margin ||
                y + height < bounds.Y + margin || y > bounds.Y + bounds.H - margin)
            {
                (x, y) = (null, null);
            }
        }

        return new WindowBounds(width, height, x, y);
    }

    public void Save(int width, int height, int? x = null, int? y = null)
    {
        width = Math.Clamp(width, MinSize.W, MaxSize.W);
        height = Math.Clamp(height, MinSize.H, MaxSize.H);
        var data = new JsonObject { ["width"] = width, ["height"] = height };
        if (x is not null && y is not null)
        {
            data["x"] = x;
            data["y"] = y;
        }
        try
        {
            File.WriteAllText(_path, data.ToJsonString(WriteOptions));
        }
        catch
        {
            // Confort seulement, jamais bloquant.
        }
    }
}
