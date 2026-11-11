using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using NovaVox.Core.Avatar;
using Path = System.Windows.Shapes.Path;

namespace NovaVox.App.Overlay;

/// <summary>
/// Avatar « tête holographique » : à la place de la bulle, une tête en 3D
/// (maillage lumineux, HeadModel3D), de face, qui pivote selon ce qu'elle
/// fait — regarde autour d'elle au repos, se tourne vers toi quand elle
/// t'entend, lève les yeux quand elle réfléchit, hoche la tête en parlant,
/// baisse la tête quand elle s'endort, regarde où elle va en balade. La
/// mâchoire s'ouvre au rythme de la voix, les yeux brillent et clignent,
/// et des ondes de lumière parcourent le maillage depuis la bouche (voix),
/// les oreilles (écoute) ou le crâne (réflexion). Couleurs d'état de la
/// bulle. La rotation et la perspective sont calculées ici (pas de moteur
/// 3D) : quelques centaines de points par image.
/// </summary>
public partial class OrbWindow
{
    private const int HeadLevels = 6;

    private bool _headBuilt;
    private double _headVisible;
    // Pose de la tête (radians) et vitesses : ressorts amortis vers la pose voulue.
    private double _headYaw, _headYawVel, _headPitch, _headPitchVel, _headRoll, _headRollVel;
    private double _headGazeYaw, _headGazePitch, _nextHeadGazeAt = 2;
    private double _nextHeadBlinkAt = 3, _headBlinkStart = -10;

    private readonly Canvas _headCanvas = new();
    private readonly Canvas _headMeshCanvas = new();
    private readonly Canvas _headPointCanvas = new();
    private readonly Path[] _headPointPaths = new Path[HeadLevels];
    private Path? _headPointGlowPath;
    private double[] _headPointMouth = Array.Empty<double>(), _headPointEar = Array.Empty<double>(), _headPointBrain = Array.Empty<double>();
    private double[] _headPointPhase = Array.Empty<double>(), _headPointFreq = Array.Empty<double>();
    private readonly Path[] _headLevelPaths = new Path[HeadLevels];
    private Path? _headBackPath, _headGlowPath, _headDotsPath, _headDotsBrightPath, _headFeaturesPath, _headFeaturesFaintPath;
    private readonly Ellipse[] _headEyes = new Ellipse[2];
    private readonly ScaleTransform[] _headEyeScales = { new(), new() };
    private readonly SolidColorBrush _headLineBrush = new(Colors.White);
    private readonly SolidColorBrush _headBrightBrush = new(Colors.White);
    private readonly SolidColorBrush _headBackBrush = new(Colors.White);
    private readonly GradientStop _headEyeEdge = new(Colors.Transparent, 1);
    private readonly EllipseGeometry _headGlowShape = new();

    private ProjectedNode[] _headProjected = Array.Empty<ProjectedNode>();
    private Vec3[] _headView = Array.Empty<Vec3>();
    private double[] _headTriBright = Array.Empty<double>();
    private bool[] _headTriVisible = Array.Empty<bool>();
    // Distances (au repos) de chaque arête à la bouche, aux oreilles et au crâne : pour les ondes.
    private double[] _headEdgeMouth = Array.Empty<double>(), _headEdgeEar = Array.Empty<double>(), _headEdgeBrain = Array.Empty<double>();
    private double[] _headNodePhase = Array.Empty<double>(), _headNodeFreq = Array.Empty<double>(), _headNodeFacing = Array.Empty<double>();

    private bool HeadMode => _config.HeadMode;

    /// <summary>Éléments de la bulle, masqués quand la tête est affichée.</summary>
    private UIElement[] BubbleOnlyElements => new UIElement[]
    {
        BodyPath, ShadePath, SpotClip, RimPath, InnerRingLayer, RingLayer, NeuralClip, CoreGroup, WaveGroup,
        ShimmerPath, MouthGroup, Highlight, EarGroup, CatBackGroup, CatFrontGroup, GlitchRed, GlitchCyan,
    };

    /// <summary>Tête affichée (homme ou femme) et ses tableaux de travail, refaits quand on change de visage.</summary>
    private HeadModel3D? _headModel;

