using Cataclysm3D.Importer;
using Cataclysm3D.Importer.World;
using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Tests;

public class MapgenTests
{
    private const string Tiles = """
        [
          { "type": "terrain", "id": "t_dirt", "name": "dirt", "move_cost": 2, "flags": ["TRANSPARENT", "FLAT"] },
          { "type": "terrain", "id": "t_floor", "name": "floor", "move_cost": 2, "roof": "t_flat_roof", "flags": ["TRANSPARENT", "INDOORS"] },
          { "type": "terrain", "id": "t_wall_w", "name": "white wall", "move_cost": 0, "flags": ["WALL"] },
          { "type": "terrain", "id": "t_wall_b", "name": "blue wall", "move_cost": 0, "flags": ["WALL"] },
          { "type": "terrain", "id": "t_door_c", "name": "closed door", "move_cost": 0, "open": "t_door_o", "flags": ["DOOR"] },
          { "type": "terrain", "id": "t_door_o", "name": "open door", "move_cost": 2, "close": "t_door_c", "flags": ["TRANSPARENT"] },
          { "type": "terrain", "id": "t_door_locked", "name": "locked door", "move_cost": 0, "open": "t_door_o",
            "flags": ["LOCKED", "OPENCLOSE_INSIDE"], "bash": { "str_min": 8, "str_max": 80, "ter_set": "t_door_b", "sound": "smash!", "sound_fail": "whump!" } },
          { "type": "terrain", "id": "t_door_b", "name": "damaged door", "move_cost": 0 },
          { "type": "terrain", "id": "t_window", "name": "window", "move_cost": 0, "open": "t_window_open", "flags": ["WINDOW"],
            "bash": { "str_min": 3, "str_max": 6, "ter_set": "t_window_frame", "sound": "glass breaking!", "sound_fail": "whack!" } },
          { "type": "terrain", "id": "t_window_open", "name": "open window", "move_cost": 4, "flags": ["WINDOW"] },
          { "type": "terrain", "id": "t_window_frame", "name": "window frame", "move_cost": 8, "flags": ["WINDOW"] },
          { "type": "furniture", "id": "f_chair", "name": "chair", "move_cost_mod": 1 },
          { "type": "furniture", "id": "f_fridge", "name": "fridge", "move_cost_mod": -1 }
        ]
        """;

    private static (MapgenGenerator Gen, TileRegistry Registry) Setup(string mapgenJson)
    {
        var db = new ContentDatabase();
        db.LoadJsonText(Tiles, "tiles.json");
        db.LoadJsonText(mapgenJson, "mapgen.json");
        var registry = TileTypeLoader.Load(db);
        return (new MapgenGenerator(db, registry, new MapgenLibrary(db)), registry);
    }

