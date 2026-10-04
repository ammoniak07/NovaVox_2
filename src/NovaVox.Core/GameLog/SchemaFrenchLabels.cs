using System.Text.RegularExpressions;

namespace NovaVox.Core.GameLog;

public static class SchemaFrenchLabels
{
    private static readonly Dictionary<string, string> ItemTypes = new()
    {
        ["Light Armor"] = "Armure légère",
        ["Medium Armor"] = "Armure moyenne",
        ["Heavy Armor"] = "Armure lourde",
        ["Super Heavy Armor"] = "Armure super lourde",
        ["Flight Helmet"] = "Casque de vol",
        ["Racing Helmet"] = "Casque de course",
        ["Undersuit"] = "Combinaison",
        ["Flight Suit"] = "Combinaison de vol",
        ["Racing Flight Suit"] = "Combinaison de course",
        ["Light Backpack"] = "Sac à dos léger",
        ["Medium Backpack"] = "Sac à dos moyen",
        ["Heavy Backpack"] = "Sac à dos lourd",
        ["Ammo Carrier"] = "Porte-munitions",
        ["Heavy Utility"] = "Utilitaire lourd",
        ["Pistol"] = "Pistolet",
        ["Frag Pistol"] = "Pistolet à fragmentation",
        ["SMG"] = "Pistolet-mitrailleur",
        ["Rifle"] = "Fusil",
        ["Assault Rifle"] = "Fusil d'assaut",
        ["Sniper Rifle"] = "Fusil de précision",
        ["Shotgun"] = "Fusil à pompe",
        ["LMG"] = "Mitrailleuse légère",
        ["HMG"] = "Mitrailleuse lourde",
        ["Crossbow"] = "Arbalète",
        ["Magazine"] = "Chargeur",
        ["Battery"] = "Batterie",
        ["Ballistic Cannon"] = "Canon balistique",
        ["Ballistic Gatling"] = "Gatling balistique",
        ["Ballistic Repeater"] = "Répéteur balistique",
        ["Ballistic Scattergun"] = "Canon à dispersion balistique",
        ["Laser Cannon"] = "Canon laser",
        ["Laser Gatling"] = "Gatling laser",
        ["Laser Repeater"] = "Répéteur laser",
        ["Laser Scattergun"] = "Canon à dispersion laser",
        ["Plasma Scattergun"] = "Canon à dispersion plasma",
        ["Distortion Cannon"] = "Canon à distorsion",
        ["Distortion Repeater"] = "Répéteur à distorsion",
        ["Neutron Cannon"] = "Canon à neutrons",
        ["Neutron Repeater"] = "Répéteur à neutrons",
        ["Tachyon Cannon"] = "Canon à tachyons",
        ["Mass Driver Cannon"] = "Canon à accélération de masse",
        ["Cooler"] = "Refroidisseur",
        ["Power Plant"] = "Générateur",
        ["Shield Generator"] = "Générateur de bouclier",
        ["Quantum Drive"] = "Moteur quantique",
        ["Radar"] = "Radar",
        ["Mining Laser"] = "Laser de minage",
        ["Scraper Module"] = "Module de raclage",
        ["Tractor Beam"] = "Rayon tracteur",
    };

    private static readonly Dictionary<string, string> Classes = new()
    {
        ["Military"] = "Militaire",
        ["Civilian"] = "Civil",
        ["Industrial"] = "Industriel",
        ["Stealth"] = "Furtif",
        ["Competition"] = "Compétition",
        ["Ballistic"] = "Balistique",
        ["Electron"] = "Électron",
        ["Laser"] = "Laser",
        ["Energy (Laser)"] = "Énergie (laser)",
        ["Energy (Plasma)"] = "Énergie (plasma)",
        ["Energy (Electron)"] = "Énergie (électron)",
    };

    private static readonly Dictionary<string, string> StatLabels = new()
    {
        ["Size"] = "Taille",
        ["Grade"] = "Grade",
        ["Class"] = "Classe",
        ["Effective Range"] = "Portée efficace",
        ["Rate Of Fire"] = "Cadence",
        ["Magazine Size"] = "Chargeur",
        ["Battery Size"] = "Batterie",
        ["Attachments"] = "Accessoires",
        ["Capacity"] = "Capacité",
        ["Carrying Capacity"] = "Capacité",
        ["Core Compatibility"] = "Compatibilité",
        ["Temp. Rating"] = "Température",
        ["Radiation Protection"] = "Protection radiations",
        ["Radiation Scrub Rate"] = "Élimination radiations",
        ["Optimal Range"] = "Portée optimale",
        ["Maximum Range"] = "Portée maximale",
        ["Mining Laser Power"] = "Puissance du laser",
        ["Extraction Laser Power"] = "Puissance d'extraction",
        ["Extraction Throughput"] = "Débit d'extraction",
        ["Module Slots"] = "Emplacements de modules",
        ["Resistance"] = "Résistance",
        ["Instability"] = "Instabilité",
        ["Laser Instability"] = "Instabilité du laser",
        ["Optimal Charge Window Size"] = "Fenêtre de charge optimale",
        ["Optimal Charge Window Rate"] = "Vitesse de la fenêtre de charge",
        ["Power Transfer"] = "Transfert de puissance",
        ["Collection Throughput"] = "Débit de collecte",
        ["Collection Point Radius"] = "Rayon de collecte",
    };

    private static readonly Dictionary<string, string> CoreCompatibilities = new()
    {
        ["All"] = "toutes les armures",
        ["Medium & Heavy"] = "armures moyennes et lourdes",
        ["Heavy"] = "armures lourdes",
    };

    private static readonly Dictionary<string, string> Attachments = new()
    {
        ["Optics"] = "Optique",
        ["Barrel"] = "Canon",
        ["Underbarrel"] = "Sous le canon",
    };

    private static readonly Regex DecimalPoint = new(@"(\d)\.(\d)");
    private static readonly Regex AttachmentName = new(@"[A-Za-z]+");

    public static string ItemType(string itemType) => ItemTypes.GetValueOrDefault(itemType, itemType);

    public static string Class(string componentClass) => Classes.GetValueOrDefault(componentClass, componentClass);

    public static string Stat(SchemaStat stat)
    {
        var value = stat.Label switch
        {
            "Class" => Class(stat.Value),
            "Core Compatibility" => CoreCompatibilities.GetValueOrDefault(stat.Value, stat.Value),
            "Attachments" => AttachmentName.Replace(stat.Value, m => Attachments.GetValueOrDefault(m.Value, m.Value)),
            "Rate Of Fire" => stat.Value.Replace("rpm", "coups/min"),
            _ => stat.Value,
        };
        return $"{StatLabels.GetValueOrDefault(stat.Label, stat.Label)} : {DecimalPoint.Replace(value, "$1,$2")}";
    }
}
