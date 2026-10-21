using Cataclysm3D.Importer.World;
using Cataclysm3D.Sim;
using Cataclysm3D.Sim.Creatures;
using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Importer.Creatures;

/// <summary>
/// Puts monsters in a town: whatever the buildings' mapgen placed, plus a few zombies from
/// CDDA's <c>GROUP_ZOMBIE</c> out on the street, standing in for the overmap's city spawns.
/// Nothing spawns right next to the player.
/// </summary>
public static class TownPopulation
{
    public const string StreetGroup = "GROUP_ZOMBIE";
    public const int StreetZombies = 6;

    /// <summary>No monster starts closer to the player than this.</summary>
    public const int SafeRadius = 12;

    public static void Populate(GameWorld game, Town town, MonsterRegistry monsters, int seed)
    {
        var rng = new Random(seed);
        var start = game.Player.Pos;
        bool FarEnough(TilePos p) => p.Z != start.Z || Math.Max(Math.Abs(p.X - start.X), Math.Abs(p.Y - start.Y)) >= SafeRadius;
        // M4 is about zombies; groups also list civilians and wildlife, which we re-roll.
        bool Zombie(MonsterType t) => t.IsZombie && t.IsHostile;

        foreach (var (spawn, pos) in town.Spawns)
        {
            var type = spawn.IsGroup ? monsters.Pick(spawn.Id, rng, Zombie) : monsters.Type(spawn.Id);
            if (type is not null && NearestFree(game, pos) is { } at && FarEnough(at))
                game.Spawn(type, at);
        }

        var map = town.Map;
        for (int placed = 0, attempts = 0; placed < StreetZombies && attempts < 2000; attempts++)
        {
            var p = new TilePos(rng.Next(map.Width), rng.Next(map.Height), 0);
            if (map.IsCovered(p) || !game.IsFree(p) || !FarEnough(p))
                continue;
            if (monsters.Pick(StreetGroup, rng, Zombie) is not { } type)
                break;
            game.Spawn(type, p);
            placed++;
        }
    }

    /// <summary>The free tile nearest <paramref name="p"/> on its level, within two tiles.</summary>
    private static TilePos? NearestFree(GameWorld game, TilePos p)
    {
        for (int r = 0; r <= 2; r++)
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            var q = p with { X = p.X + dx, Y = p.Y + dy };
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r && game.IsFree(q))
                return q;
        }
        return null;
    }
}
