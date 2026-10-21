using System.Text.Json.Nodes;
using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Importer.World;

/// <summary>
/// A CDDA <c>city_building</c> that fits on one overmap tile: which om_terrain is generated on
/// each z-level (ground floor on 0, <c>_roof</c> above, basement on -1, …).
/// </summary>
public sealed record CityBuilding(string Id, IReadOnlyDictionary<int, string> Levels)
{
    /// <summary>
    /// Loads every single-tile city_building. Multi-tile ones and ones facing anything but north
    /// (the orientation their mapgen is drawn in) are skipped for now.
    /// </summary>
    public static Dictionary<string, CityBuilding> LoadAll(ContentDatabase db)
    {
        var result = new Dictionary<string, CityBuilding>(StringComparer.Ordinal);
        foreach (var (id, data) in db.All("city_building"))
        {
            if (data["overmaps"] is not JsonArray overmaps)
                continue;
            var levels = new Dictionary<int, string>();
            bool fits = true;
            foreach (var entry in overmaps.OfType<JsonObject>())
            {
                if (entry["point"] is not JsonArray { Count: 3 } p || entry["overmap"]?.GetValue<string>() is not { } omt)
                    continue;
                if (p[0]!.GetValue<int>() != 0 || p[1]!.GetValue<int>() != 0)
                {
                    fits = false;
                    break;
                }
                levels[p[2]!.GetValue<int>()] = omt.EndsWith("_north", StringComparison.Ordinal) ? omt[..^"_north".Length] : omt;
            }
            if (fits && levels.ContainsKey(0))
                result[id] = new CityBuilding(id, levels);
        }
        return result;
    }
}

/// <summary>One building's plot in a town: top-left tile, size and how far it's turned clockwise.</summary>
public sealed record Lot(CityBuilding Building, int X, int Y, int Width, int Height, int QuarterTurns)
{
    /// <summary>Maps lot-local mapgen coordinates (as the JSON rows are drawn) to town coordinates.</summary>
    public (int X, int Y) ToTown(int lx, int ly)
    {
        var (rx, ry) = TileGrid.Rotate(lx, ly, Width, Height, QuarterTurns);
        return (X + rx, Y + ry);
    }
}

public sealed record Town(WorldMap Map, IReadOnlyList<Lot> Lots, IReadOnlyList<string> Warnings)
{
    /// <summary>Monsters the buildings' mapgen placed, in town coordinates (not yet spawned).</summary>
    public IReadOnlyList<(MonsterSpawn Spawn, TilePos Pos)> Spawns { get; init; } = Array.Empty<(MonsterSpawn, TilePos)>();
}

/// <summary>
/// Lays out a fixed town: an east-west road with sidewalks, one row of buildings south of it
/// facing north and one row north of it turned to face south. Every z-level of each building is
/// generated from its CDDA mapgen. Full overmap generation is a later milestone.
/// </summary>
public sealed class TownGenerator
{
    /// <summary>CDDA overmap tile edge in map tiles.</summary>
    public const int LotSize = 24;

    /// <summary>Open ground around the town, so you can walk around the outside.</summary>
    public const int Margin = 8;

    /// <summary>Road band between the rows: sidewalk, pavement, sidewalk.</summary>
    public const int RoadWidth = 12;
    private const int SidewalkWidth = 2;

    private readonly MapgenGenerator _mapgen;
    private readonly TileRegistry _registry;
    private readonly IReadOnlyDictionary<string, CityBuilding> _buildings;

    public TownGenerator(MapgenGenerator mapgen, TileRegistry registry, IReadOnlyDictionary<string, CityBuilding> buildings)
    {
        _mapgen = mapgen;
        _registry = registry;
        _buildings = buildings;
    }

