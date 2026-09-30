using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovaVox.App.Controls;

/// <summary>
/// Nuancier de couleur (carré saturation/luminosité + bande de teinte) —
/// voir HsvColorPicker.xaml pour le layout et le contexte (contrôle
/// réutilisé par MainWindow.ColorPickerPopup et OverlayWindow.
/// RowColorPickerPopup, remplaçant les anciens sliders R/V/B). Expose la
/// teinte/saturation/luminosité HSV en interne (pas RVB) : plus naturel
/// pour ce genre de sélecteur (glisser dans le carré ne touche jamais la
/// teinte, glisser dans la bande ne touche jamais saturation/luminosité),
/// et évite l'aller-retour RVB -> HSV -> RVB à chaque pixel de glisser qui
/// ferait dériver légèrement la teinte pour une couleur non représentable
/// exactement en HSV entier.
/// </summary>
public partial class HsvColorPicker : UserControl
{
    /// <summary>Couleur choisie (hex #RRGGBB), à chaque changement venant de l'utilisateur (glisser dans le carré ou la bande) — PAS déclenché par SetColorFromHex (mise à jour programmatique, voir _updatingFromCode).</summary>
    public event EventHandler<string>? ColorChanged;

    private double _hue; // 0-360
    private double _saturation = 1; // 0-1
    private double _value = 1; // 0-1
    private bool _updatingFromCode;
    private bool _draggingSv;
    private bool _draggingHue;

    public HsvColorPicker()
    {
        InitializeComponent();
        SizeChanged += (_, _) => PositionIndicators();
        Loaded += (_, _) => RefreshVisuals();
    }

    /// <summary>Pousse une couleur dans le nuancier sans déclencher ColorChanged (mise à jour programmatique — ouverture du picker, preset cliqué, hex saisi manuellement).</summary>
    public void SetColorFromHex(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        (_hue, _saturation, _value) = RgbToHsv(color.R, color.G, color.B);
        _updatingFromCode = true;
        RefreshVisuals();
        _updatingFromCode = false;
    }

    private void RefreshVisuals()
    {
        SvSquareBorder.Background = new SolidColorBrush(HsvToRgb(_hue, 1, 1));
        PositionIndicators();
    }

    /// <summary>
    /// Repositionne les deux indicateurs (Margin, pas Canvas.Left/Top — voir
    /// HsvColorPicker.xaml) selon _hue/_saturation/_value — sans effet tant
    /// que les Border n'ont pas encore de taille réelle (ActualWidth/Height
    /// à 0, ex. juste après construction, avant le premier Loaded/layout).
    /// </summary>
    private void PositionIndicators()
    {
        var sw = SvSquareBorder.ActualWidth;
        var sh = SvSquareBorder.ActualHeight;
        if (sw > 0 && sh > 0)
        {
            var x = _saturation * sw;
            var y = (1 - _value) * sh;
            SvIndicator.Margin = new Thickness(x - SvIndicator.Width / 2, y - SvIndicator.Height / 2, 0, 0);
        }

        var hh = HueStripBorder.ActualHeight;
        if (hh > 0)
        {
            var y = _hue / 360 * hh;
            HueIndicator.Margin = new Thickness(0, y - HueIndicator.Height / 2, 0, 0);
        }
    }

    private void SvSquare_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggingSv = true;
        SvSquareBorder.CaptureMouse();
        UpdateSvFromPoint(e.GetPosition(SvSquareBorder));
    }

    private void SvSquare_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingSv || e.LeftButton != MouseButtonState.Pressed) return;
        UpdateSvFromPoint(e.GetPosition(SvSquareBorder));
    }

    private void SvSquare_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggingSv = false;
        SvSquareBorder.ReleaseMouseCapture();
    }

    private void UpdateSvFromPoint(Point p)
    {
        var w = SvSquareBorder.ActualWidth;
        var h = SvSquareBorder.ActualHeight;
        if (w <= 0 || h <= 0) return;
        _saturation = Math.Clamp(p.X / w, 0, 1);
        _value = Math.Clamp(1 - p.Y / h, 0, 1);
        PositionIndicators();
        RaiseColorChanged();
    }

    private void HueStrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = true;
        HueStripBorder.CaptureMouse();
        UpdateHueFromPoint(e.GetPosition(HueStripBorder));
    }

    private void HueStrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingHue || e.LeftButton != MouseButtonState.Pressed) return;
        UpdateHueFromPoint(e.GetPosition(HueStripBorder));
    }

    private void HueStrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = false;
        HueStripBorder.ReleaseMouseCapture();
    }

    private void UpdateHueFromPoint(Point p)
    {
        var h = HueStripBorder.ActualHeight;
        if (h <= 0) return;
        _hue = Math.Clamp(p.Y / h, 0, 1) * 360;
        RefreshVisuals();
        RaiseColorChanged();
    }

    private void RaiseColorChanged()
    {
        if (_updatingFromCode) return;
        var color = HsvToRgb(_hue, _saturation, _value);
        ColorChanged?.Invoke(this, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static (double Hue, double Saturation, double Value) RgbToHsv(byte r8, byte g8, byte b8)
    {
        double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue;
        if (delta == 0) hue = 0;
        else if (max == r) hue = 60 * (((g - b) / delta) % 6);
        else if (max == g) hue = 60 * (((b - r) / delta) + 2);
        else hue = 60 * (((r - g) / delta) + 4);
        if (hue < 0) hue += 360;

        var saturation = max == 0 ? 0 : delta / max;
        return (hue, saturation, max);
    }
}
