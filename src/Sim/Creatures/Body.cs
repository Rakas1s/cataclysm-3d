namespace Cataclysm3D.Sim.Creatures;

/// <summary>
/// One of CDDA's main body parts (<c>body_parts.json</c>): its hit points and how likely a blow
/// lands on it. Minor parts (hands, feet, eyes, mouth) are folded into their main part, adding
/// their <c>hit_size</c> to it.
/// </summary>
/// <param name="Vital">Losing all its hit points kills you (head and torso).</param>
public sealed record BodyPartType(string Id, string Name, int BaseHp, float HitSize, bool Vital)
{
    /// <summary>CDDA 0.I's main parts, for when no JSON is loaded (tests, tools).</summary>
    public static readonly IReadOnlyList<BodyPartType> Defaults = new BodyPartType[]
    {
        new("head", "head", 60, 4 + 0.5f + 0.5f, true),
        new("torso", "torso", 60, 36, true),
        new("arm_l", "left arm", 60, 13 + 1.5f, false),
        new("arm_r", "right arm", 60, 13 + 1.5f, false),
        new("leg_l", "left leg", 60, 13 + 2, false),
        new("leg_r", "right leg", 60, 13 + 2, false),
    };
}

/// <summary>Hit points per body part. You die when a vital part (head, torso) reaches 0.</summary>
public sealed class Body
{
    private readonly int[] _hp;

    public IReadOnlyList<BodyPartType> Parts { get; }

    public Body(IReadOnlyList<BodyPartType> parts)
    {
        Parts = parts;
        _hp = parts.Select(p => p.BaseHp).ToArray();
    }

    public int Hp(int part) => _hp[part];
    public int Hp(string id) => _hp[IndexOf(id)];
    public int MaxHp(int part) => Parts[part].BaseHp;

    public int IndexOf(string id)
    {
        for (int i = 0; i < Parts.Count; i++)
            if (Parts[i].Id == id)
                return i;
        throw new KeyNotFoundException($"No body part \"{id}\"");
    }

    public bool IsDead => Parts.Select((p, i) => p.Vital && _hp[i] <= 0).Any(b => b);

    /// <summary>Takes damage off a part, never below 0. Returns what was actually lost.</summary>
    public int Damage(int part, int amount)
    {
        int lost = Math.Clamp(amount, 0, _hp[part]);
        _hp[part] -= lost;
        return lost;
    }

    /// <summary>CDDA picks the part a blow lands on weighted by <c>hit_size</c>.</summary>
    public int RandomPart(Random rng)
    {
        float total = Parts.Sum(p => p.HitSize);
        float roll = (float)rng.NextDouble() * total;
        for (int i = 0; i < Parts.Count; i++)
        {
            roll -= Parts[i].HitSize;
            if (roll < 0)
                return i;
        }
        return Parts.Count - 1;
    }
}
