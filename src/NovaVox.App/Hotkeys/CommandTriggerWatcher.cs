using NovaVox.Core.Commands;
using NovaVox.Core.Hotkeys;

namespace NovaVox.App.Hotkeys;

/// <summary>
/// Surveille, en continu et indépendamment de la reconnaissance vocale,
/// les combinaisons/boutons manette assignés en tant que TriggerHotkey
/// sur chaque commande — un déclenchement manuel "comme pour le micro"
/// (même principe que le bouton joystick d'activation du micro, voir
/// HotkeyMonitor), fonctionnalité propre au port .NET sans équivalent
/// côté app.py. Polling à ~30 Hz, un seul thread pour toutes les
/// commandes plutôt qu'un HotkeyMonitor par commande.
/// </summary>
public sealed class CommandTriggerWatcher : IDisposable
{
    private readonly Func<List<VoiceCommand>> _commandsProvider;
    private readonly JoystickManager? _joystickManager;
    private readonly Dictionary<string, bool> _prevHeld = new();
    private CancellationTokenSource? _cts;
    private Task? _task;

    public event EventHandler<VoiceCommand>? CommandFired;

    public CommandTriggerWatcher(Func<List<VoiceCommand>> commandsProvider, JoystickManager? joystickManager)
    {
        _commandsProvider = commandsProvider;
        _joystickManager = joystickManager;
    }

    public void Start()
    {
        if (_task is not null) return;
        _cts = new CancellationTokenSource();
        _prevHeld.Clear();
        _task = Task.Run(() => PollLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _task = null;
    }

    private void PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            foreach (var cmd in _commandsProvider())
            {
                if (cmd.IsTitle || string.IsNullOrEmpty(cmd.TriggerHotkey)) continue;

                var held = IsHeld(cmd.TriggerHotkey);
                var wasHeld = _prevHeld.TryGetValue(cmd.TriggerHotkey, out var prev) && prev;
                if (held && !wasHeld) CommandFired?.Invoke(this, cmd);
                _prevHeld[cmd.TriggerHotkey] = held;
            }
            Thread.Sleep(30);
        }
    }

    private bool IsHeld(string hotkey)
    {
        var joyInfo = JoystickHotkeyCodec.Decode(hotkey);
        if (joyInfo is not null)
        {
            if (_joystickManager is null || joyInfo.Button is null) return false;
            var connected = _joystickManager.GetJoysticks();
            var idx = JoystickHotkeyCodec.MatchJoystickIndex(joyInfo, connected.Select(j => (j.Name, j.Guid)).ToList());
            if (idx is null) return false;
            return _joystickManager.IsButtonHeld(connected[idx.Value].InstanceGuid, joyInfo.Button.Value) ?? false;
        }

        var parts = hotkey.Split('+').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return false;
        foreach (var part in parts)
        {
            if (!VirtualKeys.Map.TryGetValue(part, out var vk)) return false;
            if (!KeyboardState.IsKeyDown(vk)) return false;
        }
        return true;
    }

    public void Dispose() => Stop();
}
