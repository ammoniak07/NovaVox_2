using System.Text.Json;

namespace NovaVox.App.Combat;

/// <summary>
/// Derniers choix de la fenêtre Pénétration (onglet, arme, vaisseaux,
/// filtres), repris à la réouverture. Fichier penetration_view.json à côté
/// des autres réglages ; simple confort, jamais bloquant s'il est absent ou
/// abîmé.
/// </summary>
public sealed class PenetrationViewState
{
    public int Tab { get; set; }

    // Onglet Attaque
    public string? AttackShipKey { get; set; }
    public int WeaponKind { get; set; } = 1;
    public int WeaponSize { get; set; }
    public string? Weapon { get; set; }
    public int WeaponCount { get; set; } = 1;

    // Onglet Défense
    public string? DefenseShipKey { get; set; }
    public bool PilotOnly { get; set; } = true;
    public bool ShowDetails { get; set; }
    public int AttackerSize { get; set; }

    private static string FilePath => Path.Combine(NovaVox.Core.NovaVoxPaths.BaseDirectory, "penetration_view.json");

    public static PenetrationViewState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<PenetrationViewState>(File.ReadAllText(FilePath)) ?? new PenetrationViewState();
        }
        catch
        {
            // Fichier abîmé : on repart des choix par défaut.
        }
        return new PenetrationViewState();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Dossier en lecture seule : tant pis, ce n'est que du confort.
        }
    }

    /// <summary>Clé d'un choix « vaisseau » (⭐ mon vaisseau ou modèle d'origine).</summary>
    public static string? KeyOf(object? tag) => tag switch
    {
        NovaVox.Core.Combat.MyShipLoadout l => "mine:" + l.Name,
        NovaVox.Core.Combat.CombatShip s => "ship:" + s.Name,
        _ => null,
    };
}
