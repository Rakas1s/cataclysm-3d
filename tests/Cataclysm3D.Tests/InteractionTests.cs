using Cataclysm3D.Importer;
using Cataclysm3D.Importer.World;
using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Tests;

public class InteractionTests
{
    private const string Tiles = """
        [
          { "type": "terrain", "id": "t_grass", "name": "grass", "move_cost": 2, "flags": ["TRANSPARENT", "FLAT"] },
          { "type": "terrain", "id": "t_floor", "name": "floor", "move_cost": 2, "roof": "t_flat_roof", "flags": ["TRANSPARENT", "INDOORS"] },
          { "type": "terrain", "id": "t_flat_roof", "name": "flat roof", "move_cost": 2, "flags": ["TRANSPARENT", "FLAT"] },
          { "type": "terrain", "id": "t_wall", "name": "wall", "move_cost": 0, "roof": "t_flat_roof", "flags": ["WALL"],
            "bash": { "str_min": 60, "str_max": 150, "ter_set": "t_floor", "sound": "crash!", "sound_fail": "whump!" } },
          { "type": "terrain", "id": "t_door_c", "name": "closed door", "move_cost": 0, "open": "t_door_o", "flags": ["DOOR"],
            "bash": { "str_min": 8, "str_max": 80, "ter_set": "t_door_b", "sound": "smash!", "sound_fail": "whump!" } },
          { "type": "terrain", "id": "t_door_o", "name": "open door", "move_cost": 2, "close": "t_door_c", "flags": ["TRANSPARENT"] },
          { "type": "terrain", "id": "t_door_b", "name": "damaged door", "move_cost": 0 },
          { "type": "terrain", "id": "t_door_locked", "name": "locked door", "move_cost": 0, "open": "t_door_o", "roof": "t_flat_roof",
            "flags": ["LOCKED", "OPENCLOSE_INSIDE"] },
          { "type": "terrain", "id": "t_window", "name": "window", "move_cost": 0, "open": "t_window_open", "flags": ["WINDOW"],
            "bash": { "str_min": 3, "str_max": 6, "ter_set": "t_window_frame", "sound": "glass breaking!", "sound_fail": "whack!" } },
          { "type": "terrain", "id": "t_window_open", "name": "open window", "move_cost": 4, "flags": ["WINDOW"] },
          { "type": "terrain", "id": "t_window_frame", "name": "window frame", "move_cost": 8, "flags": ["WINDOW"] },
          { "type": "terrain", "id": "t_stairs_up", "name": "stairs", "move_cost": 2, "flags": ["TRANSPARENT", "GOES_UP"] },
          { "type": "terrain", "id": "t_gutter_downspout", "name": "downspout", "move_cost": 3, "flags": ["TRANSPARENT", "THIN_OBSTACLE", "CLIMBABLE"] },
          { "type": "furniture", "id": "f_chair", "name": "chair", "move_cost_mod": 1,
            "bash": { "str_min": 6, "str_max": 20, "sound": "smash!", "sound_fail": "whump." } },
          { "type": "furniture", "id": "f_fridge", "name": "refrigerator", "move_cost_mod": -1 }
        ]
        """;

    private const int Strength = 10;

    /// <summary>A 5×3 strip of grass on z0, with a z1 above (flat roof) for climbing.</summary>
    private static WorldMap Map()
    {
        var db = new ContentDatabase();
        db.LoadJsonText(Tiles, "tiles.json");
        return new WorldMap(TileTypeLoader.Load(db), 5, 3, 0, 1, z => z == 0 ? "t_grass" : "t_flat_roof");
    }

    private static readonly TilePos Player = new(1, 1, 0);
    private static readonly (int, int) Ahead = (2, 1);

    private static List<InteractionKind> Kinds(WorldMap map) =>
        Interactions.For(map, Player, Ahead, Strength).Select(i => i.Kind).ToList();

