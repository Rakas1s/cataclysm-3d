using Cataclysm3D.Importer;
using Cataclysm3D.Importer.Creatures;
using Cataclysm3D.Importer.World;
using Cataclysm3D.Sim;
using Cataclysm3D.Sim.Creatures;
using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Tests;

public class MonsterTests
{
    private const string Tiles = """
        [
          { "type": "terrain", "id": "t_grass", "name": "grass", "move_cost": 2, "flags": ["TRANSPARENT", "FLAT"] },
          { "type": "terrain", "id": "t_floor", "name": "floor", "move_cost": 2, "roof": "t_flat_roof", "flags": ["TRANSPARENT", "INDOORS"] },
          { "type": "terrain", "id": "t_flat_roof", "name": "flat roof", "move_cost": 2, "flags": ["TRANSPARENT", "FLAT"] },
          { "type": "terrain", "id": "t_open_air", "name": "open air", "move_cost": 2, "flags": ["TRANSPARENT", "NO_FLOOR"] },
          { "type": "terrain", "id": "t_wall", "name": "wall", "move_cost": 0, "roof": "t_flat_roof", "flags": ["WALL"] },
          { "type": "terrain", "id": "t_door_c", "name": "closed door", "move_cost": 0, "open": "t_door_o", "roof": "t_flat_roof", "flags": ["DOOR"],
            "bash": { "str_min": 8, "str_max": 80, "ter_set": "t_door_b", "sound": "smash!", "sound_fail": "whump!" } },
          { "type": "terrain", "id": "t_door_o", "name": "open door", "move_cost": 2, "close": "t_door_c", "roof": "t_flat_roof", "flags": ["TRANSPARENT"] },
          { "type": "terrain", "id": "t_door_b", "name": "broken door", "move_cost": 2, "roof": "t_flat_roof", "flags": ["TRANSPARENT"] },
          { "type": "terrain", "id": "t_window", "name": "window", "move_cost": 0, "roof": "t_flat_roof", "flags": ["TRANSPARENT", "WINDOW"],
            "bash": { "str_min": 3, "str_max": 6, "ter_set": "t_window_frame", "sound": "glass breaking!", "sound_fail": "whack!" } },
          { "type": "terrain", "id": "t_window_frame", "name": "window frame", "move_cost": 8, "roof": "t_flat_roof", "flags": ["TRANSPARENT", "WINDOW"] }
        ]
        """;

    // Trimmed from CDDA 0.I's zed-classic.json and zombies.json.
    private const string Monsters = """
        [
          { "abstract": "mon_zombie_base", "type": "MONSTER", "name": { "str": "zombie" }, "species": ["ZOMBIE", "HUMAN"],
            "hp": 80, "speed": 70, "symbol": "Z", "color": "light_green", "aggression": 100, "morale": 100,
            "melee_skill": 4, "melee_dice": 2, "melee_dice_sides": 3, "melee_damage": [{ "damage_type": "cut", "amount": 0 }],
            "vision_night": 3, "flags": ["SEES", "HEARS", "STUMBLES", "BASHES", "GROUP_BASH"], "armor": { "electric": 1 } },
          { "id": "mon_zombie", "type": "MONSTER", "copy-from": "mon_zombie_base" },
          { "id": "mon_zombie_tough", "type": "MONSTER", "copy-from": "mon_zombie_base", "name": { "str": "tough zombie" },
            "hp": 100, "speed": 85, "melee_dice": 3, "melee_damage": [{ "damage_type": "cut", "amount": 1 }], "dodge": 1,
            "armor": { "bash": 2, "cut": 1 } },
          { "id": "mon_zombie_cop", "type": "MONSTER", "copy-from": "mon_zombie", "name": { "str": "zombie cop" },
            "melee_skill": 6, "melee_dice_sides": 4, "vision_day": 30, "armor": { "bash": 6, "cut": 6, "stab": 6 } },
          { "id": "mon_civilian", "type": "MONSTER", "name": "civilian", "species": ["HUMAN"], "hp": 50, "speed": 100, "aggression": 0 },
          { "type": "monstergroup", "id": "GROUP_CIVILIANS", "monsters": [{ "monster": "mon_civilian", "weight": 100 }] },
          { "type": "monstergroup", "id": "GROUP_ZOMBIE", "monsters": [
            { "group": "GROUP_CIVILIANS", "weight": 500 },
            { "monster": "mon_zombie", "weight": 400 },
            { "monster": "mon_zombie_tough", "weight": 100 },
            { "monster": "mon_zombie_survivor", "weight": 1000, "starts": "120 days" } ] }
        ]
        """;

