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

    [Fact]
    public void ParsePatchNotes_SplitsByVersionAndIgnoresHeaderAndSeparators()
    {
        var text = "NOVAVOX — Notes de mise à jour\n==============================\nv0.0.2\n------\n- Corrige un bug.\nv0.0.1\n------\n- Première version.\n";
        var versions = VersionUtil.ParsePatchNotes(text);

        Assert.Equal(2, versions.Count);
        Assert.Equal("0.0.2", versions[0].Version);
        Assert.Equal("Corrige un bug.", Assert.Single(versions[0].Items).Text);
        Assert.Equal("0.0.1", versions[1].Version);
        Assert.Equal("Première version.", Assert.Single(versions[1].Items).Text);
    }

    [Fact]
    public void ParsePatchNotes_IndentedBulletBecomesSubItem()
    {
        var text = "v1.0.0\n------\n- Fonctionnalité principale\n   - détail secondaire\n   - autre détail\n- Deuxième point\n";
        var versions = VersionUtil.ParsePatchNotes(text);

        var items = versions[0].Items;
        Assert.Equal(2, items.Count);
        Assert.Equal("Fonctionnalité principale", items[0].Text);
        Assert.Equal(new[] { "détail secondaire", "autre détail" }, items[0].Subs);
        Assert.Equal("Deuxième point", items[1].Text);
        Assert.Empty(items[1].Subs);
    }

    [Fact]
    public void ParsePatchNotes_ContinuationLineIsAppendedToLastItem()
    {
        var text = "v1.0.0\n------\n- Une phrase assez longue qui\ncontinue sur la ligne suivante sans tiret.\n";
        var versions = VersionUtil.ParsePatchNotes(text);

        Assert.Equal("Une phrase assez longue qui continue sur la ligne suivante sans tiret.",
            Assert.Single(versions[0].Items).Text);
    }

    [Fact]
    public void ParsePatchNotes_EmptyTextReturnsNoVersions()
    {
        Assert.Empty(VersionUtil.ParsePatchNotes(""));
    }
}
