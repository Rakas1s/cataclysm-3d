using Cataclysm3D.Sim.Creatures;

namespace Cataclysm3D.Sim.Items;

/// <summary>
/// What you have on you: the clothes you wear (innermost layer first), the item in your hands,
/// and what you carry in the pockets of your clothes and bags. Each worn container holds its own
/// contents, so taking off a backpack takes its load with it. The rules say why you can't
/// do something as a sentence, so the UI can show it.
/// </summary>
public sealed class Inventory
{
    private readonly List<Item> _worn = new();

    public IReadOnlyList<Item> Worn => _worn;
    public Item? Wielded { get; private set; }

    /// <summary>Bumped on every change, so the UI knows when to redraw.</summary>
    public int Version { get; private set; }

    /// <summary>Everything in your pockets, with the worn item it's in.</summary>
    public IEnumerable<(Item Container, Item Item)> Carried =>
        _worn.SelectMany(w => w.Contents.Select(c => (w, c)));

    public IEnumerable<Item> CarriedItems => Carried.Select(c => c.Item);

    /// <summary>Whether you have the item on you anywhere: worn, wielded or in a pocket.</summary>
    public bool Has(Item item) => item == Wielded || _worn.Contains(item) || ContainerOf(item) is not null;

    /// <summary>The worn item whose pocket holds <paramref name="item"/>, if any.</summary>
    public Item? ContainerOf(Item item) => _worn.FirstOrDefault(w => w.Contents.Contains(item));

    /// <summary>Total weight of everything on you, in milligrams.</summary>
    public long WeightMg => _worn.Sum(w => w.WeightMg) + (Wielded?.WeightMg ?? 0);

    /// <summary>Total pocket space of what you wear, and how much of it is used, in milliliters.</summary>
    public long VolumeCapacityMl => _worn.Sum(PocketVolume);
    public long VolumeCarriedMl => _worn.Sum(w => w.ContentsVolumeMl);

    public static long PocketVolume(Item container) => container.Type.Pockets.Sum(p => p.MaxVolumeMl);

    /// <summary>CDDA's <c>weight_capacity</c>: 13 kg plus 4 kg per point of Str, in milligrams.</summary>
    public static long WeightCapacityMg(int str) => 13_000_000 + 4_000_000L * Math.Max(0, str);

    // ---- wearing --------------------------------------------------------------------------

    /// <summary>
    /// Why you can't put this on, or null if you can (CDDA's <c>Character::can_wear</c>, without
    /// the mutation, power armor and medical rules): it has to be clothing, you can't wear more
    /// than its <c>max_worn</c> of one kind, nor two rigid things on one body part, which is
    /// what keeps you from wearing two pairs of shoes.
    /// </summary>
    public string? WhyCantWear(Item item)
    {
        if (item.Type.Armor is not { } armor)
            return $"Putting on a {item.Type.Name} would be tricky.";
        if (_worn.Contains(item))
            return $"You're already wearing the {item.Type.Name}.";
        int max = item.Type.MaxWorn;
        if (_worn.Count(w => w.Type == item.Type) >= max)
            return max == 1 ? $"Can't wear more than one {item.Type.Name}!" : $"Can't wear {max + 1} or more {item.Type.NameFor(2)} at once.";
        if (item.Type.Has("ONE_PER_LAYER")
            && _worn.FirstOrDefault(w => w.Type.Has("ONE_PER_LAYER") && w.Type.Armor!.Layer == armor.Layer
                                         && armor.CoveredParts.Any(w.Type.Armor.Covers)) is { } clash)
            return $"The {item.Type.Name} won't fit over or under the {clash.Type.Name}.";
        foreach (var portion in armor.Portions.Where(p => p.Rigid))
            foreach (var part in portion.Covers)
                if (_worn.FirstOrDefault(w => w.Type.Armor!.PortionFor(part) is { Rigid: true }) is { } hard)
                    return $"Can't wear more than one rigid item on your {PartName(part)}! (The {hard.Type.Name} is in the way.)";
        return null;
    }

    /// <summary>"left foot", "eyes": a body slot as CDDA names it.</summary>
    public static string PartName(string part) => part switch
    {
        "arm_l" => "left arm", "arm_r" => "right arm",
        "hand_l" => "left hand", "hand_r" => "right hand",
        "leg_l" => "left leg", "leg_r" => "right leg",
        "foot_l" => "left foot", "foot_r" => "right foot",
        _ => part,
    };

    /// <summary>Puts the item on, in its layer after anything already on that layer (as CDDA sorts worn items).</summary>
    public void Wear(Item item)
    {
        if (WhyCantWear(item) is { } reason)
            throw new InvalidOperationException(reason);
        Detach(item);
        var layer = item.Type.Armor!.Layer;
        int at = _worn.FindLastIndex(w => w.Type.Armor!.Layer <= layer) + 1;
        _worn.Insert(at, item);
        Version++;
    }