    /// <param name="southRow">Buildings south of the road, west to east, facing north (as drawn).</param>
    /// <param name="northRow">Buildings north of the road, west to east, turned 180° to face the road.</param>
    public Town Generate(IReadOnlyList<string> southRow, IReadOnlyList<string> northRow, int seed)
    {
        var placed = new List<(CityBuilding Building, int X, int Y, int Turns)>();
        int northY = Margin;
        int roadY = northY + (northRow.Count > 0 ? LotSize : 0);
        int southY = roadY + RoadWidth;
        for (int i = 0; i < southRow.Count; i++)
            placed.Add((Building(southRow[i]), Margin + i * LotSize, southY, 0));
        for (int i = 0; i < northRow.Count; i++)
            placed.Add((Building(northRow[i]), Margin + i * LotSize, northY, 2));

        int width = Margin * 2 + Math.Max(Math.Max(southRow.Count, northRow.Count), 1) * LotSize;
        int height = southY + (southRow.Count > 0 ? LotSize : 0) + Margin;
        int minZ = Math.Min(0, placed.SelectMany(p => p.Building.Levels.Keys).DefaultIfEmpty(0).Min());
        int maxZ = Math.Max(0, placed.SelectMany(p => p.Building.Levels.Keys).DefaultIfEmpty(0).Max());

        var map = new WorldMap(_registry, width, height, minZ, maxZ, FillFor);
        LayRoad(map.Level(0), roadY);

        var warnings = new List<string>();
        var lots = new List<Lot>();
        var spawns = new List<(MonsterSpawn, TilePos)>();
        for (int i = 0; i < placed.Count; i++)
        {
            var (building, x, y, turns) = placed[i];
            foreach (var (z, omt) in building.Levels.OrderBy(l => l.Key))
            {
                MapgenResult result;
                try
                {
                    // Each building and level gets its own stable seed so re-rolls change everything.
                    result = _mapgen.Generate(omt, HashCode.Combine(seed, i, z));
                }
                catch (KeyNotFoundException e)
                {
                    warnings.Add($"{building.Id} z{z}: {e.Message}");
                    continue;
                }
                map.Level(z).Stamp(result.Map, x, y, turns, LotSize, LotSize);
                foreach (var spawn in result.Spawns)
                {
                    var (rx, ry) = TileGrid.Rotate(spawn.X, spawn.Y, LotSize, LotSize, turns);
                    spawns.Add((spawn, new TilePos(x + rx, y + ry, z)));
                }
                warnings.AddRange(result.Warnings.Select(w => $"{omt}: {w}"));
            }
            lots.Add(new Lot(building, x, y, LotSize, LotSize, turns));
        }
        map.AddMissingRoofs();
        return new Town(map, lots, warnings) { Spawns = spawns };
    }

    private CityBuilding Building(string id) =>
        _buildings.TryGetValue(id, out var b) ? b : throw new KeyNotFoundException($"No single-tile city_building \"{id}\"");

    /// <summary>Underground is solid earth, ground level grass, everything above open air.</summary>
    private string FillFor(int z) => z switch
    {
        < 0 => Pick("t_soil", "t_rock"),
        0 => Pick("t_grass", "t_dirt"),
        _ => Pick("t_open_air"),
    };

    private string Pick(params string[] ids) =>
        ids.FirstOrDefault(id => _registry.TryTerrain(id, out _)) ?? TileRegistry.NullTerrain;

    private void LayRoad(TileGrid ground, int roadY)
    {
        string sidewalk = Pick("t_sidewalk", "t_concrete", "t_dirt");
        string pavement = Pick("t_pavement", "t_dirt");
        string line = Pick("t_pavement_y", "t_pavement", "t_dirt");
        int center = roadY + RoadWidth / 2;
        for (int y = roadY; y < roadY + RoadWidth; y++)
        for (int x = 0; x < ground.Width; x++)
        {
            bool isSidewalk = y < roadY + SidewalkWidth || y >= roadY + RoadWidth - SidewalkWidth;
            // A dashed center line, two tiles on, two off.
            bool isLine = y == center && x % 4 < 2;
            ground.SetTerrain(x, y, isSidewalk ? sidewalk : isLine ? line : pavement);
        }
    }
}
