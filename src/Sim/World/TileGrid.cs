namespace Cataclysm3D.Sim.World;

/// <summary>A single z-level grid of terrain + furniture. x grows east, y grows south (CDDA convention).</summary>
public sealed class TileGrid
{
    private readonly int[] _terrain;
    private readonly int[] _furniture;

    public TileRegistry Registry { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>Raised when a tile's terrain or furniture changes, so the renderer can rebuild it.</summary>
    public event Action<int, int>? TileChanged;

    public TileGrid(TileRegistry registry, int width, int height, string fillTerrain)
    {
        Registry = registry;
        Width = width;
        Height = height;
        _terrain = new int[width * height];
        _furniture = new int[width * height];
        Array.Fill(_terrain, registry.TerrainIndex(fillTerrain));
        Array.Fill(_furniture, registry.FurnitureIndex(TileRegistry.NullFurniture));
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public TerrainType TerrainAt(int x, int y) => Registry.Terrain[_terrain[y * Width + x]];

    public FurnitureType? FurnitureAt(int x, int y)
    {
        var f = Registry.Furniture[_furniture[y * Width + x]];
        return f.Id == TileRegistry.NullFurniture ? null : f;
    }

    public void SetTerrain(int x, int y, string id)
    {
        _terrain[y * Width + x] = Registry.TerrainIndex(id);
        TileChanged?.Invoke(x, y);
    }

    public void SetFurniture(int x, int y, string? id)
    {
        _furniture[y * Width + x] = Registry.FurnitureIndex(id ?? TileRegistry.NullFurniture);
        TileChanged?.Invoke(x, y);
    }

    /// <summary>CDDA move cost of entering a tile; 0 means impassable.</summary>
    public int MoveCost(int x, int y)
    {
        if (!InBounds(x, y))
            return 0;
        int cost = TerrainAt(x, y).MoveCost;
        if (cost == 0)
            return 0;
        if (FurnitureAt(x, y) is { } f)
        {
            if (f.MoveCostMod < 0)
                return 0;
            cost += f.MoveCostMod;
        }
        return cost;
    }

    public bool IsPassable(int x, int y) => MoveCost(x, y) > 0;

    /// <summary>
    /// Opens a closed door (terrain or furniture). Locked doors with CDDA's OPENCLOSE_INSIDE flag
    /// open only when <paramref name="fromInside"/>, i.e. the opener stands on the indoor side.
    /// Windows also have "open" (curtains, sashes) but bumping them shouldn't open them.
    /// </summary>
    public bool TryOpen(int x, int y, bool fromInside = false)
    {
        if (!InBounds(x, y))
            return false;
        if (FurnitureAt(x, y) is { Open: { } fOpen } && Registry.TryFurniture(fOpen, out _))
        {
            SetFurniture(x, y, fOpen);
            return true;
        }
        var t = TerrainAt(x, y);
        if (t.Open is not { } tOpen || t.Has("WINDOW") || !Registry.TryTerrain(tOpen, out _))
            return false;
        bool canOpen = t.Has("LOCKED") || t.Has("OPENCLOSE_INSIDE") ? fromInside : t.Has("DOOR");
        if (!canOpen)
            return false;
        SetTerrain(x, y, tOpen);
        return true;
    }

    /// <summary>Whether a tile counts as indoors for OPENCLOSE_INSIDE doors: it has a roof.</summary>
    public bool IsIndoors(int x, int y) => InBounds(x, y) && TerrainAt(x, y).Roof is not null;

    public enum BashOutcome { NothingToSmash, Failed, Broke }

    /// <summary>
    /// Smashes furniture first, then terrain, using CDDA's bash data. Returns the outcome and the
    /// sound it makes ("glass breaking!", "whump!", …).
    /// </summary>
    public (BashOutcome Outcome, string Sound, string Name) TryBash(int x, int y, int strength, Random rng)
    {
        if (!InBounds(x, y))
            return (BashOutcome.NothingToSmash, "", "");

        if (FurnitureAt(x, y) is { Bash: { } fb } f)
        {
            if (rng.Next(fb.StrMin, fb.StrMax + 1) > strength)
                return (BashOutcome.Failed, fb.SoundFail, f.Name);
            SetFurniture(x, y, fb.FurnitureSet is { } set && Registry.TryFurniture(set, out _) ? set : null);
            return (BashOutcome.Broke, fb.Sound, f.Name);
        }

        var t = TerrainAt(x, y);
        if (t.Bash is not { TerrainSet: { } terSet } tb || !Registry.TryTerrain(terSet, out _))
            return (BashOutcome.NothingToSmash, "", t.Name);
        if (rng.Next(tb.StrMin, tb.StrMax + 1) > strength)
            return (BashOutcome.Failed, tb.SoundFail, t.Name);
        SetTerrain(x, y, terSet);
        if (tb.FurnitureSet is { } furnSet && Registry.TryFurniture(furnSet, out _))
            SetFurniture(x, y, furnSet);
        return (BashOutcome.Broke, tb.Sound, t.Name);
    }

    /// <summary>Closes an open door (terrain or furniture). Returns true if something closed.</summary>
    public bool TryClose(int x, int y)
    {
        if (!InBounds(x, y))
            return false;
        if (FurnitureAt(x, y) is { Close: { } fClose } && Registry.TryFurniture(fClose, out _))
        {
            SetFurniture(x, y, fClose);
            return true;
        }
        if (TerrainAt(x, y).Close is { } tClose && Registry.TryTerrain(tClose, out int i)
            && Registry.Terrain[i].Has("DOOR"))
        {
            SetTerrain(x, y, tClose);
            return true;
        }
        return false;
    }

    /// <summary>Copies another map's tiles into this one with its top-left at (ox, oy).</summary>
    public void Stamp(TileGrid source, int ox, int oy)
    {
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            if (!InBounds(ox + x, oy + y))
                continue;
            _terrain[(oy + y) * Width + ox + x] = Registry.TerrainIndex(source.TerrainAt(x, y).Id);
            _furniture[(oy + y) * Width + ox + x] = Registry.FurnitureIndex(source.FurnitureAt(x, y)?.Id ?? TileRegistry.NullFurniture);
        }
    }
}
