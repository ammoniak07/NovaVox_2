using NovaVox.Core;
using Xunit;

namespace NovaVox.Core.Tests;

public class AppLogTests
{
    [Fact]
    public void Append_WritesTimestampedLineToDailyFileInLogFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novavox_applog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AppLog.Append(dir, "Test unitaire.", "info");
            var expectedPath = Path.Combine(dir, "Log", $"novavox_{DateTime.Now:yyyy-MM-dd}.txt");
            Assert.True(File.Exists(expectedPath));
            var content = File.ReadAllText(expectedPath);
            Assert.Contains("[INFO] Test unitaire.", content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Append_IncludesKindInUppercase()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novavox_applog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AppLog.Append(dir, "Quelque chose a échoué.", "error");
            var content = File.ReadAllText(Path.Combine(dir, "Log", $"novavox_{DateTime.Now:yyyy-MM-dd}.txt"));
            Assert.Contains("[ERROR] Quelque chose a échoué.", content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AppendException_IncludesFullExceptionDetails()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novavox_applog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Exception caught;
            try { throw new InvalidOperationException("boom"); }
            catch (Exception ex) { caught = ex; }

            AppLog.AppendException(dir, "Exception non gérée", caught);
            var content = File.ReadAllText(Path.Combine(dir, "Log", $"novavox_{DateTime.Now:yyyy-MM-dd}.txt"));
            Assert.Contains("[ERROR] Exception non gérée : ", content);
            Assert.Contains("InvalidOperationException", content);
            Assert.Contains("boom", content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
