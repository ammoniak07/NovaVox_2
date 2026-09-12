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
