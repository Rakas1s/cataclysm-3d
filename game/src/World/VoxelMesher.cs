using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// Builds a face-culled mesh for a box of voxels. Opaque and translucent voxels go to separate
/// surfaces so glass can use a transparent material.
/// </summary>
public static class VoxelMesher
{
    private static readonly (Vector3I Dir, Vector3 Normal, Vector3[] Corners)[] Faces =
    {
        (new(1, 0, 0), Vector3.Right, new Vector3[] { new(1, 0, 0), new(1, 1, 0), new(1, 1, 1), new(1, 0, 1) }),
        (new(-1, 0, 0), Vector3.Left, new Vector3[] { new(0, 0, 1), new(0, 1, 1), new(0, 1, 0), new(0, 0, 0) }),
        (new(0, 1, 0), Vector3.Up, new Vector3[] { new(0, 1, 1), new(1, 1, 1), new(1, 1, 0), new(0, 1, 0) }),
        (new(0, -1, 0), Vector3.Down, new Vector3[] { new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1) }),
        (new(0, 0, 1), Vector3.Back, new Vector3[] { new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1) }),
        (new(0, 0, -1), Vector3.Forward, new Vector3[] { new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0) }),
    };

    private sealed class Surface
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Color> Colors = new();
        public readonly List<int> Indices = new();

        public void AddQuad(Vector3 origin, Vector3 normal, Vector3[] corners, Color color)
        {
            int start = Vertices.Count;
            foreach (var c in corners)
            {
                Vertices.Add(origin + c * VoxelVolume.VoxelSize);
                Normals.Add(normal);
                Colors.Add(color);
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
            arrays[(int)Mesh.ArrayType.Index] = Indices.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
    }

    /// <summary>Meshes voxels in [min, max), one chunk at most, and returns null when it's empty.</summary>
    public static ArrayMesh? Build(VoxelVolume volume, Vector3I min, Vector3I max, Material opaque, Material translucent)
    {
        if (volume.IsChunkEmpty(min.X / (VoxelVolume.ChunkTiles * VoxelVolume.PerTile), min.Z / (VoxelVolume.ChunkTiles * VoxelVolume.PerTile)))
            return null;
        var solid = new Surface();
        var glass = new Surface();

        for (int x = min.X; x < max.X; x++)
        for (int z = min.Z; z < max.Z; z++)
        for (int y = min.Y; y < max.Y; y++)
        {
            uint v = volume.Get(x, y, z);
            if (v == 0)
                continue;
            bool isGlass = VoxelVolume.IsTranslucent(v);
            var color = new Color(v);
            var origin = new Vector3(x, y, z) * VoxelVolume.VoxelSize;

            foreach (var (dir, normal, corners) in Faces)
            {
                uint n = volume.Get(x + dir.X, y + dir.Y, z + dir.Z);
                // Opaque faces show against empty or glass; glass faces only against empty.
                bool visible = isGlass ? n == 0 : n == 0 || VoxelVolume.IsTranslucent(n);
                if (visible)
                    (isGlass ? glass : solid).AddQuad(origin, normal, corners, color);
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
