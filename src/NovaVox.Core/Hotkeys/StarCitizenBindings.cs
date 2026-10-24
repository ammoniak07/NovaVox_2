using System.Xml.Linq;

namespace NovaVox.Core.Hotkeys;

/// <summary>Un bouton de joystick trouvé dans les assignations du jeu (Button : index DirectInput, à partir de 0).</summary>
public sealed record GameJoystickButton(int Instance, string ProductName, string? ProductGuid, int Button, string ActionName);

/// <summary>
/// Lit les assignations de touches de Star Citizen (actionmaps.xml) pour
/// retrouver, à partir de la touche clavier d'une commande NovaVox, le ou
/// les boutons de joystick assignés à la même action du jeu.
///
/// Le fichier ne contient que les assignations MODIFIÉES par le joueur
/// (les valeurs par défaut sont dans Data.p4k, non lu ici) : une action
/// laissée par défaut côté clavier ou côté joystick n'est pas trouvée.
/// Les noms de touches du jeu (kb1_q, kb1_np_1...) désignent des positions
/// physiques QWERTY, comme les noms de touches de NovaVox.
/// </summary>
public sealed class StarCitizenBindings
{
    private sealed record GameAction(string Name, List<string> KeyboardInputs, List<int[]> JoystickButtons);

    private readonly List<GameAction> _actions = new();
    private readonly Dictionary<int, (string Name, string? Guid)> _joysticks = new();

    public int ActionCount => _actions.Count;

    /// <summary>Chemin de actionmaps.xml de l'installation du Game.log donné (même canal LIVE/PTU), null si absent.</summary>
    public static string? FindActionMapsPath(string? gameLogPath)
    {
        if (string.IsNullOrEmpty(gameLogPath)) return null;
        var dir = Path.GetDirectoryName(gameLogPath);
        if (string.IsNullOrEmpty(dir)) return null;
        var path = Path.Combine(dir, "user", "client", "0", "Profiles", "default", "actionmaps.xml");
        return File.Exists(path) ? path : null;
    }

