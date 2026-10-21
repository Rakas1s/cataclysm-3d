namespace Cataclysm3D.Sim.Items;

/// <summary>
/// One item in the world: a type, how many charges it has (servings of food, sips of water) and,
/// for containers, what's inside. Items are reference objects: the same instance moves from the
/// ground to your hands to your back.
/// </summary>
public sealed class Item
{
    private static int _nextSerial;

    public ItemType Type { get; }

    /// <summary>Charges left; 1 for items that aren't counted by charges.</summary>
    public int Charges { get; set; }

    /// <summary>What a container holds (a bottle's water, a bag's chips).</summary>
    public List<Item> Contents { get; } = new();

    /// <summary>Unique per run, so the UI can tell two identical shirts apart.</summary>
    public int Serial { get; } = Interlocked.Increment(ref _nextSerial);

    public Item(ItemType type, int charges = -1)
    {
        Type = type;
        Charges = charges >= 0 ? charges : type.ChargesPerUnit;
    }

    public string Id => Type.Id;

    /// <summary>The item's own weight plus everything in it, in milligrams.</summary>
    public long WeightMg => Type.WeightMg * Math.Max(1, Type.CountByCharges || Type.IsLiquid ? Charges : 1) + Contents.Sum(c => c.WeightMg);

    /// <summary>
    /// The space it takes up, in milliliters. A rigid container (a bottle) takes the same space
    /// full or empty; a soft one (a bag) grows with what's in it.
    /// </summary>
    public long VolumeMl
    {
        get
        {
            long own = Type.CountByCharges || Type.IsLiquid
                ? Type.VolumeMl * Charges
                : Type.VolumeMl;
            bool rigid = Type.Pockets.Count > 0 && Type.Pockets.All(p => p.Rigid);
            return own + (rigid ? 0 : Contents.Sum(c => c.VolumeMl));
        }
    }

    /// <summary>What's to eat or drink here: this item, or the first edible thing inside it.</summary>
    public Item? Edible => Type.IsEdible && Charges > 0 ? this : Contents.Select(c => c.Edible).FirstOrDefault(e => e is not null);

    /// <summary>The volume of what's inside, in milliliters.</summary>
    public long ContentsVolumeMl => Contents.Sum(c => c.VolumeMl);

    /// <summary>
    /// "plastic bottle of clean water (2)", "potato chips", "socks". Empty containers are just their name.
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (Contents.Count == 1)
            {
                var c = Contents[0];
                return $"{Type.Name} of {c.Type.Name}{(c.Charges > 1 ? $" ({c.Charges})" : "")}";
            }
            if (Contents.Count > 1)
                return $"{Type.Name} ({Contents.Count} items)";
            return Type.CountByCharges && Charges > 1 ? $"{Type.Name} ({Charges})" : Type.Name;
        }
    }

    /// <summary>Two items stack in lists when they're the same thing in the same state.</summary>
    public string StackKey => $"{Type.Id}|{Charges}|{string.Join(",", Contents.Select(c => c.StackKey))}";

    public override string ToString() => DisplayName;
}
