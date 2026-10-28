using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NovaVox.App.Speech;
using Path = System.Windows.Shapes.Path;

namespace NovaVox.App.Overlay;

/// <summary>
/// Effets "IA" de la bulle : noyau lumineux (sa présence), onde de voix,
/// ondes d'écoute type sonar, réseau de particules pendant la réflexion,
/// reflet quand une commande est exécutée et glitch holographique en cas
/// d'erreur. Chaque effet a sa case dans les réglages (OrbConfig).
/// </summary>
public partial class OrbWindow
{
    // --- Noyau -----------------------------------------------------------
    private double _coreVisible, _coreEnergy;
    private Point _gaze, _gazeTarget;
    private double _nextGazeAt = 2, _nextBlinkAt = 4, _blinkStart = -10;

    // --- Onde de voix ----------------------------------------------------
    private const int WaveHistoryLength = 48;
    private const int WavePointCount = 64;
    private readonly double[] _waveHistory = new double[WaveHistoryLength];
    private double _waveAccum, _waveVisible;

    // --- Ondes d'écoute --------------------------------------------------
    private sealed class Ring
    {
        public required Path Shape { get; init; }
        public required SolidColorBrush Brush { get; init; }
        public required ScaleTransform Scale { get; init; }
        public double Age = -1, Strength;
    }
    private readonly List<Ring> _rings = new();
    private double _nextRingAt;
    private const double RingDuration = 1.5;

    // --- Réflexion -------------------------------------------------------
    private sealed class Orbiter
    {
        public required Ellipse Shape { get; init; }
        public required GradientStop Center { get; init; }
        public double Radius, Speed, Phase, Rotation, Squash;
        public Point Position;
        public double Depth;
    }
    private readonly List<Orbiter> _nodes = new();
    private readonly List<Orbiter> _satellites = new();
    private double _neuralVisible, _orbitTime;

    // --- Réactions -------------------------------------------------------
    private double _shimmerStart = -10, _glitchStart = -10;
    private const double ShimmerDuration = 0.75, GlitchDuration = 0.6;
    private TranslateTransform? _glitchShift;
    private static readonly Color GlitchColor = Color.FromRgb(0xff, 0x30, 0x55);

    private void OnReaction(object? sender, OrbReaction reaction) => Dispatcher.BeginInvoke(() =>
    {
        var t = _clock.Elapsed.TotalSeconds;
        // Commande reconnue : la confirmation vocale va suivre, elle reste en conversation.
        if (reaction == OrbReaction.Success) _engagedUntil = Math.Max(_engagedUntil, t + EngagedHoldSeconds);
        if (!_config.ShowReactions) return;
        if (reaction == OrbReaction.Success)
        {
            _shimmerStart = t;
            _squashVelocity += 0.8;
            Kick(_random.NextDouble() * Math.PI * 2, 0.12, 1.4);
        }
        else
        {
            _glitchStart = t;
            _squashVelocity -= 0.6;
        }
        _lastActivityAt = t;
    });

    /// <summary>Intensité (0..1) du flash rouge du glitch, à mélanger à la couleur de la bulle.</summary>
    private double GlitchAmount(double t)
    {
        var p = (t - _glitchStart) / GlitchDuration;
        return _config.ShowReactions && p is >= 0.0 and < 1.0 ? 1 - p : 0;
    }

    private void UpdateAiEffects(Geometry blob, double dt, double t, double voice, bool hearing)
    {
        var b = blob.Bounds;
        var size = Math.Min(b.Width, b.Height);
        var center = new Point(b.Left + b.Width / 2, b.Top + b.Height / 2);
        UpdateCore(b, size, dt, t);
        UpdateWave(blob, b, size, dt, t, voice);
        UpdateRings(blob, center, size, dt, t, hearing);
        UpdateNeural(blob, center, size, dt);
        UpdateReactions(blob, size, t);
    }

