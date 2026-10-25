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
        if (!DepthOn || _speaking || hearing) target = NearDistance;
        else if (_thinking) target = NearDistance + (FarDistance - NearDistance) * 0.3;
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
        var box = OrbBoxAt(topLeft);
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

    /// <summary>Place la fenêtre : position de base + flottement + perspective (lissée pour éviter les à-coups).</summary>
    private void PlaceWindow(Point topLeft)
    {
        var wanted = PerspectiveOffset(topLeft);
        _projectionOffset += (wanted - _projectionOffset) * (1 - Math.Exp(-_lastDt * 8));
        var x = topLeft.X + _projectionOffset.X;
        var y = topLeft.Y + _bobOffset + _projectionOffset.Y;
        if (Math.Abs(Left - x) > 0.01) Left = x;
        if (Math.Abs(Top - y) > 0.01) Top = y;
    }
}
