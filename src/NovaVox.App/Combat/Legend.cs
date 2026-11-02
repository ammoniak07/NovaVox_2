using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaVox.App.Combat;

/// <summary>Petite barre de légende des couleurs (pastille + texte), sous les tableaux Pénétration.</summary>
internal static class Legend
{
    public static UIElement Bar(params (Brush Brush, string Text)[] items)
    {
        var bar = new WrapPanel();
        foreach (var (brush, text) in items)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 14, 2) };
            chip.Children.Add(new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = brush,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            chip.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            bar.Children.Add(chip);
        }
        return bar;
    }
}
