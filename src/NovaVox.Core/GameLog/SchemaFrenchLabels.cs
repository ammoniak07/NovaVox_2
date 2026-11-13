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

    private static readonly Dictionary<string, string> Resources = new()
    {
        ["Aluminum"] = "Aluminium",
        ["Beryl"] = "Béryl",
        ["Copper"] = "Cuivre",
        ["Corundum"] = "Corindon",
        ["Gold"] = "Or",
        ["Iron"] = "Fer",
        ["Pressurized Ice"] = "Glace pressurisée",
        ["Saldynium (Ore)"] = "Saldynium (minerai)",
        ["Silicon"] = "Silicium",
        ["Tin"] = "Étain",
        ["Titanium"] = "Titane",
        ["Tungsten"] = "Tungstène",
        ["Yormandi Eye"] = "Œil de Yormandi",
    };

    private static readonly System.Globalization.CultureInfo FrenchCulture = new("fr-FR");

    /// <summary>Nom français d'un ingrédient (les minerais propres au jeu gardent leur nom).</summary>
    public static string Resource(string name) => Resources.GetValueOrDefault(name, name);

    /// <summary>
    /// Recette d'un schéma en une ligne : « Fer 0,26 SCU · Glace pressurisée
    /// 0,16 SCU · Hadanite ×7 ». Un même ingrédient peut servir à plusieurs
    /// étapes de la recette (Fer 0,16 pour la coque + 0,10 pour la pompe) :
    /// les quantités sont additionnées, dans l'ordre de première apparition.
    /// Null si la recette est vide.
    /// </summary>
    public static string? Recipe(IReadOnlyList<SchemaIngredient>? ingredients)
    {
        if (ingredients is not { Count: > 0 }) return null;
        var parts = ingredients
            .GroupBy(i => (i.Name, Unit: i.Unit.ToUpperInvariant()))
            .Select(g => (g.Key.Name, g.Key.Unit, Amount: g.Sum(i => i.Amount), MinQuality: g.Max(i => i.MinQuality)))
            .Select(i => (i.Unit == "SCU"
                ? $"{Resource(i.Name)} {Math.Round(i.Amount, 3).ToString("0.###", FrenchCulture)} SCU"
                : $"{Resource(i.Name)} ×{Math.Round(i.Amount, 3).ToString("0.###", FrenchCulture)}")
                + (i.MinQuality is > 1 ? $" (qualité ≥ {i.MinQuality})" : ""));
        return string.Join(" · ", parts);
    }

    private static readonly Dictionary<string, string> QualityStats = new()
    {
        ["Integrity"] = "Intégrité",
        ["Impact Force"] = "Force d'impact",
        ["Damage Mitigation"] = "Réduction des dégâts",
        ["Min Temp"] = "Temp. minimale supportée",
        ["Max Temp"] = "Temp. maximale supportée",
        ["Recoil Smoothness"] = "Fluidité du recul",
        ["Recoil Handling"] = "Maîtrise du recul",
        ["Recoil Kick"] = "Force du recul",
        ["Coolant Rating"] = "Refroidissement",
        ["Fire Rate"] = "Cadence de tir",
        ["Power Pips"] = "Points d'énergie",
        ["Max. Shield Strength"] = "Puissance max. du bouclier",
        ["Min. Assist Distance"] = "Distance d'assistance min.",
        ["Max. Assist Distance"] = "Distance d'assistance max.",
        ["Quantum Speed"] = "Vitesse quantique",
        ["Quantum Fuel Burn"] = "Consommation de carburant quantique",
        ["Laser Power"] = "Puissance du laser",
        ["Full Strength Dist."] = "Portée à pleine puissance",
        ["Max. Distance"] = "Portée max.",
        ["Beam Force"] = "Force du rayon",
        ["Max. Volume"] = "Volume max.",
        ["Radius"] = "Rayon",
        ["Speed"] = "Vitesse",
        ["Efficiency"] = "Efficacité",
        ["Radiation Dissipation"] = "Dissipation des radiations",
    };

    /// <summary>
    /// Effet de la qualité des ingrédients sur l'objet fabriqué, en une ligne :
    /// « Intégrité −10 % → +10 % (Agricium) · Points d'énergie −2 → +2
    /// (Laranite) » — de la qualité 0 à la qualité 1000 de l'ingrédient.
    /// Null si la qualité ne change rien.
    /// </summary>
    public static string? QualityEffects(IReadOnlyList<SchemaQualityEffect>? effects)
    {
        if (effects is not { Count: > 0 }) return null;
        return string.Join(" · ", effects.Select(e =>
        {
            var range = e.Additive
                ? $"{Signed(e.From)} → {Signed(e.To)}"
                : $"{Signed((e.From - 1) * 100)} % → {Signed((e.To - 1) * 100)} %";
            var ingredients = e.Ingredients.Count > 0 ? $" ({string.Join(", ", e.Ingredients.Select(Resource))})" : "";
            return $"{QualityStats.GetValueOrDefault(e.Stat, e.Stat)} {range}{ingredients}";
        }));
    }

    private static string Signed(double value)
    {
        var rounded = Math.Round(value, 1);
        if (rounded == 0) return "0";
        var text = Math.Abs(rounded).ToString("0.#", FrenchCulture);
        return rounded > 0 ? "+" + text : "−" + text;
    }

    /// <summary>Caractéristiques qui vont ensemble, affichées sous un seul nom quand elles ont le même effet.</summary>
    private static readonly (string[] Stats, string Label)[] QualityStatGroups =
    {
        (new[] { "Min Temp", "Max Temp" }, "Températures supportées"),
        (new[] { "Recoil Smoothness", "Recoil Handling", "Recoil Kick" }, "Recul (fluidité, maîtrise, force)"),
        (new[] { "Min. Assist Distance", "Max. Assist Distance" }, "Distance d'assistance (min. et max.)"),
    };

    /// <summary>
    /// Recette et effet de la qualité des ingrédients, une ligne par
    /// ingrédient (quantités additionnées), pour la fiche d'un schéma :
    /// <code>
    /// • Ouratite 0,03 SCU : Réduction des dégâts −10 % → +10 %
    /// • Savrilium 0,03 SCU : qualité sans effet
    /// </code>
    /// Sans aucun effet de qualité pour tout le schéma, les lignes n'ont que
    /// l'ingrédient (l'en-tête de la fiche le dit une fois pour toutes).
    /// Null si la recette est vide.
    /// </summary>
    public static IReadOnlyList<string>? RecipeLines(IReadOnlyList<SchemaIngredient>? ingredients, IReadOnlyList<SchemaQualityEffect>? effects)
    {
        if (ingredients is not { Count: > 0 }) return null;
        var known = effects is { Count: > 0 };
        return ingredients
            .GroupBy(i => (i.Name, Unit: i.Unit.ToUpperInvariant()))
            .Select(g =>
            {
                var amount = Math.Round(g.Sum(i => i.Amount), 3).ToString("0.###", FrenchCulture);
                var line = g.Key.Unit == "SCU" ? $"{Resource(g.Key.Name)} {amount} SCU" : $"{Resource(g.Key.Name)} ×{amount}";
                if (g.Max(i => i.MinQuality) is > 1 and var min) line += $" (qualité ≥ {min})";
                if (!known) return "• " + line;
                var mine = effects!.Where(e => e.Ingredients.Contains(g.Key.Name, StringComparer.OrdinalIgnoreCase)).ToList();
                return $"• {line} : {(mine.Count == 0 ? "qualité sans effet" : DescribeEffects(mine))}";
            })
            .ToList();
    }

    private static string DescribeEffects(List<SchemaQualityEffect> effects)
    {
        var parts = new List<string>();
        // Même plage de valeurs : regroupées (« Températures supportées −20 % → +20 % »).
        foreach (var byRange in effects.GroupBy(Range))
        {
            var stats = byRange.Select(e => e.Stat).Distinct().ToList();
            var labels = new List<string>();
            foreach (var (group, label) in QualityStatGroups)
            {
                if (!group.All(stats.Contains)) continue;
                labels.Add(label);
                stats.RemoveAll(group.Contains);
            }
            labels.InsertRange(0, stats.Select(st => QualityStats.GetValueOrDefault(st, st)));
            parts.Add($"{string.Join(", ", labels)} {byRange.Key}");
        }
        return string.Join(" ; ", parts);
    }

    private static string Range(SchemaQualityEffect e) => e.Additive
        ? $"{Signed(e.From)} → {Signed(e.To)}"
        : $"{Signed((e.From - 1) * 100)} % → {Signed((e.To - 1) * 100)} %";
}
