using System.Collections.ObjectModel;
using NovaVox.App.ViewModels;
using NovaVox.Core;
using NovaVox.Core.Commands;
using NovaVox.Core.Config;
using NovaVox.Core.Tts;

namespace NovaVox.App;

/// <summary>
/// État applicatif partagé par les différents panneaux de l'interface —
/// équivalent WPF des attributs de configuration de la classe Api
/// (app.py). Centralise les stores Core et les réglages actuellement
/// chargés ; chaque panneau lit/écrit directement dessus et persiste via
/// les stores correspondants.
/// </summary>
public sealed class AppState
{
    public CommandStore CommandStore { get; }
    public AudioConfigStore AudioConfigStore { get; }
    public AiConfigStore AiConfigStore { get; }
    public OverlayConfigStore OverlayConfigStore { get; }

    public AudioConfig Audio { get; private set; }
    public AiConfig Ai { get; private set; }
    public OverlayConfig Overlay { get; private set; }

    public ObservableCollection<VoiceCommandRow> Commands { get; } = new();
    public ObservableCollection<ProfileInfo> Profiles { get; } = new();

    public AppState(string baseDir)
    {
        CommandStore = new CommandStore(baseDir);
        AudioConfigStore = new AudioConfigStore(baseDir);
        AiConfigStore = new AiConfigStore(baseDir);
        OverlayConfigStore = new OverlayConfigStore(baseDir);

        CommandStore.ActiveProfileId = CommandStore.LoadActiveProfileId() ?? CommandStore.EnsureProfilesMigrated();
        Audio = AudioConfigStore.Load();
        Ai = AiConfigStore.Load(knownPiperVoices: PiperVoiceCatalog.Voices.Select(v => v.Id).ToList());
        Overlay = OverlayConfigStore.Load();

        ReloadCommands();
        ReloadProfiles();
    }

    public void ReloadCommands()
    {
        Commands.Clear();
        foreach (var cmd in CommandStore.LoadCommands())
            Commands.Add(VoiceCommandRow.FromModel(cmd));
    }

    public void ReloadProfiles()
    {
        Profiles.Clear();
        foreach (var p in CommandStore.ListProfiles())
            Profiles.Add(p);
    }

    /// <summary>Persiste la liste de commandes actuelle (après ajout/édition/réordonnancement/suppression).</summary>
    public void SaveCommands()
    {
        CommandStore.SaveCommands(Commands.Select(r => r.ToModel()).ToList());
    }

    public void SaveAudio() => AudioConfigStore.Save(Audio);

    public void SaveAi() => AiConfigStore.Save(Ai);

    public void SaveOverlay()
    {
        OverlayConfigStore.Save(
            Overlay.Enabled, Overlay.X, Overlay.Y, Overlay.VisibleRows,
            Overlay.BgColor, Overlay.BgOpacity, Overlay.TextColor, Overlay.TextOpacity);
    }

    /// <summary>Bascule vers un autre profil : sauvegarde le profil courant, charge le nouveau.</summary>
    public void SwitchProfile(string profileId)
    {
        SaveCommands();
        var (_, commands) = CommandStore.ReadProfile(profileId);
        CommandStore.ActiveProfileId = profileId;
        CommandStore.SaveActiveProfileId(profileId);
        CommandStore.SaveCommands(commands, mirrorToProfile: false);
        ReloadCommands();
        ReloadProfiles();
    }
}
