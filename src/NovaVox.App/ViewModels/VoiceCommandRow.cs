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

    private string? _triggerHotkey;
    public string? TriggerHotkey { get => _triggerHotkey; set { _triggerHotkey = value; Raise(); Raise(nameof(TriggerHotkeyLabel)); } }

    /// <summary>Libellé lisible de TriggerHotkey pour l'affichage (jamais l'encodage brut "joy:{...}") — voir UpdateListenHotkeyDisplay (MainWindow.xaml.cs).</summary>
    public string TriggerHotkeyLabel
    {
        get
        {
            if (string.IsNullOrEmpty(TriggerHotkey)) return "—";
            var joyInfo = NovaVox.Core.Hotkeys.JoystickHotkeyCodec.Decode(TriggerHotkey);
            return joyInfo is not null ? $"🕹 Bouton {joyInfo.Button}" : TriggerHotkey;
        }
    }

    public ObservableCollection<string> Synonyms { get; } = new();

    public List<ExtraStep> ExtraSteps { get; set; } = new();

    private bool _synonymsExpanded;
    /// <summary>État d'affichage (repliée/dépliée) de la liste de synonymes — jamais persisté, purement pour l'UI (voir toggle-syn, script.js).</summary>
    public bool SynonymsExpanded { get => _synonymsExpanded; set { _synonymsExpanded = value; Raise(); Raise(nameof(SynonymsCountLabel)); } }

    public bool HasSynonyms => Synonyms.Count > 0;
    public string SynonymsCountLabel => $"{(SynonymsExpanded ? "▾" : "▸")} {Synonyms.Count} syn";

    public VoiceCommandRow()
    {
        Synonyms.CollectionChanged += (_, _) => { Raise(nameof(HasSynonyms)); Raise(nameof(SynonymsCountLabel)); };
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
            ExtraSteps = new List<ExtraStep>(cmd.ExtraSteps),
            TriggerHotkey = cmd.TriggerHotkey,
        };
        foreach (var s in cmd.Synonyms) row.Synonyms.Add(s);
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
        ExtraSteps = ExtraSteps,
        TriggerHotkey = TriggerHotkey,
    };
}
