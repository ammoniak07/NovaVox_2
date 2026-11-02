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
    double ArmorHp)
{
    public bool CanHurtHull => HullDps > 0 || MissileHullDamage > 0;

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
        double shieldDps = 0, hullDps = 0, leakDps = 0, missileHull = 0;
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
                if (alpha > deflection) pierces = true;
                if (alpha * leak > deflection && leak > 0) leaks = true;

                if (weapon.IsGun)
                {
                    var dps = weapon.Dps * (alpha / alphaSum) * count;
                    shieldDps += dps * absorbed * (1 - ship.ShieldResistanceOf(kind));
                    if (alpha > deflection) hullDps += dps * armorMult;
                    if (leak > 0 && alpha * leak > deflection) leakDps += dps * leak * armorMult;
                }
                else if (alpha > deflection)
                {
                    missileHull += alpha * weapon.Pellets * count * armorMult;
                }
            }
            threats.Add(new WeaponThreat(weapon, rawCount, pierces, leaks));
        }
        return new AttackOnMe(attacker, attackerShip, threats, shieldDps, hullDps, leakDps, missileHull, target.ShieldHp, ship.ArmorHp);
    }

    /// <summary>Tous les vaisseaux du jeu (armes d'origine) contre le défenseur.</summary>
    public static IReadOnlyList<AttackOnMe> AllStockAttackers(DefenseProfile target) =>
        CombatDatabase.Ships
            .Select(s => (Ship: s, Weapons: new MyShipLoadout { Ship = s.Name }.EquippedWeapons(s)))
            .Where(a => a.Weapons.Count > 0)
            .Select(a => Evaluate(target, a.Ship.Name, a.Ship, a.Weapons))
            .ToList();
}
