using System.Collections.ObjectModel;
using NovaVox.App.ViewModels;
using NovaVox.Core;
using NovaVox.Core.Commands;
using NovaVox.Core.Config;
using NovaVox.Core.Gemini;
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
    public GameModeConfigStore GameModeConfigStore { get; }

    public AudioConfig Audio { get; private set; }
    public AiConfig Ai { get; private set; }
    public OverlayConfig Overlay { get; private set; }
    public GameModeConfig GameMode { get; private set; }

    public ObservableCollection<VoiceCommandRow> Commands { get; } = new();
    public ObservableCollection<ProfileInfo> Profiles { get; } = new();

    public AppState(string baseDir)
    {
        CommandStore = new CommandStore(baseDir);
        AudioConfigStore = new AudioConfigStore(baseDir);
        AiConfigStore = new AiConfigStore(baseDir);
        OverlayConfigStore = new OverlayConfigStore(baseDir);
        GameModeConfigStore = new GameModeConfigStore(baseDir);

        CommandStore.ActiveProfileId = CommandStore.LoadActiveProfileId() ?? CommandStore.EnsureProfilesMigrated();
        Audio = AudioConfigStore.Load();
        Ai = AiConfigStore.Load(
            knownGeminiModels: GeminiModels.AvailableModels.Select(m => m.Id).ToList(),
            knownPiperVoices: PiperVoiceCatalog.Voices.Select(v => v.Id).ToList());
        Overlay = OverlayConfigStore.Load();
        // Les fichiers ci-dessus sont déjà "en direct" pour le mode courant
        // (sauvegardés tels quels à chaque changement, voir SwitchGameMode) :
        // rien à réappliquer au démarrage, GameMode sert seulement à savoir
        // quel mode était actif (sélection initiale de GameModeCombo) et
        // quel profil de commandes retrouver au retour sur l'autre mode.
        GameMode = GameModeConfigStore.Load();

        ReloadCommands();
        ReloadProfiles();
    }

    public void ReloadCommands()
    {
        Commands.Clear();
        foreach (var cmd in CommandStore.LoadCommands())
            Commands.Add(VoiceCommandRow.FromModel(cmd));
    }

    /// <summary>Ne recharge que les profils de commandes du mode de jeu actif — un profil Star Citizen n'est ni visible ni sélectionnable en mode "Autre jeu", et inversement.</summary>
    public void ReloadProfiles()
    {
        Profiles.Clear();
        foreach (var p in CommandStore.ListProfiles().Where(p => p.GameMode == GameMode.CurrentMode))
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
            Overlay.BgColor, Overlay.BgOpacity, Overlay.TextColor, Overlay.TextOpacity,
            Overlay.Scale);
    }

    /// <summary>Bascule vers un autre profil (commandes) : sauvegarde le profil courant, charge le nouveau.</summary>
    public void SwitchProfile(string profileId)
    {
        SaveCommands();
        var (_, commands, _) = CommandStore.ReadProfile(profileId);
        CommandStore.ActiveProfileId = profileId;
        CommandStore.SaveActiveProfileId(profileId);
        CommandStore.SaveCommands(commands, mirrorToProfile: false);
        ReloadCommands();
        ReloadProfiles();
    }

    /// <summary>
    /// Bascule le mode de jeu (GameModeCombo, en-tête) : sauvegarde l'état
    /// actuel (touche d'activation vocale, transparence de l'overlay,
    /// Game.log, wiki Gemini, thème de couleurs, profil de commandes actif)
    /// dans le bundle du mode qu'on quitte, puis applique celui du mode
    /// qu'on rejoint — le thème (ThemeManager) doit être réappliqué par
    /// l'appelant après coup, ce n'est qu'une préférence stockée ici. Les
    /// profils de commandes sont cloisonnés par mode (chaque profil
    /// n'appartient qu'à un seul mode, voir ProfileInfo.GameMode) : on
    /// bascule sur le profil mémorisé pour ce mode s'il existe toujours,
    /// sinon sur le premier profil disponible pour ce mode, sinon on en
    /// crée un vide. L'appelant doit ensuite rafraîchir l'UI (Réglages,
    /// overlay affiché, image de fond, touche d'activation en direct).
    /// </summary>
    public void SwitchGameMode(string mode)
    {
        if (mode == GameMode.CurrentMode) return;

        var outgoing = GameMode.ForMode(GameMode.CurrentMode);
        outgoing.ListenHotkey = Audio.ListenHotkey;
        outgoing.OverlayBgOpacity = Overlay.BgOpacity;
        outgoing.OverlayTextOpacity = Overlay.TextOpacity;
        outgoing.GameLogEnabled = Ai.GameLogEnabled;
        outgoing.GeminiWikiEnabled = Ai.GeminiWikiEnabled;
        outgoing.CommandProfileId = CommandStore.ActiveProfileId;
        outgoing.UiTheme = Ai.UiTheme;

        GameMode.CurrentMode = mode;
        GameModeConfigStore.Save(GameMode);

        var incoming = GameMode.ForMode(mode);
        Audio.ListenHotkey = incoming.ListenHotkey;
        SaveAudio();
        Overlay.BgOpacity = incoming.OverlayBgOpacity;
        Overlay.TextOpacity = incoming.OverlayTextOpacity;
        Overlay.VisibleRows["zone"] = mode == GameModeConfig.StarCitizen;
        SaveOverlay();
        Ai.GameLogEnabled = incoming.GameLogEnabled;
        Ai.GeminiWikiEnabled = incoming.GeminiWikiEnabled;
        Ai.UiTheme = incoming.UiTheme;
        SaveAi();

        // Recharge la liste de profils AVANT de choisir lequel activer : ne
        // doit contenir que ceux du mode qu'on rejoint (chaque profil de
        // commandes n'appartient qu'à un seul mode de jeu).
        ReloadProfiles();

        if (incoming.CommandProfileId is { } profileId && Profiles.Any(p => p.Id == profileId))
        {
            SwitchProfile(profileId);
        }
        else if (Profiles.Count > 0)
        {
            SwitchProfile(Profiles[0].Id);
        }
        else
        {
            // Ce mode de jeu n'a encore aucun profil de commandes : on en
            // crée un vide plutôt que de laisser le mode sans profil actif.
            var pid = CommandStore.NewProfileId();
            CommandStore.WriteProfile(pid, CommandStore.DefaultProfileName, new List<VoiceCommand>(), mode);
            ReloadProfiles();
            SwitchProfile(pid);
        }
    }
}
