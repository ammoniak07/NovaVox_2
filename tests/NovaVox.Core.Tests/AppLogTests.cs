using NovaVox.Core;
using Xunit;

namespace NovaVox.Core.Tests;

public class AppLogTests
{
    // AppLog.SessionFileName n'est calculé qu'une seule fois pour tout le
    // process (un nouveau fichier par LANCEMENT de l'appli, pas par appel)
    // : chaque test utilise donc son propre dossier temporaire mais
    // retrouve le fichier par motif plutôt que de prédire son nom exact,
    // qui dépend de l'instant du tout premier appel à Append dans ce
    // process de test (potentiellement un test précédent).

    private static string FindSessionLogFile(string dir)
    {
        var files = Directory.GetFiles(Path.Combine(dir, "Log"), "novavox_*.txt");
        Assert.Single(files);
        return files[0];
    }

    [Fact]
    public void Append_WritesTimestampedLineToSessionFileInLogFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novavox_applog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AppLog.Append(dir, "Test unitaire.", "info");
            var content = File.ReadAllText(FindSessionLogFile(dir));
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
            var content = File.ReadAllText(FindSessionLogFile(dir));
            Assert.Contains("[ERROR] Quelque chose a échoué.", content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Append_SameProcessReusesSameSessionFileAcrossCalls()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novavox_applog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AppLog.Append(dir, "premier message", "info");
            AppLog.Append(dir, "second message", "info");
            var content = File.ReadAllText(FindSessionLogFile(dir));
            Assert.Contains("premier message", content);
            Assert.Contains("second message", content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PruneOldLogs_KeepsOnlyMostRecentFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novavox_applog_" + Guid.NewGuid().ToString("N"));
        var logDir = Path.Combine(dir, "Log");
        Directory.CreateDirectory(logDir);
        try
        {
            var files = new List<string>();
            for (var i = 0; i < 5; i++)
            {
                var path = Path.Combine(logDir, $"novavox_fake_{i}.txt");
                File.WriteAllText(path, "x");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-i)); // fichier 0 = le plus récent
                files.Add(path);
            }

            AppLog.PruneOldLogs(dir, keep: 3);

            var remaining = Directory.GetFiles(logDir, "*.txt");
            Assert.Equal(3, remaining.Length);
            Assert.Contains(files[0], remaining);
            Assert.Contains(files[1], remaining);
            Assert.Contains(files[2], remaining);
            Assert.DoesNotContain(files[3], remaining);
            Assert.DoesNotContain(files[4], remaining);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PruneOldLogs_DoesNothingWhenUnderLimit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novavox_applog_" + Guid.NewGuid().ToString("N"));
        var logDir = Path.Combine(dir, "Log");
        Directory.CreateDirectory(logDir);
        try
        {
            File.WriteAllText(Path.Combine(logDir, "novavox_fake_0.txt"), "x");
            File.WriteAllText(Path.Combine(logDir, "novavox_fake_1.txt"), "x");

            AppLog.PruneOldLogs(dir, keep: 20);

            Assert.Equal(2, Directory.GetFiles(logDir, "*.txt").Length);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PruneOldLogs_MissingLogFolder_DoesNotThrow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "novavox_applog_" + Guid.NewGuid().ToString("N"));
        AppLog.PruneOldLogs(dir, keep: 20); // dossier Log/ inexistant : ne doit jamais lever
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
            var content = File.ReadAllText(FindSessionLogFile(dir));
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
