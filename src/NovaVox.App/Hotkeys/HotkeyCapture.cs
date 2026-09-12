using NovaVox.Core.Hotkeys;

namespace NovaVox.App.Hotkeys;

public enum CaptureFailureReason { NoDevice, Cancelled, Timeout }

public sealed record CaptureResult(bool Ok, string? KeyName = null, string? JoystickHotkey = null, CaptureFailureReason? Reason = null);

public sealed record ComboCaptureResult(bool Ok, IReadOnlyList<string>? Modifiers = null, string? MainKey = null, CaptureFailureReason? Reason = null);

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

    private static readonly string[] ComboModifierOrder = { "ctrl", "alt", "shift", "altright" };

    /// <summary>
    /// Détecte une combinaison clavier physique (modificateur(s) + une
    /// touche principale) pour le clavier interactif de sélection des
    /// touches de commande — port de onKeyCapture (script.js) : les
    /// modificateurs pressés s'accumulent sans arrêter la détection ; la
    /// première touche non-modificatrice la termine, en capturant l'état
    /// réel des modificateurs tenus à cet instant précis (et non
    /// seulement ceux pressés avant elle). Échap annule — jamais une
    /// touche capturable ici, même si "esc" reste un choix valide via un
    /// clic direct sur la touche virtuelle (même asymétrie côté Python).
    /// </summary>
    public static async Task<ComboCaptureResult> CaptureKeyComboAsync(CancellationToken cancellationToken, double timeoutSeconds = 15.0)
    {
        var modifiers = new HashSet<string>();
        var trackedKeys = VirtualKeys.Map.Where(kv => kv.Key != "esc").ToList();
        var prevStates = trackedKeys.ToDictionary(kv => kv.Key, kv => KeyboardState.IsKeyDown(kv.Value));
        var escVk = VirtualKeys.Map["esc"];
        var escPrev = KeyboardState.IsKeyDown(escVk);

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested) return new ComboCaptureResult(false, Reason: CaptureFailureReason.Cancelled);

            var escNow = KeyboardState.IsKeyDown(escVk);
            if (escNow && !escPrev) return new ComboCaptureResult(false, Reason: CaptureFailureReason.Cancelled);
            escPrev = escNow;

            foreach (var (name, vk) in trackedKeys)
            {
                var held = KeyboardState.IsKeyDown(vk);
                if (held && !prevStates[name])
                {
                    if (ComboModifierOrder.Contains(name))
                    {
                        modifiers.Add(name);
                    }
                    else
                    {
                        var heldModifiers = ComboModifierOrder.Where(m => KeyboardState.IsKeyDown(VirtualKeys.Map[m])).ToList();
                        // AltGr (altright) est implémenté par Windows comme un Ctrl
                        // gauche synthétique tenu juste avant VK_RMENU : sans ce
                        // filtre, tout appui AltGr+touche ressortirait à tort comme
                        // "ctrl+altright+touche". Un vrai Ctrl+AltGr simultané n'est
                        // de toute façon pas une combinaison sensée.
                        if (heldModifiers.Contains("altright")) heldModifiers.Remove("ctrl");
                        return new ComboCaptureResult(true, Modifiers: heldModifiers, MainKey: name);
                    }
                }
                prevStates[name] = held;
            }
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
        return new ComboCaptureResult(false, Reason: CaptureFailureReason.Timeout);
    }

    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMButton = 0x04;
    private static readonly (string Value, int Vk)[] MouseButtonVks =
    {
        ("mouseleft", VkLButton), ("mouseright", VkRButton), ("mousemiddle", VkMButton),
    };

    /// <summary>
    /// Détecte le prochain clic physique (gauche/droit/molette), n'importe
    /// où à l'écran — via GetAsyncKeyState, qui fonctionne globalement
    /// pour les boutons souris exactement comme pour le clavier (pas
    /// besoin d'un crochet souris dédié). Port de capture_mouse_button
    /// (app.py).
    /// </summary>
    public static async Task<CaptureResult> CaptureMouseClickAsync(CancellationToken cancellationToken, double timeoutSeconds = 15.0)
    {
        var prevStates = MouseButtonVks.ToDictionary(m => m.Value, m => KeyboardState.IsKeyDown(m.Vk));
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested) return new CaptureResult(false, Reason: CaptureFailureReason.Cancelled);

            foreach (var (value, vk) in MouseButtonVks)
            {
                var held = KeyboardState.IsKeyDown(vk);
                if (held && !prevStates[value]) return new CaptureResult(true, KeyName: value);
                prevStates[value] = held;
            }
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
        return new CaptureResult(false, Reason: CaptureFailureReason.Timeout);
    }
}
