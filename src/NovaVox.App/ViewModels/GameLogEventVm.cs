namespace NovaVox.App.ViewModels;

/// <summary>Une ligne du panneau "🛰 Game.log" (événements détectés en direct).</summary>
public sealed class GameLogEventVm
{
    public required string Time { get; init; }
    public required string Summary { get; init; }
}