    /// <summary>
    /// Takes a worn item off. What was in its pockets stays in it. Returns false if you aren't
    /// wearing it.
    /// </summary>
    public bool TakeOff(Item item)
    {
        if (!_worn.Remove(item))
            return false;
        Version++;
        return true;
    }

    // ---- wielding -------------------------------------------------------------------------

    public string? WhyCantWield(Item item)
    {
        if (item == Wielded)
            return $"You're already wielding the {item.Type.Name}.";
        if (item.Type.Has("NO_UNWIELD"))
            return $"You can't wield the {item.Type.Name}.";
        return null;
    }

    /// <summary>Takes the item in your hands. Whatever you held must be put away first.</summary>
    public void Wield(Item item)
    {
        if (Wielded is not null)
            throw new InvalidOperationException("Your hands are full.");
        Detach(item);
        Wielded = item;
        Version++;
    }

    /// <summary>Empties your hands, returning what you held.</summary>
    public Item? Unwield()
    {
        var held = Wielded;
        Wielded = null;
        if (held is not null)
            Version++;
        return held;
    }

    // ---- pockets ---------------------------------------------------------------------------

    /// <summary>
    /// The worn container with room for <paramref name="item"/>: free space for its volume, a
    /// pocket long enough for it and its weight, and a watertight one for liquids. Prefers the
    /// fullest one that fits, so loose things gather in one place, as CDDA's best-pocket pick does.
    /// </summary>
    public Item? PocketFor(Item item, Item? except = null)
    {
        Item? best = null;
        long bestFree = long.MaxValue;
        foreach (var w in _worn)
        {
            if (w == item || w == except || !Fits(w, item))
                continue;
            long free = PocketVolume(w) - w.ContentsVolumeMl;
            if (free < bestFree)
            {
                best = w;
                bestFree = free;
            }
        }
        return best;
    }

    /// <summary>Whether a container's pockets take the item on top of what's in them.</summary>
    public static bool Fits(Item container, Item item)
    {
        var pockets = container.Type.Pockets;
        if (pockets.Count == 0 || item == container)
            return false;
        if (item.Type.IsLiquid && !pockets.Any(p => p.Watertight))
            return false;
        // Pockets are pooled, but the item must fit in the biggest one by itself.
        if (container.ContentsVolumeMl + item.VolumeMl > pockets.Sum(p => p.MaxVolumeMl) || item.VolumeMl > pockets.Max(p => p.MaxVolumeMl))
            return false;
        long maxLength = pockets.Max(p => p.MaxLengthMm);
        if (maxLength > 0 && LongestSideMm(item) > maxLength)
            return false;
        long maxWeight = pockets.Sum(p => p.MaxWeightMg);
        return maxWeight <= 0 || container.Contents.Sum(c => c.WeightMg) + item.WeightMg <= maxWeight;
    }

    /// <summary>
    /// An item's <c>longest_side</c>, or, when the JSON doesn't say, the edge of a cube of its
    /// volume, as CDDA assumes.
    /// </summary>
    public static long LongestSideMm(Item item) =>
        item.Type.LongestSideMm > 0 ? item.Type.LongestSideMm : (long)Math.Round(Math.Cbrt(item.Type.VolumeMl) * 10);

    /// <summary>Why the item won't go in your pockets, or null if it will.</summary>
    public string? WhyCantStow(Item item)
    {
        if (PocketFor(item) is not null)
            return null;
        if (_worn.All(w => w.Type.Pockets.Count == 0))
            return "You have no pockets to put it in.";
        if (item.Type.IsLiquid)
            return $"The {item.Type.Name} would spill; it needs a container.";
        long free = _worn.Max(w => w.Type.Pockets.Count == 0 ? 0 : PocketVolume(w) - w.ContentsVolumeMl);
        return item.VolumeMl > free
            ? $"There's no room for the {item.DisplayName} ({Format.Volume(item.VolumeMl)}); your roomiest pocket has {Format.Volume(free)} free."
            : $"The {item.DisplayName} doesn't fit in any of your pockets.";
    }

    /// <summary>Puts the item in the best pocket. Returns the container it went in, or null if nothing has room.</summary>
    public Item? Stow(Item item)
    {
        if (PocketFor(item) is not { } pocket)
            return null;
        Detach(item);
        pocket.Contents.Add(item);
        Version++;
        return pocket;
    }

    /// <summary>Takes the item out of wherever it is on you. Returns false if you don't have it.</summary>
    public bool Remove(Item item)
    {
        bool had = Has(item);
        Detach(item);
        if (had)
            Version++;
        return had;
    }

    /// <summary>Something inside an item changed (you drank from a bottle).</summary>
    public void Touch() => Version++;

    private void Detach(Item item)
    {
        if (Wielded == item)
            Wielded = null;
        _worn.Remove(item);
        foreach (var w in _worn)
            w.Contents.Remove(item);
    }

