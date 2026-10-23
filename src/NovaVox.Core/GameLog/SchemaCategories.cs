namespace NovaVox.Core.GameLog;

public static class SchemaCategories
{
    public const string Armor = "Armure";
    public const string Clothing = "Vêtement";
    public const string Weapon = "Arme";
    public const string Ammo = "Munition";
    public const string ShipWeapon = "Arme de vaisseau";
    public const string ShipComponent = "Composant de vaisseau";
    public const string Tool = "Outil";
    public const string Misc = "Divers";
    public const string Unknown = "Non reconnu";

    public static readonly IReadOnlyList<string> All =
        new[] { Armor, Clothing, Weapon, Ammo, ShipWeapon, ShipComponent, Tool, Misc, Unknown };

    // Sous-catégories (partie du corps) pour les armures et les vêtements.
    public const string Helmet = "Casque";
    public const string Head = "Tête";
    public const string Torso = "Torse";
    public const string Arms = "Bras";
    public const string Hands = "Mains";
    public const string Legs = "Jambes";
    public const string Feet = "Pieds";
    public const string Backpack = "Sac à dos";
    public const string Undersuit = "Sous-combinaison";
    public const string OtherPart = "Autre";

    /// <summary>Sous-catégories proposées dans le filtre, par catégorie (ordre d'affichage : de la tête aux pieds).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SubcategoriesByCategory =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [Armor] = new[] { Helmet, Torso, Arms, Legs, Backpack, Undersuit, OtherPart },
            [Clothing] = new[] { Head, Torso, Hands, Legs, Feet, Backpack, OtherPart },
        };

    /// <summary>
    /// Partie du corps d'une armure ou d'un vêtement, d'après son type
    /// (Char_Armor_Helmet, Char_Clothing_Torso_1...). Null pour les autres
    /// catégories (armes, composants...), qui n'ont pas de sous-catégorie.
    /// </summary>
    public static string? SubcategoryOf(string? type)
    {
        if (string.IsNullOrEmpty(type)) return null;
        string part;
        bool armor;
        if (type.StartsWith("Char_Armor_", StringComparison.Ordinal)) { part = type["Char_Armor_".Length..]; armor = true; }
        else if (type.StartsWith("Char_Clothing_", StringComparison.Ordinal)) { part = type["Char_Clothing_".Length..]; armor = false; }
        else return null;

        var underscore = part.IndexOf('_');
        if (underscore >= 0) part = part[..underscore]; // Torso_0, Torso_1 -> Torso
        return part switch
        {
            "Helmet" => armor ? Helmet : Head,
            "Hat" or "Head" => Head,
            "Torso" or "Core" => Torso,
            "Arms" => Arms,
            "Hands" or "Gloves" => Hands,
            "Legs" => Legs,
            "Feet" or "Boots" => Feet,
            "Backpack" => Backpack,
            "Undersuit" => Undersuit,
            _ => OtherPart,
        };
    }

    public static string Of(string? type)
    {
        if (string.IsNullOrEmpty(type)) return Unknown;
        if (type.StartsWith("Char_Armor_", StringComparison.Ordinal)) return Armor;
        if (type.StartsWith("Char_Clothing_", StringComparison.Ordinal)) return Clothing;
        return type switch
        {
            "WeaponPersonal" => Weapon,
            "WeaponAttachment" => Ammo,
            "WeaponGun" => ShipWeapon,
            "Cooler" or "PowerPlant" or "Shield" or "Radar" or "QuantumDrive"
                or "DockingCollar" or "Container" or "Cargo" => ShipComponent,
            "WeaponMining" or "MiningModifier" or "SalvageHead" or "SalvageModifier" or "TractorBeam" => Tool,
            _ => Misc,
        };
    }
}
