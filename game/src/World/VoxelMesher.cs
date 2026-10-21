using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// Builds a face-culled mesh for a box of voxels. Opaque and translucent voxels go to separate
/// surfaces so glass can use a transparent material. Voxels with an atlas slot show that sprite
/// across their faces, one sprite per tile-sized (1 m) square, so floors read as the tileset's
/// floor and walls repeat the wall sprite every meter; their faces are merged into one quad per
/// such square. Plain-colored voxels each get their own faces (their colors are jittered).
/// </summary>
public static class VoxelMesher
{
    internal static readonly (Vector3I Dir, Vector3 Normal, Vector3[] Corners)[] Faces =
    {
        (new(1, 0, 0), Vector3.Right, new Vector3[] { new(1, 0, 0), new(1, 1, 0), new(1, 1, 1), new(1, 0, 1) }),
        (new(-1, 0, 0), Vector3.Left, new Vector3[] { new(0, 0, 1), new(0, 1, 1), new(0, 1, 0), new(0, 0, 0) }),
        (new(0, 1, 0), Vector3.Up, new Vector3[] { new(0, 1, 1), new(1, 1, 1), new(1, 1, 0), new(0, 1, 0) }),
        (new(0, -1, 0), Vector3.Down, new Vector3[] { new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1) }),
        (new(0, 0, 1), Vector3.Back, new Vector3[] { new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1) }),
        (new(0, 0, -1), Vector3.Forward, new Vector3[] { new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0) }),
    };

    private const int N = VoxelVolume.PerTile;

    /// <summary>Middle of atlas slot 0, which is white: plain-colored voxels sample it.</summary>
    private static readonly Vector2 WhiteUv = new Vector2(SurfaceAtlas.SlotSize / 2f, SurfaceAtlas.SlotSize / 2f) / SurfaceAtlas.Size;

    private sealed class Surface
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Color> Colors = new();
        public readonly List<Vector2> Uvs = new();
        public readonly List<int> Indices = new();

        /// <summary>A quad over [origin, origin + extent) in voxels, using a unit face's corners for winding.</summary>
        public void AddQuad(Vector3I origin, Vector3I extent, Vector3I dir, Vector3 normal, Vector3[] corners, Color color, int slot)
        {
            int start = Vertices.Count;
            foreach (var c in corners)
            {
                var p = new Vector3(origin.X + c.X * extent.X, origin.Y + c.Y * extent.Y, origin.Z + c.Z * extent.Z);
                Vertices.Add(p * VoxelVolume.VoxelSize);
                Normals.Add(normal);
                Colors.Add(color);
                Uvs.Add(slot == 0 ? WhiteUv : AtlasUv(slot, dir, origin, p));
            }
            // Corners are counter-clockwise seen from outside; Godot's front faces are clockwise.
            Indices.Add(start);
            Indices.Add(start + 2);
            Indices.Add(start + 1);
            Indices.Add(start);
            Indices.Add(start + 3);
            Indices.Add(start + 2);
        }

        public void AppendTo(ArrayMesh mesh, Material material)
        {
            if (Vertices.Count == 0)
                return;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = Vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = Normals.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = Colors.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = Uvs.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = Indices.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
    }

    /// <summary>Start of the 1 m square a coordinate is in.</summary>
    private static int Cell(int c) => c - c % N;

    /// <summary>
    /// Start of the 1 m band a height is in. Bands count up from the top of the floor (y 1), so a
    /// wall shows whole sprites between floor and ceiling.
    /// </summary>
    private static int Band(int y) => y - (y - 1 + N) % N;

    /// <summary>
    /// Atlas UV of a point on a textured face whose quad starts at voxel <paramref name="origin"/>
    /// (merged quads never cross a 1 m square, so the origin says which square).
    /// </summary>
    private static Vector2 AtlasUv(int slot, Vector3I dir, Vector3I origin, Vector3 p)
    {
        float u, v;
        if (dir.Y != 0)
        {
            (u, v) = (p.X - Cell(origin.X), p.Z - Cell(origin.Z));
        }
        else
        {
            float up = p.Y - Band(origin.Y);
            float across = dir.X != 0 ? p.Z - Cell(origin.Z) : p.X - Cell(origin.X);
            // Mirror on two of the four sides so every wall face reads left to right from outside.
            u = dir.X > 0 || dir.Z < 0 ? N - across : across;
            v = N - up;
        }
        const float px = (float)SurfaceAtlas.SlotSize / N;
        const float inset = 0.02f;
        var o = SurfaceAtlas.Origin(slot);
        return new Vector2(
            o.X + Math.Clamp(u * px, inset, SurfaceAtlas.SlotSize - inset),
            o.Y + Math.Clamp(v * px, inset, SurfaceAtlas.SlotSize - inset)) / SurfaceAtlas.Size;
    }

    /// <summary>Meshes voxels in [min, max), one chunk at most, and returns null when it's empty.</summary>
    public static ArrayMesh? Build(VoxelVolume volume, Vector3I min, Vector3I max, Material opaque, Material translucent)
    {
        if (volume.IsChunkEmpty(min.X / (VoxelVolume.ChunkTiles * VoxelVolume.PerTile), min.Z / (VoxelVolume.ChunkTiles * VoxelVolume.PerTile)))
            return null;
        var solid = new Surface();
        var glass = new Surface();
        var size = max - min;

        // A local copy with a one-voxel border, so the passes below don't go through chunk lookups.
        // Columns are copied whole (y 0..Height), plus one empty voxel above and below.
        int px = size.X + 2, py = VoxelVolume.Height + 2, pz = size.Z + 2;
        var vox = new uint[px * py * pz];
        var slots = new int[vox.Length];
        for (int x = -1; x <= size.X; x++)
        for (int z = -1; z <= size.Z; z++)
        {
            int at = ((x + 1) * pz + (z + 1)) * py + 1;
            volume.ReadColumn(min.X + x, min.Z + z, vox.AsSpan(at, VoxelVolume.Height), slots.AsSpan(at, VoxelVolume.Height));
        }
        // Index of local voxel (x, y, z) is origin + x*sx + y + z*sz.
        int sx = pz * py, sz = py, origin0 = sx + sz + 1 + min.Y;
        int[] stride = { sx, 1, sz };

        foreach (var (dir, normal, corners) in Faces)
        {
            // Slice the box across the face's normal; u and v run along the face.
            int n = dir.X != 0 ? 0 : dir.Y != 0 ? 1 : 2;
            int ua = n == 0 ? 2 : 0, va = n == 1 ? 2 : 1;
            int su = size[ua], sv = size[va];
            var color = new uint[su * sv];
            var slot = new int[su * sv];
            int neighbor = dir.X * sx + dir.Y + dir.Z * sz;
            var cellU = new int[su];
            var cellV = new int[sv];
            for (int u = 0; u < su; u++)
                cellU[u] = Cell(min[ua] + u);
            for (int v = 0; v < sv; v++)
                cellV[v] = va == 1 ? Band(min[va] + v) : Cell(min[va] + v);

            for (int s = 0; s < size[n]; s++)
            {
                for (int v = 0; v < sv; v++)
                for (int u = 0; u < su; u++)
                {
                    int at = origin0 + s * stride[n] + u * stride[ua] + v * stride[va];
                    uint c = vox[at];
                    int i = v * su + u;
                    color[i] = 0;
                    if (c == 0)
                        continue;
                    uint nb = vox[at + neighbor];
                    // Opaque faces show against empty or glass; glass faces only against empty.
                    bool isGlass = VoxelVolume.IsTranslucent(c);
                    if (isGlass ? nb != 0 : nb != 0 && !VoxelVolume.IsTranslucent(nb))
                        continue;
                    color[i] = c;
                    slot[i] = slots[at];
                }

                // Greedy merge of textured faces within each 1 m square; plain faces stay single.
                for (int v = 0; v < sv; v++)
                for (int u = 0; u < su; u++)
                {
                    int i = v * su + u;
                    uint c = color[i];
                    if (c == 0)
                        continue;
                    int k = slot[i];
                    int w = 1, h = 1;
                    if (k != 0)
                    {
                        while (u + w < su && cellU[u + w] == cellU[u] && color[i + w] == c && slot[i + w] == k)
                            w++;
                        while (v + h < sv && cellV[v + h] == cellV[v])
                        {
                            bool row = true;
                            for (int x = u, j = (v + h) * su + u; x < u + w && row; x++, j++)
                                row = color[j] == c && slot[j] == k;
                            if (!row)
                                break;
                            h++;
                        }
                    }
                    for (int y = v; y < v + h; y++)
                    for (int x = u; x < u + w; x++)
                        color[y * su + x] = 0;

                    var origin = min;
                    origin[n] += s;
                    origin[ua] += u;
                    origin[va] += v;
                    var extent = Vector3I.One;
                    extent[ua] = w;
                    extent[va] = h;
                    (VoxelVolume.IsTranslucent(c) ? glass : solid).AddQuad(origin, extent, dir, normal, corners, new Color(c), k);
                }
            }
        }

        if (solid.Vertices.Count == 0 && glass.Vertices.Count == 0)
            return null;
        var mesh = new ArrayMesh();
        solid.AppendTo(mesh, opaque);
        glass.AppendTo(mesh, translucent);
        return mesh;
    }
}
