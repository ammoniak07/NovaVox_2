namespace NovaVox.App.Input;

/// <summary>
/// Une touche du clavier interactif : "Value" est la même chaîne que
/// KeyScanCodes.Map/VirtualKeys.Map (une POSITION physique, pas un
/// caractère — voir le commentaire de KeyScanCodes), "Label" son libellé
/// QWERTY de base affiché par défaut.
/// </summary>
public sealed record VirtualKey(string Label, string Value, bool IsModifier = false, bool Wide = false, bool Wider = false, bool Spacebar = false, bool Ghost = false);

/// <summary>
/// Disposition visuelle du clavier interactif de sélection des touches de
/// commande — port pixel-pour-pixel de KB_LAYOUT/KB_NAV_LAYOUT/
/// KB_NUMPAD_LAYOUT/KB_MOUSE_LAYOUT/KB_LAYOUTS (gui/script.js). Les
/// "Value" ne changent JAMAIS selon la disposition affichée (AZERTY/
/// QWERTY) : seul le libellé change, pour correspondre à ce que
/// l'utilisateur voit sur son clavier physique, alors que la touche
/// réellement envoyée au jeu reste la même position physique.
/// </summary>
public static class VirtualKeyboardLayout
{
    public static readonly string[] ModifierOrder = { "ctrl", "alt", "shift", "altright" };

    public static readonly VirtualKey[][] MainRows =
    {
        new VirtualKey[]
        {
            new("Esc", "esc"),
            new("F1", "f1"), new("F2", "f2"), new("F3", "f3"), new("F4", "f4"),
            new("F5", "f5"), new("F6", "f6"), new("F7", "f7"), new("F8", "f8"),
            new("F9", "f9"), new("F10", "f10"), new("F11", "f11"), new("F12", "f12"),
        },
        new VirtualKey[]
        {
            new("`", "`"),
            new("1", "1"), new("2", "2"), new("3", "3"), new("4", "4"), new("5", "5"),
            new("6", "6"), new("7", "7"), new("8", "8"), new("9", "9"), new("0", "0"),
            new("-", "-"), new("=", "="),
            new("←", "backspace", Wide: true),
        },
        new VirtualKey[]
        {
            new("Tab", "tab", Wide: true),
            new("Q", "q"), new("W", "w"), new("E", "e"), new("R", "r"), new("T", "t"),
            new("Y", "y"), new("U", "u"), new("I", "i"), new("O", "o"), new("P", "p"),
            new("[", "["), new("]", "]"), new("\\", "\\"),
        },
        new VirtualKey[]
        {
            new("Verr.Maj", "capslock", Wide: true),
            new("A", "a"), new("S", "s"), new("D", "d"), new("F", "f"), new("G", "g"),
            new("H", "h"), new("J", "j"), new("K", "k"), new("L", "l"),
            new(";", ";"), new("'", "'"),
            new("Entrée", "enter", Wide: true),
        },
        new VirtualKey[]
        {
            new("Shift", "shift", IsModifier: true, Wide: true),
            new("<>\\", "iso102"),
            new("Z", "z"), new("X", "x"), new("C", "c"), new("V", "v"), new("B", "b"),
            new("N", "n"), new("M", "m"),
            new(",", ","), new(".", "."), new("/", "/"),
            new("Shift", "shift", IsModifier: true, Wide: true),
        },
        new VirtualKey[]
        {
            new("Ctrl", "ctrl", IsModifier: true),
            new("⊞", "winleft"),
            new("Alt", "alt", IsModifier: true),
            new("Espace", "space", Spacebar: true),
            new("AltGr", "altright", IsModifier: true),
            new("⊞", "winleft"),
            new("Menu", "apps"),
            new("Ctrl", "ctrl", IsModifier: true),
        },
    };

    public static readonly VirtualKey[][] NavRows =
    {
        new VirtualKey[] { new("Impr", "printscreen"), new("Défil", "scrolllock"), new("Pause", "pause") },
        new VirtualKey[] { new("Ins", "insert"), new("Orig.", "home"), new("PgUp", "pageup") },
        new VirtualKey[] { new("Suppr", "delete"), new("Fin", "end"), new("PgDn", "pagedown") },
        new VirtualKey[] { new("", "", Ghost: true) },
        new VirtualKey[] { new("", "", Ghost: true), new("▲", "up"), new("", "", Ghost: true) },
        new VirtualKey[] { new("◄", "left"), new("▼", "down"), new("►", "right") },
    };

