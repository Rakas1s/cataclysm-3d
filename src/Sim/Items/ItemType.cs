using Cataclysm3D.Sim.Creatures;

namespace Cataclysm3D.Sim.Items;

/// <summary>
/// Which layer a worn item sits on, innermost first, as CDDA 0.I orders them: PERSONAL (auras
/// you wear inside), SKINTIGHT (underwear), NORMAL, WAIST (belts), OUTER (coats), BELTED
/// (backpacks, holsters) and AURA.
/// </summary>
public enum WornLayer { Personal, Skintight, Normal, Waist, Outer, Belted, Aura }

/// <summary>
/// The body part slots armor names in <c>covers</c>. The six main parts take hits
/// (<see cref="Body"/>); hands, feet, eyes and mouth are minor parts that belong to one of them.
/// </summary>
public static class BodySlots
{
    /// <summary>All CDDA 0.I body parts armor can cover, head to toe.</summary>
    public static readonly string[] All =
        { "head", "eyes", "mouth", "torso", "arm_l", "arm_r", "hand_l", "hand_r", "leg_l", "leg_r", "foot_l", "foot_r" };

    /// <summary>The minor parts of a main part and their <c>hit_size</c> next to the main part's own (CDDA 0.I).</summary>
    private static readonly Dictionary<string, (string Part, float HitSize)[]> SubParts = new()
    {
        ["head"] = new[] { ("head", 4f), ("eyes", 0.5f), ("mouth", 0.5f) },
        ["arm_l"] = new[] { ("arm_l", 13f), ("hand_l", 1.5f) },
        ["arm_r"] = new[] { ("arm_r", 13f), ("hand_r", 1.5f) },
        ["leg_l"] = new[] { ("leg_l", 13f), ("foot_l", 2f) },
        ["leg_r"] = new[] { ("leg_r", 13f), ("foot_r", 2f) },
    };

    /// <summary>Where on a main part a blow lands: the part itself or one of its minor parts, by hit size.</summary>
    public static string RandomSlot(string mainPart, Random rng)
    {
        if (!SubParts.TryGetValue(mainPart, out var parts))
            return mainPart;
        float roll = (float)rng.NextDouble() * parts.Sum(p => p.HitSize);
        foreach (var (part, size) in parts)
            if ((roll -= size) < 0)
                return part;
        return mainPart;
    }

    /// <summary>The main part a body part belongs to (its <c>main_part</c> in <c>body_parts.json</c>).</summary>
    public static string MainPart(string part) => part switch
    {
        "eyes" or "mouth" => "head",
        "hand_l" => "arm_l",
        "hand_r" => "arm_r",
        "foot_l" => "leg_l",
        "foot_r" => "leg_r",
        _ => part,
    };
}

/// <summary>One entry of an item's <c>armor</c> list: the parts it covers and how well.</summary>
/// <param name="Coverage">Percent chance a blow to one of these parts goes through this item.</param>
/// <param name="Encumbrance">Encumbrance with empty pockets; <paramref name="MaxEncumbrance"/> when full.</param>
/// <param name="Rigid">
/// Made of something hard (any material that isn't <c>soft</c>, like a sneaker's rubber sole), so
/// it won't go with another rigid item on the same part. Only the skintight, normal and outer
/// layers can be rigid, as in CDDA.
/// </param>
public sealed record ArmorPortion(IReadOnlyList<string> Covers, int Coverage, int Encumbrance, int MaxEncumbrance, bool Rigid = false);

/// <summary>What an item does when worn: <c>armor</c>, <c>warmth</c> and its layer.</summary>
/// <param name="Protection">Damage it soaks per hit (bash, cut, stab), from its materials' <c>resist</c> × thickness.</param>
public sealed record ArmorData(IReadOnlyList<ArmorPortion> Portions, WornLayer Layer, int Warmth, ArmorValues Protection)
{
    public bool Covers(string part) => Portions.Any(p => p.Covers.Contains(part));

    public ArmorPortion? PortionFor(string part) => Portions.FirstOrDefault(p => p.Covers.Contains(part));

    /// <summary>Every body part it covers, in <see cref="BodySlots.All"/> order.</summary>
    public IEnumerable<string> CoveredParts => BodySlots.All.Where(Covers);
}

