using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
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
    private const int PointCount = 16;
    /// <summary>Marge autour de la bulle pour laisser de la place au halo flouté.</summary>
    private const double GlowMarginRatio = 0.55;
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
    private Color _idleColor, _speakingColor, _thinkingColor, _listeningColor;
    /// <summary>Dernier instant où la voix du joueur a été entendue (garde la couleur "écoute" un court moment entre deux mots).</summary>
    private double _lastHeardSeconds = double.NegativeInfinity;

    // --- Vie de la bulle (voir StepLife) ---------------------------------
    // Chaque point du bord est une masse sur un ressort, reliée à ses voisins :
    // la bulle rebondit et une bosse se propage autour d'elle comme une gelée.
    private readonly double[] _offset = new double[PointCount];
    private readonly double[] _velocity = new double[PointCount];
    /// <summary>Écrasé/étiré (+ = étirée en hauteur), ressort rebondissant.</summary>
    private double _squash, _squashVelocity;
    /// <summary>Respiration : phase qui avance, plus un soupir ponctuel amorti.</summary>
    private double _breathPhase, _sigh, _sighVelocity;
    private double _nextSighAt = 10, _nextTwitchAt = 3;
    private double _slowLevel, _lastKickAt;
    private double _lastActivityAt;
    /// <summary>0 = éveillée, 1 = endormie (aucune activité depuis longtemps).</summary>
    private double _sleepiness;
    /// <summary>0..1 : elle écoute le joueur (retient sa respiration, se tient droite).</summary>
    private double _attention;
    private Color _shownColor = Colors.Gray;
    private double _mouthOpen, _mouthVisible;
    /// <summary>Oreilles : sortie (0..1) et frémissement de chaque oreille (ressorts).</summary>
    private double _earsOut, _earTwitchLeft, _earTwitchLeftVel, _earTwitchRight, _earTwitchRightVel, _nextEarFlickAt;
    private bool _wasHearing;
    private bool _asleep;
    private double _speakingAmount;
    private bool _listeningActive;
    /// <summary>Couleur au repos quand l'écoute n'est pas lancée.</summary>
    private static readonly Color InactiveColor = Color.FromRgb(0x8a, 0x92, 0x9e);

    /// <summary>Fin d'un glisser-déposer en mode déplacement : nouvelle position (Left, Top) à enregistrer.</summary>
    public event EventHandler<Point>? PositionChanged;

    public OrbWindow()
    {
        InitializeComponent();
        for (var i = 0; i < _phases.Length; i++) _phases[i] = _random.NextDouble() * Math.PI * 2;
        _color = _idleColor = ParseColor(_config.IdleColor);
        _speakingColor = ParseColor(_config.SpeakingColor);
        _thinkingColor = ParseColor(_config.ThinkingColor);
        _listeningColor = ParseColor(_config.ListeningColor);

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
        _listeningColor = ParseColor(config.ListeningColor);
        var size = Math.Clamp(config.Size, OrbConfig.MinSize, OrbConfig.MaxSize);
        var margin = size * GlowMarginRatio;
        Width = size + 2 * margin;
        Height = size * 1.2 + 2 * margin + CaptionHeight;
        GlowBlur.Radius = Math.Max(10, size * 0.3);
        RimPath.StrokeThickness = Math.Max(6, size * 0.075);
        RimBlur.Radius = Math.Max(4, size * 0.04);
        // Transparence : la bulle et son halo seulement, le texte reste lisible.
        OrbCanvas.Opacity = Math.Clamp(config.Opacity, OrbConfig.MinOpacity, 100) / 100.0;
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

    /// <summary>Écoute lancée ou non : la bulle est grise au repos tant que l'écoute est arrêtée.</summary>
    public void SetListeningActive(bool active) => _listeningActive = active;

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
        var rate = target > _level ? 5.0 : 2.0;
        _level += (target - _level) * (1 - Math.Exp(-dt * rate));
        _thinkingAmount += ((_thinking && !_speaking ? 1.0 : 0.0) - _thinkingAmount) * (1 - Math.Exp(-dt * 3));

        // Priorité des couleurs : parle > réfléchit > t'entend > repos.
        if (_micLevel > 0.05) _lastHeardSeconds = t;
        var hearing = t - _lastHeardSeconds < 0.8;
        StepLife(t, dt, target, hearing);
        // Écoute arrêtée : grise au repos (elle reprend sa couleur si elle parle ou réfléchit).
        var restColor = _listeningActive ? _idleColor : InactiveColor;
        var targetColor = _speaking ? _speakingColor : _thinking ? _thinkingColor : hearing ? _listeningColor : restColor;
        _color = Lerp(_color, targetColor, 1 - Math.Exp(-dt * 5));
        // En parlant : la couleur choisie varie, tantôt plus foncée, tantôt
        // plus claire, au fil des syllabes (comme une lumière qui pulse).
        _speakingAmount += ((_speaking ? 1.0 : 0.0) - _speakingAmount) * (1 - Math.Exp(-dt * 4));
        var shade = _speakingAmount * (0.2 * Math.Sin(t * 1.9 + _phases[1]) + 0.12 * Math.Sin(t * 3.1 + _phases[2]) + 0.35 * (_level - 0.45));
        var shown = shade >= 0 ? Lerp(_color, Colors.White, Math.Min(0.4, shade)) : Lerp(_color, Colors.Black, Math.Min(0.45, -shade));
        _shownColor = shown;
        ApplyColors(shown);

        var blob = BuildBlob(t);
        UpdateSpots(blob, dt, _shownColor);
        UpdateMouth(blob, dt, t, _speaking ? target : 0);
        UpdateEars(blob, dt, t, hearing);
        BodyPath.Data = GlowPath.Data = ShadePath.Data = RimPath.Data = blob;
        // Lueur du bord : uniquement à l'intérieur de la bulle, côté le plus
        // lumineux qui tourne lentement, plus vive quand la voix monte.
        RimPath.Clip = blob;
        var rimAngle = t * 0.25;
        RimBrush.StartPoint = new Point(0.5 + 0.5 * Math.Cos(rimAngle), 0.5 + 0.5 * Math.Sin(rimAngle));
        RimBrush.EndPoint = new Point(0.5 - 0.5 * Math.Cos(rimAngle), 0.5 - 0.5 * Math.Sin(rimAngle));
        RimPath.Opacity = Math.Clamp(0.5 + 0.1 * Math.Sin(t * 0.9) + 0.35 * _level + 0.15 * _thinkingAmount - 0.25 * _sleepiness, 0, 1);
        // Endormie, elle pâlit un peu (en plus de l'opacité choisie dans les réglages).
        GlowPath.Opacity = 0.55 * (1 - 0.45 * _sleepiness) + 0.15 * _level;
        OrbCanvas.Opacity = Math.Clamp(_config.Opacity, OrbConfig.MinOpacity, 100) / 100.0 * (1 - 0.25 * _sleepiness);
        UpdateCaption();
    }

    private Geometry BuildBlob(double t)
    {
        var size = Math.Clamp(_config.Size, OrbConfig.MinSize, OrbConfig.MaxSize);
        var margin = size * GlowMarginRatio;
        var center = new Point(Width / 2, margin + size * 0.6);
        // Presque aussi large que haute, comme une goutte souple.
        var rx = size * 0.46;
        var ry = size * 0.5;

        // Respiration (plus lente endormie, retenue quand elle t'écoute) +
        // soupir ponctuel ; légère hausse quand la voix monte ; elle se tient
        // un peu plus droite quand elle écoute.
        var breathing = 0.025 * (1 - 0.6 * _attention) * Math.Sin(_breathPhase) + _sigh;
        var scale = (1 + breathing + _level * 0.06) * (1 - 0.04 * _sleepiness);
        var stretch = _squash + 0.035 * _attention + _speakingAmount * _level * 0.16 * Math.Sin(t * 0.7 + _phases[0]);
        var scaleY = scale * (1 + stretch);
        var scaleX = scale * (1 - stretch * 0.7);
        var tilt = 4 * Math.Sin(t * 0.13) * Math.PI / 180;
        var cosTilt = Math.Cos(tilt);
        var sinTilt = Math.Sin(tilt);

        var points = new Point[PointCount];
        for (var i = 0; i < PointCount; i++)
        {
            var angle = 2 * Math.PI * i / PointCount;
            var r = 1 + _offset[i];
            // Superellipse (exposant 2,25) : côtés plus plats qu'un cercle, coins bien arrondis.
            var c = Math.Cos(angle);
            var sn = Math.Sin(angle);
            var x = rx * scaleX * r * Math.Sign(c) * Math.Pow(Math.Abs(c), 2 / 2.25);
            var y = ry * scaleY * r * Math.Sign(sn) * Math.Pow(Math.Abs(sn), 2 / 2.25);
            points[i] = new Point(center.X + x * cosTilt - y * sinTilt, center.Y + x * sinTilt + y * cosTilt);
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
                ctx.BezierTo(c1, c2, p2, isStroked: true, isSmoothJoin: true);
            }
        }
        geometry.Freeze();

        var bounds = geometry.Bounds;
        // Reflet brillant en haut à gauche.
        Highlight.Width = bounds.Width * 0.17;
        Highlight.Height = bounds.Height * 0.12;
        Canvas.SetLeft(Highlight, bounds.Left + bounds.Width * 0.27);
        Canvas.SetTop(Highlight, bounds.Top + bounds.Height * 0.22);

        if (_moveMode)
        {
            MoveHint.Width = rx * 2.4;
            MoveHint.Height = ry * 2.4;
            Canvas.SetLeft(MoveHint, center.X - MoveHint.Width / 2);
            Canvas.SetTop(MoveHint, center.Y - MoveHint.Height / 2);
        }
        return geometry;
    }

    /// <summary>
    /// Forme "voulue" de la bulle à l'instant t pour le point d'angle donné :
    /// grandes formes lentes (galet, triangle arrondi, cloche) et ondulations.
    /// Les ressorts de StepLife la suivent avec du retard et du rebond.
    /// </summary>
    private double TargetOffset(double angle, double t)
    {
        var wobble = (0.06 + _level * 0.26 + _thinkingAmount * 0.04) * (1 - 0.5 * _attention) * (1 - 0.4 * _sleepiness);
        var speed = (0.5 - _level * 0.15 + _thinkingAmount * 0.2) * (1 - 0.5 * _sleepiness);
        var shape = (1 + _level * 1.3) * (
            0.11 * Math.Sin(2 * angle + t * 0.2 + _phases[0])
            + 0.075 * Math.Sin(3 * angle - t * 0.15 + _phases[1])
            + 0.03 * Math.Sin(4 * angle + t * 0.1 + _phases[2]));
        var deform = wobble * (
            0.55 * Math.Sin(2 * angle + t * 0.9 * speed + _phases[0])
            + 0.30 * Math.Sin(3 * angle - t * 1.4 * speed + _phases[1])
            + 0.15 * Math.Sin(5 * angle + t * 2.1 * speed + _phases[2]));
        return shape + deform;
    }

    /// <summary>
    /// Fait "vivre" la bulle à chaque image : ressorts du bord (gelée),
    /// pichenettes sur les syllabes, effet écrasé-étiré, frémissements et
    /// soupirs au repos, attention quand le joueur parle, endormissement
    /// après une longue inactivité et petit rebond au réveil.
    /// </summary>
    private void StepLife(double t, double dt, double target, bool hearing)
    {
        var active = _speaking || _thinking || hearing;
        if (active)
        {
            // Réveil : un seul petit sursaut si elle dormait.
            if (_asleep)
            {
                _asleep = false;
                _squashVelocity += 0.9;
                Kick(_random.NextDouble() * Math.PI * 2, 0.18, 1.2);
            }
            _lastActivityAt = t;
        }
        var sleepTarget = t - _lastActivityAt > 90 ? 1.0 : 0.0;
        _sleepiness += (sleepTarget - _sleepiness) * (1 - Math.Exp(-dt * (sleepTarget > _sleepiness ? 0.3 : 4.0)));
        if (_sleepiness > 0.8) _asleep = true;
        _attention += ((hearing && !_speaking ? 1.0 : 0.0) - _attention) * (1 - Math.Exp(-dt * 4));

        // Attaque d'une syllabe (le niveau monte nettement au-dessus de sa
        // moyenne récente) : pichenette à un endroit au hasard + rebond.
        _slowLevel += (target - _slowLevel) * (1 - Math.Exp(-dt * 3));
        if (target - _slowLevel > 0.18 && t - _lastKickAt > 0.14)
        {
            var strength = (_speaking ? 0.2 : 0.06) + (_speaking ? 0.38 : 0.08) * target;
            Kick(_random.NextDouble() * Math.PI * 2, strength, 0.9);
            _squashVelocity += (_random.NextDouble() < 0.5 ? 1 : -1) * (_speaking ? 1.1 : 0.25) * target;
            if (!_speaking) TwitchEars(0.6 + target);
            _lastKickAt = t;
        }
        if (hearing && !_wasHearing) _squashVelocity += 0.35; // elle "tend l'oreille"
        _wasHearing = hearing;

        // Au repos : petits frémissements aléatoires et soupirs de temps en temps.
        if (!_speaking && t > _nextTwitchAt)
        {
            Kick(_random.NextDouble() * Math.PI * 2, 0.035 + 0.03 * _random.NextDouble(), 0.7);
            _nextTwitchAt = t + (2.5 + _random.NextDouble() * 3.5) * (1 + 2 * _sleepiness);
        }
        if (!active && t > _nextSighAt)
        {
            _sighVelocity += 0.12;
            _nextSighAt = t + 8 + _random.NextDouble() * 8;
        }
        _breathPhase += dt * 0.8 * (1 - 0.45 * _sleepiness) * (1 - 0.6 * _attention);

        // Intégration des ressorts par petits pas (stable même si une image tarde).
        var steps = Math.Max(1, (int)Math.Ceiling(dt / (1.0 / 120)));
        var h = dt / steps;
        var targets = new double[PointCount];
        for (var i = 0; i < PointCount; i++) targets[i] = TargetOffset(2 * Math.PI * i / PointCount, t);
        for (var s = 0; s < steps; s++)
        {
            for (var i = 0; i < PointCount; i++)
            {
                var left = _offset[(i - 1 + PointCount) % PointCount];
                var right = _offset[(i + 1) % PointCount];
                var acc = 28 * (targets[i] - _offset[i]) - 5.5 * _velocity[i] + 30 * (left + right - 2 * _offset[i]);
                _velocity[i] += acc * h;
            }
            for (var i = 0; i < PointCount; i++)
                _offset[i] = Math.Clamp(_offset[i] + _velocity[i] * h, -0.4, 0.6);

            // Écrasé-étiré : ressort vif et peu amorti, donc quelques rebonds.
            _squashVelocity += (-60 * _squash - 6 * _squashVelocity) * h;
            _squash = Math.Clamp(_squash + _squashVelocity * h, -0.3, 0.3);
            // Soupir : ressort lent, une longue inspiration qui retombe.
            _sighVelocity += (-4 * _sigh - 2.5 * _sighVelocity) * h;
            _sigh = Math.Clamp(_sigh + _sighVelocity * h, -0.06, 0.08);
        }
    }

    /// <summary>Pousse le bord vers l'extérieur autour de <paramref name="angle"/> (bosse qui se propage ensuite).</summary>
    private void Kick(double angle, double strength, double width)
    {
        for (var i = 0; i < PointCount; i++)
        {
            var a = 2 * Math.PI * i / PointCount;
            var d = Math.Abs(((a - angle + 3 * Math.PI) % (2 * Math.PI)) - Math.PI);
            _velocity[i] += strength * 5 * Math.Exp(-(d / width) * (d / width));
        }
    }

    /// <summary>
    /// (Test) Bouche dans le bas de la bulle : suit l'enveloppe de la voix
    /// sans lissage lent (ouverture syllabe par syllabe), alterne entre une
    /// forme large ("a") et ronde ("o"), apparaît quand elle parle et
    /// disparaît en douceur ensuite. Intérieur sombre plus profond en haut,
    /// langue rosée visible quand elle s'ouvre grand, lèvres douces et
    /// légèrement lumineuses, coins un peu relevés (sourire) quand elle se ferme.
    /// </summary>
    private void UpdateMouth(Geometry blob, double dt, double t, double voice)
    {
        var wanted = _config.ShowMouth && _speaking ? 1.0 : 0.0;
        _mouthVisible += (wanted - _mouthVisible) * (1 - Math.Exp(-dt * 6));
        _mouthOpen += (voice - _mouthOpen) * (1 - Math.Exp(-dt * (voice > _mouthOpen ? 30 : 14)));
        MouthGroup.Opacity = _mouthVisible;
        if (_mouthVisible < 0.01) return;

        var b = blob.Bounds;
        var roundness = 0.5 + 0.5 * Math.Sin(t * 6.3 + 1.7 * Math.Sin(t * 2.3));
        var open = Math.Clamp(_mouthOpen, 0, 1);
        var halfWidth = b.Width * (0.17 - 0.06 * roundness * open);
        var halfHeight = b.Height * (0.014 + 0.13 * open * (0.75 + 0.25 * roundness));
        var cx = b.Left + b.Width / 2;
        var cy = b.Top + b.Height * 0.66;
        // Sourire : coins relevés, surtout bouche presque fermée.
        var smile = b.Height * (0.018 - 0.01 * open);

        var mouth = new StreamGeometry();
        using (var ctx = mouth.Open())
        {
            var left = new Point(cx - halfWidth, cy - smile);
            var right = new Point(cx + halfWidth, cy - smile);
            ctx.BeginFigure(left, isFilled: true, isClosed: true);
            ctx.BezierTo(new Point(cx - halfWidth * 0.55, cy - halfHeight * 0.85 - smile * 0.2),
                         new Point(cx + halfWidth * 0.55, cy - halfHeight * 0.85 - smile * 0.2), right, isStroked: true, isSmoothJoin: true);
            ctx.BezierTo(new Point(cx + halfWidth * 0.75, cy + halfHeight * 1.4),
                         new Point(cx - halfWidth * 0.75, cy + halfHeight * 1.4), left, isStroked: true, isSmoothJoin: true);
        }
        mouth.Freeze();
        MouthPath.Data = mouth;
        LipPath.Data = mouth;
        LipPath.StrokeThickness = Math.Max(1.5, b.Width * 0.014);
        LipBlur.Radius = Math.Max(1.2, b.Width * 0.012);
        LipPath.Opacity = 0.45 + 0.25 * open;

        // Langue : arrondie au fond de la bouche, découpée par la bouche.
        var tongue = new EllipseGeometry(new Point(cx, cy + halfHeight * 0.95), halfWidth * 0.6, Math.Max(1, halfHeight * 0.7));
        tongue.Freeze();
        TonguePath.Data = tongue;
        TonguePath.Clip = mouth;
        TonguePath.Opacity = Math.Clamp((open - 0.2) / 0.4, 0, 1);

        var c = _shownColor;
        MouthStopTop.Color = WithAlpha(Lerp(c, Colors.Black, 0.9), 0xf2);
        MouthStopMid.Color = WithAlpha(Lerp(c, Colors.Black, 0.8), 0xe8);
        MouthStopBottom.Color = WithAlpha(Lerp(c, Colors.Black, 0.62), 0xe0);
        var pink = Color.FromRgb(0xe8, 0x7a, 0x8c);
        TongueStopLight.Color = Lerp(Lerp(c, pink, 0.6), Colors.White, 0.15);
        TongueStopDark.Color = Lerp(Lerp(c, pink, 0.5), Colors.Black, 0.35);
        LipBrush.Color = Lerp(c, Colors.White, 0.45);
    }

    /// <summary>Petit coup sur les oreilles (une syllabe entendue) : chacune frémit un peu différemment.</summary>
    private void TwitchEars(double strength)
    {
        _earTwitchLeftVel += (_random.NextDouble() - 0.3) * 3.0 * strength;
        _earTwitchRightVel += (_random.NextDouble() - 0.3) * 3.0 * strength;
    }

    /// <summary>
    /// (Test) Oreilles : sortent du haut de la bulle quand elle t'entend
    /// (ressort qui dépasse un peu puis se stabilise), se dressent, frémissent
    /// sur tes syllabes et de temps en temps toutes seules, puis rentrent
    /// doucement quand tu te tais ou qu'elle se met à parler.
    /// </summary>
    private void UpdateEars(Geometry blob, double dt, double t, bool hearing)
    {
        var wanted = _config.ShowEars && hearing && !_speaking ? 1.0 : 0.0;
        _earsOut += (wanted - _earsOut) * (1 - Math.Exp(-dt * (wanted > _earsOut ? 7 : 2.5)));
        EarGroup.Opacity = Math.Clamp(_earsOut * 3, 0, 1);

        // Frémissements : ressorts vifs, plus un petit mouvement spontané de temps en temps.
        if (_earsOut > 0.5 && t > _nextEarFlickAt)
        {
            if (_random.NextDouble() < 0.5) _earTwitchLeftVel += 2.2; else _earTwitchRightVel += 2.2;
            _nextEarFlickAt = t + 1.2 + _random.NextDouble() * 2.5;
        }
        var steps = Math.Max(1, (int)Math.Ceiling(dt / (1.0 / 120)));
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            _earTwitchLeftVel += (-90 * _earTwitchLeft - 7 * _earTwitchLeftVel) * h;
            _earTwitchLeft = Math.Clamp(_earTwitchLeft + _earTwitchLeftVel * h, -0.5, 0.5);
            _earTwitchRightVel += (-90 * _earTwitchRight - 7 * _earTwitchRightVel) * h;
            _earTwitchRight = Math.Clamp(_earTwitchRight + _earTwitchRightVel * h, -0.5, 0.5);
        }
        if (_earsOut < 0.01) return;

        var b = blob.Bounds;
        var size = Math.Min(b.Width, b.Height);
        // Sortie avec un léger dépassement (effet ressort) pendant qu'elles poussent.
        var grow = _earsOut < 1 ? _earsOut * (1 + 0.25 * Math.Sin(_earsOut * Math.PI)) : 1;
        var length = size * 0.34 * grow;
        var width = size * 0.27;
        var sway = 0.06 * Math.Sin(t * 1.3);

        DrawEar(EarLeft, EarLeftInner, b, -1, length, width, -0.45 + sway + _earTwitchLeft, size);
        DrawEar(EarRight, EarRightInner, b, 1, length, width, 0.45 + sway - _earTwitchRight, size);

        var c = _shownColor;
        var cream = Color.FromRgb(0xff, 0xf0, 0xb8);
        EarLeftTop.Color = EarRightTop.Color = Lerp(c, cream, 0.3);
        EarLeftBottom.Color = EarRightBottom.Color = c;
        var inner = WithAlpha(Lerp(Lerp(c, Color.FromRgb(0xe8, 0x7a, 0x8c), 0.55), Colors.White, 0.1), 0xc8);
        EarLeftInnerBrush.Color = EarRightInnerBrush.Color = inner;
        EarLeftInnerBlur.Radius = EarRightInnerBlur.Radius = Math.Max(1.5, size * 0.015);
    }

    /// <summary>
    /// Une oreille en forme de goutte arrondie, plantée dans le haut de la
    /// bulle et orientée de <paramref name="angle"/> radians par rapport à la
    /// verticale (négatif = penchée vers la gauche).
    /// </summary>
    private static void DrawEar(System.Windows.Shapes.Path outer, System.Windows.Shapes.Path inner, Rect b, int side, double length, double width, double angle, double size)
    {
        var baseCenter = new Point(b.Left + b.Width / 2 + side * b.Width * 0.28, b.Top + b.Height * 0.2);
        Point Rot(double x, double y) => new(baseCenter.X + x * Math.Cos(angle) - y * Math.Sin(angle), baseCenter.Y + x * Math.Sin(angle) + y * Math.Cos(angle));

        Geometry Ear(double earLength, double scale, double lift)
        {
            // Repère local : base sur l'axe x (enfoncée dans la bulle), bout arrondi vers le haut.
            var w = width * scale / 2;
            var baseY = size * 0.12 - lift;
            var tip = -earLength * (0.55 + 0.45 * scale) * 1.05 - lift * 0.2;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(Rot(-w, baseY), isFilled: true, isClosed: true);
                ctx.BezierTo(Rot(-w * 1.35, tip * 0.52), Rot(-w * 1.05, tip), Rot(0, tip), isStroked: false, isSmoothJoin: true);
                ctx.BezierTo(Rot(w * 1.05, tip), Rot(w * 1.35, tip * 0.52), Rot(w, baseY), isStroked: false, isSmoothJoin: true);
            }
            geometry.Freeze();
            return geometry;
        }
        outer.Data = Ear(length, 1.0, 0);
        // Intérieur rosé remonté vers le bout pour qu'il dépasse bien de la bulle.
        inner.Data = Ear(length * 0.75, 0.55, size * 0.13);
    }

    /// <summary>Une tache de couleur intérieure : trajectoire lente (courbe de Lissajous) et pulsation propres.</summary>
    private sealed class ColorSpot
    {
        public required Ellipse Shape { get; init; }
        public required GradientStop Center { get; init; }
        public required GradientStop Edge { get; init; }
        public required bool Dark { get; init; }
        public double FreqX, FreqY, PhaseX, PhaseY, SizeFactor, PulsePhase;
    }

    private readonly List<ColorSpot> _spots = new();
    private double _spotTime;

    /// <summary>3 taches claires et 3 foncées, positions et rythmes tirés au hasard.</summary>
    private void CreateSpots()
    {
        for (var i = 0; i < 6; i++)
        {
            var center = new GradientStop(Colors.White, 0);
            var edge = new GradientStop(Colors.Transparent, 1);
            var brush = new RadialGradientBrush(new GradientStopCollection { center, edge });
            var ellipse = new Ellipse { Fill = brush, IsHitTestVisible = false };
            SpotLayer.Children.Add(ellipse);
            _spots.Add(new ColorSpot
            {
                Shape = ellipse,
                Center = center,
                Edge = edge,
                Dark = i % 2 == 1,
                FreqX = 0.13 + _random.NextDouble() * 0.17,
                FreqY = 0.11 + _random.NextDouble() * 0.17,
                PhaseX = _random.NextDouble() * Math.PI * 2,
                PhaseY = _random.NextDouble() * Math.PI * 2,
                SizeFactor = 0.45 + _random.NextDouble() * 0.3,
                PulsePhase = _random.NextDouble() * Math.PI * 2,
            });
        }
    }

    /// <summary>
    /// Fait dériver les taches à l'intérieur de la bulle : plus vite quand
    /// elle parle ou réfléchit, plus lentement quand elle dort. Chaque tache
    /// grossit et s'estompe à son propre rythme, d'où un mélange de clairs et
    /// de foncés qui change sans cesse.
    /// </summary>
    private void UpdateSpots(Geometry blob, double dt, Color main)
    {
        if (_spots.Count == 0) CreateSpots();
        var b = blob.Bounds;
        SpotLayer.Width = Width;
        SpotLayer.Height = Height;
        SpotClip.Clip = blob;
        SpotBlur.Radius = Math.Max(6, Math.Min(b.Width, b.Height) * 0.14);
        _spotTime += dt * (1 + 1.4 * _level + 1.6 * _thinkingAmount) * (1 - 0.6 * _sleepiness);

        var light = Lerp(main, Colors.White, 0.72);
        var dark = Lerp(ShiftHueToward(main, 260, 28, saturation: 0.8, value: 0.5), Colors.Black, 0.45);
        foreach (var spot in _spots)
        {
            var diameter = Math.Min(b.Width, b.Height) * spot.SizeFactor * (1 + 0.15 * Math.Sin(_spotTime * 0.7 + spot.PulsePhase));
            spot.Shape.Width = spot.Shape.Height = diameter;
            var cx = b.Left + b.Width / 2 + Math.Sin(_spotTime * spot.FreqX + spot.PhaseX) * b.Width * 0.32;
            var cy = b.Top + b.Height / 2 + Math.Cos(_spotTime * spot.FreqY + spot.PhaseY) * b.Height * 0.32;
            Canvas.SetLeft(spot.Shape, cx - diameter / 2);
            Canvas.SetTop(spot.Shape, cy - diameter / 2);
            var color = spot.Dark ? dark : light;
            var alpha = (byte)Math.Clamp(255 * (0.7 + 0.3 * Math.Sin(_spotTime * 0.5 + spot.PulsePhase)), 0, 255);
            spot.Center.Color = WithAlpha(color, alpha);
            spot.Edge.Color = WithAlpha(color, 0);
        }
    }

    /// <summary>
    /// Toutes les nuances viennent de la couleur courante : reflet crème,
    /// côté chaud en haut à gauche, côté rosé en bas à droite, liseré
    /// rose-rouge et halo plus chaud — comme la goutte de référence en
    /// orange, et un rendu cohérent pour n'importe quelle couleur choisie.
    /// </summary>
    private void ApplyColors(Color main)
    {
        var cream = Color.FromRgb(0xff, 0xf0, 0xb8);
        var cool = ShiftHueToward(main, 340, 30, saturation: 0.55, value: 0.82);
        var rim = ShiftHueToward(main, 350, 22, saturation: 0.72, value: 1.0);

        GlowBrush.Color = Lerp(main, Color.FromRgb(0xff, 0xcf, 0x70), 0.35);
        StopHighlight.Color = Lerp(main, cream, 0.7);
        StopLight.Color = Lerp(main, cream, 0.3);
        StopMain.Color = main;
        StopEdge.Color = Lerp(main, cool, 0.6);
        ShadeWarm.Color = WithAlpha(Lerp(main, Color.FromRgb(0xff, 0xd8, 0x80), 0.55), 0x70);
        ShadeCool.Color = WithAlpha(cool, 0x90);
        RimStopStrong.Color = WithAlpha(rim, 0xff);
        RimStopSoft.Color = WithAlpha(rim, 0x30);
        RimStopEnd.Color = WithAlpha(rim, 0xa0);
    }

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    /// <summary>Fait tourner la teinte de <paramref name="maxDegrees"/> au plus vers <paramref name="targetHue"/> (par le plus court chemin), avec la saturation et la luminosité données.</summary>
    private static Color ShiftHueToward(Color c, double targetHue, double maxDegrees, double saturation, double value)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        double hue = 0;
        if (delta > 0)
        {
            if (max == r) hue = 60 * (((g - b) / delta) % 6);
            else if (max == g) hue = 60 * ((b - r) / delta + 2);
            else hue = 60 * ((r - g) / delta + 4);
        }
        if (hue < 0) hue += 360;
        var diff = ((targetHue - hue + 540) % 360) - 180;
        hue = (hue + Math.Clamp(diff, -maxDegrees, maxDegrees) + 360) % 360;

        var s = Math.Max(saturation, max > 0 ? delta / max : 0);
        var v = value * Math.Max(0.6, max);
        var cc = v * s;
        var x = cc * (1 - Math.Abs((hue / 60) % 2 - 1));
        var m = v - cc;
        var (r1, g1, b1) = hue switch
        {
            < 60 => (cc, x, 0.0),
            < 120 => (x, cc, 0.0),
            < 180 => (0.0, cc, x),
            < 240 => (0.0, x, cc),
            < 300 => (x, 0.0, cc),
            _ => (cc, 0.0, x),
        };
        return Color.FromRgb((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
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
