using NovaVox.Core.Avatar;
using Xunit;

namespace NovaVox.Core.Tests.Avatar;

public class HeadMeshTests
{
    private static readonly HeadMesh Mesh = HeadMesh.Default;

    [Fact]
    public void HasAReasonableNumberOfPointsAndLines()
    {
        // Assez pour ressembler à un maillage, pas trop pour rester lisible en petit et léger à animer.
        Assert.InRange(Mesh.Nodes.Count, 70, 160);
        Assert.InRange(Mesh.Edges.Count, 150, 450);
        Assert.NotEmpty(Mesh.Triangles);
    }

    [Fact]
    public void AllPointsAreInsideTheProfile()
    {
        foreach (var node in Mesh.Nodes)
            Assert.True(HeadMesh.Inside(node) || HeadMesh.DistanceToOutline(node) < 0.005, $"point hors du profil : {node}");
    }

    [Fact]
    public void LinesStayInsideTheProfile()
    {
        // Aucune ligne ne traverse un creux du profil (sous le nez, menton, cou).
        foreach (var (a, b) in Mesh.Edges)
        {
            var pa = Mesh.Nodes[a];
            var pb = Mesh.Nodes[b];
            var mid = new MeshPoint((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2);
            Assert.True(HeadMesh.Inside(mid) || HeadMesh.DistanceToOutline(mid) < 0.005, $"ligne hors du profil : {pa} → {pb}");
        }
    }

    [Fact]
    public void MeshIsInOnePiece()
    {
        var neighbours = Enumerable.Range(0, Mesh.Nodes.Count).ToDictionary(i => i, _ => new List<int>());
        foreach (var (a, b) in Mesh.Edges) { neighbours[a].Add(b); neighbours[b].Add(a); }
        var seen = new HashSet<int> { 0 };
        var queue = new Queue<int>(new[] { 0 });
        while (queue.Count > 0)
            foreach (var n in neighbours[queue.Dequeue()])
                if (seen.Add(n)) queue.Enqueue(n);
        Assert.Equal(Mesh.Nodes.Count, seen.Count);
    }

    [Fact]
    public void EyeIsAMeshPoint()
    {
        Assert.Equal(HeadMesh.Eye, Mesh.Nodes[Mesh.EyeNode]);
        Assert.True(HeadMesh.Inside(HeadMesh.Mouth) || HeadMesh.DistanceToOutline(HeadMesh.Mouth) < 0.02);
        Assert.True(HeadMesh.Inside(HeadMesh.Ear));
        Assert.True(HeadMesh.Inside(HeadMesh.Brain));
    }
}
