using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NovaVox.App.ViewModels;

/// <summary>Un identifiant de destination brut détecté dans le Game.log, avec son nom personnalisé — port de game_log_destination_aliases (app.py).</summary>
public sealed class DestinationAliasRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Clé normalisée (voir GameLogDestinations.DestinationAliasKey) — sert de clé.</summary>
    public required string RawKey { get; init; }

    private string _customName = "";
    public string CustomName { get => _customName; set { _customName = value; Raise(); } }

    private bool _isNew;
    public bool IsNew { get => _isNew; set { _isNew = value; Raise(); } }

    /// <summary>Modifié depuis le dernier "Enregistrer" (ou jamais encore enregistré) — cadre rouge tant que vrai, voir MainWindow.xaml.</summary>
    private bool _isDirty;
    public bool IsDirty { get => _isDirty; set { _isDirty = value; Raise(); } }

    /// <summary>Correspond à la recherche du Game.log (RawKey/CustomName) — voir RefreshGameLogSearchVisibility, MainWindow.xaml.cs.</summary>
    private bool _rowVisible = true;
    public bool RowVisible { get => _rowVisible; set { _rowVisible = value; Raise(); } }
}
