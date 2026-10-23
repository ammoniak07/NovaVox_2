using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NovaVox.App.Speech;
using NovaVox.Core.Config;

namespace NovaVox.App.Overlay;

/// <summary>
/// Bulle animée : deuxième overlay, séparé de l'overlay d'informations.
/// Une goutte lumineuse qui respire doucement au repos, se colore en
/// orange-rouge et se déforme au rythme de la voix quand NovaVox parle
/// (enveloppe sonore du clip, voir SpeechActivity), passe en violet avec
/// "Je réfléchis…" pendant une question à Gemini, et réagit au micro
/// pendant que le joueur parle. Clic-traversant, sauf en mode déplacement.
/// </summary>
public partial class OrbWindow : Window
{
    private const int PointCount = 12;
    /// <summary>Marge autour de la bulle pour laisser de la place au halo flouté.</summary>
    private const double GlowMarginRatio = 0.35;
    private const double CaptionHeight = 28;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Stopwatch _speechClock = new();
    private readonly Random _random = new();
    private readonly double[] _phases = new double[3];

    private IntPtr _hwnd;
    private OrbConfig _config = new();
    private bool _moveMode;
    private bool _rendering;
    private double _lastFrameSeconds;
    private double _lastTopmostSeconds;

    private float[] _envelope = Array.Empty<float>();
    private int _envelopeFrameMs = 30;
    private bool _speaking;
    private bool _thinking;
    private double _micLevel;
    private double _level;
    private double _thinkingAmount;
    private Color _color;
    private Color _idleColor, _speakingColor, _thinkingColor;

    /// <summary>Fin d'un glisser-déposer en mode déplacement : nouvelle position (Left, Top) à enregistrer.</summary>
    public event EventHandler<Point>? PositionChanged;

    public OrbWindow()
    {
        InitializeComponent();
        for (var i = 0; i < _phases.Length; i++) _phases[i] = _random.NextDouble() * Math.PI * 2;
        _color = _idleColor = ParseColor(_config.IdleColor);
        _speakingColor = ParseColor(_config.SpeakingColor);
        _thinkingColor = ParseColor(_config.ThinkingColor);

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            WindowClickThrough.SetClickThrough(_hwnd, clickThrough: !_moveMode);
        };
        IsVisibleChanged += (_, _) => UpdateRenderingSubscription();
        MouseLeftButtonDown += OnMouseLeftButtonDown;

