using Cataclysm3D.Sim.Creatures;
using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Sim.Vision;

/// <summary>
/// Whether a monster can see something: CDDA's <c>monster::sight_range</c> (vision_day in full
/// daylight, vision_night in the dark, linear between, by the light on the target) and line of
/// sight by shadowcasting from the monster's own tile, the way the player's sight works.
/// </summary>
public static class MonsterSight
{
    /// <summary>CDDA's <c>DAYLIGHT_LEVEL</c>: light at which a monster sees its full day range.</summary>
    public const float DaylightLevel = 100f;

    public static int Range(MonsterType type, float lightAtTarget)
    {
        float light = Math.Clamp(lightAtTarget, 0, DaylightLevel);
        return (int)((light * type.VisionDay + (DaylightLevel - light) * type.VisionNight) / DaylightLevel);
    }

    /// <summary>
    /// Whether <paramref name="from"/> has a clear line of sight to <paramref name="to"/> on the
    /// same level within <paramref name="range"/> tiles. Adjacent tiles are always in sight.
    /// </summary>
    public static bool InSight(WorldMap world, TilePos from, TilePos to, int range)
    {
        if (from.Z != to.Z || !world.InBounds(from) || !world.InBounds(to))
            return false;
        int dx = to.X - from.X, dy = to.Y - from.Y;
        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) <= 1)
            return true;
        if (dx * dx + dy * dy > range * range + range)
            return false;
        var level = world.Level(from.Z);
        // Cast only as far as the target, so the cost grows with its distance, not the monster's range.
        int radius = (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy));
        bool seen = false;
        ShadowCaster.Cast(from.X, from.Y, radius, (x, y) => !level.IsTransparent(x, y), (x, y) =>
        {
            if (x == to.X && y == to.Y)
                seen = true;
        });
        return seen;
    }
}
