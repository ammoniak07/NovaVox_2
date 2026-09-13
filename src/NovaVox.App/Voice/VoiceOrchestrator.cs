using NovaVox.App.Gemini;
using NovaVox.App.Hotkeys;
using NovaVox.App.Input;
using NovaVox.App.Speech;
using NovaVox.Core;
using NovaVox.Core.Commands;
using NovaVox.Core.GameLog;
using NovaVox.Core.Tts;

namespace NovaVox.App.Voice;

/// <summary>
/// Relie toutes les briques indépendantes (Vosk, dispatcher de commandes,
/// simulation clavier, Piper, Gemini, raccourci d'activation) en un seul
/// pipeline pilotable — équivalent WPF du corps de la classe Api (app.py) :
/// c'est cette classe qui fait qu'une phrase reconnue finit par appuyer
/// une touche ou déclencher une réponse Gemini.
/// </summary>
public sealed class VoiceOrchestrator : IDisposable
{
    private readonly AppState _state;
    private readonly VoskModelCache _modelCache = new();
    private readonly KeySimulator _keySimulator = new();
    private readonly CommandExecutor _commandExecutor;
    private readonly PiperTtsEngine _tts = new();
    private readonly VoiceDispatcherState _dispatcherState = new();
    private readonly JoystickManager? _joystickManager;

    private SpeechListener? _speechListener;
    private HotkeyMonitor? _hotkeyMonitor;
    private GeminiClient? _geminiClient;

    public bool IsListening { get; private set; }

    /// <summary>
    /// Capture le prochain bouton pressé sur une manette connectée, pour
    /// assigner ListenHotkey depuis les réglages — port de
    /// capture_joystick_button (app.py). Réutilise le JoystickManager déjà
    /// acquis par cette instance (créé au constructeur, indépendamment de
    /// l'écoute), pour ne pas ouvrir une seconde acquisition DirectInput.
    /// </summary>
    public Task<CaptureResult> CaptureJoystickButtonAsync(CancellationToken cancellationToken) =>
        _joystickManager is null
            ? Task.FromResult(new CaptureResult(false, Reason: CaptureFailureReason.NoDevice))
            : HotkeyCapture.CaptureJoystickButtonAsync(_joystickManager, cancellationToken);

    /// <summary>
    /// À appeler après avoir modifié ListenMode/ListenHotkey dans les
    /// réglages, pour que le changement s'applique tout de suite si
    /// l'écoute tourne déjà (équivalent partiel de _sync_hotkey_poll,
    /// app.py) plutôt que d'exiger un arrêt/redémarrage de l'écoute.
    /// </summary>
    public void UpdateListenHotkeySettings()
    {
        if (_hotkeyMonitor is null) return;
        _hotkeyMonitor.Hotkey = _state.Audio.ListenHotkey;
        _hotkeyMonitor.ListenMode = _state.Audio.ListenMode;
    }

    /// <summary>
    /// Surveillance Game.log possédée par MainWindow (panneau "🛰 Game.log") —
    /// injectée ici pour que les questions posées à voix haute à Gemini
    /// bénéficient aussi du contexte de jeu courant (zone/vaisseau), comme
    /// le fait déjà la fenêtre de discussion tapée.
    /// </summary>
    public GameLogWatcher? GameLogWatcher { get; set; }

    /// <summary>Message à afficher dans le journal système, avec son "kind" (info/success/error/warning) — voir appendLog (gui/script.js).</summary>
    public event EventHandler<(string Message, string Kind)>? Log;
    public event EventHandler<bool>? ListeningChanged;
    /// <summary>Micro effectivement ouvert (porte micro ouverte ET écoute active) — port de _overlay_set_mic (app.py).</summary>
    public event EventHandler<bool>? MicActiveChanged;
    public event EventHandler<long>? MicLevelChanged;
    public event EventHandler<string>? PhraseRecognized;
    public event EventHandler<string>? CommandExecuted;
    /// <summary>Phrase + touche(s) d'une commande qui vient d'être déclenchée — pour l'overlay (_overlay_set_phrase/_overlay_set_last_command/_overlay_flash_command, app.py).</summary>
    public event EventHandler<(string Phrase, string KeysLabel)>? CommandTriggered;