    [Fact]
    public void ClosedDoorOpensFirstThenSmashes()
    {
        var map = Map();
        map.Level(0).SetTerrain(2, 1, "t_door_c");
        var list = Interactions.For(map, Player, Ahead, Strength);
        Assert.Equal(new[] { InteractionKind.Open, InteractionKind.Smash }, list.Select(i => i.Kind));
        Assert.Equal("Open the closed door", list[0].Text);
        Assert.Equal("Smash the closed door (hard)", list[1].Text);
    }

    [Fact]
    public void OpenDoorClosesOrLetsYouThrough()
    {
        var map = Map();
        map.Level(0).SetTerrain(2, 1, "t_door_o");
        var list = Interactions.For(map, Player, Ahead, Strength);
        Assert.Equal(new[] { InteractionKind.Close, InteractionKind.WalkOnto }, list.Select(i => i.Kind));
        Assert.Equal("Walk through the open door", list[1].Text);
    }

    [Fact]
    public void LockedDoorOpensOnlyFromInside()
    {
        var map = Map();
        map.Level(0).SetTerrain(2, 1, "t_door_locked");
        Assert.DoesNotContain(InteractionKind.Open, Kinds(map));
        map.Level(0).SetTerrain(1, 1, "t_floor"); // now the player stands under a roof
        Assert.Contains(InteractionKind.Open, Kinds(map));
    }

    [Fact]
    public void WindowsAreSmashedNotOpenedByBumping()
    {
        var map = Map();
        map.Level(0).SetTerrain(2, 1, "t_window");
        var list = Interactions.For(map, Player, Ahead, Strength);
        Assert.Equal(new[] { InteractionKind.Smash }, list.Select(i => i.Kind));
        Assert.Equal("Smash the window", list[0].Text); // str 3..6 always breaks at 10
    }

    [Fact]
    public void WallsTooStrongToSmashOfferNothing()
    {
        var map = Map();
        map.Level(0).SetTerrain(2, 1, "t_wall");
        Assert.Empty(Kinds(map));
    }

    [Fact]
    public void FurnitureIsSteppedOverOrSmashed()
    {
        var map = Map();
        map.Level(0).SetFurniture(2, 1, "f_chair");
        var list = Interactions.For(map, Player, Ahead, Strength);
        Assert.Equal(new[] { InteractionKind.WalkOnto, InteractionKind.Smash }, list.Select(i => i.Kind));
        Assert.Equal("Climb over the chair (slow)", list[0].Text);
        Assert.Equal("Smash the chair (hard)", list[1].Text);

        map.Level(0).SetFurniture(2, 1, "f_fridge");
        Assert.Empty(Kinds(map)); // in the way, and nothing to do with it yet
    }

    [Fact]
    public void StairsAndDownspoutsOfferTheWayUp()
    {
        var map = Map();
        map.Level(0).SetTerrain(2, 1, "t_stairs_up");
        Assert.Equal("Go up the stairs", Interactions.For(map, Player, Ahead, Strength).Single().Text);

        map.Level(0).SetTerrain(2, 1, "t_gutter_downspout");
        var list = Interactions.For(map, Player, Ahead, Strength);
        Assert.Contains(list, i => i is { Kind: InteractionKind.ClimbUp, Text: "Climb up the downspout" });
    }

    [Fact]
    public void PlainGroundAndOutOfBoundsOfferNothing()
    {
        var map = Map();
        Assert.Empty(Kinds(map));
        Assert.Empty(Interactions.For(map, Player, (-1, 1), Strength));
    }

    [Fact]
    public void ListIsCappedAtMax()
    {
        var map = Map();
        map.Level(0).SetTerrain(2, 1, "t_door_o");
        Assert.Single(Interactions.For(map, Player, Ahead, Strength, max: 1));
    }

    [Theory]
    [InlineData(3, 6, 10, 1f)]
    [InlineData(8, 80, 10, 3f / 73)]
    [InlineData(60, 150, 10, 0f)]
    [InlineData(5, 14, 9, 0.5f)]
    public void SmashChanceFollowsCddaRoll(int min, int max, int strength, float expected) =>
        Assert.Equal(expected, Interactions.SmashChance(new BashInfo(min, max, null, null, "", ""), strength), 3);
}