    private static readonly GameTime Noon = GameTime.At(0, 12);
    private static readonly GameTime NewMoonMidnight = GameTime.At(22, 0);

    private static MonsterRegistry Registry()
    {
        var db = new ContentDatabase();
        db.LoadJsonText(Monsters, "monsters.json");
        return MonsterLoader.Registry(db);
    }

    private static MonsterType Zombie => Registry().Type("mon_zombie")!;

    /// <summary>A 30×15 field of grass on z0 under open air.</summary>
    private static WorldMap Field()
    {
        var db = new ContentDatabase();
        db.LoadJsonText(Tiles, "tiles.json");
        return new WorldMap(TileTypeLoader.Load(db), 30, 15, 0, 1, z => z == 0 ? "t_grass" : "t_open_air");
    }

    /// <summary>A walled room (x 12..20, y 3..11) with a floor and a roof; <paramref name="westSide"/> goes mid west wall.</summary>
    private static void House(WorldMap map, string westSide)
    {
        for (int y = 3; y <= 11; y++)
        for (int x = 12; x <= 20; x++)
        {
            bool edge = x == 12 || x == 20 || y == 3 || y == 11;
            map.Level(0).SetTerrain(x, y, edge ? "t_wall" : "t_floor");
            map.Level(1).SetTerrain(x, y, "t_flat_roof");
        }
        map.Level(0).SetTerrain(12, 7, westSide);
    }

    private static GameWorld World(WorldMap map, TilePos player, GameTime? time = null, int seed = 1) =>
        new(map, new PlayerActor(), player, time ?? Noon, seed);

    // ---- import ----------------------------------------------------------------------------

    [Fact]
    public void ImportsAZombieThroughCopyFrom()
    {
        var z = Zombie;
        Assert.Equal("zombie", z.Name);
        Assert.Equal(80, z.Hp);
        Assert.Equal(70, z.Speed);
        Assert.Equal(4, z.MeleeSkill);
        Assert.Equal((2, 3), (z.MeleeDice, z.MeleeDiceSides));
        Assert.Empty(z.MeleeDamage); // 0 cut adds nothing
        Assert.Equal(40, z.VisionDay); // CDDA's default
        Assert.Equal(3, z.VisionNight);
        Assert.Equal(6, z.BashSkill); // dice × sides
        Assert.True(z.IsZombie && z.IsHostile && z.Has("BASHES"));
        Assert.Equal(default, z.Armor); // electric only

        var cop = Registry().Type("mon_zombie_cop")!;
        Assert.Equal("zombie cop", cop.Name);
        Assert.Equal(80, cop.Hp); // inherited two levels down
        Assert.Equal(new Armor(6, 6, 6), cop.Armor);
        Assert.Equal(30, cop.VisionDay);
        Assert.Equal(8, cop.BashSkill);

        var tough = Registry().Type("mon_zombie_tough")!;
        Assert.Equal(new[] { (DamageType.Cut, 1) }, tough.MeleeDamage);
        Assert.Equal(1, tough.Dodge);
    }

