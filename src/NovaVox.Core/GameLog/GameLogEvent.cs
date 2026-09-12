namespace NovaVox.Core.GameLog;

public static class GameLogEventTypes
{
    public const string WatcherStarted = "watcher_started";
    public const string WatcherError = "watcher_error";
    public const string HudNotification = "hud_notification";
    public const string RouteSet = "route_set";
    public const string JumpStart = "jump_start";
    public const string ZoneChange = "zone_change";
    public const string NicknameDetected = "nickname_detected";
}

public sealed class GameLogEvent
{
    public required string Type { get; init; }
    public double Ts { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    public string? Message { get; init; }
    public string? Text { get; init; }
    public string? Destination { get; init; }
    public string? ObstructionLabel { get; init; }
    public string? StartLocation { get; init; }
    public string? Zone { get; init; }
    public string? Nickname { get; init; }
}

/// <summary>
/// État courant (dernier système/zone connu, compteurs de session) — port
/// de GameLogWatcher.state (game_log_watcher.py), pensé pour être injecté
/// dans le prompt système de l'IA (voir GameStatePrompt).
/// </summary>
public sealed class GameLogState
{
    public bool Connected { get; set; }
    public string? CurrentZone { get; set; }
    public string? CurrentShip { get; set; }
    public int SessionKills { get; set; }
    public int SessionDeaths { get; set; }
    public int SessionDestructions { get; set; }
    public string? LastEventSummary { get; set; }

    public GameLogState Clone() => new()
    {
        Connected = Connected,
        CurrentZone = CurrentZone,
        CurrentShip = CurrentShip,
        SessionKills = SessionKills,
        SessionDeaths = SessionDeaths,
        SessionDestructions = SessionDestructions,
        LastEventSummary = LastEventSummary,
    };
}
