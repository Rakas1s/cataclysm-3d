using Cataclysm3D.Importer;
using Cataclysm3D.Importer.World;
using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Tests;

public class TownTests
{
    private const string Tiles = """
        [
          { "type": "terrain", "id": "t_grass", "name": "grass", "move_cost": 2, "flags": ["TRANSPARENT", "FLAT"] },
          { "type": "terrain", "id": "t_sidewalk", "name": "sidewalk", "move_cost": 2, "flags": ["TRANSPARENT", "FLAT"] },
          { "type": "terrain", "id": "t_pavement", "name": "pavement", "move_cost": 2, "flags": ["TRANSPARENT", "FLAT"] },
          { "type": "terrain", "id": "t_open_air", "name": "open air", "move_cost": 2, "flags": ["TRANSPARENT", "NO_FLOOR"] },
          { "type": "terrain", "id": "t_soil", "name": "solid earth", "move_cost": 0, "roof": "t_dirt", "flags": ["WALL", "NATURAL_UNDERGROUND"] },
          { "type": "terrain", "id": "t_dirt", "name": "dirt", "move_cost": 2, "flags": ["TRANSPARENT", "FLAT"] },
          { "type": "terrain", "id": "t_floor", "name": "floor", "move_cost": 2, "roof": "t_flat_roof", "flags": ["TRANSPARENT", "INDOORS"] },
          { "type": "terrain", "id": "t_wall", "name": "wall", "move_cost": 0, "roof": "t_flat_roof", "flags": ["WALL"] },
          { "type": "terrain", "id": "t_flat_roof", "name": "flat roof", "move_cost": 2, "flags": ["TRANSPARENT", "FLAT"] },
          { "type": "terrain", "id": "t_stairs_up", "name": "stairs", "move_cost": 2, "flags": ["TRANSPARENT", "GOES_UP", "INDOORS"] },
          { "type": "terrain", "id": "t_stairs_down", "name": "stairs", "move_cost": 2, "roof": "t_flat_roof", "flags": ["TRANSPARENT", "GOES_DOWN", "INDOORS"] },
          { "type": "terrain", "id": "t_ladder_up", "name": "ladder", "move_cost": 2, "flags": ["TRANSPARENT", "GOES_UP", "DIFFICULT_Z"] },
          { "type": "terrain", "id": "t_gutter_downspout", "name": "downspout", "move_cost": 3, "flags": ["TRANSPARENT", "THIN_OBSTACLE", "CLIMBABLE"] },
          { "type": "terrain", "id": "t_gutter_drop", "name": "gutter drop", "move_cost": 3, "flags": ["TRANSPARENT", "THIN_OBSTACLE", "CLIMBABLE"] }
        ]
        """;

    // A 5×4 house. The second floor's stairs down sit one tile east of the ground floor's
    // stairs up, so finding the other end has to search. The second floor leaves (1, 2) open,
    // and the roof mapgen is missing a corner, so roofs get added there.
    private const string House = """
        [
          { "type": "city_building", "id": "test_house", "locations": ["land"], "overmaps": [
              { "point": [0, 0, -1], "overmap": "test_basement_north" },
              { "point": [0, 0, 0], "overmap": "test_ground_north" },
              { "point": [0, 0, 1], "overmap": "test_second_north" },
              { "point": [0, 0, 2], "overmap": "test_roof_north" } ] },
          { "type": "city_building", "id": "test_bungalow", "locations": ["land"], "overmaps": [
              { "point": [0, 0, 0], "overmap": "test_bungalow_north" },
              { "point": [0, 0, 1], "overmap": "test_bungalow_roof_north" } ] },
          { "type": "city_building", "id": "test_block", "locations": ["land"], "overmaps": [
              { "point": [0, 0, 0], "overmap": "a_north" }, { "point": [1, 0, 0], "overmap": "b_north" } ] },
          { "type": "palette", "id": "test_palette", "terrain": {
              "#": "t_wall", ".": "t_floor", "<": "t_stairs_up", ">": "t_stairs_down", " ": "t_open_air",
              "_": "t_flat_roof", "|": "t_gutter_downspout", "o": "t_gutter_drop", "g": "t_grass", "s": "t_soil" } },
          { "type": "mapgen", "om_terrain": "test_basement", "object": { "palettes": ["test_palette"], "rows": [
              "sssss",
              "s#<#s",
              "s#..s",
              "sssss" ] } },
          { "type": "mapgen", "om_terrain": "test_ground", "object": { "palettes": ["test_palette"], "rows": [
              "#####",
              "#<.>#",
              "#...#",
              "#####" ] } },
          { "type": "mapgen", "om_terrain": "test_second", "object": { "palettes": ["test_palette"], "rows": [
              "#####",
              "#.>.#",
              "# ..#",
              "#####" ] } },
          { "type": "mapgen", "om_terrain": "test_roof", "object": { "palettes": ["test_palette"], "rows": [
              "____ ",
              "_____",
              "_____",
              "_____" ] } },
          { "type": "mapgen", "om_terrain": "test_bungalow", "object": { "palettes": ["test_palette"], "rows": [
              "g|###",
              "gg#.#",
              "gg###" ] } },
          { "type": "mapgen", "om_terrain": "test_bungalow_roof", "object": { "palettes": ["test_palette"], "rows": [
              "  o__",
              "  ___",
              "  ___" ] } }
        ]
        """;

