using System.ComponentModel;
using System.Runtime.CompilerServices;
using NovaVox.Core.GameLog;

namespace NovaVox.App.ViewModels;

/// <summary>
/// Un schéma de fabrication reçu (Réglages > 📐 Schémas) — port de
/// AiConfig.SchemasReceived, enrichi (fabricant/type/description) via
/// SchemaDatabase quand ce nom y est reconnu. Les trois champs restent
/// null pour un nom inconnu de la base locale : la fiche n'affiche alors
/// que son nom, comme avant.
/// </summary>
public sealed class SchemaRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public required string Name { get; init; }
    public required string Category { get; init; }
    public string? Subtitle { get; init; }
    public string? Stats { get; init; }
    public string? Description { get; init; }
    public string? ImagePath { get; init; }

    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);
    public bool HasStats => !string.IsNullOrEmpty(Stats);
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public bool HasImage => ImagePath is not null;

    /// <summary>Correspond à la recherche du panneau Schémas — voir RefreshSchemasSearchVisibility, MainWindow.xaml.cs.</summary>
    private bool _isNew;
    public bool IsNew { get => _isNew; set { _isNew = value; Raise(); } }

    private bool _rowVisible = true;
    public bool RowVisible { get => _rowVisible; set { _rowVisible = value; Raise(); } }

    /// <param name="language">Code langue de l'interface (ex. "fr") pour traduire la description — voir SchemaDatabase.Find. Null/anglais = description source.</param>
    public static SchemaRowVm Create(string name, string? language = null, bool isNew = false)
    {
        var info = SchemaDatabase.Find(name, language);
        return new SchemaRowVm
        {
            Name = name,
            IsNew = isNew,
            Category = SchemaCategories.Of(info?.Type),
            Subtitle = BuildSubtitle(info),
            Stats = info?.Stats is { Count: > 0 } stats ? string.Join(" · ", stats.Select(SchemaFrenchLabels.Stat)) : null,
            Description = info?.Description,
            ImagePath = info is null ? null : SchemaImages.FindPath(AppContext.BaseDirectory, info.Name),
        };
    }

    private static string? BuildSubtitle(SchemaInfo? info)
    {
        if (info is null) return null;
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(info.Manufacturer)) parts.Add(info.Manufacturer);
        if (!string.IsNullOrEmpty(info.ItemType)) parts.Add(SchemaFrenchLabels.ItemType(info.ItemType));
        else if (!string.IsNullOrEmpty(info.Type)) parts.Add(info.Type);
        if (info.DamageReductionPercent is { } reduction) parts.Add($"Résistance aux dégâts {reduction} %");
        if (info.CapacityMicroScu is { } capacity) parts.Add($"Capacité {(capacity / 1000.0).ToString("0.#", FrenchCulture)}K µSCU");
        if (info.Size is { } size) parts.Add($"Taille {size}");
        if (!string.IsNullOrEmpty(info.Grade)) parts.Add(info.Grade == "Bespoke" ? "Grade sur mesure" : $"Grade {info.Grade}");
        if (!string.IsNullOrEmpty(info.ComponentClass)) parts.Add(SchemaFrenchLabels.Class(info.ComponentClass));
        if (info.CraftTimeSeconds is { } seconds) parts.Add($"{FormatCraftTime(seconds)}");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static readonly System.Globalization.CultureInfo FrenchCulture = new("fr-FR");

    private static string FormatCraftTime(int seconds)
    {
        var minutes = seconds / 60;
        var rem = seconds % 60;
        return minutes > 0 ? $"{minutes} min {rem:D2} s" : $"{rem} s";
    }
}
