using Cataclysm3D.Importer.Tiles;
using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// Turns furniture sprites into voxel meshes at the sprite's own resolution (one voxel per pixel,
/// 32 per meter). CDDA sprites are drawn top-down with a little of the front showing, so how a
/// sprite becomes 3D depends on what it is (<see cref="Shape"/>):
/// <list type="bullet">
/// <item><b>Front</b> (fridges, bookcases, ovens): the sprite is the object's front, extruded back
/// to a fixed depth; a band at the top of the sprite, if there's a clear edge, becomes its lid.</item>
/// <item><b>Top</b> (tables, beds, counters): the sprite is the top surface, raised to the object's
/// height; sparse rows at the bottom of the sprite (legs) become legs at the front and back.</item>
/// <item><b>Upright</b> (chairs, toilets, lamps, plants): the silhouette stands up and is given a
/// rounded depth, thick where the sprite is wide and thin at its edges. Seats are extruded
/// straight back instead, so they keep a flat seat and back.</item>
/// </list>
/// Models are cached per sprite and shape, so each is built once per run.
/// </summary>
public sealed class FurnitureModels
{
    public enum Kind { Front, Top, Upright }

    /// <param name="Height">Meters; 0 keeps the sprite's own height (32 px = 1 m).</param>
    /// <param name="Depth">Meters front to back (Front only).</param>
    /// <param name="Legs">Whether sparse rows at the bottom of a Top sprite are legs.</param>
    /// <param name="Flat">Upright only: extrude the silhouette straight back instead of rounding it (seats).</param>
    public readonly record struct Shape(Kind Kind, float Height, float Depth = 0.6f, bool Legs = false, bool Flat = false);

    private const int Px = 32;

    private readonly TileArt _art;
    private readonly Dictionary<(Tileset, int, int, Shape), ArrayMesh?> _cache = new();

