using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class SchemaImagesTests
{
    [Theory]
    [InlineData("5CA 'Akura'", "5ca-akura")]
    [InlineData("A03 \"Canuto\" Sniper Rifle", "a03-canuto-sniper-rifle")]
    [InlineData("10-Series Greatsword Cannon", "10-series-greatsword-cannon")]
    public void FileStem_MatchesGeneratedFileNames(string name, string expected) =>
        Assert.Equal(expected, SchemaImages.FileStem(name));

    [Fact]
    public void FindPath_ReturnsExistingImageOrNull()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(baseDir, SchemaImages.FolderName));
        try
        {
            var png = Path.Combine(baseDir, SchemaImages.FolderName, "ezra.png");
            File.WriteAllBytes(png, new byte[] { 1 });

            Assert.Equal(png, SchemaImages.FindPath(baseDir, "Ezra"));
            Assert.Null(SchemaImages.FindPath(baseDir, "Crossfield"));
        }
        finally
        {
            Directory.Delete(baseDir, true);
        }
    }
}
