using System.Text.RegularExpressions;

namespace NovaVox.Core.GameLog;

/// <summary>
/// Machine à états qui analyse une ligne du Game.log à la fois — port de
/// GameLogWatcher._process_line (game_log_watcher.py), séparé du fil de
/// lecture du fichier (voir <see cref="GameLogWatcher"/>) pour rester
/// testable indépendamment de l'I/O disque.
///
/// CONSTAT VÉRIFIÉ (15/08/2026, build 4.9 LIVE) : les kills/morts/
/// destructions de vaisseau ne sont plus détectables via le Game.log dans
/// cette version du jeu — voir game_log_watcher.py pour le détail. Cette
/// classe ne couvre donc que les changements de zone/destination et les
/// notifications HUD, comme l'original.
/// </summary>
public sealed partial class GameLogLineProcessor
{
    private const int HudNotificationMaxContinuationLines = 6;

    /// <summary>
    /// Candidat non vérifié pour le démarrage réel du saut quantique —
    /// désactivé par défaut, comme DEPARTURE_DETECTION_ENABLED côté
    /// Python, faute d'avoir pu confronter le pattern à un vrai log.
    /// </summary>
    public const bool DepartureDetectionEnabled = false;

    [GeneratedRegex(@"^<([\d\-T:.Z]+)>")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex("<Quantum Drive Arrived")]
    private static partial Regex QuantumArrivedRegex();

    [GeneratedRegex(
        @"CalculateRoute\|Projected Start Location is (?<start_location>.+?) for route to " +
        @"destination (?<destination>[A-Za-z0-9_\-]+)")]
    private static partial Regex RouteProjectedRegex();

    [GeneratedRegex(@"Successfully calculated route to (?<destination>[A-Za-z0-9_\-]+) fuel estimate")]
    private static partial Regex RouteCalculatedRegex();

    [GeneratedRegex(@"routing from .+? to (?<label>.+?) (?:Obstructing Entity|Routing around)")]
    private static partial Regex RouteObstructionRegex();

    [GeneratedRegex(
        @"OnPlayerSelectedQuantumTarget\|Player has selected point " +
        @"(?<destination>[A-Za-z0-9_\-]+) as their destination")]
    private static partial Regex TargetSelectedRegex();

    [GeneratedRegex(@"(RequestQuantumTravel|OnQuantumDriveEngaged|QuantumTravel.*Engag)")]
    private static partial Regex QuantumJumpEngagedRegex();

    [GeneratedRegex("nickname=\"(?<nickname>[^\"]+)\"")]
    private static partial Regex PlayerNicknameRegex();

    [GeneratedRegex("<SHUDEvent_OnNotification> Added notification \"(?<text>.*)$")]
    private static partial Regex HudNotificationStartRegex();

    [GeneratedRegex("^(?<text>.*?)\"\\s*\\[\\d+\\]")]
    private static partial Regex HudNotificationCloseRegex();

    public GameLogState State { get; } = new();

    /// <summary>Pseudo RSI déjà connu — évite d'émettre nickname_detected une fois renseigné.</summary>
    public string? PlayerName { get; set; }

    private string? _pendingObstructionLabel;
    private string? _pendingStartLocation;
    private string? _lastRouteDestination;
    private string? _lastObstructionLabel;
    private string? _lastStartLocation;
    private (string? Destination, string? ObstructionLabel)? _lastRouteSignature;
    private string? _pendingNotification;

    /// <summary>Traite une ligne et retourne l'événement détecté, ou null si rien à signaler.</summary>
    public GameLogEvent? ProcessLine(string line)
    {
        if (_pendingNotification is not null)
            return ProcessNotificationContinuation(line);

        var hudStart = HudNotificationStartRegex().Match(line);
        if (hudStart.Success)
        {
            var rawText = hudStart.Groups["text"].Value.TrimEnd('\n');
            var closeMatch = HudNotificationCloseRegex().Match(rawText);
            if (closeMatch.Success)
            {
                var text = GameLogText.FixMojibake(closeMatch.Groups["text"].Value.Trim());
                return new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = text };
            }
            _pendingNotification = rawText;
            return null;
        }

        var targetSelected = TargetSelectedRegex().Match(line);
        if (targetSelected.Success)
        {
            _pendingObstructionLabel = null;
            _pendingStartLocation = null;
            return null;
        }

        var routeProjected = RouteProjectedRegex().Match(line);
        if (routeProjected.Success)
        {
            _pendingStartLocation = routeProjected.Groups["start_location"].Value.Trim();
            return null;
        }

        var routeObstruction = RouteObstructionRegex().Match(line);
        if (routeObstruction.Success)
        {
            _pendingObstructionLabel = routeObstruction.Groups["label"].Value.Trim();
            return null;
        }

        var routeCalculated = RouteCalculatedRegex().Match(line);
        if (routeCalculated.Success)
            return ProcessRouteCalculated(routeCalculated);

        if (DepartureDetectionEnabled && QuantumJumpEngagedRegex().IsMatch(line))
        {
            return new GameLogEvent
            {
                Type = GameLogEventTypes.JumpStart,
                Destination = _lastRouteDestination,
                ObstructionLabel = _lastObstructionLabel,
                StartLocation = _lastStartLocation,
            };
        }

        if (QuantumArrivedRegex().IsMatch(line))
        {
            State.CurrentZone = _lastRouteDestination;
            State.Connected = true;
            return new GameLogEvent
            {
                Type = GameLogEventTypes.ZoneChange,
                Zone = _lastRouteDestination,
                ObstructionLabel = _lastObstructionLabel,
                StartLocation = _lastStartLocation,
            };
        }

        if (string.IsNullOrEmpty(PlayerName))
        {
            var nickname = PlayerNicknameRegex().Match(line);
            if (nickname.Success)
                return new GameLogEvent { Type = GameLogEventTypes.NicknameDetected, Nickname = nickname.Groups["nickname"].Value };
        }

        return null;
    }

    private GameLogEvent? ProcessNotificationContinuation(string line)
    {
        var continuation = TimestampRegex().Replace(line, "", 1);
        var closeMatch = HudNotificationCloseRegex().Match(continuation);
        if (closeMatch.Success)
        {
            _pendingNotification += "\n" + closeMatch.Groups["text"].Value;
            var text = GameLogText.FixMojibake(_pendingNotification!.Trim());
            _pendingNotification = null;
            return new GameLogEvent { Type = GameLogEventTypes.HudNotification, Text = text };
        }

        _pendingNotification += "\n" + continuation.TrimEnd('\n');
        if (_pendingNotification!.Count(c => c == '\n') > HudNotificationMaxContinuationLines)
            _pendingNotification = null; // motif de fermeture jamais apparu : on abandonne plutôt que d'accumuler indéfiniment.
        return null;
    }

    private GameLogEvent? ProcessRouteCalculated(Match routeCalculated)
    {
        var destination = routeCalculated.Groups["destination"].Value;
        var obstructionLabel = _pendingObstructionLabel;
        var startLocation = _pendingStartLocation;
        _lastRouteDestination = destination;
        _lastObstructionLabel = obstructionLabel;
        _lastStartLocation = startLocation;

        var signature = (destination, obstructionLabel);
        if (_lastRouteSignature is { } prev && prev.Destination == signature.destination && prev.ObstructionLabel == signature.obstructionLabel)
        {
            return null; // même destination déjà annoncée : on ne répète pas
        }
        _lastRouteSignature = signature;

        return new GameLogEvent
        {
            Type = GameLogEventTypes.RouteSet,
            Destination = destination,
            ObstructionLabel = obstructionLabel,
            StartLocation = startLocation,
        };
    }
}
