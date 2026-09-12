using NovaVox.Core;
using Xunit;

namespace NovaVox.Core.Tests;

public class ErrorLogTests
{
    [Fact]
    public void Append_WritesTimestampedLineToErreursLog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novavox_errorlog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            ErrorLog.Append(dir, "[Erreur] Test unitaire.");
            var path = Path.Combine(dir, "erreurs.log");
            Assert.True(File.Exists(path));
            var content = File.ReadAllText(path);
            Assert.Contains("[Erreur] Test unitaire.", content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Append_ResetsFileOnceItGrowsTooLarge()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novavox_errorlog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "erreurs.log");
            File.WriteAllBytes(path, new byte[1_100_000]);
            ErrorLog.Append(dir, "après rotation");
            var content = File.ReadAllText(path);
            Assert.Contains("après rotation", content);
            Assert.True(new FileInfo(path).Length < 1_100_000);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
