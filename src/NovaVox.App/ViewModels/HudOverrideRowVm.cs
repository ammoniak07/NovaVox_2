using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NovaVox.App.ViewModels;

/// <summary>Une notification HUD détectée dans le Game.log, avec sa correction de lecture — port de game_log_hud_overrides (app.py).</summary>
public sealed class HudOverrideRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Texte brut exact détecté dans le jeu — sert de clé.</summary>
    public required string RawText { get; init; }

    private string _customText = "";
    public string CustomText { get => _customText; set { _customText = value; Raise(); } }

    private bool _isNew;
    public bool IsNew { get => _isNew; set { _isNew = value; Raise(); } }

    /// <summary>Modifié depuis le dernier "Enregistrer" (ou jamais encore enregistré) — cadre rouge tant que vrai, voir MainWindow.xaml.</summary>
    private bool _isDirty;
    public bool IsDirty { get => _isDirty; set { _isDirty = value; Raise(); } }

    /// <summary>Correspond à la recherche du Game.log (RawText/CustomText) — voir RefreshGameLogSearchVisibility, MainWindow.xaml.cs.</summary>
    private bool _rowVisible = true;
    public bool RowVisible { get => _rowVisible; set { _rowVisible = value; Raise(); } }
}
