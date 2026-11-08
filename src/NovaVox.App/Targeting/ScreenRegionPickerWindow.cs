using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NovaVox.App.Targeting;

/// <summary>Ce que l'utilisateur a fait dans l'éditeur de zone.</summary>
public enum RegionEditOutcome { Cancelled, Saved, Deleted }

/// <summary>
/// Éditeur de la zone de lecture, en voile semi-transparent sur tous les
/// écrans (le jeu reste visible derrière en fenêtré sans bordure) :
/// dessiner un cadre (cliquer-glisser hors du cadre), le déplacer (glisser
/// dedans ou flèches du clavier), le redimensionner (poignées des coins et
/// des côtés), le supprimer (✕). Entrée / « Valider » enregistre, Échap /
/// « Annuler » ne change rien. Le résultat est en pixels réels de l'écran
/// (ceux que capture l'OCR), quel que soit le zoom Windows.
/// </summary>
public sealed class ScreenRegionPickerWindow : Window
{
    private const double MinSize = 12;
    private const double HandleSize = 10;

    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xFF));

    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), Cursor = Cursors.Cross };
    private readonly Rectangle _frame = new()
    {
        Stroke = Accent,
        StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x2D, 0xD4, 0xFF)),
        Cursor = Cursors.SizeAll,
    };
    private readonly Button _delete = new()
    {
        Content = "✕",
        Width = 26,
        Height = 26,
        FontSize = 14,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.White,
        Background = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
        BorderThickness = new Thickness(0),
        Focusable = false,
        Cursor = Cursors.Hand,
        ToolTip = "Supprimer le cadre",
    };
    private readonly TextBlock _sizeText = new() { Foreground = Brushes.White, FontSize = 12, IsHitTestVisible = false };
    private readonly Dictionary<Rectangle, (int X, int Y)> _handles = new();

    /// <summary>Cadre en coordonnées de la fenêtre ; Rect.Empty quand il n'y en a pas.</summary>
    private Rect _rect = Rect.Empty;

    // Glisser en cours : dessin d'un nouveau cadre, déplacement ou redimensionnement.
    private enum DragMode { None, Draw, Move, Resize }
    private DragMode _mode;
    private Point _dragStart;
    private Rect _rectAtStart;
    private (int X, int Y) _resizeDir;

    public RegionEditOutcome Outcome { get; private set; } = RegionEditOutcome.Cancelled;

    /// <summary>Zone en pixels réels de l'écran, quand Outcome = Saved.</summary>
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
        _canvas.Children.Add(_sizeText);
        foreach (var dir in new[] { (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0) })
        {
            var handle = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = Brushes.White,
                Stroke = Accent,
                StrokeThickness = 1.5,
                Cursor = CursorFor(dir),
            };
            _handles[handle] = dir;
            _canvas.Children.Add(handle);
        }
        _delete.Click += (_, _) => { _rect = Rect.Empty; Layout(); };
        _canvas.Children.Add(_delete);

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
                       "Vois large (le nom monte ou descend selon le vaisseau) : NovaVox garde la ligne au-dessus de FACTION.\n" +
                       "Glisser dans le cadre : déplacer (ou flèches, Maj = plus vite).   Poignées : redimensionner.   ✕ : supprimer.",
            },
        };
        _canvas.Children.Add(help);

        var save = new Button { Content = "✔ Valider (Entrée)", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 10, 0), Focusable = false };
        save.Click += (_, _) => Validate();
        var cancel = new Button { Content = "Annuler (Échap)", Padding = new Thickness(14, 6, 14, 6), Focusable = false };
        cancel.Click += (_, _) => Close();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Arrow };
        toolbar.Children.Add(save);
        toolbar.Children.Add(cancel);
        _canvas.Children.Add(toolbar);

        Loaded += (_, _) =>
        {
            Canvas.SetLeft(help, Math.Max(0, (Width - help.ActualWidth) / 2));
            Canvas.SetTop(help, 30);
            Canvas.SetLeft(toolbar, Math.Max(0, (Width - toolbar.ActualWidth) / 2));
            Canvas.SetTop(toolbar, 30 + help.ActualHeight + 10);
            if (current is { Width: > 0, Height: > 0 } zone)
            {
                var a = _canvas.PointFromScreen(new Point(zone.Left, zone.Top));
                var b = _canvas.PointFromScreen(new Point(zone.Right, zone.Bottom));
                _rect = new Rect(a, b);
            }
            Layout();
            Activate();
        };

        _canvas.MouseLeftButtonDown += OnMouseDown;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseLeftButtonUp += (_, _) =>
        {
            if (_mode == DragMode.None) return;
            _mode = DragMode.None;
            _canvas.ReleaseMouseCapture();
            if (_rect.Width < MinSize || _rect.Height < MinSize) _rect = Rect.Empty; // simple clic : pas de cadre
            Layout();
        };
        PreviewKeyDown += OnKey;
    }

    private static Cursor CursorFor((int X, int Y) dir) => dir switch
    {
        (0, _) => Cursors.SizeNS,
        (_, 0) => Cursors.SizeWE,
        (-1, -1) or (1, 1) => Cursors.SizeNWSE,
        _ => Cursors.SizeNESW,
    };

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(_canvas);
        _rectAtStart = _rect;
        if (e.OriginalSource is Rectangle handle && _handles.TryGetValue(handle, out var dir))
        {
            _mode = DragMode.Resize;
            _resizeDir = dir;
        }
        else if (ReferenceEquals(e.OriginalSource, _frame))
        {
            _mode = DragMode.Move;
        }
        else
        {
            // Hors du cadre : on en dessine un nouveau (remplace l'ancien).
            _mode = DragMode.Draw;
            _rect = new Rect(_dragStart, new Size(0, 0));
        }
        _canvas.CaptureMouse();
        e.Handled = true;
        Layout();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_mode == DragMode.None) return;
        var p = Clamp(e.GetPosition(_canvas));
        switch (_mode)
        {
            case DragMode.Draw:
                _rect = new Rect(_dragStart, p);
                break;
            case DragMode.Move:
                var dx = Math.Clamp(p.X - _dragStart.X, -_rectAtStart.Left, _canvas.ActualWidth - _rectAtStart.Right);
                var dy = Math.Clamp(p.Y - _dragStart.Y, -_rectAtStart.Top, _canvas.ActualHeight - _rectAtStart.Bottom);
                _rect = new Rect(_rectAtStart.X + dx, _rectAtStart.Y + dy, _rectAtStart.Width, _rectAtStart.Height);
                break;
            case DragMode.Resize:
                double left = _rectAtStart.Left, top = _rectAtStart.Top, right = _rectAtStart.Right, bottom = _rectAtStart.Bottom;
                if (_resizeDir.X < 0) left = Math.Min(p.X, right - MinSize);
                if (_resizeDir.X > 0) right = Math.Max(p.X, left + MinSize);
                if (_resizeDir.Y < 0) top = Math.Min(p.Y, bottom - MinSize);
                if (_resizeDir.Y > 0) bottom = Math.Max(p.Y, top + MinSize);
                _rect = new Rect(new Point(left, top), new Point(right, bottom));
                break;
        }
        Layout();
    }

    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, _canvas.ActualWidth), Math.Clamp(p.Y, 0, _canvas.ActualHeight));

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
        if (e.Key == Key.Enter) { Validate(); e.Handled = true; return; }
        if (_rect.IsEmpty) return;
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => (0, 0),
        };
        if (dx == 0 && dy == 0)
        {
            if (e.Key == Key.Delete) { _rect = Rect.Empty; Layout(); e.Handled = true; }
            return;
        }
        dx = (int)Math.Clamp(dx, -_rect.Left, _canvas.ActualWidth - _rect.Right);
        dy = (int)Math.Clamp(dy, -_rect.Top, _canvas.ActualHeight - _rect.Bottom);
        _rect.Offset(dx, dy);
        Layout();
        e.Handled = true;
    }

    /// <summary>Place le cadre, ses poignées, la croix et la taille selon _rect.</summary>
    private void Layout()
    {
        var visible = !_rect.IsEmpty;
        var showTools = visible && _mode != DragMode.Draw;
        _frame.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _sizeText.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _delete.Visibility = showTools ? Visibility.Visible : Visibility.Collapsed;
        foreach (var handle in _handles.Keys) handle.Visibility = showTools ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;

        Canvas.SetLeft(_frame, _rect.X);
        Canvas.SetTop(_frame, _rect.Y);
        _frame.Width = _rect.Width;
        _frame.Height = _rect.Height;

        foreach (var (handle, dir) in _handles)
        {
            Canvas.SetLeft(handle, _rect.X + (dir.X + 1) / 2.0 * _rect.Width - HandleSize / 2);
            Canvas.SetTop(handle, _rect.Y + (dir.Y + 1) / 2.0 * _rect.Height - HandleSize / 2);
        }

        // Croix au-dessus du coin haut-droit (dans l'écran même si le cadre touche le bord).
        Canvas.SetLeft(_delete, Math.Min(_rect.Right + 4, _canvas.ActualWidth - _delete.Width));
        Canvas.SetTop(_delete, Math.Max(_rect.Top - _delete.Height - 4, 0));

        var pixels = ToScreen(_rect);
        _sizeText.Text = $"{pixels.Width} × {pixels.Height} px";
        Canvas.SetLeft(_sizeText, _rect.X);
        Canvas.SetTop(_sizeText, _rect.Bottom + 4 + HandleSize / 2);
    }

    /// <summary>Coordonnées de la fenêtre → pixels réels (tient compte du zoom Windows de chaque écran).</summary>
    private System.Drawing.Rectangle ToScreen(Rect rect)
    {
        var a = _canvas.PointToScreen(rect.TopLeft);
        var b = _canvas.PointToScreen(rect.BottomRight);
        var x = (int)Math.Round(Math.Min(a.X, b.X));
        var y = (int)Math.Round(Math.Min(a.Y, b.Y));
        return new System.Drawing.Rectangle(x, y, (int)Math.Round(Math.Abs(b.X - a.X)), (int)Math.Round(Math.Abs(b.Y - a.Y)));
    }

    private void Validate()
    {
        if (_rect.IsEmpty)
        {
            Outcome = RegionEditOutcome.Deleted;
        }
        else
        {
            Outcome = RegionEditOutcome.Saved;
            Result = ToScreen(_rect);
        }
        Close();
    }
}
