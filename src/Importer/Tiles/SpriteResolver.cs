namespace Cataclysm3D.Importer.Tiles;

/// <summary>A tile entry found for an id, and which tileset and (looks_like) id it came from.</summary>
public sealed record ResolvedTile(Tileset Tileset, TileEntry Entry, string RequestedId, string MatchedId);

/// <summary>
/// The sprites to draw for one tile. <see cref="Fg"/> / <see cref="Bg"/> are indices into
/// <see cref="Tileset"/>'s sprites, or -1 for none. <see cref="Rotation"/> is in clockwise quarter
/// turns and only set when the tileset has a single sprite for a piece that needs rotating
/// (CDDA rotates the image then).
/// </summary>
public readonly record struct SpriteChoice(Tileset Tileset, int Fg, int Bg, int Rotation, string Subtile);

/// <summary>
/// Finds the sprite for a CDDA id across a priority list of tilesets, the way CDDA's tile
/// renderer does for one: seasonal variant first, then the id itself, then its
/// <c>looks_like</c> chain. With several tilesets, each id in that chain is tried in every
/// tileset before moving down the chain, so UndeadPeople's art for an id beats Ultica's, and
/// either tileset's art for the id itself beats a looks_like stand-in.
/// </summary>
public sealed class SpriteResolver
{
    private const int MaxLooksLikeDepth = 10;

    private readonly IReadOnlyList<Tileset> _tilesets;
    private readonly Func<string, string?> _looksLike;
    private readonly Dictionary<(string, string?), ResolvedTile?> _cache = new();

    /// <param name="tilesets">In priority order.</param>
    /// <param name="looksLike">CDDA's <c>looks_like</c> for an id, or null.</param>
    public SpriteResolver(IReadOnlyList<Tileset> tilesets, Func<string, string?> looksLike)
    {
        _tilesets = tilesets;
        _looksLike = looksLike;
    }

    public IReadOnlyList<Tileset> Tilesets => _tilesets;

    /// <param name="season">"spring", "summer", "autumn" or "winter"; tries <c>id_season_&lt;season&gt;</c> first.</param>
    public ResolvedTile? Resolve(string id, string? season = null)
    {
        if (_cache.TryGetValue((id, season), out var hit))
            return hit;
        var found = Resolve(id, season, _tilesets);
        _cache[(id, season)] = found;
        return found;
    }

    /// <summary>Like <see cref="Resolve(string, string?)"/>, but only looks in one tileset (uncached).</summary>
    public ResolvedTile? ResolveIn(Tileset set, string id, string? season = null) => Resolve(id, season, new[] { set });

    /// <summary>Like <see cref="Resolve(string, string?)"/>, but only looks in the primary (first) tileset.</summary>
    public ResolvedTile? ResolvePrimary(string id, string? season = null) =>
        _tilesets.Count == 0 ? null : Resolve(id, season, _tilesets.Take(1).ToList());

    private ResolvedTile? Resolve(string id, string? season, IReadOnlyList<Tileset> sets)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? cur = id; cur is not null && seen.Count < MaxLooksLikeDepth && seen.Add(cur); cur = _looksLike(cur))
            if (Find(id, cur, season, sets) is { } found)
                return found;
        return null;
    }

    private static ResolvedTile? Find(string requested, string id, string? season, IReadOnlyList<Tileset> sets)
    {
        foreach (var set in sets)
        {
            if (season is not null && set.TryGet($"{id}_season_{season}", out var seasonal) && HasArt(seasonal))
                return new ResolvedTile(set, seasonal, requested, seasonal.Id);
            if (set.TryGet(id, out var entry) && HasArt(entry))
                return new ResolvedTile(set, entry, requested, id);
        }
        return null;
    }

    private static bool HasArt(TileEntry e) => !e.Fg.IsEmpty || !e.Bg.IsEmpty || e.Subtiles.Values.Any(s => !s.Fg.IsEmpty);

    /// <summary>
    /// Picks the sprites for a resolved tile at a map position. <paramref name="connections"/> is
    /// CDDA's neighbor mask for multitiles (<see cref="Multitile.Mask"/>); <paramref name="rotation"/>
    /// is the facing for <c>rotates</c> tiles (0–3). Random variants are picked by a hash of the
    /// position, so a tile keeps its look between frames and rebuilds.
    /// </summary>
    public static SpriteChoice Choose(ResolvedTile tile, int x, int y, int connections = 0, int rotation = 0)
    {
        var entry = tile.Entry;
        string subtile = "";
        int rota = tile.Entry.Rotates ? rotation : 0;
        bool needsRotation = rota != 0;
        if (entry.Multitile)
        {
            var (name, r) = Multitile.Subtile(connections, rota);
            if (entry.Subtiles.TryGetValue(name, out var piece) && !piece.Fg.IsEmpty)
            {
                entry = piece;
                subtile = name;
                rota = r;
                needsRotation = name is not ("center" or "unconnected") || rota != 0;
            }
        }

        uint h = Hash(x, y);
        int fg = Pick(entry.Fg, h, rota, needsRotation, out bool fgRotated);
        // A piece without its own background uses the parent's.
        var bgList = entry.Bg.IsEmpty ? tile.Entry.Bg : entry.Bg;
        int bg = Pick(bgList, h >> 7, 0, false, out _);
        return new SpriteChoice(tile.Tileset, fg, bg, fgRotated ? 0 : rota, subtile);
    }

    /// <param name="usedRotationList">True when the list held a sprite per rotation, so the image needs no turning.</param>
    private static int Pick(SpriteList list, uint hash, int rotation, bool needsRotation, out bool usedRotationList)
    {
        usedRotationList = false;
        if (list.IsEmpty)
            return -1;
        var variant = list.Variants[0];
        if (list.Variants.Count > 1)
        {
            int total = list.Variants.Sum(v => v.Weight);
            int roll = (int)(hash % (uint)total);
            foreach (var v in list.Variants)
            {
                variant = v;
                if ((roll -= v.Weight) < 0)
                    break;
            }
        }
        var sprites = variant.Sprites;
        if (sprites.Count == 1)
            return sprites[0];
        if (needsRotation)
        {
            usedRotationList = true;
            return sprites[rotation % sprites.Count];
        }
        // A plain list on a tile that doesn't turn: treat the entries as look variants.
        return sprites[(int)((hash >> 3) % (uint)sprites.Count)];
    }

    private static uint Hash(int x, int y)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }
}

/// <summary>CDDA's multitile rules: which piece (and rotation) a connecting tile draws.</summary>
public static class Multitile
{
    public const int South = 1, East = 2, West = 4, North = 8;

    /// <summary>Builds CDDA's connection mask from which neighbors connect (map y grows south).</summary>
    public static int Mask(bool north, bool east, bool south, bool west) =>
        (south ? South : 0) | (east ? East : 0) | (west ? West : 0) | (north ? North : 0);

    /// <summary>CDDA's <c>get_rotation_and_subtile</c>.</summary>
    public static (string Subtile, int Rotation) Subtile(int mask, int unconnectedRotation = 0) => mask switch
    {
        15 => ("center", 0),
        8 => ("end_piece", 2),
        4 => ("end_piece", 3),
        2 => ("end_piece", 1),
        1 => ("end_piece", 0),
        9 => ("edge", 0),
        6 => ("edge", 1),
        12 => ("corner", 2),
        10 => ("corner", 1),
        3 => ("corner", 0),
        5 => ("corner", 3),
        14 => ("t_connection", 2),
        11 => ("t_connection", 1),
        7 => ("t_connection", 0),
        13 => ("t_connection", 3),
        _ => ("unconnected", unconnectedRotation),
    };
}
