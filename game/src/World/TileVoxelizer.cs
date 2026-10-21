using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// Turns one tile's terrain + furniture into voxels. Shapes are chosen from CDDA flags
/// (WALL, WINDOW, TREE, SHRUB, THIN_OBSTACLE, …) with id keywords for furniture. These are
/// placeholder archetypes until the sprite→voxel pipeline (M3) produces per-id models. Each
/// z-level has its own volume; its floor sits at voxel y 0 and its walls reach the next level.
/// </summary>
public static class TileVoxelizer
{
    private const int N = VoxelVolume.PerTile;

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

        b.Box(0, N, 0, 1, 0, N, TilePalette.For(ter));
        if (ter.Has("GOES_UP") && !ter.Has("LIQUID"))
            DrawWayUp(b, map, ter, tx, ty);
        else
            DrawTerrain(b, map, ter, tx, ty);
        if (map.FurnitureAt(tx, ty) is { } fur)
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
        var color = TilePalette.For(t);
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

    /// <summary>Color of a neighboring wall, so window sills and door frames match it.</summary>
    private static Color NeighborWallColor(TileGrid map, int tx, int ty)
    {
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            if (map.InBounds(tx + dx, ty + dy) && map.TerrainAt(tx + dx, ty + dy) is { } n && n.Has("WALL"))
                return TilePalette.For(n);
        return TilePalette.FromCddaName("light_gray");
    }

    private static void DrawTerrain(Brush b, TileGrid map, TerrainType t, int tx, int ty)
    {
        var color = TilePalette.For(t);
        string id = t.Id;

        if (t.Has("WALL"))
        {
            b.Box(0, N, 1, WallTop, 0, N, color);
        }
        else if (t.Has("WINDOW"))
        {
            var wall = NeighborWallColor(map, tx, ty);
            b.Box(0, N, 1, 8, 0, N, wall);
            b.Box(0, N, 19, WallTop, 0, N, wall);
            bool alongX = RunsAlongX(map, tx, ty);
            bool open = t.MoveCost > 0;
            var pane = t.IsTransparent ? TilePalette.Glass : TilePalette.For(t); // curtains drawn shut
            b.Thin(alongX, 0, N, 8, 19, 3, 5, TilePalette.FromCddaName("white")); // frame
            b.Thin(alongX, 1, N - 1, 9, 18, 3, 5, 0u);
            if (!open)
                b.Thin(alongX, 1, N - 1, 9, 18, 4, 5, pane);
        }
        else if (IsDoor(t))
        {
            var wall = NeighborWallColor(map, tx, ty);
            bool alongX = RunsAlongX(map, tx, ty);
            b.Box(0, N, 18, WallTop, 0, N, wall); // lintel
            b.Thin(alongX, 0, 1, 1, 18, 0, N, wall); // posts
            b.Thin(alongX, N - 1, N, 1, 18, 0, N, wall);
            if (t.MoveCost == 0 && id.EndsWith("_b", StringComparison.Ordinal))
                b.Thin(alongX, 1, N - 1, 1, 18, 3, 5, color, holes: 0.45f); // smashed panel
            else if (t.MoveCost == 0)
                b.Thin(alongX, 1, N - 1, 1, 18, 3, 5, color); // closed panel in the doorway
            else
                b.Thin(alongX, 1, 2, 1, 18, 4, N, color); // swung open against the frame
        }
        else if (t.Has("TREE"))
        {
            b.Box(3, 5, 1, 22, 3, 5, TilePalette.Bark);
            b.Blob(4, 26, 4, 4.5f, 7f, TilePalette.Leaves);
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
        var c = TilePalette.For(f);
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
        public void Box(int x0, int x1, int y0, int y1, int z0, int z1, Color color, float holes = 0)
        {
            for (int x = x0; x < x1; x++)
            for (int y = y0; y < y1; y++)
            for (int z = z0; z < z1; z++)
            {
                if (holes > 0 && (Hash(_ox + x, y, _oz + z) >> 8 & 0xFF) < holes * 255)
                    continue;
                _v.Set(_ox + x, y, _oz + z, Jitter(color, _ox + x, y, _oz + z));
            }
        }

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
        public void Thin(bool alongX, int a0, int a1, int y0, int y1, int c0, int c1, Color color, float holes = 0)
        {
            if (alongX) Box(a0, a1, y0, y1, c0, c1, color, holes);
            else Box(c0, c1, y0, y1, a0, a1, color, holes);
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
