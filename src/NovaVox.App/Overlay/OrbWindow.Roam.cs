using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using NovaVox.Core.Config;

namespace NovaVox.App.Overlay;

/// <summary>
/// Bulle baladeuse et apparition/disparition au repos. Écran utilisé :
/// celui où se trouve la position choisie de la bulle (détecté tout seul).
/// Elle va de place en place comme une petite créature : elle prend son
/// élan, avance en ondulant, s'étire dans le sens du mouvement, regarde où
/// elle va, se pose avec un petit rebond et flotte sur place. Elle ne
/// s'arrête jamais dans une zone interdite (ni sur le premier overlay) :
/// si elle doit en traverser une, elle la passe en trombe. Elle s'arrête
/// pour t'écouter ou réfléchir et ralentit quand elle parle. Au repos
/// (option "Disparaît au repos"), elle quitte l'écran en filant vers le bord
/// le plus proche, et revient à toute vitesse depuis le bord quand il se
/// passe quelque chose.
/// </summary>
public partial class OrbWindow
{
    private double _homeLeft, _homeTop;
    private bool _roamInit, _wasFullyHidden;
    private Point _roamPos;
    private Vector _roamVelocity;
    private Point? _roamTarget;
    private double _dwellUntil, _screenCheckedAt = double.NegativeInfinity, _roamTilt;
    private double _appear = 1;
    private Rect _screenDips = SystemParameters.WorkArea;

    // Trajet en cours : départ après la prise d'élan, vitesse propre au trajet, ondulation.
    private double _departAt, _tripSpeed = 1, _wobblePhase, _wobbleFrequency = 0.7;
    private bool _tripCrosses;
    private double _nextExploreAt = 120 + Random.Shared.NextDouble() * 360;
    private double _lastSideChangeAt = double.NegativeInfinity;
    /// <summary>Délai minimal entre deux changements de côté de l'écran.</summary>
    private const double MinSecondsBetweenSideChanges = 120;
    /// <summary>Changement de côté par les bords : 0 = non, 1 = sort par un bord, 2 = rentre par l'autre.</summary>
    private int _wrapPhase;
    private Point _wrapEntry, _wrapFinal;
    /// <summary>Points suivants d'un passage le long du haut/bas de l'écran (le dernier = destination).</summary>
    private readonly Queue<Point> _routeWaypoints = new();
    /// <summary>Taille pendant un passage étroit (1 = normale), et sa valeur affichée qui y tend en douceur.</summary>
    private double _passScaleTarget = 1, _passScale = 1;

    private bool InPassage => _routeWaypoints.Count > 0 || _passScaleTarget < 1;

    /// <summary>Décale la position pour que la bulle (à sa taille actuelle) reste entière dans l'écran.</summary>
    private Point KeepInside(Point topLeft, Box screen)
    {
        var box = OrbBoxAt(topLeft);
        var dx = box.X < screen.X ? screen.X - box.X : box.Right > screen.Right ? screen.Right - box.Right : 0;
        var dy = box.Y < screen.Y ? screen.Y - box.Y : box.Bottom > screen.Bottom ? screen.Bottom - box.Bottom : 0;
        return new Point(topLeft.X + dx, topLeft.Y + dy);
    }
    // Flottement sur place (décalage vertical affiché, pas la position "réelle").
    private double _bobPhase, _bobOffset;
    /// <summary>Étirement dans le sens du mouvement (direction et intensité), utilisé par BuildBlob.</summary>
    private Vector _motionStretch;

    // Entrée en trombe depuis le bord de l'écran, et départ vers le bord au repos.
    private bool _entering, _entryImpactDone, _exiting;
    private Point _entryTarget, _exitFrom;
    private Vector _exitDirection;
    private double _exitDepartAt;

    /// <summary>Délai au repos avant de disparaître (option "Disparaît au repos").</summary>
    private const double HideAfterSeconds = 3;
    /// <summary>Vitesse (unités WPF/s) pour traverser une zone interdite : plus rapide que n'importe quel trajet.</summary>
    private const double ZoneCrossingSpeed = 15000;

    /// <summary>Autres fenêtres à éviter automatiquement (le premier overlay), en unités WPF.</summary>
    public Func<IEnumerable<Rect>>? ExtraForbiddenAreas { get; set; }

    private bool IsRoaming => _config.Roam && !_moveMode;

