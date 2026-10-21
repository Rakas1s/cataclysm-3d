using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// Voxel grid over one z-level of a tile map, stored in square columns of
/// <see cref="ChunkTiles"/> × <see cref="ChunkTiles"/> tiles that are only allocated once
/// something is drawn in them (open air and buried soil cost nothing). Each voxel is a packed
/// RGBA8 color; 0 is empty and alpha below 255 marks translucent voxels (glass). A voxel can also
/// carry a <see cref="SurfaceAtlas"/> slot, whose sprite is then drawn across its faces (tinted by
/// the color). x/z follow tile x/y, y is up.
/// </summary>
public sealed class VoxelVolume
{
    /// <summary>Voxels per tile edge; a tile is one meter, so a voxel is 12.5 cm.</summary>
    public const int PerTile = 8;

    /// <summary>One storey (3 m); the next z-level's floor starts here.</summary>
    public const int StoreyHeight = 24;

    /// <summary>Volume height in voxels (4.5 m): one storey plus room for tree canopies.</summary>
    public const int Height = 36;

    /// <summary>Tiles per chunk edge; also the unit the renderer meshes in.</summary>
    public const int ChunkTiles = 8;

    public const float VoxelSize = 1f / PerTile;

    private const int ChunkVoxels = ChunkTiles * PerTile;

    private readonly uint[]?[] _chunks;
    private readonly ushort[]?[] _slots;
    private readonly int _chunksX;

    public int SizeX { get; }
    public int SizeZ { get; }

    public VoxelVolume(int tilesX, int tilesZ)
    {
        SizeX = tilesX * PerTile;
        SizeZ = tilesZ * PerTile;
        _chunksX = (SizeX + ChunkVoxels - 1) / ChunkVoxels;
        _chunks = new uint[]?[_chunksX * ((SizeZ + ChunkVoxels - 1) / ChunkVoxels)];
        _slots = new ushort[]?[_chunks.Length];
    }

    public bool InBounds(int x, int y, int z) =>
        x >= 0 && z >= 0 && y >= 0 && x < SizeX && z < SizeZ && y < Height;

    /// <summary>Whether nothing has been drawn in a chunk yet (chunk coordinates, not tiles).</summary>
    public bool IsChunkEmpty(int chunkX, int chunkZ) => _chunks[chunkZ * _chunksX + chunkX] is null;

    public uint Get(int x, int y, int z)
    {
        if (!InBounds(x, y, z))
            return 0;
        var chunk = _chunks[z / ChunkVoxels * _chunksX + x / ChunkVoxels];
        return chunk is null ? 0 : chunk[Index(x, y, z)];
    }

    /// <summary>The atlas slot drawn on a voxel's faces, or 0 for plain color.</summary>
    public int Slot(int x, int y, int z)
    {
        if (!InBounds(x, y, z))
            return 0;
        var slots = _slots[z / ChunkVoxels * _chunksX + x / ChunkVoxels];
        return slots is null ? 0 : slots[Index(x, y, z)];
    }

    /// <summary>
    /// Copies the whole column at (x, z), all <see cref="Height"/> voxels bottom up, into the
    /// spans; out-of-bounds or unallocated columns read as empty.
    /// </summary>
    public void ReadColumn(int x, int z, Span<uint> colors, Span<int> slots)
    {
        if (x < 0 || z < 0 || x >= SizeX || z >= SizeZ || _chunks[z / ChunkVoxels * _chunksX + x / ChunkVoxels] is not { } chunk)
        {
            colors[..Height].Clear();
            slots[..Height].Clear();
            return;
        }
        int i = Index(x, 0, z);
        chunk.AsSpan(i, Height).CopyTo(colors);
        var s = _slots[z / ChunkVoxels * _chunksX + x / ChunkVoxels];
        for (int y = 0; y < Height; y++)
            slots[y] = s is null ? 0 : s[i + y];
    }

    public void Set(int x, int y, int z, uint rgba, int slot = 0)
    {
        if (!InBounds(x, y, z))
            return;
        int c = z / ChunkVoxels * _chunksX + x / ChunkVoxels;
        ref var chunk = ref _chunks[c];
        if (chunk is null)
        {
            if (rgba == 0)
                return;
            chunk = new uint[ChunkVoxels * ChunkVoxels * Height];
        }
        int i = Index(x, y, z);
        chunk[i] = rgba;
        ref var slots = ref _slots[c];
        if (slot != 0 || slots is not null)
        {
            slots ??= new ushort[chunk.Length];
            slots[i] = (ushort)(rgba == 0 ? 0 : slot);
        }
    }

    private static int Index(int x, int y, int z) =>
        ((z % ChunkVoxels) * ChunkVoxels + x % ChunkVoxels) * Height + y;

    public void ClearTile(int tx, int tz)
    {
        var chunk = _chunks[tz / ChunkTiles * _chunksX + tx / ChunkTiles];
        if (chunk is null)
            return;
        var slots = _slots[tz / ChunkTiles * _chunksX + tx / ChunkTiles];
        for (int x = tx * PerTile; x < (tx + 1) * PerTile; x++)
        for (int z = tz * PerTile; z < (tz + 1) * PerTile; z++)
        {
            Array.Clear(chunk, Index(x, 0, z), Height);
            if (slots is not null)
                Array.Clear(slots, Index(x, 0, z), Height);
        }
    }

    public static bool IsTranslucent(uint rgba) => rgba != 0 && (rgba & 0xFF) < 0xFF;

    public static uint Pack(Color c) => c.ToRgba32();
}
