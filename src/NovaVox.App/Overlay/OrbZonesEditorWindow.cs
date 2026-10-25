using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NovaVox.Core.Config;

namespace NovaVox.App.Overlay;

/// <summary>
/// Éditeur plein écran des zones interdites de la bulle baladeuse : un voile
/// sombre par-dessus l'écran (le jeu reste visible derrière s'il est en
/// fenêtré sans bordure). Cliquer-glisser dessine un cadre, clic droit sur
/// un cadre le supprime, Entrée valide, Échap annule.
/// </summary>
public sealed class OrbZonesEditorWindow : Window
{
    private readonly Rect _screen;
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)) };
    private readonly List<Rectangle> _zones = new();
    private Rectangle? _drawing;
    private Point _drawStart;

    private static readonly Brush ZoneFill = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0xff, 0x40, 0x40)));
    private static readonly Brush ZoneStroke = Freeze(new SolidColorBrush(Color.FromRgb(0xff, 0x60, 0x60)));

    /// <summary>Zones validées (fractions de l'écran), null si annulé.</summary>
    public List<OrbZone>? Result { get; private set; }

    public OrbZonesEditorWindow(Rect screen, IEnumerable<OrbZone> zones, Rect? orbPreview)
    {
        _screen = screen;
        Title = "Zones interdites de la bulle";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = screen.Left;
        Top = screen.Top;
        Width = screen.Width;
        Height = screen.Height;
        Cursor = Cursors.Cross;
        Content = _canvas;

        // Repère : taille de la bulle, pour voir quelle place elle prend.
        if (orbPreview is { } orb)
        {
            var hint = new Ellipse
            {
                Width = orb.Width,
                Height = orb.Height,
                Stroke = Brushes.LightSkyBlue,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(hint, orb.Left - screen.Left);
            Canvas.SetTop(hint, orb.Top - screen.Top);
            _canvas.Children.Add(hint);
        }

        var box = new Box(screen.Left, screen.Top, screen.Width, screen.Height);
        foreach (var zone in zones)
        {
            var r = zone.ToScreen(box);
            AddZone(new Rect(r.X - screen.Left, r.Y - screen.Top, r.Width, r.Height));
        }

        _canvas.Children.Add(BuildToolbar());

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) Validate();
        };
        Loaded += (_, _) => Activate();
    }

    private UIElement BuildToolbar()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(new TextBlock
        {
            Text = "Dessine les cadres où la bulle ne doit jamais aller (cliquer-glisser).\nClic droit sur un cadre : le supprimer.  Entrée : valider.  Échap : annuler.",
            Foreground = Brushes.White,
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(MakeButton("✔ Valider", (_, _) => Validate()));
        buttons.Children.Add(MakeButton("🗑 Tout effacer", (_, _) =>
        {
            foreach (var z in _zones) _canvas.Children.Remove(z);
            _zones.Clear();
        }));
        buttons.Children.Add(MakeButton("✖ Annuler", (_, _) => Close()));
        panel.Children.Add(buttons);

        var border = new Border
        {
            Child = panel,
            Background = new SolidColorBrush(Color.FromArgb(0xe0, 0x14, 0x1a, 0x24)),
            BorderBrush = Brushes.SteelBlue,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Cursor = Cursors.Arrow,
        };
        // Les clics sur la barre ne dessinent pas de cadre.
        border.MouseLeftButtonDown += (_, e) => e.Handled = true;
        border.Loaded += (_, _) => Canvas.SetLeft(border, (Width - border.ActualWidth) / 2);
        Canvas.SetTop(border, 24);
        Panel.SetZIndex(border, 10);
        return border;
    }

    private static Button MakeButton(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Margin = new Thickness(4, 0, 4, 0), Padding = new Thickness(12, 4, 12, 4), FontSize = 13 };
        button.Click += click;
        return button;
    }

    private Rectangle AddZone(Rect rect)
    {
        var shape = new Rectangle { Width = rect.Width, Height = rect.Height, Fill = ZoneFill, Stroke = ZoneStroke, StrokeThickness = 2 };
        Canvas.SetLeft(shape, rect.Left);
        Canvas.SetTop(shape, rect.Top);
        shape.MouseRightButtonUp += (_, e) =>
        {
            _canvas.Children.Remove(shape);
            _zones.Remove(shape);
            e.Handled = true;
        };
        _canvas.Children.Add(shape);
        _zones.Add(shape);
        return shape;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _drawStart = e.GetPosition(_canvas);
        _drawing = AddZone(new Rect(_drawStart, new Size(0, 0)));
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_drawing is null) return;
        var p = e.GetPosition(_canvas);
        Canvas.SetLeft(_drawing, Math.Min(p.X, _drawStart.X));
        Canvas.SetTop(_drawing, Math.Min(p.Y, _drawStart.Y));
        _drawing.Width = Math.Abs(p.X - _drawStart.X);
        _drawing.Height = Math.Abs(p.Y - _drawStart.Y);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_drawing is null) return;
        ReleaseMouseCapture();
        // Simple clic sans glisser : pas de cadre minuscule.
        if (_drawing.Width < 12 || _drawing.Height < 12)
        {
            _canvas.Children.Remove(_drawing);
            _zones.Remove(_drawing);
        }
        _drawing = null;
    }

    private void Validate()
    {
        var local = new Box(0, 0, _screen.Width, _screen.Height);
        Result = _zones
            .Select(z => OrbZone.FromScreen(new Box(Canvas.GetLeft(z), Canvas.GetTop(z), z.Width, z.Height), local))
            .OfType<OrbZone>()
            .Select(z => new OrbZone(Math.Round(z.X, 4), Math.Round(z.Y, 4), Math.Round(z.Width, 4), Math.Round(z.Height, 4)))
            .ToList();
        Close();
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
