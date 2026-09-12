using System.ComponentModel;
using System.Runtime.CompilerServices;
using NovaVox.Core.Tts;

namespace NovaVox.App.ViewModels;

/// <summary>Ligne affichée dans la liste des voix Piper téléchargeables (Réglages > 🌟 IA Gemini).</summary>
public sealed class PiperVoiceRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public required PiperVoiceInfo Voice { get; init; }

    public string Id => Voice.Id;
    public string Label => Voice.Label;

    private bool _isInstalled;
    public bool IsInstalled { get => _isInstalled; set { _isInstalled = value; Raise(); Raise(nameof(CanDownload)); } }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set { _isSelected = value; Raise(); } }

    private bool _isDownloading;
    public bool IsDownloading { get => _isDownloading; set { _isDownloading = value; Raise(); Raise(nameof(CanDownload)); } }

    public bool CanDownload => !IsDownloading && !IsInstalled;

    private string _statusText = "";
    public string StatusText { get => _statusText; set { _statusText = value; Raise(); } }
}
