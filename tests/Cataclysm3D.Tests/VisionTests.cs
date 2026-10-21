using Cataclysm3D.Importer;
using Cataclysm3D.Importer.World;
using Cataclysm3D.Sim;
using Cataclysm3D.Sim.Vision;
using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Tests;

public class VisionTests
{
    private const string Tiles = """
        [
          { "type": "terrain", "id": "t_grass", "name": "grass", "move_cost": 2, "flags": ["TRANSPARENT", "FLAT"] },
          { "type": "terrain", "id": "t_floor", "name": "floor", "move_cost": 2, "roof": "t_flat_roof", "flags": ["TRANSPARENT", "INDOORS"] },
          { "type": "terrain", "id": "t_flat_roof", "name": "flat roof", "move_cost": 2, "flags": ["TRANSPARENT", "FLAT"] },
          { "type": "terrain", "id": "t_open_air", "name": "open air", "move_cost": 2, "flags": ["TRANSPARENT", "NO_FLOOR"] },
          { "type": "terrain", "id": "t_wall", "name": "wall", "move_cost": 0, "roof": "t_flat_roof", "flags": ["WALL"] },
          { "type": "terrain", "id": "t_window", "name": "window", "move_cost": 0, "roof": "t_flat_roof", "flags": ["TRANSPARENT", "WINDOW"] },
          { "type": "terrain", "id": "t_curtains", "name": "closed curtains", "move_cost": 0, "roof": "t_flat_roof", "flags": ["WINDOW"] },
          { "type": "terrain", "id": "t_door_c", "name": "closed door", "move_cost": 0, "open": "t_door_o", "roof": "t_flat_roof", "flags": ["DOOR"] },
          { "type": "terrain", "id": "t_door_o", "name": "open door", "move_cost": 2, "close": "t_door_c", "roof": "t_flat_roof", "flags": ["TRANSPARENT"] },
          { "type": "terrain", "id": "t_utility_light", "name": "utility light", "move_cost": 0, "light_emitted": 240, "flags": ["TRANSPARENT"] },
          { "type": "furniture", "id": "f_table", "name": "table", "move_cost_mod": 1, "flags": ["TRANSPARENT"] },
          { "type": "furniture", "id": "f_bookcase", "name": "bookcase", "move_cost_mod": -1 }
        ]
        """;

    private static readonly GameTime Noon = GameTime.At(0, 12);
    private static readonly GameTime NewMoonMidnight = GameTime.At(22, 0);

    /// <summary>A 21×11 field of grass on z0 under open air on z1.</summary>
    private static WorldMap Field()
    {
        var db = new ContentDatabase();
        db.LoadJsonText(Tiles, "tiles.json");
        return new WorldMap(TileTypeLoader.Load(db), 21, 11, 0, 1, z => z == 0 ? "t_grass" : "t_open_air");
    }

