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
        Assert.InRange(Model.Nodes.Count, 1500, 2600);
        Assert.InRange(Model.Triangles.Count, 3000, 5000);
        Assert.Equal(468, Model.FaceNodeCount); // visage MediaPipe complet
        Assert.InRange(Model.Edges.Count, 4500, 7500);
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
    public void Speaking_MovesOnlyTheLips()
    {
        var closed = new ProjectedNode[Model.Nodes.Count];
        var open = new ProjectedNode[Model.Nodes.Count];
        Model.Project(0, 0, 0, 0, closed);
        Model.Project(0, 0, 0, 1, open);
        // Repères MediaPipe (premiers sommets du maillage) : 14 lèvre du bas, 13 lèvre du haut, 152 menton, 1 nez.
        double Move(int i) => open[i].Y - closed[i].Y; // y écran vers le bas
        Assert.InRange(Move(14), 0.03, 0.06);   // la lèvre du bas descend un peu
        Assert.True(Move(13) < 0);              // la lèvre du haut remonte légèrement
        Assert.InRange(Move(152), -0.001, 0.02); // le menton bouge à peine
        Assert.Equal(closed[1].Y, open[1].Y, 6); // le nez ne bouge pas
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

    [Fact]
    public void PointCloud_CoversTheHeadAndLightsUpTheFace()
    {
        var cloud = HeadModel3D.PointCloud;
        Assert.InRange(cloud.Positions.Length, 4000, 10000);
        Assert.Equal(cloud.Positions.Length, cloud.Normals.Length);
        // Normales unitaires.
        for (var i = 0; i < cloud.Positions.Length; i++)
        {
            Assert.InRange(cloud.Normals[i].Length, 0.99, 1.01);
        }
        // Les yeux brillent, le crâne non.
        var eye = Enumerable.Range(0, cloud.Positions.Length).OrderBy(i => cloud.Positions[i].DistanceTo(HeadModel3D.RightEye)).First();
        var skull = Enumerable.Range(0, cloud.Positions.Length).OrderBy(i => cloud.Positions[i].DistanceTo(new Vec3(0, 0.7, -0.5))).First();
        Assert.True(cloud.Glow[eye] > 0.5);
        Assert.True(cloud.Glow[skull] < 0.05);
    }

    [Fact]
    public void Mesh_IsClosedExceptAtTheNeck()
    {
        // Chaque arête borde deux triangles (pas de trou entre le visage et le crâne),
        // sauf le bas du cou et le bord des oreilles.
        var open = Enumerable.Range(0, Model.Edges.Count).Where(e => Model.EdgeTriangles[e].Length == 1).ToList();
        foreach (var e in open)
        {
            var (a, b) = Model.Edges[e];
            var bottom = Model.Nodes[a].Y < -1.1 && Model.Nodes[b].Y < -1.1;
            var ear = Math.Abs(Model.Nodes[a].X) > 0.45 && Math.Abs(Model.Nodes[b].X) > 0.45 && a >= Model.FaceNodeCount;
            Assert.True(bottom || ear, $"trou entre {Model.Nodes[a]} et {Model.Nodes[b]}");
        }
    }

    [Fact]
    public void Mesh_TrianglesFaceOutward()
    {
        // Sens cohérent : vue de face, presque tous les triangles du visage sont visibles.
        var projected = new ProjectedNode[Model.Nodes.Count];
        Model.Project(0, 0, 0, 0, projected);
        var faceTriangles = Enumerable.Range(0, Model.Triangles.Count)
            .Where(t => Model.Triangles[t].A < Model.FaceNodeCount && Model.Triangles[t].B < Model.FaceNodeCount && Model.Triangles[t].C < Model.FaceNodeCount)
            .ToList();
        var visible = faceTriangles.Count(t => Model.FacesViewer(t, projected));
        Assert.True(visible > faceTriangles.Count * 0.85, $"{visible}/{faceTriangles.Count}");
    }
}
