using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NovaVox.App.ViewModels;

/// <summary>Une ligne de l'éditeur de gabarits Game.log (Réglages > 🛰 Game.log > Entrées) — port de GAME_LOG_PHRASE_META (app.py).</summary>
public sealed class GameLogPhraseRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string PlaceholderHint { get; init; }

    private string _text = "";
    public string Text { get => _text; set { _text = value; Raise(); } }
}
