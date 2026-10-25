using System.Windows;
using NovaVox.Core.Config;

namespace NovaVox.App.Overlay;

/// <summary>
/// Effet de profondeur : le joueur est au centre de l'écran, la bulle flotte
/// à une certaine distance de lui (2 m par défaut au repos). Plus elle est
/// loin, plus elle paraît petite et proche du centre (perspective) ; quand
/// elle parle ou t'entend, elle s'approche à 1 m (sa taille réglée) et
/// s'écarte un peu du centre en avançant, comme un objet qui vient vers toi.
/// Chaque trajet a sa propre profondeur : elle grandit en venant vers toi et
/// rapetisse en s'éloignant. La perspective ne la fait jamais entrer dans
/// une zone interdite ni sortir de l'écran.
/// </summary>
public partial class OrbWindow
{
    /// <summary>Distance quand elle te parle ou t'écoute : la bulle a alors sa taille réglée.</summary>
    private const double NearDistance = 1.0;

    private double _depth = 2, _tripDepth = 2, _depthScale = 1, _lastDt = 1.0 / 60;
    private Vector _projectionOffset;

    private bool DepthOn => _config.DepthEffect && !_moveMode;

    private double FarDistance => Math.Clamp(_config.FarDistance, OrbConfig.MinFarDistance, OrbConfig.MaxFarDistance);

    /// <summary>Nouvelle profondeur pour un trajet : un peu plus près ou plus loin que la distance de repos.</summary>
    private void PickTripDepth() =>
        _tripDepth = Math.Max(NearDistance * 1.2, FarDistance * (0.75 + _random.NextDouble() * 0.55));

    private void UpdateDepth(double dt, bool hearing)
    {
        _lastDt = dt;
        double target;
        // Pendant toute la conversation (blancs compris), elle reste près de toi.
        if (!DepthOn || _engaged) target = NearDistance;
        else target = IsRoaming ? _tripDepth : FarDistance;
        // Elle vient vite vers toi, et repart en douceur.
        var rate = target < _depth ? 2.5 : 0.8;
        _depth += (target - _depth) * (1 - Math.Exp(-dt * rate));
        _depthScale = DepthOn ? NearDistance / Math.Max(NearDistance, _depth) : 1;
    }

    /// <summary>
    /// Décalage de perspective par rapport au centre de l'écran pour la
    /// position de base <paramref name="topLeft"/> (celle qu'elle aurait à sa
    /// distance de repos). Réduit si la perspective la ferait toucher une
    /// zone ou sortir de l'écran ; aucun décalage si elle est hors de l'écran.
    /// </summary>
    private Vector PerspectiveOffset(Point topLeft)
    {
        if (!DepthOn) return default;
        var screen = ScreenBox();
        var zones = ForbiddenBoxes(screen);
        var box = VisualBoxAt(topLeft);
        var factor = FarDistance / Math.Max(NearDistance * 0.5, _depth); // > 1 : plus près, donc plus loin du centre
        for (var blend = 1.0; blend > 1e-9; blend -= 0.25)
        {
            var k = 1 + (factor - 1) * blend;
            var offset = new Vector((box.CenterX - screen.CenterX) * (k - 1), (box.CenterY - screen.CenterY) * (k - 1));
            var moved = box.MovedTo(box.X + offset.X, box.Y + offset.Y);
            if (screen.Contains(moved) && !TouchesZone(moved, zones)) return offset;
        }
        return default;
    }

    /// <summary>
    /// Place la fenêtre : position de base + flottement + perspective (lissée
    /// pour éviter les à-coups), puis butée ferme sur les bords : la bulle
    /// entière (avec ses déformations) ne sort jamais de l'écran, même quand
    /// elle grossit en s'approchant près d'un bord. Sauf quand elle doit
    /// justement sortir ou entrer (départ, retour, tour par les bords).
    /// </summary>
    private void PlaceWindow(Point topLeft)
    {
        var wanted = PerspectiveOffset(topLeft);
        _projectionOffset += (wanted - _projectionOffset) * (1 - Math.Exp(-_lastDt * 8));
        var x = topLeft.X + _projectionOffset.X;
        var y = topLeft.Y + _bobOffset + _projectionOffset.Y;
        if (!_entering && !_exiting && _wrapPhase == 0 && !_moveMode)
        {
            var screen = ScreenBox();
            var body = VisualBoxAt(new Point(x, y));
            if (body.Width <= screen.Width) x += body.X < screen.X ? screen.X - body.X : body.Right > screen.Right ? screen.Right - body.Right : 0;
            if (body.Height <= screen.Height) y += body.Y < screen.Y ? screen.Y - body.Y : body.Bottom > screen.Bottom ? screen.Bottom - body.Bottom : 0;
        }
        if (Math.Abs(Left - x) > 0.01) Left = x;
        if (Math.Abs(Top - y) > 0.01) Top = y;
    }

    /// <summary>
    /// Rectangle de la bulle telle qu'on la voit, déformations comprises
    /// (plus large que OrbBoxAt, qui sert aux trajets) : sert à la garder
    /// entière dans l'écran. Le halo flou, lui, peut dépasser.
    /// </summary>
    private Box VisualBoxAt(Point topLeft)
    {
        var size = OrbSize;
        var scale = _passScale * _depthScale * _gagInflate * (1 + 0.1 * _level + _gagStretch);
        var cx = Width / 2;
        var cy = size * GlowMarginRatio + size * 0.6;
        var stretch = 1 + _motionStretch.Length;
        var halfWidth = size * 0.66 * scale * stretch;
        var halfHeight = size * 0.72 * scale * stretch;
        return new Box(topLeft.X + cx - halfWidth, topLeft.Y + cy - halfHeight, 2 * halfWidth, 2 * halfHeight);
    }
}