    private static (TownGenerator Towns, Dictionary<string, CityBuilding> Buildings) Setup()
    {
        var db = new ContentDatabase();
        db.LoadJsonText(Tiles, "tiles.json");
        db.LoadJsonText(House, "house.json");
        var registry = TileTypeLoader.Load(db);
        var buildings = CityBuilding.LoadAll(db);
        return (new TownGenerator(new MapgenGenerator(db, registry, new MapgenLibrary(db)), registry, buildings), buildings);
    }

    [Fact]
    public void CityBuildingsMapZLevelsToOmTerrains()
    {
        var (_, buildings) = Setup();

        var house = buildings["test_house"];
        Assert.Equal("test_basement", house.Levels[-1]);
        Assert.Equal("test_ground", house.Levels[0]);
        Assert.Equal("test_roof", house.Levels[2]);
        Assert.DoesNotContain("test_block", buildings.Keys); // spans two overmap tiles
    }

    [Fact]
    public void GeneratesEveryLevelOfAHouse()
    {
        var (towns, _) = Setup();
        var town = towns.Generate(new[] { "test_house" }, Array.Empty<string>(), seed: 1);
        var map = town.Map;
        var lot = town.Lots[0];
        TerrainType At(int lx, int ly, int z)
        {
            var (x, y) = lot.ToTown(lx, ly);
            return map.TerrainAt(new TilePos(x, y, z));
        }

        Assert.Equal(-1, map.MinZ);
        Assert.Equal(2, map.MaxZ);
        Assert.Equal("t_stairs_up", At(2, 1, -1).Id);
        Assert.Equal("t_stairs_up", At(1, 1, 0).Id);
        Assert.Equal("t_stairs_down", At(3, 1, 0).Id);
        Assert.Equal("t_stairs_down", At(2, 1, 1).Id);
        Assert.Equal("t_flat_roof", At(1, 1, 2).Id);

        // Outside the house: soil below, grass on the ground, open air above.
        Assert.Equal("t_soil", map.TerrainAt(new TilePos(0, 0, -1)).Id);
        Assert.Equal("t_grass", map.TerrainAt(new TilePos(0, 0, 0)).Id);
        Assert.Equal("t_open_air", map.TerrainAt(new TilePos(0, 0, 1)).Id);

        // CDDA's add_roofs: the ground floor's wall corner gets the roof the second floor left
        // out, and the gap in the roof mapgen gets one too.
        Assert.Equal("t_flat_roof", At(1, 2, 1).Id);
        Assert.Equal("t_flat_roof", At(4, 0, 2).Id);
    }