    public static readonly VirtualKey[][] NumpadRows =
    {
        new VirtualKey[] { new("", "", Ghost: true) },
        new VirtualKey[] { new("Verr.Num", "numlock", Wide: true), new("/", "divide"), new("*", "multiply") },
        new VirtualKey[] { new("7", "num7"), new("8", "num8"), new("9", "num9"), new("-", "subtract") },
        new VirtualKey[] { new("4", "num4"), new("5", "num5"), new("6", "num6"), new("+", "add") },
        new VirtualKey[] { new("1", "num1"), new("2", "num2"), new("3", "num3") },
        new VirtualKey[] { new("0", "num0", Wide: true), new(",", "decimal"), new("↵", "enter") },
    };

    public static readonly VirtualKey[] MouseRow =
    {
        new("🖱 Gauche", "mouseleft", Wider: true),
        new("🖱 Droit", "mouseright", Wider: true),
        new("🖱 Molette", "mousemiddle", Wider: true),
    };

    // AZERTY France et Belgique partagent la même disposition des lettres
    // (A/Q et Z/W intervertis, M remonté d'une rangée, virgule/point-virgule
    // permutés) ; seules quelques touches de ponctuation diffèrent d'un
    // clavier à l'autre selon le modèle exact — voir AZERTY_LETTERS (script.js).
    private static readonly Dictionary<string, string> AzertyLetters = new()
    {
        ["q"] = "A", ["w"] = "Z", ["a"] = "Q", ["z"] = "W",
        [";"] = "M", ["m"] = ",", [","] = ";", ["."] = ":", ["/"] = "!",
        ["'"] = "Ù", ["`"] = "²", ["["] = "^", ["]"] = "$",
        ["-"] = ")",
        ["1"] = "&", ["2"] = "é", ["3"] = "\"", ["4"] = "'", ["5"] = "(",
        ["7"] = "è", ["9"] = "ç", ["0"] = "à",
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LayoutOverrides = new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["qwerty"] = new Dictionary<string, string>(),
        ["azerty_fr"] = Merge(AzertyLetters, new() { ["\\"] = "*", ["6"] = "-", ["8"] = "_", ["iso102"] = "< >" }),
        ["azerty_be"] = Merge(AzertyLetters, new() { ["\\"] = "µ", ["="] = "-", ["/"] = "=", ["6"] = "§", ["8"] = "!", ["iso102"] = "< > \\" }),
    };

    private static Dictionary<string, string> Merge(Dictionary<string, string> baseMap, Dictionary<string, string> overrides)
    {
        var merged = new Dictionary<string, string>(baseMap);
        foreach (var (k, v) in overrides) merged[k] = v;
        return merged;
    }

    private static readonly IReadOnlyDictionary<string, string> BaseLabels = BuildBaseLabels();

    private static Dictionary<string, string> BuildBaseLabels()
    {
        var labels = new Dictionary<string, string>();
        foreach (var row in MainRows) foreach (var key in row) if (!key.Ghost) labels[key.Value] = key.Label;
        foreach (var row in NavRows) foreach (var key in row) if (!key.Ghost) labels[key.Value] = key.Label;
        foreach (var row in NumpadRows) foreach (var key in row) if (!key.Ghost) labels[key.Value] = key.Label;
        foreach (var key in MouseRow) labels[key.Value] = key.Label;
        return labels;
    }

    /// <summary>Libellé affiché pour une "value" selon la disposition choisie (azerty_fr/azerty_be/qwerty) — voir keyDisplayLabel (script.js).</summary>
    public static string DisplayLabel(string value, string layoutKey)
    {
        if (LayoutOverrides.TryGetValue(layoutKey, out var overrides) && overrides.TryGetValue(value, out var label)) return label;
        return BaseLabels.TryGetValue(value, out var baseLabel) ? baseLabel : value.ToUpperInvariant();
    }
}
