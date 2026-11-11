namespace NovaVox.Core.Avatar;

/// <summary>Point du maillage, en fraction du carré de l'avatar (0..1, y vers le bas).</summary>
public readonly record struct MeshPoint(double X, double Y)
{
    public double DistanceTo(MeshPoint o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));
}

/// <summary>
/// Tête de Nova en maillage « low-poly » holographique, vue de profil
/// (visage vers la droite) : contour, points répartis dedans et reliés en
/// triangles (Delaunay). Calculée une fois, toujours identique (graine
/// fixe) ; l'affichage et l'animation sont faits par la bulle (OrbWindow).
/// </summary>
public sealed class HeadMesh
{
    /// <summary>Contour du profil (sens horaire, en partant du haut du crâne).</summary>
    public static readonly MeshPoint[] Outline =
    {
        new(0.47, 0.06), new(0.58, 0.075), new(0.67, 0.12), new(0.735, 0.19), new(0.77, 0.27),
        new(0.78, 0.33),   // front
        new(0.80, 0.365),  // arcade sourcilière
        new(0.78, 0.405),  // creux de l'œil
        new(0.795, 0.44), new(0.83, 0.49), new(0.885, 0.545), // nez
        new(0.87, 0.565), new(0.825, 0.575), // dessous du nez
        new(0.835, 0.60), new(0.85, 0.62), // lèvre du haut
        new(0.82, 0.635), // bouche
        new(0.84, 0.655), new(0.825, 0.685), // lèvre du bas
        new(0.80, 0.70),   // creux du menton
        new(0.82, 0.74), new(0.80, 0.775), // menton
        new(0.74, 0.795), new(0.67, 0.805), // dessous de la mâchoire
        new(0.625, 0.84), new(0.615, 0.90), new(0.625, 0.97), // cou (devant)
        new(0.38, 0.97), new(0.395, 0.89), new(0.40, 0.81), // cou (derrière)
        new(0.34, 0.75), new(0.27, 0.69), // nuque
        new(0.21, 0.60), new(0.18, 0.50), new(0.18, 0.39), new(0.21, 0.28), new(0.26, 0.19), new(0.32, 0.125), new(0.39, 0.08), // arrière du crâne
    };

    /// <summary>
    /// Traits dessinés plus lumineux par-dessus le maillage : oreille, œil
    /// en amande, sourcil.
    /// </summary>
    public static readonly MeshPoint[][] FeatureStrokes =
    {
        // Oreille (forme de « C » ouvert vers l'avant) et son pli intérieur.
        new MeshPoint[] { new(0.47, 0.41), new(0.44, 0.395), new(0.405, 0.41), new(0.39, 0.45), new(0.395, 0.50), new(0.41, 0.54), new(0.44, 0.555), new(0.465, 0.545) },
        new MeshPoint[] { new(0.455, 0.44), new(0.425, 0.44), new(0.415, 0.475), new(0.43, 0.51), new(0.45, 0.515) },
        // Œil en amande.
        new MeshPoint[] { new(0.70, 0.43), new(0.72, 0.415), new(0.75, 0.42), new(0.765, 0.43), new(0.745, 0.445), new(0.715, 0.443), new(0.70, 0.43) },
        // Sourcil.
        new MeshPoint[] { new(0.68, 0.385), new(0.72, 0.37), new(0.765, 0.375), new(0.785, 0.385) },
    };

    /// <summary>
    /// Pistes de circuit qui partent de l'arrière du crâne vers la gauche
    /// (angles droits, pastille au bout) : des impulsions lumineuses y
    /// circulent quand Nova réfléchit.
    /// </summary>
    public static readonly MeshPoint[][] Traces =
    {
        new MeshPoint[] { new(0.25, 0.21), new(0.12, 0.21), new(0.08, 0.17), new(-0.10, 0.17) },
        new MeshPoint[] { new(0.205, 0.30), new(0.02, 0.30), new(-0.02, 0.26), new(-0.18, 0.26) },
        new MeshPoint[] { new(0.185, 0.38), new(-0.05, 0.38) },
        new MeshPoint[] { new(0.18, 0.46), new(0.06, 0.46), new(0.02, 0.50), new(-0.22, 0.50) },
        new MeshPoint[] { new(0.19, 0.55), new(0.08, 0.55), new(0.04, 0.59), new(-0.08, 0.59) },
        new MeshPoint[] { new(0.235, 0.64), new(0.13, 0.64), new(0.09, 0.68), new(-0.14, 0.68) },
    };