    [Fact]
    public void NorthRowHousesAreTurnedToFaceTheRoad()
    {
        var (towns, _) = Setup();
        var town = towns.Generate(new[] { "test_house" }, new[] { "test_house" }, seed: 1);
        var south = town.Lots.Single(l => l.QuarterTurns == 0);
        var north = town.Lots.Single(l => l.QuarterTurns == 2);

        Assert.True(north.Y < south.Y);
        // Mapgen (1, 1) is the stairs up either way; turned 180° it lands near the lot's far corner.
        var (nx, ny) = north.ToTown(1, 1);
        Assert.Equal((north.X + north.Width - 2, north.Y + north.Height - 2), (nx, ny));
        Assert.Equal("t_stairs_up", town.Map.TerrainAt(new TilePos(nx, ny, 0)).Id);
        // The road between the rows.
        Assert.Equal("t_sidewalk", town.Map.TerrainAt(new TilePos(0, north.Y + north.Height, 0)).Id);
        Assert.Equal("t_pavement", town.Map.TerrainAt(new TilePos(1, north.Y + north.Height + 3, 0)).Id);
    }

    [Fact]
    public void StairsLeadToTheNearestMatchingStairs()
    {
        var (towns, _) = Setup();
        var town = towns.Generate(new[] { "test_house" }, Array.Empty<string>(), seed: 1);
        var map = town.Map;
        var lot = town.Lots[0];
        TilePos P(int lx, int ly, int z)
        {
            var (x, y) = lot.ToTown(lx, ly);
            return new TilePos(x, y, z);
        }

        Assert.True(map.TryPlanVerticalMove(P(1, 1, 0), 1, (0, 0), out var up, out _));
        Assert.Equal(P(2, 1, 1), up.Destination); // the stairs down are one tile over
        Assert.Equal(VerticalMoveKind.Stairs, up.Kind);
        Assert.Equal(WorldMap.StairsCost, up.Cost);

        Assert.True(map.TryPlanVerticalMove(up.Destination, -1, (0, 0), out var back, out _));
        Assert.Equal(P(1, 1, 0), back.Destination);

        Assert.True(map.TryPlanVerticalMove(P(3, 1, 0), -1, (0, 0), out var basement, out _));
        Assert.Equal(P(2, 1, -1), basement.Destination);

        // Plain floor has no way up, and stairs only go the way they say.
        Assert.False(map.TryPlanVerticalMove(P(2, 2, 0), 1, (0, 0), out _, out var reason));
        Assert.Equal("You can't go up here.", reason);
        Assert.False(map.TryPlanVerticalMove(P(1, 1, 0), -1, (0, 0), out _, out _));
    }

    [Fact]
    public void StairsWithNothingWalkableAtTheOtherEndAreBlocked()
    {
        var registry = TileTypeLoader.Load(LoadTiles());
        var map = new WorldMap(registry, 3, 3, 0, 1, z => z == 0 ? "t_floor" : "t_wall");
        map.Level(0).SetTerrain(1, 1, "t_ladder_up");

        Assert.False(map.TryPlanVerticalMove(new TilePos(1, 1, 0), 1, (1, 0), out _, out var reason));
        Assert.Equal("Halfway up, the way up is blocked.", reason);

        // A ladder up into a room with no ladder down lands straight above.
        map.Level(1).SetTerrain(1, 1, "t_floor");
        Assert.True(map.TryPlanVerticalMove(new TilePos(1, 1, 0), 1, (1, 0), out var move, out _));
        Assert.Equal(new TilePos(1, 1, 1), move.Destination);
        Assert.Equal(VerticalMoveKind.Ladder, move.Kind);
    }

    [Fact]
    public void DownspoutsClimbToTheRoofAndGutterDropsBackDown()
    {
        var (towns, _) = Setup();
        var town = towns.Generate(new[] { "test_bungalow" }, Array.Empty<string>(), seed: 1);
        var map = town.Map;
        var lot = town.Lots[0];
        TilePos P(int lx, int ly, int z)
        {
            var (x, y) = lot.ToTown(lx, ly);
            return new TilePos(x, y, z);
        }

        // Standing on the grass next to the downspout at (1, 0), facing it. Straight above it is
        // open air, so you land on the gutter drop beside it.
        var start = P(1, 1, 0);
        var spout = P(1, 0, 0);
        Assert.True(map.TryPlanVerticalMove(start, 1, (spout.X, spout.Y), out var up, out _));
        Assert.Equal(VerticalMoveKind.Climb, up.Kind);
        Assert.Equal(P(2, 0, 1), up.Destination);
        Assert.Equal(WorldMap.ClimbCost, up.Cost);

        // From the roof, the gutter drop you stand on lets you climb back down next to it.
        Assert.True(map.TryPlanVerticalMove(up.Destination, -1, (up.Destination.X + 1, up.Destination.Y), out var down, out _));
        Assert.Equal(VerticalMoveKind.Climb, down.Kind);
        Assert.Equal(0, down.Destination.Z);
        Assert.True(map.Level(0).IsPassable(down.Destination.X, down.Destination.Y));
    }

