namespace NovaVox.App.Input;

/// <summary>Code de balayage (scan code) PS/2 "Set 1" d'une touche, plus l'indicateur "étendu" (préfixe E0) exigé par certaines touches.</summary>
public readonly record struct ScanCode(ushort Code, bool Extended);

/// <summary>
/// Table complète nom de touche -> code de balayage brut, envoyée via
/// SendInput (voir NativeInput). Remplace pydirectinput : plutôt que de
/// dépendre de son dictionnaire interne (basé sur un clavier US, et
/// incomplet — voir NUMPAD_DIGIT_SCAN_CODES/ISO102_SCAN_CODE dans
/// app.py, qui contournaient déjà ses lacunes), toutes les touches sont
/// envoyées nous-mêmes en code de balayage brut, la même technique bas
/// niveau que pydirectinput utilise en interne pour le reste — donc tout
/// aussi compatible avec les jeux basés sur DirectInput comme Star
/// Citizen. Les noms de touches correspondent exactement aux "value" de
/// KB_LAYOUT/KB_NAV_LAYOUT/KB_NUMPAD_LAYOUT (gui/script.js), qui
/// désignent déjà des POSITIONS physiques (via event.code côté JS), pas
/// des caractères — exactement ce qu'un code de balayage représente.
/// Valeurs vérifiées contre NUMPAD_DIGIT_SCAN_CODES/ISO102_SCAN_CODE
/// (app.py) pour les entrées qui s'y trouvent déjà.
/// </summary>
public static class KeyScanCodes
{
    public static readonly IReadOnlyDictionary<string, ScanCode> Map = new Dictionary<string, ScanCode>
    {
        ["esc"] = new(0x01, false),
        ["f1"] = new(0x3B, false), ["f2"] = new(0x3C, false), ["f3"] = new(0x3D, false), ["f4"] = new(0x3E, false),
        ["f5"] = new(0x3F, false), ["f6"] = new(0x40, false), ["f7"] = new(0x41, false), ["f8"] = new(0x42, false),
        ["f9"] = new(0x43, false), ["f10"] = new(0x44, false), ["f11"] = new(0x57, false), ["f12"] = new(0x58, false),

        ["`"] = new(0x29, false),
        ["1"] = new(0x02, false), ["2"] = new(0x03, false), ["3"] = new(0x04, false), ["4"] = new(0x05, false),
        ["5"] = new(0x06, false), ["6"] = new(0x07, false), ["7"] = new(0x08, false), ["8"] = new(0x09, false),
        ["9"] = new(0x0A, false), ["0"] = new(0x0B, false),
        ["-"] = new(0x0C, false), ["="] = new(0x0D, false), ["backspace"] = new(0x0E, false),

        ["tab"] = new(0x0F, false),
        ["q"] = new(0x10, false), ["w"] = new(0x11, false), ["e"] = new(0x12, false), ["r"] = new(0x13, false),
        ["t"] = new(0x14, false), ["y"] = new(0x15, false), ["u"] = new(0x16, false), ["i"] = new(0x17, false),
        ["o"] = new(0x18, false), ["p"] = new(0x19, false),
        ["["] = new(0x1A, false), ["]"] = new(0x1B, false), ["\\"] = new(0x2B, false),

        ["capslock"] = new(0x3A, false),
        ["a"] = new(0x1E, false), ["s"] = new(0x1F, false), ["d"] = new(0x20, false), ["f"] = new(0x21, false),
        ["g"] = new(0x22, false), ["h"] = new(0x23, false), ["j"] = new(0x24, false), ["k"] = new(0x25, false),
        ["l"] = new(0x26, false),
        [";"] = new(0x27, false), ["'"] = new(0x28, false), ["enter"] = new(0x1C, false),

        ["shift"] = new(0x2A, false), // touche unique gauche/droite : NovaVox ne distingue pas les deux (voir KB_CODE_TO_VALUE)
        ["iso102"] = new(0x56, false), // DIK_OEM_102, cf. ISO102_SCAN_CODE (app.py)
        ["z"] = new(0x2C, false), ["x"] = new(0x2D, false), ["c"] = new(0x2E, false), ["v"] = new(0x2F, false),
        ["b"] = new(0x30, false), ["n"] = new(0x31, false), ["m"] = new(0x32, false),
        [","] = new(0x33, false), ["."] = new(0x34, false), ["/"] = new(0x35, false),

        ["ctrl"] = new(0x1D, false), // gauche/droite non distingués, voir "shift" ci-dessus
        ["winleft"] = new(0x5B, true),
        ["alt"] = new(0x38, false),
        ["altright"] = new(0x38, true), // AltGr
        ["space"] = new(0x39, false),
        ["apps"] = new(0x5D, true),

        ["printscreen"] = new(0x37, true),
        ["scrolllock"] = new(0x46, false),
        // "pause" n'a pas de code de balayage make/break standard en Set 1
        // (séquence E1 spéciale, sans relâchement propre) : approximation
        // best-effort, touche extrêmement rare comme commande de jeu.
        ["pause"] = new(0x45, true),

        ["insert"] = new(0x52, true),
        ["home"] = new(0x47, true),
        ["pageup"] = new(0x49, true),
        ["delete"] = new(0x53, true),
        ["end"] = new(0x4F, true),
        ["pagedown"] = new(0x51, true),
        ["up"] = new(0x48, true),
        ["down"] = new(0x50, true),
        ["left"] = new(0x4B, true),
        ["right"] = new(0x4D, true),

        ["numlock"] = new(0x45, false),
        ["divide"] = new(0x35, true),
        ["multiply"] = new(0x37, false),
        ["subtract"] = new(0x4A, false),
        ["add"] = new(0x4E, false),
        ["decimal"] = new(0x53, false),
        ["num0"] = new(0x52, false), ["num1"] = new(0x4F, false), ["num2"] = new(0x50, false),
        ["num3"] = new(0x51, false), ["num4"] = new(0x4B, false), ["num5"] = new(0x4C, false),
        ["num6"] = new(0x4D, false), ["num7"] = new(0x47, false), ["num8"] = new(0x48, false),
        ["num9"] = new(0x49, false),
    };
}
