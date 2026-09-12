using System.Text;

namespace NovaVox.Core.GameLog;

/// <summary>
/// "Tail" du Game.log dans un fil dédié, sans jamais bloquer ni ralentir
/// le reste de NovaVox. Port de la classe GameLogWatcher (thread) de
/// game_log_watcher.py ; la logique d'analyse ligne par ligne vit dans
/// <see cref="GameLogLineProcessor"/>.
/// </summary>
public sealed class GameLogWatcher : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(0.5);

    private readonly GameLogLineProcessor _processor = new();
    private readonly Action<GameLogEvent> _onEvent;
    private readonly Action<string>? _onDebugLine;
    private readonly string? _configuredLogPath;
    private readonly object _stateLock = new();
    private CancellationTokenSource? _cts;
    private Task? _task;

    public GameLogWatcher(
        Action<GameLogEvent>? onEvent = null, string? logPath = null,
        Action<string>? onDebugLine = null, string? playerName = null)
    {
        _onEvent = onEvent ?? (_ => { });
        _onDebugLine = onDebugLine;
        _configuredLogPath = logPath;
        _processor.PlayerName = string.IsNullOrWhiteSpace(playerName) ? null : playerName.Trim();
    }

    public GameLogState GetState()
    {
        lock (_stateLock) return _processor.State.Clone();
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => Run(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    /// <summary>Attend la fin du fil (tests uniquement — la surveillance réelle tourne indéfiniment).</summary>
    public Task WaitAsync() => _task ?? Task.CompletedTask;

    private void Run(CancellationToken token)
    {
        var path = _configuredLogPath ?? GameLogPaths.FindGameLogPath();
        if (path is null)
        {
            Emit(new GameLogEvent { Type = GameLogEventTypes.WatcherError, Message = "Game.log introuvable" });
            return;
        }

        Emit(new GameLogEvent { Type = GameLogEventTypes.WatcherStarted, Message = $"Surveillance de {path}" });

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // On se positionne à la fin du fichier existant : pas de replay
            // d'une session précédente au démarrage, seulement les nouveaux
            // événements à partir de maintenant.
            stream.Seek(0, SeekOrigin.End);
        }
        catch (IOException e)
        {
            Emit(new GameLogEvent { Type = GameLogEventTypes.WatcherError, Message = e.Message });
            return;
        }

        using (stream)
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            long lastSize = SafeFileSize(path) ?? 0;

            while (!token.IsCancellationRequested)
            {
                var line = reader.ReadLine();
                if (line is null)
                {
                    Thread.Sleep(PollInterval);

                    // Détecte une rotation/reset du log (relance du jeu) :
                    // si le fichier a rapetissé, on se replace au début.
                    var currentSize = SafeFileSize(path);
                    if (currentSize is null) continue;
                    if (currentSize < lastSize)
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                        reader.DiscardBufferedData();
                    }
                    lastSize = currentSize.Value;
                    continue;
                }

                _onDebugLine?.Invoke(line);
                var evt = _processor.ProcessLine(line);
                if (evt is not null) Emit(evt);
            }
        }
    }

    private static long? SafeFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void Emit(GameLogEvent evt)
    {
        lock (_stateLock) _processor.State.LastEventSummary = evt.Type;
        try
        {
            _onEvent(evt);
        }
        catch
        {
            // Un callback défaillant ne doit jamais interrompre la surveillance du log.
        }
    }

    public void Dispose() => Stop();
}