    public VoiceOrchestrator(AppState state, IntPtr windowHandle)
    {
        _state = state;
        _commandExecutor = new CommandExecutor(_keySimulator);
        _keySimulator.OnError = (context, ex) => RaiseLog($"[Erreur touche] {context} : {ex.Message}", "error");
        _keySimulator.OnWarning = msg => RaiseLog(msg, "warning");
        _keySimulator.OnKeyPress = keysStr => RaiseLog($"[Touche] {keysStr}", "diagnostic");
        _tts.ErrorOccurred += (_, msg) => RaiseLog($"[Erreur voix] {msg}", "error");
        _tts.Diagnostic += (_, msg) => RaiseLog(msg, "diagnostic");

        try
        {
            _joystickManager = new JoystickManager(windowHandle);
        }
        catch (Exception ex)
        {
            _joystickManager = null; // DirectInput indisponible : les boutons manette ne seront pas surveillés.
            // AppLog directement (pas RaiseLog) : on est dans le constructeur,
            // avant que l'appelant ait pu s'abonner à Log — un RaiseLog ici
            // ne trouverait aucun abonné et disparaîtrait silencieusement.
            AppLog.Append(NovaVoxPaths.BaseDirectory, $"[Joystick] DirectInput indisponible, boutons manette désactivés ({ex.Message}).", "diagnostic");
        }
    }

    /// <summary>
    /// À appeler après tout changement des réglages voix Piper (voix,
    /// vitesse, expressivité, volume, effet radio, périphérique de sortie)
    /// pendant que l'écoute tourne — sans quoi le moteur TTS déjà démarré
    /// garde les valeurs figées au dernier appel de Start()/ApplyAiSettings()
    /// jusqu'au prochain redémarrage de l'écoute. Séparé d'ApplyAiSettings
    /// pour pouvoir être appelé à chaque tick d'un slider sans recréer
    /// _geminiClient à chaque fois (ce qui viderait l'historique de la
    /// conversation Gemini en cours).
    /// </summary>
    public void ApplyTtsSettings()
    {
        _tts.DefaultPiperVoice = _state.Ai.PiperVoice;
        _tts.LengthScale = _state.Ai.PiperLengthScale;
        _tts.NoiseScale = _state.Ai.PiperNoiseScale;
        _tts.RadioEffectEnabled = _state.Ai.RadioEffect;
        _tts.Volume = _state.Audio.TtsVolume;
        _tts.OutputDeviceName = _state.Audio.OutputDevice;
    }

    /// <summary>À appeler après toute modification des réglages IA (voix Piper, Gemini...) pendant que l'écoute tourne.</summary>
    public void ApplyAiSettings()
    {
        ApplyTtsSettings();

        _geminiClient = new GeminiClient(_state.Ai, _state.AiConfigStore) { GameLogStateProvider = () => GameLogWatcher?.GetState() };
        _geminiClient.UserMessageAdded += (_, question) => RaiseLog($"Question pour {_state.Ai.GeminiName} : « {question} »", "info");
        _geminiClient.Log += (_, e) => RaiseLog(e.Message, e.Kind);
        _geminiClient.ReplyReceived += (_, e) =>
        {
            RaiseLog(e.IsError ? e.Reply : $"{_state.Ai.GeminiName} : {e.Reply}", e.IsError ? "error" : "info");
            if (!e.IsError) _tts.Speak(e.Reply);
        };
    }

