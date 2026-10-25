using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NovaVox.Core.Config;
using NovaVox.Core.Speech;

namespace NovaVox.App.Overlay;

/// <summary>
/// Petits gags de la bulle, joués rarement et seulement quand elle s'ennuie
/// (jamais pendant une conversation, une réflexion ou un déplacement
/// spécial) : pet discret puis « C'est pas moi », hoquet, éternuement,
/// bâillement, répliques Star Citizen, et ronflement quand elle dort.
/// Le son et la voix sont joués par la fenêtre principale (mêmes réglages
/// que la voix de Nova) via <see cref="GagSpeech"/> et <see cref="GagSound"/>.
/// </summary>
public partial class OrbWindow
{
    /// <summary>Phrase à faire dire par Nova pour un gag.</summary>
    public event EventHandler<string>? GagSpeech;

    /// <summary>Petit son à jouer pour un gag (PCM 16 bits mono, OrbGags.SampleRate).</summary>
    public event EventHandler<short[]>? GagSound;

    private sealed class GagParticle
    {
        public required FrameworkElement Element { get; init; }
        public required Canvas Layer { get; init; }
        public Point Position;
        public Vector Velocity;
        public double Age, Life, StartSize, EndSize, Alpha;
    }

    private readonly List<(double At, Action Act)> _gagSteps = new();
    private readonly List<GagParticle> _gagParticles = new();
    private double _nextGagAt = double.NaN;
    private OrbGag? _lastGag;
    private double _blush, _blushUntil, _gagInflate = 1, _gagInflateTarget = 1, _gagStretch, _gagStretchTarget, _shakeUntil;
    private Rect _gagBounds;
    private readonly TranslateTransform _gagShake = new();

    private static readonly Color BlushColor = Color.FromRgb(0xff, 0x6b, 0x9a);

    private double GagIntervalSeconds => Math.Clamp(_config.GagMinutes, OrbConfig.MinGagMinutes, OrbConfig.MaxGagMinutes) * 60.0;

    /// <summary>Le bon moment pour un gag : elle est là, au calme, et rien ne se passe depuis un moment.</summary>
    private bool CanGag(double t) =>
        _config.Gags && IsVisible && !_engaged && !_speaking && !_thinking && !_moveMode
        && !_wasFullyHidden && !_entering && !_exiting && _wrapPhase == 0
        && t - _lastActivityAt > 15;

    private void UpdateGags(double t, double dt, Rect bounds)
    {
        _gagBounds = bounds;
        if (double.IsNaN(_nextGagAt)) _nextGagAt = t + GagIntervalSeconds * (0.5 + _random.NextDouble());

        // Étapes du gag en cours.
        for (var i = 0; i < _gagSteps.Count; i++)
        {
            if (t < _gagSteps[i].At) continue;
            var act = _gagSteps[i].Act;
            _gagSteps.RemoveAt(i--);
            act();
        }

        if (_gagSteps.Count == 0 && t >= _nextGagAt)
        {
            if (CanGag(t))
            {
                StartGag(t);
                _nextGagAt = t + GagIntervalSeconds * (0.5 + _random.NextDouble());
            }
            else _nextGagAt = t + 30; // pas maintenant : on réessaie un peu plus tard
        }
        // Tu lui parles : le gag s'arrête net.
        if (_attention > 0.5 && _gagSteps.Count > 0)
        {
            _gagSteps.Clear();
            _gagInflateTarget = 1;
            _gagStretchTarget = 0;
        }

        // Rougeur, gonflement (éternuement), étirement (bâillement), tremblement.
        _blush += ((t < _blushUntil ? 1.0 : 0.0) - _blush) * (1 - Math.Exp(-dt * 4));
        _gagInflate += (_gagInflateTarget - _gagInflate) * (1 - Math.Exp(-dt * (_gagInflateTarget > _gagInflate ? 2 : 12)));
        _gagStretch += (_gagStretchTarget - _gagStretch) * (1 - Math.Exp(-dt * 2.5));
        if (!ReferenceEquals(RootGrid.RenderTransform, _gagShake)) RootGrid.RenderTransform = _gagShake;
        if (t < _shakeUntil)
        {
            var size = Math.Min(bounds.Width, bounds.Height);
            _gagShake.X = (_random.NextDouble() - 0.5) * size * 0.06;
            _gagShake.Y = (_random.NextDouble() - 0.5) * size * 0.03;
        }
        else _gagShake.X = _gagShake.Y = 0;

        UpdateGagParticles(dt);
    }

    /// <summary>Bouton « Tester un gag » des réglages : un gag tout de suite (s'il n'y en a pas déjà un en cours).</summary>
    public void PlayGagNow()
    {
        if (_gagSteps.Count > 0) return;
        StartGag(_clock.Elapsed.TotalSeconds);
    }

    private void At(double time, Action act) => _gagSteps.Add((time, act));

    private void Say(string text) => GagSpeech?.Invoke(this, text);

