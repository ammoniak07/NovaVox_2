using System.Windows;
using System.Windows.Media;

namespace NovaVox.App;

/// <summary>
/// Bascule jour/nuit : remplace le dictionnaire de couleurs fusionné
/// (Theme.Dark.xaml / Theme.Light.xaml, voir App.xaml), PUIS force la
/// mise à jour de chaque SolidColorBrush partagé de Theme.xaml en
/// réécrivant directement sa Color. Remplacer seulement le dictionnaire
/// et compter sur le DynamicResource des brushes pour propager le
/// changement s'est révélé peu fiable en pratique (observé en mode
/// clair : en-tête, cartes Commandes/Journal et puces clavier restaient
/// teintés du thème sombre alors que les boutons — liés à PanelAltBrush
/// via le style implicite Button — se mettaient bien à jour). Le mode
/// sombre n'a jamais montré le problème simplement parce que c'est le
/// thème chargé par défaut au démarrage : aucune propagation n'est
/// alors nécessaire. Cette synchronisation explicite élimine toute
/// dépendance à ce mécanisme, sans redémarrer la fenêtre.
/// </summary>
public static class ThemeManager
{
    private static readonly Uri DarkUri = new("Resources/Theme.Dark.xaml", UriKind.Relative);
    private static readonly Uri LightUri = new("Resources/Theme.Light.xaml", UriKind.Relative);

    /// <summary>(clé du Brush dans Theme.xaml, clé de la Color dans Theme.Dark/Light.xaml dont il dépend).</summary>
    private static readonly (string BrushKey, string ColorKey)[] BrushColorKeys =
    {
        ("BgBrush", "BgColor"),
        ("PanelBrush", "PanelColor"),
        ("PanelAltBrush", "PanelAltColor"),
        ("BorderBrush", "BorderColor"),
        ("TextBrush", "TextColor"),
        ("MutedBrush", "MutedColor"),
        ("AccentBrush", "AccentColor"),
        ("AmberBrush", "AmberColor"),
        ("DangerBrush", "DangerColor"),
        ("SuccessBrush", "SuccessColor"),
        ("SelectionBrush", "SelectionColor"),
        ("AccentDimBrush", "AccentColor"),
        ("PanelBrush50", "PanelColor"),
        ("PanelAltBrush50", "PanelAltColor"),
    };

    public static void Apply(string theme)
    {
        var targetUri = theme == "light" ? LightUri : DarkUri;
        var app = Application.Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;
        ResourceDictionary? colors = null;
        for (int i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source;
            if (source is null) continue;
            if (IsSameResource(source, DarkUri) || IsSameResource(source, LightUri))
            {
                colors = new ResourceDictionary { Source = targetUri };
                merged[i] = colors;
                break;
            }
        }
        if (colors is null)
        {
            colors = new ResourceDictionary { Source = targetUri };
            merged.Insert(0, colors);
        }

        foreach (var (brushKey, colorKey) in BrushColorKeys)
        {
            if (app.Resources[brushKey] is SolidColorBrush brush && colors[colorKey] is Color color)
                brush.Color = color;
        }
    }

    private static bool IsSameResource(Uri a, Uri b) =>
        a.ToString().EndsWith(b.ToString(), StringComparison.OrdinalIgnoreCase);
}
