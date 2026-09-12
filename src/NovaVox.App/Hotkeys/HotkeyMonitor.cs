using NovaVox.Core.Hotkeys;

namespace NovaVox.App.Hotkeys;

/// <summary>
/// Surveille l'état (appuyée/relâchée) de la touche/bouton d'activation
/// vocale configurée, à ~30 Hz — port de Api._hotkey_poll_loop/
/// _hotkey_is_held (app.py). Volontairement séparé du flux audio temps
/// réel (voir SpeechListener), sur son propre thread.
/// </summary>
public sealed class HotkeyMonitor : IDisposable
{
    private readonly JoystickManager? _joystickManager;
    private CancellationTokenSource? _cts;
    private Task? _task;
    private bool _prevToggleHeld;
    private bool? _prevPttHeld;

    /// <summary>Combinaison clavier ("ctrl+f9") ou bouton joystick (JoystickHotkeyCodec.Encode(...)).</summary>
    public string? Hotkey { get; set; }

    /// <summary>"toggle_key" ou "push_to_talk" (voir AudioConfig.ListenModes) — "always" ne déclenche aucune surveillance.</summary>
    public string ListenMode { get; set; } = "always";

    /// <summary>Push-to-talk : état effectif du micro (null/indéterminé traité comme "maintenu", voir _hotkey_poll_loop).</summary>
    public event EventHandler<bool>? PushToTalkStateChanged;

    /// <summary>Touche bascule : front montant détecté (relâchée -> pressée).</summary>
    public event EventHandler? ToggleTriggered;

    /// <summary>Bouton joystick configuré introuvable parmi les manettes actuellement connectées.</summary>
    public event EventHandler<string>? UnmatchedJoystick;

    public HotkeyMonitor(JoystickManager? joystickManager)
    {
        _joystickManager = joystickManager;
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _prevToggleHeld = false;
        _prevPttHeld = null;
        _task = Task.Run(() => PollLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private void PollLoop(CancellationToken token)
    {
        var warnedUnmatched = false;

        while (!token.IsCancellationRequested)
        {
            var held = IsHeld(out var isJoystick, out var displayLabel);

            if (held is null && isJoystick)
            {
                if (!warnedUnmatched)
                {
                    warnedUnmatched = true;
                    UnmatchedJoystick?.Invoke(this, displayLabel);
                }
            }
            else if (held is not null)
            {
                warnedUnmatched = false;
            }

            if (ListenMode == "push_to_talk")
            {
                // Indéterminé : on suppose le micro ouvert plutôt que de risquer de le couper par erreur.
                var effectiveHeld = held ?? true;
                if (_prevPttHeld != effectiveHeld)
                {
                    _prevPttHeld = effectiveHeld;
                    PushToTalkStateChanged?.Invoke(this, effectiveHeld);
                }
            }
            else if (ListenMode == "toggle_key")
            {
                // Un état indéterminé n'est jamais traité comme un front
                // montant (voir docstring de _hotkey_poll_loop).
                if (held is not null)
                {
                    if (held.Value && !_prevToggleHeld) ToggleTriggered?.Invoke(this, EventArgs.Empty);
                    _prevToggleHeld = held.Value;
                }
            }

            Thread.Sleep(30);
        }
    }

    private bool? IsHeld(out bool isJoystick, out string displayLabel)
    {
        isJoystick = false;
        displayLabel = "";
        var hotkey = Hotkey;
        if (string.IsNullOrEmpty(hotkey)) return null;

        var joyInfo = JoystickHotkeyCodec.Decode(hotkey);
        if (joyInfo is not null)
        {
            isJoystick = true;
            displayLabel = $"🕹 Bouton {joyInfo.Button}";
            if (_joystickManager is null || joyInfo.Button is null) return null;

            var connected = _joystickManager.GetJoysticks();
            var idx = JoystickHotkeyCodec.MatchJoystickIndex(joyInfo, connected.Select(j => (j.Name, j.Guid)).ToList());
            if (idx is null) return null;

            return _joystickManager.IsButtonHeld(connected[idx.Value].InstanceGuid, joyInfo.Button.Value);
        }

        var parts = hotkey.Split('+').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return null;
        displayLabel = string.Join(" + ", parts);

        foreach (var part in parts)
        {
            if (!VirtualKeys.Map.TryGetValue(part, out var vk)) return null;
            if (!KeyboardState.IsKeyDown(vk)) return false;
        }
        return true;
    }

    public void Dispose() => Stop();
}