    /// <summary>Œil (point brillant qui cligne).</summary>
    public static readonly MeshPoint Eye = new(0.735, 0.43);
    /// <summary>Bouche : les ondes de la voix partent d'ici.</summary>
    public static readonly MeshPoint Mouth = new(0.81, 0.635);
    /// <summary>Oreille : les ondes d'écoute partent d'ici.</summary>
    public static readonly MeshPoint Ear = new(0.425, 0.475);
    /// <summary>Cerveau : la réflexion pulse depuis ici.</summary>
    public static readonly MeshPoint Brain = new(0.45, 0.27);

    public IReadOnlyList<MeshPoint> Nodes { get; }
    public IReadOnlyList<(int A, int B)> Edges { get; }
    public IReadOnlyList<(int A, int B, int C)> Triangles { get; }
    /// <summary>Indice du nœud de l'œil dans Nodes.</summary>
    public int EyeNode { get; }

    private static readonly Lazy<HeadMesh> Shared = new(() => new HeadMesh());
    public static HeadMesh Default => Shared.Value;

    private HeadMesh()
    {
        var random = new Random(4557);
        var nodes = new List<MeshPoint>();

        // 1) Contour, redécoupé pour des segments réguliers.
        for (var i = 0; i < Outline.Length; i++)
        {
            var a = Outline[i];
            var b = Outline[(i + 1) % Outline.Length];
            nodes.Add(a);
            var steps = (int)Math.Floor(a.DistanceTo(b) / 0.08);
            for (var s = 1; s <= steps; s++)
            {
                var f = s / (double)(steps + 1);
                nodes.Add(new MeshPoint(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f));
            }
        }

        // 2) Traits du visage : œil, oreille (petit ovale), pommette, mâchoire.
        nodes.Add(Eye);
        EyeNode = nodes.Count - 1;
        var features = new List<MeshPoint>
        {
            new(0.70, 0.40), new(0.70, 0.46), // paupières
            new(0.68, 0.52), new(0.62, 0.55), // pommette
            new(0.74, 0.60), // coin de la bouche
            new(0.72, 0.70), new(0.62, 0.72), new(0.53, 0.66), // mâchoire
            new(0.50, 0.58), // devant l'oreille
        };
        // Oreille : les points de son contour font aussi partie du maillage.
        features.AddRange(FeatureStrokes[0].Where((_, k) => k % 2 == 0));
        foreach (var f in features)
            if (nodes.All(n => n.DistanceTo(f) > 0.03)) nodes.Add(f);

        // 3) Intérieur : grille décalée et un peu désordonnée.
        const double step = 0.08;
        for (var y = 0.04; y < 1; y += step * 0.87)
        {
            var row = (int)Math.Round(y / (step * 0.87));
            for (var x = 0.12 + (row % 2) * step / 2; x < 0.9; x += step)
            {
                var p = new MeshPoint(x + (random.NextDouble() - 0.5) * step * 0.5, y + (random.NextDouble() - 0.5) * step * 0.5);
                if (!Inside(p) || DistanceToOutline(p) < 0.035) continue;
                if (nodes.Any(n => n.DistanceTo(p) < 0.05)) continue;
                nodes.Add(p);
            }
        }

        // 4) Triangles de Delaunay, gardés seulement à l'intérieur du profil.
        var triangles = new List<(int, int, int)>();
        foreach (var (a, b, c) in Delaunay(nodes))
        {
            var pa = nodes[a];
            var pb = nodes[b];
            var pc = nodes[c];
            var centroid = new MeshPoint((pa.X + pb.X + pc.X) / 3, (pa.Y + pb.Y + pc.Y) / 3);
            if (!Inside(centroid)) continue;
            if (!InsideOrOnEdge(Mid(pa, pb)) || !InsideOrOnEdge(Mid(pb, pc)) || !InsideOrOnEdge(Mid(pa, pc))) continue;
            triangles.Add((a, b, c));
        }

        var edges = new HashSet<(int, int)>();
        foreach (var (a, b, c) in triangles)
        {
            edges.Add(Ordered(a, b));
            edges.Add(Ordered(b, c));
            edges.Add(Ordered(a, c));
        }

        // Nœuds isolés (tous leurs triangles rejetés) : retirés.
        var used = new HashSet<int>(edges.SelectMany(e => new[] { e.Item1, e.Item2 }));
        used.Add(EyeNode);
        var remap = new Dictionary<int, int>();
        var kept = new List<MeshPoint>();
        for (var i = 0; i < nodes.Count; i++)
        {
            if (!used.Contains(i)) continue;
            remap[i] = kept.Count;
            kept.Add(nodes[i]);
        }
        Nodes = kept;
        EyeNode = remap[EyeNode];
        Edges = edges.Select(e => (remap[e.Item1], remap[e.Item2])).OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToList();
        Triangles = triangles.Select(t => (remap[t.Item1], remap[t.Item2], remap[t.Item3])).ToList();
    }

