namespace NovaVox.Core.Audio;

/// <summary>
/// Calculs du son spatialisé de la bulle : gauche/droite selon sa position
/// à l'écran, volume et léger étouffement selon sa distance (effet de
/// profondeur). Stéréo seulement : pas de vrai « devant / derrière ».
/// </summary>
public static class SpatialAudio
{
    /// <summary>Panoramique (-1 = gauche, +1 = droite) d'une bulle centrée en <paramref name="centerX"/> sur un écran ; jamais complètement d'un seul côté (±0,85).</summary>
    public static double Pan(double centerX, double screenLeft, double screenWidth)
    {
        if (screenWidth <= 0) return 0;
        var relative = (centerX - (screenLeft + screenWidth / 2)) / (screenWidth / 2);
        return Math.Clamp(relative, -1, 1) * 0.85;
    }

    /// <summary>Volume selon la distance : 1 tout près (échelle 1), environ 0,78 à 2 m (échelle 0,5), jamais sous 0,5.</summary>
    public static double Gain(double depthScale) => Math.Clamp(0.55 + 0.45 * depthScale, 0.5, 1);

    /// <summary>Coefficient du filtre « étouffé » (1 = son net) : plus elle est loin, plus les aigus sont adoucis, sans gêner la voix.</summary>
    public static double LowPassAlpha(double depthScale) => Math.Clamp(0.55 + 0.45 * depthScale, 0.55, 1);

    /// <summary>Gains gauche/droite à puissance constante (le volume perçu ne change pas en traversant l'écran).</summary>
    public static (double Left, double Right) PanGains(double pan)
    {
        var angle = (Math.Clamp(pan, -1, 1) + 1) * Math.PI / 4;
        return (Math.Cos(angle), Math.Sin(angle));
    }
}
