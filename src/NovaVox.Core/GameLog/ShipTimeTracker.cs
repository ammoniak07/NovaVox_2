namespace NovaVox.Core.GameLog;

/// <summary>
/// Accumulateur du temps passé dans chaque vaisseau, à partir des
/// évènements d'entrée/sortie du canal de bord (voir
/// GameLogAnnouncer.TryExtractShipChannelEvent) — une instance par session
/// continue (surveillance en direct, ou un seul fichier Game.log archivé ;
/// jamais partagée entre deux fichiers, comme les autres machines à états
/// de ce scan, voir GameLogBackups.ScanFile).
/// </summary>
public sealed class ShipTimeTracker
{
    /// <summary>Vaisseau actuellement occupé, ou null hors de tout vaisseau suivi.</summary>
    public string? CurrentShip { get; private set; }

    private DateTimeOffset? _anchor;

    /// <summary>
    /// Traite un évènement d'entrée (<paramref name="entered"/> = true) ou
    /// de sortie détecté à l'horodatage <paramref name="ts"/>. Retourne le
    /// vaisseau et la durée (secondes) à créditer si un intervalle se
    /// termine — une sortie, ou une entrée dans un AUTRE vaisseau sans
    /// sortie vue entre-temps (ex. changement direct de vaisseau sans
    /// notification de sortie du précédent) — sinon null.
    /// </summary>
    public (string Ship, double Seconds)? Process(DateTimeOffset ts, string shipName, bool entered)
    {
        var closed = CloseCurrentInterval(ts);
        if (entered)
        {
            CurrentShip = shipName;
            _anchor = ts;
        }
        else
        {
            CurrentShip = null;
            _anchor = null;
        }
        return closed;
    }

    /// <summary>
    /// Clôt l'intervalle en cours jusqu'à <paramref name="ts"/> SANS changer
    /// de vaisseau — pour créditer une session encore ouverte (ex. appel
    /// périodique pendant la surveillance en direct, ou fin d'un fichier
    /// Game.log archivé jamais suivi d'une notification de sortie).
    /// Redémarre l'ancre à <paramref name="ts"/> : un appel ultérieur ne
    /// recrédite donc jamais le même intervalle deux fois.
    /// </summary>
    public (string Ship, double Seconds)? Flush(DateTimeOffset ts)
    {
        var closed = CloseCurrentInterval(ts);
        if (CurrentShip is not null) _anchor = ts;
        return closed;
    }

    private (string Ship, double Seconds)? CloseCurrentInterval(DateTimeOffset ts)
    {
        if (CurrentShip is null || _anchor is null) return null;
        var elapsed = (ts - _anchor.Value).TotalSeconds;
        if (elapsed <= 0) return null;
        return (CurrentShip, elapsed);
    }
}