    private static (int, int) Ordered(int a, int b) => a < b ? (a, b) : (b, a);
    private static MeshPoint Mid(MeshPoint a, MeshPoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    /// <summary>Point dans le profil (règle pair-impair).</summary>
    public static bool Inside(MeshPoint p)
    {
        var inside = false;
        for (int i = 0, j = Outline.Length - 1; i < Outline.Length; j = i++)
        {
            var a = Outline[i];
            var b = Outline[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    private static bool InsideOrOnEdge(MeshPoint p) => Inside(p) || DistanceToOutline(p) < 0.004;

    public static double DistanceToOutline(MeshPoint p)
    {
        var best = double.MaxValue;
        for (var i = 0; i < Outline.Length; i++)
        {
            var a = Outline[i];
            var b = Outline[(i + 1) % Outline.Length];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            var f = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
            best = Math.Min(best, p.DistanceTo(new MeshPoint(a.X + dx * f, a.Y + dy * f)));
        }
        return best;
    }

    /// <summary>Triangulation de Delaunay (Bowyer-Watson), suffisante pour une centaine de points.</summary>
    private static List<(int, int, int)> Delaunay(IReadOnlyList<MeshPoint> points)
    {
        var all = new List<MeshPoint>(points) { new(-10, -10), new(10, -10), new(0.5, 10) };
        int s0 = points.Count, s1 = s0 + 1, s2 = s0 + 2;
        var tris = new List<(int A, int B, int C)> { (s0, s1, s2) };
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var bad = tris.Where(t => InCircumcircle(p, all[t.A], all[t.B], all[t.C])).ToList();
            var boundary = new List<(int, int)>();
            foreach (var t in bad)
                foreach (var e in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                {
                    var shared = bad.Any(o => o != t && HasEdge(o, e.Item1, e.Item2));
                    if (!shared) boundary.Add(e);
                }
            tris.RemoveAll(t => bad.Contains(t));
            foreach (var (a, b) in boundary) tris.Add((a, b, i));
        }
        return tris.Where(t => t.A < s0 && t.B < s0 && t.C < s0).Select(t => (t.A, t.B, t.C)).ToList();
    }

    private static bool HasEdge((int A, int B, int C) t, int a, int b)
    {
        var hasA = t.A == a || t.B == a || t.C == a;
        var hasB = t.A == b || t.B == b || t.C == b;
        return hasA && hasB;
    }

    private static bool InCircumcircle(MeshPoint p, MeshPoint a, MeshPoint b, MeshPoint c)
    {
        double ax = a.X - p.X, ay = a.Y - p.Y, bx = b.X - p.X, by = b.Y - p.Y, cx = c.X - p.X, cy = c.Y - p.Y;
        var det = (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
        var orientation = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        return orientation > 0 ? det > 0 : det < 0;
    }
}
