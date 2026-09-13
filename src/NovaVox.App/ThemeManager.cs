using System.Windows;

namespace NovaVox.App;

/// <summary>
/// Bascule jour/nuit : remplace le dictionnaire de couleurs/brushes
/// fusionné (Theme.Dark.xaml / Theme.Light.xaml, voir App.xaml).
///
/// Tentative précédente : garder un unique jeu de SolidColorBrush
/// partagés (définis une fois dans Theme.xaml, Color="{DynamicResource
/// ...}") et forcer leur mise à jour ici en réécrivant Color
/// directement. Ça plantait à l'usage
/// ("InvalidOperationException: ... est en lecture seule") : WPF gèle
/// (Freeze) ces Brush dès qu'ils sont consommés par un Setter de Style/
/// ControlTemplate scellé (les styles implicites de Theme.xaml, dont
/// pratiquement tous les contrôles dépendent), qu'ils référencent une
/// DynamicResource ou non — l'hypothèse inverse, indiquée dans une
/// version précédente de ce commentaire, était fausse pour ce runtime
/// (.NET WPF, pas .NET Framework).
///
/// Solution : chaque thème définit maintenant ses PROPRES objets Brush
/// (dans Theme.Dark.xaml/Theme.Light.xaml, plus dans Theme.xaml) et tout
/// ce qui en dépend (styles de Theme.xaml, XAML de l'appli) les
/// consomme via DynamicResource plutôt que StaticResource. Remplacer le
/// dictionnaire fusionné ici fait alors pointer chaque DynamicResource
/// vers le NOUVEL objet Brush (jamais besoin de muter un Brush existant,
/// donc jamais de conflit avec un Freeze) — le mécanisme de
/// propagation standard et fiable de WPF pour ce cas d'usage.
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
