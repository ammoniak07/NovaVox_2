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
    };
}
