using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using NovaVox.Core.Config;

namespace NovaVox.App.Overlay;

/// <summary>
/// Bulle baladeuse et apparition/disparition au repos. Écran utilisé :
/// celui où se trouve la position choisie de la bulle (détecté tout seul).
/// Elle va de place en place sans jamais toucher les zones interdites ;
/// si aucune place n'est atteignable en ligne droite, elle disparaît et
/// réapparaît à destination. Elle s'arrête pour t'écouter ou réfléchir et
/// ralentit quand elle parle, pour ne pas distraire.
/// </summary>
public partial class OrbWindow
{
    private double _homeLeft, _homeTop;
    private bool _roamInit, _teleporting, _wasFullyHidden, _relocatedWhileHidden;
    private Point _roamPos, _teleportTo;
    private Vector _roamVelocity;
    private Point? _roamTarget;
    private double _dwellUntil, _screenCheckedAt = double.NegativeInfinity, _roamTilt;
    private double _appear = 1;
    private Rect _screenDips = SystemParameters.WorkArea;

    /// <summary>Délai au repos avant de disparaître (option "Disparaît au repos").</summary>
    private const double HideAfterSeconds = 3;

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
        var size = Math.Clamp(_config.Size, OrbConfig.MinSize, OrbConfig.MaxSize);
        var cx = Width / 2;
        var cy = size * GlowMarginRatio + size * 0.6;
        var halfWidth = size * 0.55;
        var halfHeight = size * 0.62;
        return new Box(topLeft.X + cx - halfWidth, topLeft.Y + cy - halfHeight, 2 * halfWidth, 2 * halfHeight);
    }

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
        // --- Apparition / disparition ---------------------------------
        var wantVisible = !_teleporting && (_moveMode || !_config.HideAtRest || t - _lastActivityAt < HideAfterSeconds);
        var appearTarget = wantVisible ? 1.0 : 0.0;
        _appear += (appearTarget - _appear) * (1 - Math.Exp(-dt * (appearTarget > _appear ? 7 : 3)));
        if (_appear < 0.02 && !wantVisible)
        {
            _appear = 0;
            _wasFullyHidden = true;
            if (_teleporting)
            {
                MoveWindowTo(_teleportTo);
                _teleporting = false;
                _roamTarget = null;
                _roamVelocity = default;
                _dwellUntil = t + 2;
            }
            else if (IsRoaming && !_relocatedWhileHidden)
            {
                // Cachée au repos : elle réapparaîtra ailleurs.
                _relocatedWhileHidden = true;
                var spot = PickSpot(minDistance: 0);
                if (spot is { } s) MoveWindowTo(s.TopLeft);
            }
        }
        if (wantVisible && _wasFullyHidden && _appear > 0.05)
        {
            // Apparition : petit "pop".
            _wasFullyHidden = false;
            _relocatedWhileHidden = false;
            _squashVelocity += 0.9;
            Kick(_random.NextDouble() * Math.PI * 2, 0.15, 1.3);
        }
        RootGrid.Opacity = _appear;

        // --- Balade ------------------------------------------------------
        if (!IsRoaming)
        {
            if (_roamInit)
            {
                _roamInit = false;
                _teleporting = false;
                Left = _homeLeft;
                Top = _homeTop;
            }
            _roamTilt *= Math.Exp(-dt * 3);
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

        // Une zone vient d'être posée sur elle (ou l'écran a changé) : elle file ailleurs.
        if (!_teleporting && !OrbRoaming.IsAllowed(here, screen, zones))
        {
            if (PickSpot(minDistance: 0) is { } escape)
            {
                _teleporting = true;
                _teleportTo = escape.TopLeft;
            }
        }

        // Elle s'arrête pour t'écouter ou réfléchir, et ralentit quand elle parle.
        var speedFactor = _speaking ? 0.4 : hearing || _thinking ? 0 : 1;
        if (_appear < 0.3) speedFactor = 0;

        if (_roamTarget is null && !_teleporting && speedFactor > 0 && t > _dwellUntil)
        {
            var size = Math.Clamp(_config.Size, OrbConfig.MinSize, OrbConfig.MaxSize);
            var pick = OrbRoaming.PickTarget(here, screen, zones, _random, minDistance: size * 1.5);
            if (pick is null) _dwellUntil = t + 3;
            else
            {
                var topLeft = new Point(_roamPos.X + pick.Value.Target.X - here.X, _roamPos.Y + pick.Value.Target.Y - here.Y);
                if (pick.Value.Teleport)
                {
                    _teleporting = true;
                    _teleportTo = topLeft;
                }
                else _roamTarget = topLeft;
            }
        }

        var desired = new Vector();
        if (_roamTarget is { } target)
        {
            var toTarget = target - _roamPos;
            var distance = toTarget.Length;
            if (distance < 2)
            {
                _roamTarget = null;
                _dwellUntil = t + 3 + _random.NextDouble() * 7;
            }
            else
            {
                var maxSpeed = 70 * Math.Clamp(_config.RoamSpeed, OrbConfig.MinRoamSpeed, OrbConfig.MaxRoamSpeed) / 100.0 * speedFactor;
                // Démarre et s'arrête en douceur (ralentit à l'approche).
                desired = toTarget / distance * Math.Min(maxSpeed, distance * 0.9 + 4 * speedFactor);
            }
        }
        _roamVelocity += (desired - _roamVelocity) * (1 - Math.Exp(-dt * 1.5));
        var next = _roamPos + _roamVelocity * dt;
        // Sécurité : jamais dans une zone interdite, même à cause de l'élan.
        if (OrbRoaming.IsAllowed(OrbBoxAt(next), screen, zones) || !OrbRoaming.IsAllowed(here, screen, zones)) MoveWindowTo(next);
        else
        {
            _roamVelocity = default;
            _roamTarget = null;
        }
        // Elle penche un peu dans le sens où elle avance.
        _roamTilt += (Math.Clamp(_roamVelocity.X / 400, -0.15, 0.15) - _roamTilt) * (1 - Math.Exp(-dt * 3));
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
        if (Math.Abs(Left - topLeft.X) > 0.01) Left = topLeft.X;
        if (Math.Abs(Top - topLeft.Y) > 0.01) Top = topLeft.Y;
    }

    private Box ScreenBox() => new(_screenDips.X, _screenDips.Y, _screenDips.Width, _screenDips.Height);

    private List<Box> ForbiddenBoxes(Box screen) => _config.ForbiddenZones.Select(z => z.ToScreen(screen)).ToList();

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
