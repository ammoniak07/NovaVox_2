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

    public AudioConfig Audio { get; private set; }
    public AiConfig Ai { get; private set; }
    public OverlayConfig Overlay { get; private set; }

    /// <summary>
    /// Image de fond du profil actif (chemin absolu), ou null pour l'image
    /// par défaut (background.png/jpg/jpeg à côté de l'exécutable) — voir
    /// ProfileGameSettings.BackgroundImagePath / MainWindow.LoadPanelsBackgroundImage.
    /// </summary>
    public string? ActiveProfileBackgroundImagePath { get; private set; }

    public ObservableCollection<VoiceCommandRow> Commands { get; } = new();
    public ObservableCollection<ProfileInfo> Profiles { get; } = new();

    public AppState(string baseDir)
    {
        CommandStore = new CommandStore(baseDir);
        AudioConfigStore = new AudioConfigStore(baseDir);
        AiConfigStore = new AiConfigStore(baseDir);
        OverlayConfigStore = new OverlayConfigStore(baseDir);

        Audio = AudioConfigStore.Load();
        Ai = AiConfigStore.Load(
            knownGeminiModels: GeminiModels.AvailableModels.Select(m => m.Id).ToList(),
            knownPiperVoices: PiperVoiceCatalog.Voices.Select(v => v.Id).ToList());
        Overlay = OverlayConfigStore.Load();

        // Réglages jeu actuellement en vigueur, figés dans le tout premier
        // profil migré (installation existante mise à jour vers cette
        // version, sans dossier profiles/ encore) : sinon ce premier
        // profil repartirait sur les valeurs par défaut au lieu de ce que
        // l'utilisateur a déjà configuré (Ai/Overlay chargés juste au-dessus).
        var currentGameSettings = new ProfileGameSettings
        {
            GameLogEnabled = Ai.GameLogEnabled,
            GeminiWikiEnabled = Ai.GeminiWikiEnabled,
            OverlayVisibleRows = new Dictionary<string, bool>(Overlay.VisibleRows),
        };
        CommandStore.ActiveProfileId = CommandStore.LoadActiveProfileId() ?? CommandStore.EnsureProfilesMigrated(currentGameSettings);

        ReloadCommands();
        ReloadProfiles();

        if (CommandStore.ActiveProfileId is { } activeId)
        {
            try { ActiveProfileBackgroundImagePath = CommandStore.ReadProfile(activeId).Game.BackgroundImagePath; }
            catch { /* profil illisible : image par défaut */ }
        }
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

    /// <summary>Recharge Overlay depuis overlay_config.json — nécessaire après une écriture faite directement par OverlayWindow (cases à cocher par ligne), qui contourne cet objet en mémoire.</summary>
    public void ReloadOverlay() => Overlay = OverlayConfigStore.Load();

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

    /// <summary>Bascule vers un autre profil : sauvegarde le profil courant (commandes ET réglages jeu), charge le nouveau.</summary>
    public void SwitchProfile(string profileId)
    {
        SaveCommands();
        SaveCurrentProfileGameSettings();

        var (_, commands, game) = CommandStore.ReadProfile(profileId);
        CommandStore.ActiveProfileId = profileId;
        CommandStore.SaveActiveProfileId(profileId);
        CommandStore.SaveCommands(commands, mirrorToProfile: false);
        ReloadCommands();
        ReloadProfiles();

        // Applique les réglages jeu du profil qu'on vient de charger — même
        // mécanisme que les commandes juste au-dessus : ces fichiers
        // globaux (ai_config.json/overlay_config.json) reflètent toujours
        // le profil ACTUELLEMENT actif, pas un état par profil séparé.
        Ai.GameLogEnabled = game.GameLogEnabled;
        Ai.GeminiWikiEnabled = game.GeminiWikiEnabled;
        SaveAi();
        Overlay.VisibleRows = game.OverlayVisibleRows.Count > 0
            ? new Dictionary<string, bool>(game.OverlayVisibleRows)
            : OverlayConfig.RowKeys.ToDictionary(k => k, _ => true);
        SaveOverlay();
        ActiveProfileBackgroundImagePath = game.BackgroundImagePath;
    }

    /// <summary>
    /// Reflète les réglages jeu ACTUELLEMENT en vigueur (Ai/Overlay) dans
    /// le profil actif — appelée après une modification de l'un des
    /// réglages concernés (Game.log, wiki Gemini, lignes visibles de
    /// l'overlay, image de fond) pour qu'elle survive à un changement de
    /// profil ultérieur, exactement comme les commandes le font déjà
    /// (SaveCommands/mirrorToProfile). Best effort, jamais bloquant.
    /// </summary>
    public void SaveCurrentProfileGameSettings()
    {
        if (CommandStore.ActiveProfileId is not { } profileId) return;
        try
        {
            var (name, commands, _) = CommandStore.ReadProfile(profileId);
            var game = new ProfileGameSettings
            {
                GameLogEnabled = Ai.GameLogEnabled,
                GeminiWikiEnabled = Ai.GeminiWikiEnabled,
                OverlayVisibleRows = new Dictionary<string, bool>(Overlay.VisibleRows),
                BackgroundImagePath = ActiveProfileBackgroundImagePath,
            };
            CommandStore.WriteProfile(profileId, name, commands, game);
        }
        catch
        {
            // Best effort : ne doit jamais empêcher le reste de fonctionner.
        }
    }
}
