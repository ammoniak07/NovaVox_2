using System.Globalization;
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
    /// Fenêtre anti-rafale pour les notifications HUD : vérifié en vrai
    /// Game.log, un aller-retour de connectivité ("CommLink Restauré" /
    /// "CommLink hors service") peut spammer des centaines de lignes
    /// "Added notification" en alternance sur plus d'une minute (bug
    /// réseau côté client, pas un vrai évènement à annoncer à chaque
    /// occurrence). Une même annonce (texte identique) revenant dans cette
    /// fenêtre depuis sa DERNIÈRE occurrence (pas la première) est
    /// ignorée — donc silencieuse tant que le flapping continue plus vite
    /// que cet intervalle, mais réarmée dès qu'un vrai calme revient (ex.
    /// une reconnexion isolée, des minutes plus tard, s'annonce à nouveau
    /// normalement).
    /// </summary>
    private static readonly TimeSpan HudNotificationRepeatSuppressWindow = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Candidat non vérifié pour le démarrage réel du saut quantique —
    /// désactivé par défaut, comme DEPARTURE_DETECTION_ENABLED côté
    /// Python, faute d'avoir pu confronter le pattern à un vrai log.
    /// </summary>
    public const bool DepartureDetectionEnabled = false;

    [GeneratedRegex(@"^<(?<ts>[\d\-T:.Z]+)>")]
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

    /// <summary>
    /// CORRECTIF (vrai Game.log fourni par l'utilisateur, 30/09/2026) :
    /// "Legacy login response" ne contient PAS de champ Location[...] dans
    /// cette version du jeu ("User Login Success - Handle[...] - Time[...]"
    /// seulement) — la première tentative de détecter la zone de spawn via
    /// cette ligne ne matchait donc jamais, et l'overlay restait affiché sur
    /// la dernière zone connue d'une session précédente (ex. un point de
    /// saut "Pyro Gateway"), plutôt que le vrai lieu de spawn ("Megumi
    /// Ravitaillement"). La ligne réellement fiable et automatique à la
    /// connexion (vérifiée dans le vrai log) est <RequestLocationInventory>,
    /// émise dès que le jeu récupère l'inventaire du lieu où le personnage
    /// apparaît — son identifiant brut (ex. "RR_P6_L5") passe par le même
    /// mécanisme de résolution que les destinations de saut quantique (voir
    /// GameLogDestinations.NormalizeKnownIdSynonyms). Elle n'est PAS limitée
    /// au spawn : rouvrir l'inventaire d'une station déjà visitée (ATM,
    /// terminal...) la réémet aussi pour le même lieu (constaté en vrai
    /// Game.log, 01/10/2026) — voir le garde sur spawnLocation ==
    /// State.CurrentZone plus bas, qui évite de réannoncer "Arrivée à" à
    /// chaque réémission.
    /// </summary>
    [GeneratedRegex(@"<RequestLocationInventory> Player\[[^\]]+\] requested inventory for Location\[(?<location>[A-Za-z0-9_]+)\]")]
    private static partial Regex RequestLocationInventoryRegex();

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
    private readonly Dictionary<string, DateTimeOffset> _lastHudNotificationSeenAt = new();

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
                return BuildHudNotificationEvent(text, line);
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

        var locationInventory = RequestLocationInventoryRegex().Match(line);
        if (locationInventory.Success)
        {
            var spawnLocation = locationInventory.Groups["location"].Value;
            State.Connected = true;
            // <RequestLocationInventory> n'est pas émise qu'une fois au spawn
            // comme le laissait penser le constat initial (voir le commentaire
            // sur RequestLocationInventoryRegex) : ouvrir un inventaire de
            // station (ATM, terminal...) la réémet aussi, pour le MÊME lieu,
            // confirmé en vrai Game.log (répétitions à quelques secondes
            // d'intervalle, le joueur n'ayant pas bougé entre-temps) — sans ce
            // garde, chaque ouverture spammait une nouvelle annonce "Arrivée
            // à" déjà faite pour ce lieu.
            if (spawnLocation == State.CurrentZone) return null;
            State.CurrentZone = spawnLocation;
            return new GameLogEvent { Type = GameLogEventTypes.ZoneChange, Zone = spawnLocation };
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
            return BuildHudNotificationEvent(text, line);
        }

        _pendingNotification += "\n" + continuation.TrimEnd('\n');
        if (_pendingNotification!.Count(c => c == '\n') > HudNotificationMaxContinuationLines)
            _pendingNotification = null; // motif de fermeture jamais apparu : on abandonne plutôt que d'accumuler indéfiniment.
        return null;
    }

    /// <summary>
    /// Construit l'évènement hud_notification pour <paramref name="text"/>,
    /// SAUF si ce même texte (déjà nettoyé) vient d'être annoncé il y a
    /// moins de <see cref="HudNotificationRepeatSuppressWindow"/> — voir ce
    /// champ pour le cas vérifié qui a motivé ce filtre (spam "CommLink
    /// Restauré"/"CommLink hors service"). Le délai est mesuré sur
    /// l'horodatage du Game.log lui-même (pas l'horloge de la machine) :
    /// robuste même si plusieurs lignes en rafale sont lues d'un coup bien
    /// après avoir été écrites (ex. démarrage de NovaVox après une
    /// longue absence, gros retard de lecture).
    /// </summary>
    private GameLogEvent? BuildHudNotificationEvent(string text, string sourceLine)
    {
        var timestamp = ParseLineTimestamp(sourceLine);
        if (timestamp is { } ts)
        {
            if (_lastHudNotificationSeenAt.TryGetValue(text, out var lastSeen)
                && ts - lastSeen < HudNotificationRepeatSuppressWindow)
            {
                _lastHudNotificationSeenAt[text] = ts;
                return null;
            }
            _lastHudNotificationSeenAt[text] = ts;
        }
        return new GameLogEvent
        {
            Type = GameLogEventTypes.HudNotification,
            Text = text,
            // Horodatage réel de la ligne plutôt que l'instant de traitement (par
            // défaut sur GameLogEvent.Ts) : sans effet perceptible en direct (traité
            // à quelques ms de l'écriture), mais indispensable pour le suivi du
            // temps passé par vaisseau (voir ShipTimeTracker) lors d'un scan
            // rétroactif des archives Game.log, où l'instant de traitement n'a
            // aucun rapport avec quand l'évènement a vraiment eu lieu.
            Ts = (timestamp ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds() / 1000.0,
        };
    }

    /// <summary>Horodatage &lt;...&gt; en tête de ligne (UTC) — public car réutilisé par GameLogBackups pour suivre la dernière activité connue d'une archive (voir ScanForShipTimes).</summary>
    public static DateTimeOffset? ParseLineTimestamp(string line)
    {
        var match = TimestampRegex().Match(line);
        if (!match.Success) return null;
        return DateTimeOffset.TryParse(
            match.Groups["ts"].Value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts)
            ? ts
            : null;
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
