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
    public void ScanForReceivedSchemas_MinTimestamp_ExcludesNotificationsBeforeCutoff()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[]
            {
                Notification("Schémas reçu : Ezra", ts: "2026-09-19T12:00:00.000Z"), // avant le wipe
                Notification("Schémas reçu : Deadbolt IV Cannon", id: 2, ts: "2026-09-20T12:00:00.000Z"), // après le wipe
            });

        var found = GameLogBackups.ScanForReceivedSchemas(backups, minTimestamp: DateTimeOffset.Parse("2026-09-20T00:00:00Z"));

        Assert.DoesNotContain("Ezra", found);
        Assert.Contains("Deadbolt IV Cannon", found);
    }

    [Fact]
    public void ScanForReceivedSchemas_MinTimestamp_NotificationExactlyAtCutoff_IsIncluded()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[] { Notification("Schémas reçu : Ezra", ts: "2026-09-20T00:00:00.000Z") });

        var found = GameLogBackups.ScanForReceivedSchemas(backups, minTimestamp: DateTimeOffset.Parse("2026-09-20T00:00:00Z"));

        Assert.Contains("Ezra", found);
    }

    [Fact]
    public void ScanForReceivedSchemas_NoMinTimestamp_IncludesEverything()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[] { Notification("Schémas reçu : Ezra", ts: "2020-01-01T00:00:00.000Z") });

        var found = GameLogBackups.ScanForReceivedSchemas(backups);

        Assert.Contains("Ezra", found);
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

        GameLogBackups.ScanForReceivedSchemas(backups, progress: progress);

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
    public void ScanForStats_EntersAndLeavesSameShip_CreditsElapsedSeconds()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[]
            {
                Notification("CANAL 'Drake Cutter : Ammoniak' rejoint.", ts: "2026-09-20T18:00:00.000Z"),
                Notification("Vous avez quitté le CANAL 'Drake Cutter : Ammoniak'.", ts: "2026-09-20T18:01:30.000Z"),
            });

        var result = GameLogBackups.ScanForStats(backups);

        Assert.Equal(90.0, result.ShipSecondsByShip["Drake Cutter"]);
        Assert.Single(result.ScannedFileNames);
    }

    [Fact]
    public void ScanForStats_OpenShipIntervalAtEndOfFile_CreditsUpToLastKnownTimestamp()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[]
            {
                Notification("CANAL 'Drake Cutter : Ammoniak' rejoint.", ts: "2026-09-20T18:00:00.000Z"),
                "<2026-09-20T18:05:00.000Z> [Notice] <SomeOtherLine> rien à voir, juste pour avancer l'horloge",
            });

        var result = GameLogBackups.ScanForStats(backups);

        Assert.Equal(300.0, result.ShipSecondsByShip["Drake Cutter"]);
    }

    [Fact]
    public void ScanForStats_IndependentAcrossFiles_NeverCarriesAnOpenShipIntervalOver()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[] { Notification("CANAL 'Drake Cutter : Ammoniak' rejoint.", ts: "2026-09-20T18:00:00.000Z") });
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(2) 02 Jun 18 (11 00 00).log"),
            new[] { Notification("CANAL 'Anvil Paladin : Ammoniak' rejoint.", ts: "2026-09-21T09:00:00.000Z") });

        var result = GameLogBackups.ScanForStats(backups);

        // Chaque fichier n'a vu qu'une entrée sans sortie ni autre ligne après : rien à créditer (pas de confusion entre les deux vaisseaux de fichiers différents).
        Assert.Empty(result.ShipSecondsByShip);
        Assert.Equal(2, result.ScannedFileNames.Count);
    }

    [Fact]
    public void ScanForStats_AlreadyScannedFile_IsSkippedAndNotRecounted()
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

        var result = GameLogBackups.ScanForStats(backups, alreadyScannedFileNames: new HashSet<string> { fileName });

        Assert.Empty(result.ShipSecondsByShip);
        Assert.Empty(result.ScannedFileNames);
    }

    [Fact]
    public void ScanForStats_MissingFolder_ReturnsEmptyRatherThanThrowing()
    {
        var result = GameLogBackups.ScanForStats(Path.Combine(_dir, "does-not-exist"));

        Assert.Empty(result.ShipSecondsByShip);
        Assert.Empty(result.DestinationVisitCounts);
        Assert.Equal(0, result.PlayTimeSeconds);
        Assert.Equal(0, result.AuecSent);
        Assert.Empty(result.ScannedFileNames);
    }

    [Fact]
    public void ScanForStats_PlayTime_IsSpanBetweenFirstAndLastTimestampInFile()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[]
            {
                "<2026-09-20T18:00:00.000Z> [Notice] <Something> première ligne de la session",
                "<2026-09-20T19:30:00.000Z> [Notice] <Something> dernière ligne de la session",
            });

        var result = GameLogBackups.ScanForStats(backups);

        Assert.Equal(TimeSpan.FromMinutes(90).TotalSeconds, result.PlayTimeSeconds);
    }

    [Fact]
    public void ScanForStats_PlayTime_AccumulatesAcrossMultipleFiles()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[]
            {
                "<2026-09-20T18:00:00.000Z> [Notice] <Something> a",
                "<2026-09-20T18:30:00.000Z> [Notice] <Something> b",
            });
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(2) 02 Jun 18 (11 00 00).log"),
            new[]
            {
                "<2026-09-21T09:00:00.000Z> [Notice] <Something> a",
                "<2026-09-21T10:00:00.000Z> [Notice] <Something> b",
            });

        var result = GameLogBackups.ScanForStats(backups);

        Assert.Equal(TimeSpan.FromMinutes(90).TotalSeconds, result.PlayTimeSeconds);
    }

    [Fact]
    public void ScanForStats_AuecSent_AccumulatesAcrossNotifications()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[]
            {
                Notification("Vous avez envoyé Droz64: 2,000,000 aUEC", ts: "2026-09-20T18:00:00.000Z"),
                Notification("Vous avez envoyé Zeilos: 500 aUEC", ts: "2026-09-20T18:05:00.000Z"),
            });

        var result = GameLogBackups.ScanForStats(backups);

        Assert.Equal(2_000_500.0, result.AuecSent);
    }

    [Fact]
    public void ScanForStats_DestinationVisitCounts_CountsZoneChangesByResolvedName()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[]
            {
                "<2026-09-30T06:38:32.439Z> [Notice] <RequestLocationInventory> Player[Ammoniak] requested inventory for Location[RR_P6_L5] [Team_CoreGameplayFeatures][Inventory]",
            });

        var result = GameLogBackups.ScanForStats(backups);

        Assert.Equal(1, result.DestinationVisitCounts["Megumi Ravitaillement"]);
    }

    [Fact]
    public void ScanForStats_GroupPlayerCounts_CountsEachGroupJoinByPlayer()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[]
            {
                Notification("Un joueur a rejoint Bistic a rejoint le Groupe.", ts: "2026-09-20T18:00:00.000Z"),
                Notification("Un joueur a rejoint Bistic a rejoint le Groupe.", id: 2, ts: "2026-09-21T18:00:00.000Z"),
                Notification("Un joueur a rejoint Tinou214 a rejoint le Groupe.", id: 3, ts: "2026-09-22T18:00:00.000Z"),
            });

        var result = GameLogBackups.ScanForStats(backups);

        Assert.Equal(2, result.GroupPlayerCounts["Bistic"]);
        Assert.Equal(1, result.GroupPlayerCounts["Tinou214"]);
    }

    [Fact]
    public void ScanForStats_GroupPlayerCounts_IgnoresConnectAndDisconnectNotifications()
    {
        var backups = Directory.CreateDirectory(Path.Combine(_dir, "logbackups")).FullName;
        File.WriteAllLines(
            Path.Combine(backups, "Game Build(1) 01 Jun 18 (10 09 04).log"),
            new[]
            {
                Notification("Groupe : Dionico31 s'est connecté.", ts: "2026-09-20T18:00:00.000Z"),
                Notification("Groupe : Dionico31 s'est déconnecté.", id: 2, ts: "2026-09-20T18:05:00.000Z"),
                Notification("A quitté le groupe : Tork a quitté le Groupe", id: 3, ts: "2026-09-20T18:10:00.000Z"),
            });

        var result = GameLogBackups.ScanForStats(backups);

        Assert.Empty(result.GroupPlayerCounts);
    }
}
