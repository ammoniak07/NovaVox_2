using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using NovaVox.App.ViewModels;

namespace NovaVox.App.Converters;

/// <summary>
/// Convertit une couleur hex ("#RRGGBB") en SolidColorBrush, pour lier
/// directement ShipCheatSheetPointRowVm.Color (Réglages > 🚀 Vaisseaux) et
/// OverlayWindow.ShipSheetPoint.Color (overlay en jeu) au Background d'une
/// pastille ou au Foreground d'un Run, sans repasser par du code-behind à
/// chaque changement. Une valeur vide/invalide retombe sur la couleur
/// neutre par défaut plutôt que de faire planter l'affichage.
/// </summary>
public sealed class HexColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch (FormatException)
            {
                // Retombe sur la couleur par défaut ci-dessous.
            }
        }
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(ShipCheatSheetPointRowVm.DefaultColor));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("HexColorToBrushConverter est à sens unique (affichage seulement).");
}
