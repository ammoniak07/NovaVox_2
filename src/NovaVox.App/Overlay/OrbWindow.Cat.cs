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

    private Point _catGaze, _catGazeTarget;
    private double _nextCatGazeAt = 1, _nextCatBlinkAt = 3, _catBlinkStart = -10;
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

        DrawCatEyes(b, size, cx, t, dt);

        // Petit nez rose et bouche en « ω ».
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

        // Elle parle : la bouche s'ouvre syllabe par syllabe (même rythme que la bouche « test »).
        var talk = _speaking ? Math.Clamp(_mouthOpen * 1.3, 0, 1) : 0;
        var top = noseY + noseW * 0.7;
        var mouth = new StreamGeometry();
        using (var ctx = mouth.Open())
        {
            var drop = noseW * 0.75 + talk * size * 0.02;
            ctx.BeginFigure(new Point(cx - noseW * 0.9, top + noseW * 0.25), isFilled: false, isClosed: false);
            ctx.QuadraticBezierTo(new Point(cx - noseW * 0.45, top + drop), new Point(cx, top), isStroked: true, isSmoothJoin: true);
            ctx.QuadraticBezierTo(new Point(cx + noseW * 0.45, top + drop), new Point(cx + noseW * 0.9, top + noseW * 0.25), isStroked: true, isSmoothJoin: true);
        }
        mouth.Freeze();

        // Bouche ouverte sous le « ω » : petit ovale sombre avec une langue rose au fond.
        var openHeight = talk * size * 0.16;
        if (openHeight > 0.5)
        {
            var openTop = top + noseW * 0.35;
            var openWidth = size * (0.1 - 0.02 * talk);
            var openGeometry = new EllipseGeometry(new Point(cx, openTop + openHeight / 2), openWidth / 2, openHeight / 2);
            openGeometry.Freeze();
            CatMouthOpen.Data = openGeometry;
            var tongue = new EllipseGeometry(new Point(cx, openTop + openHeight * 0.8), openWidth * 0.32, openHeight * 0.3);
            tongue.Freeze();
            CatTongue.Data = tongue;
            CatTongue.Clip = openGeometry;
        }
        CatMouthOpen.Opacity = Math.Clamp(talk * 3, 0, 1);
        CatTongue.Opacity = Math.Clamp((talk - 0.3) * 2.5, 0, 1);
        CatMouthOpenBrush.Color = Lerp(_shownColor, Color.FromRgb(0x30, 0x05, 0x18), 0.85);
        CatMouth.Data = mouth;
        CatMouth.StrokeThickness = Math.Max(1, size * 0.012);
        CatMouthBrush.Color = Lerp(_shownColor, Color.FromRgb(0x80, 0x20, 0x50), 0.65);
    }

    /// <summary>
    /// Yeux de chat : pupilles fendues qui regardent autour d'elle (et là où
    /// elle va), plus rondes quand elle t'écoute ; clignements ; paupières
    /// lourdes quand elle s'endort ; yeux un peu plissés (contente) quand elle parle.
    /// </summary>
    private void DrawCatEyes(Rect b, double size, double cx, double t, double dt)
    {
        if (t > _nextCatGazeAt)
        {
            _catGazeTarget = new Point((_random.NextDouble() - 0.5) * 0.8, (_random.NextDouble() - 0.5) * 0.5);
            _nextCatGazeAt = t + 1.2 + _random.NextDouble() * 3;
        }
        var target = _catGazeTarget;
        var speed = _roamVelocity.Length;
        if (speed > 40) target = new Point(_roamVelocity.X / speed * 0.6, _roamVelocity.Y / speed * 0.4);
        if (_attention > 0.5) target = new Point(0, 0.1); // elle te regarde
        var follow = 1 - Math.Exp(-dt * 10);
        _catGaze = new Point(_catGaze.X + (target.X - _catGaze.X) * follow, _catGaze.Y + (target.Y - _catGaze.Y) * follow);

        if (t > _nextCatBlinkAt)
        {
            _catBlinkStart = t;
            _nextCatBlinkAt = t + (2.5 + _random.NextDouble() * 4.5) * (1 + _sleepiness);
        }
        var sinceBlink = t - _catBlinkStart;
        var blink = sinceBlink is >= 0.0 and < 0.2 ? Math.Sin(Math.PI * sinceBlink / 0.2) : 0;
        var closed = Math.Max(Math.Max(blink, 0.75 * _sleepiness), _speaking ? 0.18 : 0);

        var eyeWidth = size * 0.17;
        var eyeHeight = size * 0.2 * (1 - 0.9 * closed);
        var eyeY = b.Top + b.Height * 0.43;
        // Pupille : fente fine au repos, plus ronde quand elle t'écoute (curieuse).
        var pupilWidth = eyeWidth * (0.28 + 0.3 * _attention);
        var pupilHeight = eyeHeight * 0.86;
        var shine = eyeWidth * 0.2;
        CatEyeStrokeBrush.Color = Lerp(_shownColor, Color.FromRgb(0x90, 0x20, 0x55), 0.6);
        CatEyeLeft.StrokeThickness = Math.Max(1, size * 0.01);
        for (var side = -1; side <= 1; side += 2)
        {
            var eye = side < 0 ? CatEyeLeft : CatEyeRight;
            var pupil = side < 0 ? CatPupilLeft : CatPupilRight;
            var gleam = side < 0 ? CatShineLeft : CatShineRight;
            var ex = cx + side * b.Width * 0.18;
            Place(eye, ex, eyeY, eyeWidth, eyeHeight);
            var px = ex + _catGaze.X * (eyeWidth - pupilWidth) / 2;
            var py = eyeY + _catGaze.Y * (eyeHeight - pupilHeight) / 2;
            Place(pupil, px, py, pupilWidth, pupilHeight);
            Place(gleam, px - pupilWidth * 0.3, py - pupilHeight * 0.25, shine, shine);
            gleam.Opacity = closed > 0.6 ? 0 : 1;
        }
    }

    private static void Place(FrameworkElement element, double centerX, double centerY, double width, double height)
    {
        element.Width = Math.Max(0.5, width);
        element.Height = Math.Max(0.5, height);
        System.Windows.Controls.Canvas.SetLeft(element, centerX - element.Width / 2);
        System.Windows.Controls.Canvas.SetTop(element, centerY - element.Height / 2);
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
