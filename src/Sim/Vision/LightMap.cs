using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Sim.Vision;

/// <summary>CDDA's light thresholds (<c>lightmap.h</c>).</summary>
public static class LightLevels
{
    /// <summary>Below this a tile is too dark to make out (CDDA's <c>LIGHT_AMBIENT_LOW</c>).</summary>
    public const float AmbientLow = 3.5f;

    /// <summary>Twilight's floor (CDDA's <c>LIGHT_AMBIENT_MINIMAL</c>).</summary>
    public const float AmbientMinimal = 3.7f;

    /// <summary>Comfortably lit (CDDA's <c>LIGHT_AMBIENT_LIT</c>).</summary>
    public const float AmbientLit = 10f;

    /// <summary>An item's light flag LIGHT_300: a flashlight that's on.</summary>
    public const float Flashlight = 300f;

    /// <summary>
    /// Light a source of <paramref name="luminance"/> casts at <paramref name="distance"/> tiles:
    /// inverse square, counted from one tile out, so it reaches <see cref="AmbientLow"/> at
    /// CDDA's <c>LIGHT_RANGE</c> (√(luminance / 3.5) − 1 tiles).
    /// </summary>
    public static float Falloff(float luminance, double distance) => (float)(luminance / ((distance + 1) * (distance + 1)));

    /// <summary>How far a source lights tiles enough to see them (CDDA's <c>LIGHT_RANGE</c>).</summary>
    public static int Range(float luminance) => (int)Math.Ceiling(Math.Sqrt(luminance / AmbientLow) - 1);
}

/// <summary>
/// Light from fixed sources on every z-level: the sky outdoors, daylight (or moonlight) coming in
/// through windows and doorways, and terrain or furniture with CDDA's <c>light_emitted</c>. Carried
/// lights are added on top by <see cref="FieldOfView"/>. Rebuilt only when a tile changes or the
/// sky's light has moved noticeably, not every turn.
/// </summary>
public sealed class LightMap
{
    private readonly WorldMap _world;
    private readonly float[][] _light;
    private readonly List<(TilePos Pos, int Luminance)> _emitters = new();
    private bool _dirty = true;

    /// <summary>Natural light outdoors as of the last rebuild.</summary>
    public float SkyLight { get; private set; } = float.NaN;

    /// <summary>Tiles with <c>light_emitted</c>, as of the last rebuild (lamps for the renderer).</summary>
    public IReadOnlyList<(TilePos Pos, int Luminance)> Emitters => _emitters;

    /// <summary>Counts rebuilds, so callers can tell when the light changed.</summary>
    public int Version { get; private set; }

    public LightMap(WorldMap world)
    {
        _world = world;
        _light = new float[world.MaxZ - world.MinZ + 1][];
        for (int i = 0; i < _light.Length; i++)
            _light[i] = new float[world.Width * world.Height];
        world.TileChanged += (_, _, _) => _dirty = true;
    }

    /// <summary>Fixed light at a tile, or 0 off the map.</summary>
    public float At(TilePos p) => _world.InBounds(p) ? _light[p.Z - _world.MinZ][p.Y * _world.Width + p.X] : 0;

    /// <summary>Brings the map up to date for <paramref name="now"/>. Returns true if it was rebuilt.</summary>
    public bool Update(GameTime now)
    {
        float sky = Celestial.SunLight(now);
        // Sky light drifts every turn around dawn and dusk; a 2% change is too small to see.
        if (!_dirty && Math.Abs(sky - SkyLight) <= Math.Max(0.05f, SkyLight * 0.02f))
            return false;
        Rebuild(sky);
        return true;
    }

    private void Rebuild(float sky)
    {
        SkyLight = sky;
        _dirty = false;
        _emitters.Clear();
        int w = _world.Width, h = _world.Height;
        for (int z = _world.MinZ; z <= _world.MaxZ; z++)
        {
            var light = _light[z - _world.MinZ];
            var level = _world.Level(z);
            var outdoors = new bool[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                outdoors[y * w + x] = !_world.IsCovered(new TilePos(x, y, z));
            for (int i = 0; i < light.Length; i++)
                light[i] = outdoors[i] ? sky : 0;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Daylight comes in through every covered, see-through tile next to the open sky:
                // windows, doorways, the edge of a porch. CDDA shines it in from the outside tile.
                if (!outdoors[y * w + x] && level.IsTransparent(x, y) && TouchesSky(level, outdoors, x, y))
                    AddSource(level, light, x, y, sky);
                if (level.LightEmitted(x, y) is > 0 and var lum)
                {
                    AddSource(level, light, x, y, lum);
                    _emitters.Add((new TilePos(x, y, z), lum));
                }
            }
        }
        Version++;
    }

    private static bool TouchesSky(TileGrid level, bool[] outdoors, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (level.InBounds(nx, ny) && outdoors[ny * level.Width + nx] && level.IsTransparent(nx, ny))
                return true;
        }
        return false;
    }

    /// <summary>Adds a point source's light to every tile it can see.</summary>
    internal static void AddSource(TileGrid level, float[] light, int sx, int sy, float luminance, List<int>? touched = null)
    {
        int range = LightLevels.Range(luminance);
        if (range < 0)
            return;
        // Shadowcasting can reveal a tile twice along octant edges; light it once.
        var lit = new HashSet<int>();
        ShadowCaster.Cast(sx, sy, range, (x, y) => !level.IsTransparent(x, y), (x, y) =>
        {
            if (!level.InBounds(x, y))
                return;
            int i = y * level.Width + x;
            if (!lit.Add(i))
                return;
            light[i] += LightLevels.Falloff(luminance, Math.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy)));
            touched?.Add(i);
        });
    }
}
