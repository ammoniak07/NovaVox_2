using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using NovaVox.Core.Avatar;
using Path = System.Windows.Shapes.Path;

namespace NovaVox.App.Overlay;

/// <summary>
/// Avatar « tête holographique » : à la place de la bulle, une tête de
/// profil en maillage lumineux (triangles, points, traits du visage,
/// pistes de circuit à l'arrière du crâne), dans les couleurs d'état de la
/// bulle. Quand Nova parle, des ondes de lumière partent de la bouche au
/// rythme de la voix ; quand elle t'entend, elles partent de l'oreille ;
/// quand elle réfléchit, le crâne pulse et des impulsions filent sur les
/// circuits. L'œil cligne, les points scintillent, et la tête regarde vers
/// le centre de l'écran. La géométrie est construite une fois (HeadMesh)
/// puis seulement déplacée/agrandie : peu de calcul par image.
/// </summary>
public partial class OrbWindow
{
    /// <summary>Taille de construction (unités WPF pour 1 côté du carré de la tête).</summary>
    private const double HeadUnit = 200;
    private const int HeadBands = 5;

    private sealed class HeadNode
    {
        public required Ellipse Shape { get; init; }
        public required MeshPoint Point { get; init; }
        public double Freq, Phase;
    }

    private sealed class HeadTrace
    {
        public required MeshPoint[] Points { get; init; }
        public required double[] Cumulative { get; init; }
        public required Ellipse Pulse { get; init; }
        public double Offset;
    }

    private bool _headBuilt;
    private double _headVisible, _headFace = 1, _nextHeadBlinkAt = 3, _headBlinkStart = -10;
    private readonly Canvas _headContent = new();
    private readonly MatrixTransform _headTransform = new();
    private readonly MatrixTransform _headGlowTransform = new();
    private PathGeometry? _headGlowGeometry;
    private Path? _headFill, _headBaseLines, _headGlowLines, _headFeatures, _headTraces;
    private readonly Path[] _headMouthBands = new Path[HeadBands];
    private readonly Path[] _headEarBands = new Path[HeadBands];
    private readonly Path[] _headBrainBands = new Path[HeadBands];
    private readonly List<HeadNode> _headNodes = new();
    private readonly List<HeadTrace> _headTraceList = new();
    private Ellipse? _headEye;
    private readonly ScaleTransform _headEyeScale = new();
    private readonly SolidColorBrush _headLineBrush = new(Colors.White);
    private readonly SolidColorBrush _headBrightBrush = new(Colors.White);
    private readonly SolidColorBrush _headNodeBrush = new(Colors.White);
    private readonly GradientStop _headFillTop = new(Colors.Transparent, 0);
    private readonly GradientStop _headFillBottom = new(Colors.Transparent, 1);
    private readonly GradientStop _headEyeEdge = new(Colors.Transparent, 1);

    private bool HeadMode => _config.HeadMode;

    /// <summary>Éléments de la bulle, masqués quand la tête est affichée.</summary>
    private UIElement[] BubbleOnlyElements => new UIElement[]
    {
        BodyPath, ShadePath, SpotClip, RimPath, InnerRingLayer, RingLayer, NeuralClip, CoreGroup, WaveGroup,
        ShimmerPath, MouthGroup, Highlight, EarGroup, CatBackGroup, CatFrontGroup, GlitchRed, GlitchCyan,
    };

    private static Point HeadPoint(MeshPoint p) => new(p.X * HeadUnit, p.Y * HeadUnit);

