using System.Text.RegularExpressions;

namespace NovaVox.Core.Tts;

/// <summary>
/// Retire la mise en forme Markdown qu'un modèle d'IA ajoute parfois avant
/// d'envoyer un texte à la synthèse vocale — port de
/// Api._strip_markdown_for_speech (app.py). Piper lit les symboles
/// littéralement ("*" est par exemple prononcé "astérisque") ; ne touche
/// que le texte envoyé à la voix, pas celui affiché à l'écran.
/// </summary>
public static partial class TtsTextSanitizer
{
    [GeneratedRegex("`+")]
    private static partial Regex Backticks();

    [GeneratedRegex(@"(?m)^#{1,6}\s*")]
    private static partial Regex Headings();

    [GeneratedRegex(@"(?m)^[ \t]*[-*+]\s+")]
    private static partial Regex BulletPoints();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex("__(.+?)__")]
    private static partial Regex BoldUnderscore();

    [GeneratedRegex(@"(?<!\w)\*(.+?)\*(?!\w)")]
    private static partial Regex Italic();

    [GeneratedRegex(@"(?<!\w)_(.+?)_(?!\w)")]
    private static partial Regex ItalicUnderscore();

    [GeneratedRegex("[*_#`]")]
    private static partial Regex StraySymbols();

    public static string StripMarkdownForSpeech(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";

        var result = text;
        result = Backticks().Replace(result, "");
        result = Headings().Replace(result, "");
        result = BulletPoints().Replace(result, "");
        result = Bold().Replace(result, "$1");
        result = BoldUnderscore().Replace(result, "$1");
        result = Italic().Replace(result, "$1");
        result = ItalicUnderscore().Replace(result, "$1");
        // Symboles restants isolés (rares, mais au cas où) : retirés
        // plutôt que de risquer que Piper les épelle.
        result = StraySymbols().Replace(result, "");
        return result;
    }
}
