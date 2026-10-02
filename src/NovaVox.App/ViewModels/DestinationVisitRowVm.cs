namespace NovaVox.App.ViewModels;

/// <summary>
/// Une ligne de la liste "Destinations les plus visitées" du panneau
/// "📊 Statistiques" — port direct d'une entrée de
/// AiConfig.DestinationVisitCounts, juste mise en forme pour l'affichage.
/// </summary>
public sealed class DestinationVisitRowVm
{
    public required string DestinationName { get; init; }
    public required int VisitCount { get; init; }
    public string FormattedCount => VisitCount == 1 ? "1 visite" : $"{VisitCount} visites";
}
