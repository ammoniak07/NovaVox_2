using System.Globalization;

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
    public Vec3 Normalized() => Length < 1e-12 ? this : this * (1 / Length);
}

/// <summary>Visage de la tête holographique.</summary>
public enum HeadSpecies
{
    Male,
    Female,
    /// <summary>Extraterrestre inspiré des Banu de Star Citizen : crâne en crête, visage ridé, pas d'oreilles.</summary>
    Banu,
}

/// <summary>Sommet projeté à l'écran : position (unités de l'avatar), profondeur.</summary>
public struct ProjectedNode
{
    public double X, Y;
    /// <summary>0 = au fond de la tête, 1 = le plus près du spectateur.</summary>
    public double Depth;
}

/// <summary>
/// Tête de Nova en 3D, en maillage holographique. Le visage est un vrai
/// visage humain mesuré : le modèle de visage de référence de MediaPipe
/// (Google, licence Apache 2.0, fichier canonical_face_model.obj, 468
/// points), mis à l'échelle et greffé sur un crâne, des oreilles et un cou
/// construits ici ; la jonction est lissée. Construite une fois ; Project()
/// la fait pivoter (gauche/droite, haut/bas, inclinaison), ouvre la
/// mâchoire et la projette en perspective pour l'affichage (OrbWindow).
/// </summary>
public sealed class HeadModel3D
{
    // Profil du crâne et du cou : hauteur, demi-largeur, profondeur avant, profondeur arrière.
    // (L'avant du visage vient du modèle MediaPipe ; ce profil sert au front, aux côtés, à
    // l'arrière du crâne, au dessous de la mâchoire et au cou.)
    private static readonly (double Y, double Rx, double Front, double Back)[] Profile =
    {
        (0.90, 0.20, 0.22, 0.28),
        (0.82, 0.37, 0.42, 0.50),
        (0.70, 0.48, 0.55, 0.63),
        (0.54, 0.545, 0.62, 0.69),
        (0.38, 0.565, 0.65, 0.73),
        (0.22, 0.57, 0.66, 0.745),
        (0.08, 0.565, 0.66, 0.73),
        (-0.06, 0.56, 0.66, 0.67),
        (-0.20, 0.545, 0.65, 0.58),
        (-0.34, 0.52, 0.63, 0.47),
        (-0.48, 0.48, 0.60, 0.33),
        (-0.60, 0.42, 0.50, 0.26),
        (-0.72, 0.31, 0.38, 0.25),
        (-0.82, 0.25, 0.25, 0.29),
        (-0.92, 0.235, 0.20, 0.33),
        (-1.06, 0.26, 0.19, 0.37),
        (-1.20, 0.33, 0.20, 0.42),
    };

    /// <summary>Écart visé entre deux points du crâne (proche de celui du visage).</summary>
    private const double SkullSpacing = 0.06;
    /// <summary>Échelle des angles autour de la tête dans le plan de triangulation (≈ rayon de la tête).</summary>
    private const double AngleScale = 0.6;

    /// <summary>Ligne des lèvres : sous cette hauteur, le bas du visage descend quand la mâchoire s'ouvre.</summary>
    public const double MouthLine = -0.40;

    public static Vec3 Top => Default.TopPoint;
    public static Vec3 LeftEye => Default.EyeLeft;
    public static Vec3 RightEye => Default.EyeRight;
    public static Vec3 Mouth => Default.MouthPoint;
    public static Vec3 LeftEar => Default.EarLeft;
    public static Vec3 RightEar => Default.EarRight;
    public static readonly Vec3 Brain = new(0, 0.50, -0.05);

    public IReadOnlyList<Vec3> Nodes { get; }
    public IReadOnlyList<(int A, int B)> Edges { get; }
    public IReadOnlyList<(int A, int B, int C)> Triangles { get; }
    /// <summary>Pour chaque arête, les (un ou deux) triangles qui la bordent.</summary>
    public IReadOnlyList<int[]> EdgeTriangles { get; }
    /// <summary>Poids d'ouverture de la mâchoire de chaque sommet (0 = immobile, 1 = suit le menton).</summary>
    public IReadOnlyList<double> JawWeight { get; }
    /// <summary>Normale de chaque sommet (vers l'extérieur).</summary>
    public IReadOnlyList<Vec3> NodeNormals { get; }
    /// <summary>Ombre de chaque sommet (0 = normal, 1 = au fond d'un creux : fentes de la crête des Banu).</summary>
    public IReadOnlyList<double> NodeShade { get; private set; } = Array.Empty<double>();
    /// <summary>Traits du visage (yeux, sourcils, lèvres, oreilles), dessinés plus lumineux sur le maillage.</summary>
    public IReadOnlyList<Vec3[]> FeatureStrokes { get; }
    /// <summary>Nombre de sommets qui viennent du visage MediaPipe (les premiers de Nodes).</summary>
    public int FaceNodeCount { get; }
    /// <summary>Pour chaque triangle : vrai s'il fait partie du visage (option « visage seul »).</summary>
    public IReadOnlyList<bool> FaceTriangle { get; }
    /// <summary>Pour chaque sommet : vrai s'il est affiché en « visage seul » (le visage, plus le haut du crâne et la crête pour les Banu).</summary>
    public IReadOnlyList<bool> InFaceOnly { get; private set; } = Array.Empty<bool>();
    /// <summary>Nombre de traits du visage (les premiers de FeatureStrokes ; les suivants sont les oreilles).</summary>
    public int FaceStrokeCount { get; }

    /// <summary>Visage de femme (sinon d'homme).</summary>
    public bool Female => Species == HeadSpecies.Female;
    public HeadSpecies Species { get; }
    public Vec3 TopPoint { get; }
    public Vec3 EyeLeft { get; }
    public Vec3 EyeRight { get; }
    public Vec3 MouthPoint { get; }
    public Vec3 EarLeft { get; private set; }
    public Vec3 EarRight { get; private set; }
    private readonly List<(Vec3[] Line, double Weight)> _glowLines = new();

    private static readonly Lazy<HeadModel3D> SharedMale = new(() => new HeadModel3D(HeadSpecies.Male));
    private static readonly Lazy<HeadModel3D> SharedFemale = new(() => new HeadModel3D(HeadSpecies.Female));
    private static readonly Lazy<HeadModel3D> SharedBanu = new(() => new HeadModel3D(HeadSpecies.Banu));
    /// <summary>Tête d'homme (modèle par défaut).</summary>
    public static HeadModel3D Default => SharedMale.Value;
    /// <summary>Tête d'homme ou de femme (chacune calculée une fois).</summary>
    public static HeadModel3D For(bool female) => female ? SharedFemale.Value : SharedMale.Value;
    /// <summary>Tête du visage demandé (chacune calculée une fois).</summary>
    public static HeadModel3D For(HeadSpecies species) => species switch
    {
        HeadSpecies.Female => SharedFemale.Value,
        HeadSpecies.Banu => SharedBanu.Value,
        _ => SharedMale.Value,
    };

    // Paupières du haut (pour les cils du visage de femme).
    private static readonly int[] RightUpperLid = { 33, 246, 161, 160, 159, 158, 157, 173, 133 };
    private static readonly int[] LeftUpperLid = { 263, 466, 388, 387, 386, 385, 384, 398, 362 };

