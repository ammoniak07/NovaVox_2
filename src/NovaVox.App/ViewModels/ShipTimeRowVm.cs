namespace NovaVox.App.ViewModels;

/// <summary>
/// Une ligne du panneau "📊 Statistiques" (temps passé par vaisseau) — port
/// direct d'une entrée de AiConfig.ShipTimeSeconds, juste mise en forme
/// pour l'affichage (voir FormatDuration).
/// </summary>
public sealed class ShipTimeRowVm
{
    public required string ShipName { get; init; }
    public required double TotalSeconds { get; init; }
    public string FormattedDuration => FormatDuration(TotalSeconds);

    private static string FormatDuration(double totalSeconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} h {span.Minutes:D2} min";
        if (span.TotalMinutes >= 1) return $"{span.Minutes} min {span.Seconds:D2} s";
        return $"{span.Seconds} s";
    }
}
