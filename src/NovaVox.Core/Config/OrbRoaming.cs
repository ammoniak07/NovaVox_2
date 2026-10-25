namespace NovaVox.Core.Config;

/// <summary>Rectangle simple (x, y = coin haut-gauche), sans dépendance à WPF.</summary>
public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;

    public bool Intersects(Box other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    public bool Contains(Box inner) =>
        inner.X >= X && inner.Y >= Y && inner.Right <= Right && inner.Bottom <= Bottom;

    public Box MovedTo(double x, double y) => this with { X = x, Y = y };
}

/// <summary>
/// Zone interdite de la bulle baladeuse, en fractions de l'écran (0..1) :
/// indépendante de la résolution, elle reste au même endroit visuel si
/// la résolution change.
/// </summary>
public sealed record OrbZone(double X, double Y, double Width, double Height)
{
    public Box ToScreen(Box screen) =>
        new(screen.X + X * screen.Width, screen.Y + Y * screen.Height, Width * screen.Width, Height * screen.Height);

    /// <summary>Zone normalisée (dans l'écran, taille positive), null si trop petite pour compter.</summary>
    public static OrbZone? FromScreen(Box rect, Box screen)
    {
        if (screen.Width <= 0 || screen.Height <= 0) return null;
        double x1 = Math.Clamp((Math.Min(rect.X, rect.Right) - screen.X) / screen.Width, 0, 1);
        double y1 = Math.Clamp((Math.Min(rect.Y, rect.Bottom) - screen.Y) / screen.Height, 0, 1);
        double x2 = Math.Clamp((Math.Max(rect.X, rect.Right) - screen.X) / screen.Width, 0, 1);
        double y2 = Math.Clamp((Math.Max(rect.Y, rect.Bottom) - screen.Y) / screen.Height, 0, 1);
        if (x2 - x1 < 0.005 || y2 - y1 < 0.005) return null;
        return new OrbZone(x1, y1, x2 - x1, y2 - y1);
    }
}

/// <summary>
/// Choix des déplacements de la bulle baladeuse : elle doit rester entière
/// dans l'écran, ne jamais toucher une zone interdite, et si possible y
/// aller en ligne droite sans traverser de zone (sinon : elle disparaît et
/// réapparaît à destination).
/// </summary>
public static class OrbRoaming
{
    /// <summary>La bulle (rectangle <paramref name="orb"/>) est-elle à une place autorisée ?</summary>
    public static bool IsAllowed(Box orb, Box screen, IReadOnlyList<Box> forbidden)
    {
        if (!screen.Contains(orb)) return false;
        foreach (var zone in forbidden)
            if (orb.Intersects(zone)) return false;
        return true;
    }

    /// <summary>Le trajet en ligne droite de <paramref name="from"/> à <paramref name="to"/> évite-t-il toutes les zones ?</summary>
    public static bool IsPathClear(Box from, Box to, Box screen, IReadOnlyList<Box> forbidden)
    {
        var distance = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
        var steps = Math.Max(2, (int)Math.Ceiling(distance / Math.Max(4, Math.Min(from.Width, from.Height) / 4)));
        for (var i = 0; i <= steps; i++)
        {
            var k = (double)i / steps;
            if (!IsAllowed(from.MovedTo(from.X + (to.X - from.X) * k, from.Y + (to.Y - from.Y) * k), screen, forbidden)) return false;
        }
        return true;
    }

    /// <summary>
    /// Prochaine destination : une place autorisée tirée au hasard, assez
    /// loin pour que le déplacement se voie (au moins <paramref name="minDistance"/>).
    /// Teleport = true si aucune n'est atteignable en ligne droite (il faut
    /// disparaître puis réapparaître). Null si aucune place n'est libre.
    /// </summary>
    public static (Box Target, bool Teleport)? PickTarget(Box current, Box screen, IReadOnlyList<Box> forbidden, Random random, double minDistance, int attempts = 80)
    {
        if (screen.Width < current.Width || screen.Height < current.Height) return null;
        Box? fallback = null;
        for (var i = 0; i < attempts; i++)
        {
            var candidate = current.MovedTo(
                screen.X + random.NextDouble() * (screen.Width - current.Width),
                screen.Y + random.NextDouble() * (screen.Height - current.Height));
            if (!IsAllowed(candidate, screen, forbidden)) continue;
            var dx = candidate.X - current.X;
            var dy = candidate.Y - current.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < minDistance) continue;
            if (IsPathClear(current, candidate, screen, forbidden)) return (candidate, false);
            fallback ??= candidate;
        }
        return fallback is { } f ? (f, true) : null;
    }
}
