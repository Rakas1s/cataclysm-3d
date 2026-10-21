namespace Cataclysm3D.Sim.Vision;

/// <summary>
/// Recursive shadowcasting over a grid, as CDDA's <c>castLight</c> does it: walks the eight
/// octants around an origin row by row, narrowing the lit wedge at every opaque tile. Opaque tiles
/// are revealed themselves (you see the wall) but hide what's behind them.
/// </summary>
public static class ShadowCaster
{
    // Octant transforms: (xx, xy, yx, yy) for each of the eight octants.
    private static readonly int[,] Octants =
    {
        { 1, 0, 0, -1, -1, 0, 0, 1 },
        { 0, 1, -1, 0, 0, -1, 1, 0 },
        { 0, 1, 1, 0, 0, -1, -1, 0 },
        { 1, 0, 0, 1, -1, 0, 0, -1 },
    };

    /// <summary>
    /// Calls <paramref name="reveal"/> for every tile in sight of (<paramref name="ox"/>,
    /// <paramref name="oy"/>) within <paramref name="radius"/> (Euclidean), including the origin.
    /// Tiles may be revealed more than once along octant edges. <paramref name="isOpaque"/> must
    /// return true off the map.
    /// </summary>
    public static void Cast(int ox, int oy, int radius, Func<int, int, bool> isOpaque, Action<int, int> reveal)
    {
        reveal(ox, oy);
        for (int o = 0; o < 8; o++)
            CastOctant(ox, oy, 1, 1f, 0f, radius, Octants[0, o], Octants[1, o], Octants[2, o], Octants[3, o], isOpaque, reveal);
    }

    private static void CastOctant(int ox, int oy, int row, float start, float end, int radius,
        int xx, int xy, int yx, int yy, Func<int, int, bool> isOpaque, Action<int, int> reveal)
    {
        if (start < end)
            return;
        // A little over r² rounds the circle's edge like CDDA's trig distance.
        int radiusSq = radius * radius + radius;
        float newStart = 0;
        for (int j = row; j <= radius; j++)
        {
            int dy = -j;
            bool blocked = false;
            for (int dx = -j; dx <= 0; dx++)
            {
                float leftSlope = (dx - 0.5f) / (dy + 0.5f);
                float rightSlope = (dx + 0.5f) / (dy - 0.5f);
                if (start < rightSlope)
                    continue;
                if (end > leftSlope)
                    break;

                int x = ox + dx * xx + dy * xy, y = oy + dx * yx + dy * yy;
                bool opaque = isOpaque(x, y);
                if (dx * dx + dy * dy <= radiusSq)
                    reveal(x, y);

                if (blocked)
                {
                    if (opaque)
                    {
                        newStart = rightSlope;
                        continue;
                    }
                    blocked = false;
                    start = newStart;
                }
                else if (opaque && j < radius)
                {
                    blocked = true;
                    CastOctant(ox, oy, j + 1, start, leftSlope, radius, xx, xy, yx, yy, isOpaque, reveal);
                    newStart = rightSlope;
                }
            }
            if (blocked)
                break;
        }
    }
}
