using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// Turns one tile's terrain + furniture into voxels. Shapes are chosen from CDDA flags
/// (WALL, WINDOW, TREE, SHRUB, THIN_OBSTACLE, …). Where the tilesets have art (<see cref="Art"/>),
/// floors, roofs and walls carry the tile's sprite as a texture, doors are painted from theirs,
/// and furniture is a separate model extruded from its sprite (<see cref="FurnitureModel"/>);
/// ids with no art keep placeholder shapes in colors from <see cref="TilePalette"/>. Each z-level
/// has its own volume; its floor sits at voxel y 0 and its walls reach the next level.
/// </summary>
public static class TileVoxelizer
{
    private const int N = VoxelVolume.PerTile;

    /// <summary>Tileset art, or null to draw everything as placeholders.</summary>
    public static TileArt? Art { get; set; }

    /// <summary>Tile types, for looking up the closed version of an open door.</summary>
    public static TileRegistry? Registry { get; set; }

    private static readonly (int Dx, int Dy)[] Sides = { (0, -1), (1, 0), (0, 1), (-1, 0) };

    /// <summary>Top of a storey's walls, exclusive (3 m). The next level's floor starts here.</summary>
    public const int WallTop = VoxelVolume.StoreyHeight;

    public static void Fill(VoxelVolume volume, WorldMap world, int tx, int ty, int tz)
    {
        volume.ClearTile(tx, ty);
        var map = world.Level(tz);
        var ter = map.TerrainAt(tx, ty);
        var b = new Brush(volume, tx, ty);

        // Open air and the top of a stairwell are holes; solid earth only shows where it meets a room.
        if (ter.Has("NO_FLOOR") || (ter.Has("NATURAL_UNDERGROUND") && IsBuried(map, tx, ty)))
            return;
        if (ter.Has("GOES_DOWN") && !ter.Has("GOES_UP"))
            return;

        b.Box(0, N, 0, 1, 0, N, ColorOf(ter), FloorSlot(map, ter, tx, ty));
        if (ter.Has("GOES_UP") && !ter.Has("LIQUID"))
            DrawWayUp(b, map, ter, tx, ty);
        else
            DrawTerrain(b, map, ter, tx, ty);
        if (map.FurnitureAt(tx, ty) is { } fur && FurnitureModel(world, tx, ty, tz) is null)
            DrawFurniture(b, fur);
        if (HasCeiling(world, ter, tx, ty, tz))
            b.Box(0, N, WallTop - 1, WallTop, 0, N, TilePalette.Ceiling);
    }

    /// <summary>
    /// A ceiling under anything solid on the level above, so rooms stay closed in when the upper
    /// levels are hidden. Stairwells stay open.
    /// </summary>
    private static bool HasCeiling(WorldMap world, TerrainType ter, int tx, int ty, int tz)
    {
        if (!world.HasLevel(tz + 1) || ter.Has("GOES_UP"))
            return false;
        var above = world.Level(tz + 1).TerrainAt(tx, ty);
        return !above.Has("NO_FLOOR") && !above.Has("GOES_DOWN");
    }

    private static bool IsBuried(TileGrid map, int tx, int ty)
    {
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            if (map.InBounds(tx + dx, ty + dy) && !map.TerrainAt(tx + dx, ty + dy).Has("NATURAL_UNDERGROUND"))
                return false;
        return true;
    }

    // ---- Tileset art ------------------------------------------------------------------------

    /// <summary>A tile's color: the mean of its sprite if a tileset has one, else the placeholder palette.</summary>
    private static Color ColorOf(TileType t) => Art?.AverageColor(t) ?? TilePalette.For(t);

    /// <summary>Whether a terrain is something you walk on, so its whole sprite is its floor.</summary>
    private static bool IsSurface(TerrainType t) =>
        !IsWallish(t) && !t.Has("TREE") && !t.Has("SHRUB") && !t.Has("GOES_UP") && !t.Has("THIN_OBSTACLE")
        && !t.Id.Contains("fence", StringComparison.Ordinal) && !t.Id.Contains("railing", StringComparison.Ordinal)
        && !t.Id.Contains("downspout", StringComparison.Ordinal);

