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

    /// <summary>true pour une correction de CETTE notification précise (AiConfig.GameLogHudExactOverrides), false pour un gabarit commun (AiConfig.GameLogHudOverrides).</summary>
    public bool IsExact { get; init; }

    public string DetectedPrefix => IsExact ? "🎯 Seulement pour : « " : "Détecté : « ";

    /// <summary>Dernier texte exact vu pendant cette session pour ce gabarit (ex. "Contrat accepté : Relay Race") — exemple pour "▶ Écouter" et "🎯 Seulement celle-ci".</summary>
    private string? _lastExample;
    public string? LastExample
    {
        get => _lastExample;
        set { _lastExample = value; Raise(); Raise(nameof(HasExample)); Raise(nameof(CanMakeExact)); }
    }

    public bool HasExample => !IsExact && !string.IsNullOrEmpty(_lastExample) && _lastExample != RawText;

    public bool CanMakeExact => HasExample;

    private string _customText = "";
    public string CustomText { get => _customText; set { _customText = value; Raise(); } }

    private bool _isHidden;
    public bool IsHidden { get => _isHidden; set { _isHidden = value; Raise(); } }

    private bool _isNew;
    public bool IsNew { get => _isNew; set { _isNew = value; Raise(); } }

    /// <summary>Modifié depuis le dernier "Enregistrer" (ou jamais encore enregistré) — cadre rouge tant que vrai, voir MainWindow.xaml.</summary>
    private bool _isDirty;
    public bool IsDirty { get => _isDirty; set { _isDirty = value; Raise(); } }

    /// <summary>Correspond à la recherche du Game.log (RawText/CustomText) — voir RefreshGameLogSearchVisibility, MainWindow.xaml.cs.</summary>
    private bool _rowVisible = true;
    public bool RowVisible { get => _rowVisible; set { _rowVisible = value; Raise(); } }
}
