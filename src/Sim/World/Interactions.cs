namespace Cataclysm3D.Sim.World;

public enum InteractionKind { Open, Close, WalkOnto, ClimbUp, ClimbDown, Smash }

/// <summary>Something the player can do to a tile, phrased for a tooltip ("Open the door").</summary>
public readonly record struct Interaction(InteractionKind Kind, string Text);

/// <summary>
/// What the player can do to the tile they face, best first, so the game can list the top few
/// next to the crosshair. Everything here is a dry run of the real actions (bumping to open,
/// closing, smashing, stairs and climbing); nothing changes the map.
/// </summary>
public static class Interactions
{
    public static IReadOnlyList<Interaction> For(WorldMap world, TilePos player, (int X, int Y) target, int bashStrength, int max = 3)
    {
        var list = new List<Interaction>();
        var level = world.Level(player.Z);
        var (x, y) = target;
        if (!level.InBounds(x, y))
            return list;
        var ter = level.TerrainAt(x, y);
        var fur = level.FurnitureAt(x, y);

        if (level.CanOpen(x, y, fromInside: level.IsIndoors(player.X, player.Y)))
            list.Add(new(InteractionKind.Open, $"Open the {(fur is { Open: not null } ? fur.Name : ter.Name)}"));
        if (level.CanClose(x, y))
            list.Add(new(InteractionKind.Close, $"Close the {(fur is { Close: not null } ? fur.Name : ter.Name)}"));

        if (level.IsPassable(x, y) && WalkOntoText(level, ter, fur, x, y) is { } walk)
            list.Add(new(InteractionKind.WalkOnto, walk));

        if (ter.Has("CLIMBABLE"))
        {
            foreach (int dz in new[] { 1, -1 })
            {
                if (world.TryPlanVerticalMove(player, dz, target, out var move, out _) && move.Kind == VerticalMoveKind.Climb)
                    list.Add(new(dz > 0 ? InteractionKind.ClimbUp : InteractionKind.ClimbDown,
                        $"Climb {(dz > 0 ? "up" : "down")} the {move.Via}"));
            }
        }

        if (level.BashTarget(x, y) is { } bash && SmashChance(bash.Bash, bashStrength) > 0)
        {
            float p = SmashChance(bash.Bash, bashStrength);
            string odds = p >= 0.99f ? "" : p >= 0.5f ? " (may take a few tries)" : " (hard)";
            list.Add(new(InteractionKind.Smash, $"Smash the {bash.Name}{odds}"));
        }

        return list.Count > max ? list.GetRange(0, max) : list;
    }

    /// <summary>
    /// Stepping onto a tile is only worth listing when it does something or isn't plain ground:
    /// stairs, ladders, open doors, furniture you clamber over.
    /// </summary>
    private static string? WalkOntoText(TileGrid level, TerrainType ter, FurnitureType? fur, int x, int y)
    {
        bool slow = level.MoveCost(x, y) > 2;
        if (ter.Has("GOES_UP") && !ter.Has("LIQUID"))
            return $"Go up the {ter.Name}";
        if (ter.Has("GOES_DOWN") && !ter.Has("LIQUID"))
            return $"Go down the {ter.Name}";
        if (fur is not null)
            return slow ? $"Climb over the {fur.Name} (slow)" : $"Step onto the {fur.Name}";
        if (ter.Close is not null || ter.Has("WINDOW"))
            return slow ? $"Climb through the {ter.Name} (slow)" : $"Walk through the {ter.Name}";
        return null;
    }

    /// <summary>Chance one smash works: CDDA rolls str_min..str_max and succeeds at or below your strength.</summary>
    public static float SmashChance(BashInfo bash, int strength)
    {
        int range = bash.StrMax - bash.StrMin + 1;
        return range <= 0 ? (strength >= bash.StrMin ? 1 : 0) : Math.Clamp((float)(strength - bash.StrMin + 1) / range, 0f, 1f);
    }
}