    public bool Start()
    {
        if (IsListening) return true;

        var modelPath = _state.Audio.ModelPath;
        if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
        {
            RaiseLog("[Erreur] Aucun modèle Vosk valide sélectionné (voir Réglages > 🔊 Sons > Parcourir).", "error");
            return false;
        }

        ApplyAiSettings();

        _speechListener = new SpeechListener(_modelCache)
        {
            ListenMode = _state.Audio.ListenMode,
            MicGain = _state.Audio.MicGain,
            MicGate = _state.Audio.MicGate,
            IsStopPhrase = textLower => TtsStopPhrase.IsStopPhrase(textLower, _state.Ai.GeminiName),
            IsSpeakingProvider = () => _tts.IsSpeaking,
            SpeechMuteUntilProvider = () => _tts.SpeechMuteUntil,
        };
        _speechListener.TextRecognized += OnTextRecognized;
        _speechListener.MicLevelChanged += (_, rms) => MicLevelChanged?.Invoke(this, rms);
        _speechListener.StopPhraseRecognized += (_, _) =>
        {
            _tts.Interrupt();
            RaiseLog("⏹ Lecture vocale interrompue.", "info");
        };
        _speechListener.ErrorOccurred += (_, msg) => RaiseLog($"[Erreur micro] {msg}", "error");

        var deviceNumber = AudioDevices.ResolveInputDeviceNumber(_state.Audio.InputDevice);

        try
        {
            _speechListener.Start(modelPath, deviceNumber);
        }
        catch (Exception ex)
        {
            RaiseLog($"[Erreur] Impossible de démarrer l'écoute : {ex.Message}", "error");
            _speechListener = null;
            return false;
        }

        if (_state.Audio.ListenMode is "toggle_key" or "push_to_talk" && !string.IsNullOrEmpty(_state.Audio.ListenHotkey))
        {
            _speechListener.MicGateOpen = _state.Audio.ListenMode != "push_to_talk"; // PTT démarre micro coupé, bascule démarre micro actif
            _hotkeyMonitor = new HotkeyMonitor(_joystickManager)
            {
                Hotkey = _state.Audio.ListenHotkey,
                ListenMode = _state.Audio.ListenMode,
            };
            _hotkeyMonitor.PushToTalkStateChanged += (_, held) =>
            {
                if (_speechListener is null) return;
                _speechListener.MicGateOpen = held;
                MicActiveChanged?.Invoke(this, held);
            };
            _hotkeyMonitor.ToggleTriggered += (_, _) =>
            {
                if (_speechListener is null) return;
                _speechListener.MicGateOpen = !_speechListener.MicGateOpen;
                MicActiveChanged?.Invoke(this, _speechListener.MicGateOpen);
            };
            _hotkeyMonitor.UnmatchedJoystick += (_, label) => RaiseLog($"[Avertissement] Bouton {label} introuvable parmi les manettes connectées.", "warning");
            _hotkeyMonitor.Start();
        }

        IsListening = true;
        ListeningChanged?.Invoke(this, true);
        MicActiveChanged?.Invoke(this, _speechListener.MicGateOpen);
        RaiseLog("Modèle chargé. Écoute en cours...", "success");
        return true;
    }

    public void Stop()
    {
        if (!IsListening) return;

        _hotkeyMonitor?.Dispose();
        _hotkeyMonitor = null;
        _speechListener?.Dispose();
        _speechListener = null;

        IsListening = false;
        ListeningChanged?.Invoke(this, false);
        MicActiveChanged?.Invoke(this, false);
        RaiseLog("Écoute arrêtée.", "info");
    }

    private void OnTextRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        RaiseLog($"Reconnu : « {e.Text} »", "info");
        PhraseRecognized?.Invoke(this, e.Text);

        var commands = _state.Commands.Select(row => row.ToModel()).ToList();
        var result = VoiceCommandDispatcher.Dispatch(
            e.Text, e.Alternatives, commands,
            _state.Ai.GeminiEnabled, _state.Ai.GeminiName, _state.Ai.UiLanguage,
            _dispatcherState, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);

        switch (result.Kind)
        {
            case VoiceActionKind.ExecuteCommand:
                FireCommand(commands[result.CommandIndex]);
                break;

            case VoiceActionKind.AskGemini:
                if (result.Question is not null) _ = _geminiClient?.AskAsync(result.Question);
                break;

            case VoiceActionKind.AwaitGeminiQuestion:
                RaiseLog($"{_state.Ai.GeminiName} à l'écoute, pose ta question...", "info");
                break;

            case VoiceActionKind.GeminiTimeout:
                RaiseLog($"({_state.Ai.GeminiName} : délai dépassé, annulé)", "warning");
                break;
        }
    }

    /// <summary>Exécute une commande reconnue à la voix et notifie tout le monde (journal, overlay, confirmation vocale).</summary>
    private void FireCommand(VoiceCommand cmd)
    {
        var keysLabel = CommandMatcher.CommandKeysLabel(cmd);
        CommandExecuted?.Invoke(this, keysLabel);
        CommandTriggered?.Invoke(this, (cmd.Phrase, keysLabel));
        RaiseLog($"Commande : « {cmd.Phrase} » → {keysLabel}", "success");
        Task.Run(() => _commandExecutor.Run(cmd));
        // Port de "if self.confirm_commands_voice: self._speak(cmd['phrase'])" (_execute_command, app.py).
        if (_state.Ai.ConfirmCommands) _tts.Speak(cmd.Phrase);
    }

    private void RaiseLog(string message, string kind = "info") => Log?.Invoke(this, (message, kind));

    public void Dispose()
    {
        Stop();
        _tts.Dispose();
        _joystickManager?.Dispose();
    }
}
