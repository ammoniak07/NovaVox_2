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
