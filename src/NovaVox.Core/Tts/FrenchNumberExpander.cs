using System.Globalization;
using System.Text.RegularExpressions;

namespace NovaVox.Core.Tts;

/// <summary>Variante de prononciation des nombres 70-79/90-99 — voir FrenchNumberExpander.</summary>
public enum FrenchNumberStyle
{
    /// <summary>soixante-dix, quatre-vingt-dix.</summary>
    France,
    /// <summary>septante, nonante (80 reste "quatre-vingts" dans les deux variantes).</summary>
    Belgique,
}

/// <summary>
/// Convertit les nombres entiers d'un texte en toutes lettres françaises
/// avant synthèse vocale — port de _integer_to_french_words /
/// _expand_numbers_for_speech (app.py).
///
/// CONSTAT (vérifié en usage réel) : Piper épelle chiffre par chiffre tout
/// nombre un peu long faute de séparateur qu'il reconnaisse (ex. "40000"
/// lu "quatre zéro zéro zéro zéro" au lieu de "quarante mille", "1000000"
/// lu "un zéro zéro zéro zéro zéro zéro zéro" au lieu de "un million") —
/// typiquement les montants en aUEC des notifications HUD de dons/amendes.
/// Plutôt que de dépendre du normaliseur de nombres intégré à Piper, cette
/// classe convertit ici tout nombre entier détecté dans un texte en
/// toutes lettres AVANT de l'envoyer à Piper (voir PiperTtsEngine.Speak),
/// qui n'a alors plus jamais à décider comment prononcer un chiffre.
/// </summary>
public static partial class FrenchNumberExpander
{
    private static readonly string[] UnitsWords =
        { "", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf" };

    private static readonly string[] TeenWords =
        { "dix", "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf" };

    // Variante France : 70/90 construits sur soixante/quatre-vingt (soixante-dix,
    // quatre-vingt-dix), gérés à part dans Below100ToWords. Variante Belgique :
    // 70/90 sont des dizaines à part entière (septante, nonante), donc
    // directement dans cette table comme les autres — 80 reste "quatre-vingt"
    // dans les deux variantes (l'huitante/octante belgo-suisse reste trop
    // minoritaire, y compris en Belgique, pour être couvert ici).
    private static readonly IReadOnlyDictionary<int, string> TensWordsFrance = new Dictionary<int, string>
    {
        [2] = "vingt", [3] = "trente", [4] = "quarante", [5] = "cinquante", [6] = "soixante", [8] = "quatre-vingt",
    };

    private static readonly IReadOnlyDictionary<int, string> TensWordsBelgique = new Dictionary<int, string>
    {
        [2] = "vingt", [3] = "trente", [4] = "quarante", [5] = "cinquante", [6] = "soixante",
        [7] = "septante", [8] = "quatre-vingt", [9] = "nonante",
    };

    /// <summary>
    /// Nombre groupé par milliers avec un espace (normal ou insécable, les
    /// deux étant vus en pratique selon la source du texte) comme
    /// séparateur, ex. "40 000" ou "1 234 567" — reconnu et "dégroupé"
    /// AVANT la conversion chiffre-par-chiffre, sans quoi "40 000" serait
    /// lu comme deux nombres séparés ("quarante", puis "zéro").
    /// </summary>
    [GeneratedRegex(@"\d{1,3}(?:[  ]\d{3})+")]
    private static partial Regex GroupedNumberRegex();

    [GeneratedRegex(@"\b\d+\b")]
    private static partial Regex PlainNumberRegex();

    [GeneratedRegex(@"[  ]")]
    private static partial Regex GroupSeparatorRegex();

    /// <summary>Convertit la valeur persistée dans AiConfig.FrenchNumberStyle ("france"/"belgique") en <see cref="FrenchNumberStyle"/>.</summary>
    public static FrenchNumberStyle ParseStyle(string? value) =>
        value == "belgique" ? FrenchNumberStyle.Belgique : FrenchNumberStyle.France;

    /// <summary>
    /// Remplace tout nombre entier détecté dans le texte par son écriture
    /// en toutes lettres françaises, pour que Piper le prononce
    /// correctement au lieu de l'épeler chiffre par chiffre.
    /// </summary>
    public static string Expand(string? text, FrenchNumberStyle style = FrenchNumberStyle.France)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";

        var result = GroupedNumberRegex().Replace(text, m =>
        {
            var digits = GroupSeparatorRegex().Replace(m.Value, "");
            return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                ? IntegerToWords(n, style)
                : m.Value;
        });

        return PlainNumberRegex().Replace(result, m =>
        {
            var digits = m.Value;
            if (digits.Length > 1 && digits[0] == '0')
            {
                // Zéro(s) non significatif en tête (ex. "007", horaire) :
                // pas un vrai cardinal, le convertir donnerait un résultat faux.
                return digits;
            }
            return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                ? IntegerToWords(n, style)
                : digits;
        });
    }

