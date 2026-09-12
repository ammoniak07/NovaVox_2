using System.ComponentModel;
using System.Runtime.CompilerServices;
using NovaVox.Core.Speech;

namespace NovaVox.App.ViewModels;

/// <summary>Ligne affichée dans la liste des modèles Vosk téléchargeables (Réglages > 🔊 Sons).</summary>
public sealed class VoskModelRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public required VoskModelInfo Model { get; init; }

    public string Label => Model.Label;
    public string Description => Model.Description;

    private bool _isDownloading;
    public bool IsDownloading { get => _isDownloading; set { _isDownloading = value; Raise(); Raise(nameof(CanInstall)); } }

    public bool CanInstall => !IsDownloading;

    private bool _isInstalled;
    public bool IsInstalled { get => _isInstalled; set { _isInstalled = value; Raise(); Raise(nameof(InstallButtonLabel)); } }

    public string InstallButtonLabel => IsInstalled ? "Réinstaller" : "Télécharger et installer";

    private string _statusText = "";
    public string StatusText { get => _statusText; set { _statusText = value; Raise(); } }
}