    private void StartGag(double t)
    {
        var gag = OrbGags.PickGag(_random, asleep: _sleepiness > 0.8, _lastGag);
        _lastGag = gag;
        switch (gag)
        {
            case OrbGag.Fart:
                // Prout discret, petit nuage, elle rougit, jette un œil de côté… et nie tout.
                GagSound?.Invoke(this, OrbGags.Fart(_random));
                _squashVelocity -= 0.6;
                Kick(Math.PI / 2, 0.12, 1.0);
                EmitPuffs(7);
                _blushUntil = t + 3;
                _gazeTarget = new Point(_random.NextDouble() < 0.5 ? -0.08 : 0.08, 0.02);
                _nextGazeAt = t + 2.5;
                At(t + 1.3, () => Say(OrbGags.PickLine(_random, OrbGags.FartLines)));
                break;

            case OrbGag.Hiccup:
                for (var i = 0; i < 3; i++)
                {
                    At(t + i * 1.2, () =>
                    {
                        _squashVelocity += 1.6;
                        Kick(-Math.PI / 2, 0.15, 1.2);
                        Say("Hic !");
                    });
                }
                At(t + 4.0, () => Say("Pardon."));
                break;

            case OrbGag.Sneeze:
                Say("Ah… ah…");
                _gagInflateTarget = 1.18;
                At(t + 1.6, () =>
                {
                    Say("Atchoum !");
                    _gagInflateTarget = 1;
                    _shakeUntil = _clock.Elapsed.TotalSeconds + 0.5;
                    _squashVelocity += 1.5;
                    EmitSparkles(12);
                });
                At(t + 3.4, () => Say(OrbGags.PickLine(_random, OrbGags.SneezeLines)));
                break;

            case OrbGag.Yawn:
                _gagStretchTarget = 0.22;
                At(t + 0.9, () => Say(OrbGags.PickLine(_random, OrbGags.YawnLines)));
                At(t + 2.2, () => _gagStretchTarget = 0);
                break;

            case OrbGag.StarCitizenLine:
                _squashVelocity += 0.6;
                Say(OrbGags.PickLine(_random, OrbGags.StarCitizenLines));
                break;

            case OrbGag.Snore:
                // Endormie : ronflements et petits « z » qui s'envolent, sans la réveiller.
                GagSound?.Invoke(this, OrbGags.Snore(_random));
                At(t + 2.6, () => GagSound?.Invoke(this, OrbGags.Snore(_random)));
                for (var i = 0; i < 6; i++) At(t + i * 0.8, () => EmitZ());
                break;
        }
    }

    // --- Particules ------------------------------------------------------

    private void EmitPuffs(int count)
    {
        var b = _gagBounds;
        var size = Math.Min(b.Width, b.Height);
        for (var i = 0; i < count; i++)
        {
            var color = Color.FromArgb(0x90, 0xb8, 0xc8, 0x90); // petit nuage verdâtre
            var ellipse = new Ellipse { Fill = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B)), IsHitTestVisible = false };
            AddParticle(ellipse, GagBackLayer,
                new Point(b.Left + b.Width * (0.35 + 0.3 * _random.NextDouble()), b.Bottom - b.Height * 0.08),
                new Vector((_random.NextDouble() - 0.5) * size * 0.6, size * (0.15 + 0.25 * _random.NextDouble())),
                life: 1.6 + _random.NextDouble(), startSize: size * 0.12, endSize: size * 0.45, alpha: 0.9);
        }
    }

    private void EmitSparkles(int count)
    {
        var b = _gagBounds;
        var size = Math.Min(b.Width, b.Height);
        var light = Lerp(_shownColor, Colors.White, 0.85);
        for (var i = 0; i < count; i++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var ellipse = new Ellipse { Fill = new RadialGradientBrush(light, Color.FromArgb(0, light.R, light.G, light.B)), IsHitTestVisible = false };
            AddParticle(ellipse, GagFrontLayer,
                new Point(b.Left + b.Width / 2 + Math.Cos(angle) * b.Width * 0.3, b.Top + b.Height / 2 + Math.Sin(angle) * b.Height * 0.3),
                new Vector(Math.Cos(angle), Math.Sin(angle)) * size * (0.8 + _random.NextDouble()),
                life: 0.6 + _random.NextDouble() * 0.4, startSize: size * 0.07, endSize: size * 0.02, alpha: 1);
        }
    }

    private void EmitZ()
    {
        var b = _gagBounds;
        var size = Math.Min(b.Width, b.Height);
        var text = new TextBlock
        {
            Text = "z",
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Lerp(_shownColor, Colors.White, 0.7)),
            IsHitTestVisible = false,
        };
        AddParticle(text, GagFrontLayer,
            new Point(b.Right - b.Width * 0.2, b.Top + b.Height * 0.15),
            new Vector(size * 0.15, -size * 0.35),
            life: 2.2, startSize: size * 0.12, endSize: size * 0.3, alpha: 0.9);
    }

    private void AddParticle(FrameworkElement element, Canvas layer, Point position, Vector velocity, double life, double startSize, double endSize, double alpha)
    {
        layer.Children.Add(element);
        _gagParticles.Add(new GagParticle { Element = element, Layer = layer, Position = position, Velocity = velocity, Life = life, StartSize = startSize, EndSize = endSize, Alpha = alpha });
    }

    private void UpdateGagParticles(double dt)
    {
        for (var i = _gagParticles.Count - 1; i >= 0; i--)
        {
            var p = _gagParticles[i];
            p.Age += dt;
            var k = p.Age / p.Life;
            if (k >= 1)
            {
                p.Layer.Children.Remove(p.Element);
                _gagParticles.RemoveAt(i);
                continue;
            }
            p.Velocity *= Math.Exp(-dt * 1.5);
            p.Position += p.Velocity * dt;
            var size = p.StartSize + (p.EndSize - p.StartSize) * k;
            if (p.Element is TextBlock text)
            {
                text.FontSize = Math.Max(4, size);
                Canvas.SetLeft(text, p.Position.X);
                Canvas.SetTop(text, p.Position.Y - size);
            }
            else
            {
                p.Element.Width = p.Element.Height = Math.Max(1, size);
                Canvas.SetLeft(p.Element, p.Position.X - size / 2);
                Canvas.SetTop(p.Element, p.Position.Y - size / 2);
            }
            p.Element.Opacity = p.Alpha * (1 - k);
        }
    }
}
