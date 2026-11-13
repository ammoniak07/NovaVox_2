using System.Text.Json;

namespace NovaVox.Core.GameLog;

/// <summary>Une tranche de qualité du jeu : un minerai de qualité <see cref="Start"/> à <see cref="End"/> compte comme <see cref="Value"/>.</summary>
public sealed record QualityBand(int Start, int End, int Value);

/// <summary>
/// Tranches de qualité des ingrédients de fabrication (CraftingQualityQuantization
/// du jeu, ResourceQualityBands.json généré par tools/gen_tranches_qualite.py) :
/// le jeu ne garde pas la qualité exacte d'un minerai, il la range dans une
/// tranche qui compte comme une valeur fixe (Aslarite : 0-399 -> 287,
/// 400-599 -> 575...). C'est cette valeur retenue qui fixe l'effet sur
/// l'objet fabriqué.
/// </summary>
public static class ResourceQualityBands
{
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<QualityBand>>> All = new(Load);

    /// <summary>Tranches de cet ingrédient (nom anglais de la recette, ex. "Aslarite", "Pressurized Ice"), null si inconnues.</summary>
    public static IReadOnlyList<QualityBand>? For(string? ingredient)
    {
        if (string.IsNullOrWhiteSpace(ingredient)) return null;
        if (All.Value.TryGetValue(ingredient.Trim(), out var bands)) return bands;
        // Tables sans minerai minable connu : rangées sous le nom de fichier du jeu ("#yormandieye").
        var fileKey = "#" + new string(ingredient.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return All.Value.GetValueOrDefault(fileKey);
    }

    /// <summary>Valeur retenue par le jeu pour un minerai de qualité <paramref name="quality"/>, ou la qualité telle quelle sans tranches connues.</summary>
    public static int Retained(string? ingredient, int quality)
    {
        var bands = For(ingredient);
        return bands?.FirstOrDefault(b => quality >= b.Start && quality <= b.End)?.Value ?? quality;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<QualityBand>> Load()
    {
        try
        {
            var assembly = typeof(ResourceQualityBands).Assembly;
            var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("ResourceQualityBands.json", StringComparison.Ordinal));
            if (name is null) return new Dictionary<string, IReadOnlyList<QualityBand>>();
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) return new Dictionary<string, IReadOnlyList<QualityBand>>();
            var raw = JsonSerializer.Deserialize<Dictionary<string, int[][]>>(stream) ?? new();
            return raw.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<QualityBand>)kv.Value.Where(b => b.Length >= 3).Select(b => new QualityBand(b[0], b[1], b[2])).OrderBy(b => b.Start).ToList(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, IReadOnlyList<QualityBand>>();
        }
    }
}
