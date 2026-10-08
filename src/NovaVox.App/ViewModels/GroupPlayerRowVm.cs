namespace NovaVox.App.ViewModels;

/// <summary>
/// Une ligne de la liste "Joueurs les plus groupés" du panneau
/// "📊 Statistiques" — port direct d'une entrée de
/// AiConfig.GroupPlayerCounts, juste mise en forme pour l'affichage.
/// </summary>
public sealed class GroupPlayerRowVm
{
    public required string PlayerName { get; init; }
    public required int JoinCount { get; init; }
    public string FormattedCount => JoinCount == 1 ? "1 session" : $"{JoinCount} sessions";
}