    // Repères du modèle MediaPipe (numéros de ses 468 points).
    private static readonly int[] RightEyeLoop = { 33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246, 33 };
    private static readonly int[] LeftEyeLoop = { 263, 249, 390, 373, 374, 380, 381, 382, 362, 398, 384, 385, 386, 387, 388, 466, 263 };
    private static readonly int[] RightBrow = { 46, 53, 52, 65, 55 };
    private static readonly int[] RightBrowTop = { 70, 63, 105, 66, 107 };
    private static readonly int[] LeftBrow = { 276, 283, 282, 295, 285 };
    private static readonly int[] LeftBrowTop = { 300, 293, 334, 296, 336 };
    private static readonly int[] LipsOuter = { 61, 146, 91, 181, 84, 17, 314, 405, 321, 375, 291, 409, 270, 269, 267, 0, 37, 39, 40, 185, 61 };
    private static readonly int[] LipsInner = { 78, 95, 88, 178, 87, 14, 317, 402, 318, 324, 308, 415, 310, 311, 312, 13, 82, 81, 80, 191, 78 };
    private static readonly int[] NoseBridge = { 168, 6, 197, 195, 5, 4, 1 };
    private static readonly int[] NoseBase = { 98, 97, 2, 326, 327 };

    private HeadModel3D(HeadSpecies species)
    {
        Species = species;
        var female = species == HeadSpecies.Female;
        var random = new Random(4557);

        // 1) Visage MediaPipe, mis à l'échelle : milieu des yeux et milieu de la bouche
        //    placés aux hauteurs de la tête (0,10 et MouthLine).
        var (faceRaw, faceTriangles) = LoadFace();
        var eyeY = new[] { 33, 133, 263, 362 }.Average(i => faceRaw[i].Y);
        var mouthY = (faceRaw[13].Y + faceRaw[14].Y) / 2;
        var scale = (0.10 - MouthLine) / (eyeY - mouthY);
        var face = faceRaw.Select(p => new Vec3(p.X * scale, (p.Y - mouthY) * scale + MouthLine, p.Z * scale)).ToList();

        var boundary = BoundaryLoop(faceTriangles);
        // Profondeur : le contour du visage doit tomber sur la surface du crâne.
        var offsets = boundary.Select(i => BaseFrontZ(face[i].X, face[i].Y) - face[i].Z).OrderBy(v => v).ToList();
        var zOffset = offsets[offsets.Count / 2];
        face = face.Select(p => new Vec3(p.X, p.Y, p.Z + zOffset)).ToList();

        var nodes = new List<Vec3>(face);
        var triangles = new List<(int, int, int)>(faceTriangles);
        FaceNodeCount = face.Count;

        // 2) Crâne et cou : points réguliers sur la surface, hors de l'empreinte du visage,
        //    triangulés avec le contour du visage dans le plan (angle autour de la tête, hauteur).
        var footprint = boundary.Select(i => ToPlane(face[i])).ToList();
        var planePoints = new List<(double U, double V, int Node)>();
        foreach (var i in boundary)
        {
            var (u, v) = ToPlane(face[i]);
            planePoints.Add((u, v, i));
        }

        var skullStart = nodes.Count;
        var row = 0;
        for (var y = Profile[0].Y; y >= Profile[^1].Y - 1e-9; y -= SkullSpacing * 0.87, row++)
        {
            var (_, rx, front, back) = ProfileAt(y);
            var perimeter = Math.PI * (1.5 * (rx + (front + back) / 2) - Math.Sqrt(rx * (front + back) / 2));
            var count = Math.Max(8, (int)Math.Round(perimeter / SkullSpacing));
            for (var i = 0; i < count; i++)
            {
                var phi = -Math.PI + (i + (row % 2) * 0.5 + (random.NextDouble() - 0.5) * 0.3) / count * 2 * Math.PI;
                var yy = row == 0 ? y : y + (random.NextDouble() - 0.5) * SkullSpacing * 0.3;
                var u = phi * AngleScale;
                if (InsidePolygon(footprint, u, yy) || DistanceToPolygon(footprint, u, yy) < SkullSpacing * 0.55) continue;
                var (_, rx2, f2, b2) = ProfileAt(yy);
                nodes.Add(Surface(yy, rx2, f2, b2, phi));
                planePoints.Add((u, yy, nodes.Count - 1));
            }
        }
        var top = new Vec3(0, Profile[0].Y + 0.035, -0.04);
        nodes.Add(top);
        var topIndex = nodes.Count - 1;

        // Lissage de la jonction : les points du crâne proches du visage suivent l'écart
        // entre le contour du visage et la surface du crâne.
        var seams = boundary.Select(i =>
        {
            var (u, v) = ToPlane(face[i]);
            var phi = u / AngleScale;
            var (_, rx, front, back) = ProfileAt(v);
            return (U: u, V: v, Delta: face[i] - Surface(v, rx, front, back, phi));
        }).ToList();
        for (var n = skullStart; n < topIndex; n++)
        {
            var (u, v) = ToPlane(nodes[n]);
            double sw = 0, dmin = double.MaxValue;
            var delta = new Vec3(0, 0, 0);
            foreach (var s in seams)
            {
                var du = WrapAngle(u - s.U);
                var d2 = du * du + (v - s.V) * (v - s.V);
                dmin = Math.Min(dmin, Math.Sqrt(d2));
                var w = 1 / (d2 + 1e-4);
                sw += w;
                delta += s.Delta * w;
            }
            var falloff = Math.Exp(-Math.Pow(dmin / 0.14, 2));
            nodes[n] += delta * (falloff / sw);
        }

        // Triangulation (avec copies de part et d'autre de la couture arrière, et du sommet au-dessus).
        var seamLimit = Math.PI * AngleScale;
        var all = new List<(double U, double V, int Node)>(planePoints);
        foreach (var p in planePoints)
        {
            if (p.U > seamLimit - 0.4) all.Add((p.U - 2 * seamLimit, p.V, p.Node));
            if (p.U < -seamLimit + 0.4) all.Add((p.U + 2 * seamLimit, p.V, p.Node));
        }
        var topV = Profile[0].Y + 0.05;
        for (var u = -seamLimit - 0.4; u <= seamLimit + 0.4; u += SkullSpacing) all.Add((u, topV, topIndex));
        var seen = new HashSet<(int, int, int)>();
        foreach (var (a, b, c) in Delaunay(all.Select(p => (p.U, p.V)).ToList()))
        {
            var cu = (all[a].U + all[b].U + all[c].U) / 3;
            var cv = (all[a].V + all[b].V + all[c].V) / 3;
            if (cu < -seamLimit || cu >= seamLimit) continue;
            if (InsidePolygon(footprint, cu, cv)) continue;
            int na = all[a].Node, nb = all[b].Node, nc = all[c].Node;
            if (na == nb || nb == nc || na == nc) continue;
            if (na < FaceNodeCount && nb < FaceNodeCount && nc < FaceNodeCount) continue; // déjà couvert par le visage
            if (seen.Add(Sorted(na, nb, nc))) triangles.Add((na, nb, nc));
        }

        // 3) Oreilles : petits pavillons en relief collés sur les côtés (les Banu n'en ont pas).
        var earRims = new List<Vec3[]>();
        EarLeft = new Vec3(-0.56, 0.03, -0.08);
        EarRight = new Vec3(0.56, 0.03, -0.08);
        foreach (var side in species == HeadSpecies.Banu ? Array.Empty<double>() : new[] { -1.0, 1.0 })
        {
            var (earNodes, earTriangles, rim, center) = BuildEar(side, nodes.Count);
            nodes.AddRange(earNodes);
            triangles.AddRange(earTriangles);
            earRims.Add(rim);
            if (side < 0) EarLeft = center;
            else EarRight = center;
        }

        // 3 bis) Homme ou femme : le visage et le cou sont remodelés (voir Morph).
        var eyeCenters = new[] { Average(LeftEyeLoop.Select(i => nodes[i])), Average(RightEyeLoop.Select(i => nodes[i])) };
        var mouthCenter = (nodes[13] + nodes[14]) * 0.5;
        var noseTip = nodes[1];
        Vec3 Shape(Vec3 p) => species == HeadSpecies.Banu
            ? MorphBanu(p, eyeCenters, mouthCenter, noseTip)
            : Morph(p, female, eyeCenters, mouthCenter, noseTip);
        var shades = new double[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            if (species == HeadSpecies.Banu) nodes[i] = MorphBanu(nodes[i], eyeCenters, mouthCenter, noseTip, out shades[i]);
            else nodes[i] = Shape(nodes[i]);
        }
        EarLeft = Shape(EarLeft);
        EarRight = Shape(EarRight);
        earRims = earRims.Select(rim => rim.Select(Shape).ToArray()).ToList();
        TopPoint = nodes[topIndex];

        // 4) Orientation : chaque triangle tourné vers l'extérieur (normale = (b-a)×(c-a)).
        var oriented = new List<(int, int, int)>(triangles.Count);
        foreach (var (a, b, c) in triangles)
        {
            var n = Vec3.Cross(nodes[b] - nodes[a], nodes[c] - nodes[a]);
            var centroid = (nodes[a] + nodes[b] + nodes[c]) * (1.0 / 3);
            oriented.Add(Vec3.Dot(n, Outward(centroid)) >= 0 ? (a, b, c) : (a, c, b));
        }
        // Le visage a un sens de rotation cohérent : on l'impose d'après la majorité (le nez,
        // les narines et les lèvres pourraient tromper le test ci-dessus).
        var faceVotes = 0;
        foreach (var (a, b, c) in faceTriangles)
        {
            var n = Vec3.Cross(nodes[b] - nodes[a], nodes[c] - nodes[a]);
            faceVotes += Vec3.Dot(n, Outward((nodes[a] + nodes[b] + nodes[c]) * (1.0 / 3))) >= 0 ? 1 : -1;
        }
        for (var t = 0; t < faceTriangles.Count; t++)
        {
            var (a, b, c) = faceTriangles[t];
            oriented[t] = faceVotes >= 0 ? (a, b, c) : (a, c, b);
        }

        var edgeTris = new Dictionary<(int, int), List<int>>();
        for (var t = 0; t < oriented.Count; t++)
        {
            var (a, b, c) = oriented[t];
            foreach (var e in new[] { Ordered(a, b), Ordered(b, c), Ordered(a, c) })
            {
                if (!edgeTris.TryGetValue(e, out var list)) edgeTris[e] = list = new List<int>();
                list.Add(t);
            }
        }

        var normals = new Vec3[nodes.Count];
        foreach (var (a, b, c) in oriented)
        {
            var n = Vec3.Cross(nodes[b] - nodes[a], nodes[c] - nodes[a]);
            normals[a] += n;
            normals[b] += n;
            normals[c] += n;
        }
        for (var i = 0; i < normals.Length; i++)
            normals[i] = normals[i].Length < 1e-12 ? Outward(nodes[i]).Normalized() : normals[i].Normalized();

        Nodes = nodes;
        Triangles = oriented;
        Edges = edgeTris.Keys.OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToList();
        EdgeTriangles = Edges.Select(e => edgeTris[e].ToArray()).ToList();
        NodeNormals = normals;
        NodeShade = shades;
        // Visage seul : le masque du visage ; pour les Banu, aussi le dessus de la tête (la crête fait partie du visage).
        var inFace = nodes.Select((n, i) => i < FaceNodeCount || (species == HeadSpecies.Banu && n.Y > 0.28 && n.Z > -0.2)).ToArray();
        InFaceOnly = inFace;
        FaceTriangle = oriented.Select(t => inFace[t.Item1] && inFace[t.Item2] && inFace[t.Item3]).ToList();

        // 5) Repères et traits du visage (légèrement devant la peau).
        Vec3 Lift(int i) => nodes[i] + normals[i] * 0.008;
        Vec3[] Line(int[] ids) => ids.Select(Lift).ToArray();
        EyeLeft = Average(LeftEyeLoop.Select(i => nodes[i])) + new Vec3(0, 0, 0.01);
        EyeRight = Average(RightEyeLoop.Select(i => nodes[i])) + new Vec3(0, 0, 0.01);
        MouthPoint = (nodes[13] + nodes[14]) * 0.5;
        // Lèvres : calées sur la vraie bouche de ce visage (déplacée pour les visages remodelés).
        JawWeight = nodes.Select(JawWeightOf).ToList();
        var featureStrokes = species == HeadSpecies.Banu
            ? new List<Vec3[]> { Line(RightEyeLoop), Line(LeftEyeLoop), Line(LipsInner) } // ni sourcils ni contour de lèvres
            : new List<Vec3[]> { Line(RightEyeLoop), Line(LeftEyeLoop), Line(RightBrow), Line(LeftBrow), Line(LipsOuter), Line(LipsInner) };
        if (female)
        {
            // Cils : trait au-dessus de la paupière du haut, prolongé en petite virgule au coin extérieur.
            foreach (var (lid, side) in new[] { (RightUpperLid, -1.0), (LeftUpperLid, 1.0) })
            {
                var lash = lid.Select(i => Lift(i) + new Vec3(0, 0.008, 0.006)).ToList();
                var corner = lash[0];
                lash.Insert(0, corner + new Vec3(side * 0.03, 0.018, -0.01));
                featureStrokes.Add(lash.ToArray());
                _glowLines.Add((lash.ToArray(), 0.8));
            }
        }
        FaceStrokeCount = featureStrokes.Count;
        featureStrokes.AddRange(earRims);
        FeatureStrokes = featureStrokes;

        // Éclat propre des traits pour le rendu en points.
        _glowLines.Add((Line(RightEyeLoop), 1.0));
        _glowLines.Add((Line(LeftEyeLoop), 1.0));
        var banu = species == HeadSpecies.Banu;
        _glowLines.Add((Line(RightBrow), banu ? 0 : 0.45));
        _glowLines.Add((Line(LeftBrow), banu ? 0 : 0.45));
        _glowLines.Add((Line(RightBrowTop), banu ? 0 : 0.25));
        _glowLines.Add((Line(LeftBrowTop), banu ? 0 : 0.25));
        _glowLines.Add((Line(LipsOuter), banu ? 0.15 : 0.55));
        _glowLines.Add((Line(LipsInner), 0.9));
        _glowLines.Add((Line(NoseBridge), 0.12));
        _glowLines.Add((Line(NoseBase), 0.85));
        foreach (var rim in earRims) _glowLines.Add((rim, 0.4));
    }

