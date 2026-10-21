namespace Cataclysm3D.Sim.Creatures;

/// <summary>
/// A* over one z-level in 8 directions, like CDDA's monster pathfinding: diagonal steps cost the
/// same as straight ones and may squeeze between two walls. The caller prices each tile.
/// </summary>
public static class Pathfinder
{
    private static readonly (int Dx, int Dy)[] Steps =
        { (0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1) };

    /// <summary>
    /// The steps from <paramref name="from"/> to <paramref name="to"/> (excluding the start), or
    /// null when there's no way within <paramref name="maxNodes"/> expansions.
    /// </summary>
    /// <param name="cost">Cost of entering a tile, or 0 if it can't be entered. The goal is always enterable.</param>
    public static List<(int X, int Y)>? Find(int width, int height, (int X, int Y) from, (int X, int Y) to,
        Func<int, int, int> cost, int maxNodes = 6000)
    {
        if (from == to)
            return new List<(int, int)>();
        int Index(int x, int y) => y * width + x;
        static int Heuristic(int x, int y, (int X, int Y) t) => Math.Max(Math.Abs(x - t.X), Math.Abs(y - t.Y)) * 2;

        var open = new PriorityQueue<int, int>();
        var g = new Dictionary<int, int>();
        var came = new Dictionary<int, int>();
        int start = Index(from.X, from.Y), goal = Index(to.X, to.Y);
        g[start] = 0;
        open.Enqueue(start, Heuristic(from.X, from.Y, to));
        int expanded = 0;
        while (open.TryDequeue(out int cur, out int priority))
        {
            int cx = cur % width, cy = cur / width;
            if (cur == goal)
                break;
            if (priority > g[cur] + Heuristic(cx, cy, to))
                continue; // stale entry
            if (++expanded > maxNodes)
                return null;
            foreach (var (dx, dy) in Steps)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;
                int ni = Index(nx, ny);
                int c = ni == goal ? Math.Max(1, cost(nx, ny)) : cost(nx, ny);
                if (c <= 0)
                    continue;
                int ng = g[cur] + c;
                if (g.TryGetValue(ni, out int old) && old <= ng)
                    continue;
                g[ni] = ng;
                came[ni] = cur;
                open.Enqueue(ni, ng + Heuristic(nx, ny, to));
            }
        }
        if (!came.ContainsKey(goal))
            return null;
        var path = new List<(int, int)>();
        for (int at = goal; at != start; at = came[at])
            path.Add((at % width, at / width));
        path.Reverse();
        return path;
    }
}
