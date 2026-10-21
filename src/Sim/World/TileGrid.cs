namespace Cataclysm3D.Sim.World;

/// <summary>
/// One z-level of terrain + furniture. x grows east, y grows south (CDDA convention).
/// <see cref="WorldMap"/> stacks these into a multi-level map.
/// </summary>
public sealed class TileGrid
{
    private readonly int[] _terrain;
    private readonly int[] _furniture;
    private readonly int _nullFurniture;

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
        _nullFurniture = registry.FurnitureIndex(TileRegistry.NullFurniture);
        Array.Fill(_furniture, _nullFurniture);
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

    /// <summary>
    /// CDDA move cost of entering a tile; 0 means impassable. Open air (NO_FLOOR) counts as
    /// impassable because falling isn't modeled yet.
    /// </summary>
    public int MoveCost(int x, int y)
    {
        if (!InBounds(x, y))
            return 0;
        var ter = TerrainAt(x, y);
        if (ter.Has("NO_FLOOR"))
            return 0;
        int cost = ter.MoveCost;
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
    /// Whether sight and light pass through a tile: its terrain and any furniture on it are
    /// TRANSPARENT (windows and open doors are; walls, closed doors, drawn curtains, fridges and
    /// bookcases aren't). Off the map counts as opaque.
    /// </summary>
    public bool IsTransparent(int x, int y)
    {
        if (!InBounds(x, y))
            return false;
        int i = y * Width + x;
        return Registry.Terrain[_terrain[i]].IsTransparent
               && (_furniture[i] == _nullFurniture || Registry.Furniture[_furniture[i]].IsTransparent);
    }

    /// <summary>Light the tile's terrain and furniture give off (CDDA's <c>light_emitted</c>).</summary>
    public int LightEmitted(int x, int y)
    {
        int i = y * Width + x;
        return Registry.Terrain[_terrain[i]].LightEmitted + Registry.Furniture[_furniture[i]].LightEmitted;
    }

    /// <summary>
    /// Opens a closed door (terrain or furniture). Locked doors with CDDA's OPENCLOSE_INSIDE flag
    /// open only when <paramref name="fromInside"/>, i.e. the opener stands on the indoor side.
    /// Windows also have "open" (curtains, sashes) but bumping them shouldn't open them.
    /// </summary>
    public bool TryOpen(int x, int y, bool fromInside = false)
    {
        switch (OpenTarget(x, y, fromInside))
        {
            case (true, var id):
                SetFurniture(x, y, id);
                return true;
            case (false, var id):
                SetTerrain(x, y, id);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Whether <see cref="TryOpen"/> would open something, without opening it.</summary>
    public bool CanOpen(int x, int y, bool fromInside = false) => OpenTarget(x, y, fromInside) is not null;

    /// <summary>
    /// Whether a door here only opens from the inside (CDDA's LOCKED / OPENCLOSE_INSIDE), so
    /// bumping it from outside does nothing.
    /// </summary>
    public bool OpensOnlyFromInside(int x, int y) =>
        InBounds(x, y) && TerrainAt(x, y) is { Open: not null } t && !t.Has("WINDOW")
        && (t.Has("LOCKED") || t.Has("OPENCLOSE_INSIDE"));

    private (bool Furniture, string Id)? OpenTarget(int x, int y, bool fromInside)
    {
        if (!InBounds(x, y))
            return null;
        if (FurnitureAt(x, y) is { Open: { } fOpen } && Registry.TryFurniture(fOpen, out _))
            return (true, fOpen);
        var t = TerrainAt(x, y);
        if (t.Open is not { } tOpen || t.Has("WINDOW") || !Registry.TryTerrain(tOpen, out _))
            return null;
        bool canOpen = t.Has("LOCKED") || t.Has("OPENCLOSE_INSIDE") ? fromInside : t.Has("DOOR");
        return canOpen ? (false, tOpen) : null;
    }

    /// <summary>Whether a tile counts as indoors for OPENCLOSE_INSIDE doors: it has a roof.</summary>
    public bool IsIndoors(int x, int y) => InBounds(x, y) && TerrainAt(x, y).Roof is not null;

    public enum BashOutcome { NothingToSmash, Failed, Broke }

    /// <summary>
    /// Smashes furniture first, then terrain, using CDDA's bash data. Returns the outcome and the
    /// sound it makes ("glass breaking!", "whump!", …).
    /// </summary>
    /// <summary>
    /// What <see cref="TryBash"/> would hit here (furniture first, then terrain) and its bash
    /// data, or null if there's nothing to smash.
    /// </summary>
    public (string Name, BashInfo Bash)? BashTarget(int x, int y)
    {
        if (!InBounds(x, y))
            return null;
        if (FurnitureAt(x, y) is { Bash: { } fb } f)
            return (f.Name, fb);
        var t = TerrainAt(x, y);
        return t.Bash is { TerrainSet: { } terSet } tb && Registry.TryTerrain(terSet, out _) ? (t.Name, tb) : null;
    }

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
        switch (CloseTarget(x, y))
        {
            case (true, var id):
                SetFurniture(x, y, id);
                return true;
            case (false, var id):
                SetTerrain(x, y, id);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Whether <see cref="TryClose"/> would close something, without closing it.</summary>
    public bool CanClose(int x, int y) => CloseTarget(x, y) is not null;

    private (bool Furniture, string Id)? CloseTarget(int x, int y)
    {
        if (!InBounds(x, y))
            return null;
        if (FurnitureAt(x, y) is { Close: { } fClose } && Registry.TryFurniture(fClose, out _))
            return (true, fClose);
        if (TerrainAt(x, y).Close is { } tClose && Registry.TryTerrain(tClose, out int i)
            && Registry.Terrain[i].Has("DOOR"))
            return (false, tClose);
        return null;
    }

    /// <summary>
    /// Copies another map's tiles into this one with its top-left at (ox, oy), turned
    /// <paramref name="quarterTurns"/> × 90° clockwise first. The turn happens inside a
    /// <paramref name="frameWidth"/> × <paramref name="frameHeight"/> frame (default: the source's
    /// size), so a map smaller than its lot turns with the lot.
    /// </summary>
    public void Stamp(TileGrid source, int ox, int oy, int quarterTurns = 0, int? frameWidth = null, int? frameHeight = null)
    {
        int fw = frameWidth ?? source.Width, fh = frameHeight ?? source.Height;
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            var (rx, ry) = Rotate(x, y, fw, fh, quarterTurns);
            if (!InBounds(ox + rx, oy + ry))
                continue;
            int i = (oy + ry) * Width + ox + rx;
            _terrain[i] = Registry.TerrainIndex(source.TerrainAt(x, y).Id);
            _furniture[i] = Registry.FurnitureIndex(source.FurnitureAt(x, y)?.Id ?? TileRegistry.NullFurniture);
        }
    }

    /// <summary>Where (x, y) of a width × height map lands after turning it clockwise.</summary>
    public static (int X, int Y) Rotate(int x, int y, int width, int height, int quarterTurns) =>
        (((quarterTurns % 4) + 4) % 4) switch
        {
            1 => (height - 1 - y, x),
            2 => (width - 1 - x, height - 1 - y),
            3 => (y, width - 1 - x),
            _ => (x, y),
        };
}
