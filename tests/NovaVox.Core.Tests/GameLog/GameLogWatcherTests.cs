using System.Collections.Concurrent;
using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class GameLogWatcherTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novavox-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Watcher_IgnoresExistingContentAndTailsNewLines()
    {
        var path = Path.Combine(_dir, "Game.log");
        File.WriteAllText(path, "<2026-08-15T16:00:00.000Z> old line before start\n");

        var events = new ConcurrentQueue<GameLogEvent>();
        using var watcher = new GameLogWatcher(onEvent: events.Enqueue, logPath: path);
        watcher.Start();

        // Laisse le watcher démarrer et se positionner en fin de fichier.
        await WaitUntil(() => events.Any(e => e.Type == GameLogEventTypes.WatcherStarted));

        await File.AppendAllTextAsync(path,
            "<2026-08-15T16:00:01.000Z> ...Successfully calculated route to Foo fuel estimate 1.0\n");

        var routeEvent = await WaitUntil(() => events.FirstOrDefault(e => e.Type == GameLogEventTypes.RouteSet));
        Assert.NotNull(routeEvent);
        Assert.Equal("Foo", routeEvent!.Destination);

        watcher.Stop();
    }

    [Fact]
    public async Task Watcher_EmitsErrorWhenLogFileMissing()
    {
        var events = new ConcurrentQueue<GameLogEvent>();
        using var watcher = new GameLogWatcher(onEvent: events.Enqueue, logPath: Path.Combine(_dir, "does-not-exist.log"));
        watcher.Start();

        var errorEvent = await WaitUntil(() => events.FirstOrDefault(e => e.Type == GameLogEventTypes.WatcherError));
        Assert.NotNull(errorEvent);
    }

    private static async Task<GameLogEvent?> WaitUntil(Func<GameLogEvent?> probe, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var result = probe();
            if (result is not null) return result;
            await Task.Delay(25);
        }
        return null;
    }

    private static async Task WaitUntil(Func<bool> probe, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (probe()) return;
            await Task.Delay(25);
        }
        throw new TimeoutException("Condition never became true.");
    }
}
