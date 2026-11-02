using System.Text.RegularExpressions;

namespace NovaVox.Core.Combat;

/// <summary>Traduction mot à mot des noms d'emplacements du jeu (« turret top › weapon left upper »).</summary>
public static class CombatLabels
{
    private static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["turret"] = "tourelle", ["turrets"] = "tourelles", ["weapon"] = "arme", ["weapons"] = "armes", ["gun"] = "canon", ["guns"] = "canons",
        ["left"] = "gauche", ["right"] = "droite", ["upper"] = "haut", ["lower"] = "bas", ["top"] = "dessus", ["bottom"] = "dessous",
        ["front"] = "avant", ["rear"] = "arrière", ["back"] = "arrière", ["nose"] = "nez", ["wing"] = "aile", ["wings"] = "ailes",
        ["body"] = "fuselage", ["side"] = "côté", ["center"] = "centre", ["centre"] = "centre", ["middle"] = "milieu",
        ["missile"] = "missile", ["missiles"] = "missiles", ["missilerack"] = "lance-missiles", ["rack"] = "lance-missiles",
        ["torpedo"] = "torpilles", ["remote"] = "téléguidée", ["manned"] = "pilotée", ["pylon"] = "pylône", ["inner"] = "intérieur",
        ["outer"] = "extérieur", ["chin"] = "menton", ["tail"] = "queue", ["belly"] = "ventre", ["dorsal"] = "dorsale",
        ["ventral"] = "ventrale", ["pdc"] = "défense rapprochée", ["fixed"] = "fixe", ["mount"] = "support", ["class"] = "classe",
    };

    public static string French(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return label;
        // « missile rack » -> un seul mot avant la traduction.
        var text = Regex.Replace(label, @"missile\s+rack", "missilerack", RegexOptions.IgnoreCase);
        var translated = Regex.Replace(text, @"[A-Za-z]+", m => Words.TryGetValue(m.Value, out var fr) ? fr : m.Value);
        return translated.Length > 0 ? char.ToUpper(translated[0]) + translated[1..] : translated;
    }
}
