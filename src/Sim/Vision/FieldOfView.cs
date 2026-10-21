using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Sim.Vision;

/// <summary>
/// What the player can see, and what they remember. The authority on sight for gameplay.
/// <para>
/// On the player's z-level, CDDA-style shadowcasting finds the tiles in line of sight; of those,
/// the ones lit to at least <see cref="LightLevels.AmbientLow"/> are visible, plus the tiles
/// right next to you, which you can make out by feel. Levels below are seen straight down through
/// open air and stairwells. Tiles once seen are remembered.
/// </para>
/// Recomputed only when something that matters changed: the viewer moved, a tile changed, the
/// carried light was switched, or the light map was rebuilt.
/// </summary>
public sealed class FieldOfView
{
    /// <summary>CDDA's <c>MAX_VIEW_DISTANCE</c>, in tiles.</summary>
    public const int MaxRange = 60;

    private readonly WorldMap _world;
    private readonly bool[][] _visible;
    private readonly bool[][] _seen;
    private readonly float[] _carried;
    private readonly List<int> _carriedTouched = new();
    private (TilePos Viewer, float Carried, int LightVersion)? _last;
    private bool _dirty = true;

    public LightMap Light { get; }

    /// <summary>Where the last update looked from.</summary>
    public TilePos Viewer { get; private set; }

    /// <summary>Counts recomputes, so the renderer can tell when to redraw the fog.</summary>
    public int Version { get; private set; }

    public FieldOfView(WorldMap world)
    {
        _world = world;
        Light = new LightMap(world);
        int levels = world.MaxZ - world.MinZ + 1, size = world.Width * world.Height;
        _visible = new bool[levels][];
        _seen = new bool[levels][];
        for (int i = 0; i < levels; i++)
        {
            _visible[i] = new bool[size];
            _seen[i] = new bool[size];
        }
        _carried = new float[size];
        world.TileChanged += (_, _, _) => _dirty = true;
    }

    /// <summary>
    /// Brings sight up to date for a viewer at <paramref name="viewer"/> carrying a light of
    /// <paramref name="carriedLight"/> (0 for none) at <paramref name="now"/>. Returns true if
    /// anything was recomputed.
    /// </summary>
    public bool Update(TilePos viewer, float carriedLight, GameTime now)
    {
        bool lightChanged = Light.Update(now);
        var state = (viewer, carriedLight, Light.Version);
        if (!_dirty && !lightChanged && _last == state)
            return false;
        _last = state;
        _dirty = false;
        Recompute(viewer, carriedLight);
        return true;
    }

    public bool IsVisible(TilePos p) => _world.InBounds(p) && _visible[p.Z - _world.MinZ][Index(p)];

    /// <summary>Whether the player has ever seen this tile.</summary>
    public bool HasSeen(TilePos p) => _world.InBounds(p) && _seen[p.Z - _world.MinZ][Index(p)];

    /// <summary>Seen before but not in view now: drawn from memory.</summary>
    public bool IsRemembered(TilePos p) => HasSeen(p) && !IsVisible(p);

    /// <summary>Total light at a tile: fixed sources plus the viewer's carried light (on their level).</summary>
    public float LightAt(TilePos p)
    {
        float light = Light.At(p);
        if (_world.InBounds(p) && p.Z == Viewer.Z)
            light += _carried[Index(p)];
        return light;
    }

    /// <summary>Forgets everything seen (a new map, or for tests).</summary>
    public void ForgetAll()
    {
        foreach (var seenLevel in _seen)
            Array.Clear(seenLevel);
        _dirty = true;
    }

    private int Index(TilePos p) => p.Y * _world.Width + p.X;

    private void Recompute(TilePos viewer, float carriedLight)
    {
        Viewer = viewer;
        foreach (var visibleLevel in _visible)
            Array.Clear(visibleLevel);
        foreach (int i in _carriedTouched)
            _carried[i] = 0;
        _carriedTouched.Clear();
        if (!_world.InBounds(viewer))
        {
            Version++;
            return;
        }

        var level = _world.Level(viewer.Z);
        if (carriedLight > 0)
            LightMap.AddSource(level, _carried, viewer.X, viewer.Y, carriedLight, _carriedTouched);

        int vz = viewer.Z - _world.MinZ;
        var visible = _visible[vz];
        var seen = _seen[vz];
        ShadowCaster.Cast(viewer.X, viewer.Y, MaxRange, (x, y) => !level.IsTransparent(x, y), (x, y) =>
        {
            if (!level.InBounds(x, y))
                return;
            bool adjacent = Math.Abs(x - viewer.X) <= 1 && Math.Abs(y - viewer.Y) <= 1;
            if (adjacent || ApparentLight(level, viewer.Z, x, y, viewer.X, viewer.Y) >= LightLevels.AmbientLow)
            {
                int i = y * level.Width + x;
                visible[i] = true;
                seen[i] = true;
            }
        });

        // Looking down: through open air and stairwells you see the level below, lit or not by its own light.
        for (int z = viewer.Z - 1; z >= _world.MinZ; z--)
        {
            var above = _world.Level(z + 1);
            var below = _world.Level(z);
            var aboveVisible = _visible[z + 1 - _world.MinZ];
            var belowVisible = _visible[z - _world.MinZ];
            var belowSeen = _seen[z - _world.MinZ];
            for (int y = 0; y < _world.Height; y++)
            for (int x = 0; x < _world.Width; x++)
            {
                int i = y * _world.Width + x;
                if (!aboveVisible[i] || !IsOpenBelow(above.TerrainAt(x, y)))
                    continue;
                if (ApparentLight(below, z, x, y, x, y) < LightLevels.AmbientLow)
                    continue;
                belowVisible[i] = true;
                belowSeen[i] = true;
            }
        }
        Version++;
    }

    /// <summary>Whether you can look down through this tile to the level below.</summary>
    private static bool IsOpenBelow(TerrainType t) => t.Has("NO_FLOOR") || t.Has("GOES_DOWN");

    /// <summary>
    /// How lit a tile looks from (vx, vy). A see-through tile shows its own light. An opaque one
    /// (a wall) shows the light on its side facing the viewer: the brightest see-through neighbor
    /// toward them. That way a sunlit outside wall doesn't show up from a dark room.
    /// </summary>
    private float ApparentLight(TileGrid level, int z, int x, int y, int vx, int vy)
    {
        if (level.IsTransparent(x, y) || (x == vx && y == vy))
            return LightAt(new TilePos(x, y, z));
        int sx = Math.Sign(vx - x), sy = Math.Sign(vy - y);
        float best = 0;
        foreach (var (nx, ny) in new[] { (x + sx, y + sy), (x + sx, y), (x, y + sy) })
        {
            if ((nx, ny) != (x, y) && level.IsTransparent(nx, ny))
                best = Math.Max(best, LightAt(new TilePos(nx, ny, z)));
        }
        return best;
    }
}