    private static Vec3 Average(IEnumerable<Vec3> points)
    {
        var list = points.ToList();
        var sum = new Vec3(0, 0, 0);
        foreach (var p in list) sum += p;
        return sum * (1.0 / list.Count);
    }

    /// <summary>Direction « vers l'extérieur » approximative depuis l'intérieur de la tête.</summary>
    private static Vec3 Outward(Vec3 p) => new(p.X, 0.4 * (p.Y + 0.1), p.Z + 0.1);

    private static (int, int) Ordered(int a, int b) => a < b ? (a, b) : (b, a);

    private static (int, int, int) Sorted(int a, int b, int c)
    {
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        return (a, b, c);
    }

    private static double WrapAngle(double du)
    {
        var period = 2 * Math.PI * AngleScale;
        while (du > period / 2) du -= period;
        while (du < -period / 2) du += period;
        return du;
    }

    // ---------- Visage MediaPipe ----------

    private static (List<Vec3> Vertices, List<(int, int, int)> Triangles) LoadFace()
    {
        var assembly = typeof(HeadModel3D).Assembly;
        var name = assembly.GetManifestResourceNames().First(n => n.EndsWith("canonical_face_model.obj", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var vertices = new List<Vec3>();
        var triangles = new List<(int, int, int)>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && parts[0] == "v")
                vertices.Add(new Vec3(Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
            else if (parts.Length == 4 && parts[0] == "f")
                triangles.Add((Index(parts[1]), Index(parts[2]), Index(parts[3])));
        }
        return (vertices, triangles);

        static double Parse(string s) => double.Parse(s, CultureInfo.InvariantCulture);
        static int Index(string s) => int.Parse(s.Split('/')[0], CultureInfo.InvariantCulture) - 1;
    }

    /// <summary>Contour du visage (arêtes qui ne bordent qu'un triangle), dans l'ordre.</summary>
    private static List<int> BoundaryLoop(List<(int, int, int)> triangles)
    {
        var count = new Dictionary<(int, int), int>();
        foreach (var (a, b, c) in triangles)
            foreach (var e in new[] { Ordered(a, b), Ordered(b, c), Ordered(a, c) })
                count[e] = count.GetValueOrDefault(e) + 1;
        var neighbours = new Dictionary<int, List<int>>();
        foreach (var ((a, b), n) in count)
        {
            if (n != 1) continue;
            if (!neighbours.TryGetValue(a, out var la)) neighbours[a] = la = new List<int>();
            if (!neighbours.TryGetValue(b, out var lb)) neighbours[b] = lb = new List<int>();
            la.Add(b);
            lb.Add(a);
        }
        var start = neighbours.Keys.Min();
        var loop = new List<int> { start };
        var visited = new HashSet<int> { start };
        int previous = -1, current = start;
        while (true)
        {
            var next = neighbours[current].FirstOrDefault(n => n != previous && !visited.Contains(n), -1);
            if (next < 0) break;
            loop.Add(next);
            visited.Add(next);
            previous = current;
            current = next;
        }
        return loop;
    }

    // ---------- Crâne ----------

    /// <summary>Arrondi de la coupe horizontale (superellipse) : plus de 2 = côtés qui tournent vite, visage plat.</summary>
    private static double Roundness(double y, bool front)
    {
        if (!front) return 2.1;
        if (y > 0.45) return 2.15;
        if (y > 0.2) return 2.15 + 0.45 * (0.45 - y) / 0.25;
        if (y > -0.3) return 2.6;
        if (y > -0.75) return 2.6 - 0.6 * (-0.3 - y) / 0.45;
        return 2.0;
    }

    /// <summary>Point de la surface du crâne à la hauteur y et à l'angle phi autour de la tête (0 = face).</summary>
    private static Vec3 Surface(double y, double rx, double front, double back, double phi)
    {
        double s = Math.Sin(phi), c = Math.Cos(phi);
        var n = Roundness(y, c > 0);
        var x = rx * Math.Sign(s) * Math.Pow(Math.Abs(s), 2 / n);
        var z = (c > 0 ? front : back) * Math.Sign(c) * Math.Pow(Math.Abs(c), 2 / n);
        // Tempes un peu creusées, angle de la mâchoire marqué.
        var aphi = Math.Abs(phi);
        var dx = -0.03 * G((aphi - 1.0) / 0.3) * G((y - 0.25) / 0.12) + 0.03 * G((aphi - 1.3) / 0.3) * G((y + 0.55) / 0.08);
        return new Vec3(x + Math.Sign(s) * dx, y, z);
    }

    /// <summary>Largeur et profondeurs de la tête à la hauteur y (interpolation douce du profil).</summary>
    private static (double Y, double Rx, double Front, double Back) ProfileAt(double y)
    {
        var i = 0;
        while (i + 2 < Profile.Length && Profile[i + 1].Y > y) i++;
        var (y0, rx0, f0, b0) = Profile[i];
        var (y1, rx1, f1, b1) = Profile[i + 1];
        var k = Math.Clamp((y0 - y) / (y0 - y1), 0, 1);
        k = k * k * (3 - 2 * k) * 0.5 + k * 0.5;
        return (y, rx0 + (rx1 - rx0) * k, f0 + (f1 - f0) * k, b0 + (b1 - b0) * k);
    }

    /// <summary>Profondeur de l'avant du crâne en (x, y) (sert à poser le visage dessus).</summary>
    private static double BaseFrontZ(double x, double y)
    {
        var (_, rx, front, back) = ProfileAt(y);
        var ratio = Math.Clamp(Math.Abs(x) / rx, 0, 1);
        var n = Roundness(y, true);
        var phi = Math.Sign(x) * Math.Asin(Math.Pow(ratio, n / 2));
        return Surface(y, rx, front, back, phi).Z;
    }

    /// <summary>Demi-largeur du crâne à la hauteur y et à la profondeur z (côté de la tête).</summary>
    private static double SideX(double y, double z)
    {
        var (_, rx, front, back) = ProfileAt(y);
        var depth = z >= 0 ? front : back;
        var n = Roundness(y, z >= 0);
        var Z = Math.Clamp(z / depth, -1, 1);
        var c = Math.Sign(Z) * Math.Pow(Math.Abs(Z), n / 2);
        var phi = Math.Acos(Math.Clamp(c, -1, 1));
        return Math.Abs(Surface(y, rx, front, back, phi).X);
    }

    /// <summary>Position dans le plan de triangulation : angle autour de la tête (mis à l'échelle) et hauteur.</summary>
    private static (double U, double V) ToPlane(Vec3 p)
    {
        var (_, rx, front, back) = ProfileAt(p.Y);
        var depth = p.Z >= 0 ? front : back;
        double X = p.X / rx, Z = p.Z / depth;
        var n = Roundness(p.Y, p.Z >= 0);
        var s = Math.Sign(X) * Math.Pow(Math.Abs(X), n / 2);
        var c = Math.Sign(Z) * Math.Pow(Math.Abs(Z), n / 2);
        return (Math.Atan2(s, c) * AngleScale, p.Y);
    }

    private static double G(double v) => Math.Exp(-v * v);

    private static double Smooth(double v)
    {
        v = Math.Clamp(v, 0, 1);
        return v * v * (3 - 2 * v);
    }

    /// <summary>
    /// Tête de Banu (inspirée de l'espèce extraterrestre de Star Citizen) :
    /// crâne qui s'allonge en crête vers le haut et l'arrière, tempes et
    /// visage étroits, front et joues creusés de rides verticales, arête
    /// qui part du nez et monte sur le front, nez aplati, yeux enfoncés,
    /// lèvres fines, menton étroit, cou long et fin. Un peu réduite pour
    /// que la crête tienne dans la place de l'avatar.
    /// </summary>
    private static Vec3 MorphBanu(Vec3 p, Vec3[] eyes, Vec3 mouth, Vec3 nose) => MorphBanu(p, eyes, mouth, nose, out _);

    /// <summary>Idem, avec l'ombre du point (1 = au fond d'une fente de la crête, dessiné plus sombre).</summary>
    private static Vec3 MorphBanu(Vec3 p, Vec3[] eyes, Vec3 mouth, Vec3 nose, out double shade)
    {
        double x = p.X, y = p.Y, z = p.Z;
        var front = Math.Clamp((z + 0.05) / 0.4, 0, 1);
        var ax = Math.Abs(x);
        var neck = Smooth((-0.8 - y) / 0.15);

        // Crâne plus haut, arrière de la tête très bombé (grosse boîte crânienne vers l'arrière).
        var high = Smooth((y - 0.15) / 0.7);
        var backSide = Smooth((0.05 - z) / 0.45);
        y += 0.16 * high;
        z -= 0.2 * backSide * G((y - 0.6) / 0.3);
        y += 0.08 * backSide * high;
        x *= 1 - 0.1 * high;
        // Crête : une lame au milieu du haut du crâne, en pointe vers le haut, côté front.
        // Lame large au sommet plat : plateau sur toute sa largeur, bords qui retombent franchement.
        var finHalf = 0.17 + 0.06 * Smooth((0.9 - y) / 0.4);
        var finZone = Smooth((finHalf - Math.Abs(x)) / 0.07) * Smooth((y - 0.5) / 0.4) * G((z - 0.1) / 0.32);
        y += 0.3 * finZone;
        z += 0.08 * finZone;
        // Dessus plat : au-delà d'une hauteur, la crête ne monte presque plus (plateau, pas de pointe).
        const double plateau = 1.02;
        if (y > plateau) y = plateau + (y - plateau) * 0.12;
        // Sillons verticaux sur le crâne et le front (rayonnent depuis l'axe de la tête).
        var phi = Math.Atan2(x, z + 0.1);
        var grooves = Smooth((y + 0.05) / 0.35) * (1 - 0.7 * finZone);
        var groove = 0.035 * grooves * (Math.Sin(phi * 9 + 0.6 * Math.Sin(y * 6)) * 0.75 + Math.Sin(phi * 19 + 1) * 0.25);
        var radial = Math.Sqrt(x * x + (z + 0.1) * (z + 0.1));
        if (radial > 1e-6)
        {
            x += x / radial * groove;
            z += (z + 0.1) / radial * groove;
        }
        var top = high * 0.5;

        // Visage long, mâchoire et menton larges et forts, cou épais strié de tendons.
        y -= 0.05 * Smooth((-0.3 - y) / 0.4);
        // Menton grand : plus long vers le bas, large et en avant.
        var chin = G(x / 0.24) * Smooth((-0.5 - y) / 0.14) * (1 - neck) * front;
        y -= 0.2 * chin;
        x *= 1 + 0.15 * chin;
        z += 0.12 * G(x / 0.24) * G((y + 0.82) / 0.13) * front;
        x *= 1 + 0.06 * G((y + 0.55) / 0.15) * (1 - neck);                        // mâchoire large
        x *= 1 + 0.08 * neck;
        z = -0.07 + (z + 0.07) * (1 + 0.04 * neck);
        var neckPhi = Math.Atan2(x, z + 0.07);
        var tendon = 0.03 * neck * Math.Max(0, Math.Sin(neckPhi * 7));
        var neckR = Math.Sqrt(x * x + (z + 0.07) * (z + 0.07));
        if (neckR > 1e-6)
        {
            x += x / neckR * tendon;
            z += (z + 0.07) / neckR * tendon;
        }

        // Lourdes arcades qui surplombent les yeux.
        foreach (var eye in eyes)
            z += 0.1 * G((y - eye.Y - 0.08) / 0.045) * G((x - eye.X) / 0.13) * front;

        // Nez aplati (narines en fentes) puis arête centrale : du bout du nez jusqu'en haut de la
        // crête, une seule ligne en relief, à peu près à la même hauteur que le nez.
        z -= 0.045 * G(x / 0.08) * G((y - nose.Y) / 0.07) * front;
        var ridgeZ = nose.Z - 0.03 - 0.06 * (y - nose.Y) - 0.27 * top * top;
        var ridge = G(x / 0.05) * Smooth((y - nose.Y + 0.06) / 0.06) * Smooth((1.15 - y) / 0.25) * Smooth((z + 0.2) / 0.3);
        if (z < ridgeZ) z += (ridgeZ - z) * ridge;
        // Fentes de chaque côté de l'arête, dans le front et la crête.
        var slit = G((ax - 0.1) / 0.05) * Smooth((y - 0.2) / 0.12) * Smooth((0.95 - y) / 0.2) * Smooth((z + 0.2) / 0.3);
        z -= 0.11 * slit;
        shade = Math.Clamp(slit * 1.6, 0, 1);
        // Yeux enfoncés, orbites marquées.
        foreach (var eye in eyes)
        {
            var d = Math.Sqrt((x - eye.X) * (x - eye.X) + (y - eye.Y) * (y - eye.Y));
            z -= 0.03 * G(d / 0.09) * front;
        }
        // Pommettes saillantes (en avant et sur les côtés), joues bien creuses dessous.
        z += 0.09 * G((ax - 0.33) / 0.09) * G((y + 0.02) / 0.08) * front;
        x += Math.Sign(x) * 0.07 * G((ax - 0.42) / 0.11) * G((y + 0.02) / 0.09) * front;
        z -= 0.14 * G((ax - 0.28) / 0.1) * G((y + 0.27) / 0.11) * front;          // joues très creusées
        // Bouche large aux lèvres épaisses, coins tombants.
        var lips = G(x / 0.16) * G((y - mouth.Y) / 0.06) * front;
        x += x * 0.18 * lips;                                                      // bouche large
        z += 0.03 * lips;                                                          // lèvres épaisses
        y -= 0.015 * G((ax - 0.14) / 0.05) * G((y - mouth.Y) / 0.03) * front;      // coins tombants
        // Profil plat : crête, nez, centre de la bouche et menton sur une même ligne presque
        // verticale (un peu plus en avant en haut). Le milieu du visage est ramené sur cette ligne :
        // le nez recule, le front et la crête avancent.
        {
            var lineZ = nose.Z - 0.05 + 0.04 * (y - nose.Y);
            var band = 0.2 - 0.04 * Smooth((y - 0.3) / 0.6);
            var along = Smooth((y + 0.85) / 0.12) * Smooth((1.4 - y) / 0.2);
            var w = 0.9 * G(x / band) * along * Smooth((z + 0.3) / 0.4);
            w *= 1 - 0.8 * Smooth((mouth.Y - 0.08 - y) / 0.12); // le menton dépasse de la ligne
            z += (lineZ - z) * w;
        }

        // Rides et sillons verticaux sur le front, le crâne et les joues.
        var wrinkleZone = front * (1 - top);
        var wrinkles = Math.Sin(x * 30 + 0.5 * Math.Sin(y * 9)) * 0.65 + Math.Sin(x * 17 + 1.3) * 0.35;
        var calm = 1 - G((y - mouth.Y) / 0.08) * G(x / 0.2) - 0.7 * G(((x - eyes[0].X) * (x - eyes[0].X) + (y - eyes[0].Y) * (y - eyes[0].Y)) / 0.012) - 0.7 * G(((x - eyes[1].X) * (x - eyes[1].X) + (y - eyes[1].Y) * (y - eyes[1].Y)) / 0.012);
        z += 0.035 * wrinkles * wrinkleZone * Math.Clamp(calm, 0, 1) * (1 - neck);

        const double scale = 0.84;
        return new Vec3(x * scale, -0.15 + (y + 0.15) * scale, z * scale);
    }

    /// <summary>
    /// Différences homme / femme (proportions moyennes) appliquées à la tête
    /// construite. Femme : arcades effacées, yeux un peu plus grands, lèvres
    /// plus pleines, nez plus petit, pommettes plus hautes, mâchoire plus
    /// étroite, menton plus petit, bas du visage plus court, cou plus fin,
    /// tête un peu plus petite. Homme : arcades et nez plus marqués,
    /// mâchoire plus large, menton plus carré, cou plus épais, pomme d'Adam.
    /// </summary>
    private static Vec3 Morph(Vec3 p, bool female, Vec3[] eyes, Vec3 mouth, Vec3 nose)
    {
        double x = p.X, y = p.Y, z = p.Z;
        var front = Math.Clamp((z + 0.05) / 0.4, 0, 1);
        var ax = Math.Abs(x);
        var noseZone = G(x / 0.07) * G((y - nose.Y) / 0.09) * front;
        var jawZone = G((y + 0.55) / 0.16);
        var neck = Smooth((-0.8 - y) / 0.15);
        if (female)
        {
            z -= 0.035 * G((y - 0.20) / 0.05) * G(x / 0.32) * front;                 // arcades effacées
            foreach (var eye in eyes)
            {
                var d = Math.Sqrt((x - eye.X) * (x - eye.X) + (y - eye.Y) * (y - eye.Y));
                var w = G(d / 0.075) * front;
                x += (x - eye.X) * 0.22 * w;                                         // yeux plus grands
                y += (y - eye.Y) * 0.3 * w;
            }
            var lips = G(x / 0.14) * G((y - mouth.Y) / 0.055) * front;
            y += (y - mouth.Y) * 0.35 * lips;                                        // lèvres plus pleines
            z += 0.025 * lips;
            z -= 0.035 * noseZone;                                                   // nez plus petit
            x *= 1 - 0.18 * G(x / 0.08) * G((y - nose.Y + 0.03) / 0.05) * front;
            z += 0.025 * G((ax - 0.3) / 0.1) * G((y - 0.02) / 0.08) * front;         // pommettes hautes
            x *= 1 - 0.17 * Smooth((-0.25 - y) / 0.45) * (1 - neck);                 // mâchoire étroite, visage en cœur
            z -= 0.03 * G(x / 0.13) * G((y + 0.72) / 0.08) * front;                  // menton plus petit
            y += 0.04 * Smooth((-0.45 - y) / 0.3);                                   // bas du visage plus court
            x *= 1 - 0.25 * neck;                                                    // cou fin
            z = -0.07 + (z + 0.07) * (1 - 0.2 * neck);
            const double scale = 0.95;                                               // tête un peu plus petite
            return new Vec3(x * scale, -0.1 + (y + 0.1) * scale, z * scale);
        }
        z += 0.02 * G((y - 0.20) / 0.05) * G(x / 0.32) * front;                      // arcades marquées
        z += 0.01 * noseZone;                                                        // nez un peu plus fort
        x *= 1 + 0.11 * jawZone * Smooth((-0.3 - y) / 0.2);                          // mâchoire large
        z += 0.02 * G(x / 0.18) * G((y + 0.7) / 0.07) * front;                       // menton carré
        x *= 1 + 0.25 * G(x / 0.2) * G((y + 0.72) / 0.06) * front * (Math.Abs(x) > 0.05 ? 1 : 0); // menton large
        x *= 1 + 0.18 * neck;                                                        // cou épais
        z = -0.07 + (z + 0.07) * (1 + 0.12 * neck);
        if (z > 0) z += 0.03 * G(x / 0.045) * G((y + 0.97) / 0.05);                  // pomme d'Adam
        return new Vec3(x, y, z);
    }

    // ---------- Oreilles ----------

    /// <summary>
    /// Pavillon d'oreille : ovale incliné vers l'arrière, avec un rebord
    /// (hélix) en relief et un creux (conque), posé sur le côté de la tête
    /// entre la hauteur des sourcils et celle du bas du nez.
    /// </summary>
    private static (List<Vec3> Nodes, List<(int, int, int)> Triangles, Vec3[] Rim, Vec3 Center) BuildEar(double side, int offset)
    {
        const int around = 18;
        double[] radii = { 1.0, 0.82, 0.6, 0.35 };
        double[] heights = { 0.045, 0.06, 0.035, 0.012 };
        const double centerY = -0.02, height = 0.2, width = 0.11, tilt = 0.25, frontZ = -0.03;
        var nodes = new List<Vec3>();

        Vec3 Place(double w, double v, double h)
        {
            // w : vers l'arrière ; v : vers le haut ; h : décollement de la tête.
            var y = centerY + v * Math.Cos(tilt) - w * Math.Sin(tilt);
            var back = w * Math.Cos(tilt) + v * Math.Sin(tilt);
            var z = frontZ - back;
            var flare = Math.Max(0, back) * 0.3;
            return new Vec3(side * (SideX(y, z) + 0.005 + h + flare), y, z);
        }

        for (var ring = 0; ring < radii.Length; ring++)
        {
            for (var k = 0; k < around; k++)
            {
                var a = k / (double)around * 2 * Math.PI;
                var r = radii[ring];
                var w = width * r * (0.15 + 0.85 * (Math.Cos(a) + 1) / 2);
                var v = height * r * Math.Sin(a);
                // Le devant de l'oreille est collé à la tête : pas de rebord.
                var attach = Math.Clamp((Math.Cos(a) + 0.6) / 0.9, 0, 1);
                nodes.Add(Place(w, v, heights[ring] * attach));
            }
        }
        nodes.Add(Place(width * 0.25, -0.01, 0.0)); // fond de la conque
        var center = nodes[^1];

        var triangles = new List<(int, int, int)>();
        for (var ring = 0; ring + 1 < radii.Length; ring++)
            for (var k = 0; k < around; k++)
            {
                int a = offset + ring * around + k, b = offset + ring * around + (k + 1) % around;
                int c = offset + (ring + 1) * around + k, d = offset + (ring + 1) * around + (k + 1) % around;
                triangles.Add((a, b, c));
                triangles.Add((b, d, c));
            }
        var last = (radii.Length - 1) * around;
        for (var k = 0; k < around; k++)
            triangles.Add((offset + last + k, offset + last + (k + 1) % around, offset + nodes.Count - 1));

        var rim = Enumerable.Range(0, around + 1).Select(k => nodes[around + k % around] + new Vec3(side * 0.008, 0, 0)).ToArray();
        return (nodes, triangles, rim, center);
    }

    // ---------- Mâchoire, projection, éclairage ----------

    /// <summary>Descente maximale de la lèvre du bas quand la bouche est grande ouverte (unités de la tête).</summary>
    public const double LipOpening = 0.06;

    /// <summary>
    /// Mouvement des lèvres pour un point : 1 = descend avec la lèvre du
    /// bas, négatif = remonte un peu avec la lèvre du haut, 0 = immobile.
    /// Seules les lèvres bougent (le menton suit à peine), comme quand on parle.
    /// </summary>
    public static double JawWeightAt(Vec3 n) => JawWeightAt(n, MouthLine);

    /// <summary>Mouvement des lèvres d'un point pour la bouche de cette tête-ci.</summary>
    public double JawWeightOf(Vec3 n) => JawWeightAt(n, MouthPoint.Y);

    private static double JawWeightAt(Vec3 n, double mouthLine)
    {
        if (n.Z < 0.2) return 0.0;
        var width = Math.Exp(-Math.Pow(n.X / 0.17, 2)); // largeur de la bouche
        if (n.Y >= mouthLine - 0.004)
        {
            // Lèvre du haut : remonte d'un tiers du mouvement de celle du bas.
            return -0.35 * width * Math.Exp(-Math.Pow((n.Y - (mouthLine + 0.02)) / 0.03, 2));
        }
        var below = Math.Clamp((mouthLine - 0.004 - n.Y) / 0.015, 0, 1);
        // Lèvre du bas entière, puis le menton ne suit presque plus.
        var depth = mouthLine - n.Y;
        var falloff = depth < 0.07 ? 1.0 : Math.Max(0, 1 - (depth - 0.07) / 0.12) * 0.6;
        return below * width * falloff;
    }

    private const double Camera = 4.0;

    /// <summary>Rotation, ouverture de la mâchoire et perspective d'un point.</summary>
    public static (double X, double Y, double Z) ProjectPoint(Vec3 n, double jawWeight, double yaw, double pitch, double roll, double jawOpen)
    {
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch), cr = Math.Cos(roll), sr = Math.Sin(roll);
        var y0 = n.Y - jawWeight * jawOpen * LipOpening + 0.3;
        var z0 = n.Z - Math.Abs(jawWeight) * jawOpen * 0.008;
        var x1 = n.X * cy + z0 * sy;
        var z1 = -n.X * sy + z0 * cy;
        var y2 = y0 * cp - z1 * sp;
        var z2 = y0 * sp + z1 * cp;
        var x3 = x1 * cr - y2 * sr;
        var y3 = x1 * sr + y2 * cr - 0.3;
        var f = Camera / (Camera - z2);
        return (x3 * f, -y3 * f, z2);
    }

    /// <summary>
    /// Visibilité d'un point de la surface (0 = caché derrière la tête,
    /// 1 = bien de face) : composante vers le spectateur de sa direction
    /// depuis l'axe de la tête, après rotation.
    /// </summary>
    public static double SurfaceVisibility(Vec3 n, double yaw, double pitch)
    {
        var dir = new Vec3(n.X, 0, n.Z + 0.1);
        var length = dir.Length;
        if (length < 1e-6) return 1;
        var z = (-dir.X * Math.Sin(yaw) + dir.Z * Math.Cos(yaw)) / length;
        z *= Math.Cos(pitch);
        return Math.Clamp((z - 0.05) / 0.35, 0, 1);
    }

    /// <summary>
    /// Fait pivoter la tête (yaw : tourne vers la droite de l'écran si
    /// positif ; pitch : lève la tête si positif ; roll : penche), ouvre la
    /// mâchoire (0..1) et projette en perspective. Résultat en unités de
    /// l'avatar (y vers le bas). world (facultatif) : positions tournées, y vers le haut.
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

    private static readonly Vec3 Light = new Vec3(-0.45, 0.55, 0.70).Normalized();

    /// <summary>
    /// Éclat d'un triangle tourné vers le spectateur (0..1) : bords de la
    /// silhouette lumineux (effet hologramme) plus un éclairage venant d'en
    /// haut à gauche, qui fait ressortir le relief du visage.
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

    /// <summary>Même rotation que ProjectPoint, pour une direction (normale).</summary>
    public static Vec3 RotateDirection(Vec3 n, double yaw, double pitch, double roll)
    {
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch), cr = Math.Cos(roll), sr = Math.Sin(roll);
        var x1 = n.X * cy + n.Z * sy;
        var z1 = -n.X * sy + n.Z * cy;
        var y2 = n.Y * cp - z1 * sp;
        var z2 = n.Y * sp + z1 * cp;
        return new Vec3(x1 * cr - y2 * sr, x1 * sr + y2 * cr, z2);
    }

