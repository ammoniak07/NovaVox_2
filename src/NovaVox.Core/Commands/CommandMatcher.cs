using NovaVox.Core.Text;

namespace NovaVox.Core.Commands;

/// <summary>
/// Port de la logique de reconnaissance des commandes de app.py
/// (_command_trigger_list / _find_strict_command_match /
/// _find_fuzzy_command_match / _is_significant_match).
/// </summary>
public static class CommandMatcher
{
    public const double DefaultAiMatchThreshold = 0.6;

    /// <summary>
    /// Construit la liste des variantes (phrase + synonymes, avec et sans
    /// apostrophes) sur lesquelles comparer le texte reconnu.
    /// </summary>
    public static List<string> BuildTriggerList(VoiceCommand cmd)
    {
        var phraseNorm = cmd.Phrase.ToLowerInvariant().Trim();
        var declencheurs = new List<string>
        {
            phraseNorm,
            phraseNorm.Replace("'", " "),
            phraseNorm.Replace("'", ""),
        };
        foreach (var s in cmd.Synonyms)
        {
            var sClean = s.ToLowerInvariant().Trim();
            declencheurs.Add(sClean);
            declencheurs.Add(sClean.Replace("'", " "));
        }
        return declencheurs.Where(d => d.Length > 0).ToList();
    }

    /// <summary>
    /// Un déclencheur d'un seul mot ne doit pas suffire à lui seul à
    /// déclencher une commande s'il n'est qu'un mot perdu au milieu d'une
    /// phrase plus longue sans rapport. Une expression de plusieurs mots
    /// est déjà assez spécifique en soi.
    /// </summary>
    public static bool IsSignificantMatch(string declencheur, string textNorm)
    {
        if (declencheur.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2)
            return true;
        return declencheur.Length >= 0.5 * textNorm.Length;
    }

    public static int? FindStrictMatch(
        IReadOnlyList<VoiceCommand> commands, string textNorm, bool requireSignificant = false)
    {
        var textSansApostrophe = textNorm.Replace("'", " ");
        int? bestIdx = null;
        int bestLen = -1;
        for (int idx = 0; idx < commands.Count; idx++)
        {
            var cmd = commands[idx];
            if (cmd.IsTitle) continue;
            var declencheurs = BuildTriggerList(cmd);
            if (declencheurs.Count == 0) continue;
            foreach (var d in declencheurs)
            {
                foreach (var candidate in new[] { textNorm, textSansApostrophe })
                {
                    if (candidate.Contains(d) && (!requireSignificant || IsSignificantMatch(d, candidate)))
                    {
                        if (d.Length > bestLen)
                        {
                            bestLen = d.Length;
                            bestIdx = idx;
                        }
                        break;
                    }
                }
            }
        }
        return bestIdx;
    }

    /// <summary>
    /// Cherche la commande dont la phrase/un synonyme ressemble le plus au
    /// texte reconnu (correspondance stricte d'abord, puis ressemblance
    /// approximative). Utilisé quand la phrase suit le nom de l'IA.
    /// </summary>
    public static (int? Index, double Ratio) FindFuzzyMatch(
        IReadOnlyList<VoiceCommand> commands, string textNorm, double threshold = DefaultAiMatchThreshold)
    {
        var strictIdx = FindStrictMatch(commands, textNorm, requireSignificant: true);
        if (strictIdx is not null)
            return (strictIdx, 1.0);

        var textSansApostrophe = textNorm.Replace("'", " ");
        int? bestIdx = null;
        double bestRatio = 0.0;
        for (int idx = 0; idx < commands.Count; idx++)
        {
            var cmd = commands[idx];
            if (cmd.IsTitle) continue;
            foreach (var d in BuildTriggerList(cmd))
            {
                double ratio = Math.Max(
                    new SequenceMatcher(d, textNorm).Ratio(),
                    new SequenceMatcher(d, textSansApostrophe).Ratio());
                if (ratio > bestRatio)
                {
                    bestIdx = idx;
                    bestRatio = ratio;
                }
            }
        }

        if (bestIdx is not null && bestRatio >= threshold)
            return (bestIdx, bestRatio);
        return (null, 0.0);
    }

    /// <summary>Représentation "n → alt+n" de la succession complète des touches d'une commande.</summary>
    public static string CommandKeysLabel(VoiceCommand cmd)
    {
        var parts = new List<string> { cmd.Keys };
        parts.AddRange(cmd.ExtraSteps.Select(s => s.Keys));
        return string.Join(" → ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }
}