    /// <summary>Écran de la bulle (en unités WPF), pour l'éditeur des zones interdites.</summary>
    public Rect CurrentScreen()
    {
        RefreshScreen(_clock.Elapsed.TotalSeconds);
        return _screenDips;
    }

    /// <summary>Place occupée par la bulle à sa position choisie (repère dans l'éditeur des zones).</summary>
    public Rect HomeOrbRect()
    {
        var box = OrbBoxAt(new Point(_homeLeft, _homeTop));
        return new Rect(box.X, box.Y, box.Width, box.Height);
    }

    /// <summary>Rectangle de la bulle elle-même (sans le halo) quand la fenêtre est en <paramref name="topLeft"/>.</summary>
    private Box OrbBoxAt(Point topLeft)
    {
        var size = OrbSize;
        var cx = Width / 2;
        var cy = size * GlowMarginRatio + size * 0.6;
        var halfWidth = size * 0.55 * _passScale;
        var halfHeight = size * 0.62 * _passScale;
        return new Box(topLeft.X + cx - halfWidth, topLeft.Y + cy - halfHeight, 2 * halfWidth, 2 * halfHeight);
    }

    private double OrbSize => Math.Clamp(_config.Size, OrbConfig.MinSize, OrbConfig.MaxSize);

    private void RefreshScreen(double t)
    {
        _screenCheckedAt = t;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var center = new NativePoint
            {
                X = (int)Math.Round((_homeLeft + Width / 2) * dpi.DpiScaleX),
                Y = (int)Math.Round((_homeTop + Height / 2) * dpi.DpiScaleY),
            };
            var monitor = MonitorFromPoint(center, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                var r = info.Monitor;
                _screenDips = new Rect(r.Left / dpi.DpiScaleX, r.Top / dpi.DpiScaleY, (r.Right - r.Left) / dpi.DpiScaleX, (r.Bottom - r.Top) / dpi.DpiScaleY);
                return;
            }
        }
        catch
        {
            // Repli ci-dessous.
        }
        _screenDips = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }

    private void UpdateRoamAndVisibility(double t, double dt, bool hearing)
    {
        if (!IsRoaming && _routeWaypoints.Count == 0) _passScaleTarget = 1;
        _passScale += (_passScaleTarget - _passScale) * (1 - Math.Exp(-dt * 10)); // rétrécit pendant la prise d'élan
        // --- Départ au repos (elle quitte l'écran) / retour en trombe ------
        var restHide = _config.HideAtRest && !_moveMode && !_entering && t - _lastActivityAt >= HideAfterSeconds;
        if (restHide && !_wasFullyHidden && !_exiting) StartExit(t);
        if (_exiting && !restHide)
        {
            // Quelque chose se passe pendant qu'elle s'en va : elle fait demi-tour.
            _exiting = false;
            FlyTo(_exitFrom);
        }
        if (!restHide && _wasFullyHidden)
        {
            _wasFullyHidden = false;
            StartEntrance(t);
        }
        _appear = _wasFullyHidden ? 0 : 1;
        RootGrid.Opacity = _appear;

        if (_exiting)
        {
            StepExit(t, dt);
            UpdateMotionStretch(dt);
            return;
        }
        if (_wasFullyHidden) return;
        if (_entering)
        {
            StepEntrance(t, dt);
            UpdateMotionStretch(dt);
            return;
        }

        // --- Balade --------------------------------------------------------
        if (!IsRoaming)
        {
            if (_roamInit)
            {
                _roamInit = false;
                Left = _homeLeft;
                Top = _homeTop;
            }
            _roamVelocity = default;
            _roamTilt *= Math.Exp(-dt * 3);
            UpdateMotionStretch(dt);
            return;
        }
        if (!_roamInit)
        {
            _roamInit = true;
            _roamPos = new Point(Left, Top);
            _roamVelocity = default;
            _roamTarget = null;
            _dwellUntil = t + 1;
        }
        if (t - _screenCheckedAt > 3) RefreshScreen(t);

        var screen = ScreenBox();
        var zones = ForbiddenBoxes(screen);
        var here = OrbBoxAt(_roamPos);
        var inZone = TouchesZone(here, zones);
        var size = OrbSize;

        // Une zone vient d'être posée sur elle : elle en sort tout de suite.
        if (inZone && _roamTarget is null && PickSpot(minDistance: 0) is { } escape)
        {
            _roamTarget = escape.TopLeft;
            _tripCrosses = true;
            _tripSpeed = 1;
            _departAt = t;
        }

        // Elle s'arrête pour t'écouter ou réfléchir, ralentit quand elle parle — jamais dans une zone.
        var speedFactor = _speaking ? 0.4 : hearing || _thinking ? 0 : 1;
        if (inZone) speedFactor = 1;

        // Pendant un changement de côté par les bords, elle ne s'arrête pas à moitié hors de l'écran.
        if (_wrapPhase != 0) speedFactor = 1;

        if (_roamTarget is null && speedFactor > 0 && t > _dwellUntil)
        {
            Box? chosen = null;
            // Changement de côté (en moyenne toutes les 5 minutes, jamais moins de 2 minutes après le
            // précédent) : sans jamais entrer dans une zone. Si une zone barre le chemin, elle fait le
            // tour : elle sort par le bord opposé à sa destination et rentre par l'autre bord.
            if (t >= _nextExploreAt && t - _lastSideChangeAt >= MinSecondsBetweenSideChanges)
            {
                _nextExploreAt = t + MinSecondsBetweenSideChanges + _random.NextDouble() * 360;
                var plan = OrbRoaming.PickSideChange(here, screen, zones, _random, minDistance: Math.Max(size * 1.5, screen.Width * 0.4), margin: size * 0.7);
                if (plan is { } side)
                {
                    _lastSideChangeAt = t;
                    chosen = side.Target;
                    if (side.Exit is { } exit && side.Entry is { } entry)
                    {
                        _wrapPhase = 1;
                        _wrapEntry = new Point(_roamPos.X + entry.X - here.X, _roamPos.Y + entry.Y - here.Y);
                        _wrapFinal = new Point(_roamPos.X + side.Target.X - here.X, _roamPos.Y + side.Target.Y - here.Y);
                        chosen = exit;
                    }
                    else if (side.Waypoints.Count > 0)
                    {
                        // Passage le long du haut ou du bas de l'écran, en rétrécissant si besoin.
                        _routeWaypoints.Clear();
                        foreach (var corner in side.Waypoints.Skip(1))
                            _routeWaypoints.Enqueue(new Point(_roamPos.X + corner.X - here.X, _roamPos.Y + corner.Y - here.Y));
                        _routeWaypoints.Enqueue(new Point(_roamPos.X + side.Target.X - here.X, _roamPos.Y + side.Target.Y - here.Y));
                        chosen = side.Waypoints[0];
                        _passScaleTarget = side.Scale;
                    }
                }
            }
            // Petit déplacement de son côté : uniquement par un chemin libre.
            chosen ??= OrbRoaming.PickTarget(here, screen, zones, _random, minDistance: size * 1.5, allowCrossing: false)?.Target;
            if (chosen is null) _dwellUntil = t + 3;
            else
            {
                _roamTarget = new Point(_roamPos.X + chosen.Value.X - here.X, _roamPos.Y + chosen.Value.Y - here.Y);
                _tripCrosses = false;
                // Chaque trajet a son allure ; parfois une petite course rapide.
                var low = Math.Clamp(_config.RoamSpeedMin, OrbConfig.MinRoamSpeed, OrbConfig.MaxRoamSpeed);
                var high = Math.Clamp(_config.RoamSpeedMax, low, OrbConfig.MaxRoamSpeed);
                _tripSpeed = (low + _random.NextDouble() * (high - low)) / 100.0;
                _wobblePhase = _random.NextDouble() * Math.PI * 2;
                _wobbleFrequency = 0.4 + _random.NextDouble() * 0.6;
                // Prise d'élan : elle se tasse et recule un peu avant de partir.
                _departAt = t + 0.35 + _random.NextDouble() * 0.25;
                _squashVelocity -= 0.6;
                var away = _roamPos - _roamTarget.Value;
                Kick(Math.Atan2(away.Y, away.X), 0.09, 1.0);
            }
        }

        var desired = new Vector();
        Vector direction = default;
        if (_roamTarget is { } target && t >= _departAt)
        {
            var toTarget = target - _roamPos;
            var distance = toTarget.Length;
            if (_wrapPhase == 1 && !VisibleArea(screen).Intersects(here))
            {
                // Sortie complète par un bord : elle réapparaît de l'autre côté de l'écran, lancée vers sa place.
                _bobOffset = 0;
                MoveWindowTo(_wrapEntry);
                _wrapPhase = 2;
                _roamTarget = _wrapFinal;
                var toFinal = _wrapFinal - _roamPos;
                if (toFinal.Length > 0) _roamVelocity = toFinal / toFinal.Length * _roamVelocity.Length;
                here = OrbBoxAt(_roamPos);
                target = _wrapFinal;
                toTarget = target - _roamPos;
                distance = toTarget.Length;
            }
            // Coin d'un passage : elle enchaîne vers le point suivant sans se poser.
            if (_routeWaypoints.Count > 0 && distance < Math.Max(8, _roamVelocity.Length * 0.12))
            {
                target = _routeWaypoints.Dequeue();
                _roamTarget = target;
                toTarget = target - _roamPos;
                distance = toTarget.Length;
            }
            if (_wrapPhase != 1 && _routeWaypoints.Count == 0 && distance < Math.Max(2, _roamVelocity.Length * 0.02))
            {
                _wrapPhase = 0;
                _passScaleTarget = 1;
                // Atterrissage : petit rebond dans le sens où elle arrivait.
                var landing = Math.Min(1, _roamVelocity.Length / 150);
                _squashVelocity -= 0.6 * landing;
                if (_roamVelocity.Length > 1) Kick(Math.Atan2(_roamVelocity.Y, _roamVelocity.X), 0.1 * landing, 1.0);
                _roamTarget = null;
                _dwellUntil = t + 3 + _random.NextDouble() * 7;
            }
            else if (distance > 1e-6)
            {
                direction = toTarget / distance;
                // Vitesse non linéaire : 100 % = 70, 300 % ≈ 700, 500 % ≈ 2060, 1000 % ≈ 8800 unités/s.
                var maxSpeed = 70 * Math.Pow(_tripSpeed, 2.1) * speedFactor;
                if (inZone) maxSpeed = Math.Max(maxSpeed, ZoneCrossingSpeed);
                // Freinage à l'approche proportionnel à sa vitesse : rapide, elle freine tard et fort.
                var braking = Math.Max(60, maxSpeed * 1.2);
                // En sortant par un bord, elle ne freine pas : elle file hors de l'écran.
                // Vers un coin de passage, elle ralentit juste assez pour tourner sans se poser.
                var cornerSpeed = _routeWaypoints.Count > 0 ? 0.35 * maxSpeed : 4 * speedFactor;
                var forward = _wrapPhase == 1 ? maxSpeed : Math.Min(maxSpeed, Math.Sqrt(2 * braking * distance) + cornerSpeed);
                // Elle ondule au lieu d'aller tout droit (sauf dans une zone, qu'elle file traverser).
                var perpendicular = new Vector(-direction.Y, direction.X);
                var wobble = inZone || _wrapPhase != 0 || InPassage ? 0 : Math.Sin(t * _wobbleFrequency * 2 * Math.PI + _wobblePhase) * 0.35 / Math.Sqrt(Math.Max(1, _tripSpeed)) * maxSpeed * Math.Min(1, distance / (size * 2));
                desired = direction * forward + perpendicular * wobble;
            }
        }
        // Plus le trajet est rapide, plus elle accélère et freine sec.
        _roamVelocity += (desired - _roamVelocity) * (1 - Math.Exp(-dt * (inZone ? 30 : 1.5 + 0.6 * Math.Max(0, _tripSpeed - 1))));
        var next = _roamPos + _roamVelocity * dt;

        // Un trajet ne doit pas frôler une zone à cause de l'ondulation : on garde l'avance seule.
        if (!_tripCrosses && !inZone && direction != default && TouchesZone(OrbBoxAt(next), zones))
        {
            _roamVelocity = direction * Math.Max(0, Vector.Multiply(_roamVelocity, direction));
            next = _roamPos + _roamVelocity * dt;
        }
        // Dans un passage le long d'un bord : elle reste collée dans l'écran au lieu de s'arrêter.
        if (InPassage && _wrapPhase == 0) next = KeepInside(next, screen);
        // Toujours entière dans l'écran (sauf quand elle passe par un bord pour changer de côté).
        else if (_wrapPhase == 0 && !screen.Contains(OrbBoxAt(next)) && screen.Contains(here))
        {
            _roamVelocity = default;
            _roamTarget = null;
            next = _roamPos;
        }

        // Flotte doucement sur place quand elle ne bouge presque pas.
        var still = Math.Clamp(1 - _roamVelocity.Length / 60, 0, 1);
        _bobPhase += dt * 1.6;
        _bobOffset = 4 * still * Math.Sin(_bobPhase);
        MoveWindowTo(next);

        _roamTilt += (Math.Clamp(_roamVelocity.X / 400, -0.15, 0.15) - _roamTilt) * (1 - Math.Exp(-dt * 3));
        UpdateMotionStretch(dt);
    }

    /// <summary>
    /// Départ au repos : elle se tasse un instant, puis file en accélérant vers
    /// le bord de l'écran le plus proche et sort complètement (pas de fondu).
    /// </summary>
    private void StartExit(double t)
    {
        RefreshScreen(t);
        var screen = ScreenBox();
        var here = OrbBoxAt(_roamPos = new Point(Left, Top - _bobOffset));
        var outside = OrbRoaming.EntryStart(here, screen, ForbiddenBoxes(screen), margin: OrbSize * 0.7);
        var away = new Vector(outside.X - here.X, outside.Y - here.Y);
        if (away.Length > 0) away.Normalize();
        _exitDirection = away;
        _exitFrom = _entering ? _entryTarget : _roamPos;
        _exiting = true;
        _entering = false;
        _roamTarget = null;
        _wrapPhase = 0;
        _routeWaypoints.Clear();
        _passScaleTarget = 1;
        _exitDepartAt = t + 0.3;
        // Prise d'élan : elle se tasse et recule un peu à l'opposé.
        _squashVelocity -= 0.8;
        Kick(Math.Atan2(-away.Y, -away.X), 0.12, 1.0);
        _roamVelocity = -away * 60;
    }

    private void StepExit(double t, double dt)
    {
        if (t >= _exitDepartAt)
        {
            _roamVelocity += _exitDirection * 6000 * dt;
            if (_roamVelocity.Length > 5000) _roamVelocity *= 5000 / _roamVelocity.Length;
        }
        else _roamVelocity *= Math.Exp(-dt * 6);
        _bobOffset = 0;
        MoveWindowTo(_roamPos + _roamVelocity * dt);
        _roamTilt += (Math.Clamp(_roamVelocity.X / 1500, -0.25, 0.25) - _roamTilt) * (1 - Math.Exp(-dt * 8));

        // Sortie complète (halo compris) : elle est partie.
        var screen = ScreenBox();
        var glow = OrbSize * 0.6;
        var visibleArea = new Box(screen.X - glow, screen.Y - glow, screen.Width + 2 * glow, screen.Height + 2 * glow);
        if (!visibleArea.Intersects(OrbBoxAt(_roamPos)))
        {
            _exiting = false;
            _wasFullyHidden = true;
            _roamVelocity = default;
            _motionStretch = default;
        }
    }

    /// <summary>Revient vers <paramref name="destination"/> depuis là où elle est (même mouvement que l'entrée en trombe).</summary>
    private void FlyTo(Point destination)
    {
        _entryTarget = destination;
        _entering = true;
        _entryImpactDone = false;
        _roamTarget = null;
    }

    /// <summary>Retour après une disparition : part de hors de l'écran (bord le plus proche) et fonce vers sa place.</summary>
    private void StartEntrance(double t)
    {
        RefreshScreen(t);
        var screen = ScreenBox();
        var zones = ForbiddenBoxes(screen);
        if (!IsRoaming) _roamPos = new Point(_homeLeft, _homeTop);
        var destination = IsRoaming ? PickSpot(minDistance: 0)?.TopLeft ?? _roamPos : new Point(_homeLeft, _homeTop);
        var destinationBox = OrbBoxAt(destination);
        var startBox = OrbRoaming.EntryStart(destinationBox, screen, zones, margin: OrbSize * 0.7);
        var start = new Point(destination.X + startBox.X - destinationBox.X, destination.Y + startBox.Y - destinationBox.Y);

        _bobOffset = 0;
        MoveWindowTo(start);
        var direction = destination - start;
        if (direction.Length > 0) direction.Normalize();
        _roamVelocity = direction * 2800;
        _roamTarget = null;
        _wrapPhase = 0;
        _routeWaypoints.Clear();
        _passScaleTarget = 1;
        _entryTarget = destination;
        _entering = true;
        _entryImpactDone = false;
        _appear = 1;
        RootGrid.Opacity = 1;
    }

    /// <summary>Entrée en trombe : ressort raide et peu amorti — elle arrive très vite, freine d'un coup, dépasse un peu et revient.</summary>
    private void StepEntrance(double t, double dt)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(dt / (1.0 / 240)));
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            var acceleration = 60 * (_entryTarget - _roamPos) - 13 * _roamVelocity;
            _roamVelocity += acceleration * h;
            if (_roamVelocity.Length > 6000) _roamVelocity *= 6000 / _roamVelocity.Length;
            _roamPos += _roamVelocity * h;
        }
        var distance = (_entryTarget - _roamPos).Length;
        if (!_entryImpactDone && distance < OrbSize * 0.3)
        {
            // Freinage brutal : elle s'écrase un peu dans le sens de sa course.
            _entryImpactDone = true;
            _squashVelocity -= 1.2;
            Kick(Math.Atan2(_roamVelocity.Y, _roamVelocity.X), 0.3, 1.1);
            _lastActivityAt = Math.Max(_lastActivityAt, t);
        }
        if (distance < 1 && _roamVelocity.Length < 12)
        {
            _roamPos = _entryTarget;
            _roamVelocity = default;
            _entering = false;
            _dwellUntil = t + 2 + _random.NextDouble() * 3;
        }
        _roamTilt += (Math.Clamp(_roamVelocity.X / 1500, -0.25, 0.25) - _roamTilt) * (1 - Math.Exp(-dt * 8));
        MoveWindowTo(_roamPos);
    }

    /// <summary>Plus elle va vite, plus elle s'étire dans le sens du mouvement (et s'amincit sur les côtés).</summary>
    private void UpdateMotionStretch(double dt)
    {
        var wanted = _roamVelocity / 1800;
        if (wanted.Length > 0.4) wanted *= 0.4 / wanted.Length;
        _motionStretch += (wanted - _motionStretch) * (1 - Math.Exp(-dt * 10));
    }

    private (Point TopLeft, Box Area)? PickSpot(double minDistance)
    {
        var screen = ScreenBox();
        var current = OrbBoxAt(_roamPos);
        var pick = OrbRoaming.PickTarget(current, screen, ForbiddenBoxes(screen), _random, minDistance);
        if (pick is null) return null;
        var box = pick.Value.Target;
        return (new Point(_roamPos.X + box.X - current.X, _roamPos.Y + box.Y - current.Y), box);
    }

    private void MoveWindowTo(Point topLeft)
    {
        _roamPos = topLeft;
        var y = topLeft.Y + _bobOffset;
        if (Math.Abs(Left - topLeft.X) > 0.01) Left = topLeft.X;
        if (Math.Abs(Top - y) > 0.01) Top = y;
    }

    private static bool TouchesZone(Box box, List<Box> zones) => zones.Any(z => box.Intersects(z));

    /// <summary>Écran agrandi de la taille du halo : hors de cette zone, la bulle ne se voit plus du tout.</summary>
    private Box VisibleArea(Box screen)
    {
        var glow = OrbSize * 0.6;
        return new Box(screen.X - glow, screen.Y - glow, screen.Width + 2 * glow, screen.Height + 2 * glow);
    }

    private Box ScreenBox() => new(_screenDips.X, _screenDips.Y, _screenDips.Width, _screenDips.Height);

    /// <summary>Zones dessinées par le joueur + premier overlay (avec une petite marge).</summary>
    private List<Box> ForbiddenBoxes(Box screen)
    {
        var boxes = _config.ForbiddenZones.Select(z => z.ToScreen(screen)).ToList();
        try
        {
            foreach (var area in ExtraForbiddenAreas?.Invoke() ?? Enumerable.Empty<Rect>())
                if (!area.IsEmpty && area.Width > 0 && area.Height > 0)
                    boxes.Add(new Box(area.X - 12, area.Y - 12, area.Width + 24, area.Height + 24));
        }
        catch
        {
            // Overlay en cours de fermeture : on l'ignore.
        }
        return boxes;
    }

    // --- Win32 : écran contenant un point -----------------------------
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