    /// <summary>
    /// Noyau : un cœur d'énergie au centre. Il respire, s'intensifie avec la
    /// voix, regarde autour de lui par petits sauts (comme un regard), lève
    /// les yeux quand il réfléchit, se fixe quand il t'écoute, cligne de temps
    /// en temps et se ferme à moitié quand la bulle s'endort.
    /// </summary>
    private void UpdateCore(Rect b, double size, double dt, double t)
    {
        _coreVisible += ((_config.ShowCore && !CatMode ? 1.0 : 0.0) - _coreVisible) * (1 - Math.Exp(-dt * 4));
        CoreGroup.Opacity = _coreVisible;
        if (_coreVisible < 0.01) return;

        if (t > _nextGazeAt)
        {
            _gazeTarget = new Point((_random.NextDouble() - 0.5) * 0.16, (_random.NextDouble() - 0.5) * 0.1);
            _nextGazeAt = t + 1.5 + _random.NextDouble() * 3;
        }
        var target = _thinkingAmount > 0.5 ? new Point(0.07, -0.08) : _attention > 0.5 ? new Point(0, -0.02) : _gazeTarget;
        // En déplacement, elle regarde là où elle va.
        var speed = _roamVelocity.Length;
        if (speed > 40 && _thinkingAmount < 0.5)
        {
            var look = Math.Min(1, (speed - 40) / 160);
            target = new Point(target.X + (_roamVelocity.X / speed * 0.11 - target.X) * look, target.Y + (_roamVelocity.Y / speed * 0.08 - target.Y) * look);
        }
        var follow = 1 - Math.Exp(-dt * 9);
        _gaze = new Point(_gaze.X + (target.X - _gaze.X) * follow, _gaze.Y + (target.Y - _gaze.Y) * follow);

        if (t > _nextBlinkAt && !_speaking)
        {
            _blinkStart = t;
            _nextBlinkAt = t + (3 + _random.NextDouble() * 5) * (1 + _sleepiness);
        }
        var sinceBlink = t - _blinkStart;
        var blink = sinceBlink is >= 0.0 and < 0.22 ? Math.Sin(Math.PI * sinceBlink / 0.22) : 0;
        var closed = Math.Max(blink, 0.7 * _sleepiness);

        var energyTarget = 0.5 + 0.05 * Math.Sin(_breathPhase) + (_speaking ? 0.5 * _level : 0.25 * _level) + 0.15 * _thinkingAmount + 0.1 * _attention;
        _coreEnergy += (energyTarget - _coreEnergy) * (1 - Math.Exp(-dt * (energyTarget > _coreEnergy ? 12 : 5)));
        var energy = Math.Clamp(_coreEnergy, 0, 1.2);

        var cx = b.Left + b.Width * (0.5 + _gaze.X);
        var cy = b.Top + b.Height * (0.47 + _gaze.Y);
        var dot = size * (0.07 + 0.035 * energy);
        var halo = size * (0.24 + 0.14 * energy);
        CoreDot.Width = CoreDot.Height = dot * 2;
        Canvas.SetLeft(CoreDot, cx - dot);
        Canvas.SetTop(CoreDot, cy - dot);
        CoreHalo.Width = CoreHalo.Height = halo * 2;
        Canvas.SetLeft(CoreHalo, cx - halo);
        Canvas.SetTop(CoreHalo, cy - halo);
        CoreScale.CenterX = cx;
        CoreScale.CenterY = cy;
        CoreScale.ScaleY = 1 - 0.85 * closed;
        CoreScale.ScaleX = 1 + 0.12 * closed;

        var c = _shownColor;
        var bright = Lerp(c, Colors.White, 0.65);
        CoreHaloCenter.Color = WithAlpha(bright, (byte)Math.Clamp(150 + 90 * energy, 0, 255));
        CoreHaloMid.Color = WithAlpha(Lerp(c, Colors.White, 0.35), (byte)Math.Clamp(60 + 60 * energy, 0, 255));
        CoreHaloEdge.Color = WithAlpha(c, 0);
        CoreDotMid.Color = Lerp(c, Colors.White, 0.85);
        CoreDotEdge.Color = WithAlpha(Lerp(c, Colors.White, 0.6), 0);
    }

