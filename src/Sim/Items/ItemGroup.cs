namespace Cataclysm3D.Sim.Items;

/// <summary>An inclusive range rolled uniformly, like CDDA's <c>[min, max]</c> pairs.</summary>
public readonly record struct IntRange(int Min, int Max)
{
    public static readonly IntRange One = new(1, 1);
    public int Roll(Random rng) => Max <= Min ? Min : rng.Next(Min, Max + 1);
}

/// <summary>
/// CDDA's two kinds of <c>item_group</c>: a <c>collection</c> rolls every entry on its own,
/// <c>prob</c> percent each; a <c>distribution</c> picks exactly one entry, weighted by <c>prob</c>.
/// Groups without a subtype are the old style, which is a distribution.
/// </summary>
public enum ItemGroupKind { Collection, Distribution }

/// <summary>One line of an item group: an item, a group by id, or a group written inline.</summary>
/// <param name="Prob">Percent chance in a collection, weight in a distribution.</param>
/// <param name="Count">How many times the entry is spawned.</param>
/// <param name="Charges">Overrides the charges of the spawned item; a negative max means "up to full".</param>
/// <param name="ContainerItem">Spawns the item in this container instead of its default one; "null" for none.</param>
public sealed record ItemGroupEntry(int Prob, IntRange Count)
{
    public string? ItemId { get; init; }
    public string? GroupId { get; init; }
    public ItemGroup? Inline { get; init; }
    public IntRange? Charges { get; init; }
    public string? ContainerItem { get; init; }
}

public sealed record ItemGroup(string Id, ItemGroupKind Kind, IReadOnlyList<ItemGroupEntry> Entries)
{
    /// <summary>Puts everything this group spawns into one container of this type (<c>container-item</c>).</summary>
    public string? ContainerItem { get; init; }
}

/// <summary>
/// Item types and groups by id, built on demand by the importer (CDDA has thousands of items and
/// a house needs a few dozen), and the rules for spawning them: rolling groups and putting
/// food and drink in their containers.
/// </summary>
public sealed class ItemRegistry
{
    private const int MaxGroupDepth = 12;

    private readonly Func<string, ItemType?> _loadType;
    private readonly Func<string, ItemGroup?> _loadGroup;
    private readonly Dictionary<string, ItemType?> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ItemGroup?> _groups = new(StringComparer.Ordinal);

    /// <summary>Ids asked for that don't exist (the importer's coverage log).</summary>
    public SortedSet<string> Missing { get; } = new(StringComparer.Ordinal);

    public ItemRegistry(Func<string, ItemType?> loadType, Func<string, ItemGroup?> loadGroup)
    {
        _loadType = loadType;
        _loadGroup = loadGroup;
    }

    /// <summary>A registry over fixed lists, for tests.</summary>
    public ItemRegistry(IEnumerable<ItemType> types, IEnumerable<ItemGroup>? groups = null) : this(_ => null, _ => null)
    {
        foreach (var t in types)
            _types[t.Id] = t;
        foreach (var g in groups ?? Enumerable.Empty<ItemGroup>())
            _groups[g.Id] = g;
    }

    public ItemType? Type(string id)
    {
        if (!_types.TryGetValue(id, out var t))
        {
            _types[id] = t = _loadType(id);
            if (t is null)
                Missing.Add(id);
        }
        return t;
    }

    public ItemGroup? Group(string id)
    {
        if (!_groups.TryGetValue(id, out var g))
        {
            _groups[id] = g = _loadGroup(id);
            if (g is null)
                Missing.Add("group " + id);
        }
        return g;
    }

    /// <summary>A new item of a type, in its default container when it comes in one. Null for unknown ids.</summary>
    public Item? Create(string id, IntRange? charges = null, string? containerItem = null, Random? rng = null)
    {
        if (Type(id) is not { } type)
            return null;
        var item = new Item(type);
        if (charges is { } c)
        {
            int max = c.Max < 0 ? type.ChargesPerUnit : c.Max;
            item.Charges = Math.Max(1, new IntRange(Math.Max(1, c.Min), Math.Max(c.Min, max)).Roll(rng ?? Random.Shared));
        }
        string? containerId = containerItem ?? type.DefaultContainer;
        if (containerId is null or "null" || Type(containerId) is not { } containerType)
            return item;
        var container = new Item(containerType);
        // Liquids fill their container, as CDDA does when it spawns a drink in its bottle.
        if (type.IsLiquid && type.VolumeMl > 0 && containerType.Pockets.FirstOrDefault() is { } pocket && charges is null)
            item.Charges = (int)Math.Max(1, pocket.MaxVolumeMl / type.VolumeMl);
        container.Contents.Add(item);
        return container;
    }

    /// <summary>Rolls a group by id. Unknown groups spawn nothing.</summary>
    public List<Item> Roll(string groupId, Random rng) =>
        Group(groupId) is { } g ? Roll(g, rng) : new List<Item>();

    public List<Item> Roll(ItemGroup group, Random rng)
    {
        var items = new List<Item>();
        Roll(group, rng, items, 0);
        return items;
    }

    private void Roll(ItemGroup group, Random rng, List<Item> into, int depth)
    {
        if (depth > MaxGroupDepth)
            return;
        var spawned = new List<Item>();
        if (group.Kind == ItemGroupKind.Collection)
        {
            foreach (var e in group.Entries)
                if (rng.Next(100) < e.Prob)
                    Spawn(e, rng, spawned, depth);
        }
        else
        {
            int total = group.Entries.Sum(e => Math.Max(0, e.Prob));
            if (total > 0)
            {
                int roll = rng.Next(total);
                foreach (var e in group.Entries)
                {
                    if ((roll -= Math.Max(0, e.Prob)) >= 0)
                        continue;
                    Spawn(e, rng, spawned, depth);
                    break;
                }
            }
        }
        if (group.ContainerItem is { } cid && spawned.Count > 0 && Type(cid) is { } ct)
        {
            var container = new Item(ct);
            container.Contents.AddRange(spawned);
            into.Add(container);
        }
        else
        {
            into.AddRange(spawned);
        }
    }

    private void Spawn(ItemGroupEntry e, Random rng, List<Item> into, int depth)
    {
        int count = e.Count.Roll(rng);
        if (e.ItemId is { } id)
        {
            // CDDA folds a count of a charge-counted item into one stack.
            if (Type(id) is { CountByCharges: true } t && e.Charges is null && count > 1)
            {
                if (Create(id, new IntRange(t.ChargesPerUnit * count, t.ChargesPerUnit * count), e.ContainerItem, rng) is { } stack)
                    into.Add(stack);
                return;
            }
            for (int i = 0; i < count; i++)
                if (Create(id, e.Charges, e.ContainerItem, rng) is { } item)
                    into.Add(item);
            return;
        }
        var group = e.Inline ?? (e.GroupId is { } gid ? Group(gid) : null);
        if (group is null)
            return;
        for (int i = 0; i < count; i++)
            Roll(group, rng, into, depth + 1);
    }
}