    private HeadModel3D CurrentHeadModel()
    {
        var model = HeadModel3D.For(_config.HeadBanu ? HeadSpecies.Banu : _config.HeadFemale ? HeadSpecies.Female : HeadSpecies.Male);
        if (!ReferenceEquals(model, _headModel)) PrepareHeadModel(model);
        return model;
    }

    private void PrepareHeadModel(HeadModel3D model)
    {
        _headModel = model;
        var count = model.Nodes.Count;
        _headProjected = new ProjectedNode[count];
        _headView = new Vec3[count];
        _headTriBright = new double[model.Triangles.Count];
        _headTriVisible = new bool[model.Triangles.Count];
        _headNodePhase = new double[count];
        _headNodeFreq = new double[count];
        _headNodeFacing = new double[count];
        for (var i = 0; i < count; i++)
        {
            _headNodePhase[i] = _random.NextDouble() * Math.PI * 2;
            _headNodeFreq[i] = 0.4 + _random.NextDouble() * 1.2;
        }
        _headEdgeMouth = new double[model.Edges.Count];
        _headEdgeEar = new double[model.Edges.Count];
        _headEdgeBrain = new double[model.Edges.Count];
        for (var e = 0; e < model.Edges.Count; e++)
        {
            var (a, b) = model.Edges[e];
            var mid = (model.Nodes[a] + model.Nodes[b]) * 0.5;
            _headEdgeMouth[e] = mid.DistanceTo(model.MouthPoint);
            _headEdgeEar[e] = Math.Min(mid.DistanceTo(model.EarLeft), mid.DistanceTo(model.EarRight));
            _headEdgeBrain[e] = mid.DistanceTo(HeadModel3D.Brain);
        }

        PrepareHeadPoints(model);
    }

