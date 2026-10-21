namespace Cataclysm3D.Sim.Creatures;

/// <summary>
/// Simplified CDDA melee (<c>melee.cpp</c>, <c>monattack</c>). A hit roll is a normal roll around
/// accuracy × 5 with a spread of 25 (CDDA's <c>melee_hit_range</c>), and lands when it beats the
/// defender's dodge roll (dodge × 5). Damage is reduced by armor per type. No crits, grabs,
/// bites, techniques or weapons yet; see docs/divergences.md.
/// </summary>
public static class Melee
{
    /// <summary>Spread of CDDA's hit roll.</summary>
    public const float HitSpread = 25f;

    /// <summary>Moves an unarmed attack costs; CDDA's is about this for a bare-handed survivor.</summary>
    public const int UnarmedAttackCost = 100;

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

    /// <summary>The player's melee accuracy: Dex / 4 plus melee skill (none yet).</summary>
    public static float PlayerAccuracy(PlayerActor p) => p.Dex / 4f;

    /// <summary>The player's dodge: Dex / 4 plus dodge skill (none yet).</summary>
    public static float PlayerDodge(PlayerActor p) => p.Dex / 4f;

    /// <summary>
    /// A bare-handed blow: 2 + Str / 2 bash, scaled by a 50–100 % roll (CDDA's unarmed strength
    /// bonus and damage roll, flattened). An 8-Str survivor hits for 3–6.
    /// </summary>
    public static int PlayerUnarmedBash(PlayerActor p, Random rng) =>
        (int)MathF.Round((2 + p.Str / 2f) * (0.5f + 0.5f * (float)rng.NextDouble()));

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
