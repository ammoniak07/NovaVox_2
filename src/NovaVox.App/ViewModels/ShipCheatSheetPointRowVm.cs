using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NovaVox.App.ViewModels;

/// <summary>Un repère (tourelle, côté, accès...) du vaisseau en cours d'édition dans Réglages > 🚀 Vaisseaux — port de AiConfig.ShipCheatSheets.</summary>
public sealed class ShipCheatSheetPointRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private string _label = "";
    public string Label { get => _label; set { _label = value; Raise(); } }

    private string _description = "";
    public string Description { get => _description; set { _description = value; Raise(); } }
}
