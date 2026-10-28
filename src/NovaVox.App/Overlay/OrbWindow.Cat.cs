using System.Windows;
using System.Windows.Media;
using Path = System.Windows.Shapes.Path;

namespace NovaVox.App.Overlay;

/// <summary>
/// Option « ROxieWOOki » : la bulle devient une petite tête de chat rose —
/// deux oreilles pointues (qui frémissent quand elle t'entend et bougent
/// toutes seules de temps en temps), des moustaches qui frétillent quand
/// elle parle, un petit nez et une bouche en « ω », et des tons roses à la
/// place des couleurs réglées.
/// </summary>
public partial class OrbWindow
{
    private static readonly Color CatIdleColor = Color.FromRgb(0xff, 0x8f, 0xc8);
    private static readonly Color CatSpeakingColor = Color.FromRgb(0xff, 0x5f, 0xa8);
    private static readonly Color CatThinkingColor = Color.FromRgb(0xe0, 0x8c, 0xff);
    private static readonly Color CatListeningColor = Color.FromRgb(0xff, 0xa8, 0xc4);
    private static readonly Color CatInactiveColor = Color.FromRgb(0xc9, 0xa3, 0xb6);

    private double _catVisible, _catEarLeft, _catEarLeftVel, _catEarRight, _catEarRightVel, _nextCatFlickAt = 3;
    private bool _catWasHearing;

    private bool CatMode => _config.CatMode;

    /// <summary>Couleur cible en mode chat : mêmes états (repos, parole, réflexion, écoute, inactive), en roses.</summary>
    private Color CatTargetColor(bool hearing) =>
        _speaking ? CatSpeakingColor : _thinking ? CatThinkingColor : hearing ? CatListeningColor : _listeningActive ? CatIdleColor : CatInactiveColor;

    private void UpdateCat(Rect b, double dt, double t, bool hearing)
    {
        _catVisible += ((CatMode ? 1.0 : 0.0) - _catVisible) * (1 - Math.Exp(-dt * 6));
        CatBackGroup.Opacity = CatFrontGroup.Opacity = _catVisible;

        // Oreilles : ressorts vifs ; elles se dressent quand elle t'entend, frémissent toutes seules.
        if (hearing && !_catWasHearing) { _catEarLeftVel -= 2.5; _catEarRightVel += 2.5; }
        _catWasHearing = hearing;
        if (t > _nextCatFlickAt)
        {
            if (_random.NextDouble() < 0.5) _catEarLeftVel -= 3; else _catEarRightVel += 3;
            _nextCatFlickAt = t + 2.5 + _random.NextDouble() * 5;
        }
        var steps = Math.Max(1, (int)Math.Ceiling(dt / (1.0 / 120)));
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            _catEarLeftVel += (-110 * _catEarLeft - 8 * _catEarLeftVel) * h;
            _catEarLeft = Math.Clamp(_catEarLeft + _catEarLeftVel * h, -0.5, 0.5);
            _catEarRightVel += (-110 * _catEarRight - 8 * _catEarRightVel) * h;
            _catEarRight = Math.Clamp(_catEarRight + _catEarRightVel * h, -0.5, 0.5);
        }
        if (_catVisible < 0.01) return;

        var size = Math.Min(b.Width, b.Height);
        var cx = b.Left + b.Width / 2;
        // Quand elle t'écoute, les oreilles se dressent un peu plus.
        var perk = 1 + 0.12 * _attention;
        DrawCatEar(CatEarLeft, CatEarLeftInner, b, -1, perk, _catEarLeft);
        DrawCatEar(CatEarRight, CatEarRightInner, b, 1, perk, _catEarRight);