    /// <summary>Éclairage d'un point (normale déjà tournée) : bords lumineux + lumière d'en haut à gauche.</summary>
    public static double PointLight(Vec3 rotatedNormal)
    {
        var fresnel = Math.Pow(1 - Math.Clamp(rotatedNormal.Z, 0, 1), 1.5);
        var lambert = Math.Max(0, Vec3.Dot(rotatedNormal, Light));
        return Math.Clamp(0.22 + 0.4 * fresnel + 0.28 * lambert, 0, 1);
    }

    // ---------- Nuage de points ----------

    /// <summary>Nuage de points pour le rendu « points » : position, normale, éclat propre (traits du visage), poids de mâchoire.</summary>
    public sealed record PointCloudData(Vec3[] Positions, Vec3[] Normals, double[] Glow, double[] Jaw, bool[] OnFace);

    private PointCloudData? _points;

    /// <summary>Nuage de points de cette tête (calculé à la première demande).</summary>
    public PointCloudData Points => _points ??= BuildPointCloud();

    /// <summary>Points répartis sur toute la surface (plus serrés sur le visage), calculés une fois.</summary>
    public static PointCloudData PointCloud => Default.Points;

    /// <summary>Écart entre deux rangées de points, et entre deux points d'une rangée (au plus large de la tête).</summary>
    private const double PointRowStep = 0.026;
    private const int PointsPerRow = 132;