    [Fact]
    public void PlacesRowsThroughNestedParametrizedPalettes()
    {
        var (gen, _) = Setup("""
            [
              { "type": "palette", "id": "walls", "parameters": { "wall": { "type": "ter_str_id", "default": { "distribution": [["t_wall_b", 1]] } } },
                "terrain": { "|": { "param": "wall", "fallback": "t_wall_w" }, "+": "t_door_c" } },
              { "type": "palette", "id": "house", "palettes": ["walls"], "furniture": { "h": "f_chair", "{": "f_fridge" } },
              { "type": "mapgen", "om_terrain": "test_house", "object": {
                  "fill_ter": "t_floor",
                  "palettes": ["house"],
                  "rows": [
                    "|||||",
                    "|h {|",
                    "|+|||"
                  ],
                  "terrain": { "h": "t_floor", "{": "t_floor" } } }
            ]
            """);

        var result = gen.Generate("test_house", seed: 1);
        var map = result.Map;

        Assert.Equal(5, map.Width);
        Assert.Equal(3, map.Height);
        Assert.Equal("t_wall_b", map.TerrainAt(0, 0).Id);
        Assert.Equal("f_chair", map.FurnitureAt(1, 1)!.Id);
        Assert.True(map.IsPassable(1, 1));
        Assert.False(map.IsPassable(3, 1)); // fridge
        Assert.Equal("t_floor", map.TerrainAt(2, 1).Id); // unmapped space keeps fill_ter
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void DoorsOpenAndBecomePassable()
    {
        var (gen, _) = Setup("""
            [ { "type": "mapgen", "om_terrain": "door_test", "object": { "rows": ["+"], "terrain": { "+": "t_door_c" } } } ]
            """);
        var map = gen.Generate("door_test", 1).Map;

        Assert.False(map.IsPassable(0, 0));
        Assert.True(map.TryOpen(0, 0));
        Assert.Equal("t_door_o", map.TerrainAt(0, 0).Id);
        Assert.True(map.IsPassable(0, 0));
    }

    [Fact]
    public void LockedDoorsOpenOnlyFromInside()
    {
        var (gen, _) = Setup("""
            [ { "type": "mapgen", "om_terrain": "lock", "object": { "rows": ["L"], "terrain": { "L": "t_door_locked" } } } ]
            """);
        var map = gen.Generate("lock", 1).Map;

        Assert.False(map.TryOpen(0, 0, fromInside: false));
        Assert.Equal("t_door_locked", map.TerrainAt(0, 0).Id);
        Assert.True(map.TryOpen(0, 0, fromInside: true));
        Assert.Equal("t_door_o", map.TerrainAt(0, 0).Id);
    }

    [Fact]
    public void WindowsDontOpenOnBumpButSmashIntoFrames()
    {
        var (gen, _) = Setup("""
            [ { "type": "mapgen", "om_terrain": "win", "object": { "rows": ["W"], "terrain": { "W": "t_window" } } } ]
            """);
        var map = gen.Generate("win", 1).Map;

        Assert.False(map.TryOpen(0, 0, fromInside: true));
        var (outcome, sound, _) = map.TryBash(0, 0, strength: 10, new Random(1));
        Assert.Equal(TileGrid.BashOutcome.Broke, outcome);
        Assert.Equal("glass breaking!", sound);
        Assert.Equal("t_window_frame", map.TerrainAt(0, 0).Id);
        Assert.True(map.IsPassable(0, 0));
    }

    [Fact]
    public void WeakBashesUsuallyFailOnDoors()
    {
        var (gen, _) = Setup("""
            [ { "type": "mapgen", "om_terrain": "lock", "object": { "rows": ["L"], "terrain": { "L": "t_door_locked" } } } ]
            """);
        var map = gen.Generate("lock", 1).Map;
        var rng = new Random(3);

        int tries = 0;
        while (map.TryBash(0, 0, strength: 10, rng).Outcome == TileGrid.BashOutcome.Failed)
            tries++;
        Assert.Equal("t_door_b", map.TerrainAt(0, 0).Id);
        Assert.True(tries > 0, "a strength-10 kick should rarely break an 8–80 door first try");
    }

    [Fact]
    public void WeightedListsAndUnknownSymbolsAreReported()
    {
        var (gen, _) = Setup("""
            [ { "type": "mapgen", "om_terrain": "w", "object": { "rows": ["ab"], "terrain": { "a": ["t_wall_w", ["t_wall_b", 0]] } } } ]
            """);
        var result = gen.Generate("w", 7);

        Assert.Equal("t_wall_w", result.Map.TerrainAt(0, 0).Id);
        Assert.Contains(result.Warnings, w => w.Contains("'b'"));
    }

    /// <summary>Runs every house in the real CDDA snapshot when it has been fetched.</summary>
    [Fact]
    public void RealCddaHousesGenerate()
    {
        string root = FindSnapshot();
        if (root is null)
            return;

        var db = new ContentDatabase();
        db.LoadDirectory(Path.Combine(root, "furniture_and_terrain"));
        db.LoadDirectory(Path.Combine(root, "mapgen_palettes"));
        db.LoadDirectory(Path.Combine(root, "mapgen", "house"));
        var registry = TileTypeLoader.Load(db);
        var gen = new MapgenGenerator(db, registry, new MapgenLibrary(db));

        var result = gen.Generate("bungalow01_1", seed: 42);
        Assert.Equal(24, result.Map.Width);
        Assert.Equal(24, result.Map.Height);
        Assert.Contains(Enumerable.Range(0, 24), x => result.Map.TerrainAt(x, 6).Has("WALL"));
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
