using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class GameLogBackupsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novavox-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Notification(string text, int id = 1, string ts = "2026-09-20T18:30:31.900Z") =>
        $"<{ts}> [Notice] <SHUDEvent_OnNotification> Added notification \"{text}\" [{id}] to queue. New queue size: 1, MissionId: [x]";

    [Fact]
    public void FindBackupsFolder_SiblingExists_ReturnsItsPath()
    {
        var gameLogPath = Path.Combine(_dir, "Game.log");
        File.WriteAllText(gameLogPath, "");
        Directory.CreateDirectory(Path.Combine(_dir, "logbackups"));

        var backups = GameLogBackups.FindBackupsFolder(gameLogPath);

        Assert.Equal(Path.Combine(_dir, "logbackups"), backups);
    }

    [Fact]
    public void FindBackupsFolder_NoSiblingFolder_ReturnsNull()
    {
        var gameLogPath = Path.Combine(_dir, "Game.log");
        File.WriteAllText(gameLogPath, "");

        Assert.Null(GameLogBackups.FindBackupsFolder(gameLogPath));
    }

    [Fact]
    public void FindBackupsFolder_NullLiveLogPath_ReturnsNull()
    {
        Assert.Null(GameLogBackups.FindBackupsFolder(null));
    }

    [Fact]
    public void ScanForReceivedSchemas_FindsNamesAcrossMultipleArchiveFiles()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllText(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            Notification("Schémas reçu : Ezra") + "\n");
        File.WriteAllText(
            Path.Combine(backups, "Game Build(2) 02 Jun 18 (11 00 00).log"),
            Notification("Schémas reçu : Deadbolt IV Cannon") + "\n");

        var found = GameLogBackups.ScanForReceivedSchemas(backups);

        Assert.Contains("Ezra", found);
        Assert.Contains("Deadbolt IV Cannon", found);
    }

    [Fact]
    public void ScanForReceivedSchemas_SameNameTwiceAcrossFiles_ReturnedOnlyOnce()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllText(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            Notification("Schémas reçu : Ezra") + "\n");
        File.WriteAllText(
            Path.Combine(backups, "Game Build(2) 02 Jun 18 (11 00 00).log"),
            Notification("Schémas reçu : Ezra") + "\n");

        var found = GameLogBackups.ScanForReceivedSchemas(backups);

        Assert.Single(found, name => name == "Ezra");
    }

    [Fact]
    public void ScanForReceivedSchemas_UnrelatedNotifications_ReturnsEmpty()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllText(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            Notification("Nouvel objectif : Livrer la cargaison") + "\n");

        Assert.Empty(GameLogBackups.ScanForReceivedSchemas(backups));
    }

    [Fact]
    public void ScanForReceivedSchemas_IgnoresNonLogFiles()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllText(Path.Combine(backups, "readme.txt"), Notification("Schémas reçu : Ezra"));

        Assert.Empty(GameLogBackups.ScanForReceivedSchemas(backups));
    }

    [Fact]
    public void ScanForReceivedSchemas_MissingFolder_ReturnsEmptyRatherThanThrowing()
    {
        Assert.Empty(GameLogBackups.ScanForReceivedSchemas(Path.Combine(_dir, "does-not-exist")));
    }

    [Fact]
    public void ScanForReceivedSchemas_ReportsProgressAfterEachFile()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllText(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            Notification("Schémas reçu : Ezra") + "\n");
        File.WriteAllText(
            Path.Combine(backups, "Game Build(2) 02 Jun 18 (11 00 00).log"),
            Notification("Schémas reçu : Deadbolt IV Cannon") + "\n");

        var reports = new List<(int Done, int Total)>();
        var progress = new SynchronousProgress<(int Done, int Total)>(reports.Add);

        GameLogBackups.ScanForReceivedSchemas(backups, progress);

        Assert.Equal(2, reports.Count);
        Assert.Equal((1, 2), reports[0]);
        Assert.Equal((2, 2), reports[1]);
    }

    /// <summary>IProgress&lt;T&gt; invoque normalement via le SynchronizationContext capturé
    /// à la création (asynchrone sur WPF) — ici on veut une notification synchrone immédiate
    /// pour pouvoir vérifier l'ordre exact des rapports dans un test xUnit.</summary>
    private sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    [Fact]
    public void ResolveBackupsFolder_CustomPathExists_TakesPriorityOverAutoDetection()
    {
        var custom = Directory.CreateDirectory(Path.Combine(_dir, "mes-archives")).FullName;
        var gameLogPath = Path.Combine(_dir, "Game.log");
        File.WriteAllText(gameLogPath, "");
        Directory.CreateDirectory(Path.Combine(_dir, "logbackups")); // existe aussi, mais ne doit pas gagner

        var resolved = GameLogBackups.ResolveBackupsFolder(custom, gameLogPath);

        Assert.Equal(custom, resolved);
    }

    [Fact]
    public void ResolveBackupsFolder_CustomPathMissing_ReturnsNullRatherThanFallingBack()
    {
        var gameLogPath = Path.Combine(_dir, "Game.log");
        File.WriteAllText(gameLogPath, "");
        Directory.CreateDirectory(Path.Combine(_dir, "logbackups")); // existe, mais le chemin manuel explicite est prioritaire

        var resolved = GameLogBackups.ResolveBackupsFolder(Path.Combine(_dir, "does-not-exist"), gameLogPath);

        Assert.Null(resolved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveBackupsFolder_NoCustomPath_FallsBackToAutoDetection(string? customPath)
    {
        var gameLogPath = Path.Combine(_dir, "Game.log");
        File.WriteAllText(gameLogPath, "");
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;

        var resolved = GameLogBackups.ResolveBackupsFolder(customPath, gameLogPath);

        Assert.Equal(backups, resolved);
    }

    [Fact]
    public void ScanForShipTimes_EntersAndLeavesSameShip_CreditsElapsedSeconds()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[]
            {
                Notification("CANAL 'Drake Cutter : Ammoniak' rejoint.", ts: "2026-09-20T18:00:00.000Z"),
                Notification("Vous avez quitté le CANAL 'Drake Cutter : Ammoniak'.", ts: "2026-09-20T18:01:30.000Z"),
            });

        var result = GameLogBackups.ScanForShipTimes(backups);

        Assert.Equal(90.0, result.SecondsByShip["Drake Cutter"]);
        Assert.Single(result.ScannedFileNames);
    }

    [Fact]
    public void ScanForShipTimes_OpenIntervalAtEndOfFile_CreditsUpToLastKnownTimestamp()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[]
            {
                Notification("CANAL 'Drake Cutter : Ammoniak' rejoint.", ts: "2026-09-20T18:00:00.000Z"),
                "<2026-09-20T18:05:00.000Z> [Notice] <SomeOtherLine> rien à voir, juste pour avancer l'horloge",
            });

        var result = GameLogBackups.ScanForShipTimes(backups);

        Assert.Equal(300.0, result.SecondsByShip["Drake Cutter"]);
    }

    [Fact]
    public void ScanForShipTimes_IndependentAcrossFiles_NeverCarriesAnOpenIntervalOver()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[] { Notification("CANAL 'Drake Cutter : Ammoniak' rejoint.", ts: "2026-09-20T18:00:00.000Z") });
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(2) 02 Jun 18 (11 00 00).log"),
            new[] { Notification("CANAL 'Anvil Paladin : Ammoniak' rejoint.", ts: "2026-09-21T09:00:00.000Z") });

        var result = GameLogBackups.ScanForShipTimes(backups);

        // Chaque fichier n'a vu qu'une entrée sans sortie ni autre ligne après : rien à créditer (pas de confusion entre les deux vaisseaux de fichiers différents).
        Assert.Empty(result.SecondsByShip);
        Assert.Equal(2, result.ScannedFileNames.Count);
    }

    [Fact]
    public void ScanForShipTimes_AlreadyScannedFile_IsSkippedAndNotRecounted()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        const string fileName = "Game Build(1) 01 Jun 18 (10 09 04).log";
        File.WriteAllLines(
            Path.Combine(backups, fileName),
            new[]
            {
                Notification("CANAL 'Drake Cutter : Ammoniak' rejoint.", ts: "2026-09-20T18:00:00.000Z"),
                Notification("Vous avez quitté le CANAL 'Drake Cutter : Ammoniak'.", ts: "2026-09-20T18:01:30.000Z"),
            });

        var result = GameLogBackups.ScanForShipTimes(backups, alreadyScannedFileNames: new HashSet<string> { fileName });

        Assert.Empty(result.SecondsByShip);
        Assert.Empty(result.ScannedFileNames);
    }

    [Fact]
    public void ScanForShipTimes_MissingFolder_ReturnsEmptyRatherThanThrowing()
    {
        var result = GameLogBackups.ScanForShipTimes(Path.Combine(_dir, "does-not-exist"));

        Assert.Empty(result.SecondsByShip);
        Assert.Empty(result.ScannedFileNames);
    }
}