    /// <summary>
    /// Onde de voix : deux ondes lumineuses traversent la bulle. Le centre
    /// suit la voix à l'instant présent et les côtés montrent les syllabes
    /// juste passées, comme si le son partait du noyau vers les bords.
    /// </summary>
    private void UpdateWave(Geometry blob, Rect b, double size, double dt, double t, double voice)
    {
        var wanted = _config.ShowVoiceWave && !_config.ShowMouth && !CatMode && _speaking ? 1.0 : 0.0;
        _waveVisible += (wanted - _waveVisible) * (1 - Math.Exp(-dt * 5));
        _waveAccum += dt;
        while (_waveAccum >= 0.025)
        {
            _waveAccum -= 0.025;
            Array.Copy(_waveHistory, 0, _waveHistory, 1, WaveHistoryLength - 1);
            _waveHistory[0] = _speaking ? voice : 0;
        }
        WaveGroup.Opacity = _waveVisible;
        if (_waveVisible < 0.01) return;

        var left = b.Left + b.Width * 0.1;
        var width = b.Width * 0.8;
        var cy = b.Top + b.Height * 0.5;
        var main = new StreamGeometry();
        var second = new StreamGeometry();
        using (var c1 = main.Open())
        using (var c2 = second.Open())
        {
            for (var i = 0; i < WavePointCount; i++)
            {
                var u = -1 + 2.0 * i / (WavePointCount - 1);
                var history = _waveHistory[(int)(Math.Abs(u) * (WaveHistoryLength - 1))];
                var taper = 0.5 * (1 + Math.Cos(Math.PI * u));
                var amp = b.Height * 0.2 * (0.06 + history) * taper;
                var x = left + width * (i / (double)(WavePointCount - 1));
                var y1 = cy + amp * Math.Sin(u * 7.5 - t * 7 + _phases[0]) * (0.7 + 0.3 * Math.Sin(u * 3 + t * 2.3));
                var y2 = cy + amp * 0.7 * Math.Sin(u * 11 + t * 5.5 + _phases[1]);
                if (i == 0)
                {
                    c1.BeginFigure(new Point(x, y1), isFilled: false, isClosed: false);
                    c2.BeginFigure(new Point(x, y2), isFilled: false, isClosed: false);
                }
                else
                {
                    c1.LineTo(new Point(x, y1), isStroked: true, isSmoothJoin: true);
                    c2.LineTo(new Point(x, y2), isStroked: true, isSmoothJoin: true);
                }
            }
        }
        main.Freeze();
        second.Freeze();
        WavePath.Data = WaveGlow.Data = main;
        WavePath2.Data = second;
        WaveGroup.Clip = blob;

        WavePath.StrokeThickness = Math.Max(1.5, size * 0.018);
        WavePath2.StrokeThickness = Math.Max(1, size * 0.011);
        WaveGlow.StrokeThickness = Math.Max(4, size * 0.055);
        WaveGlowBlur.Radius = Math.Max(3, size * 0.035);
        var c = _shownColor;
        WaveBrush.Color = Lerp(c, Colors.White, 0.85);
        WaveBrush2.Color = Lerp(c, Colors.White, 0.6);
        WaveGlowBrush.Color = Lerp(c, Colors.White, 0.4);
    }

