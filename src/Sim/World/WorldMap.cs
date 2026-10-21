namespace Cataclysm3D.Sim.World;

/// <summary>A tile position on a <see cref="WorldMap"/>. z is the CDDA z-level: 0 is ground, -1 a basement.</summary>
public readonly record struct TilePos(int X, int Y, int Z);

/// <summary>How a vertical move gets you there.</summary>
public enum VerticalMoveKind { Stairs, Ladder, Climb }

/// <summary>A planned move between z-levels: where you end up, what it costs and how.</summary>
public readonly record struct VerticalMove(TilePos Destination, int Cost, VerticalMoveKind Kind, string Via);

/// <summary>
/// A stack of equally sized <see cref="TileGrid"/> z-levels, from <see cref="MinZ"/> to
/// <see cref="MaxZ"/>. Stairs (GOES_UP / GOES_DOWN terrain) and CLIMBABLE terrain connect them.
/// </summary>
public sealed class WorldMap
{
    /// <summary>Moves to take stairs or a ladder; CDDA charges about one normal step.</summary>
    public const int StairsCost = 100;

    /// <summary>Moves to climb a downspout or similar. CDDA rolls for failure; we don't yet.</summary>
    public const int ClimbCost = 200;

    /// <summary>How far to look for the matching stairs on the other level (CDDA searches nearby too).</summary>
    public const int StairsSearchRadius = 12;

    private readonly TileGrid[] _levels;

    public TileRegistry Registry { get; }
    public int Width { get; }
    public int Height { get; }
    public int MinZ { get; }
    public int MaxZ { get; }

    /// <summary>Raised when a tile on any level changes, with its (x, y, z).</summary>
    public event Action<int, int, int>? TileChanged;

    /// <param name="fillFor">Terrain each level starts as, e.g. soil below ground and open air above.</param>
    public WorldMap(TileRegistry registry, int width, int height, int minZ, int maxZ, Func<int, string> fillFor)
    {
        if (minZ > 0 || maxZ < 0)
            throw new ArgumentException("The z range must include ground level 0.");
        Registry = registry;
        Width = width;
        Height = height;
        MinZ = minZ;
        MaxZ = maxZ;
        _levels = new TileGrid[maxZ - minZ + 1];
        for (int z = minZ; z <= maxZ; z++)
        {
            var level = new TileGrid(registry, width, height, fillFor(z));
            int lz = z;
            level.TileChanged += (x, y) => TileChanged?.Invoke(x, y, lz);
            _levels[z - minZ] = level;
        }
    }

    public bool HasLevel(int z) => z >= MinZ && z <= MaxZ;

    public TileGrid Level(int z) =>
        HasLevel(z) ? _levels[z - MinZ] : throw new ArgumentOutOfRangeException(nameof(z), z, "No such z-level");

    public bool InBounds(TilePos p) => HasLevel(p.Z) && p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

    public TerrainType TerrainAt(TilePos p) => Level(p.Z).TerrainAt(p.X, p.Y);

    /// <summary>
    /// Whether a tile is under cover: its terrain has a roof or the INDOORS flag, or something
    /// solid sits on the level above it.
    /// </summary>
    public bool IsCovered(TilePos p)
    {
        if (!InBounds(p))
            return false;
        var ter = TerrainAt(p);
        if (ter.Roof is not null || ter.Has("INDOORS"))
            return true;
        return HasLevel(p.Z + 1) && !Level(p.Z + 1).TerrainAt(p.X, p.Y).Has("NO_FLOOR");
    }