    // ---- what your clothes do ---------------------------------------------------------------

    /// <summary>
    /// Encumbrance of a body part (one of <see cref="BodySlots.All"/>): what covers it (soft
    /// pockets add up to their <c>max_encumbrance</c> as they fill), plus CDDA's layering penalty:
    /// on a layer with several items, each one but the bulkiest adds its encumbrance again,
    /// clamped to 2–10 (<c>layer_details</c>). Two shirts of 3 and 5 make 11.
    /// </summary>
    public int Encumbrance(string part)
    {
        int total = 0;
        foreach (var layer in _worn.Where(w => w.Type.Armor!.Covers(part)).GroupBy(w => w.Type.Armor!.Layer))
        {
            int biggest = 0, penalty = 0;
            foreach (var w in layer)
            {
                int e = ItemEncumbrance(w, part);
                total += e;
                if (layer.Key == WornLayer.Personal || w.Type.Has("SEMITANGIBLE"))
                    continue;
                int value = Math.Clamp(e, 2, 10);
                penalty += Math.Min(value, biggest);
                biggest = Math.Max(biggest, value);
            }
            total += penalty;
        }
        return total;
    }

    /// <summary>One item's encumbrance on a part: CDDA's <c>get_encumber</c>, growing with how full its soft pockets are.</summary>
    public static int ItemEncumbrance(Item w, string part)
    {
        if (w.Type.Armor?.PortionFor(part) is not { } portion)
            return 0;
        long capacity = PocketVolume(w);
        bool soft = w.Type.Pockets.All(p => !p.Rigid);
        if (!soft || capacity <= 0 || w.ContentsVolumeMl <= 0)
            return portion.Encumbrance;
        double fill = Math.Min(1.0, (double)w.ContentsVolumeMl / capacity);
        return portion.Encumbrance + (int)Math.Ceiling(fill * (portion.MaxEncumbrance - portion.Encumbrance));
    }

    /// <summary>
    /// CDDA's <c>item_handling_cost</c>: moves to handle an item, a base cost plus its bulk
    /// (1 per 20 ml, at most 200) plus the encumbrance of your freer hand.
    /// </summary>
    public int HandlingCost(Item item, int baseCost, bool bulk = true) =>
        baseCost + (bulk ? (int)Math.Min(200, item.VolumeMl / 20) : 0) + Math.Min(Encumbrance("hand_l"), Encumbrance("hand_r"));

    /// <summary>
    /// CDDA's <c>item_wear_cost</c>: handling it, scaled by its layer (underwear is fiddly, belts
    /// and straps quick) and by its average encumbrance over 10.
    /// </summary>
    public int WearCost(Item item)
    {
        double moves = HandlingCost(item, 100);
        moves *= item.Type.Armor?.Layer switch
        {
            WornLayer.Skintight => 1.5,
            WornLayer.Waist or WornLayer.Outer => 1 / 1.5,
            WornLayer.Belted => 0.5,
            _ => 1,
        };
        var parts = item.Type.Armor?.CoveredParts.ToList() ?? new List<string>();
        double avg = parts.Count == 0 ? 0 : parts.Average(p => ItemEncumbrance(item, p));
        return (int)(moves * Math.Max(avg / 10, 1));
    }

    /// <summary>Moves to take off a worn item: CDDA's worn-item obtain cost.</summary>
    public int TakeOffCost(Item item) => HandlingCost(item, 50) + 250;

    /// <summary>Total <c>warmth</c> of what covers a body part.</summary>
    public int Warmth(string part) => _worn.Where(w => w.Type.Armor!.Covers(part)).Sum(w => w.Type.Armor!.Warmth);

    /// <summary>
    /// The armor that stops a blow to <paramref name="part"/>: each worn item covering it stands in
    /// the way with its <c>coverage</c> percent chance, outermost first, as CDDA rolls them.
    /// </summary>
    public (ArmorValues Protection, List<Item> Hit) ArmorAgainst(string part, Random rng)
    {
        var protection = default(ArmorValues);
        var hit = new List<Item>();
        for (int i = _worn.Count - 1; i >= 0; i--)
        {
            var w = _worn[i];
            if (w.Type.Armor!.PortionFor(part) is { } portion && rng.Next(100) < portion.Coverage)
            {
                protection += w.Type.Armor.Protection;
                hit.Add(w);
            }
        }
        return (protection, hit);
    }
}

/// <summary>CDDA-style unit strings for the UI and messages.</summary>
public static class Format
{
    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    public static string Volume(long ml) => ml >= 1000 ? string.Format(Invariant, "{0:0.##} L", ml / 1000.0) : $"{ml} ml";
    public static string Weight(long mg) => mg >= 1_000_000
        ? string.Format(Invariant, "{0:0.##} kg", mg / 1_000_000.0)
        : string.Format(Invariant, "{0:0.#} g", mg / 1000.0);
}
