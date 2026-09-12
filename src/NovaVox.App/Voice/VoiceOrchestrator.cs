using NovaVox.App.Gemini;
using NovaVox.App.Hotkeys;
using NovaVox.App.Input;
using NovaVox.App.Speech;
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

    /// <summary>Message à afficher dans le journal système (déjà formaté, préfixe [Erreur]/[Info] inclus si besoin).</summary>
    public event EventHandler<string>? Log;
    public event EventHandler<bool>? ListeningChanged;
    public event EventHandler<long>? MicLevelChanged;
    public event EventHandler<string>? PhraseRecognized;
    public event EventHandler<string>? CommandExecuted;

    public VoiceOrchestrator(AppState state, IntPtr windowHandle)
    {
        _state = state;
        _commandExecutor = new CommandExecutor(_keySimulator);
        _keySimulator.OnError = (context, ex) => RaiseLog($"[Erreur touche] {context} : {ex.Message}");
        _keySimulator.OnWarning = RaiseLog;
        _tts.ErrorOccurred += (_, msg) => RaiseLog($"[Erreur voix] {msg}");

        try
        {
            _joystickManager = new JoystickManager(windowHandle);
        }
        catch
        {
            _joystickManager = null; // DirectInput indisponible : les boutons manette ne seront pas surveillés.
        }
    }

    /// <summary>À appeler après toute modification des réglages IA (voix Piper, Gemini...) pendant que l'écoute tourne.</summary>
    public void ApplyAiSettings()
    {
        _tts.DefaultPiperVoice = _state.Ai.PiperVoice;
        _tts.LengthScale = _state.Ai.PiperLengthScale;
        _tts.NoiseScale = _state.Ai.PiperNoiseScale;
        _tts.RadioEffectEnabled = _state.Ai.RadioEffect;
        _tts.Volume = _state.Audio.TtsVolume;

        _geminiClient = new GeminiClient(_state.Ai, _state.AiConfigStore) { GameLogStateProvider = () => (GameLogState?)null };
        _geminiClient.UserMessageAdded += (_, question) => RaiseLog($"Question pour {_state.Ai.GeminiName} : « {question} »");
        _geminiClient.ReplyReceived += (_, e) =>
        {
            RaiseLog(e.IsError ? e.Reply : $"{_state.Ai.GeminiName} : {e.Reply}");
            if (!e.IsError) _tts.Speak(e.Reply);
        };
    }

    public bool Start()
    {
        if (IsListening) return true;

        var modelPath = _state.Audio.ModelPath;
        if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
        {
            RaiseLog("[Erreur] Aucun modèle Vosk valide sélectionné (voir Réglages > 🔊 Sons > Parcourir).");
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
            RaiseLog("⏹ Lecture vocale interrompue.");
        };
        _speechListener.ErrorOccurred += (_, msg) => RaiseLog($"[Erreur micro] {msg}");

        var deviceNumber = AudioDevices.ResolveInputDeviceNumber(_state.Audio.InputDevice);

        try
        {
            _speechListener.Start(modelPath, deviceNumber);
        }
        catch (Exception ex)
        {
            RaiseLog($"[Erreur] Impossible de démarrer l'écoute : {ex.Message}");
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
            _hotkeyMonitor.PushToTalkStateChanged += (_, held) => { if (_speechListener is not null) _speechListener.MicGateOpen = held; };
            _hotkeyMonitor.ToggleTriggered += (_, _) => { if (_speechListener is not null) _speechListener.MicGateOpen = !_speechListener.MicGateOpen; };
            _hotkeyMonitor.UnmatchedJoystick += (_, label) => RaiseLog($"[Avertissement] Bouton {label} introuvable parmi les manettes connectées.");
            _hotkeyMonitor.Start();
        }

        IsListening = true;
        ListeningChanged?.Invoke(this, true);
        RaiseLog("Modèle chargé. Écoute en cours...");
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
        RaiseLog("Écoute arrêtée.");
    }

    private void OnTextRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        RaiseLog($"Reconnu : « {e.Text} »");
        PhraseRecognized?.Invoke(this, e.Text);

        var commands = _state.Commands.Select(row => row.ToModel()).ToList();
        var result = VoiceCommandDispatcher.Dispatch(
            e.Text, e.Alternatives, commands,
            _state.Ai.GeminiEnabled, _state.Ai.GeminiName, _state.Ai.UiLanguage,
            _dispatcherState, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);

        switch (result.Kind)
        {
            case VoiceActionKind.ExecuteCommand:
                var cmd = commands[result.CommandIndex];
                CommandExecuted?.Invoke(this, CommandMatcher.CommandKeysLabel(cmd));
                RaiseLog($"Commande : « {cmd.Phrase} » → {CommandMatcher.CommandKeysLabel(cmd)}");
                Task.Run(() => _commandExecutor.Run(cmd));
                break;

            case VoiceActionKind.AskGemini:
                if (result.Question is not null) _ = _geminiClient?.AskAsync(result.Question);
                break;

            case VoiceActionKind.AwaitGeminiQuestion:
                RaiseLog($"{_state.Ai.GeminiName} à l'écoute, pose ta question...");
                break;

            case VoiceActionKind.GeminiTimeout:
                RaiseLog($"({_state.Ai.GeminiName} : délai dépassé, annulé)");
                break;
        }
    }

    private void RaiseLog(string message) => Log?.Invoke(this, message);

    public void Dispose()
    {
        Stop();
        _tts.Dispose();
        _joystickManager?.Dispose();
    }
}