    [Fact]
    public void OpenAirIsNotWalkableAndCountsAsUncovered()
    {
        var (towns, _) = Setup();
        var town = towns.Generate(new[] { "test_bungalow" }, Array.Empty<string>(), seed: 1);
        var map = town.Map;
        var lot = town.Lots[0];
        var (gx, gy) = lot.ToTown(0, 1);
        var (fx, fy) = lot.ToTown(3, 1);

        Assert.False(map.Level(1).IsPassable(gx, gy));
        Assert.True(map.Level(1).IsPassable(fx, fy)); // roof
        Assert.False(map.IsCovered(new TilePos(gx, gy, 0)));
        Assert.True(map.IsCovered(new TilePos(fx, fy, 0)));
    }

    /// <summary>The real CDDA two-story house: basement, ground floor, upstairs and roof all connect.</summary>
    [Fact]
    public void RealCddaTwoStoryHouseConnectsAllLevels()
    {
        string root = FindSnapshot();
        if (root is null)
            return;

        var db = new ContentDatabase();
        TownContent.Load(db, root);
        var registry = TileTypeLoader.Load(db);
        var towns = new TownGenerator(new MapgenGenerator(db, registry, new MapgenLibrary(db)), registry, CityBuilding.LoadAll(db));
        var town = towns.Generate(TownContent.SouthRow, TownContent.NorthRow, seed: 3);
        var map = town.Map;
        Assert.Equal(-1, map.MinZ);
        Assert.Equal(2, map.MaxZ);

        var lot = town.Lots.Single(l => l.Building.Id == "house_two_story_basement");
        var ground = map.Level(0);
        var stairs = Enumerable.Range(0, lot.Height)
            .SelectMany(ly => Enumerable.Range(0, lot.Width).Select(lx => lot.ToTown(lx, ly)))
            .ToList();
        var up = stairs.Single(p => ground.TerrainAt(p.X, p.Y).Has("GOES_UP"));
        var down = stairs.Single(p => ground.TerrainAt(p.X, p.Y).Has("GOES_DOWN"));

        Assert.True(map.TryPlanVerticalMove(new TilePos(up.X, up.Y, 0), 1, (0, 0), out var toSecond, out _));
        Assert.Equal(new TilePos(up.X, up.Y, 1), toSecond.Destination);
        Assert.True(map.TryPlanVerticalMove(new TilePos(down.X, down.Y, 0), -1, (0, 0), out var toBasement, out _));
        Assert.Equal(new TilePos(down.X, down.Y, -1), toBasement.Destination);
        Assert.True(map.IsCovered(new TilePos(up.X, up.Y + 1, 0)));

        // Some downspout in town gets you onto a roof.
        var spouts = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width).Select(x => (x, y)))
            .Where(p => ground.TerrainAt(p.x, p.y).Has("CLIMBABLE"))
            .ToList();
        Assert.Contains(spouts, s =>
            map.TryPlanVerticalMove(new TilePos(s.x, s.y, 0), 1, s, out var climb, out _)
            && (map.TerrainAt(climb.Destination).Id.Contains("roof", StringComparison.Ordinal)
                || map.TerrainAt(climb.Destination).Id.Contains("gutter", StringComparison.Ordinal)));
    }

    private static ContentDatabase LoadTiles()
    {
        var db = new ContentDatabase();
        db.LoadJsonText(Tiles, "tiles.json");
        return db;
    }

    private static string FindSnapshot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "cdda-snapshot", "data", "json");
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null!;
    }
}
