namespace NovaVox.Core.Commands;

/// <summary>
/// Une étape supplémentaire jouée après l'action principale d'une commande :
/// un simple appui bref (pas de maintien/répétition individuels), précédé
/// du délai <see cref="DelayBefore"/> (secondes).
/// </summary>
public sealed class ExtraStep
{
    public string Keys { get; set; } = "";
    public double DelayBefore { get; set; }
}

/// <summary>
/// Une ligne de la liste de commandes : soit un titre de groupe
/// (<see cref="Type"/> == "title"), soit une vraie commande vocale.
/// </summary>
public sealed class VoiceCommand
{
    public string Type { get; set; } = "command";
    public string Phrase { get; set; } = "";
    public List<string> Synonyms { get; set; } = new();
    public string Keys { get; set; } = "";
    public bool Hold { get; set; }
    public int RepeatCount { get; set; } = 1;
    public double RepeatDelay { get; set; } = 0.1;
    public List<ExtraStep> ExtraSteps { get; set; } = new();
    /// <summary>Titre replié dans l'interface (masque les commandes du groupe) — uniquement significatif pour un titre, ignoré pour une commande.</summary>
    public bool Collapsed { get; set; }

    public bool IsTitle => Type == "title";
}

public sealed class CommandProfile
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<VoiceCommand> Commands { get; set; } = new();
}

/// <summary>
/// Réglages liés au JEU rattachés à un profil (pas aux commandes elles-
/// mêmes) : bascule automatiquement en changeant de profil, ex. un
/// profil "Star Citizen" (surveillance du Game.log, wiki Gemini, zone
/// affichée dans l'overlay) vs un profil "autre jeu" générique (tout ça
/// désactivé — masque même les points d'entrée Game.log de l'interface,
/// voir MainWindow.RefreshGameLogStatus ; l'image de fond suit le même
/// interrupteur par convention de nom de fichier, voir
/// MainWindow.LoadPanelsBackgroundImage, pas stocké ici). Stocké dans le
/// fichier du profil (profiles/&lt;id&gt;.json, clé "game") à côté de ses
/// commandes — voir CommandStore.ReadProfile/WriteProfile.
/// </summary>
public sealed class ProfileGameSettings
{
    public bool GameLogEnabled { get; set; } = true;
    public bool GeminiWikiEnabled { get; set; } = true;
    /// <summary>Mêmes clés que OverlayConfig.RowKeys — vide = tout visible (comportement par défaut inchangé).</summary>
    public Dictionary<string, bool> OverlayVisibleRows { get; set; } = new();
}
