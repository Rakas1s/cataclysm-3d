using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// Renders a <see cref="WorldMap"/> as voxels: one layer per z-level, stacked
/// <see cref="LevelHeight"/> apart, each meshed in square chunks of
/// <see cref="VoxelVolume.ChunkTiles"/> tiles. Chunks touched by tile changes (doors opening,
/// etc.) are re-meshed on the next frame.
/// </summary>
public partial class WorldView : Node3D
{
    /// <summary>World-space height of one z-level (3 m).</summary>
    public const float LevelHeight = VoxelVolume.StoreyHeight * VoxelVolume.VoxelSize;

    /// <summary>Chunks this close to the player (in chunks) have their upper levels hidden when indoors.</summary>
    private const int CutawayRadius = 2;

    private const int ChunkTiles = VoxelVolume.ChunkTiles;

    private readonly Dictionary<Vector3I, MeshInstance3D> _chunks = new();
    private readonly HashSet<Vector3I> _dirty = new();
    private readonly Dictionary<int, Node3D> _levelNodes = new();
    private readonly Dictionary<int, VoxelVolume> _volumes = new();
    private WorldMap? _map;
    private (int Z, Vector2I Chunk, bool Indoors)? _cutaway;

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

    public static float LevelBase(int z) => z * LevelHeight;

    public void Display(WorldMap map)
    {
        if (_map is not null)
            _map.TileChanged -= OnTileChanged;
        foreach (var node in _levelNodes.Values)
            node.QueueFree();
        _levelNodes.Clear();
        _volumes.Clear();
        _chunks.Clear();
        _dirty.Clear();
        _cutaway = null;

        _map = map;
        for (int z = map.MinZ; z <= map.MaxZ; z++)
        {
            var node = new Node3D { Name = $"Level{z}", Position = new Vector3(0, LevelBase(z), 0) };
            AddChild(node);
            _levelNodes[z] = node;
            var volume = new VoxelVolume(map.Width, map.Height);
            _volumes[z] = volume;
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                TileVoxelizer.Fill(volume, map, x, y, z);
            for (int cy = 0; cy * ChunkTiles < map.Height; cy++)
            for (int cx = 0; cx * ChunkTiles < map.Width; cx++)
                _dirty.Add(new Vector3I(cx, cy, z));
        }
        RebuildDirty();

        map.TileChanged += OnTileChanged;
    }

    private void OnTileChanged(int x, int y, int z)
    {
        // Neighbors can change shape too (frames, rails connect to adjacent tiles), and the level
        // below draws its ceiling from what's above it.
        for (int dz = -1; dz <= 0; dz++)
        {
            if (!_map!.HasLevel(z + dz))
                continue;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (!_map.Level(z + dz).InBounds(nx, ny))
                    continue;
                TileVoxelizer.Fill(_volumes[z + dz], _map, nx, ny, z + dz);
                _dirty.Add(new Vector3I(nx / ChunkTiles, ny / ChunkTiles, z + dz));
            }
        }
    }

    /// <summary>
    /// Hides the levels above the player, around them, while they're indoors, so roofs and upper
    /// floors stop shading and cluttering the interior. Each level draws its own ceiling, so rooms
    /// still look closed in.
    /// </summary>
    public void SetCutaway(TilePos player, bool indoors)
    {
        var state = (player.Z, new Vector2I(player.X / ChunkTiles, player.Y / ChunkTiles), indoors);
        if (_cutaway == state)
            return;
        _cutaway = state;
        foreach (var (key, chunk) in _chunks)
            chunk.Visible = IsChunkVisible(key);
    }

    private bool IsChunkVisible(Vector3I key)
    {
        if (_cutaway is not { } c || !c.Indoors || key.Z <= c.Z)
            return true;
        return Math.Max(Math.Abs(key.X - c.Chunk.X), Math.Abs(key.Y - c.Chunk.Y)) > CutawayRadius;
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
            var volume = _volumes[c.Z];
            var min = new Vector3I(c.X * ChunkTiles * VoxelVolume.PerTile, 0, c.Y * ChunkTiles * VoxelVolume.PerTile);
            var max = new Vector3I(
                Math.Min(min.X + ChunkTiles * VoxelVolume.PerTile, volume.SizeX),
                VoxelVolume.Height,
                Math.Min(min.Z + ChunkTiles * VoxelVolume.PerTile, volume.SizeZ));
            var mesh = VoxelMesher.Build(volume, min, max, _opaque, _glass);

            if (!_chunks.TryGetValue(c, out var instance))
            {
                if (mesh is null)
                    continue;
                instance = new MeshInstance3D { Visible = IsChunkVisible(c) };
                _levelNodes[c.Z].AddChild(instance);
                _chunks[c] = instance;
            }
            instance.Mesh = mesh;
        }
        _dirty.Clear();
    }
}
