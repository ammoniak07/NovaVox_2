namespace NovaVox.App.Hotkeys;

/// <summary>
/// Table nom de touche -> code de touche virtuelle (VK) Windows, pour la
/// surveillance d'état (GetAsyncKeyState) — DISTINCTE de KeyScanCodes
/// (codes de balayage bruts pour SendInput, position physique). Pour les
/// touches "caractère" (lettres/chiffres), Windows traduit déjà le code
/// de balayage matériel en VK selon la disposition clavier ACTIVE : sur
/// AZERTY, la touche physique qui tape "A" produit VK_A — exactement la
/// même sémantique "par caractère" que le module `keyboard` de Python
/// (voir AZERTY_TO_KEYBOARD_LIB, app.py). Aucune table de traduction
/// AZERTY séparée n'est donc nécessaire ici : Windows s'en charge nativement.
/// </summary>
public static class VirtualKeys
{
    public static readonly IReadOnlyDictionary<string, int> Map = BuildMap();

    private static Dictionary<string, int> BuildMap()
    {
        var map = new Dictionary<string, int>
        {
            ["esc"] = 0x1B,
            ["`"] = 0xC0,
            ["-"] = 0xBD,
            ["="] = 0xBB,
            ["backspace"] = 0x08,
            ["tab"] = 0x09,
            ["["] = 0xDB,
            ["]"] = 0xDD,
            ["\\"] = 0xDC,
            ["capslock"] = 0x14,
            [";"] = 0xBA,
            ["'"] = 0xDE,
            ["enter"] = 0x0D,
            ["shift"] = 0x10,
            ["iso102"] = 0xE2,
            [","] = 0xBC,
            ["."] = 0xBE,
            ["/"] = 0xBF,
            ["ctrl"] = 0x11,
            ["winleft"] = 0x5B,
            ["alt"] = 0x12,
            ["altright"] = 0xA5,
            ["space"] = 0x20,
            ["apps"] = 0x5D,
            ["printscreen"] = 0x2C,
            ["scrolllock"] = 0x91,
            ["pause"] = 0x13,
            ["insert"] = 0x2D,
            ["home"] = 0x24,
            ["pageup"] = 0x21,
            ["delete"] = 0x2E,
            ["end"] = 0x23,
            ["pagedown"] = 0x22,
            ["up"] = 0x26,
            ["down"] = 0x28,
            ["left"] = 0x25,
            ["right"] = 0x27,
            ["numlock"] = 0x90,
            ["divide"] = 0x6F,
            ["multiply"] = 0x6A,
            ["subtract"] = 0x6D,
            ["add"] = 0x6B,
            ["decimal"] = 0x6E,
        };

        for (int i = 0; i <= 9; i++)
        {
            map[i.ToString()] = 0x30 + i; // chiffres du clavier principal
            map[$"num{i}"] = 0x60 + i; // pavé numérique
        }
        for (char c = 'a'; c <= 'z'; c++)
            map[c.ToString()] = 0x41 + (c - 'a');

        return map;
    }
}