    /// <summary>
    /// The atlas slot for a tile's floor. Floors, roofs and other ground use their sprite (fg over
    /// bg, connecting to neighbors of the same id like CDDA's multitiles: carpet borders, grass
    /// edges); anything standing on the tile (walls, doors, fences, trees) uses only its bg, or the
    /// floor next to it.
    /// </summary>
    private static int FloorSlot(TileGrid map, TerrainType t, int tx, int ty, int depth = 0)
    {
        if (Art is not { } art || art.Choose(t, tx, ty, Connections(map, t, tx, ty)) is not { } c)
            return 0;
        if (IsSurface(t))
            return SpriteSlot(art, c, c.Fg, c.Bg);
        if (c.Bg >= 0)
            return SpriteSlot(art, c, -1, c.Bg);
        if (depth == 0)
            foreach (var (dx, dy) in Sides)
                if (map.InBounds(tx + dx, ty + dy) && map.TerrainAt(tx + dx, ty + dy) is { } n && IsSurface(n) && !n.Has("NO_FLOOR"))
                    return FloorSlot(map, n, tx + dx, ty + dy, 1);
        return 0;
    }

    /// <summary>
    /// The atlas slot for a wall's faces: the inside ("center") piece of the wall's multitile.
    /// Only the primary tileset's walls are flat textures that tile as a face; fallback walls
    /// (Ultica draws them with bevelled tops) get plain voxels in their sprite's mean color.
    /// </summary>
    private static int WallSlot(TerrainType t, int tx, int ty)
    {
        if (Art is not { } art || art.Choose(t, tx, ty, 15, primaryOnly: true) is not { } c)
            return 0;
        return SpriteSlot(art, c, c.Fg, c.Bg);
    }

    /// <summary>CDDA multitile neighbor mask: which sides have the same id (walls connect to any wall).</summary>
    private static int Connections(TileGrid map, TileType t, int tx, int ty)
    {
        bool Connects(int x, int y) => map.InBounds(x, y) && (t is FurnitureType
            ? map.FurnitureAt(x, y)?.Id == t.Id
            : map.TerrainAt(x, y).Id == t.Id || (t.Has("WALL") && map.TerrainAt(x, y).Has("WALL")));
        return Importer.Tiles.Multitile.Mask(Connects(tx, ty - 1), Connects(tx + 1, ty), Connects(tx, ty + 1), Connects(tx - 1, ty));
    }

