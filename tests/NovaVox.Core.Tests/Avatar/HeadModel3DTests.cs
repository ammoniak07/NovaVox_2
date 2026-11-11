using NovaVox.Core.Avatar;
using Xunit;

namespace NovaVox.Core.Tests.Avatar;

public class HeadModel3DTests
{
    private static readonly HeadModel3D Model = HeadModel3D.Default;

    [Fact]
    public void HasAReasonableNumberOfPointsAndLines()
    {
        // Assez pour un visage lisible, pas trop pour rester léger à animer à chaque image.
        Assert.InRange(Model.Nodes.Count, 1200, 1800);
        Assert.InRange(Model.Triangles.Count, 2800, 3200);
        Assert.InRange(Model.Edges.Count, 4000, 5000);
        Assert.All(Model.EdgeTriangles, t => Assert.InRange(t.Length, 1, 2));
    }

    [Fact]
    public void FacingFront_NoseIsClosestAndFrontTrianglesFaceTheViewer()
    {
        var projected = new ProjectedNode[Model.Nodes.Count];
        Model.Project(0, 0, 0, 0, projected);
        var closest = Enumerable.Range(0, Model.Nodes.Count).OrderByDescending(i => projected[i].Depth).First();
        Assert.True(Model.Nodes[closest].Z > 0.6, "le point le plus proche doit être sur le visage");

        // La moitié avant (z > 0) est majoritairement visible, l'arrière majoritairement cachée.
        int frontVisible = 0, front = 0, backVisible = 0, backCount = 0;
        for (var t = 0; t < Model.Triangles.Count; t++)
        {
            var (a, b, c) = Model.Triangles[t];
            var z = (Model.Nodes[a].Z + Model.Nodes[b].Z + Model.Nodes[c].Z) / 3;
            var visible = Model.FacesViewer(t, projected);
            if (z > 0.2) { front++; if (visible) frontVisible++; }
            if (z < -0.2) { backCount++; if (visible) backVisible++; }
        }
        Assert.True(frontVisible > front * 0.9, $"{frontVisible}/{front} triangles avant visibles");
        Assert.True(backVisible < backCount * 0.1, $"{backVisible}/{backCount} triangles arrière visibles");
    }

    [Fact]
    public void TurningRight_MovesTheFaceToTheRight()
    {
        var front = new ProjectedNode[Model.Nodes.Count];
        var turned = new ProjectedNode[Model.Nodes.Count];
        Model.Project(0, 0, 0, 0, front);
        Model.Project(0.8, 0, 0, 0, turned);
        var nose = Enumerable.Range(0, Model.Nodes.Count).OrderByDescending(i => front[i].Depth).First();
        Assert.True(turned[nose].X > front[nose].X + 0.2);
    }

    [Fact]
    public void OpeningTheJaw_MovesTheChinDownButNotTheEyes()
    {
        var closed = new ProjectedNode[Model.Nodes.Count];
        var open = new ProjectedNode[Model.Nodes.Count];
        Model.Project(0, 0, 0, 0, closed);
        Model.Project(0, 0, 0, 1, open);
        var chin = Enumerable.Range(0, Model.Nodes.Count)
            .Where(i => Model.Nodes[i].Y < -0.5 && Model.Nodes[i].Y > -0.7)
            .OrderByDescending(i => Model.Nodes[i].Z).First();
        Assert.True(open[chin].Y > closed[chin].Y + 0.05, "le menton descend (y écran vers le bas)");
        for (var i = 0; i < Model.Nodes.Count; i++)
            if (Model.Nodes[i].Y > 0) Assert.Equal(closed[i].Y, open[i].Y, 6);
    }

    [Fact]
    public void FaceFeatures_VisibleFromFrontHiddenFromBehind()
    {
        Assert.True(HeadModel3D.SurfaceVisibility(HeadModel3D.LeftEye, 0, 0) > 0.9);
        Assert.Equal(0, HeadModel3D.SurfaceVisibility(HeadModel3D.LeftEye, Math.PI, 0));
        Assert.NotEmpty(Model.FeatureStrokes);
    }
}
