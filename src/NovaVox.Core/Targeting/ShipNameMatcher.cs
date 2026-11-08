using System.Text;
using NovaVox.Core.Combat;

namespace NovaVox.Core.Targeting;

/// <summary>Vaisseau reconnu dans le texte lu à l'écran (OCR du HUD).</summary>
public sealed record ShipMatch(CombatShip Ship, double Score, string MatchedText);

/// <summary>
/// Retrouve le vaisseau ciblé à partir du texte lu par l'OCR dans la zone
/// du HUD où le jeu affiche le nom de la cible. Tolère les erreurs typiques
/// de l'OCR (0/O, 1/I/L, 5/S, 8/B, lettres manquantes) et le nom du
/// fabricant devant ou non (« Drake Cutlass Black », « CUTLASS BLACK »).
/// Le nom le plus long qui colle gagne (« Gladius Pirate » avant « Gladius »).
/// </summary>
public static class ShipNameMatcher
{
    /// <summary>Score minimal (0 à 1) pour accepter une correspondance approchée.</summary>
    public const double MinScore = 0.82;

    private sealed record Candidate(CombatShip Ship, string Key, string Display)
    {
        public string Compact { get; } = Key.Replace(" ", "");
        public int[] Letters { get; } = LetterCounts(Key.Replace(" ", ""));
    }

    private static int[] LetterCounts(string text)
    {
        var counts = new int[36];
        foreach (var c in text)
        {
            if (c is >= 'A' and <= 'Z') counts[c - 'A']++;
            else if (c is >= '0' and <= '9') counts[26 + c - '0']++;
        }
        return counts;
    }

    /// <summary>Part des lettres du nom présentes dans le texte (filtre rapide avant le calcul approché).</summary>
    private static double LetterOverlap(int[] key, int[] text)
    {
        int shared = 0, total = 0;
        for (var i = 0; i < key.Length; i++)
        {
            shared += Math.Min(key[i], text[i]);
            total += key[i];
        }
        return total == 0 ? 0 : (double)shared / total;
    }

    private static readonly Lazy<IReadOnlyList<Candidate>> Candidates = new(BuildCandidates);

    private static IReadOnlyList<Candidate> BuildCandidates()
    {
        var list = new List<Candidate>();
        foreach (var ship in CombatDatabase.Ships)
        {
            foreach (var name in new[] { ship.Name, ship.FullName }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var key = Canonical(name);
                if (key.Length >= 3) list.Add(new Candidate(ship, key, name));
            }
        }
        return list;
    }

    /// <summary>
    /// Forme de comparaison : majuscules, sans accents ni ponctuation, et
    /// caractères que l'OCR confond ramenés à un seul (0→O, 1/L→I, 5→S, 8→B).
    /// Appliquée des deux côtés, elle ne change pas l'identité d'un nom.
    /// </summary>
    public static string Canonical(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var raw in text.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(raw) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            var c = char.ToUpperInvariant(raw);
            c = c switch
            {
                '0' => 'O',
                '1' or 'L' or '|' or '!' => 'I',
                '5' => 'S',
                '8' => 'B',
                _ => c,
            };
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Meilleur vaisseau pour ces lignes de texte (null si rien de fiable).
    /// Chaque ligne est testée seule, puis avec la suivante (un nom peut être
    /// coupé sur deux lignes dans le HUD).
    /// </summary>
    public static ShipMatch? Match(IEnumerable<string> lines)
    {
        var texts = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
        var windows = new List<string>(texts);
        for (var i = 0; i + 1 < texts.Count; i++) windows.Add(texts[i] + " " + texts[i + 1]);

        var prepared = windows.Select(w => Canonical(w).Replace(" ", "")).Where(t => t.Length >= 3).Distinct().ToList();
        ShipMatch? best = null;

        // 1) Correspondances exactes (rapide) : si on en trouve, pas besoin d'approcher.
        foreach (var compact in prepared)
            foreach (var candidate in Candidates.Value)
                if (compact.Contains(candidate.Compact, StringComparison.Ordinal))
                    best = Better(best, candidate, 1.0);
        if (best is not null) return best;

        // 2) Correspondances approchées (lettres mal lues), seulement sur les noms plausibles.
        foreach (var compact in prepared)
        {
            var letters = LetterCounts(compact);
            foreach (var candidate in Candidates.Value)
            {
                if (candidate.Compact.Length < 4 || LetterOverlap(candidate.Letters, letters) < MinScore - 0.05) continue;
                var score = BestWindowSimilarity(compact, candidate.Compact);
                if (score < MinScore) continue;
                best = Better(best, candidate, score);
            }
        }
        return best;
    }

    /// <summary>À score égal, le nom le plus long (le plus précis) l'emporte.</summary>
    private static ShipMatch? Better(ShipMatch? best, Candidate candidate, double score)
    {
        if (best is null || score > best.Score + 1e-9
            || (Math.Abs(score - best.Score) < 1e-9 && candidate.Compact.Length > Canonical(best.MatchedText).Replace(" ", "").Length))
            return new ShipMatch(candidate.Ship, score, candidate.Display);
        return best;
    }

    public static ShipMatch? Match(string text) => Match(text.Split('\n'));

    /// <summary>Similarité entre le nom cherché et le meilleur morceau du texte de même longueur (±2).</summary>
    private static double BestWindowSimilarity(string text, string key)
    {
        if (key.Length < 4) return 0; // trop court pour une correspondance approchée fiable
        var best = 0.0;
        for (var len = Math.Max(1, key.Length - 2); len <= key.Length + 2; len++)
        {
            if (len > text.Length)
            {
                best = Math.Max(best, Similarity(text, key));
                break;
            }
            for (var start = 0; start + len <= text.Length; start++)
                best = Math.Max(best, Similarity(text.Substring(start, len), key));
        }
        return best;
    }

    /// <summary>1 - distance de Levenshtein normalisée.</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return 1.0 - (double)prev[b.Length] / Math.Max(a.Length, b.Length);
    }
}
