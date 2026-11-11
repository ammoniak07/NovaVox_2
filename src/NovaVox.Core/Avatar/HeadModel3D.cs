namespace NovaVox.Core.Avatar;

/// <summary>Point 3D (x vers la droite, y vers le haut, z vers le spectateur).</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double DistanceTo(Vec3 o) => (this - o).Length;
    public static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

/// <summary>Sommet projeté à l'écran : position (unités de l'avatar), profondeur, et s'il est du côté visible.</summary>
public struct ProjectedNode
{
    public double X, Y;
    /// <summary>0 = au fond de la tête, 1 = le plus près du spectateur.</summary>
    public double Depth;
}

/// <summary>
/// Tête de Nova en 3D, maillage « low-poly » holographique vue de face :
/// anneaux horizontaux (comme un objet tourné) déformés pour former crâne,
/// arcades, orbites, nez, pommettes, lèvres, menton et cou, puis reliés en
/// triangles. Construite une fois (graine fixe) ; Project() la fait
/// pivoter (gauche/droite, haut/bas, inclinaison), ouvre la mâchoire et
/// la projette en perspective pour l'affichage (OrbWindow).
/// </summary>
public sealed class HeadModel3D
{
    // Profil de révolution : hauteur, demi-largeur, profondeur avant, profondeur arrière.
    private static readonly (double Y, double Rx, double Front, double Back)[] Profile =
    {
        (0.85, 0.32, 0.32, 0.36),
        (0.72, 0.50, 0.50, 0.57),
        (0.56, 0.61, 0.61, 0.69),
        (0.38, 0.66, 0.66, 0.74),
        (0.22, 0.66, 0.70, 0.73),
        (0.08, 0.65, 0.72, 0.71),
        (-0.06, 0.62, 0.73, 0.67),
        (-0.20, 0.58, 0.72, 0.60),
        (-0.33, 0.53, 0.70, 0.52),
        (-0.46, 0.46, 0.67, 0.43),
        (-0.58, 0.36, 0.60, 0.35),
        (-0.68, 0.27, 0.46, 0.30),
        (-0.80, 0.25, 0.27, 0.30),
        (-0.95, 0.27, 0.27, 0.31),
        (-1.10, 0.31, 0.29, 0.33),
    };

    private const int PointsPerRing = 26;
    /// <summary>Écart vertical entre deux anneaux du maillage (le profil ci-dessus est interpolé).</summary>
    private const double RingStep = 0.08;

    public static readonly Vec3 Top = new(0, 0.93, -0.02);
    public static readonly Vec3 LeftEye = new(-0.24, 0.10, 0.60);
    public static readonly Vec3 RightEye = new(0.24, 0.10, 0.60);
    public static readonly Vec3 Mouth = new(0, -0.40, 0.70);
    public static readonly Vec3 LeftEar = new(-0.66, 0.02, 0.0);
    public static readonly Vec3 RightEar = new(0.66, 0.02, 0.0);
    public static readonly Vec3 Brain = new(0, 0.55, 0.0);
    /// <summary>Ligne des lèvres : sous cette hauteur, le bas du visage descend quand la mâchoire s'ouvre.</summary>
    public const double MouthLine = -0.40;

    public IReadOnlyList<Vec3> Nodes { get; }
    public IReadOnlyList<(int A, int B)> Edges { get; }
    public IReadOnlyList<(int A, int B, int C)> Triangles { get; }
    /// <summary>Pour chaque arête, les (un ou deux) triangles qui la bordent.</summary>
    public IReadOnlyList<int[]> EdgeTriangles { get; }
    /// <summary>Poids d'ouverture de la mâchoire de chaque sommet (0 = immobile, 1 = suit le menton).</summary>
    public IReadOnlyList<double> JawWeight { get; }

    private static readonly Lazy<HeadModel3D> Shared = new(() => new HeadModel3D());
    public static HeadModel3D Default => Shared.Value;