    /// <summary>
    /// Adds a house: walls around (x0..x1, y0..y1) with a floor inside, roofed on z1.
    /// <paramref name="westSide"/> is what goes in the middle of the west wall.
    /// </summary>
    private static (int X0, int Y0, int X1, int Y1) House(WorldMap map, string westSide = "t_wall")
    {
        const int x0 = 8, y0 = 2, x1 = 16, y1 = 8;
        var ground = map.Level(0);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            bool edge = x == x0 || x == x1 || y == y0 || y == y1;
            ground.SetTerrain(x, y, edge ? "t_wall" : "t_floor");
            map.Level(1).SetTerrain(x, y, "t_flat_roof");
        }
        ground.SetTerrain(x0, 5, westSide);
        return (x0, y0, x1, y1);
    }

    private static FieldOfView See(WorldMap map, TilePos from, GameTime now, float carried = 0)
    {
        var fov = new FieldOfView(map);
        fov.Update(from, carried, now);
        return fov;
    }

    private static readonly TilePos Outside = new(3, 5, 0);
    private static readonly TilePos Inside = new(12, 5, 0);

    [Fact]
    public void YouSeeAnOpenFieldByDaylight()
    {
        var map = Field();
        var fov = See(map, Outside, Noon);
        for (int y = 0; y < 11; y++)
        for (int x = 0; x < 21; x++)
            Assert.True(fov.IsVisible(new TilePos(x, y, 0)), $"({x}, {y})");
    }

    [Fact]
    public void WallsBlockSight()
    {
        var map = Field();
        House(map);
        var fov = See(map, Outside, Noon);
        Assert.True(fov.IsVisible(new TilePos(8, 5, 0)), "you see the wall itself");
        Assert.False(fov.IsVisible(Inside), "but not what's behind it");
        Assert.False(fov.IsVisible(new TilePos(18, 5, 0)), "nor the field beyond the house");
        Assert.True(fov.IsVisible(new TilePos(9, 0, 0)), "you see past the corner of the house");
    }

    [Fact]
    public void WindowsLetSightThrough()
    {
        var map = Field();
        House(map, "t_window");
        var fov = See(map, Outside, Noon);
        Assert.True(fov.IsVisible(new TilePos(8, 5, 0)));
        Assert.True(fov.IsVisible(new TilePos(9, 5, 0)), "daylight through the window lights the room behind it");
        Assert.True(fov.IsVisible(new TilePos(10, 5, 0)));
    }

    [Fact]
    public void DrawnCurtainsBlockSight()
    {
        var map = Field();
        House(map, "t_curtains");
        var fov = See(map, Outside, Noon);
        Assert.False(fov.IsVisible(new TilePos(9, 5, 0)));
    }

    [Fact]
    public void ClosedDoorsBlockSightAndOpenDoorsDont()
    {
        var map = Field();
        House(map, "t_door_c");
        var fov = new FieldOfView(map);
        fov.Update(Outside, 0, Noon);
        Assert.False(fov.IsVisible(new TilePos(9, 5, 0)));

        Assert.True(map.Level(0).TryOpen(8, 5));
        Assert.True(fov.Update(Outside, 0, Noon), "a tile change makes it look again");
        Assert.True(fov.IsVisible(new TilePos(9, 5, 0)));

        Assert.True(map.Level(0).TryClose(8, 5));
        fov.Update(Outside, 0, Noon);
        Assert.False(fov.IsVisible(new TilePos(9, 5, 0)));
        Assert.True(fov.IsRemembered(new TilePos(9, 5, 0)), "what you saw stays remembered");
    }

    [Fact]
    public void OpaqueFurnitureBlocksSightButTablesDont()
    {
        var map = Field();
        map.Level(0).SetFurniture(5, 5, "f_bookcase");
        map.Level(0).SetFurniture(3, 3, "f_table");
        var fov = See(map, Outside, Noon);
        Assert.False(fov.IsVisible(new TilePos(7, 5, 0)));
        Assert.True(fov.IsVisible(new TilePos(3, 0, 0)));
    }

    [Fact]
    public void RecomputesOnlyWhenSomethingChanged()
    {
        var map = Field();
        var fov = new FieldOfView(map);
        Assert.True(fov.Update(Outside, 0, Noon));
        Assert.False(fov.Update(Outside, 0, Noon.Plus(1)));
        Assert.True(fov.Update(Outside with { X = 4 }, 0, Noon.Plus(2)));
        Assert.True(fov.Update(Outside with { X = 4 }, LightLevels.Flashlight, Noon.Plus(3)));
    }

    [Fact]
    public void WindowlessInteriorsAreDarkButLitThroughAWindowByDay()
    {
        var map = Field();
        House(map, "t_window");
        var fov = See(map, Inside, Noon);
        Assert.True(fov.IsVisible(new TilePos(9, 5, 0)), "near the window");
        Assert.True(fov.LightAt(new TilePos(9, 5, 0)) > LightLevels.AmbientLit);
        Assert.True(fov.IsVisible(Outside), "out through the window");
        Assert.False(fov.IsVisible(new TilePos(15, 3, 0)), "the far corner of the room is dark");
    }

    [Fact]
    public void InteriorsAreDarkAtNightExceptRightNextToYou()
    {
        var map = Field();
        House(map, "t_window");
        var fov = See(map, Inside, NewMoonMidnight);
        Assert.True(fov.IsVisible(Inside with { X = 13 }), "you can feel your way around the tiles next to you");
        Assert.False(fov.IsVisible(Inside with { X = 14 }));
        Assert.False(fov.IsVisible(Outside), "a moonless night outside is too dark too");
    }

    [Fact]
    public void AFlashlightLightsTheRoom()
    {
        var map = Field();
        House(map);
        var fov = See(map, Inside, NewMoonMidnight, LightLevels.Flashlight);
        Assert.True(fov.IsVisible(new TilePos(15, 3, 0)));
        Assert.True(fov.IsVisible(new TilePos(16, 5, 0)), "the far wall");
        Assert.False(fov.IsVisible(Outside), "walls still block it");
    }

    [Fact]
    public void LightEmittingTerrainLightsTheDark()
    {
        var map = Field();
        House(map);
        map.Level(0).SetTerrain(14, 6, "t_utility_light");
        var fov = See(map, Inside, NewMoonMidnight);
        Assert.Single(fov.Light.Emitters);
        Assert.True(fov.IsVisible(new TilePos(15, 7, 0)));
        Assert.True(fov.IsVisible(new TilePos(15, 3, 0)));
    }

    [Fact]
    public void ALitRoomIsVisibleFromTheDarkOutsideThroughItsWindow()
    {
        var map = Field();
        House(map, "t_window");
        map.Level(0).SetTerrain(10, 5, "t_utility_light");
        var fov = See(map, Outside, NewMoonMidnight);
        Assert.False(fov.IsVisible(new TilePos(3, 9, 0)), "the dark field around you");
        Assert.True(fov.IsVisible(new TilePos(9, 5, 0)), "the lit room through the window");
    }

    [Fact]
    public void SunlitOutsideWallsDontShowFromADarkRoom()
    {
        var map = Field();
        House(map);
        var fov = See(map, Inside, Noon);
        Assert.False(fov.IsVisible(new TilePos(16, 5, 0)), "the east wall is sunlit outside, dark inside");
        Assert.True(fov.IsVisible(Inside with { X = 13 }), "but you see next to you");
    }

    [Fact]
    public void YouSeeTheGroundBelowFromAbove()
    {
        var map = Field();
        House(map);
        var fov = See(map, new TilePos(12, 5, 1), Noon);
        Assert.True(fov.IsVisible(new TilePos(3, 5, 1)), "open air around the roof");
        Assert.True(fov.IsVisible(new TilePos(3, 5, 0)), "the sunlit ground under it");
        Assert.False(fov.IsVisible(Inside), "not the room under the roof you stand on");
    }

    [Fact]
    public void LightIsRebuiltAsTheSunMovesButNotEveryTurn()
    {
        var map = Field();
        var light = new LightMap(map);
        Assert.True(light.Update(Noon));
        Assert.False(light.Update(Noon.Plus(60)));
        Assert.True(light.Update(NewMoonMidnight));
        Assert.InRange(light.At(Outside), 0, LightLevels.AmbientLow);
    }
}