        // Moustaches : trois de chaque côté, qui frétillent quand elle parle.
        var whiskers = new StreamGeometry();
        using (var ctx = whiskers.Open())
        {
            var baseY = b.Top + b.Height * 0.62;
            for (var side = -1; side <= 1; side += 2)
            {
                for (var k = -1; k <= 1; k++)
                {
                    var wiggle = Math.Sin(t * (3 + k) + side) * size * 0.01 + _level * size * 0.05 * Math.Sin(t * 14 + k);
                    var start = new Point(cx + side * b.Width * 0.16, baseY + k * size * 0.035);
                    var end = new Point(cx + side * b.Width * 0.66, baseY + k * size * 0.1 + size * 0.03 + wiggle);
                    var control = new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2 - size * 0.03);
                    ctx.BeginFigure(start, isFilled: false, isClosed: false);
                    ctx.QuadraticBezierTo(control, end, isStroked: true, isSmoothJoin: true);
                }
            }
        }
        whiskers.Freeze();
        CatWhiskers.Data = whiskers;
        CatWhiskers.StrokeThickness = Math.Max(1, size * 0.011);
        CatWhiskersBrush.Color = Color.FromArgb(0xd8, 0xff, 0xf4, 0xfa);

        // Petit nez rose et bouche en « ω » (pas avec la bouche « humaine »).
        var noseY = b.Top + b.Height * 0.58;
        var noseW = size * 0.07;
        var face = new StreamGeometry();
        using (var ctx = face.Open())
        {
            ctx.BeginFigure(new Point(cx - noseW / 2, noseY), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(cx + noseW / 2, noseY), isStroked: true, isSmoothJoin: true);
            ctx.LineTo(new Point(cx, noseY + noseW * 0.7), isStroked: true, isSmoothJoin: true);
        }
        face.Freeze();
        CatNose.Data = face;
        CatNoseBrush.Color = Lerp(_shownColor, Color.FromRgb(0xc0, 0x30, 0x70), 0.6);

        var mouth = new StreamGeometry();
        if (!_config.ShowMouth)
        {
            using var ctx = mouth.Open();
            var top = noseY + noseW * 0.7;
            var open = _level * size * 0.04;
            ctx.BeginFigure(new Point(cx - noseW * 0.9, top + noseW * 0.25), isFilled: false, isClosed: false);
            ctx.QuadraticBezierTo(new Point(cx - noseW * 0.45, top + noseW * 0.75 + open), new Point(cx, top), isStroked: true, isSmoothJoin: true);
            ctx.QuadraticBezierTo(new Point(cx + noseW * 0.45, top + noseW * 0.75 + open), new Point(cx + noseW * 0.9, top + noseW * 0.25), isStroked: true, isSmoothJoin: true);
        }
        mouth.Freeze();
        CatMouth.Data = mouth;
        CatMouth.StrokeThickness = Math.Max(1, size * 0.012);
        CatMouthBrush.Color = Lerp(_shownColor, Color.FromRgb(0x80, 0x20, 0x50), 0.65);
    }

    /// <summary>Une oreille de chat pointue (côté -1 = gauche, +1 = droite), qui pivote autour de sa base.</summary>
    private void DrawCatEar(Path outer, Path inner, Rect b, int side, double perk, double twitch)
    {
        var size = Math.Min(b.Width, b.Height);
        var cx = b.Left + b.Width / 2;
        var baseCenter = new Point(cx + side * b.Width * 0.27, b.Top + b.Height * 0.14);
        var angle = side * (0.18 + twitch);
        Point P(double x, double y)
        {
            // x vers l'extérieur, y vers le haut, en fractions de la taille.
            var px = side * x * size;
            var py = -y * size * perk;
            return new Point(baseCenter.X + px * Math.Cos(angle) - py * Math.Sin(angle), baseCenter.Y + px * Math.Sin(angle) + py * Math.Cos(angle));
        }

        Geometry Ear(double scale, double lift)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(P(-0.17 * scale, lift), isFilled: true, isClosed: true);
                ctx.QuadraticBezierTo(P(-0.06 * scale, lift + 0.24 * scale), P(0.06 * scale, lift + 0.36 * scale), isStroked: true, isSmoothJoin: true);
                ctx.QuadraticBezierTo(P(0.14 * scale, lift + 0.2 * scale), P(0.17 * scale, lift), isStroked: true, isSmoothJoin: true);
            }
            g.Freeze();
            return g;
        }

        outer.Data = Ear(1, 0);
        inner.Data = Ear(0.55, 0.05);
        CatEarBrush.Color = Lerp(_shownColor, Colors.Black, 0.06);
        CatEarInnerBrush.Color = Lerp(_shownColor, Color.FromRgb(0xff, 0xd6, 0xe8), 0.6);
    }
}