    private void BuildHead()
    {
        _headBuilt = true;
        var mesh = HeadMesh.Default;
        HeadLayer.Children.Add(_headContent);
        _headContent.RenderTransform = _headTransform;

        // Silhouette : remplissage très léger (et halo flouté via GlowPath).
        var outline = new PathFigure { StartPoint = HeadPoint(HeadMesh.Outline[0]), IsClosed = true, IsFilled = true };
        foreach (var p in HeadMesh.Outline.Skip(1)) outline.Segments.Add(new LineSegment(HeadPoint(p), true));
        var silhouette = new PathGeometry(new[] { outline });
        silhouette.Freeze();
        _headGlowGeometry = new PathGeometry(new[] { outline.Clone() }) { Transform = _headGlowTransform };
        _headFill = new Path
        {
            Data = silhouette,
            Fill = new LinearGradientBrush(new GradientStopCollection { _headFillTop, _headFillBottom }, new Point(0.6, 0), new Point(0.4, 1)),
        };
        _headContent.Children.Add(_headFill);

        // Maillage : un tracé flouté (lueur) et un tracé net, géométrie figée.
        var lines = EdgesGeometry(mesh, mesh.Edges);
        _headGlowLines = new Path
        {
            Data = lines,
            Stroke = _headLineBrush,
            StrokeThickness = HeadUnit * 0.012,
            Opacity = 0.45,
            Effect = new BlurEffect { Radius = HeadUnit * 0.025, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
        };
        _headBaseLines = new Path { Data = lines, Stroke = _headLineBrush, StrokeThickness = HeadUnit * 0.0055, Opacity = 0.55 };
        _headContent.Children.Add(_headGlowLines);
        _headContent.Children.Add(_headBaseLines);

        // Ondes : arêtes groupées par distance à la bouche, à l'oreille et au cerveau ;
        // chaque groupe s'allume à son tour (l'onde avance).
        BuildBands(mesh, HeadMesh.Mouth, _headMouthBands);
        BuildBands(mesh, HeadMesh.Ear, _headEarBands);
        BuildBands(mesh, HeadMesh.Brain, _headBrainBands);

        // Pistes de circuit (avec une pastille au bout) et leurs impulsions.
        var traces = new StreamGeometry();
        using (var ctx = traces.Open())
        {
            foreach (var trace in HeadMesh.Traces)
            {
                ctx.BeginFigure(HeadPoint(trace[0]), false, false);
                foreach (var p in trace.Skip(1)) ctx.LineTo(HeadPoint(p), true, true);
            }
        }
        traces.Freeze();
        var traceEnds = new GeometryGroup();
        foreach (var trace in HeadMesh.Traces) traceEnds.Children.Add(new EllipseGeometry(HeadPoint(trace[^1]), HeadUnit * 0.012, HeadUnit * 0.012));
        traceEnds.Freeze();
        _headTraces = new Path
        {
            Data = new GeometryGroup { Children = { traces, traceEnds } },
            Stroke = _headLineBrush,
            StrokeThickness = HeadUnit * 0.006,
            StrokeLineJoin = PenLineJoin.Round,
        };
        _headContent.Children.Add(_headTraces);
        foreach (var trace in HeadMesh.Traces)
        {
            var cumulative = new double[trace.Length];
            for (var i = 1; i < trace.Length; i++) cumulative[i] = cumulative[i - 1] + trace[i].DistanceTo(trace[i - 1]);
            var pulse = new Ellipse
            {
                Width = HeadUnit * 0.03,
                Height = HeadUnit * 0.03,
                Fill = new RadialGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255)),
                Opacity = 0,
            };
            _headContent.Children.Add(pulse);
            _headTraceList.Add(new HeadTrace { Points = trace, Cumulative = cumulative, Pulse = pulse, Offset = _random.NextDouble() });
        }

        // Traits du visage (oreille, œil, sourcil), plus lumineux.
        var features = new StreamGeometry();
        using (var ctx = features.Open())
        {
            foreach (var stroke in HeadMesh.FeatureStrokes)
            {
                ctx.BeginFigure(HeadPoint(stroke[0]), false, false);
                foreach (var p in stroke.Skip(1)) ctx.LineTo(HeadPoint(p), true, true);
            }
        }
        features.Freeze();
        _headFeatures = new Path
        {
            Data = features,
            Stroke = _headBrightBrush,
            StrokeThickness = HeadUnit * 0.008,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = 0.75,
        };
        _headContent.Children.Add(_headFeatures);

        // Points du maillage, qui scintillent chacun à leur rythme.
        var nodeSize = HeadUnit * 0.016;
        for (var i = 0; i < mesh.Nodes.Count; i++)
        {
            if (i == mesh.EyeNode) continue;
            var p = HeadPoint(mesh.Nodes[i]);
            var shape = new Ellipse { Width = nodeSize, Height = nodeSize, Fill = _headNodeBrush };
            Canvas.SetLeft(shape, p.X - nodeSize / 2);
            Canvas.SetTop(shape, p.Y - nodeSize / 2);
            _headContent.Children.Add(shape);
            _headNodes.Add(new HeadNode { Shape = shape, Point = mesh.Nodes[i], Freq = 0.4 + _random.NextDouble() * 1.2, Phase = _random.NextDouble() * Math.PI * 2 });
        }