    /// <summary>
    /// Points rangés comme un scan 3D : la tête est coupée en tranches
    /// horizontales régulières et, sur chaque tranche, un point est posé à
    /// angles égaux autour de la tête (le plus à l'extérieur de la surface
    /// dans cette direction). Les rangées sont donc parfaitement
    /// horizontales et les colonnes suivent les courbes du visage.
    /// </summary>
    private PointCloudData BuildPointCloud()
    {
        var positions = new List<Vec3>();
        var normals = new List<Vec3>();
        var onFaceList = new List<bool>();
        var shadeList = new List<double>();
        const double axisZ = -0.05;

        var minY = Nodes.Min(n => n.Y);
        var maxY = Nodes.Max(n => n.Y);
        var row = 0;
        for (var y = maxY - PointRowStep * 0.5; y > minY + 0.01; y -= PointRowStep, row++)
        {
            // Coupe de la tête à cette hauteur : un segment par triangle traversé.
            var cuts = new List<(Vec3 A, Vec3 B, Vec3 NA, Vec3 NB, bool Face, double SA, double SB)>();
            for (var t = 0; t < Triangles.Count; t++)
            {
                var (a, b, c) = Triangles[t];
                var ends = new List<(Vec3 P, Vec3 N, double S)>(2);
                foreach (var (i, j) in new[] { (a, b), (b, c), (c, a) })
                {
                    var pi = Nodes[i];
                    var pj = Nodes[j];
                    if ((pi.Y - y) * (pj.Y - y) > 0 || Math.Abs(pi.Y - pj.Y) < 1e-12) continue;
                    var f = (y - pi.Y) / (pj.Y - pi.Y);
                    if (f < 0 || f > 1) continue;
                    ends.Add((pi + (pj - pi) * f, NodeNormals[i] * (1 - f) + NodeNormals[j] * f, NodeShade[i] * (1 - f) + NodeShade[j] * f));
                    if (ends.Count == 2) break;
                }
                if (ends.Count == 2) cuts.Add((ends[0].P, ends[1].P, ends[0].N, ends[1].N, FaceTriangle[t], ends[0].S, ends[1].S));
            }
            if (cuts.Count == 0) continue;

            // Moins de points sur les petites tranches (sommet du crâne) pour garder le même écart.
            var reach = cuts.Max(cu => Math.Max(new Vec3(cu.A.X, 0, cu.A.Z - axisZ).Length, new Vec3(cu.B.X, 0, cu.B.Z - axisZ).Length));
            var count = Math.Clamp((int)Math.Round(2 * Math.PI * reach * 0.85 / PointRowStep), 8, PointsPerRow);
            for (var k = 0; k < count; k++)
            {
                // Colonnes alignées d'une rangée à l'autre (même angle de départ).
                var phi = -Math.PI + (k + 0.5) / count * 2 * Math.PI;
                double dx = Math.Sin(phi), dz = Math.Cos(phi);
                var best = -1.0;
                Vec3 hit = default, normal = default;
                var shade = 0.0;
                var face = false;
                foreach (var cu in cuts)
                {
                    // Rayon horizontal depuis l'axe de la tête : on garde la surface la plus à l'extérieur.
                    double ex = cu.B.X - cu.A.X, ez = cu.B.Z - cu.A.Z;
                    var denom = dx * ez - dz * ex;
                    if (Math.Abs(denom) < 1e-12) continue;
                    double ox = cu.A.X, oz = cu.A.Z - axisZ;
                    var tRay = (ox * ez - oz * ex) / denom;
                    var u = (ox * dz - oz * dx) / denom;
                    if (tRay <= 0 || u < 0 || u > 1 || tRay <= best) continue;
                    best = tRay;
                    hit = cu.A + (cu.B - cu.A) * u;
                    normal = cu.NA * (1 - u) + cu.NB * u;
                    face = cu.Face;
                    shade = cu.SA * (1 - u) + cu.SB * u;
                }
                if (best < 0) continue;
                positions.Add(hit);
                normals.Add(normal.Normalized());
                onFaceList.Add(face);
                shadeList.Add(shade);
            }
        }
        // Les creux (fentes de la crête) sont assombris : éclat négatif.
        var glow = positions.Select((p, i) => FeatureGlow(p) - 0.9 * shadeList[i]).ToArray();
        var jaw = positions.Select(JawWeightOf).ToArray();
        return new PointCloudData(positions.ToArray(), normals.ToArray(), glow, jaw, onFaceList.ToArray());
    }