        SpeechActivity.SpeakingStarted += OnSpeakingStarted;
        SpeechActivity.SpeakingStopped += OnSpeakingStopped;
        SpeechActivity.ThinkingChanged += OnThinkingChanged;
        Closed += (_, _) =>
        {
            SpeechActivity.SpeakingStarted -= OnSpeakingStarted;
            SpeechActivity.SpeakingStopped -= OnSpeakingStopped;
            SpeechActivity.ThinkingChanged -= OnThinkingChanged;
            _rendering = false;
            CompositionTarget.Rendering -= OnRendering;
        };
    }

    /// <summary>Applique taille, couleurs et position (position par défaut : coin bas-droit de l'écran principal).</summary>
    public void ApplyConfig(OrbConfig config)
    {
        _config = config;
        _idleColor = ParseColor(config.IdleColor);
        _speakingColor = ParseColor(config.SpeakingColor);
        _thinkingColor = ParseColor(config.ThinkingColor);
        var size = Math.Clamp(config.Size, OrbConfig.MinSize, OrbConfig.MaxSize);
        var margin = size * GlowMarginRatio;
        Width = size + 2 * margin;
        Height = size * 1.2 + 2 * margin + CaptionHeight;
        GlowBlur.Radius = Math.Max(8, size * 0.22);
        CaptionText.FontSize = Math.Clamp(size * 0.085, 11, 20);

        var work = SystemParameters.WorkArea;
        var left = config.X ?? (int)(work.Right - Width - 30);
        var top = config.Y ?? (int)(work.Bottom - Height - 30);
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var onScreen = left + Width > virtualLeft + 20 && left < virtualLeft + SystemParameters.VirtualScreenWidth - 20
                       && top + Height > virtualTop + 20 && top < virtualTop + SystemParameters.VirtualScreenHeight - 20;
        Left = onScreen ? left : work.Right - Width - 30;
        Top = onScreen ? top : work.Bottom - Height - 30;
        UpdateCaption();
    }

    /// <summary>Niveau du micro (0..1) pendant que le joueur parle : la bulle frémit à sa voix.</summary>
    public void SetMicLevel(double level) => _micLevel = Math.Clamp(level, 0, 1);

    /// <summary>Mode déplacement : la bulle accepte la souris pour être glissée, avec un contour en pointillés.</summary>
    public void SetMoveMode(bool enabled)
    {
        _moveMode = enabled;
        if (_hwnd != IntPtr.Zero) WindowClickThrough.SetClickThrough(_hwnd, clickThrough: !enabled);
        MoveHint.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        Cursor = enabled ? Cursors.SizeAll : null;
        UpdateCaption();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_moveMode) return;
        try { DragMove(); } catch (InvalidOperationException) { return; }
        PositionChanged?.Invoke(this, new Point(Left, Top));
    }

    private void OnSpeakingStarted(object? sender, SpeakingStartedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        _envelope = e.Envelope;
        _envelopeFrameMs = Math.Max(1, e.FrameMs);
        _speaking = true;
        _speechClock.Restart();
    });

    private void OnSpeakingStopped(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        _speaking = false;
        _speechClock.Reset();
    });

    private void OnThinkingChanged(object? sender, bool thinking) => Dispatcher.BeginInvoke(() =>
    {
        _thinking = thinking;
        UpdateCaption();
    });

    private void UpdateCaption()
    {
        if (_moveMode)
        {
            CaptionText.Text = "Glisser pour déplacer";
            CaptionText.Visibility = Visibility.Visible;
            return;
        }
        CaptionText.Text = "Je réfléchis…";
        CaptionText.Visibility = _thinking && !_speaking && _config.ShowCaption ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateRenderingSubscription()
    {
        if (IsVisible && !_rendering)
        {
            _rendering = true;
            _lastFrameSeconds = _clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnRendering;
        }
        else if (!IsVisible && _rendering)
        {
            _rendering = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var t = _clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(t - _lastFrameSeconds, 0, 0.1);
        _lastFrameSeconds = t;

        // Filet de sécurité : rester devant le jeu (voir TopmostKeeper).
        if (t - _lastTopmostSeconds > 1.0)
        {
            _lastTopmostSeconds = t;
            TopmostKeeper.Reassert(_hwnd);
        }

        // Niveau cible : voix de NovaVox (enveloppe du clip) en priorité, sinon le micro.
        double target = 0;
        if (_speaking && _envelope.Length > 0)
        {
            var index = (int)(_speechClock.Elapsed.TotalMilliseconds / _envelopeFrameMs);
            target = index < _envelope.Length ? _envelope[index] : 0;
        }
        else if (!_speaking)
        {
            target = _micLevel * 0.6;
        }
        // Attaque rapide, relâchement plus doux : la bulle "pulse" sur les syllabes.
        var rate = target > _level ? 18.0 : 6.0;
        _level += (target - _level) * (1 - Math.Exp(-dt * rate));
        _thinkingAmount += ((_thinking && !_speaking ? 1.0 : 0.0) - _thinkingAmount) * (1 - Math.Exp(-dt * 3));

        var targetColor = _speaking ? _speakingColor : _thinking ? _thinkingColor : _idleColor;
        _color = Lerp(_color, targetColor, 1 - Math.Exp(-dt * 5));
        ApplyColors(_color);

        BodyPath.Data = GlowPath.Data = BuildBlob(t);
        UpdateCaption();
    }

    private Geometry BuildBlob(double t)
    {
        var size = Math.Clamp(_config.Size, OrbConfig.MinSize, OrbConfig.MaxSize);
        var margin = size * GlowMarginRatio;
        var center = new Point(Width / 2, margin + size * 0.6);
        var rx = size * 0.42;
        var ry = size * 0.55;

        // Respiration au repos, ondulations plus fortes quand la voix monte,
        // tourbillon lent pendant la réflexion.
        var breathing = 0.02 * Math.Sin(t * 1.3);
        var scale = 1 + breathing + _level * 0.08;
        var wobble = 0.03 + _level * 0.11 + _thinkingAmount * 0.05;
        var speed = 1.0 + _level * 2.5 + _thinkingAmount * 1.5;

        var points = new Point[PointCount];
        for (var i = 0; i < PointCount; i++)
        {
            var angle = 2 * Math.PI * i / PointCount;
            var deform = wobble * (
                0.55 * Math.Sin(2 * angle + t * 0.9 * speed + _phases[0])
                + 0.30 * Math.Sin(3 * angle - t * 1.4 * speed + _phases[1])
                + 0.15 * Math.Sin(5 * angle + t * 2.1 * speed + _phases[2]));
            var r = (1 + deform) * scale;
            points[i] = new Point(center.X + rx * r * Math.Cos(angle), center.Y + ry * r * Math.Sin(angle));
        }

        // Courbe fermée lisse passant par tous les points (Catmull-Rom -> Bézier).
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], isFilled: true, isClosed: true);
            for (var i = 0; i < PointCount; i++)
            {
                var p0 = points[(i - 1 + PointCount) % PointCount];
                var p1 = points[i];
                var p2 = points[(i + 1) % PointCount];
                var p3 = points[(i + 2) % PointCount];
                var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                ctx.BezierTo(c1, c2, p2, isStroked: false, isSmoothJoin: true);
            }
        }
        geometry.Freeze();

        if (_moveMode)
        {
            MoveHint.Width = rx * 2.3;
            MoveHint.Height = ry * 2.3;
            Canvas.SetLeft(MoveHint, center.X - MoveHint.Width / 2);
            Canvas.SetTop(MoveHint, center.Y - MoveHint.Height / 2);
        }
        return geometry;
    }

    private void ApplyColors(Color main)
    {
        GlowBrush.Color = main;
        StopHighlight.Color = Lerp(main, Color.FromRgb(0xff, 0xf6, 0xc8), 0.6);
        StopLight.Color = Lerp(main, Colors.White, 0.2);
        StopMain.Color = main;
        StopEdge.Color = Lerp(main, Colors.Black, 0.35);
    }

    private static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex)!; }
        catch { return Colors.DeepSkyBlue; }
    }

    private static Color Lerp(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * amount),
            (byte)(a.G + (b.G - a.G) * amount),
            (byte)(a.B + (b.B - a.B) * amount));
    }
}