    public static StarCitizenBindings Load(string path)
    {
        // Partage large : le jeu peut avoir le fichier ouvert pendant qu'on le lit.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static StarCitizenBindings Parse(string xml)
    {
        var result = new StarCitizenBindings();
        var doc = XDocument.Parse(xml);

        foreach (var opt in doc.Descendants("options"))
        {
            if (!string.Equals((string?)opt.Attribute("type"), "joystick", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse((string?)opt.Attribute("instance"), out var instance)) continue;
            var product = ((string?)opt.Attribute("Product") ?? "").Trim();
            string? guid = null;
            var brace = product.LastIndexOf('{');
            if (brace >= 0 && product.EndsWith('}'))
            {
                if (Guid.TryParse(product[(brace + 1)..^1], out var g)) guid = g.ToString();
                product = product[..brace].Trim();
            }
            result._joysticks.TryAdd(instance, (product, guid));
        }

        foreach (var actionEl in doc.Descendants("action"))
        {
            var name = (string?)actionEl.Attribute("name") ?? "";
            var action = new GameAction(name, new List<string>(), new List<int[]>());
            foreach (var rebind in actionEl.Elements("rebind"))
            {
                // Double appui (multiTap) : un appui simple ne correspond pas.
                if (int.TryParse((string?)rebind.Attribute("multiTap"), out var taps) && taps > 1) continue;
                var input = ((string?)rebind.Attribute("input") ?? "").Trim().ToLowerInvariant();
                if (input.StartsWith("kb", StringComparison.Ordinal))
                {
                    var key = NormalizeGameKeyboard(input);
                    if (key is not null) action.KeyboardInputs.Add(key);
                }
                else if (TryParseJoystickButton(input, out var instance, out var button))
                {
                    action.JoystickButtons.Add(new[] { instance, button });
                }
            }
            if (action.KeyboardInputs.Count > 0 && action.JoystickButtons.Count > 0)
                result._actions.Add(action);
        }
        return result;
    }

    /// <summary>Boutons de joystick assignés à la même action que la touche clavier NovaVox donnée ("alt+f", "num1"...).</summary>
    public IReadOnlyList<GameJoystickButton> FindJoystickButtonsForKeys(string? novaKeys)
    {
        var wanted = NormalizeNovaKeys(novaKeys);
        if (wanted is null) return Array.Empty<GameJoystickButton>();
        var found = new List<GameJoystickButton>();
        foreach (var action in _actions)
        {
            if (!action.KeyboardInputs.Contains(wanted)) continue;
            foreach (var jb in action.JoystickButtons)
            {
                var (name, guid) = _joysticks.TryGetValue(jb[0], out var info) ? info : ("", null);
                var button = new GameJoystickButton(jb[0], name, guid, jb[1], action.Name);
                if (!found.Any(f => f.Instance == button.Instance && f.Button == button.Button)) found.Add(button);
            }
        }
        return found;
    }

    // "js1_button5" -> instance 1, bouton DirectInput 4 (le jeu compte à partir de 1).
    private static bool TryParseJoystickButton(string input, out int instance, out int button)
    {
        instance = 0;
        button = 0;
        if (!input.StartsWith("js", StringComparison.Ordinal)) return false;
        var sep = input.IndexOf('_');
        if (sep < 0 || !int.TryParse(input[2..sep], out instance)) return false;
        var rest = input[(sep + 1)..];
        if (!rest.StartsWith("button", StringComparison.Ordinal) || !int.TryParse(rest["button".Length..], out var oneBased) || oneBased < 1) return false;
        button = oneBased - 1;
        return true;
    }

    // Forme commune comparée des deux côtés : modificateurs triés ("alt", "altright", "ctrl", "shift") + touche, séparés par "+".
    private static string? Canonical(IEnumerable<string> modifiers, string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return string.Join("+", modifiers.Distinct().OrderBy(m => m, StringComparer.Ordinal).Append(key));
    }

    internal static string? NormalizeGameKeyboard(string input)
    {
        var sep = input.IndexOf('_');
        if (sep < 0) return null;
        var parts = input[(sep + 1)..].Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        var mods = new List<string>();
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var mod = parts[i] switch
            {
                "lalt" => "alt",
                "ralt" => "altright",
                "lctrl" or "rctrl" => "ctrl",
                "lshift" or "rshift" => "shift",
                _ => null,
            };
            if (mod is null) return null;
            mods.Add(mod);
        }
        return Canonical(mods, GameKeyNames.TryGetValue(parts[^1], out var nova) ? nova : parts[^1]);
    }

    internal static string? NormalizeNovaKeys(string? keys)
    {
        if (string.IsNullOrWhiteSpace(keys)) return null;
        var parts = keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => p.ToLowerInvariant()).ToList();
        var mods = parts.Where(p => p is "ctrl" or "shift" or "alt" or "altright").ToList();
        var main = parts.Where(p => p is not ("ctrl" or "shift" or "alt" or "altright" or "winleft")).ToList();
        if (main.Count != 1 || parts.Contains("winleft")) return null;
        return Canonical(mods, main[0]);
    }

    // Noms du jeu -> noms NovaVox (identiques sinon : lettres, chiffres, f1...).
    private static readonly Dictionary<string, string> GameKeyNames = new(StringComparer.Ordinal)
    {
        ["escape"] = "esc",
        ["grave"] = "`",
        ["minus"] = "-",
        ["equals"] = "=",
        ["lbracket"] = "[",
        ["rbracket"] = "]",
        ["backslash"] = "\\",
        ["semicolon"] = ";",
        ["apostrophe"] = "'",
        ["comma"] = ",",
        ["period"] = ".",
        ["slash"] = "/",
        ["pgup"] = "pageup",
        ["pgdn"] = "pagedown",
        ["print"] = "printscreen",
        ["np_add"] = "add",
        ["np_subtract"] = "subtract",
        ["np_multiply"] = "multiply",
        ["np_divide"] = "divide",
        ["np_period"] = "decimal",
        ["np_0"] = "num0", ["np_1"] = "num1", ["np_2"] = "num2", ["np_3"] = "num3", ["np_4"] = "num4",
        ["np_5"] = "num5", ["np_6"] = "num6", ["np_7"] = "num7", ["np_8"] = "num8", ["np_9"] = "num9",
    };
}