    /// <summary>
    /// CDDA adds roofs after mapgen: wherever a tile's terrain names a roof and the level above is
    /// open air, the roof terrain goes there. Fills gaps the <c>_roof</c> mapgen leaves.
    /// </summary>
    public void AddMissingRoofs()
    {
        for (int z = MinZ; z < MaxZ; z++)
        {
            var level = Level(z);
            var above = Level(z + 1);
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (level.TerrainAt(x, y).Roof is { } roof && above.TerrainAt(x, y).Has("NO_FLOOR")
                    && Registry.TryTerrain(roof, out _))
                    above.SetTerrain(x, y, roof);
            }
        }
    }

    /// <summary>
    /// Plans going up (<paramref name="dz"/> = 1, CDDA's <c>&lt;</c>) or down (-1, <c>&gt;</c>) from
    /// <paramref name="origin"/>. Stairs and ladders under you come first; otherwise a CLIMBABLE tile
    /// you face or stand next to (downspouts, gutter drops) lets you climb. Returns false with a
    /// reason when there's no way.
    /// </summary>
    public bool TryPlanVerticalMove(TilePos origin, int dz, (int X, int Y) facing, out VerticalMove move, out string reason)
    {
        move = default;
        if (dz is not (1 or -1))
            throw new ArgumentOutOfRangeException(nameof(dz));
        if (!InBounds(origin))
        {
            reason = "You're nowhere.";
            return false;
        }

        var here = TerrainAt(origin);
        int z = origin.Z + dz;
        string flag = dz > 0 ? "GOES_UP" : "GOES_DOWN";

        if (here.Has(flag))
        {
            if (!HasLevel(z))
            {
                reason = $"The {here.Name} lead{(here.Name.EndsWith('s') ? "" : "s")} nowhere.";
                return false;
            }
            if (FindStairsEnd(origin, z, dz) is not { } end)
            {
                reason = dz > 0 ? "Halfway up, the way up is blocked." : "Halfway down, the way down is blocked.";
                return false;
            }
            var kind = here.Has("DIFFICULT_Z") || here.Id.Contains("ladder", StringComparison.Ordinal)
                ? VerticalMoveKind.Ladder
                : VerticalMoveKind.Stairs;
            move = new VerticalMove(end, StairsCost, kind, here.Name);
            reason = "";
            return true;
        }

        if (HasLevel(z))
        {
            foreach (var (cx, cy) in ClimbCandidates(origin, facing))
            {
                var climbable = Level(origin.Z).TerrainAt(cx, cy);
                if (FindLanding(cx, cy, z) is { } landing)
                {
                    move = new VerticalMove(landing, ClimbCost, VerticalMoveKind.Climb, climbable.Name);
                    reason = "";
                    return true;
                }
            }
        }

        reason = dz > 0 ? "You can't go up here." : "You can't go down here.";
        return false;
    }

    /// <summary>
    /// The far end of a flight of stairs: the nearest tile on level <paramref name="z"/> with the
    /// opposite flag (CDDA lines stairs up under stairs down), or straight above/below when that's
    /// walkable and there are none.
    /// </summary>
    private TilePos? FindStairsEnd(TilePos origin, int z, int dz)
    {
        var level = Level(z);
        string opposite = dz > 0 ? "GOES_DOWN" : "GOES_UP";
        TilePos? best = null;
        int bestDist = int.MaxValue;
        for (int y = Math.Max(0, origin.Y - StairsSearchRadius); y <= Math.Min(Height - 1, origin.Y + StairsSearchRadius); y++)
        for (int x = Math.Max(0, origin.X - StairsSearchRadius); x <= Math.Min(Width - 1, origin.X + StairsSearchRadius); x++)
        {
            if (!level.TerrainAt(x, y).Has(opposite) || !level.IsPassable(x, y))
                continue;
            int d = (x - origin.X) * (x - origin.X) + (y - origin.Y) * (y - origin.Y);
            if (d < bestDist)
            {
                bestDist = d;
                best = new TilePos(x, y, z);
            }
        }
        if (best is null && level.IsPassable(origin.X, origin.Y))
            best = origin with { Z = z };
        return best;
    }

    /// <summary>CLIMBABLE tiles to try, best first: the one you face, the one you're on, then the rest around you.</summary>
    private IEnumerable<(int X, int Y)> ClimbCandidates(TilePos origin, (int X, int Y) facing)
    {
        var level = Level(origin.Z);
        var order = new List<(int, int)> { facing, (origin.X, origin.Y) };
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            order.Add((origin.X + dx, origin.Y + dy));
        foreach (var (x, y) in order.Distinct())
        {
            if (Math.Abs(x - origin.X) <= 1 && Math.Abs(y - origin.Y) <= 1 && level.InBounds(x, y)
                && level.TerrainAt(x, y).Has("CLIMBABLE"))
                yield return (x, y);
        }
    }

    /// <summary>
    /// Where you end up climbing from a tile at (x, y) to level <paramref name="z"/>: straight
    /// above/below it, or the nearest walkable tile next to that. CDDA's downspouts and gutter
    /// drops are often a tile apart.
    /// </summary>
    private TilePos? FindLanding(int x, int y, int z)
    {
        var level = Level(z);
        if (level.IsPassable(x, y))
            return new TilePos(x, y, z);
        foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0), (-1, -1), (1, -1), (1, 1), (-1, 1) })
            if (level.IsPassable(x + dx, y + dy))
                return new TilePos(x + dx, y + dy, z);
        return null;
    }
}
