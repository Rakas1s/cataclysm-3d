using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// Renders a <see cref="TileGrid"/> as voxels, in square chunks of <see cref="ChunkTiles"/> tiles.
/// Chunks touched by tile changes (doors opening, etc.) are re-meshed on the next frame.
/// </summary>
public partial class WorldView : Node3D
{
    public const int ChunkTiles = 8;

    private readonly Dictionary<Vector2I, MeshInstance3D> _chunks = new();
    private readonly HashSet<Vector2I> _dirty = new();
    private TileGrid? _map;
    private VoxelVolume? _volume;

    private readonly StandardMaterial3D _opaque = new()
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        Roughness = 0.95f,
    };

    private readonly StandardMaterial3D _glass = new()
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.1f,
        Metallic = 0.2f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    public void Display(TileGrid map)
    {
        if (_map is not null)
            _map.TileChanged -= OnTileChanged;
        foreach (var chunk in _chunks.Values)
            chunk.QueueFree();
        _chunks.Clear();
        _dirty.Clear();

        _map = map;
        _volume = new VoxelVolume(map.Width, map.Height);
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            TileVoxelizer.Fill(_volume, map, x, y);

        for (int cy = 0; cy * ChunkTiles < map.Height; cy++)
        for (int cx = 0; cx * ChunkTiles < map.Width; cx++)
            _dirty.Add(new Vector2I(cx, cy));
        RebuildDirty();

        map.TileChanged += OnTileChanged;
    }

    private void OnTileChanged(int x, int y)
    {
        // Neighbors can change shape too (frames, rails connect to adjacent tiles).
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (!_map!.InBounds(nx, ny))
                continue;
            TileVoxelizer.Fill(_volume!, _map, nx, ny);
            _dirty.Add(new Vector2I(nx / ChunkTiles, ny / ChunkTiles));
        }
    }

    public override void _Process(double delta)
    {
        if (_dirty.Count > 0)
            RebuildDirty();
    }

    private void RebuildDirty()
    {
        foreach (var c in _dirty)
        {
            var min = new Vector3I(c.X * ChunkTiles * VoxelVolume.PerTile, 0, c.Y * ChunkTiles * VoxelVolume.PerTile);
            var max = new Vector3I(
                Math.Min(min.X + ChunkTiles * VoxelVolume.PerTile, _volume!.SizeX),
                VoxelVolume.Height,
                Math.Min(min.Z + ChunkTiles * VoxelVolume.PerTile, _volume.SizeZ));
            var mesh = VoxelMesher.Build(_volume, min, max, _opaque, _glass);

            if (!_chunks.TryGetValue(c, out var instance))
            {
                instance = new MeshInstance3D();
                AddChild(instance);
                _chunks[c] = instance;
            }
            instance.Mesh = mesh;
        }
        _dirty.Clear();
    }
}
