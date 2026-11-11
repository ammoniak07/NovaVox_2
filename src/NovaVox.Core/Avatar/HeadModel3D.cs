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
    // Profil de la tête : hauteur, demi-largeur, profondeur avant, profondeur arrière.
    // Proportions d'une vraie tête (largeur ≈ 2/3 de la hauteur, crâne long vers
    // l'arrière, mâchoire qui se resserre vers le menton, cou placé en arrière).
    private static readonly (double Y, double Rx, double Front, double Back)[] Profile =
    {
        (0.885, 0.20, 0.22, 0.28),
        (0.80, 0.37, 0.44, 0.50),
        (0.68, 0.48, 0.58, 0.63),
        (0.52, 0.545, 0.65, 0.68),
        (0.36, 0.56, 0.68, 0.725),
        (0.22, 0.555, 0.69, 0.74),
        (0.10, 0.545, 0.70, 0.72),
        (-0.02, 0.55, 0.71, 0.66),
        (-0.14, 0.53, 0.72, 0.57),
        (-0.26, 0.50, 0.71, 0.47),
        (-0.38, 0.465, 0.69, 0.36),
        (-0.48, 0.43, 0.66, 0.27),
        (-0.57, 0.33, 0.63, 0.24),
        (-0.65, 0.21, 0.57, 0.24),
        (-0.72, 0.22, 0.34, 0.27),
        (-0.82, 0.235, 0.20, 0.34),
        (-0.97, 0.25, 0.18, 0.37),
        (-1.12, 0.29, 0.19, 0.41),
    };

    private const int PointsPerRing = 40;
    /// <summary>Écart vertical entre deux anneaux du maillage (le profil ci-dessus est interpolé).</summary>
    private const double RingStep = 0.054;

    public static readonly Vec3 Top = new(0, 0.925, -0.05);
    public static readonly Vec3 LeftEye = new(-0.20, 0.10, 0.62);
    public static readonly Vec3 RightEye = new(0.20, 0.10, 0.62);
    public static readonly Vec3 Mouth = new(0, -0.40, 0.70);
    public static readonly Vec3 LeftEar = new(-0.60, 0.03, -0.08);
    public static readonly Vec3 RightEar = new(0.60, 0.03, -0.08);
    public static readonly Vec3 Brain = new(0, 0.50, -0.05);
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
                phi += (random.NextDouble() - 0.5) * 0.05;
                var yy = y + (random.NextDouble() - 0.5) * (r == 0 ? 0.008 : 0.022);
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

    /// <summary>
    /// Arrondi de la coupe horizontale (superellipse) : plus de 2 = plus
    /// carré (visage plat, côtés qui tournent vite), moins de 2 = en pointe
    /// (menton). L'arrière reste une ellipse.
    /// </summary>
    private static double FrontRoundness(double y)
    {
        if (y > 0.45) return 2.15;
        if (y > 0.2) return 2.15 + 0.45 * (0.45 - y) / 0.25;
        if (y > -0.25) return 2.6;
        if (y > -0.66) return 2.6 - 0.9 * (-0.25 - y) / 0.41;
        return 2.0;
    }

    /// <summary>Point de la surface à la hauteur y et à l'angle phi autour de la tête (0 = face).</summary>
    private static Vec3 Surface(double y, double rx, double front, double back, double phi)
    {
        double s = Math.Sin(phi), c = Math.Cos(phi);
        var n = c > 0 ? FrontRoundness(y) : 2.1;
        var x = rx * Math.Sign(s) * Math.Pow(Math.Abs(s), 2 / n);
        var z = (c > 0 ? front : back) * Math.Sign(c) * Math.Pow(Math.Abs(c), 2 / n);
        return Face(new Vec3(x, y, z), phi);
    }

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

    /// <summary>Point de la surface du visage à (x, y), légèrement devant (pour les traits dessinés dessus).</summary>
    public static Vec3 OnFace(double x, double y, double lift = 0.012)
    {
        var (_, rx, front, back) = ProfileAt(y);
        var n = FrontRoundness(y);
        var ratio = Math.Clamp(Math.Abs(x) / rx, 0, 1);
        var phi = Math.Sign(x) * Math.Asin(Math.Pow(ratio, n / 2));
        var p = Surface(y, rx, front, back, phi);
        return new Vec3(p.X, p.Y, p.Z + lift);
    }

    /// <summary>Point de la surface sur le côté de la tête (angle phi), un peu en dehors.</summary>
    private static Vec3 OnSide(double phi, double y, double lift = 0.012)
    {
        var (_, rx, front, back) = ProfileAt(y);
        var p = Surface(y, rx, front, back, phi);
        return new Vec3(p.X + Math.Sign(p.X) * lift, p.Y, p.Z);
    }

    /// <summary>Traits du visage (yeux, sourcils, nez, lèvres, oreilles), dessinés plus lumineux sur le maillage.</summary>
    public IReadOnlyList<Vec3[]> FeatureStrokes { get; } = BuildStrokes();

    private static List<Vec3[]> BuildStrokes()
    {
        var strokes = new List<Vec3[]>();
        Vec3[] Line(params (double X, double Y)[] pts) => pts.Select(p => OnFace(p.X, p.Y)).ToArray();
        foreach (var side in new[] { -1.0, 1.0 })
        {
            // Œil en amande (coin intérieur plus bas, paupière du haut plus bombée).
            var eye = new List<(double, double)>();
            for (var k = 0; k <= 14; k++)
            {
                var a = k / 14.0 * Math.PI * 2;
                var lid = Math.Sin(a) > 0 ? 0.038 : 0.026;
                var cx = Math.Cos(a);
                eye.Add((side * (0.20 + 0.075 * cx), 0.10 + lid * Math.Sin(a) + 0.008 * side * cx * side));
            }
            strokes.Add(Line(eye.ToArray()));
            // Pli de la paupière.
            strokes.Add(Line((side * 0.13, 0.13), (side * 0.18, 0.152), (side * 0.235, 0.152), (side * 0.275, 0.135)));
            // Sourcil.
            strokes.Add(Line((side * 0.085, 0.19), (side * 0.15, 0.215), (side * 0.23, 0.225), (side * 0.31, 0.205)));
            // Aile du nez et narine.
            strokes.Add(Line((side * 0.02, -0.215), (side * 0.06, -0.21), (side * 0.085, -0.19), (side * 0.075, -0.155), (side * 0.045, -0.135)));
            // Pli entre nez et coin de la bouche.
            strokes.Add(Line((side * 0.10, -0.20), (side * 0.15, -0.28), (side * 0.175, -0.37)));
            // Oreille : contour en « C » sur le côté de la tête et son pli intérieur.
            var ear = new List<Vec3>();
            foreach (var (dPhi, y) in new[] { (0.0, 0.17), (0.12, 0.15), (0.2, 0.08), (0.2, -0.02), (0.12, -0.10), (0.0, -0.12), (-0.08, -0.08) })
                ear.Add(OnSide(side * (1.62 + dPhi), y, 0.012));
            strokes.Add(ear.ToArray());
            var fold = new List<Vec3>();
            foreach (var (dPhi, y) in new[] { (0.02, 0.11), (0.1, 0.06), (0.1, -0.02), (0.03, -0.06) })
                fold.Add(OnSide(side * (1.62 + dPhi), y, 0.016));
            strokes.Add(fold.ToArray());
        }
        // Arête du nez (de l'entre-deux yeux à la pointe).
        strokes.Add(Line((0, 0.12), (0, 0.02), (0, -0.08), (0, -0.17), (0, -0.205)));
        // Lèvres : contour du haut (arc de Cupidon), fente, lèvre du bas.
        strokes.Add(Line((-0.15, -0.40), (-0.08, -0.36), (-0.025, -0.353), (0, -0.362), (0.025, -0.353), (0.08, -0.36), (0.15, -0.40)));
        strokes.Add(Line((-0.15, -0.40), (-0.07, -0.403), (0, -0.40), (0.07, -0.403), (0.15, -0.40)));
        strokes.Add(Line((-0.15, -0.40), (-0.08, -0.44), (0, -0.452), (0.08, -0.44), (0.15, -0.40)));
        // Creux sous la lèvre et menton.
        strokes.Add(Line((-0.07, -0.50), (0, -0.495), (0.07, -0.50)));
        return strokes;
    }

    /// <summary>Poids de la mâchoire pour un point du visage (même règle que les sommets du maillage).</summary>
    public static double JawWeightAt(Vec3 n)
    {
        if (n.Y > MouthLine - 0.01 || n.Y < -0.85) return 0.0;
        var below = Math.Clamp((MouthLine - 0.01 - n.Y) / 0.04, 0, 1);
        var front = Math.Clamp((n.Z + 0.05) / 0.35, 0, 1);
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

    /// <summary>Reliefs du visage, des tempes, de la mâchoire et des oreilles, appliqués au point de la forme de base.</summary>
    private static Vec3 Face(Vec3 p, double phi)
    {
        double x = p.X, y = p.Y, z = p.Z;
        double ax = Math.Abs(x), sign = Math.Sign(x == 0 ? Math.Sin(phi) : x), aphi = Math.Abs(phi);
        var dx = 0.0;
        // Oreilles : décollées sur les côtés, un peu en arrière, entre les yeux et le nez en hauteur.
        dx += 0.085 * G((aphi - 1.72) / 0.2) * G((y - 0.03) / 0.12);
        // Tempes un peu creusées, angle de la mâchoire marqué.
        dx -= 0.03 * G((aphi - 1.0) / 0.3) * G((y - 0.25) / 0.12);
        dx += 0.025 * G((aphi - 1.35) / 0.3) * G((y + 0.47) / 0.07);
        var c = Math.Cos(phi);
        if (c <= 0) return new Vec3(x + sign * dx, y, z);

        var front = Math.Pow(c, 1.5);
        var dz = 0.0;
        dz += 0.045 * G((y - 0.20) / 0.05) * G(x / 0.34);                        // arcades
        dz -= 0.075 * G((y - 0.10) / 0.06) * G((ax - 0.20) / 0.085);             // orbites
        dz += 0.03 * G((y - 0.10) / 0.035) * G((ax - 0.20) / 0.06);              // globe de l'œil
        dz += 0.045 * G(x / 0.06) * Math.Clamp((0.12 - y) / 0.29, 0, 1) * (y > -0.21 ? 1 : 0.2); // arête du nez
        dz += 0.08 * G(x / 0.07) * G((y + 0.17) / 0.065);                        // pointe du nez
        dz += 0.035 * G((ax - 0.06) / 0.035) * G((y + 0.195) / 0.035);           // ailes du nez
        dz += 0.035 * G((ax - 0.36) / 0.09) * G((y + 0.02) / 0.08);              // pommettes
        dz -= 0.03 * G((ax - 0.30) / 0.08) * G((y + 0.22) / 0.10);               // joues un peu creuses
        dz += 0.025 * G(x / 0.03) * G((y + 0.30) / 0.04);                        // sillon au-dessus de la lèvre
        dz += 0.04 * G(x / 0.13) * G((y + 0.37) / 0.03);                         // lèvre du haut
        dz -= 0.02 * G(x / 0.13) * G((y - MouthLine) / 0.015);                   // fente de la bouche
        dz += 0.04 * G(x / 0.12) * G((y + 0.44) / 0.03);                         // lèvre du bas
        dz -= 0.03 * G(x / 0.12) * G((y + 0.50) / 0.03);                         // creux sous la lèvre
        dz += 0.05 * G(x / 0.11) * G((y + 0.585) / 0.05);                        // menton
        dx += 0.025 * G((ax - 0.45) / 0.1) * G((y + 0.02) / 0.1) * front;        // pommettes (largeur)
        return new Vec3(x + sign * dx, y, z + dz * front);
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