    private HeadModel3D()
    {
        var random = new Random(4557);
        var nodes = new List<Vec3> { Top };
        var rings = new List<int[]>();

        var levels = new List<double>();
        for (var level = Profile[0].Y; level >= Profile[^1].Y - 1e-9; level -= RingStep) levels.Add(level);
        for (var r = 0; r < levels.Count; r++)
        {
            var (y, rx, front, back) = ProfileAt(levels[r]);
            var ring = new int[PointsPerRing];
            for (var i = 0; i < PointsPerRing; i++)
            {
                // Plus de points sur le visage que derrière la tête ; anneaux décalés d'un demi-pas.
                var u = ((i + (r % 2) * 0.5) / PointsPerRing) * 2 - 1; // -1..1, 0 = face
                var phi = Math.Sign(u) * Math.PI * Math.Pow(Math.Abs(u), 1.25);
                phi += (random.NextDouble() - 0.5) * 0.09;
                var yy = y + (random.NextDouble() - 0.5) * (r == 0 ? 0.01 : 0.04);
                nodes.Add(Surface(yy, rx, front, back, phi));
                ring[i] = nodes.Count - 1;
            }
            rings.Add(ring);
        }

        var triangles = new List<(int, int, int)>();
        // Calotte : éventail autour du sommet.
        for (var i = 0; i < PointsPerRing; i++)
            triangles.Add((0, rings[0][i], rings[0][(i + 1) % PointsPerRing]));
        // Bandes entre anneaux voisins (décalés d'un demi-pas : triangles en zigzag).
        for (var r = 0; r + 1 < rings.Count; r++)
        {
            var a = rings[r];
            var b = rings[r + 1];
            var shifted = r % 2 == 0; // l'anneau r+1 est décalé vers +
            for (var i = 0; i < PointsPerRing; i++)
            {
                var i1 = (i + 1) % PointsPerRing;
                if (shifted)
                {
                    triangles.Add((a[i], b[i], a[i1]));
                    triangles.Add((a[i1], b[i], b[i1]));
                }
                else
                {
                    triangles.Add((a[i], b[(i + PointsPerRing - 1) % PointsPerRing], b[i]));
                    triangles.Add((a[i], b[i], a[i1]));
                }
            }
        }

        var edgeTris = new Dictionary<(int, int), List<int>>();
        for (var t = 0; t < triangles.Count; t++)
        {
            var (a, b, c) = triangles[t];
            foreach (var e in new[] { Ordered(a, b), Ordered(b, c), Ordered(a, c) })
            {
                if (!edgeTris.TryGetValue(e, out var list)) edgeTris[e] = list = new List<int>();
                list.Add(t);
            }
        }

        Nodes = nodes;
        Triangles = triangles;
        Edges = edgeTris.Keys.OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToList();
        EdgeTriangles = Edges.Select(e => edgeTris[e].ToArray()).ToList();
        JawWeight = nodes.Select(JawWeightAt).ToList();
    }