        // Œil : point très brillant qui cligne.
        var eyeSize = HeadUnit * 0.07;
        var eye = HeadPoint(HeadMesh.Eye);
        _headEye = new Ellipse
        {
            Width = eyeSize,
            Height = eyeSize,
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new GradientStop(Colors.White, 0),
                new GradientStop(Color.FromArgb(0xd0, 0xff, 0xff, 0xff), 0.25),
                _headEyeEdge,
            }),
            RenderTransform = _headEyeScale,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        Canvas.SetLeft(_headEye, eye.X - eyeSize / 2);
        Canvas.SetTop(_headEye, eye.Y - eyeSize / 2);
        _headContent.Children.Add(_headEye);
    }

    private static StreamGeometry EdgesGeometry(HeadMesh mesh, IEnumerable<(int A, int B)> edges)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            foreach (var (a, b) in edges)
            {
                ctx.BeginFigure(HeadPoint(mesh.Nodes[a]), false, false);
                ctx.LineTo(HeadPoint(mesh.Nodes[b]), true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private void BuildBands(HeadMesh mesh, MeshPoint source, Path[] bands)
    {
        var groups = new List<(int, int)>[HeadBands];
        for (var k = 0; k < HeadBands; k++) groups[k] = new List<(int, int)>();
        var distances = mesh.Edges.Select(e =>
        {
            var a = mesh.Nodes[e.A];
            var b = mesh.Nodes[e.B];
            return new MeshPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2).DistanceTo(source);
        }).ToList();
        var max = distances.Max();
        for (var i = 0; i < mesh.Edges.Count; i++)
            groups[Math.Min(HeadBands - 1, (int)(distances[i] / max * HeadBands))].Add(mesh.Edges[i]);
        for (var k = 0; k < HeadBands; k++)
        {
            bands[k] = new Path { Data = EdgesGeometry(mesh, groups[k]), Stroke = _headBrightBrush, StrokeThickness = HeadUnit * 0.008, Opacity = 0 };
            _headContent.Children.Add(bands[k]);
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

        // Regarde vers le centre de l'écran (retournement doux : elle « tourne la tête »).
        var screen = _screenDips;
        var faceTarget = screen.Width > 0 && Left + Width / 2 > screen.Left + screen.Width / 2 ? -1.0 : 1.0;
        _headFace += (faceTarget - _headFace) * (1 - Math.Exp(-dt * 3));
        var face = Math.Abs(_headFace) < 0.05 ? 0.05 * Math.Sign(faceTarget) : _headFace;

        // Place et taille : la tête occupe le carré de la bulle (un peu plus grand, elle est moins large).
        var size = Math.Min(b.Width, b.Height) * 1.18;
        var scale = size / HeadUnit;
        var matrix = Matrix.Identity;
        matrix.Translate(-0.53 * HeadUnit, -0.52 * HeadUnit);
        matrix.Scale(face * scale, scale);
        matrix.Translate(b.Left + b.Width / 2, b.Top + b.Height / 2);
        _headTransform.Matrix = matrix;
        _headGlowTransform.Matrix = matrix;
        if (HeadMode)
        {
            GlowPath.Data = _headGlowGeometry;
            GlowPath.Opacity *= 0.55;
        }

        // Couleurs : celles de l'état (repos, écoute, réflexion, parole), en plus clair pour les traits.
        var main = _shownColor;
        _headLineBrush.Color = Lerp(main, Colors.White, 0.25);
        _headBrightBrush.Color = Lerp(main, Colors.White, 0.6);
        _headNodeBrush.Color = Lerp(main, Colors.White, 0.75);
        _headFillTop.Color = WithAlpha(main, (byte)(40 + 40 * _level));
        _headFillBottom.Color = WithAlpha(main, 10);
        _headEyeEdge.Color = WithAlpha(Lerp(main, Colors.White, 0.3), 0);
        _headGlowLines!.Opacity = 0.35 + 0.3 * _level + 0.15 * _attention;
        _headBaseLines!.Opacity = 0.45 + 0.25 * _level - 0.15 * _sleepiness;

        // Ondes : parole (bouche), écoute (oreille), réflexion (cerveau).
        var speak = _speaking ? Math.Clamp(voice * 1.6, 0, 1) : 0;
        for (var k = 0; k < HeadBands; k++)
        {
            var fade = 1 - k * 0.15;
            _headMouthBands[k].Opacity = speak * fade * (0.5 + 0.5 * Math.Sin(t * 9 - k * 1.3));
            _headEarBands[k].Opacity = _attention * fade * Math.Max(0, Math.Sin(t * 6 - k * 1.2)) * 0.9;
            _headBrainBands[k].Opacity = _thinkingAmount * (0.15 + 0.6 * Math.Max(0, Math.Sin(t * 4 - k * 1.1)));
        }

        // Commande exécutée : un éclat balaie la tête d'avant en arrière.
        var sweep = (t - _shimmerStart) / ShimmerDuration;
        var sweepOn = sweep is >= 0 and <= 1 && _config.ShowReactions;
        var sweepX = 0.95 - sweep * 0.9;

        foreach (var node in _headNodes)
        {
            var twinkle = Math.Pow(Math.Max(0, Math.Sin(t * node.Freq * (1 + 2 * _thinkingAmount) + node.Phase)), 4);
            var opacity = 0.45 + 0.45 * twinkle + 0.3 * _level * Math.Max(0, 1 - node.Point.DistanceTo(HeadMesh.Mouth) * 3);
            if (sweepOn) opacity += Math.Exp(-Math.Pow((node.Point.X - sweepX) / 0.07, 2));
            node.Shape.Opacity = Math.Clamp(opacity * (1 - 0.4 * _sleepiness), 0, 1);
        }

        // Pistes : plus visibles en réflexion ; impulsions vers l'extérieur quand elle réfléchit
        // ou parle, vers la tête quand elle t'entend.
        _headTraces!.Opacity = 0.35 + 0.4 * _thinkingAmount + 0.2 * _attention;
        var pulseAmount = Math.Max(_thinkingAmount, Math.Max(_attention, _speakingAmount * 0.6));
        foreach (var trace in _headTraceList)
        {
            var progress = (t * (0.5 + 0.6 * _thinkingAmount) + trace.Offset) % 1.0;
            if (_attention > _thinkingAmount && !_speaking) progress = 1 - progress;
            var position = PointAlong(trace, progress);
            Canvas.SetLeft(trace.Pulse, position.X - trace.Pulse.Width / 2);
            Canvas.SetTop(trace.Pulse, position.Y - trace.Pulse.Height / 2);
            trace.Pulse.Opacity = pulseAmount * Math.Sin(Math.PI * progress);
        }

        // Œil : clignement de temps en temps, mi-clos quand elle s'endort, plus vif quand elle t'écoute.
        if (t > _nextHeadBlinkAt)
        {
            _headBlinkStart = t;
            _nextHeadBlinkAt = t + 2.5 + _random.NextDouble() * 4;
        }
        var blink = t - _headBlinkStart < 0.16 ? Math.Sin(Math.PI * (t - _headBlinkStart) / 0.16) : 0;
        _headEyeScale.ScaleY = Math.Max(0.08, (1 - 0.9 * blink) * (1 - 0.8 * _sleepiness));
        _headEyeScale.ScaleX = 1 + 0.25 * _attention + 0.2 * _level;
        _headEye!.Opacity = 0.75 + 0.25 * Math.Max(_attention, _level);
        _headFeatures!.Opacity = 0.6 + 0.3 * Math.Max(_attention, _level);
    }

    private static Point PointAlong(HeadTrace trace, double progress)
    {
        var target = progress * trace.Cumulative[^1];
        for (var i = 1; i < trace.Points.Length; i++)
        {
            if (trace.Cumulative[i] < target && i < trace.Points.Length - 1) continue;
            var segment = trace.Cumulative[i] - trace.Cumulative[i - 1];
            var f = segment <= 0 ? 0 : Math.Clamp((target - trace.Cumulative[i - 1]) / segment, 0, 1);
            var a = trace.Points[i - 1];
            var b = trace.Points[i];
            return HeadPoint(new MeshPoint(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f));
        }
        return HeadPoint(trace.Points[^1]);
    }
}
