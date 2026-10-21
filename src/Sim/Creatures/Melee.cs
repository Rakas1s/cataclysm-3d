using Cataclysm3D.Sim.Items;

namespace Cataclysm3D.Sim.Creatures;

/// <summary>
/// Simplified CDDA melee (<c>melee.cpp</c>, <c>monattack</c>), for a survivor with no skills.
/// A hit roll is a normal roll around accuracy × 5 with a spread of 25 (CDDA's
/// <c>melee_hit_range</c>), and lands when it beats the defender's dodge roll (dodge × 5). Your
/// accuracy is Dex / 4 plus your weapon's <c>to_hit</c>; its damage and attack speed follow
/// <c>roll_melee_damage_internal</c> and <c>attack_speed</c>. Damage is reduced by armor per type.
/// No crits, grabs, bites or techniques yet; see docs/divergences.md.
/// </summary>
public static class Melee
{
    /// <summary>Spread of CDDA's hit roll.</summary>
    public const float HitSpread = 25f;

    /// <summary>CDDA's <c>attack_time</c> of bare hands.</summary>
    public const int UnarmedAttackTime = 65;

    public static float NormalRoll(Random rng, float mean, float sd)
    {
        // Box-Muller.
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return mean + sd * (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
    }

    public static float HitRoll(Random rng, float accuracy) => NormalRoll(rng, accuracy * 5, HitSpread);

    /// <summary>Chance a hit roll at <paramref name="accuracy"/> beats a dodge of <paramref name="dodge"/>.</summary>
    public static double HitChance(float accuracy, float dodge)
    {
        double z = (accuracy * 5 - dodge * 5) / HitSpread;
        return 0.5 * (1 + Erf(z / Math.Sqrt(2)));
    }

    /// <summary>The player's melee accuracy: Dex / 4 plus the wielded item's <c>to_hit</c> (no skills yet).</summary>
    public static float PlayerAccuracy(PlayerActor p) => p.Dex / 4f + (p.Wielded?.Type.ToHit ?? 0);

    /// <summary>The player's dodge: Dex / 4 plus dodge skill (none yet).</summary>
    public static float PlayerDodge(PlayerActor p) => p.Dex / 4f;

    /// <summary>
    /// An item's <c>attack_time</c>: 65, plus one per 62.5 ml and one per 60 g. Bare hands are 65.
    /// </summary>
    public static int AttackTime(Item? weapon) => weapon is null
        ? UnarmedAttackTime
        : (int)(UnarmedAttackTime + weapon.VolumeMl / 62.5 + weapon.WeightMg / 1000 / 60);

    /// <summary>
    /// Moves an attack costs (CDDA's <c>attack_speed</c> with no melee skill and full stamina):
    /// half the attack time, twice over (the second half is the skill penalty), less Dex / 2,
    /// and never under 25. Bare-handed that is 60; a baseball bat, 106.
    /// </summary>
    public static int AttackCost(PlayerActor p)
    {
        int half = AttackTime(p.Wielded) / 2;
        return Math.Max(25, half + half - p.Dex / 2);
    }

    /// <summary>Extra moves a miss costs as you stumble after a heavy weapon (CDDA's <c>stumble_pen</c>, at most 60).</summary>
    public static int StumbleCost(PlayerActor p) => p.Wielded is not { } w
        ? 0
        : (int)Math.Min(60, w.VolumeMl / 125 + w.WeightMg / 1000 / (p.Str * 10 + 13));

    /// <summary>
    /// The damage of one of your blows, by type, before armor (CDDA's
    /// <c>roll_melee_damage_internal</c>, no skills): bash is the weapon's bash plus Str / 2 to
    /// Str, capped near 2 × Str, rolled down to 40–100 % (more with Str over 20), × 0.8; cut is
    /// the weapon's × 0.8 and stab its × 0.66. Bare-handed, an 8-Str survivor hits for 1–6.
    /// </summary>
    public static List<(DamageType Type, int Amount)> PlayerDamage(PlayerActor p, Random rng)
    {
        var w = p.Wielded?.Type;
        float str = p.Str;
        float bash = (w?.Bash ?? 0) + str / 2 + (float)rng.NextDouble() * (str - str / 2);
        float mul = 0.8f;
        float cap = 2 * str;
        if (w is not null && cap < bash)
            mul *= (1 + cap / bash) / 2;
        float low = Math.Min(1, str / 20);
        bash = low * bash + (float)rng.NextDouble() * (bash - low * bash);
        var damage = new List<(DamageType, int)> { (DamageType.Bash, (int)MathF.Round(bash * mul)) };
        if (w is { Cut: > 0 })
            damage.Add((DamageType.Cut, (int)MathF.Round(w.Cut * 0.8f)));
        if (w is { Stab: > 0 })
            damage.Add((DamageType.Stab, (int)MathF.Round(w.Stab * 0.66f)));
        return damage;
    }

    /// <summary>A monster's blow: its bash dice plus its extra damage, typed.</summary>
    public static List<(DamageType Type, int Amount)> MonsterDamage(MonsterType t, Random rng)
    {
        int bash = 0;
        for (int i = 0; i < t.MeleeDice; i++)
            bash += rng.Next(1, Math.Max(1, t.MeleeDiceSides) + 1);
        var damage = new List<(DamageType, int)> { (DamageType.Bash, bash) };
        damage.AddRange(t.MeleeDamage);
        return damage;
    }

    /// <summary>Total damage after armor: each type is reduced by its armor, down to 0.</summary>
    public static int AfterArmor(IEnumerable<(DamageType Type, int Amount)> damage, Armor armor) =>
        damage.Sum(d => Math.Max(0, d.Amount - armor.Against(d.Type)));

    /// <summary>Damage after clothing's fractional protection, rounded once at the end.</summary>
    public static int AfterClothing(IEnumerable<(DamageType Type, int Amount)> damage, ArmorValues armor) =>
        (int)MathF.Round(damage.Sum(d => MathF.Max(0, d.Amount - armor.Against(d.Type))));

    // Abramowitz-Stegun 7.1.26; plenty for showing odds.
    private static double Erf(double x)
    {
        double sign = Math.Sign(x);
        x = Math.Abs(x);
        double t = 1 / (1 + 0.3275911 * x);
        double y = 1 - ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return sign * y;
    }
}
