namespace Cataclysm3D.Sim.World;

/// <summary>Fields shared by CDDA terrain and furniture.</summary>
public abstract class TileType
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Symbol { get; init; } = "?";

    /// <summary>CDDA color name, e.g. "light_gray".</summary>
    public string Color { get; init; } = "white";

    /// <summary>Id this one should look like when we have no art for it (CDDA's <c>looks_like</c>).</summary>
    public string? LooksLike { get; init; }

    /// <summary>Percent of a tile this blocks for cover/vision; doubles as a height hint for rendering.</summary>
    public int Coverage { get; init; }

    public IReadOnlySet<string> Flags { get; init; } = new HashSet<string>();

    /// <summary>Id this becomes when opened / closed, if it can be.</summary>
    public string? Open { get; init; }
    public string? Close { get; init; }

    /// <summary>What happens when this is smashed, or null if it can't be.</summary>
    public BashInfo? Bash { get; init; }

    /// <summary>CDDA's <c>light_emitted</c>: how bright a light this gives off, or 0.</summary>
    public int LightEmitted { get; init; }

    public bool Has(string flag) => Flags.Contains(flag);

    private bool? _transparent;

    /// <summary>Whether sight and light pass through (CDDA's TRANSPARENT flag). Cached: FOV asks a lot.</summary>
    public bool IsTransparent => _transparent ??= Has("TRANSPARENT");
}

/// <summary>
/// CDDA's <c>bash</c> block. A smash succeeds when a roll between <see cref="StrMin"/> and
/// <see cref="StrMax"/> is at most the basher's strength.
/// </summary>
public sealed record BashInfo(int StrMin, int StrMax, string? TerrainSet, string? FurnitureSet, string Sound, string SoundFail);

public sealed class TerrainType : TileType
{
    /// <summary>Move cost multiplier; 0 means impassable (walls, closed doors, windows).</summary>
    public int MoveCost { get; init; } = 2;

    /// <summary>Terrain id of the roof above this tile, or null if open to the sky.</summary>
    public string? Roof { get; init; }
}

public sealed class FurnitureType : TileType
{
    /// <summary>Added to the terrain's move cost; -1 means impassable.</summary>
    public int MoveCostMod { get; init; }
}

/// <summary>All terrain and furniture types, indexed for compact map storage.</summary>
public sealed class TileRegistry
{
    private readonly List<TerrainType> _terrain = new();
    private readonly List<FurnitureType> _furniture = new();
    private readonly Dictionary<string, int> _terrainIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _furnitureIndex = new(StringComparer.Ordinal);

    public const string NullTerrain = "t_null";
    public const string NullFurniture = "f_null";

    public TileRegistry()
    {
        Add(new TerrainType { Id = NullTerrain, Name = "nothing", MoveCost = 2 });
        Add(new FurnitureType { Id = NullFurniture, Name = "nothing" });
    }

    public IReadOnlyList<TerrainType> Terrain => _terrain;
    public IReadOnlyList<FurnitureType> Furniture => _furniture;

    public void Add(TerrainType t)
    {
        _terrainIndex[t.Id] = _terrain.Count;
        _terrain.Add(t);
    }

    public void Add(FurnitureType f)
    {
        _furnitureIndex[f.Id] = _furniture.Count;
        _furniture.Add(f);
    }

    public bool TryTerrain(string id, out int index) => _terrainIndex.TryGetValue(id, out index);
    public bool TryFurniture(string id, out int index) => _furnitureIndex.TryGetValue(id, out index);

    public int TerrainIndex(string id) =>
        _terrainIndex.TryGetValue(id, out int i) ? i : throw new KeyNotFoundException($"Unknown terrain \"{id}\"");

    public int FurnitureIndex(string id) =>
        _furnitureIndex.TryGetValue(id, out int i) ? i : throw new KeyNotFoundException($"Unknown furniture \"{id}\"");
}