    /// <summary>Éclat propre d'un point (0..1) : près des yeux, sourcils, nez, lèvres et oreilles.</summary>
    private double FeatureGlow(Vec3 p)
    {
        var glow = 0.0;
        foreach (var (line, weight) in _glowLines)
            glow += weight * G(DistanceToLine(line, p) / 0.012);
        glow += 0.5 * G(p.DistanceTo(EyeLeft) / 0.035) + 0.5 * G(p.DistanceTo(EyeRight) / 0.035);
        return Math.Clamp(glow, 0, 1);
    }

    private static double DistanceToLine(Vec3[] line, Vec3 p)
    {
        var best = double.MaxValue;
        for (var i = 0; i + 1 < line.Length; i++)
        {
            var a = line[i];
            var ab = line[i + 1] - a;
            var length2 = Vec3.Dot(ab, ab);
            var t = length2 < 1e-12 ? 0 : Math.Clamp(Vec3.Dot(p - a, ab) / length2, 0, 1);
            best = Math.Min(best, p.DistanceTo(a + ab * t));
        }
        return best;
    }

    // ---------- Outils 2D ----------

    private static bool InsidePolygon(List<(double U, double V)> polygon, double u, double v)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (ui, vi) = polygon[i];
            var (uj, vj) = polygon[j];
            if ((vi > v) != (vj > v) && u < (uj - ui) * (v - vi) / (vj - vi) + ui) inside = !inside;
        }
        return inside;
    }

    private static double DistanceToPolygon(List<(double U, double V)> polygon, double u, double v)
    {
        var best = double.MaxValue;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (ua, va) = polygon[j];
            var (ub, vb) = polygon[i];
            double du = ub - ua, dv = vb - va;
            var t = Math.Clamp(((u - ua) * du + (v - va) * dv) / Math.Max(1e-12, du * du + dv * dv), 0, 1);
            var x = ua + du * t - u;
            var y = va + dv * t - v;
            best = Math.Min(best, Math.Sqrt(x * x + y * y));
        }
        return best;
    }

    /// <summary>Triangulation de Delaunay (Bowyer-Watson).</summary>
    private static List<(int, int, int)> Delaunay(List<(double U, double V)> points)
    {
        var all = new List<(double U, double V)>(points) { (-100, -100), (100, -100), (0, 100) };
        int s0 = points.Count, s1 = s0 + 1, s2 = s0 + 2;
        var triangles = new List<(int A, int B, int C, double X, double Y, double R2)> { Circle(s0, s1, s2) };
        for (var i = 0; i < points.Count; i++)
        {
            var (px, py) = points[i];
            var edges = new Dictionary<(int, int), int>();
            var keep = new List<(int A, int B, int C, double X, double Y, double R2)>(triangles.Count + 4);
            foreach (var t in triangles)
            {
                var dx = px - t.X;
                var dy = py - t.Y;
                if (dx * dx + dy * dy < t.R2)
                {
                    foreach (var e in new[] { Ordered(t.A, t.B), Ordered(t.B, t.C), Ordered(t.A, t.C) })
                        edges[e] = edges.GetValueOrDefault(e) + 1;
                }
                else keep.Add(t);
            }
            foreach (var ((a, b), n) in edges)
                if (n == 1) keep.Add(Circle(a, b, i));
            triangles = keep;
        }
        return triangles.Where(t => t.A < s0 && t.B < s0 && t.C < s0).Select(t => (t.A, t.B, t.C)).ToList();

        (int, int, int, double, double, double) Circle(int a, int b, int c)
        {
            var (ax, ay) = all[a];
            var (bx, by) = all[b];
            var (cx, cy) = all[c];
            var d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            if (Math.Abs(d) < 1e-14) return (a, b, c, 0, 0, double.MaxValue);
            var ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
            var uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;
            return (a, b, c, ux, uy, (ax - ux) * (ax - ux) + (ay - uy) * (ay - uy));
        }
    }
}
