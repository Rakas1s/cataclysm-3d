using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Sim.Items;

/// <summary>
/// The items lying on each tile of a map (CDDA's map item stacks), including those inside
/// furniture such as fridges and dressers, which CDDA also keeps on the tile.
/// </summary>
public sealed class MapItems
{
    private readonly Dictionary<TilePos, List<Item>> _tiles = new();

    /// <summary>Bumped on every change, so views know when to redraw.</summary>
    public int Version { get; private set; }

    /// <summary>Raised with the tile whose pile changed.</summary>
    public event Action<TilePos>? Changed;

    public IReadOnlyList<Item> At(TilePos p) => _tiles.TryGetValue(p, out var list) ? list : Array.Empty<Item>();

    public IEnumerable<(TilePos Pos, IReadOnlyList<Item> Items)> All =>
        _tiles.Select(kv => (kv.Key, (IReadOnlyList<Item>)kv.Value));

    public int Count => _tiles.Values.Sum(l => l.Count);

    public void Add(TilePos p, Item item)
    {
        if (!_tiles.TryGetValue(p, out var list))
            _tiles[p] = list = new List<Item>();
        list.Add(item);
        Touch(p);
    }

    public bool Remove(TilePos p, Item item)
    {
        if (!_tiles.TryGetValue(p, out var list) || !list.Remove(item))
            return false;
        if (list.Count == 0)
            _tiles.Remove(p);
        Touch(p);
        return true;
    }

    /// <summary>Where an item lies, if it's on this map.</summary>
    public TilePos? Find(Item item)
    {
        foreach (var (p, list) in _tiles)
            if (list.Contains(item))
                return p;
        return null;
    }

    /// <summary>Something about a pile changed in place (a bottle in it was drunk from).</summary>
    public void Touch(TilePos p)
    {
        Version++;
        Changed?.Invoke(p);
    }
}