    /// <summary>
    /// Convertit un entier (positif, négatif ou nul) en toutes lettres
    /// françaises — France (soixante-dix/quatre-vingt-dix) ou Belgique
    /// (septante/nonante).
    /// </summary>
    public static string IntegerToWords(long n, FrenchNumberStyle style = FrenchNumberStyle.France)
    {
        if (n == 0) return "zéro";
        if (n < 0) return "moins " + IntegerToWords(-n, style);

        var billions = n / 1_000_000_000;
        var rem = n % 1_000_000_000;
        var millions = rem / 1_000_000;
        rem %= 1_000_000;
        var thousands = rem / 1000;
        var units = rem % 1000;

        var parts = new List<string>();
        if (billions > 0)
            parts.Add(GroupToWords((int)billions, allowPluralExceptions: false, style) + (billions == 1 ? " milliard" : " milliards"));
        if (millions > 0)
            parts.Add(GroupToWords((int)millions, allowPluralExceptions: false, style) + (millions == 1 ? " million" : " millions"));
        if (thousands > 0)
            parts.Add(thousands == 1 ? "mille" : $"{GroupToWords((int)thousands, allowPluralExceptions: false, style)} mille");
        if (units > 0 || parts.Count == 0)
            parts.Add(GroupToWords((int)units, allowPluralExceptions: true, style));

        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    /// <summary>
    /// Convertit un groupe 0..999 en lettres. allowPluralExceptions n'est
    /// vrai que pour le groupe des unités (0-999) final du nombre complet :
    /// seul celui-ci peut porter le 's' de "cents"/"vingts", qui ne
    /// s'applique jamais quand le groupe est suivi d'un mot comme
    /// "mille"/"million" (ex. "quatre-vingt mille", jamais "quatre-vingts
    /// mille").
    /// </summary>
    private static string GroupToWords(int n, bool allowPluralExceptions, FrenchNumberStyle style)
    {
        var hundreds = n / 100;
        var rem = n % 100;

        var parts = new List<string>();
        if (hundreds > 0)
            parts.Add(hundreds == 1 ? "cent" : $"{UnitsWords[hundreds]} cent");
        if (rem > 0)
            parts.Add(Below100ToWords(rem, style));

        var text = string.Join(" ", parts);
        if (allowPluralExceptions)
        {
            if (rem == 0 && hundreds >= 2) text += "s"; // "deux cents"
            else if (rem == 80) text += "s"; // "quatre-vingts"
        }
        return text;
    }

    /// <summary>n dans 1..99.</summary>
    private static string Below100ToWords(int n, FrenchNumberStyle style)
    {
        if (n < 10) return UnitsWords[n];
        if (n < 20) return TeenWords[n - 10];
        if (style == FrenchNumberStyle.France)
        {
            if (n is >= 70 and < 80) // soixante-dix..soixante-dix-neuf ("et" seulement à 71)
            {
                var rem = n - 60;
                return rem == 11 ? "soixante et onze" : $"soixante-{TeenWords[rem - 10]}";
            }
            if (n is >= 90 and < 100) // quatre-vingt-dix..quatre-vingt-dix-neuf (jamais de "et")
                return $"quatre-vingt-{TeenWords[n - 90]}";
        }

        var tens = n / 10;
        var units = n % 10;
        var baseWord = (style == FrenchNumberStyle.Belgique ? TensWordsBelgique : TensWordsFrance)[tens];
        if (units == 0) return baseWord;
        if (units == 1 && tens != 8) return $"{baseWord} et un"; // sauf après quatre-vingt (quatre-vingt-un)
        return $"{baseWord}-{UnitsWords[units]}";
    }
}