    private static (int, int) Ordered(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>Point de la surface à la hauteur y et à l'angle phi autour de la tête (0 = face).</summary>
    private static Vec3 Surface(double y, double rx, double front, double back, double phi)
    {
        var x = rx * Math.Sin(phi);
        var c = Math.Cos(phi);
        var z = (c > 0 ? front : back) * c;
        // Visage un peu aplati (moins rond qu'une ellipse), côtés plus droits.
        if (c > 0) z *= 1 - 0.12 * Math.Pow(Math.Sin(phi), 2);
        return Face(new Vec3(x, y, z), c);
    }

    /// <summary>Point de la surface du visage à (x, y), légèrement devant (pour les traits dessinés dessus).</summary>
    /// <summary>Largeur et profondeurs de la tête à la hauteur y (interpolation douce du profil).</summary>
    private static (double Y, double Rx, double Front, double Back) ProfileAt(double y)
    {
        var i = 0;
        while (i + 2 < Profile.Length && Profile[i + 1].Y > y) i++;
        var (y0, rx0, f0, b0) = Profile[i];
        var (y1, rx1, f1, b1) = Profile[i + 1];
        var k = Math.Clamp((y0 - y) / (y0 - y1), 0, 1);
        k = k * k * (3 - 2 * k) * 0.5 + k * 0.5; // à mi-chemin entre linéaire et lissé : pas de cassure aux anneaux du profil
        return (y, rx0 + (rx1 - rx0) * k, f0 + (f1 - f0) * k, b0 + (b1 - b0) * k);
    }

    public static Vec3 OnFace(double x, double y, double lift = 0.012)
    {
        var (_, rx, front, back) = ProfileAt(y);
        var phi = Math.Asin(Math.Clamp(x / rx, -1, 1));
        var p = Surface(y, rx, front, back, phi);
        return new Vec3(p.X, p.Y, p.Z + lift);
    }

    /// <summary>Traits du visage (yeux, sourcils, nez, lèvres), dessinés plus lumineux sur le maillage.</summary>
    public IReadOnlyList<Vec3[]> FeatureStrokes { get; } = BuildStrokes();

    private static List<Vec3[]> BuildStrokes()
    {
        var strokes = new List<Vec3[]>();
        Vec3[] Line(params (double X, double Y)[] pts) => pts.Select(p => OnFace(p.X, p.Y)).ToArray();
        foreach (var side in new[] { -1.0, 1.0 })
        {
            // Œil en amande.
            var eye = new List<(double, double)>();
            for (var k = 0; k <= 12; k++)
            {
                var a = k / 12.0 * Math.PI * 2;
                var lid = Math.Sin(a) > 0 ? 0.045 : 0.035;
                eye.Add((side * (0.24 + 0.095 * Math.Cos(a)), 0.10 + lid * Math.Sin(a) * (1 - 0.25 * side * Math.Cos(a))));
            }
            strokes.Add(Line(eye.ToArray()));
            // Sourcil.
            strokes.Add(Line((side * 0.12, 0.215), (side * 0.22, 0.245), (side * 0.32, 0.24), (side * 0.39, 0.205)));
            // Aile du nez.
            strokes.Add(Line((side * 0.03, -0.22), (side * 0.085, -0.205), (side * 0.10, -0.17), (side * 0.07, -0.13)));
        }
        // Arête du nez.
        strokes.Add(Line((0, 0.12), (0, 0.0), (0, -0.12), (0, -0.19)));
        // Lèvres : contour du haut (arc de Cupidon), fente, lèvre du bas.
        strokes.Add(Line((-0.17, -0.40), (-0.09, -0.355), (-0.03, -0.35), (0, -0.36), (0.03, -0.35), (0.09, -0.355), (0.17, -0.40)));
        strokes.Add(Line((-0.17, -0.40), (-0.08, -0.405), (0, -0.40), (0.08, -0.405), (0.17, -0.40)));
        strokes.Add(Line((-0.17, -0.40), (-0.09, -0.445), (0, -0.46), (0.09, -0.445), (0.17, -0.40)));
        return strokes;
    }

    /// <summary>Poids de la mâchoire pour un point du visage (même règle que les sommets du maillage).</summary>
    public static double JawWeightAt(Vec3 n)
    {
        if (n.Y > MouthLine - 0.01 || n.Y < -0.85) return 0.0;
        var below = Math.Clamp((MouthLine - 0.01 - n.Y) / 0.04, 0, 1);
        var front = Math.Clamp(n.Z / 0.35, 0, 1);
        var fadeNeck = Math.Clamp((n.Y + 0.85) / 0.15, 0, 1);
        return below * front * fadeNeck;
    }

    /// <summary>Même rotation et perspective que Project(), pour un point isolé (traits du visage, yeux).</summary>
    public static (double X, double Y, double Z) ProjectPoint(Vec3 n, double jawWeight, double yaw, double pitch, double roll, double jawOpen)
    {
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch), cr = Math.Cos(roll), sr = Math.Sin(roll);
        var y0 = n.Y - jawWeight * jawOpen * 0.10 + 0.3;
        var z0 = n.Z - jawWeight * jawOpen * 0.03;
        var x1 = n.X * cy + z0 * sy;
        var z1 = -n.X * sy + z0 * cy;
        var y2 = y0 * cp - z1 * sp;
        var z2 = y0 * sp + z1 * cp;
        var x3 = x1 * cr - y2 * sr;
        var y3 = x1 * sr + y2 * cr - 0.3;
        var f = Camera / (Camera - z2);
        return (x3 * f, -y3 * f, z2);
    }

    private const double Camera = 4.0;

    /// <summary>
    /// Visibilité d'un point de la surface (0 = caché derrière la tête,
    /// 1 = bien de face) : composante vers le spectateur de sa direction
    /// depuis l'axe de la tête, après rotation.
    /// </summary>
    public static double SurfaceVisibility(Vec3 n, double yaw, double pitch)
    {
        var dir = new Vec3(n.X, 0, n.Z);
        var length = dir.Length;
        if (length < 1e-6) return 1;
        var z = (-n.X * Math.Sin(yaw) + n.Z * Math.Cos(yaw)) / length;
        z = z * Math.Cos(pitch);
        return Math.Clamp((z - 0.05) / 0.35, 0, 1);
    }

    private static double G(double v) => Math.Exp(-v * v);

