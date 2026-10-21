using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// Dense voxel grid over a tile map. Each voxel is a packed RGBA8 color; 0 is empty and
/// alpha below 255 marks translucent voxels (glass). x/z follow tile x/y, y is up.
/// </summary>
public sealed class VoxelVolume
{
    /// <summary>Voxels per tile edge; a tile is one meter, so a voxel is 12.5 cm.</summary>
    public const int PerTile = 8;

    /// <summary>Volume height in voxels (4.5 m): one 3 m storey plus room for tree canopies.</summary>
    public const int Height = 36;

    public const float VoxelSize = 1f / PerTile;

    private readonly uint[] _voxels;

    public int SizeX { get; }
    public int SizeZ { get; }

    public VoxelVolume(int tilesX, int tilesZ)
    {
        SizeX = tilesX * PerTile;
        SizeZ = tilesZ * PerTile;
        _voxels = new uint[SizeX * SizeZ * Height];
    }

    private int Index(int x, int y, int z) => (z * SizeX + x) * Height + y;

    public bool InBounds(int x, int y, int z) =>
        x >= 0 && z >= 0 && y >= 0 && x < SizeX && z < SizeZ && y < Height;

    public uint Get(int x, int y, int z) => InBounds(x, y, z) ? _voxels[Index(x, y, z)] : 0;

    public void Set(int x, int y, int z, uint rgba)
    {
        if (InBounds(x, y, z))
            _voxels[Index(x, y, z)] = rgba;
    }

    public void ClearTile(int tx, int tz)
    {
        for (int x = tx * PerTile; x < (tx + 1) * PerTile; x++)
        for (int z = tz * PerTile; z < (tz + 1) * PerTile; z++)
            Array.Clear(_voxels, Index(x, 0, z), Height);
    }

    public static bool IsTranslucent(uint rgba) => rgba != 0 && (rgba & 0xFF) < 0xFF;

    public static uint Pack(Color c) => c.ToRgba32();
}