    private readonly StandardMaterial3D _material = new()
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        Roughness = 0.9f,
    };

    public FurnitureModels(TileArt art) => _art = art;

    public int Count => _cache.Count;

    private static readonly (string Key, Shape Shape)[] Shapes =
    {
        // Tall things with a front.
        ("fridge", new(Kind.Front, 1.8f, 0.7f)),
        ("freezer", new(Kind.Front, 1.8f, 0.7f)),
        ("bookcase", new(Kind.Front, 1.9f, 0.4f)),
        ("wardrobe", new(Kind.Front, 1.9f, 0.6f)),
        ("locker", new(Kind.Front, 1.8f, 0.5f)),
        ("vending", new(Kind.Front, 1.9f, 0.8f)),
        ("gunsafe", new(Kind.Front, 1.5f, 0.6f)),
        ("filing_cabinet", new(Kind.Front, 1.3f, 0.6f)),
        ("cabinet", new(Kind.Front, 1.1f, 0.5f)),
        ("dresser", new(Kind.Front, 1.1f, 0.5f)),
        ("piano", new(Kind.Front, 1.2f, 0.6f)),
        ("safe", new(Kind.Front, 1.0f, 0.6f)),
        ("displaycase", new(Kind.Front, 1.0f, 0.6f)),
        ("cupboard", new(Kind.Front, 0.9f, 0.6f)),
        ("oven", new(Kind.Front, 0.9f, 0.65f)),
        ("stove", new(Kind.Front, 0.9f, 0.65f)),
        ("dishwasher", new(Kind.Front, 0.9f, 0.6f)),
        ("washer", new(Kind.Front, 0.9f, 0.65f)),
        ("dryer", new(Kind.Front, 0.9f, 0.65f)),
        ("sink", new(Kind.Front, 0.9f, 0.6f)),
        ("woodstove", new(Kind.Front, 0.9f, 0.6f)),
        ("fireplace", new(Kind.Front, 1.1f, 0.6f)),
        ("tv", new(Kind.Front, 0.9f, 0.4f)),
        ("server", new(Kind.Front, 1.9f, 0.8f)),
        // Seats and small things (before "table" so "f_table_chair"-like ids stay chairs).
        ("armchair", new(Kind.Upright, 0f, Flat: true)),
        ("chair", new(Kind.Upright, 0f, Flat: true)),
        ("stool", new(Kind.Upright, 0f, Flat: true)),
        ("toilet", new(Kind.Upright, 0f)),
        ("lamp", new(Kind.Upright, 1.5f)),
        // Flat tops.
        ("coffee_table", new(Kind.Top, 0.45f, Legs: true)),
        ("table", new(Kind.Top, 0.75f, Legs: true)),
        ("desk", new(Kind.Top, 0.75f)),
        ("counter", new(Kind.Top, 0.9f)),
        ("bed", new(Kind.Top, 0.55f)),
        ("mattress", new(Kind.Top, 0.25f)),
        ("sofa", new(Kind.Top, 0.8f)),
        ("couch", new(Kind.Top, 0.8f)),
        ("bathtub", new(Kind.Top, 0.55f)),
        ("bench", new(Kind.Top, 0.45f, Legs: true)),
        ("pool_table", new(Kind.Top, 0.8f)),
        ("rug", new(Kind.Top, 0.02f)),
        ("carpet", new(Kind.Top, 0.02f)),
    };

    /// <summary>How a piece of furniture's sprite becomes 3D, by id keyword, else by its sprite and coverage.</summary>
    public static Shape ShapeFor(FurnitureType f, SpritePixels sprite)
    {
        foreach (var (key, shape) in Shapes)
            if (f.Id.Contains(key, StringComparison.Ordinal))
                return shape;
        if (sprite.Bounds() is not { } b)
            return new Shape(Kind.Upright, 0f);
        int w = b.X1 - b.X0, h = b.Y1 - b.Y0;
        // Something that fills its tile is a block; anything smaller stands up as a silhouette.
        if (w >= Px * 0.85f && h >= Px * 0.85f && sprite.Height <= Px)
            return new Shape(Kind.Top, Math.Clamp(f.Coverage / 100f * 1.4f, 0.1f, 1.4f));
        return new Shape(Kind.Upright, 0f);
    }

    /// <summary>The model for a piece of furniture's chosen sprite, or null when the sprite is empty.</summary>
    public ArrayMesh? For(FurnitureType f, SpriteChoice choice)
    {
        var sprite = _art.Pixels(choice.Tileset, choice.Fg, choice.Rotation);
        if (sprite is null)
            return null;
        var shape = ShapeFor(f, sprite);
        var key = (choice.Tileset, choice.Fg, choice.Rotation, shape);
        if (_cache.TryGetValue(key, out var mesh))
            return mesh;
        var grid = shape.Kind switch
        {
            Kind.Front => BuildFront(sprite, shape),
            Kind.Top => BuildTop(sprite, shape),
            _ => BuildUpright(sprite, shape),
        };
        mesh = grid?.Mesh(_material);
        _cache[key] = mesh;
        return mesh;
    }

    /// <summary>Dense voxel grid in pixel units; (0, 0, 0) is the tile's north-west corner at floor level.</summary>
    private sealed class Grid
    {
        public readonly int X0, Z0, SizeX, SizeY, SizeZ;
        private readonly Color[] _c;
        private readonly bool[] _on;

        public Grid(int x0, int z0, int sx, int sy, int sz)
        {
            (X0, Z0, SizeX, SizeY, SizeZ) = (x0, z0, Math.Max(1, sx), Math.Max(1, sy), Math.Max(1, sz));
            _c = new Color[SizeX * SizeY * SizeZ];
            _on = new bool[_c.Length];
        }

        private int I(int x, int y, int z) => (x * SizeY + y) * SizeZ + z;
        private bool In(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < SizeX && y < SizeY && z < SizeZ;

        /// <summary>Grid-local coordinates.</summary>
        public void Set(int x, int y, int z, Color c)
        {
            if (!In(x, y, z))
                return;
            _c[I(x, y, z)] = c;
            _on[I(x, y, z)] = true;
        }

        public bool On(int x, int y, int z) => In(x, y, z) && _on[I(x, y, z)];

        public bool Empty => !_on.Any(b => b);

        public ArrayMesh? Mesh(Material material)
        {
            if (Empty)
                return null;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var indices = new List<int>();
            const float s = 1f / Px;
            for (int x = 0; x < SizeX; x++)
            for (int y = 0; y < SizeY; y++)
            for (int z = 0; z < SizeZ; z++)
            {
                if (!_on[I(x, y, z)])
                    continue;
                var color = _c[I(x, y, z)];
                var origin = new Vector3(X0 + x, y, Z0 + z);
                foreach (var (dir, normal, corners) in VoxelMesher.Faces)
                {
                    if (On(x + dir.X, y + dir.Y, z + dir.Z))
                        continue;
                    // Faces pointing up catch more light in the sprites' own shading; keep the
                    // sides a little darker so edges read.
                    var shade = dir.Y > 0 ? color : dir.Y < 0 ? color.Darkened(0.3f) : color.Darkened(0.06f);
                    int start = verts.Count;
                    foreach (var c in corners)
                    {
                        verts.Add((origin + c) * s);
                        normals.Add(normal);
                        colors.Add(shade);
                    }
                    indices.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
                }
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Normal] = normals.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Color] = colors.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Index] = indices.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(0, material);
            return mesh;
        }
    }

    /// <summary>
    /// Color for a voxel that isn't on the sprite's face (sides, insides): the pixel a little in
    /// from the sprite's edge, since sprites outline themselves in near-black.
    /// </summary>
    private static Color Inner(SpritePixels s, (int X0, int Y0, int X1, int Y1) b, int x, int y)
    {
        int ix = Math.Clamp(x, Math.Min(b.X0 + 2, b.X1 - 1), Math.Max(b.X1 - 3, b.X0));
        int iy = Math.Clamp(y, Math.Min(b.Y0 + 2, b.Y1 - 1), Math.Max(b.Y1 - 3, b.Y0));
        return s.Opaque(ix, iy) ? s.At(ix, iy) : s.Opaque(ix, y) ? s.At(ix, y) : s.At(x, y);
    }

    private static float Luma(Color c) => 0.3f * c.R + 0.59f * c.G + 0.11f * c.B;

    /// <summary>
    /// Splits a front-facing sprite into its lid (rows [Lid0, Lid1)) and its front (rows from
    /// Front down): the strongest horizontal color edge in the top part of the sprite. Near-black
    /// outline rows are skipped, so the edge is between real surfaces. No clear edge means no lid.
    /// </summary>
    private static (int Lid0, int Lid1, int Front) LidSplit(SpritePixels s, (int X0, int Y0, int X1, int Y1) b, float maxFraction)
    {
        int x0 = Math.Min(b.X0 + 1, b.X1 - 1), x1 = Math.Max(b.X1 - 1, x0 + 1);
        float RowLuma(int r)
        {
            float sum = 0;
            int n = 0;
            for (int x = x0; x < x1; x++)
                if (s.Opaque(x, r))
                {
                    sum += Luma(s.At(x, r));
                    n++;
                }
            return n < (x1 - x0) / 2 ? -1 : sum / n;
        }
        bool Dark(int r) => RowLuma(r) < 0.14f;

        int lid0 = b.Y0;
        while (lid0 < b.Y1 - 1 && Dark(lid0))
            lid0++;
        int last = b.Y0 + (int)((b.Y1 - b.Y0) * maxFraction);
        var best = (Lid0: b.Y0, Lid1: b.Y0, Front: b.Y0);
        float bestScore = 0.07f;
        int prev = lid0;
        for (int r = lid0 + 1; r <= last; r++)
        {
            if (Dark(r))
                continue;
            float score = 0;
            int n = 0;
            for (int x = x0; x < x1; x++)
            {
                if (!s.Opaque(x, r) || !s.Opaque(x, prev))
                    continue;
                score += Math.Abs(Luma(s.At(x, r)) - Luma(s.At(x, prev)));
                n++;
            }
            if (n > 0 && r - lid0 >= 2 && score / n > bestScore)
            {
                bestScore = score / n;
                best = (lid0, prev + 1, prev + 1);
            }
            prev = r;
        }
        return best;
    }

    private static Grid? BuildFront(SpritePixels s, Shape shape)
    {
        if (s.Bounds() is not { } b)
            return null;
        var (lid0, lid1, split) = LidSplit(s, b, 0.35f);
        int frontRows = b.Y1 - split;
        int lidRows = lid1 - lid0;
        int height = shape.Height > 0 ? (int)MathF.Round(shape.Height * Px) : frontRows;
        int depth = Math.Clamp((int)MathF.Round(shape.Depth * Px), 2, Px);
        var g = new Grid(s.OffsetX + b.X0, 0, b.X1 - b.X0, height, depth);
        for (int x = 0; x < g.SizeX; x++)
        {
            int sx = b.X0 + x;
            int top = -1;
            for (int y = 0; y < height; y++)
            {
                int row = b.Y1 - 1 - y * frontRows / height;
                if (!s.Opaque(sx, row))
                    continue;
                var c = s.At(sx, row);
                var side = Inner(s, b, sx, row).Darkened(0.1f);
                for (int z = 0; z < depth; z++)
                    g.Set(x, y, z, z == depth - 1 ? c : side);
                top = y;
            }
            if (top < 0)
                continue;
            // Lid: the band above the split, its bottom row along the front edge. Without one, the
            // top takes the front's upper color.
            for (int z = 0; z < depth; z++)
            {
                var c = lidRows > 0
                    ? s.At(sx, lid0 + (lidRows - 1) * z / Math.Max(1, depth - 1))
                    : Inner(s, b, sx, b.Y0).Lightened(0.08f);
                if (c.A > 0.5f)
                    g.Set(x, top, z, c);
            }
        }
        return g;
    }

    private static Grid? BuildTop(SpritePixels s, Shape shape)
    {
        if (s.Bounds() is not { } b)
            return null;
        int width = b.X1 - b.X0;
        float Fill(int row)
        {
            int n = 0;
            for (int x = b.X0; x < b.X1; x++)
                if (s.Opaque(x, row))
                    n++;
            return (float)n / width;
        }
        // Rows at the bottom that are mostly see-through are legs (or a front skirt) drawn below the top.
        int legStart = b.Y1;
        if (shape.Legs)
            while (legStart - 1 > b.Y0 + (b.Y1 - b.Y0) / 2 && Fill(legStart - 1) < 0.7f)
                legStart--;
        int bodyRows = legStart - b.Y0;
        bool legs = b.Y1 - legStart >= 2;

        int height = Math.Max(1, (int)MathF.Round(shape.Height * Px));
        int slab = legs ? Math.Max(2, height / 6) : height;
        int z0 = Math.Max(0, s.OffsetY + b.Y0);
        int depth = Math.Max(1, Px - z0);
        var g = new Grid(s.OffsetX + b.X0, z0, width, height, depth);
        for (int x = 0; x < width; x++)
        {
            int sx = b.X0 + x;
            for (int z = 0; z < depth; z++)
            {
                int row = b.Y0 + z * bodyRows / depth;
                if (!s.Opaque(sx, row))
                    continue;
                var c = s.At(sx, row);
                var side = Inner(s, (b.X0, b.Y0, b.X1, legStart), sx, row).Darkened(0.12f);
                for (int y = height - slab; y < height; y++)
                    g.Set(x, y, z, y == height - 1 ? c : side);
            }
            if (!legs || !s.Opaque(sx, b.Y1 - 1))
                continue;
            // Legs stand at the front and back edge, under the columns the sprite draws them in.
            var leg = s.At(sx, b.Y1 - 1);
            for (int y = 0; y < height - slab; y++)
            for (int z = 0; z < 3; z++)
            {
                g.Set(x, y, depth - 1 - z, leg);
                g.Set(x, y, z, leg);
            }
        }
        return g;
    }

    private static Grid? BuildUpright(SpritePixels s, Shape shape)
    {
        if (s.Bounds() is not { } b)
            return null;
        int rows = b.Y1 - b.Y0;
        int width = b.X1 - b.X0;
        int height = shape.Height > 0 ? (int)MathF.Round(shape.Height * Px) : rows;
        int maxHalf = Math.Clamp(width / 2, 2, Px / 2 - 1);
        var g = new Grid(s.OffsetX + b.X0, Px / 2 - maxHalf, width, height, maxHalf * 2);
        for (int y = 0; y < height; y++)
        {
            int row = b.Y1 - 1 - y * rows / height;
            for (int x = 0; x < width; x++)
            {
                int sx = b.X0 + x;
                if (!s.Opaque(sx, row))
                    continue;
                // Thickness grows with the distance to the silhouette's edge on this row, so
                // round things come out round and thin parts (legs, poles) stay thin.
                int edge = 1;
                while (edge < maxHalf && s.Opaque(sx - edge, row) && s.Opaque(sx + edge, row))
                    edge++;
                int half = shape.Flat ? maxHalf : Math.Clamp(edge + 1, 1, maxHalf);
                var c = s.At(sx, row);
                // Sides take the color a couple of pixels in from the outline, toward the middle.
                int inward = sx + (sx < (b.X0 + b.X1) / 2 ? 2 : -2);
                var side = (s.Opaque(inward, row) ? s.At(inward, row) : c).Darkened(0.08f);
                for (int z = maxHalf - half; z < maxHalf + half; z++)
                    g.Set(x, y, z, z == maxHalf + half - 1 ? c : side);
            }
        }
        return g;
    }
}