    /// <summary>Reliefs du visage, appliqués au point de la forme de base (c = cos de l'angle autour de la tête, 1 = face).</summary>
    private static Vec3 Face(Vec3 p, double c)
    {
        if (c <= 0) return p;
        var front = Math.Pow(c, 2);
        double x = p.X, y = p.Y, z = p.Z;
        double ax = Math.Abs(x);
        var dz = 0.0;
        dz += 0.05 * G((y - 0.22) / 0.06) * G(x / 0.38);                       // arcades
        dz -= 0.08 * G((y - 0.10) / 0.07) * G((ax - 0.24) / 0.10);             // orbites
        dz += 0.11 * G(x / 0.075) * G((y + 0.15) / 0.09);                      // nez
        dz += 0.07 * G(x / 0.07) * Math.Clamp((0.10 - y) / 0.24, 0, 1) * (y > -0.24 ? 1 : 0); // arête du nez
        dz += 0.04 * G((ax - 0.40) / 0.10) * G((y + 0.08) / 0.10);             // pommettes
        dz += 0.035 * G(x / 0.16) * G((y + 0.36) / 0.035);                     // lèvre du haut
        dz -= 0.02 * G(x / 0.14) * G((y - MouthLine) / 0.02);                  // fente de la bouche
        dz += 0.035 * G(x / 0.15) * G((y + 0.45) / 0.04);                      // lèvre du bas
        dz += 0.05 * G(x / 0.14) * G((y + 0.60) / 0.06);                       // menton
        var dx = 0.03 * Math.Sign(x) * G((ax - 0.48) / 0.12) * G((y + 0.05) / 0.12); // pommettes (largeur)
        return new Vec3(x + dx * front, y, z + dz * front);
    }

    /// <summary>
    /// Fait pivoter la tête (yaw : tourne vers la droite de l'écran si
    /// positif ; pitch : lève la tête si positif ; roll : penche), ouvre la
    /// mâchoire (0..1) et projette en perspective. Résultat en unités de
    /// l'avatar (la tête fait environ 2 unités de haut, centrée sur 0, y vers le bas).
    /// </summary>
    public void Project(double yaw, double pitch, double roll, double jawOpen, ProjectedNode[] output, Vec3[]? world = null)
    {
        double minZ = double.MaxValue, maxZ = double.MinValue;
        for (var i = 0; i < Nodes.Count; i++)
        {
            var (x, y, z) = ProjectPoint(Nodes[i], JawWeight[i], yaw, pitch, roll, jawOpen);
            if (world is not null) world[i] = new Vec3(x, -y, z);
            output[i].X = x;
            output[i].Y = y;
            output[i].Depth = z;
            minZ = Math.Min(minZ, z);
            maxZ = Math.Max(maxZ, z);
        }
        var range = Math.Max(1e-6, maxZ - minZ);
        for (var i = 0; i < Nodes.Count; i++) output[i].Depth = (output[i].Depth - minZ) / range;
    }

    private static readonly Vec3 Light = new Vec3(-0.45, 0.55, 0.70) * (1 / new Vec3(-0.45, 0.55, 0.70).Length);

    /// <summary>
    /// Éclat d'un triangle tourné vers le spectateur (0..1) : bords de la
    /// silhouette lumineux (effet hologramme) plus un éclairage venant d'en
    /// haut à gauche, qui fait ressortir le relief du visage. view : sortie
    /// « world » de Project().
    /// </summary>
    public double Brightness(int triangle, Vec3[] view)
    {
        var (a, b, c) = Triangles[triangle];
        var n = Vec3.Cross(view[b] - view[a], view[c] - view[a]);
        var length = n.Length;
        if (length < 1e-9) return 0;
        n *= 1 / length;
        if (n.Z < 0) n *= -1;
        var fresnel = Math.Pow(1 - n.Z, 1.3);
        var lambert = Math.Max(0, Vec3.Dot(n, Light));
        return Math.Clamp(0.15 + 0.5 * fresnel + 0.45 * lambert * lambert, 0, 1);
    }

    /// <summary>Vrai si le triangle est tourné vers le spectateur (d'après les positions projetées).</summary>
    public bool FacesViewer(int triangle, ProjectedNode[] projected)
    {
        var (a, b, c) = Triangles[triangle];
        var pa = projected[a];
        var pb = projected[b];
        var pc = projected[c];
        var cross = (pb.X - pa.X) * (pc.Y - pa.Y) - (pb.Y - pa.Y) * (pc.X - pa.X);
        return cross < 0;
    }
}
