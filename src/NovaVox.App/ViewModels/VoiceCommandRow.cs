using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NovaVox.Core.Commands;

namespace NovaVox.App.ViewModels;

/// <summary>
/// Wrapper observable d'une ligne de la liste de commandes (commande ou
/// titre de groupe) pour le data binding WPF — NovaVox.Core.Commands.
/// VoiceCommand reste un modèle "plat" volontairement indépendant de
/// l'UI.
/// </summary>
public sealed class VoiceCommandRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool _isTitle;
    public bool IsTitle
    {
        get => _isTitle;
        set { _isTitle = value; Raise(); Raise(nameof(IsCommand)); }
    }

    public bool IsCommand => !IsTitle;

    private string _phrase = "";
    public string Phrase { get => _phrase; set { _phrase = value; Raise(); } }

    private string _keys = "";
    public string Keys { get => _keys; set { _keys = value; Raise(); Raise(nameof(KeysDisplayWithBadges)); } }

    private bool _hold;
    public bool Hold { get => _hold; set { _hold = value; Raise(); Raise(nameof(KeysDisplayWithBadges)); } }

    private int _repeatCount = 1;
    public int RepeatCount { get => _repeatCount; set { _repeatCount = value; Raise(); Raise(nameof(KeysDisplayWithBadges)); } }

    /// <summary>Touche(s) + petits indicateurs maintien/répétition — port des badges ⏱/🔁N à côté du "keycap" (script.js).</summary>
    public string KeysDisplayWithBadges
    {
        get
        {
            var suffix = "";
            if (Hold) suffix += " ⏱";
            if (RepeatCount > 1) suffix += $" 🔁{RepeatCount}";
            return Keys + suffix;
        }
    }

    private double _repeatDelay = 0.1;
    public double RepeatDelay { get => _repeatDelay; set { _repeatDelay = value; Raise(); } }

    private bool _speakOnKeyPress;
    /// <summary>Case "Lire" : Nova lit la phrase quand le joueur appuie lui-même sur la touche de la commande.</summary>
    public bool SpeakOnKeyPress { get => _speakOnKeyPress; set { _speakOnKeyPress = value; Raise(); } }

    private string _speakJoystickButton = "";
    /// <summary>Bouton de joystick qui déclenche aussi la lecture (en plus de la touche clavier), encodé par JoystickHotkeyCodec — vide si aucun.</summary>
    public string SpeakJoystickButton
    {
        get => _speakJoystickButton;
        set { _speakJoystickButton = value ?? ""; Raise(); Raise(nameof(SpeakJoystickLabel)); }
    }

    private bool _speakJoystickCapturing;
    /// <summary>Détection du bouton en cours (affichage seulement, jamais persisté).</summary>
    public bool SpeakJoystickCapturing
    {
        get => _speakJoystickCapturing;
        set { _speakJoystickCapturing = value; Raise(); Raise(nameof(SpeakJoystickLabel)); }
    }

    private string _gameJoystickHint = "";
    /// <summary>Bouton(s) de joystick trouvés dans les assignations du jeu pour la même action ("js1 bouton 5") — calculé, jamais persisté.</summary>
    public string GameJoystickHint
    {
        get => _gameJoystickHint;
        set { if (_gameJoystickHint == (value ?? "")) return; _gameJoystickHint = value ?? ""; Raise(); Raise(nameof(SpeakJoystickLabel)); Raise(nameof(SpeakJoystickTooltip)); }
    }

    public string SpeakJoystickLabel =>
        SpeakJoystickCapturing ? "🕹 appuie…"
        : NovaVox.Core.Hotkeys.JoystickHotkeyCodec.Decode(SpeakJoystickButton) is { Button: { } button } ? $"🕹 {button}"
        : GameJoystickHint.Length > 0 ? $"🕹 {GameJoystickHint} (jeu)"
        : "🕹 +";

    public string SpeakJoystickTooltip =>
        "Bouton de joystick qui déclenche aussi la lecture (clic : choisir le bouton, clic droit : le retirer)."
        + (GameJoystickHint.Length > 0 ? $"\nTrouvé automatiquement dans les assignations de Star Citizen : {GameJoystickHint}." : "");

    public ObservableCollection<string> Synonyms { get; } = new();

    /// <summary>Suite de touches jouées après l'action principale (jusqu'à CommandStore.MaxCommandExtraSteps) — voir AddExtraStep_Click/OpenKeyboardForExtraStep (MainWindow.xaml.cs).</summary>
    public ObservableCollection<ExtraStep> ExtraSteps { get; } = new();

    private bool _synonymsExpanded;
    /// <summary>État d'affichage (repliée/dépliée) de la liste de synonymes — jamais persisté, purement pour l'UI (voir toggle-syn, script.js).</summary>
    public bool SynonymsExpanded { get => _synonymsExpanded; set { _synonymsExpanded = value; Raise(); Raise(nameof(SynonymsCountLabel)); } }

    public bool HasSynonyms => Synonyms.Count > 0;
    public string SynonymsCountLabel => $"{(SynonymsExpanded ? "▾" : "▸")} {Synonyms.Count} syn";

    private bool _extraStepsExpanded;
    /// <summary>État d'affichage (repliée/dépliée) de la liste d'étapes supplémentaires — jamais persisté, purement pour l'UI (même principe que SynonymsExpanded).</summary>
    public bool ExtraStepsExpanded { get => _extraStepsExpanded; set { _extraStepsExpanded = value; Raise(); Raise(nameof(ExtraStepsCountLabel)); } }

    public bool HasExtraSteps => ExtraSteps.Count > 0;
    public string ExtraStepsCountLabel => $"{(ExtraStepsExpanded ? "▾" : "▸")} {ExtraSteps.Count} étape(s)";

    private bool _isDropTargetTop;
    /// <summary>État d'affichage (survol pendant un glisser) — jamais persisté, purement pour l'UI.</summary>
    public bool IsDropTargetTop { get => _isDropTargetTop; set { _isDropTargetTop = value; Raise(); } }

    private bool _isDropTargetBottom;
    public bool IsDropTargetBottom { get => _isDropTargetBottom; set { _isDropTargetBottom = value; Raise(); } }

    private bool _rowVisible = true;
    /// <summary>Visibilité effective de la ligne dans CommandsList — recalculée par MainWindow.RefreshCommandsVisibility (recherche + repli de groupe), jamais persistée ni touchée ici directement.</summary>
    public bool RowVisible { get => _rowVisible; set { _rowVisible = value; Raise(); } }

    private bool _isCollapsed;
    /// <summary>Titre replié : masque les commandes du groupe jusqu'au titre suivant (voir RefreshCommandsVisibility, MainWindow.xaml.cs) — persisté (VoiceCommand.Collapsed), significatif seulement pour un titre.</summary>
    public bool IsCollapsed { get => _isCollapsed; set { _isCollapsed = value; Raise(); Raise(nameof(CollapseGlyph)); } }

    public string CollapseGlyph => IsCollapsed ? "▸" : "▾";

    public VoiceCommandRow()
    {
        Synonyms.CollectionChanged += (_, _) => { Raise(nameof(HasSynonyms)); Raise(nameof(SynonymsCountLabel)); };
        ExtraSteps.CollectionChanged += (_, _) => { Raise(nameof(HasExtraSteps)); Raise(nameof(ExtraStepsCountLabel)); };
    }

    public static VoiceCommandRow FromModel(VoiceCommand cmd)
    {
        var row = new VoiceCommandRow
        {
            IsTitle = cmd.IsTitle,
            Phrase = cmd.Phrase,
            Keys = cmd.Keys,
            Hold = cmd.Hold,
            RepeatCount = cmd.RepeatCount,
            RepeatDelay = cmd.RepeatDelay,
            IsCollapsed = cmd.Collapsed,
            SpeakOnKeyPress = cmd.SpeakOnKeyPress,
            SpeakJoystickButton = cmd.SpeakJoystickButton,
        };
        foreach (var s in cmd.Synonyms) row.Synonyms.Add(s);
        foreach (var step in cmd.ExtraSteps) row.ExtraSteps.Add(step);
        return row;
    }

    public VoiceCommand ToModel() => new()
    {
        Type = IsTitle ? "title" : "command",
        Phrase = Phrase,
        Keys = Keys,
        Hold = Hold,
        RepeatCount = RepeatCount,
        RepeatDelay = RepeatDelay,
        Synonyms = Synonyms.ToList(),
        ExtraSteps = ExtraSteps.ToList(),
        Collapsed = IsCollapsed,
        SpeakOnKeyPress = SpeakOnKeyPress,
        SpeakJoystickButton = SpeakJoystickButton,
    };
}
