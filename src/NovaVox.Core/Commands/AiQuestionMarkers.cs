using System.Text.RegularExpressions;

namespace NovaVox.Core.Commands;

/// <summary>
/// Tournures interrogatives typiques par langue d'interface — port de
/// AI_QUESTION_MARKER_RE_BY_LANG (app.py). Une phrase qui matche est
/// présumée être une vraie question ("c'est quoi le bouclier ?"), pas un
/// ordre d'exécuter une commande du même nom.
/// </summary>
public static partial class AiQuestionMarkers
{
    [GeneratedRegex(@"c[\s']?est quoi|c[\s']?est o[uù]\b|qu[\s']?est[\s-]?ce que|qu[\s']?est[\s-]?ce qui|quel(?:le)?s? (?:touche|est|sont|bouton)|\bcomment\b|\bpourquoi\b|\bcombien\b|[aà] quoi sert|\bo[uù] est\b")]
    private static partial Regex Fr();

    [GeneratedRegex(@"\bwhat'?s\b|\bwhat is\b|\bwhere'?s\b|\bwhere is\b|\bwhich (?:key|button)\b|\bhow\b|\bwhy\b")]
    private static partial Regex En();

    [GeneratedRegex(@"\bwat is\b|\bwaar is\b|\bwelke? (?:toets|knop|is|zijn)\b|\bhoe\b|\bwaarom\b|\bhoeveel\b|\bwaarvoor\b")]
    private static partial Regex Nl();

    [GeneratedRegex(@"qu[ée] es\b|d[oó]nde est[aá]\b|qu[ée] (?:tecla|bot[oó]n)\b|\bc[oó]mo\b|\bpor ?qu[ée]\b|\bcu[aá]nto\b|para qu[ée] sirve\b")]
    private static partial Regex Es();

    [GeneratedRegex(@"cos['\s]?[eè]\b|dov['\s]?[eè]\b|quale? (?:tasto|pulsante)\b|\bcome\b|\bperch[eé]\b|\bquant[oi]\b|a cosa serve\b")]
    private static partial Regex It();

    [GeneratedRegex(@"\bwas ist\b|\bwo ist\b|welche[rs]? (?:taste|knopf)\b|\bwie\b|\bwarum\b|\bwof[uü]r\b")]
    private static partial Regex De();

    public static Regex ForLanguage(string? language) => language switch
    {
        "en" => En(),
        "nl" => Nl(),
        "es" => Es(),
        "it" => It(),
        "de" => De(),
        _ => Fr(),
    };
}