/// <summary>Protection against each damage type, in damage points per hit (CDDA's are fractional).</summary>
public readonly record struct ArmorValues(float Bash, float Cut, float Stab)
{
    public float Against(DamageType type) => type switch
    {
        DamageType.Bash => Bash,
        DamageType.Cut => Cut,
        _ => Stab,
    };

    public static ArmorValues operator +(ArmorValues a, ArmorValues b) => new(a.Bash + b.Bash, a.Cut + b.Cut, a.Stab + b.Stab);
}

public enum ComestibleKind { Food, Drink, Med }

/// <summary>An item you can eat or drink: CDDA's <c>COMESTIBLE</c> subtype, per charge.</summary>
public sealed record ComestibleData(ComestibleKind Kind, int Calories, int Quench, int Fun);

/// <summary>A pocket of a container or a piece of clothing (<c>pocket_data</c>, CONTAINER pockets only).</summary>
/// <param name="Holster">Holds a single item, whatever its volume (CDDA's <c>holster</c>).</param>
/// <param name="Moves">Moves to take something out of it (CDDA's pocket <c>moves</c>, default 100).</param>
public sealed record Pocket(long MaxVolumeMl, long MaxWeightMg, long MaxLengthMm, bool Watertight, bool Rigid, bool Holster, int Moves = 100);

/// <summary>
/// A CDDA item definition (type <c>ITEM</c> and its subtypes, with <c>copy-from</c> resolved):
/// the fields M5 uses. Weights are per item, or per charge for items counted by charges.
/// </summary>
public sealed class ItemType
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? PluralName { get; init; }
    public string Description { get; init; } = "";
    public string? LooksLike { get; init; }
    public string Symbol { get; init; } = "?";
    public string Color { get; init; } = "white";
    public string Category { get; init; } = "";
    public IReadOnlySet<string> Flags { get; init; } = new HashSet<string>();
    public IReadOnlyList<string> Materials { get; init; } = Array.Empty<string>();

    /// <summary>Milligrams, per charge when <see cref="ChargesPerUnit"/> &gt; 1.</summary>
    public long WeightMg { get; init; }

    /// <summary>Milliliters of one item (or one charge).</summary>
    public long VolumeMl { get; init; }

    public long LongestSideMm { get; init; }

    /// <summary>
    /// The charges one spawned item has (CDDA's <c>charges</c>); items with more than one stack
    /// their charges, like a box of 10 cigarettes or two servings of water.
    /// </summary>
    public int ChargesPerUnit { get; init; } = 1;

    /// <summary>Melee damage when wielded or thrown (<c>melee_damage</c>).</summary>
    public int Bash { get; init; }
    public int Cut { get; init; }
    public int Stab { get; init; }

    /// <summary>Accuracy bonus when wielded (<c>to_hit</c>, numeric or from grip/length/surface/balance).</summary>
    public int ToHit { get; init; }

    /// <summary>How many of it you can wear at once (<c>max_worn</c>, CDDA's default 2).</summary>
    public int MaxWorn { get; init; } = 2;

    public ArmorData? Armor { get; init; }
    public ComestibleData? Comestible { get; init; }
    public IReadOnlyList<Pocket> Pockets { get; init; } = Array.Empty<Pocket>();

    /// <summary>The container CDDA spawns it in (<c>container</c>), e.g. water in a plastic bottle.</summary>
    public string? DefaultContainer { get; init; }

    /// <summary>A liquid (<c>"phase": "liquid"</c>): it can only be carried in a watertight container.</summary>
    public bool IsLiquid { get; init; }

    public bool Has(string flag) => Flags.Contains(flag);
    public bool IsArmor => Armor is not null;
    public bool IsEdible => Comestible is not null;
    public bool IsContainer => Pockets.Count > 0;

    /// <summary>CDDA counts comestibles and ammo by charges; we only stack comestibles.</summary>
    public bool CountByCharges => Comestible is not null && ChargesPerUnit > 1;

    public string NameFor(int count) => count == 1 ? Name : PluralName ?? Name + "s";

    public override string ToString() => Id;
}