    /// <summary>Paints fg over bg into an atlas slot; whatever stays see-through gets the sprite's mean color.</summary>
    private static int SpriteSlot(TileArt art, Importer.Tiles.SpriteChoice c, int fg, int bg) =>
        art.Atlas.SlotFor((c.Tileset, fg, bg, c.Rotation), () =>
        {
            var layers = new[] { art.Pixels(c.Tileset, bg, 0), art.Pixels(c.Tileset, fg, c.Rotation) };
            if (layers.All(l => l is null))
                return null;
            const int S = SurfaceAtlas.SlotSize;
            var under = (layers[1] ?? layers[0])!.Average;
            var px = new byte[S * S * 4];
            for (int i = 0; i < px.Length; i += 4)
                (px[i], px[i + 1], px[i + 2], px[i + 3]) = ((byte)under.R8, (byte)under.G8, (byte)under.B8, 255);
            foreach (var l in layers)
            {
                if (l is null)
                    continue;
                // The part of the sprite over the tile itself (big sprites hang over their neighbors).
                for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int sx = x - l.OffsetX, sy = y - l.OffsetY;
                    if (sx < 0 || sy < 0 || sx >= l.Width || sy >= l.Height)
                        continue;
                    int si = (sy * l.Width + sx) * 4, di = (y * S + x) * 4;
                    int a = l.Rgba[si + 3];
                    for (int ch = 0; ch < 3; ch++)
                        px[di + ch] = (byte)((l.Rgba[si + ch] * a + px[di + ch] * (255 - a)) / 255);
                }
            }
            return Image.CreateFromData(S, S, false, Image.Format.Rgba8, px);
        });

    /// <summary>
    /// The extruded model for the furniture on a tile, and which way it faces (radians about up,
    /// 0 = its front faces south), or null when there's no furniture or no art for it.
    /// </summary>
    public static (Mesh Mesh, float Yaw)? FurnitureModel(WorldMap world, int tx, int ty, int tz)
    {
        var map = world.Level(tz);
        if (Art is not { } art || map.FurnitureAt(tx, ty) is not { } f)
            return null;
        var resolved = art.Resolve(f);
        if (resolved is null)
            return null;
        bool multitile = resolved.Entry.Multitile;
        var choice = art.Choose(f, tx, ty, multitile ? Connections(map, f, tx, ty) : 0);
        if (choice is not { Fg: >= 0 } c || art.Models.For(f, c) is not { } mesh)
            return null;
        // Joined pieces (tables, counters, beds) are drawn in map orientation already.
        return (mesh, multitile ? 0f : FacingYaw(map, f, tx, ty));
    }

    /// <summary>
    /// Which way a piece of furniture faces: seats face a table next to them; other things turn
    /// their back to a wall. CDDA sprites always face the viewer, so this is ours.
    /// </summary>
    private static float FacingYaw(TileGrid map, FurnitureType f, int tx, int ty)
    {
        // Yaw that turns a model's front (+z, south) toward each side: north, east, south, west.
        float[] toward = { Mathf.Pi, Mathf.Pi / 2, 0, -Mathf.Pi / 2 };
        string id = f.Id;
        if (id.Contains("chair", StringComparison.Ordinal) || id.Contains("stool", StringComparison.Ordinal))
        {
            for (int i = 0; i < 4; i++)
            {
                var (dx, dy) = Sides[i];
                if (map.InBounds(tx + dx, ty + dy) && map.FurnitureAt(tx + dx, ty + dy) is { } n
                    && (n.Id.Contains("table", StringComparison.Ordinal) || n.Id.Contains("desk", StringComparison.Ordinal)))
                    return toward[i];
            }
        }
        foreach (int i in new[] { 0, 1, 3, 2 })
        {
            var (dx, dy) = Sides[i];
            if (map.InBounds(tx + dx, ty + dy) && IsWallish(map.TerrainAt(tx + dx, ty + dy)))
                return toward[(i + 2) % 4];
        }
        return 0;
    }

    /// <summary>
    /// Stairs climb one storey across their tile, rising away from the open side you walk onto
    /// them from. Ladders stand in the middle of the tile.
    /// </summary>
    private static void DrawWayUp(Brush b, TileGrid map, TerrainType t, int tx, int ty)
    {
        if (t.Has("DIFFICULT_Z") || t.Id.Contains("ladder", StringComparison.Ordinal))
        {
            var metal = TilePalette.FromCddaName("dark_gray");
            b.Box(1, 2, 1, WallTop, 3, 5, metal);
            b.Box(N - 2, N - 1, 1, WallTop, 3, 5, metal);
            for (int y = 3; y < WallTop; y += 3)
                b.Box(2, N - 2, y, y + 1, 4, 5, metal);
            return;
        }

        var (dx, dy) = RiseDirection(map, tx, ty);
        var color = ColorOf(t);
        const int StepRise = WallTop / N;
        for (int i = 0; i < N; i++)
        {
            int top = Math.Min(1 + StepRise * (i + 1), WallTop);
            var c = i % 2 == 0 ? color : color.Darkened(0.08f);
            // Slice i of the flight, counted from the bottom step.
            int a0 = dx + dy > 0 ? i : N - 1 - i;
            if (dx != 0)
                b.Box(a0, a0 + 1, 1, top, 0, N, c);
            else
                b.Box(0, N, 1, top, a0, a0 + 1, c);
        }
    }

    /// <summary>Which way a flight of stairs rises: away from the first walkable, non-stair neighbor.</summary>
    public static (int Dx, int Dy) RiseDirection(TileGrid map, int tx, int ty)
    {
        foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
        {
            int nx = tx + dx, ny = ty + dy;
            if (map.InBounds(nx, ny) && map.IsPassable(nx, ny)
                && !map.TerrainAt(nx, ny).Has("GOES_UP") && !map.TerrainAt(nx, ny).Has("GOES_DOWN"))
                return (-dx, -dy);
        }
        return (0, -1);
    }

    private static bool IsWallish(TerrainType t) =>
        t.Has("WALL") || t.Has("WINDOW") || IsDoor(t) || (t.MoveCost == 0 && !t.Has("TREE"));

    private static bool IsDoor(TerrainType t) => t.Id.Contains("door", StringComparison.Ordinal) && !t.Has("WALL");

    /// <summary>True when walls continue east/west, so thin features (glass, doors) run along x.</summary>
    private static bool RunsAlongX(TileGrid map, int tx, int ty)
    {
        bool ew = (map.InBounds(tx - 1, ty) && IsWallish(map.TerrainAt(tx - 1, ty)))
                  || (map.InBounds(tx + 1, ty) && IsWallish(map.TerrainAt(tx + 1, ty)));
        bool ns = (map.InBounds(tx, ty - 1) && IsWallish(map.TerrainAt(tx, ty - 1)))
                  || (map.InBounds(tx, ty + 1) && IsWallish(map.TerrainAt(tx, ty + 1)));
        return ew || !ns;
    }

    /// <summary>Look of a neighboring wall, so window sills and door frames match it.</summary>
    private static (Color Color, int Slot) NeighborWall(TileGrid map, int tx, int ty)
    {
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            if (map.InBounds(tx + dx, ty + dy) && map.TerrainAt(tx + dx, ty + dy) is { } n && n.Has("WALL"))
                return (ColorOf(n), WallSlot(n, tx + dx, ty + dy));
        return (TilePalette.FromCddaName("light_gray"), 0);
    }

    /// <summary>
    /// Paints a door's panel (given as along × up across the doorway) from the door's sprite,
    /// which CDDA draws as a front view. Without art, the panel is the door's color.
    /// </summary>
    private static void DoorPanel(Brush b, TerrainType t, bool alongX, int a0, int a1, int y0, int y1, int c0, int c1, float holes = 0)
    {
        if (Art?.Choose(t, 0, 0) is not { Fg: >= 0 } c || Art.Pixels(c.Tileset, c.Fg, c.Rotation) is not { } s || s.Bounds() is not { } r)
        {
            b.Thin(alongX, a0, a1, y0, y1, c0, c1, ColorOf(t), holes);
            return;
        }
        for (int a = a0; a < a1; a++)
        for (int y = y0; y < y1; y++)
        {
            int px = r.X0 + (a - a0) * (r.X1 - r.X0) / (a1 - a0);
            int py = r.Y1 - 1 - (y - y0) * (r.Y1 - r.Y0) / (y1 - y0);
            var color = s.Opaque(px, py) ? s.At(px, py) : s.Average;
            b.Thin(alongX, a, a + 1, y, y + 1, c0, c1, color, holes);
        }
    }

    private static void DrawTerrain(Brush b, TileGrid map, TerrainType t, int tx, int ty)
    {
        var color = ColorOf(t);
        string id = t.Id;

        if (t.Has("WALL"))
        {
            b.Box(0, N, 1, WallTop, 0, N, color, WallSlot(t, tx, ty));
        }
        else if (t.Has("WINDOW"))
        {
            var (wall, wallSlot) = NeighborWall(map, tx, ty);
            b.Box(0, N, 1, 8, 0, N, wall, wallSlot);
            b.Box(0, N, 19, WallTop, 0, N, wall, wallSlot);
            bool alongX = RunsAlongX(map, tx, ty);
            bool open = t.MoveCost > 0;
            var pane = t.IsTransparent ? TilePalette.Glass : color; // curtains drawn shut
            b.Thin(alongX, 0, N, 8, 19, 3, 5, TilePalette.FromCddaName("white")); // frame
            b.Thin(alongX, 1, N - 1, 9, 18, 3, 5, 0u);
            if (!open)
                b.Thin(alongX, 1, N - 1, 9, 18, 4, 5, pane);
        }
        else if (IsDoor(t))
        {
            var (wall, wallSlot) = NeighborWall(map, tx, ty);
            bool alongX = RunsAlongX(map, tx, ty);
            b.Box(0, N, 18, WallTop, 0, N, wall, wallSlot); // lintel
            b.Thin(alongX, 0, 1, 1, 18, 0, N, wall, slot: wallSlot); // posts
            b.Thin(alongX, N - 1, N, 1, 18, 0, N, wall, slot: wallSlot);
            if (t.MoveCost == 0 && id.EndsWith("_b", StringComparison.Ordinal))
                DoorPanel(b, t, alongX, 1, N - 1, 1, 18, 3, 5, holes: 0.45f); // smashed panel
            else if (t.MoveCost == 0)
                DoorPanel(b, t, alongX, 1, N - 1, 1, 18, 3, 5); // closed panel in the doorway
            else if (t.Close is { } closed && Registry is not null && Registry.TryTerrain(closed, out int ci))
                DoorPanel(b, Registry.Terrain[ci], !alongX, 4, N, 1, 18, 1, 2); // swung open: the closed door's face
            else
                b.Thin(alongX, 1, 2, 1, 18, 4, N, color); // swung open against the frame
        }
        else if (t.Has("TREE"))
        {
            b.Box(3, 5, 1, 22, 3, 5, TilePalette.Bark);
            b.Blob(4, 26, 4, 4.5f, 7f, Art?.AverageColor(t) is { } leaves ? leaves.Lerp(TilePalette.Leaves, 0.3f) : TilePalette.Leaves);
        }
        else if (t.Has("SHRUB"))
        {
            b.Blob(4, 3, 4, 3.6f, 3.2f, color.Lerp(TilePalette.Leaves, 0.6f));
        }
        else if (id.Contains("downspout", StringComparison.Ordinal))
        {
            b.Box(0, 1, 1, WallTop, 0, 1, color);
        }
        else if (t.Has("THIN_OBSTACLE") || id.Contains("fence", StringComparison.Ordinal) || id.Contains("railing", StringComparison.Ordinal))
        {
            b.Box(3, 5, 1, 8, 3, 5, color);
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (!map.InBounds(tx + dx, ty + dy) || map.TerrainAt(tx + dx, ty + dy).Id != id)
                    continue;
                int x0 = dx > 0 ? 4 : dx < 0 ? 0 : 3, x1 = dx > 0 ? N : dx < 0 ? 4 : 5;
                int z0 = dy > 0 ? 4 : dy < 0 ? 0 : 3, z1 = dy > 0 ? N : dy < 0 ? 4 : 5;
                b.Box(x0, x1, 6, 8, z0, z1, color);
                b.Box(x0, x1, 3, 4, z0, z1, color);
            }
        }
        else if (t.MoveCost == 0)
        {
            b.Box(0, N, 1, 12, 0, N, color);
        }
    }

    private static void DrawFurniture(Brush b, FurnitureType f)
    {
        var c = ColorOf(f);
        string id = f.Id;
        bool Is(string k) => id.Contains(k, StringComparison.Ordinal);

        if (Is("table") || Is("desk"))
        {
            b.Box(0, N, 6, 7, 0, N, c);
            foreach (var (x, z) in new[] { (0, 0), (N - 1, 0), (0, N - 1), (N - 1, N - 1) })
                b.Box(x, x + 1, 1, 6, z, z + 1, c.Darkened(0.2f));
        }
        else if (Is("armchair") || Is("sofa") || Is("couch"))
        {
            b.Box(0, N, 1, 4, 0, N, c);
            b.Box(0, N, 4, 7, 0, 2, c.Darkened(0.1f));
            b.Box(0, 1, 4, 5, 2, N, c);
            b.Box(N - 1, N, 4, 5, 2, N, c);
        }
        else if (Is("chair") || Is("stool"))
        {
            b.Box(2, 6, 3, 4, 2, 6, c);
            foreach (var (x, z) in new[] { (2, 2), (5, 2), (2, 5), (5, 5) })
                b.Box(x, x + 1, 1, 3, z, z + 1, c.Darkened(0.2f));
            if (Is("chair"))
                b.Box(2, 6, 4, 8, 2, 3, c);
        }
        else if (Is("bed"))
        {
            b.Box(0, N, 1, 2, 0, N, TilePalette.FromCddaName("brown"));
            b.Box(0, N, 2, 4, 0, N, c);
        }
        else if (Is("toilet"))
        {
            b.Box(2, 6, 1, 4, 3, 7, c);
            b.Box(2, 6, 1, 8, 1, 3, c);
        }
        else if (Is("bathtub"))
        {
            b.Box(0, N, 1, 5, 0, N, c);
            b.Box(1, N - 1, 2, 5, 1, N - 1, 0u);
        }
        else if (Is("lamp"))
        {
            b.Box(3, 5, 1, 13, 3, 5, TilePalette.FromCddaName("dark_gray"));
            b.Box(2, 6, 12, 15, 2, 6, new Color(0.95f, 0.9f, 0.7f));
        }
        else if (Is("plant"))
        {
            b.Box(2, 6, 1, 4, 2, 6, TilePalette.FromCddaName("brown"));
            b.Blob(4, 6, 4, 2.6f, 3f, c);
        }
        else if (Is("trashcan") || Is("trash_can"))
        {
            b.Box(2, 6, 1, 6, 2, 6, c);
        }
        else if (Is("mailbox"))
        {
            b.Box(3, 5, 1, 8, 3, 5, TilePalette.FromCddaName("brown"));
            b.Box(2, 6, 8, 10, 1, 7, c);
        }
        else if (Is("rack_coat"))
        {
            b.Box(3, 5, 1, 14, 3, 5, c);
        }
        else if (Is("fridge") || Is("bookcase") || Is("wardrobe") || Is("rack") || Is("safe") || Is("locker"))
        {
            b.Box(0, N, 1, 15, 1, N - 1, c);
        }
        else if (Is("counter") || Is("cupboard") || Is("sink") || Is("oven") || Is("dishwasher")
                 || Is("washer") || Is("dryer") || Is("dresser") || Is("cabinet"))
        {
            b.Box(0, N, 1, 8, 0, N, c);
            b.Box(0, N, 8, 9, 0, N, c.Lightened(0.15f));
        }
        else
        {
            int height = Math.Clamp(f.Coverage * 16 / 100, 3, 16);
            b.Box(1, N - 1, 1, 1 + height, 1, N - 1, c);
        }
    }

    /// <summary>Draws into one tile's voxels using tile-local coordinates, with per-voxel color jitter.</summary>
    private readonly struct Brush
    {
        private readonly VoxelVolume _v;
        private readonly int _ox, _oz;

        public Brush(VoxelVolume volume, int tx, int tz)
        {
            _v = volume;
            _ox = tx * N;
            _oz = tz * N;
        }

        /// <param name="holes">Fraction of voxels left out, in a stable pattern (smashed doors).</param>
        /// <param name="slot">Atlas slot to texture the box with; the color is then only used if there's no art.</param>
        public void Box(int x0, int x1, int y0, int y1, int z0, int z1, Color color, int slot = 0, float holes = 0)
        {
            for (int x = x0; x < x1; x++)
            for (int y = y0; y < y1; y++)
            for (int z = z0; z < z1; z++)
            {
                if (holes > 0 && (Hash(_ox + x, y, _oz + z) >> 8 & 0xFF) < holes * 255)
                    continue;
                if (slot != 0)
                    _v.Set(_ox + x, y, _oz + z, Textured, slot);
                else
                    _v.Set(_ox + x, y, _oz + z, Jitter(color, _ox + x, y, _oz + z));
            }
        }

        /// <summary>Textured voxels are white, so the sprite shows in its own colors.</summary>
        private static readonly uint Textured = VoxelVolume.Pack(Colors.White);

        public void Box(int x0, int x1, int y0, int y1, int z0, int z1, uint raw)
        {
            for (int x = x0; x < x1; x++)
            for (int y = y0; y < y1; y++)
            for (int z = z0; z < z1; z++)
                _v.Set(_ox + x, y, _oz + z, raw);
        }

        /// <summary>
        /// A box given as (along, up, across) the run of a wall. alongX swaps which tile axis
        /// "along" maps to, so the same call draws a door in an east-west or north-south wall.
        /// </summary>
        public void Thin(bool alongX, int a0, int a1, int y0, int y1, int c0, int c1, Color color, float holes = 0, int slot = 0)
        {
            if (alongX) Box(a0, a1, y0, y1, c0, c1, color, slot, holes);
            else Box(c0, c1, y0, y1, a0, a1, color, slot, holes);
        }

        public void Thin(bool alongX, int a0, int a1, int y0, int y1, int c0, int c1, uint raw)
        {
            if (alongX) Box(a0, a1, y0, y1, c0, c1, raw);
            else Box(c0, c1, y0, y1, a0, a1, raw);
        }

        /// <summary>An ellipsoid centered in tile-local voxel space, clipped to the tile.</summary>
        public void Blob(float cx, float cy, float cz, float radius, float halfHeight, Color color)
        {
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            for (int y = (int)(cy - halfHeight); y <= (int)(cy + halfHeight); y++)
            {
                float dx = (x + 0.5f - cx) / radius, dy = (y + 0.5f - cy) / halfHeight, dz = (z + 0.5f - cz) / radius;
                if (dx * dx + dy * dy + dz * dz <= 1f && y >= 1)
                    _v.Set(_ox + x, y, _oz + z, Jitter(color, _ox + x, y, _oz + z));
            }
        }

        private static uint Hash(int x, int y, int z)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return h;
        }

        private static uint Jitter(Color c, int x, int y, int z)
        {
            float f = 0.9f + (Hash(x, y, z) & 0xFF) / 255f * 0.14f;
            return VoxelVolume.Pack(new Color(c.R * f, c.G * f, c.B * f, c.A));
        }
    }
}
