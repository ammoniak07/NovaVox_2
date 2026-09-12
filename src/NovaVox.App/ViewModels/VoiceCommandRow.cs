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
    public string Keys { get => _keys; set { _keys = value; Raise(); } }

    private bool _hold;
    public bool Hold { get => _hold; set { _hold = value; Raise(); } }

    private int _repeatCount = 1;
    public int RepeatCount { get => _repeatCount; set { _repeatCount = value; Raise(); } }

    private double _repeatDelay = 0.1;
    public double RepeatDelay { get => _repeatDelay; set { _repeatDelay = value; Raise(); } }

    public ObservableCollection<string> Synonyms { get; } = new();

    public List<ExtraStep> ExtraSteps { get; set; } = new();

    public string SynonymsText
    {
        get => string.Join(", ", Synonyms);
        set
        {
            Synonyms.Clear();
            foreach (var s in value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
                Synonyms.Add(s);
            Raise();
        }
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
    };
}
