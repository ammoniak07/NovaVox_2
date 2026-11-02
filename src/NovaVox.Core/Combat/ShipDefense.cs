namespace NovaVox.Core.Combat;

/// <summary>Défense d'un vaisseau avec ses générateurs de bouclier choisis.</summary>
public sealed record DefenseProfile(
    CombatShip Ship,
    /// <summary>Générateurs montés (tous), et ceux qui fonctionnent en même temps (les plus solides).</summary>
    IReadOnlyList<CombatShield> Mounted,
    IReadOnlyList<CombatShield> Active,
    double ShieldHp,
    double ShieldRegen);

/// <summary>Une arme de l'attaquant et ce qu'elle fait au défenseur.</summary>
public sealed record WeaponThreat(CombatWeapon Weapon, int Count, bool PenetratesArmor, bool PenetratesThroughShield);

/// <summary>Ce qu'un vaisseau attaquant (toutes ses armes) fait au défenseur. Estimations : tous les tirs touchent.</summary>
public sealed record AttackOnMe(
    string Attacker,
    CombatShip? AttackerShip,
    IReadOnlyList<WeaponThreat> Weapons,
    /// <summary>PV de bouclier retirés par seconde (canons).</summary>
    double ShieldDps,
    /// <summary>Dégâts par seconde au blindage une fois le bouclier tombé (canons qui dépassent la déflexion).</summary>
    double HullDps,
    /// <summary>Dégâts par seconde au blindage À TRAVERS le bouclier plein (part physique qui passe et dépasse la déflexion).</summary>
    double LeakHullDps,
    /// <summary>Dégâts de toute la salve de missiles/torpilles au blindage (bouclier tombé).</summary>
    double MissileHullDamage,
    double ShieldHp,
    double ArmorHp,
    /// <summary>PV de structure à détruire après le blindage.</summary>
    double HullHp = 0,
    /// <summary>Dégâts de distorsion par seconde (bouclier tombé) : surchargent les composants, pas la coque.</summary>
    double DistortionDps = 0)
{
    /// <summary>Ses canons percent le blindage (dégâts de coque continus).</summary>
    public bool GunsPierce => HullDps > 0;

    public bool CanHurtHull => HullDps > 0 || MissileHullDamage > 0;

    /// <summary>
    /// Secondes pour détruire le vaisseau avec les canons : pendant que le
    /// bouclier tient, la part qui le traverse (balistique) abîme déjà la
    /// coque ; une fois tombé, tout ce qui perce le blindage frappe la coque
    /// (blindage puis structure).
    /// </summary>
    public double SecondsToDestroy
    {
        get
        {
            var hull = ArmorHp + HullHp;
            var shieldTime = SecondsToDropShield;
            if (LeakHullDps > 0 && (double.IsInfinity(shieldTime) || LeakHullDps * shieldTime >= hull))
                return hull / LeakHullDps;
            if (HullDps <= 0) return double.PositiveInfinity;
            var leaked = LeakHullDps > 0 ? LeakHullDps * shieldTime : 0;
            return shieldTime + Math.Max(0, hull - leaked) / HullDps;
        }
    }

    /// <summary>Part du blindage + structure qu'enlève toute la salve de missiles (bouclier tombé).</summary>
    public double MissileShareOfHull => ArmorHp + HullHp > 0 ? MissileHullDamage / (ArmorHp + HullHp) : 0;

    /// <summary>Secondes pour faire tomber le bouclier (sans régénération : il ne remonte pas sous le feu).</summary>
    public double SecondsToDropShield => ShieldDps > 0 ? ShieldHp / ShieldDps : double.PositiveInfinity;

    /// <summary>Secondes pour vider le blindage une fois le bouclier tombé (canons seulement).</summary>
    public double SecondsToStripArmor => HullDps > 0 ? ArmorHp / HullDps : double.PositiveInfinity;

    public double SecondsTotal => SecondsToDropShield + SecondsToStripArmor;
}

/// <summary>
/// Calcul « Défense » : quels vaisseaux peuvent me faire des dégâts, et
/// combien. Mêmes règles que <see cref="WeaponPenetration"/> (déflexion par
/// projectile, absorption du bouclier), plus : le bouclier perd
/// absorption × (1 - résistance) des dégâts, et le blindage applique son
/// multiplicateur de dégâts par type. Seuls les N générateurs les plus
/// solides fonctionnent en même temps (N = 2 dans le jeu actuel).
/// </summary>
public static class DefenseCalculator
{
    private static readonly DamageKind[] Kinds = { DamageKind.Physical, DamageKind.Energy, DamageKind.Distortion };

