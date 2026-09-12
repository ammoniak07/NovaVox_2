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

    /// <summary>
    /// Déclenchement manuel optionnel — combinaison clavier ou bouton
    /// joystick (encodage "joy:{...}", voir JoystickHotkeyCodec) qui
    /// exécute cette commande directement, indépendamment de la
    /// reconnaissance vocale (fonctionnalité propre au port .NET, sans
    /// équivalent côté app.py).
    /// </summary>
    public string? TriggerHotkey { get; set; }

    public bool IsTitle => Type == "title";
}

public sealed class CommandProfile
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<VoiceCommand> Commands { get; set; } = new();
}