    [Fact]
    public void GroupsRollByWeightAndSkipLateAndFilteredEntries()
    {
        var registry = Registry();
        var rng = new Random(3);
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < 1000; i++)
        {
            var t = registry.Pick("GROUP_ZOMBIE", rng, t => t.IsZombie)!;
            counts[t.Id] = counts.GetValueOrDefault(t.Id) + 1;
        }
        Assert.Equal(new[] { "mon_zombie", "mon_zombie_tough" }, counts.Keys.Order());
        Assert.InRange(counts["mon_zombie"], 720, 880); // 400 : 100
        Assert.Equal("mon_civilian", registry.Pick("GROUP_CIVILIANS", rng)!.Id);
    }

    [Fact]
    public void MapgenRollsPlaceMonsters()
    {
        var db = new ContentDatabase();
        db.LoadJsonText(Tiles, "tiles.json");
        db.LoadJsonText("""
            [{ "type": "mapgen", "om_terrain": "zhouse", "object": {
                 "fill_ter": "t_floor", "rows": ["....", "....", "....", "...."],
                 "place_monsters": [{ "monster": "GROUP_ZOMBIE", "x": [1, 2], "y": 3, "chance": 1, "repeat": 2 }],
                 "place_monster": [{ "monster": "mon_zombie_cop", "x": 0, "y": 0, "chance": 100 },
                                   { "monster": "mon_zombie", "x": 0, "y": 0, "chance": 0 }] } }]
            """, "mapgen.json");
        var registry = TileTypeLoader.Load(db);
        var result = new MapgenGenerator(db, registry, new MapgenLibrary(db)).Generate("zhouse", 1);
        Assert.Equal(3, result.Spawns.Count);
        Assert.Equal(2, result.Spawns.Count(s => s is { Id: "GROUP_ZOMBIE", IsGroup: true, Y: 3, X: >= 1 and <= 2 }));
        Assert.Contains(new MonsterSpawn("mon_zombie_cop", false, 0, 0), result.Spawns);
    }

    // ---- noticing ----------------------------------------------------------------------------

    [Fact]
    public void SightRangeFollowsTheLightOnTheTarget()
    {
        var z = Zombie;
        Assert.Equal(40, Sim.Vision.MonsterSight.Range(z, 100));
        Assert.Equal(3, Sim.Vision.MonsterSight.Range(z, 0));
        Assert.Equal(21, Sim.Vision.MonsterSight.Range(z, 50));
    }

    [Fact]
    public void AZombieNoticesYouInDaylight()
    {
        var game = World(Field(), new TilePos(3, 7, 0));
        var z = game.Spawn(Zombie, new TilePos(25, 7, 0));
        game.Wait(1);
        Assert.True(z.IsHunting);
    }

    [Fact]
    public void ButNotThroughAWall()
    {
        var map = Field();
        House(map, "t_wall");
        var game = World(map, new TilePos(16, 7, 0));
        var z = game.Spawn(Zombie, new TilePos(3, 7, 0));
        game.Wait(1);
        Assert.False(z.IsHunting);
    }

    [Fact]
    public void AtNightItOnlySeesYouUpCloseUnlessYouCarryALight()
    {
        var game = World(Field(), new TilePos(3, 7, 0), NewMoonMidnight);
        var z = game.Spawn(Zombie, new TilePos(13, 7, 0));
        game.Wait(1);
        Assert.False(z.IsHunting, "10 tiles off on a moonless night");

        game.Player.CarriedLight = Sim.Vision.LightLevels.Flashlight;
        game.Wait(1);
        Assert.True(z.IsHunting, "your flashlight gives you away");
    }

    // ---- chasing and doors -----------------------------------------------------------------

    [Fact]
    public void AZombieWalksUpToYouAndAttacks()
    {
        var game = World(Field(), new TilePos(3, 7, 0));
        var z = game.Spawn(Zombie, new TilePos(10, 7, 0));
        game.Wait(15);
        Assert.Equal(1, Math.Max(Math.Abs(z.Pos.X - 3), Math.Abs(z.Pos.Y - 7)));
        Assert.Contains(game.Messages, m => m.Text.StartsWith("The zombie hits your") || m.Text == "The zombie misses you.");
    }

    [Fact]
    public void AZombieSmashesAWindowToGetIn()
    {
        var map = Field();
        House(map, "t_window");
        // Indoors is dimmer than outside, so the zombie only sees you from closer by.
        var game = World(map, new TilePos(14, 7, 0));
        var z = game.Spawn(Zombie, new TilePos(8, 7, 0));
        game.Wait(30);
        Assert.Equal("t_window_frame", map.TerrainAt(new TilePos(12, 7, 0)).Id);
        Assert.True(z.Pos.X > 12, $"it climbed in (at {z.Pos})");
        Assert.Contains(game.Messages, m => m.Text.Contains("smashes the window"));
    }

    [Fact]
    public void ALoneZombieCantBreakADoorButAPackCan()
    {
        var map = Field();
        House(map, "t_door_c");
        // The zombie can't see through the door; it remembers where you went.
        var game = World(map, new TilePos(15, 7, 0));
        var lone = game.Spawn(Zombie, new TilePos(11, 7, 0));
        lone.Notice(new TilePos(15, 7, 0));
        game.Wait(30);
        Assert.Equal("t_door_c", map.TerrainAt(new TilePos(12, 7, 0)).Id);
        Assert.True(lone.Pos.X < 12);

        // Bash strength 6 never beats the door's minimum of 8; three side by side (18) do.
        var a = game.Spawn(Zombie, new TilePos(11, 6, 0));
        var b = game.Spawn(Zombie, new TilePos(11, 8, 0));
        // Path planning goes by a lone zombie's strength, so have them smash the door directly.
        for (int i = 0; i < 400 && map.TerrainAt(new TilePos(12, 7, 0)).Id == "t_door_c"; i++)
            game.MonsterBash(a, new TilePos(12, 7, 0));
        Assert.Equal("t_door_b", map.TerrainAt(new TilePos(12, 7, 0)).Id);
    }

    [Fact]
    public void AZombieWalksThroughAnOpenDoor()
    {
        var map = Field();
        House(map, "t_door_o");
        var game = World(map, new TilePos(18, 7, 0));
        var z = game.Spawn(Zombie, new TilePos(5, 7, 0));
        z.Notice(game.Player.Pos);
        game.Wait(40);
        Assert.Equal(1, Math.Max(Math.Abs(z.Pos.X - 18), Math.Abs(z.Pos.Y - 7)));
    }

    // ---- melee ---------------------------------------------------------------------------

    [Fact]
    public void HitChancesMatchCddasRolls()
    {
        var player = new PlayerActor();
        // A survivor (Dex 8: accuracy 2) against a zombie's dodge of 0, and a zombie (skill 4)
        // against the survivor's dodge of 2: both about two in three.
        Assert.InRange(Melee.HitChance(Melee.PlayerAccuracy(player), 0), 0.64, 0.68);
        Assert.InRange(Melee.HitChance(Zombie.MeleeSkill, Melee.PlayerDodge(player)), 0.64, 0.68);

        var rng = new Random(5);
        int hits = Enumerable.Range(0, 4000).Count(_ => Melee.HitRoll(rng, 2) > 0);
        Assert.InRange(hits / 4000.0, 0.62, 0.70);
    }

    [Fact]
    public void DamageRollsAndArmor()
    {
        var rng = new Random(9);
        var player = new PlayerActor();
        // CDDA's bare-handed bash for Str 8: U(4, 8), rolled down to 40-100 %, x 0.8.
        var punches = Enumerable.Range(0, 2000).Select(_ => Melee.AfterArmor(Melee.PlayerDamage(player, rng), default(Armor))).ToList();
        Assert.Equal(1, punches.Min());
        Assert.Equal(6, punches.Max());

        var bites = Enumerable.Range(0, 500).Select(_ => Melee.AfterArmor(Melee.MonsterDamage(Zombie, rng), default(Armor))).ToList();
        Assert.Equal(2, bites.Min());
        Assert.Equal(6, bites.Max());

        Assert.Equal(1, Melee.AfterArmor(new[] { (DamageType.Bash, 5), (DamageType.Cut, 1) }, new Armor(4, 2, 0)));
        Assert.Equal(0, Melee.AfterArmor(new[] { (DamageType.Bash, 5) }, new Armor(6, 6, 6)));
    }

    [Fact]
    public void YouCanPunchAZombieToDeathAndItLeavesACorpse()
    {
        var game = World(Field(), new TilePos(3, 7, 0));
        var z = game.Spawn(Zombie, new TilePos(4, 7, 0));
        for (int i = 0; i < 200 && !z.IsDead && !game.Player.IsDead; i++)
            game.PlayerAttack(z);
        Assert.True(z.IsDead);
        Assert.Empty(game.Monsters);
        Assert.Equal(new TilePos(4, 7, 0), Assert.Single(game.Corpses).Pos);
        Assert.Contains(game.Messages, m => m.Text == "You kill the zombie!");
        Assert.Contains(game.DrainEvents(), e => e is MonsterDiedEvent);
    }

    [Fact]
    public void ZombiesKillYouWhenYourHeadOrTorsoGivesOut()
    {
        var game = World(Field(), new TilePos(7, 7, 0));
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1), (1, 1) })
            game.Spawn(Zombie, new TilePos(7 + dx, 7 + dy, 0));
        for (int i = 0; i < 2000 && !game.Player.IsDead; i++)
            game.Wait(1);
        Assert.True(game.Player.IsDead);
        var body = game.Player.Body;
        Assert.True(body.Hp("torso") == 0 || body.Hp("head") == 0);
        Assert.False(game.Scheduler.PlayerCanAct);
        Assert.Equal("You die...", game.Messages[^1].Text);
    }

    [Fact]
    public void BlowsLandOnBodyPartsByHitSize()
    {
        var body = new Body(BodyPartType.Defaults);
        var rng = new Random(2);
        var counts = new int[body.Parts.Count];
        for (int i = 0; i < 10000; i++)
            counts[body.RandomPart(rng)]++;
        int torso = body.IndexOf("torso"), head = body.IndexOf("head");
        Assert.InRange(counts[torso] / 10000.0, 0.33, 0.40);
        Assert.InRange(counts[head] / 10000.0, 0.03, 0.07);
    }

    // ---- starting outfit ---------------------------------------------------------------------

    [Fact]
    public void YouStartInYourProfessionsClothesInnermostFirstWithTheRestInYourPockets()
    {
        var db = new ContentDatabase();
        db.LoadJsonText("""
            [
              { "type": "profession", "id": "unemployed", "items": {
                "both": { "entries": [{ "item": "jeans" }, { "item": "longshirt" }, { "item": "mbag" }, { "item": "pockknife" }, { "group": "starter_wallet" }] },
                "male": { "entries": [{ "item": "boxer_shorts" }] }, "female": { "entries": [{ "item": "bra" }] } } },
              { "type": "ITEM", "id": "jeans", "name": { "str_sp": "jeans" }, "armor": [{ "covers": ["leg_l", "leg_r"] }] },
              { "type": "ITEM", "id": "longshirt", "name": { "str": "long-sleeved shirt" }, "flags": ["SKINTIGHT"], "armor": [{ "covers": ["torso"] }] },
              { "type": "ITEM", "id": "mbag", "name": { "str": "messenger bag" }, "flags": ["BELTED"], "armor": [{ "covers": ["torso"] }],
                "pocket_data": [{ "pocket_type": "CONTAINER", "max_contains_volume": "5 L", "max_contains_weight": "5 kg" }] },
              { "type": "ITEM", "id": "boxer_shorts", "name": { "str": "boxer shorts" }, "flags": ["SKINTIGHT"], "armor": [{ "covers": ["leg_l", "leg_r"] }] },
              { "type": "ITEM", "id": "pockknife", "name": { "str": "pocket knife" }, "volume": "100 ml", "weight": "100 g" },
              { "type": "item_group", "id": "starter_wallet", "subtype": "collection", "items": ["pockknife"] }
            ]
            """, "outfit.json");
        var items = Cataclysm3D.Importer.Items.ItemLoader.Registry(db);
        var kit = Outfit.Starting(db, items, "unemployed", male: true, new Random(1));
        Assert.Equal(new[] { "jeans", "longshirt", "mbag", "pockknife", "pockknife", "boxer_shorts" }, kit.Select(c => c.Id));
        var player = new PlayerActor();
        Assert.Empty(Outfit.Dress(player.Inventory, kit));
        Assert.Equal(new[] { "longshirt", "boxer_shorts", "jeans", "mbag" }, player.Worn.Select(c => c.Id));
        Assert.Equal("jeans", player.Worn[2].Type.Name);
        Assert.Equal(2, player.Inventory.CarriedItems.Count(i => i.Id == "pockknife"));
        Assert.DoesNotContain(Outfit.Starting(db, items, "unemployed", male: false, new Random(1)), c => c.Id == "boxer_shorts");
    }

    [Fact]
    public void HairIsARandomStyleTraitInANaturalColor()
    {
        var db = new ContentDatabase();
        db.LoadJsonText("""
            [
              { "type": "mutation", "id": "hair_bald", "types": ["hair_style"] },
              { "type": "mutation", "id": "hair_medium", "types": ["hair_style"],
                "variants": [{ "id": "black" }, { "id": "blue" }, { "id": "brown" }] },
              { "type": "mutation", "id": "eye_color", "types": ["eye_color"], "variants": [{ "id": "brown" }] }
            ]
            """, "hair.json");
        var rng = new Random(4);
        for (int i = 0; i < 50; i++)
        {
            var hair = Outfit.RandomHair(db, rng)!;
            Assert.Equal("hair_medium", hair.Style);
            Assert.Contains(hair.Color, new[] { "black", "brown" });
        }
    }

    // ---- waiting -------------------------------------------------------------------------

    [Fact]
    public void WaitingStopsWhenAZombieComesIntoView()
    {
        var map = Field();
        House(map, "t_wall");
        // You're outside, south of the house; a zombie behind it wanders into view.
        var game = World(map, new TilePos(16, 14, 0));
        var z = game.Spawn(Zombie, new TilePos(16, 1, 0));
        z.Notice(new TilePos(25, 13, 0));
        long before = game.Scheduler.Turn;
        bool interrupted = game.Wait(30 * 60);
        Assert.True(interrupted);
        Assert.True(game.Scheduler.Turn - before < 60);
        Assert.Contains(game.Messages, m => m.Text == "You see the zombie! You stop waiting.");
    }
}