    /// <summary>Défense du vaisseau : générateurs choisis dans <paramref name="loadout"/> (sinon ceux d'origine).</summary>
    public static DefenseProfile Profile(CombatShip ship, MyShipLoadout? loadout = null)
    {
        var mounts = ship.MountList.Where(m => m.IsShield).ToList();
        if (mounts.Count == 0)
            return new DefenseProfile(ship, Array.Empty<CombatShield>(), Array.Empty<CombatShield>(), ship.ShieldHp, ship.ShieldRegen);

        var chooser = loadout ?? new MyShipLoadout { Ship = ship.Name };
        var mounted = mounts
            .Select(m => chooser.WeaponOn(m) is { } name ? CombatDatabase.FindShield(name) : null)
            .OfType<CombatShield>()
            .ToList();
        var active = mounted.OrderByDescending(s => s.Hp).Take(Math.Max(1, ship.ActiveShields)).ToList();
        return new DefenseProfile(ship, mounted, active, active.Sum(s => s.Hp), active.Sum(s => s.Regen));
    }

    public static AttackOnMe Evaluate(DefenseProfile target, string attacker, CombatShip? attackerShip, IReadOnlyList<(CombatWeapon Weapon, int Count)> weapons)
    {
        var ship = target.Ship;
        double shieldDps = 0, hullDps = 0, leakDps = 0, missileHull = 0, distortionDps = 0;
        var threats = new List<WeaponThreat>();
        foreach (var (weapon, rawCount) in weapons)
        {
            var count = Math.Max(1, rawCount);
            var alphaSum = Kinds.Sum(weapon.Alpha);
            var pierces = false;
            var leaks = false;
            foreach (var kind in Kinds)
            {
                var alpha = weapon.Alpha(kind);
                if (alpha <= 0) continue;
                var absorbed = 1 - ship.ShieldLeak(kind);
                var leak = ship.ShieldLeak(kind);
                var deflection = ship.DeflectionOf(kind);
                var armorMult = ship.ArmorMultiplierOf(kind);
                // La distorsion ne touche ni le blindage ni la coque (elle surcharge les composants).
                var hitsHull = kind != DamageKind.Distortion;
                if (hitsHull && alpha > deflection) pierces = true;
                if (hitsHull && alpha * leak > deflection && leak > 0) leaks = true;

                if (weapon.IsGun)
                {
                    var dps = weapon.Dps * (alpha / alphaSum) * count;
                    shieldDps += dps * absorbed * (1 - ship.ShieldResistanceOf(kind));
                    if (!hitsHull) distortionDps += dps;
                    else
                    {
                        if (alpha > deflection) hullDps += dps * armorMult;
                        if (leak > 0 && alpha * leak > deflection) leakDps += dps * leak * armorMult;
                    }
                }
                else if (hitsHull && alpha > deflection)
                {
                    missileHull += alpha * weapon.Pellets * count * armorMult;
                }
            }
            threats.Add(new WeaponThreat(weapon, rawCount, pierces, leaks));
        }
        return new AttackOnMe(attacker, attackerShip, threats, shieldDps, hullDps, leakDps, missileHull, target.ShieldHp, ship.ArmorHp, ship.HullHp, distortionDps);
    }

    /// <summary>
    /// Tous les vaisseaux du jeu (armes d'origine) contre le défenseur.
    /// <paramref name="pilotOnly"/> : seulement les armes du pilote (pas de
    /// tourelles d'équipage), plus réaliste pour un vaisseau seul à bord.
    /// </summary>
    public static IReadOnlyList<AttackOnMe> AllStockAttackers(DefenseProfile target, bool pilotOnly = false) =>
        CombatDatabase.Ships
            .Select(s => (Ship: s, Weapons: new MyShipLoadout { Ship = s.Name }.EquippedWeapons(s, pilotOnly)))
            .Where(a => a.Weapons.Count > 0)
            .Select(a => Evaluate(target, a.Ship.Name, a.Ship, a.Weapons))
            .ToList();
}
