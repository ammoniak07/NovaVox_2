using System.Windows;

namespace NovaVox.App;

/// <summary>
/// Bascule jour/nuit : remplace le dictionnaire de couleurs fusionné
/// (Theme.Dark.xaml / Theme.Light.xaml, voir App.xaml) — les brushes de
/// Theme.xaml référencent ces couleurs via DynamicResource et se mettent
/// donc à jour en place, sans redémarrer la fenêtre.
/// </summary>
public static class ThemeManager
{
    private static readonly Uri DarkUri = new("Resources/Theme.Dark.xaml", UriKind.Relative);
    private static readonly Uri LightUri = new("Resources/Theme.Light.xaml", UriKind.Relative);

    public static void Apply(string theme)
    {
        var targetUri = theme == "light" ? LightUri : DarkUri;
        var app = Application.Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;
        for (int i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source;
            if (source is null) continue;
            if (IsSameResource(source, DarkUri) || IsSameResource(source, LightUri))
            {
                merged[i] = new ResourceDictionary { Source = targetUri };
                return;
            }
        }
        merged.Insert(0, new ResourceDictionary { Source = targetUri });
    }

    private static bool IsSameResource(Uri a, Uri b) =>
        a.ToString().EndsWith(b.ToString(), StringComparison.OrdinalIgnoreCase);
}
