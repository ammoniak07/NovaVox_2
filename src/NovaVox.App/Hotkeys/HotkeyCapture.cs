using NovaVox.Core.Hotkeys;

namespace NovaVox.App.Hotkeys;

public enum CaptureFailureReason { NoDevice, Cancelled, Timeout }

public sealed record CaptureResult(bool Ok, string? KeyName = null, string? JoystickHotkey = null, CaptureFailureReason? Reason = null);

/// <summary>
/// Détection du prochain appui (touche clavier ou bouton de manette) pour
/// assigner un raccourci sans en connaître le nom/index à l'avance — port
/// de Api.capture_joystick_button/_joystick_capture_thread (app.py) et de
/// l'équivalent clavier (repli sur keyboard.read_event côté Python).
/// </summary>
public static class HotkeyCapture
{
    public static async Task<CaptureResult> CaptureKeyboardKeyAsync(CancellationToken cancellationToken, double timeoutSeconds = 15.0)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var prevStates = VirtualKeys.Map.ToDictionary(kv => kv.Key, kv => KeyboardState.IsKeyDown(kv.Value));

        while (DateTime.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested) return new CaptureResult(false, Reason: CaptureFailureReason.Cancelled);

            foreach (var (name, vk) in VirtualKeys.Map)
            {
                var held = KeyboardState.IsKeyDown(vk);
                if (held && !prevStates[name]) return new CaptureResult(true, KeyName: name);
                prevStates[name] = held;
            }
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
        return new CaptureResult(false, Reason: CaptureFailureReason.Timeout);
    }

    public static async Task<CaptureResult> CaptureJoystickButtonAsync(
        JoystickManager joystickManager, CancellationToken cancellationToken, double timeoutSeconds = 15.0)
    {
        var joysticks = joystickManager.GetJoysticks();
        if (joysticks.Count == 0) return new CaptureResult(false, Reason: CaptureFailureReason.NoDevice);

        // État de référence établi après un court échauffement : certains
        // périphériques renvoient un état initial non fiable juste après
        // l'acquisition (voir _joystick_capture_thread, app.py).
        const int maxButtons = 32; // bien au-delà de ce qu'une manette réelle expose
        var prevStates = new bool[joysticks.Count, maxButtons];
        for (int warmup = 0; warmup < 3; warmup++)
        {
            for (int ji = 0; ji < joysticks.Count; ji++)
                for (int b = 0; b < maxButtons; b++)
                    prevStates[ji, b] = joystickManager.IsButtonHeld(joysticks[ji].InstanceGuid, b) ?? false;
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested) return new CaptureResult(false, Reason: CaptureFailureReason.Cancelled);

            for (int ji = 0; ji < joysticks.Count; ji++)
            {
                for (int b = 0; b < maxButtons; b++)
                {
                    var held = joystickManager.IsButtonHeld(joysticks[ji].InstanceGuid, b) ?? false;
                    if (held && !prevStates[ji, b])
                    {
                        var (name, guid, _) = joysticks[ji];
                        return new CaptureResult(true, JoystickHotkey: JoystickHotkeyCodec.Encode(name, guid, b));
                    }
                    prevStates[ji, b] = held;
                }
            }
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
        return new CaptureResult(false, Reason: CaptureFailureReason.Timeout);
    }
}
