using NovaVox.Core.GameLog;

namespace NovaVox.App.ViewModels;

/// <summary>
/// Un schéma de fabrication reçu (Réglages > 📐 Schémas) — port de
/// AiConfig.SchemasReceived, enrichi (fabricant/type/description) via
/// SchemaDatabase quand ce nom y est reconnu. Les trois champs restent
/// null pour un nom inconnu de la base locale : la fiche n'affiche alors
/// que son nom, comme avant.
/// </summary>
public sealed class SchemaRowVm
{
    public required string Name { get; init; }
    public string? Subtitle { get; init; }
    public string? Description { get; init; }

    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);
    public bool HasDescription => !string.IsNullOrEmpty(Description);

    /// <param name="language">Code langue de l'interface (ex. "fr") pour traduire la description — voir SchemaDatabase.Find. Null/anglais = description source.</param>
    public static SchemaRowVm Create(string name, string? language = null)
    {
        var info = SchemaDatabase.Find(name, language);
        return new SchemaRowVm
        {
            Name = name,
            Subtitle = BuildSubtitle(info),
            Description = info?.Description,
        };
    }

    private static string? BuildSubtitle(SchemaInfo? info)
    {
        if (info is null) return null;
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(info.Manufacturer)) parts.Add(info.Manufacturer);
        if (!string.IsNullOrEmpty(info.Type)) parts.Add(info.Type);
        if (info.CraftTimeSeconds is { } seconds) parts.Add($"{FormatCraftTime(seconds)}");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string FormatCraftTime(int seconds)
    {
        var minutes = seconds / 60;
        var rem = seconds % 60;
        return minutes > 0 ? $"{minutes} min {rem:D2} s" : $"{rem} s";
    }
}
