using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NovaVox.App.Targeting;

/// <summary>
/// Voile semi-transparent sur tous les écrans : on dessine à la souris le
/// cadre où le HUD affiche le nom du vaisseau ciblé. Le jeu reste visible
/// derrière (en fenêtré sans bordure). Entrée ou relâcher la souris :
/// valider ; Échap : annuler. Le résultat est en pixels réels de l'écran
/// (ceux que capture l'OCR), quel que soit le zoom Windows.
/// </summary>
public sealed class ScreenRegionPickerWindow : Window
{
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), Cursor = Cursors.Cross };
    private readonly Rectangle _frame = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xFF)),
        StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x2D, 0xD4, 0xFF)),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };
    private Point _start;
    private bool _drawing;

    /// <summary>Zone choisie en pixels réels de l'écran (null si annulé).</summary>
    public System.Drawing.Rectangle? Result { get; private set; }

    public ScreenRegionPickerWindow(System.Drawing.Rectangle? current)
    {
        Title = "Zone du nom du vaisseau ciblé";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Content = _canvas;
        _canvas.Children.Add(_frame);

        var help = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x14, 0x1A, 0x24)),
            BorderBrush = Brushes.SteelBlue,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                Text = "Cible un vaisseau en jeu, puis dessine un cadre (cliquer-glisser) autour du panneau de la cible : son NOM, les lignes FACTION et VITESSE, et l'hologramme.\n" +
                       "Vois large (le nom monte ou descend selon le vaisseau) : NovaVox garde la ligne au-dessus de FACTION.   Relâcher : valider.   Échap : annuler.",
            },
        };
        _canvas.Children.Add(help);
        Loaded += (_, _) =>
        {
            Canvas.SetLeft(help, Math.Max(0, (Width - help.ActualWidth) / 2));
            Canvas.SetTop(help, 30);
            if (current is { Width: > 0, Height: > 0 } zone) ShowExisting(zone);
            Activate();
        };

        MouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(_canvas);
            _drawing = true;
            CaptureMouse();
            UpdateFrame(_start);
            _frame.Visibility = Visibility.Visible;
        };
        MouseMove += (_, e) =>
        {
            if (_drawing) UpdateFrame(e.GetPosition(_canvas));
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_drawing) return;
            _drawing = false;
            ReleaseMouseCapture();
            UpdateFrame(e.GetPosition(_canvas));
            if (_frame.Width >= 8 && _frame.Height >= 8) Validate();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter && _frame.Visibility == Visibility.Visible) Validate();
        };
    }

    private void UpdateFrame(Point p)
    {
        Canvas.SetLeft(_frame, Math.Min(p.X, _start.X));
        Canvas.SetTop(_frame, Math.Min(p.Y, _start.Y));
        _frame.Width = Math.Abs(p.X - _start.X);
        _frame.Height = Math.Abs(p.Y - _start.Y);
    }

    /// <summary>Affiche la zone déjà réglée (pixels réels → coordonnées de la fenêtre).</summary>
    private void ShowExisting(System.Drawing.Rectangle zone)
    {
        var topLeft = _canvas.PointFromScreen(new Point(zone.Left, zone.Top));
        var bottomRight = _canvas.PointFromScreen(new Point(zone.Right, zone.Bottom));
        _start = topLeft;
        UpdateFrame(bottomRight);
        _frame.Visibility = Visibility.Visible;
    }

    private void Validate()
    {
        var left = Canvas.GetLeft(_frame);
        var top = Canvas.GetTop(_frame);
        // PointToScreen donne des pixels réels (tient compte du zoom Windows de chaque écran).
        var a = _canvas.PointToScreen(new Point(left, top));
        var b = _canvas.PointToScreen(new Point(left + _frame.Width, top + _frame.Height));
        var x = (int)Math.Round(Math.Min(a.X, b.X));
        var y = (int)Math.Round(Math.Min(a.Y, b.Y));
        Result = new System.Drawing.Rectangle(x, y, (int)Math.Round(Math.Abs(b.X - a.X)), (int)Math.Round(Math.Abs(b.Y - a.Y)));
        Close();
    }
}