    private void BuildHead()
    {
        _headBuilt = true;
        HeadLayer.Children.Add(_headCanvas);
        _headCanvas.Children.Add(_headMeshCanvas);
        _headCanvas.Children.Add(_headPointCanvas);
        BuildHeadPoints();
        _headBackPath = new Path { Stroke = _headBackBrush, Opacity = 0.1 };
        _headMeshCanvas.Children.Add(_headBackPath);
        _headGlowPath = new Path
        {
            Stroke = _headLineBrush,
            Opacity = 0.45,
            Effect = new BlurEffect { Radius = 4, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
        };
        _headMeshCanvas.Children.Add(_headGlowPath);
        for (var l = 0; l < HeadLevels; l++)
        {
            _headLevelPaths[l] = new Path { Stroke = l >= HeadLevels - 2 ? _headBrightBrush : _headLineBrush, Opacity = 0.18 + 0.82 * l / (HeadLevels - 1) };
            _headMeshCanvas.Children.Add(_headLevelPaths[l]);
        }
        _headFeaturesFaintPath = new Path { Stroke = _headBrightBrush, Opacity = 0.3, StrokeLineJoin = PenLineJoin.Round };
        _headFeaturesPath = new Path { Stroke = _headBrightBrush, Opacity = 0.75, StrokeLineJoin = PenLineJoin.Round };
        _headMeshCanvas.Children.Add(_headFeaturesFaintPath);
        _headMeshCanvas.Children.Add(_headFeaturesPath);
        _headDotsPath = new Path { Fill = _headBrightBrush, Opacity = 0.55 };
        _headDotsBrightPath = new Path { Fill = Brushes.White, Opacity = 0.95 };
        _headMeshCanvas.Children.Add(_headDotsPath);
        _headMeshCanvas.Children.Add(_headDotsBrightPath);

        for (var i = 0; i < 2; i++)
        {
            _headEyes[i] = new Ellipse
            {
                Fill = new RadialGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Colors.White, 0),
                    new GradientStop(Color.FromArgb(0xe0, 0xff, 0xff, 0xff), 0.22),
                    _headEyeEdge,
                }),
                RenderTransform = _headEyeScales[i],
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            _headCanvas.Children.Add(_headEyes[i]);
        }
    }

    /// <summary>Rendu « nuage de points » : chemins par niveau d'éclat (points = traits de longueur nulle à bouts ronds).</summary>
    /// <summary>Distances et phases de chaque point du nuage (pour les ondes et le scintillement).</summary>
    private void PrepareHeadPoints(HeadModel3D model)
    {
        var cloud = model.Points;
        var count = cloud.Positions.Length;
        _headPointMouth = new double[count];
        _headPointEar = new double[count];
        _headPointBrain = new double[count];
        _headPointPhase = new double[count];
        _headPointFreq = new double[count];
        for (var i = 0; i < count; i++)
        {
            var p = cloud.Positions[i];
            _headPointMouth[i] = p.DistanceTo(model.MouthPoint);
            _headPointEar[i] = Math.Min(p.DistanceTo(model.EarLeft), p.DistanceTo(model.EarRight));
            _headPointBrain[i] = p.DistanceTo(HeadModel3D.Brain);
            _headPointPhase[i] = _random.NextDouble() * Math.PI * 2;
            _headPointFreq[i] = 0.3 + _random.NextDouble() * 1.5;
        }
    }

    private void BuildHeadPoints()
    {
        _headPointGlowPath = new Path
        {
            Stroke = _headLineBrush,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = 0.5,
            Effect = new BlurEffect { Radius = 4, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
        };
        _headPointCanvas.Children.Add(_headPointGlowPath);
        for (var l = 0; l < HeadLevels; l++)
        {
            _headPointPaths[l] = new Path
            {
                Stroke = l >= HeadLevels - 2 ? _headBrightBrush : _headLineBrush,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = 0.12 + 0.88 * l / (HeadLevels - 1),
            };
            _headPointCanvas.Children.Add(_headPointPaths[l]);
        }
    }

    /// <summary>Dessine la tête en points lumineux (rotation faite ici, une seule fois par image pour les sinus/cosinus).</summary>
    private void RenderHeadPoints(double t, double voice, double jaw, double cx, double cy, double k)
    {
        var cloud = _headModel!.Points;
        double cyw = Math.Cos(_headYaw), syw = Math.Sin(_headYaw), cp = Math.Cos(_headPitch), sp = Math.Sin(_headPitch), cr = Math.Cos(_headRoll), sr = Math.Sin(_headRoll);
        const double camera = 4.0;
        var speak = _speaking ? Math.Clamp(voice * 1.6, 0, 1) : 0;
        var sweep = (t - _shimmerStart) / ShimmerDuration;
        var sweepOn = sweep is >= 0 and <= 1 && _config.ShowReactions;
        var sweepY = 1.0 - sweep * 2.2;
        var tiny = Math.Max(0.01, k * 0.0005);

        var geometries = new StreamGeometry[HeadLevels];
        var contexts = new StreamGeometryContext[HeadLevels];
        for (var l = 0; l < HeadLevels; l++)
        {
            geometries[l] = new StreamGeometry();
            contexts[l] = geometries[l].Open();
        }
        var glow = new StreamGeometry();
        using (var glowCtx = glow.Open())
        {
            var faceOnly = _config.HeadFaceOnly;
            for (var i = 0; i < cloud.Positions.Length; i++)
            {
                if (faceOnly && !cloud.OnFace[i]) continue;
                // Normale tournée : de dos, le point n'est qu'un reflet très léger (un sur deux).
                var n = cloud.Normals[i];
                var nx1 = n.X * cyw + n.Z * syw;
                var nz1 = -n.X * syw + n.Z * cyw;
                var ny2 = n.Y * cp - nz1 * sp;
                var nz2 = n.Y * sp + nz1 * cp;
                var rotatedNormal = new Vec3(nx1 * cr - ny2 * sr, nx1 * sr + ny2 * cr, nz2);
                var back = nz2 < 0;
                if (back && (i & 1) == 0) continue;

                var q = cloud.Positions[i];
                var w = cloud.Jaw[i] * jaw;
                var y0 = q.Y - w * HeadModel3D.LipOpening + 0.3;
                var z0 = q.Z - Math.Abs(w) * 0.008;
                var x1 = q.X * cyw + z0 * syw;
                var z1 = -q.X * syw + z0 * cyw;
                var y2 = y0 * cp - z1 * sp;
                var z2 = y0 * sp + z1 * cp;
                var x3 = x1 * cr - y2 * sr;
                var y3 = x1 * sr + y2 * cr - 0.3;
                var f = camera / (camera - z2);
                var point = new Point(cx + x3 * f * k, cy - y3 * f * k);

                double bright;
                if (back) bright = 0.02;
                else
                {
                    bright = HeadModel3D.PointLight(rotatedNormal) + cloud.Glow[i] * 0.9 * (0.8 + 0.4 * Math.Max(_attention, _level));
                    var dm = _headPointMouth[i];
                    bright += speak * Math.Max(0, 1 - dm * 0.9) * (0.45 + 0.55 * Math.Sin(t * 9 - dm * 14));
                    var de = _headPointEar[i];
                    bright += _attention * Math.Max(0, 1 - de * 1.1) * Math.Max(0, Math.Sin(t * 6 - de * 10)) * 0.8;
                    var db = _headPointBrain[i];
                    bright += _thinkingAmount * (0.1 + 0.5 * Math.Max(0, Math.Sin(t * 4 - db * 7)));
                    if (sweepOn) bright += 0.9 * Math.Exp(-Math.Pow((q.Y - sweepY) / 0.15, 2));
                    var twinkle = Math.Sin(t * _headPointFreq[i] * (1 + 2 * _thinkingAmount) + _headPointPhase[i]);
                    if (twinkle > 0.97) bright += 0.5;
                    bright *= 1 - 0.35 * _sleepiness;
                }
                var level = Math.Clamp((int)(bright * HeadLevels), 0, HeadLevels - 1);
                contexts[level].BeginFigure(point, false, false);
                contexts[level].LineTo(new Point(point.X + tiny, point.Y), true, false);
                if (level >= HeadLevels - 2)
                {
                    glowCtx.BeginFigure(point, false, false);
                    glowCtx.LineTo(new Point(point.X + tiny, point.Y), true, false);
                }
            }
        }
        var dot = Math.Max(0.6, k * 0.0095);
        for (var l = 0; l < HeadLevels; l++)
        {
            contexts[l].Close();
            geometries[l].Freeze();
            _headPointPaths[l].Data = geometries[l];
            _headPointPaths[l].StrokeThickness = l >= HeadLevels - 2 ? dot * 1.4 : dot;
        }
        glow.Freeze();
        _headPointGlowPath!.Data = glow;
        _headPointGlowPath.StrokeThickness = dot * 3.5;
        ((BlurEffect)_headPointGlowPath.Effect).Radius = Math.Max(2, k * 0.03);
        _headPointGlowPath.Opacity = 0.4 + 0.3 * Math.Max(_level, _attention);
    }

    /// <summary>Ressort amorti (un peu souple) vers la valeur voulue.</summary>
    private static void Spring(ref double value, ref double velocity, double target, double dt, double stiffness = 30, double damping = 9)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(dt / (1.0 / 120)));
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            velocity += (stiffness * (target - value) - damping * velocity) * h;
            value += velocity * h;
        }
    }

    /// <summary>Anime la tête (appelée à chaque image, après la forme de la bulle).</summary>
    private void UpdateHead(Rect b, double dt, double t, double voice, bool hearing)
    {
        _headVisible += ((HeadMode ? 1.0 : 0.0) - _headVisible) * (1 - Math.Exp(-dt * 6));
        var showHead = _headVisible > 0.5;
        foreach (var element in BubbleOnlyElements)
        {
            var wanted = showHead ? Visibility.Hidden : Visibility.Visible;
            if (element.Visibility != wanted) element.Visibility = wanted;
        }
        HeadLayer.Opacity = _headVisible;
        if (_headVisible < 0.01)
        {
            if (HeadLayer.Visibility != Visibility.Collapsed) HeadLayer.Visibility = Visibility.Collapsed;
            return;
        }
        if (HeadLayer.Visibility != Visibility.Visible) HeadLayer.Visibility = Visibility.Visible;
        if (!_headBuilt) BuildHead();
        CurrentHeadModel();

        UpdateHeadPose(dt, t, voice);
        var jaw = _speaking ? Math.Clamp(_mouthOpen * 1.5, 0, 1) : 0;

        // Place et taille : la tête (environ 2 unités de haut) remplit la place de la bulle.
        var size = Math.Min(b.Width, b.Height) * 1.15;
        // Visage seul : agrandi pour remplir la place de la tête.
        var k = size * (_config.HeadFaceOnly ? 0.62 : 0.43);
        var cx = b.Left + b.Width / 2;
        var cy = b.Top + b.Height / 2 - 0.13 * k;

        // Couleurs : celles de l'état (repos, écoute, réflexion, parole), plus claires pour les traits.
        var main = _shownColor;
        _headLineBrush.Color = Lerp(main, Colors.White, 0.3);
        _headBrightBrush.Color = Lerp(main, Colors.White, 0.65);
        _headBackBrush.Color = main;
        _headEyeEdge.Color = WithAlpha(Lerp(main, Colors.White, 0.4), 0);

        // Halo flouté (GlowPath) : un ovale autour de la tête.
        _headGlowShape.Center = new Point(cx, cy + (_config.HeadFaceOnly ? 0.13 : 0.05) * k);
        _headGlowShape.RadiusX = (_config.HeadFaceOnly ? 0.6 : 0.7) * k;
        _headGlowShape.RadiusY = (_config.HeadFaceOnly ? 0.72 : 0.98) * k;
        if (HeadMode)
        {
            GlowPath.Data = _headGlowShape;
            GlowPath.Opacity *= 0.5;
        }

        var pointsMode = _config.HeadPoints;
        _headMeshCanvas.Visibility = pointsMode ? Visibility.Collapsed : Visibility.Visible;
        _headPointCanvas.Visibility = pointsMode ? Visibility.Visible : Visibility.Collapsed;
        if (pointsMode) RenderHeadPoints(t, voice, jaw, cx, cy, k);
        else RenderHeadMesh(t, voice, jaw, cx, cy, k);
        UpdateHeadEyes(t, jaw, cx, cy, k);
    }

    /// <summary>Dessine la tête en maillage (triangles, points, traits du visage).</summary>
    private void RenderHeadMesh(double t, double voice, double jaw, double cx, double cy, double k)
    {
        var model = _headModel!;
        model.Project(_headYaw, _headPitch, _headRoll, jaw, _headProjected, _headView);
        Point P(int i) => new(cx + _headProjected[i].X * k, cy + _headProjected[i].Y * k);

        // Triangles visibles et leur éclat (bords lumineux + relief éclairé).
        for (var tri = 0; tri < model.Triangles.Count; tri++)
        {
            _headTriVisible[tri] = model.FacesViewer(tri, _headProjected);
            _headTriBright[tri] = _headTriVisible[tri] ? model.Brightness(tri, _headView) : 0;
        }

        // Ondes : parole (bouche), écoute (oreilles), réflexion (crâne).
        var speak = _speaking ? Math.Clamp(voice * 1.6, 0, 1) : 0;
        var sweep = (t - _shimmerStart) / ShimmerDuration;
        var sweepOn = sweep is >= 0 and <= 1 && _config.ShowReactions;
        var sweepY = 1.0 - sweep * 2.2;

        var levels = new StreamGeometry[HeadLevels];
        var contexts = new StreamGeometryContext[HeadLevels];
        for (var l = 0; l < HeadLevels; l++)
        {
            levels[l] = new StreamGeometry();
            contexts[l] = levels[l].Open();
        }
        var glow = new StreamGeometry();
        var back = new StreamGeometry();
        using (var glowCtx = glow.Open())
        using (var backCtx = back.Open())
        {
            var faceOnly = _config.HeadFaceOnly;
            for (var e = 0; e < model.Edges.Count; e++)
            {
                var (a, c) = model.Edges[e];
                if (faceOnly && (a >= model.FaceNodeCount || c >= model.FaceNodeCount)) continue;
                var bright = -1.0;
                foreach (var tri in model.EdgeTriangles[e])
                    if (_headTriVisible[tri] && (!faceOnly || model.FaceTriangle[tri])) bright = Math.Max(bright, _headTriBright[tri]);
                if (bright < 0)
                {
                    backCtx.BeginFigure(P(a), false, false);
                    backCtx.LineTo(P(c), true, false);
                    continue;
                }
                var dm = _headEdgeMouth[e];
                bright += speak * Math.Max(0, 1 - dm * 0.9) * (0.45 + 0.55 * Math.Sin(t * 9 - dm * 14));
                var de = _headEdgeEar[e];
                bright += _attention * Math.Max(0, 1 - de * 1.1) * Math.Max(0, Math.Sin(t * 6 - de * 10)) * 0.8;
                var db = _headEdgeBrain[e];
                bright += _thinkingAmount * (0.1 + 0.5 * Math.Max(0, Math.Sin(t * 4 - db * 7)));
                if (sweepOn)
                {
                    var y = (model.Nodes[a].Y + model.Nodes[c].Y) / 2;
                    bright += 0.9 * Math.Exp(-Math.Pow((y - sweepY) / 0.15, 2));
                }
                bright *= 1 - 0.35 * _sleepiness;
                var level = Math.Clamp((int)(bright * HeadLevels), 0, HeadLevels - 1);
                contexts[level].BeginFigure(P(a), false, false);
                contexts[level].LineTo(P(c), true, false);
                if (level >= HeadLevels - 3)
                {
                    glowCtx.BeginFigure(P(a), false, false);
                    glowCtx.LineTo(P(c), true, false);
                }
            }
        }
        var thickness = Math.Max(0.5, k * 0.007);
        for (var l = 0; l < HeadLevels; l++)
        {
            contexts[l].Close();
            levels[l].Freeze();
            _headLevelPaths[l].Data = levels[l];
            _headLevelPaths[l].StrokeThickness = thickness;
        }
        glow.Freeze();
        back.Freeze();
        _headGlowPath!.Data = glow;
        _headGlowPath.StrokeThickness = thickness * 2.6;
        ((BlurEffect)_headGlowPath.Effect).Radius = Math.Max(2, k * 0.035);
        _headGlowPath.Opacity = 0.35 + 0.3 * Math.Max(_level, _attention);
        _headBackPath!.Data = back;
        _headBackPath.StrokeThickness = thickness * 0.7;

        // Points du maillage : ceux du côté visible, qui scintillent.
        var dots = new StreamGeometry();
        var brightDots = new StreamGeometry();
        var radius = Math.Max(0.45, k * 0.0055);
        using (var dotCtx = dots.Open())
        using (var brightCtx = brightDots.Open())
        {
            // Un point sur trois : avec un maillage aussi fin, tous les points feraient une masse blanche.
            for (var i = 0; i < model.Nodes.Count; i += 3)
            {
                if (_config.HeadFaceOnly && i >= model.FaceNodeCount) break;
                var visible = HeadModel3D.SurfaceVisibility(model.Nodes[i], _headYaw, _headPitch);
                if (visible < 0.3) continue;
                var twinkle = Math.Pow(Math.Max(0, Math.Sin(t * _headNodeFreq[i] * (1 + 2 * _thinkingAmount) + _headNodePhase[i])), 6);
                var ctx = twinkle > 0.5 ? brightCtx : dotCtx;
                var p = P(i);
                var r = twinkle > 0.5 ? radius * 1.4 : radius;
                ctx.BeginFigure(new Point(p.X - r, p.Y), true, true);
                ctx.ArcTo(new Point(p.X + r, p.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
                ctx.ArcTo(new Point(p.X - r, p.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
            }
        }
        dots.Freeze();
        brightDots.Freeze();
        _headDotsPath!.Data = dots;
        _headDotsBrightPath!.Data = brightDots;
        _headDotsPath.Opacity = 0.5 + 0.2 * _level - 0.2 * _sleepiness;

        // Traits du visage (yeux, sourcils, nez, lèvres) : nets de face, estompés sur les côtés.
        var features = new StreamGeometry();
        var faint = new StreamGeometry();
        using (var strong = features.Open())
        using (var weak = faint.Open())
        {
            for (var s = 0; s < model.FeatureStrokes.Count; s++)
            {
                if (_config.HeadFaceOnly && s >= model.FaceStrokeCount) break; // oreilles
                var stroke = model.FeatureStrokes[s];
                var visibility = 0.0;
                foreach (var q in stroke) visibility += HeadModel3D.SurfaceVisibility(q, _headYaw, _headPitch);
                visibility /= stroke.Length;
                if (visibility < 0.25) continue;
                var ctx = visibility > 0.6 ? strong : weak;
                for (var i = 0; i < stroke.Length; i++)
                {
                    var (x, y, _) = HeadModel3D.ProjectPoint(stroke[i], HeadModel3D.JawWeightAt(stroke[i]), _headYaw, _headPitch, _headRoll, jaw);
                    var point = new Point(cx + x * k, cy + y * k);
                    if (i == 0) ctx.BeginFigure(point, false, false);
                    else ctx.LineTo(point, true, true);
                }
            }
        }
        features.Freeze();
        faint.Freeze();
        _headFeaturesPath!.Data = features;
        _headFeaturesFaintPath!.Data = faint;
        _headFeaturesPath.StrokeThickness = _headFeaturesFaintPath.StrokeThickness = Math.Max(0.8, k * 0.011);
        _headFeaturesPath.Opacity = 0.6 + 0.3 * Math.Max(_attention, _level);

    }

    /// <summary>Yeux : points brillants qui clignent (fermés à moitié quand elle s'endort).</summary>
    private void UpdateHeadEyes(double t, double jaw, double cx, double cy, double k)
    {
        if (t > _nextHeadBlinkAt)
        {
            _headBlinkStart = t;
            _nextHeadBlinkAt = t + 2.5 + _random.NextDouble() * 4;
        }
        var blink = t - _headBlinkStart < 0.16 ? Math.Sin(Math.PI * (t - _headBlinkStart) / 0.16) : 0;
        var eyeSize = k * (0.16 + 0.03 * _attention + 0.03 * _level);
        var eyes = new[] { _headModel!.EyeLeft, _headModel.EyeRight };
        for (var i = 0; i < 2; i++)
        {
            var eye = eyes[i];
            var (x, y, _) = HeadModel3D.ProjectPoint(eye, 0, _headYaw, _headPitch, _headRoll, jaw);
            var shape = _headEyes[i];
            shape.Width = shape.Height = eyeSize;
            Canvas.SetLeft(shape, cx + x * k - eyeSize / 2);
            Canvas.SetTop(shape, cy + y * k - eyeSize / 2);
            shape.Opacity = HeadModel3D.SurfaceVisibility(eye, _headYaw, _headPitch) * (0.8 + 0.2 * Math.Max(_attention, _level));
            _headEyeScales[i].ScaleY = Math.Max(0.08, (1 - 0.9 * blink) * (1 - 0.8 * _sleepiness));
        }
    }

    /// <summary>
    /// Pose voulue : regard qui se promène au repos, face à toi quand elle
    /// t'entend, yeux levés quand elle réfléchit, hochements en parlant,
    /// tête basse quand elle s'endort, et tournée vers sa direction en balade
    /// (ou vers le centre de l'écran quand elle est sur un côté).
    /// </summary>
    private void UpdateHeadPose(double dt, double t, double voice)
    {
        if (t > _nextHeadGazeAt)
        {
            _headGazeYaw = (_random.NextDouble() - 0.5) * 1.0;
            _headGazePitch = (_random.NextDouble() - 0.5) * 0.3;
            _nextHeadGazeAt = t + 2.5 + _random.NextDouble() * 4;
        }

        // Vers le centre de l'écran : légèrement tournée quand elle est sur un côté.
        var screen = _screenDips;
        var towardCenter = 0.0;
        if (screen.Width > 0)
        {
            var offset = (Left + Width / 2 - (screen.Left + screen.Width / 2)) / (screen.Width / 2);
            towardCenter = -0.35 * Math.Clamp(offset, -1, 1);
        }
        // En balade : regarde où elle va.
        if (_roamVelocity.Length > 20) towardCenter = 0.6 * Math.Clamp(_roamVelocity.X / 150, -1, 1);

        var idle = 1 - Math.Max(_attention, Math.Max(_thinkingAmount, _speakingAmount));
        var yaw = towardCenter + idle * _headGazeYaw * 0.8 + _thinkingAmount * 0.3 * Math.Sin(t * 0.5);
        var pitch = idle * _headGazePitch + _thinkingAmount * 0.22 + _attention * 0.04
                    + _speakingAmount * 0.05 * Math.Sin(t * 5.3) * (0.3 + voice)
                    - 0.35 * _sleepiness;
        var roll = _thinkingAmount * 0.12 * Math.Sin(t * 0.7) + 0.15 * _sleepiness + 0.04 * Math.Sin(t * 0.37);
        // Visage seul : il ne tourne pas trop, pour ne jamais se montrer de profil (un masque vu par la tranche).
        var maxYaw = _config.HeadFaceOnly ? 0.6 : 1.2;
        Spring(ref _headYaw, ref _headYawVel, Math.Clamp(yaw, -maxYaw, maxYaw), dt, 22, 8);
        Spring(ref _headPitch, ref _headPitchVel, Math.Clamp(pitch, -0.5, 0.4), dt, 26, 8);
        Spring(ref _headRoll, ref _headRollVel, roll, dt, 18, 7);
    }
}