    /// <summary>
    /// Ondes d'écoute : quand elle t'entend, des contours de la bulle partent
    /// vers l'extérieur et s'effacent, plus nombreux et plus marqués quand tu
    /// parles fort.
    /// </summary>
    private void UpdateRings(Geometry blob, Point center, double size, double dt, double t, bool hearing)
    {
        if (_rings.Count == 0)
        {
            for (var i = 0; i < 6; i++)
            {
                var brush = new SolidColorBrush(Colors.White);
                var scale = new ScaleTransform();
                var path = new Path { Stroke = brush, Opacity = 0, IsHitTestVisible = false, RenderTransform = scale };
                RingLayer.Children.Add(path);
                _rings.Add(new Ring { Shape = path, Brush = brush, Scale = scale });
            }
        }
        RingBlur.Radius = InnerRingBlur.Radius = Math.Max(1, size * 0.012);
        var inside = _config.ListenRingsInside;
        var layer = inside ? InnerRingLayer : RingLayer;
        if (inside) InnerRingLayer.Clip = blob;
        foreach (var ring in _rings)
        {
            if (ReferenceEquals(ring.Shape.Parent, layer)) continue;
            (ring.Shape.Parent as Panel)?.Children.Remove(ring.Shape);
            layer.Children.Add(ring.Shape);
        }

        if (_config.ShowListenRings && hearing && !_speaking && _micLevel > 0.04 && t >= _nextRingAt)
        {
            var ring = _rings.FirstOrDefault(r => r.Age < 0) ?? _rings.OrderByDescending(r => r.Age).First();
            ring.Age = 0;
            ring.Strength = Math.Clamp(0.35 + _micLevel * 1.3, 0, 1);
            _nextRingAt = t + 0.5 - 0.25 * _micLevel;
        }

        var color = Lerp(_shownColor, Colors.White, inside ? 0.6 : 0.35);
        foreach (var ring in _rings)
        {
            if (ring.Age < 0) continue;
            ring.Age += dt;
            var p = ring.Age / RingDuration;
            if (p >= 1)
            {
                ring.Age = -1;
                ring.Shape.Opacity = 0;
                continue;
            }
            var eased = 1 - (1 - p) * (1 - p);
            // Extérieur : du bord vers le dehors. Intérieur : du noyau vers le bord.
            var scale = inside ? 0.12 + 0.88 * eased : 1.02 + 0.48 * eased;
            ring.Shape.Data = blob;
            ring.Scale.CenterX = center.X;
            ring.Scale.CenterY = center.Y;
            ring.Scale.ScaleX = ring.Scale.ScaleY = scale;
            ring.Shape.StrokeThickness = Math.Max(1, size * 0.022 * (1 - 0.6 * p)) / scale;
            var fadeIn = inside ? Math.Min(1, p / 0.12) : 1;
            ring.Shape.Opacity = ring.Strength * fadeIn * Math.Pow(1 - p, inside ? 1.1 : 1.5);
            ring.Brush.Color = color;
        }
    }

    /// <summary>
    /// Réflexion : des particules tournent dans la bulle sur des orbites
    /// inclinées et se relient quand elles se rapprochent (comme un réseau de
    /// neurones), et trois satellites tournent autour d'elle.
    /// </summary>
    private void UpdateNeural(Geometry blob, Point center, double size, double dt)
    {
        var wanted = _config.ShowThinkingFx ? _thinkingAmount : 0;
        _neuralVisible += (wanted - _neuralVisible) * (1 - Math.Exp(-dt * 4));
        NeuralClip.Opacity = _neuralVisible;
        SatelliteLayer.Opacity = _neuralVisible;
        if (_neuralVisible < 0.01) return;

        if (_nodes.Count == 0)
        {
            for (var i = 0; i < 9; i++) _nodes.Add(CreateOrbiter(NeuralDots, 0.1 + 0.26 * _random.NextDouble(), 0.3 + 0.6 * _random.NextDouble()));
            for (var i = 0; i < 3; i++) _satellites.Add(CreateOrbiter(SatelliteLayer, 0.6 + 0.08 * i, 0.22));
            foreach (var s in _satellites) s.Rotation = (_random.NextDouble() - 0.5) * 0.45;
        }
        _orbitTime += dt * (1 + 0.5 * _level);
        NeuralClip.Clip = blob;

        var light = Lerp(_shownColor, Colors.White, 0.85);
        foreach (var node in _nodes) PlaceOrbiter(node, center, size, size * 0.05, light);
        foreach (var sat in _satellites)
        {
            PlaceOrbiter(sat, center, size, size * 0.06, light);
            // Derrière la bulle : plus petit et presque effacé.
            sat.Shape.Opacity = 0.15 + 0.85 * Math.Clamp((sat.Depth + 0.2) / 0.4, 0, 1);
        }

        var lines = new StreamGeometry();
        using (var ctx = lines.Open())
        {
            var reach = size * 0.32;
            for (var i = 0; i < _nodes.Count; i++)
            for (var j = i + 1; j < _nodes.Count; j++)
            {
                var a = _nodes[i].Position;
                var c = _nodes[j].Position;
                if ((a - c).Length > reach) continue;
                ctx.BeginFigure(a, isFilled: false, isClosed: false);
                ctx.LineTo(c, isStroked: true, isSmoothJoin: false);
            }
        }
        lines.Freeze();
        NeuralLines.Data = lines;
        NeuralLines.StrokeThickness = Math.Max(0.8, size * 0.007);
        NeuralBrush.Color = WithAlpha(Lerp(_shownColor, Colors.White, 0.7), 0x70);
    }

