using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NovaVox.App.ViewModels;

/// <summary>Un repère (tourelle, côté, accès...) du vaisseau en cours d'édition dans Réglages > 🚀 Vaisseaux — port de AiConfig.ShipCheatSheets/ShipCheatSheetColors.</summary>
public sealed class ShipCheatSheetPointRowVm : INotifyPropertyChanged
{
    /// <summary>Couleur neutre par défaut — celle codée en dur dans l'overlay avant l'introduction d'une couleur par repère, pour qu'un repère jamais recoloré ait le même rendu qu'avant.</summary>
    public const string DefaultColor = "#DBE4EE";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private string _label = "";
    public string Label { get => _label; set { _label = value; Raise(); } }

    private string _description = "";
    public string Description { get => _description; set { _description = value; Raise(); } }

    private string _color = DefaultColor;
    public string Color { get => _color; set { _color = value; Raise(); } }
}
