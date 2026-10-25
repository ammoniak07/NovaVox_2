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
/// dans l'écran et ne jamais s'arrêter dans une zone interdite. Elle peut en
/// traverser une, mais très vite, pour ne pas gêner le joueur ; un trajet
/// sans traversée est préféré quand il existe.
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

    /// <summary>Le trajet en ligne droite traverse-t-il une zone (sans exiger de rester dans l'écran) ?</summary>
    public static bool CrossesZones(Box from, Box to, IReadOnlyList<Box> forbidden)
    {
        var distance = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
        var steps = Math.Max(2, (int)Math.Ceiling(distance / Math.Max(4, Math.Min(from.Width, from.Height) / 4)));
        for (var i = 0; i <= steps; i++)
        {
            var k = (double)i / steps;
            var box = from.MovedTo(from.X + (to.X - from.X) * k, from.Y + (to.Y - from.Y) * k);
            foreach (var zone in forbidden)
                if (box.Intersects(zone)) return true;
        }
        return false;
    }

    /// <summary>
    /// Point de départ hors de l'écran pour une entrée en trombe vers
    /// <paramref name="target"/> : par le bord le plus proche dont le trajet
    /// (perpendiculaire au bord) ne traverse aucune zone interdite ; à défaut,
    /// le bord le plus proche (la bulle traversera alors la zone très vite).
    /// <paramref name="margin"/> : distance en plus au-delà du bord (pour que
    /// le halo soit lui aussi hors de l'écran).
    /// </summary>
    public static Box EntryStart(Box target, Box screen, IReadOnlyList<Box> forbidden, double margin)
    {
        var candidates = new[]
        {
            target.MovedTo(screen.X - target.Width - margin, target.Y),
            target.MovedTo(screen.Right + margin, target.Y),
            target.MovedTo(target.X, screen.Y - target.Height - margin),
            target.MovedTo(target.X, screen.Bottom + margin),
        }.OrderBy(c => Math.Abs(c.X - target.X) + Math.Abs(c.Y - target.Y)).ToList();
        return candidates.FirstOrDefault(c => !CrossesZones(c, target, forbidden), candidates[0]);
    }

    /// <summary>
    /// Prochaine destination : une place autorisée tirée au hasard, assez
    /// loin pour que le déplacement se voie (au moins <paramref name="minDistance"/>).
    /// CrossesZone = true si le trajet traverse une zone (aucune place libre
    /// n'était atteignable sans traversée) : la bulle la passe alors en
    /// trombe. Null si aucune place n'est libre.
    /// </summary>
    public static (Box Target, bool CrossesZone)? PickTarget(Box current, Box screen, IReadOnlyList<Box> forbidden, Random random, double minDistance, int attempts = 80, bool explore = false, bool allowCrossing = true)
    {
        if (screen.Width < current.Width || screen.Height < current.Height) return null;
        if (explore) return PickFarTarget(current, screen, forbidden, random, minDistance, attempts);
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
        return allowCrossing && fallback is { } f ? (f, true) : null;
    }

    /// <summary>
    /// Changement de côté sans jamais entrer dans une zone : une place loin
    /// d'ici (parmi les plus éloignées). Si le chemin direct est libre, elle
    /// y va directement (Exit/Entry null). Sinon elle "fait le tour" par les
    /// côtés de l'écran : pour aller à droite, elle sort par le bord gauche
    /// (Exit, hors écran, même hauteur qu'elle) et rentre par le bord droit
    /// (Entry, hors écran, à la hauteur de la destination) — et inversement.
    /// Les deux morceaux de trajet visibles doivent être libres. Null si
    /// aucune place lointaine n'est atteignable ainsi.
    /// </summary>
    public static (Box Target, Box? Exit, Box? Entry)? PickSideChange(Box current, Box screen, IReadOnlyList<Box> forbidden, Random random, double minDistance, double margin, int attempts = 120)
    {
        var candidates = new List<(Box Box, double Distance)>();
        for (var i = 0; i < attempts; i++)
        {
            var candidate = current.MovedTo(
                screen.X + random.NextDouble() * (screen.Width - current.Width),
                screen.Y + random.NextDouble() * (screen.Height - current.Height));
            if (!IsAllowed(candidate, screen, forbidden)) continue;
            var dx = candidate.X - current.X;
            var dy = candidate.Y - current.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance >= minDistance) candidates.Add((candidate, distance));
        }
        var farthest = candidates.OrderByDescending(c => c.Distance).Take(Math.Max(1, candidates.Count / 3))
            .OrderBy(_ => random.Next()).ToList();
        foreach (var (target, _) in farthest)
        {
            if (IsPathClear(current, target, screen, forbidden)) return (target, null, null);
            var goingRight = target.CenterX > current.CenterX;
            var exit = current.MovedTo(goingRight ? screen.X - current.Width - margin : screen.Right + margin, current.Y);
            var entry = target.MovedTo(goingRight ? screen.Right + margin : screen.X - target.Width - margin, target.Y);
            if (!CrossesZones(current, exit, forbidden) && !CrossesZones(entry, target, forbidden))
                return (target, exit, entry);
        }
        return null;
    }

    /// <summary>
    /// Grande traversée : une place loin d'ici (parmi le tiers des places
    /// autorisées les plus éloignées, au hasard), même s'il faut traverser
    /// une zone pour y aller — sinon, avec une zone qui coupe l'écran en
    /// deux, la bulle resterait toujours du même côté.
    /// </summary>
    private static (Box Target, bool CrossesZone)? PickFarTarget(Box current, Box screen, IReadOnlyList<Box> forbidden, Random random, double minDistance, int attempts)
    {
        var candidates = new List<(Box Box, double Distance)>();
        for (var i = 0; i < attempts; i++)
        {
            var candidate = current.MovedTo(
                screen.X + random.NextDouble() * (screen.Width - current.Width),
                screen.Y + random.NextDouble() * (screen.Height - current.Height));
            if (!IsAllowed(candidate, screen, forbidden)) continue;
            var dx = candidate.X - current.X;
            var dy = candidate.Y - current.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance >= minDistance) candidates.Add((candidate, distance));
        }
        if (candidates.Count == 0) return null;
        var farthest = candidates.OrderByDescending(c => c.Distance).Take(Math.Max(1, candidates.Count / 3)).ToList();
        var chosen = farthest[random.Next(farthest.Count)].Box;
        return (chosen, !IsPathClear(current, chosen, screen, forbidden));
    }
}