    private Orbiter CreateOrbiter(Canvas layer, double radius, double squash)
    {
        var center = new GradientStop(Colors.White, 0);
        var brush = new RadialGradientBrush(new GradientStopCollection { center, new GradientStop(Color.FromArgb(0x60, 0xff, 0xff, 0xff), 0.45), new GradientStop(Colors.Transparent, 1) });
        var ellipse = new Ellipse { Fill = brush, IsHitTestVisible = false };
        layer.Children.Add(ellipse);
        return new Orbiter
        {
            Shape = ellipse,
            Center = center,
            Radius = radius,
            Squash = squash,
            Speed = (0.5 + 0.7 * _random.NextDouble()) * (_random.NextDouble() < 0.5 ? -1 : 1),
            Phase = _random.NextDouble() * Math.PI * 2,
            Rotation = _random.NextDouble() * Math.PI,
        };
    }

    private void PlaceOrbiter(Orbiter o, Point center, double size, double baseDiameter, Color color)
    {
        var a = o.Phase + _orbitTime * o.Speed;
        var x = o.Radius * size * Math.Cos(a);
        var y = o.Radius * size * o.Squash * Math.Sin(a);
        o.Depth = Math.Sin(a);
        o.Position = new Point(center.X + x * Math.Cos(o.Rotation) - y * Math.Sin(o.Rotation), center.Y + x * Math.Sin(o.Rotation) + y * Math.Cos(o.Rotation));
        var diameter = baseDiameter * (0.75 + 0.35 * o.Depth);
        o.Shape.Width = o.Shape.Height = diameter;
        Canvas.SetLeft(o.Shape, o.Position.X - diameter / 2);
        Canvas.SetTop(o.Shape, o.Position.Y - diameter / 2);
        o.Center.Color = color;
    }

    /// <summary>
    /// Réactions : bande de lumière qui balaie la bulle quand une commande est
    /// exécutée ; en cas d'erreur, copies rouge et cyan décalées et la bulle
    /// qui tressaute (glitch holographique), le tout s'apaisant en ~0,6 s.
    /// </summary>
    private void UpdateReactions(Geometry blob, double size, double t)
    {
        var ps = (t - _shimmerStart) / ShimmerDuration;
        if (_config.ShowReactions && ps is >= 0.0 and < 1.0)
        {
            ShimmerPath.Data = blob;
            var o = -0.3 + 1.6 * ps;
            ShimmerStop0.Offset = o - 0.18;
            ShimmerStop1.Offset = o;
            ShimmerStop2.Offset = o + 0.18;
            ShimmerPath.Opacity = 0.9 * Math.Sin(Math.PI * ps);
        }
        else ShimmerPath.Opacity = 0;

        _glitchShift ??= new TranslateTransform();
        if (!ReferenceEquals(OrbCanvas.RenderTransform, _glitchShift)) OrbCanvas.RenderTransform = _glitchShift;
        var glitch = GlitchAmount(t);
        if (glitch > 0)
        {
            GlitchRed.Data = GlitchCyan.Data = blob;
            // Saccadé : un nouveau décalage une image sur deux environ.
            if (_random.NextDouble() < 0.5)
            {
                var dx = (_random.NextDouble() - 0.5) * size * 0.1 * glitch;
                GlitchRed.RenderTransform = new TranslateTransform(dx, (_random.NextDouble() - 0.5) * size * 0.02);
                GlitchCyan.RenderTransform = new TranslateTransform(-dx, 0);
                _glitchShift.X = (_random.NextDouble() - 0.5) * size * 0.04 * glitch;
            }
            GlitchRed.Opacity = GlitchCyan.Opacity = 0.6 * glitch;
        }
        else
        {
            GlitchRed.Opacity = GlitchCyan.Opacity = 0;
            _glitchShift.X = 0;
        }
    }
}
