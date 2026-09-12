using NovaVox.Core.Update;
using Xunit;

namespace NovaVox.Core.Tests.Update;

public class VersionUtilTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novavox-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("0.9", "0.10", -1)] // comparaison numérique, pas lexicographique
    [InlineData("0.2.8", "0.2.8", 0)]
    [InlineData("1.2.0", "1.2", 1)] // tuple plus long avec un composant supplémentaire non nul
    [InlineData("1.2", "1.2.0", -1)]
    public void CompareVersions_MatchesPythonTupleSemantics(string a, string b, int expectedSign)
    {
        var result = VersionUtil.CompareVersions(a, b);
        Assert.Equal(Math.Sign(expectedSign), Math.Sign(result));
    }

    [Fact]
    public void IsNewer_TrueWhenRemoteGreater()
    {
        Assert.True(VersionUtil.IsNewer("0.3.0", "0.2.8"));
        Assert.False(VersionUtil.IsNewer("0.2.8", "0.2.8"));
        Assert.False(VersionUtil.IsNewer("0.2.0", "0.2.8"));
    }

    [Fact]
    public void GetAppVersion_ReadsFirstVersionLine()
    {
        var path = Path.Combine(_dir, "patch_maj.txt");
        File.WriteAllText(path, "Quelques notes\nv1.4.2 - dernière version\nv1.4.1 - précédente\n");
        Assert.Equal("1.4.2", VersionUtil.GetAppVersion(path));
    }

    [Fact]
    public void GetAppVersion_FallsBackWhenFileMissing()
    {
        Assert.Equal(VersionUtil.FallbackVersion, VersionUtil.GetAppVersion(Path.Combine(_dir, "missing.txt")));
    }

    [Fact]
    public void GetPatchNotes_ReturnsFriendlyMessageWhenMissing()
    {
        Assert.Equal("Aucune note de mise à jour disponible pour le moment.", VersionUtil.GetPatchNotes(Path.Combine(_dir, "missing.txt")));
    }

    [Fact]
    public void GetPatchNotes_ReturnsTrimmedContent()
    {
        var path = Path.Combine(_dir, "patch_maj.txt");
        File.WriteAllText(path, "  v1.0.0 - notes  \n");
        Assert.Equal("v1.0.0 - notes", VersionUtil.GetPatchNotes(path));
    }
}
