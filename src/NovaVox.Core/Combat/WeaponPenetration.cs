namespace NovaVox.Core.Combat;

/// <summary>Résultat pour un type de dégâts de l'arme contre un vaisseau.</summary>
public sealed record DamageCheck(DamageKind Kind, double Alpha, double Deflection, double ShieldLeak, double AlphaThroughShield)
{
    /// <summary>Bouclier tombé : le projectile dépasse le seuil de déflexion du blindage.</summary>
    public bool PenetratesArmor => Alpha > Deflection;

    /// <summary>Bouclier plein : ce qui traverse le bouclier dépasse encore le seuil de déflexion.</summary>
    public bool PenetratesThroughShield => AlphaThroughShield > 0 && AlphaThroughShield > Deflection;
}

/// <summary>Verdict d'une arme contre un vaisseau (voir <see cref="WeaponPenetration.Evaluate"/>).</summary>
public sealed record PenetrationResult(CombatWeapon Weapon, CombatShip Ship, IReadOnlyList<DamageCheck> Checks)
{
    /// <summary>Bouclier tombé : au moins un type de dégâts passe le blindage.</summary>
    public bool ArmorPenetrated => Checks.Any(c => c.PenetratesArmor);

    /// <summary>Bouclier plein : des dégâts traversent le bouclier ET passent le blindage.</summary>
    public bool ShieldAndArmorPenetrated => Checks.Any(c => c.PenetratesThroughShield);

    /// <summary>Le bouclier plein laisse passer une partie des dégâts de l'arme (même s'ils ricochent ensuite).</summary>
    public bool LeaksThroughShield => Checks.Any(c => c.AlphaThroughShield > 0);

    /// <summary>
    /// Marge par rapport au seuil de déflexion (bouclier tombé) : dégâts du
    /// meilleur type / seuil. Au-dessus de 1, ça passe ; plus c'est haut, plus
    /// c'est confortable. Infini si le seuil est nul.
    /// </summary>
    public double ArmorMargin => Checks.Count == 0 ? 0 : Checks.Max(c => c.Deflection <= 0 ? double.PositiveInfinity : c.Alpha / c.Deflection);
}

/// <summary>
/// Est-ce qu'une arme « passe » sur un vaisseau ? Règles du jeu (4.x) :
/// le bouclier absorbe les dégâts selon leur type (toute l'énergie et la
/// distorsion, une partie seulement du physique) ; ce qui passe frappe le
/// blindage, qui fait ricocher tout projectile dont les dégâts ne dépassent
/// pas son seuil de déflexion pour ce type. On compare donc les dégâts
/// d'UN projectile (une seule bille pour un fusil à pompe) au seuil.
/// </summary>
public static class WeaponPenetration
{
    private static readonly DamageKind[] Kinds = { DamageKind.Physical, DamageKind.Energy, DamageKind.Distortion };

    public static PenetrationResult Evaluate(CombatWeapon weapon, CombatShip ship)
    {
        var checks = new List<DamageCheck>();
        foreach (var kind in Kinds)
        {
            var alpha = weapon.Alpha(kind);
            if (alpha <= 0) continue;
            var leak = ship.ShieldLeak(kind);
            checks.Add(new DamageCheck(kind, alpha, ship.DeflectionOf(kind), leak, alpha * leak));
        }
        return new PenetrationResult(weapon, ship, checks);
    }

    public static string FrenchName(DamageKind kind) => kind switch
    {
        DamageKind.Physical => "Physique",
        DamageKind.Energy => "Énergie",
        DamageKind.Distortion => "Distorsion",
        _ => kind.ToString(),
    };
}
