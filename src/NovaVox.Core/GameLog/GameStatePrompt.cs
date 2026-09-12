namespace NovaVox.Core.GameLog;

public static class GameStatePrompt
{
    /// <summary>
    /// Construit un petit paragraphe factuel à ajouter au prompt système
    /// de l'IA — port de game_state_to_prompt_block (game_log_watcher.py).
    /// Chaîne vide si rien d'utile n'est encore connu.
    /// </summary>
    public static string ToPromptBlock(GameLogState? state)
    {
        if (state is null) return "";
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(state.CurrentZone))
            parts.Add($"Destination/zone la plus récente : {state.CurrentZone}");
        if (parts.Count == 0) return "";
        return "\n\nÉtat de la partie en cours (issu du Game.log en temps réel) :\n- " + string.Join("\n- ", parts);
    }
}
