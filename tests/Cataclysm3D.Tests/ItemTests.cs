using Cataclysm3D.Importer;
using Cataclysm3D.Importer.Items;
using Cataclysm3D.Importer.World;
using Cataclysm3D.Sim;
using Cataclysm3D.Sim.Creatures;
using Cataclysm3D.Sim.Items;
using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Tests;

public class ItemTests
{
    // Trimmed from CDDA 0.I's item, material and item group JSON.
    private const string Items = """
        [
          { "type": "material", "id": "cotton", "soft": true, "resist": { "bash": 1, "cut": 1 } },
          { "type": "material", "id": "denim", "soft": true, "resist": { "bash": 1.5, "cut": 1.5, "stab": 1.2 } },
          { "type": "material", "id": "rubber", "resist": { "bash": 2, "cut": 1 } },
          { "type": "material", "id": "wood", "resist": { "bash": 4, "cut": 4 } },
          { "type": "material", "id": "nylon", "soft": true, "resist": { "bash": 1, "cut": 1 } },

          { "abstract": "pants_base", "type": "ITEM", "subtypes": ["ARMOR"], "weight": "500 g", "volume": "2 L", "material": ["denim"],
            "material_thickness": 0.5, "warmth": 10,
            "armor": [{ "encumbrance": [7, 11], "coverage": 100, "covers": ["leg_l", "leg_r"] }],
            "pocket_data": [{ "pocket_type": "CONTAINER", "max_contains_volume": "1250 ml", "max_contains_weight": "4 kg", "max_item_length": "19 cm" },
                            { "pocket_type": "CONTAINER", "max_contains_volume": "1250 ml", "max_contains_weight": "4 kg", "max_item_length": "19 cm" }] },
          { "id": "jeans", "type": "ITEM", "copy-from": "pants_base", "name": { "str_sp": "jeans" }, "relative": { "weight": "100 g" } },
          { "id": "tshirt", "type": "ITEM", "subtypes": ["ARMOR"], "name": { "str": "t-shirt" }, "weight": "110 g", "volume": "500 ml",
            "material": ["cotton"], "material_thickness": 0.1, "warmth": 5, "flags": ["SKINTIGHT"],
            "armor": [{ "encumbrance": 3, "coverage": 90, "covers": ["torso"] }] },
          { "id": "longshirt", "type": "ITEM", "copy-from": "tshirt", "name": { "str": "long-sleeved shirt" }, "flags": [],
            "armor": [{ "encumbrance": 5, "coverage": 90, "covers": ["torso", "arm_l", "arm_r"] }] },
          { "id": "socks", "type": "ITEM", "subtypes": ["ARMOR"], "name": { "str_sp": "socks (pair)" }, "weight": "32 g", "volume": "100 ml",
            "material": ["cotton"], "flags": ["SKINTIGHT"], "armor": [{ "coverage": 100, "covers": ["foot_l", "foot_r"] }] },
          { "id": "sneakers", "type": "ITEM", "subtypes": ["ARMOR"], "name": { "str_sp": "sneakers (pair)" }, "weight": "454 g", "volume": "1500 ml",
            "material": ["cotton", "rubber"], "warmth": 20,
            "armor": [
              { "covers": ["foot_l", "foot_r"], "material": [{ "type": "cotton", "covered_by_mat": 100, "thickness": 1 }], "encumbrance": 4, "coverage": 100 },
              { "covers": ["foot_l", "foot_r"], "material": [{ "type": "rubber", "covered_by_mat": 100, "thickness": 4 }], "coverage": 100 } ] },
          { "id": "boots", "type": "ITEM", "copy-from": "sneakers", "name": { "str_sp": "boots (pair)" } },
          { "id": "backpack", "type": "ITEM", "subtypes": ["ARMOR"], "name": { "str": "backpack" }, "weight": "633 g", "volume": "2 L",
            "material": ["nylon"], "flags": ["BELTED"], "armor": [{ "encumbrance": [2, 25], "coverage": 60, "covers": ["torso"] }],
            "pocket_data": [{ "pocket_type": "CONTAINER", "max_contains_volume": "25 L", "max_contains_weight": "30 kg", "max_item_length": "40 cm", "moves": 300 }] },
          { "id": "wristwatch", "type": "ITEM", "subtypes": ["ARMOR"], "name": { "str": "wrist watch" }, "max_worn": 1, "weight": "80 g",
            "volume": "50 ml", "armor": [{ "coverage": 5, "covers": ["hand_l"] }] },
          { "type": "ITEM", "id": "bat", "name": { "str": "baseball bat" }, "weight": "1133 g", "longest_side": "90 cm",
            "to_hit": { "grip": "weapon", "length": "long", "surface": "any", "balance": "good" }, "material": ["wood"],
            "volume": "1750 ml", "melee_damage": { "bash": 22 } },
          { "type": "ITEM", "id": "pipe", "name": { "str": "pipe" }, "weight": "600 g", "volume": "370 ml", "longest_side": "60 cm",
            "to_hit": { "grip": "solid", "length": "short", "surface": "any", "balance": "neutral" }, "melee_damage": { "bash": 10 } },
          { "type": "ITEM", "id": "rock", "name": { "str": "rock" }, "weight": "657 g", "volume": "250 ml", "melee_damage": { "bash": 7 } },
          { "type": "ITEM", "id": "knife_old", "name": { "str": "old knife" }, "weight": 100, "volume": 1, "to_hit": 1, "cutting": 8 },
          { "id": "bottle_plastic", "type": "ITEM", "name": { "str": "plastic bottle" }, "weight": "13 g", "volume": "510 ml", "longest_side": "18 cm",
            "pocket_data": [{ "pocket_type": "CONTAINER", "watertight": true, "rigid": true, "max_contains_volume": "500 ml", "max_contains_weight": "1250 g" }] },
          { "id": "water_clean", "type": "ITEM", "subtypes": ["COMESTIBLE"], "comestible_type": "DRINK", "name": { "str_sp": "clean water" },
            "weight": "250 g", "volume": "250 ml", "charges": 1, "phase": "liquid", "quench": 50, "container": "bottle_plastic" },
          { "type": "ITEM", "subtypes": ["COMESTIBLE"], "id": "apple", "name": { "str": "apple" }, "weight": "182 g", "volume": "232 ml",
            "comestible_type": "FOOD", "quench": 3, "calories": 94 },
          { "type": "ITEM", "subtypes": ["COMESTIBLE"], "id": "cracker", "name": { "str": "cracker" }, "weight": "10 g", "volume": "20 ml",
            "comestible_type": "FOOD", "calories": 30, "charges": 4 },
          { "type": "ITEM", "id": "pillow", "name": { "str": "pillow" }, "weight": "500 g", "volume": "3 L", "flags": ["SLEEP_AID"] },

          { "type": "item_group", "id": "dresser", "subtype": "collection", "items": [
              { "item": "socks", "count": [2, 3] }, { "item": "tshirt", "prob": 50 }, { "group": "pants", "prob": 100 } ] },
          { "type": "item_group", "id": "pants", "items": [["jeans", 3], ["tshirt", 1]] },
          { "type": "item_group", "id": "snacks", "subtype": "distribution", "entries": [
              { "item": "apple", "prob": 75 }, { "item": "cracker", "count": 2, "prob": 25 } ] },
          { "type": "item_group", "id": "nested", "subtype": "collection", "entries": [
              { "distribution": [{ "item": "bat" }, { "item": "pipe" }] }, { "item": "water_clean" } ] },
          { "type": "item_group", "id": "lunchbag", "subtype": "collection", "container-item": "backpack", "items": ["apple", "cracker"] }
        ]
        """;

    private static (ContentDatabase Db, ItemRegistry Registry) Load()
    {
        var db = new ContentDatabase();
        db.LoadJsonText(Items, "items.json");
        return (db, ItemLoader.Registry(db));
    }

    private static ItemRegistry Registry() => Load().Registry;

    private static Item Make(ItemRegistry r, string id) => r.Create(id) ?? throw new KeyNotFoundException(id);

    // ---- loading ---------------------------------------------------------------------

    [Fact]
    public void LoadsArmorThroughCopyFromWithUnitsAndPockets()
    {
        var r = Registry();
        var jeans = r.Type("jeans")!;
        Assert.Equal("jeans", jeans.Name);
        Assert.Equal(600_000, jeans.WeightMg); // 500 g + relative 100 g
        Assert.Equal(2000, jeans.VolumeMl);
        Assert.Equal(WornLayer.Normal, jeans.Armor!.Layer);
        Assert.Equal(new[] { "leg_l", "leg_r" }, jeans.Armor.CoveredParts);
        Assert.Equal((7, 11), (jeans.Armor.Portions[0].Encumbrance, jeans.Armor.Portions[0].MaxEncumbrance));
        Assert.Equal(10, jeans.Armor.Warmth);
        Assert.Equal(2, jeans.Pockets.Count);
        Assert.Equal(190, jeans.Pockets[0].MaxLengthMm);
        Assert.Equal(0.75f, jeans.Armor.Protection.Bash, 3); // 1.5 per mm × 0.5 mm
        Assert.Equal(0.6f, jeans.Armor.Protection.Stab, 3);
        Assert.False(jeans.Armor.Portions[0].Rigid); // denim is soft

        Assert.Equal(WornLayer.Skintight, r.Type("tshirt")!.Armor!.Layer);
        Assert.Equal(WornLayer.Normal, r.Type("longshirt")!.Armor!.Layer); // its own flags replace the parent's
        Assert.Equal(WornLayer.Belted, r.Type("backpack")!.Armor!.Layer);
        Assert.Equal(1, r.Type("wristwatch")!.MaxWorn);
    }

    [Fact]
    public void SneakerPortionsMergeAndTheirRubberSoleIsRigid()
    {
        var sneakers = Registry().Type("sneakers")!.Armor!;
        var portion = Assert.Single(sneakers.Portions);
        Assert.Equal(4, portion.Encumbrance);
        Assert.True(portion.Rigid);
        Assert.Equal(1, sneakers.Protection.Bash, 3); // the cotton upper (1 × 1 mm), where most blows land
    }

    [Fact]
    public void ToHitObjectsAndMeleeDamage()
    {
        var r = Registry();
        Assert.Equal(3, r.Type("bat")!.ToHit);  // -2 + weapon 2 + long 2 + any 0 + good 1
        Assert.Equal(0, r.Type("pipe")!.ToHit); // -2 + solid 1 + short 1
        Assert.Equal(-2, r.Type("rock")!.ToHit); // no to_hit at all
        Assert.Equal(22, r.Type("bat")!.Bash);
        var old = r.Type("knife_old")!;
        Assert.Equal((1, 8, 100_000L, 250L), (old.ToHit, old.Cut, old.WeightMg, old.VolumeMl)); // legacy numbers
    }

    [Fact]
    public void FoodAndDrinkSpawnInTheirContainers()
    {
        var r = Registry();
        var water = Make(r, "water_clean");
        Assert.Equal("bottle_plastic", water.Id);
        var inside = Assert.Single(water.Contents);
        Assert.Equal(2, inside.Charges); // a 500 ml bottle holds two 250 ml servings
        Assert.Equal("plastic bottle of clean water (2)", water.DisplayName);
        Assert.Equal(510, water.VolumeMl); // rigid: same size full or empty
        Assert.Equal(13_000 + 500_000, water.WeightMg);
        Assert.Same(inside, water.Edible);

        var apple = Make(r, "apple");
        Assert.Equal(94, apple.Type.Comestible!.Calories);
        Assert.Empty(apple.Contents);
    }

    // ---- item groups ----------------------------------------------------------------------

    [Fact]
    public void CollectionsRollEachEntryAndDistributionsPickOne()
    {
        var r = Registry();
        var rng = new Random(7);
        int shirts = 0, jeans = 0, socks = 0;
        const int Rolls = 2000;
        for (int i = 0; i < Rolls; i++)
        {
            var items = r.Roll("dresser", rng);
            socks += items.Count(x => x.Id == "socks");
            shirts += items.Count(x => x.Id == "tshirt");
            jeans += items.Count(x => x.Id == "jeans");
            Assert.InRange(items.Count(x => x.Id == "socks"), 2, 3);
        }
        Assert.InRange(socks / (double)Rolls, 2.4, 2.6);
        // 50 % t-shirts from the collection, plus the "pants" distribution's 1 in 4.
        Assert.InRange(shirts / (double)Rolls, 0.70, 0.80);
        Assert.InRange(jeans / (double)Rolls, 0.70, 0.80);

        int apples = 0;
        for (int i = 0; i < Rolls; i++)
        {
            var snack = r.Roll("snacks", rng);
            var one = Assert.Single(snack);
            if (one.Id == "apple")
                apples++;
            else
                Assert.Equal(8, one.Charges); // two packs of 4 fold into one stack
        }
        Assert.InRange(apples / (double)Rolls, 0.71, 0.79);
    }

    [Fact]
    public void InlineGroupsAndGroupContainers()
    {
        var r = Registry();
        var items = r.Roll("nested", new Random(1));
        Assert.Equal(2, items.Count);
        Assert.Contains(items[0].Id, new[] { "bat", "pipe" });
        Assert.Equal("bottle_plastic", items[1].Id);

        var bag = Assert.Single(r.Roll("lunchbag", new Random(1)));
        Assert.Equal("backpack", bag.Id);
        Assert.Equal(new[] { "apple", "cracker" }, bag.Contents.Select(c => c.Id));
    }

    [Fact]
    public void MapgenPlacesItemsFromSymbolTablesAndLootLists()
    {
        var (db, items) = Load();
        db.LoadJsonText("""
            [
              { "type": "terrain", "id": "t_floor", "name": "floor", "move_cost": 2, "flags": ["TRANSPARENT"] },
              { "type": "furniture", "id": "f_dresser", "name": "dresser", "move_cost_mod": -1, "flags": ["CONTAINER"] },
              { "type": "palette", "id": "home", "terrain": { ".": "t_floor", "d": "t_floor" }, "furniture": { "d": "f_dresser" },
                "items": { "d": { "item": "dresser", "chance": 100 } } },
              { "type": "mapgen", "om_terrain": "lootbox", "object": {
                  "fill_ter": "t_floor", "palettes": ["home"],
                  "rows": ["d..", "...", "..."],
                  "item": { ".": { "item": "apple", "chance": 0 } },
                  "place_loot": [{ "item": "bat", "x": 2, "y": 2 }, { "group": "snacks", "x": 1, "y": 0, "chance": 0 }],
                  "place_items": [{ "item": "nested", "x": [0, 2], "y": 1, "chance": 100 }] } }
            ]
            """, "mapgen.json");
        var gen = new MapgenGenerator(db, TileTypeLoader.Load(db), new MapgenLibrary(db)) { Items = items };
        var result = gen.Generate("lootbox", 3);
        Assert.Contains(result.Items, p => (p.X, p.Y) == (0, 0) && p.Item.Id == "socks");
        Assert.Contains(result.Items, p => (p.X, p.Y) == (2, 2) && p.Item.Id == "bat");
        Assert.Contains(result.Items, p => p.Y == 1 && p.Item.Id == "bottle_plastic");
        Assert.DoesNotContain(result.Items, p => p.Item.Id is "apple" or "cracker"); // chance 0
        Assert.DoesNotContain(result.Warnings, w => w.Contains("item"));

        // Loot never shifts the terrain a seed gives.
        var plain = new MapgenGenerator(db, TileTypeLoader.Load(db), new MapgenLibrary(db)).Generate("lootbox", 3);
        Assert.Empty(plain.Items);
    }

    // ---- inventory ------------------------------------------------------------------------

    private static (GameWorld Game, ItemRegistry Items) Room(int seed = 1)
    {
        var db = new ContentDatabase();
        db.LoadJsonText(Items, "items.json");
        db.LoadJsonText("""
            [
              { "type": "terrain", "id": "t_floor", "name": "floor", "move_cost": 2, "comfort": 1, "flags": ["TRANSPARENT", "INDOORS"] },
              { "type": "terrain", "id": "t_wall", "name": "wall", "move_cost": 0, "flags": ["WALL"] },
              { "type": "furniture", "id": "f_bed", "name": "bed", "move_cost_mod": 1, "comfort": 5, "flags": ["TRANSPARENT"] }
            ]
            """, "tiles.json");
        var map = new WorldMap(TileTypeLoader.Load(db), 10, 10, 0, 0, _ => "t_floor");
        map.Level(0).SetFurniture(7, 7, "f_bed");
        return (new GameWorld(map, new PlayerActor(), new TilePos(5, 5, 0), GameTime.At(0, 12), seed), ItemLoader.Registry(db));
    }

    [Fact]
    public void PocketsLimitWhatYouCanCarry()
    {
        var (game, r) = Room();
        var p = game.Player;
        var apple = Make(r, "apple");
        game.Map.Items.Add(p.Pos, apple);
        Assert.Equal("You have no pockets to put it in.", game.WhyCantPickUp(apple));

        var jeans = Make(r, "jeans");
        p.Inventory.Wear(jeans);
        Assert.Equal(2500, p.Inventory.VolumeCapacityMl);
        Assert.Null(game.WhyCantPickUp(apple));
        Assert.True(game.PickUp(apple));
        Assert.Same(jeans, p.Inventory.ContainerOf(apple));

        // A bat is too long for jeans pockets; a pillow too big.
        var bat = Make(r, "bat");
        game.Map.Items.Add(p.Pos with { X = 6 }, bat);
        Assert.Contains("doesn't fit", game.WhyCantPickUp(bat));
        var pillow = Make(r, "pillow");
        game.Map.Items.Add(p.Pos, pillow);
        Assert.Contains("no room", game.WhyCantPickUp(pillow));

        // Out of reach.
        var far = Make(r, "apple");
        game.Map.Items.Add(p.Pos with { X = 8 }, far);
        Assert.Contains("out of reach", game.WhyCantPickUp(far));

        // A backpack takes the pillow, but a 90 cm bat is too long even for its 40 cm pocket.
        p.Inventory.Wear(Make(r, "backpack"));
        Assert.True(game.PickUp(pillow));
        Assert.Equal("backpack", p.Inventory.ContainerOf(pillow)!.Id);
        Assert.Contains("doesn't fit", game.WhyCantPickUp(bat));
        Assert.Equal(45_000_000, p.WeightCapacityMg); // 13 kg + 4 kg × Str 8
    }

    [Fact]
    public void PickingUpCostsMovesByBulkAndDistance()
    {
        var (game, r) = Room();
        var p = game.Player;
        p.Inventory.Wear(Make(r, "backpack"));
        var here = Make(r, "apple");
        var there = Make(r, "apple");
        game.Map.Items.Add(p.Pos, here);
        game.Map.Items.Add(p.Pos with { X = 6 }, there);
        Assert.Equal(80 + 232 / 20, game.ObtainCost(here));
        Assert.Equal(80 + 232 / 20 + 100, game.ObtainCost(there));
        long before = game.Scheduler.Turn;
        game.PickUp(there);
        Assert.Equal(1, game.Scheduler.Turn - before);
        Assert.Equal(100 - 191 + 100, p.Moves); // 191 moves at speed 100
    }

    [Fact]
    public void YouCantWearTwoPairsOfShoesButSocksGoUnder()
    {
        var (game, r) = Room();
        var inv = game.Player.Inventory;
        inv.Wear(Make(r, "sneakers"));
        var boots = Make(r, "boots");
        Assert.Equal("Can't wear more than one rigid item on your left foot! (The sneakers (pair) is in the way.)", inv.WhyCantWear(boots));
        var socks = Make(r, "socks");
        Assert.Null(inv.WhyCantWear(socks));
        inv.Wear(socks);
        Assert.Equal(new[] { "socks", "sneakers" }, inv.Worn.Select(w => w.Id)); // skintight goes under

        inv.Wear(Make(r, "wristwatch"));
        Assert.Equal("Can't wear more than one wrist watch!", inv.WhyCantWear(Make(r, "wristwatch")));
        inv.Wear(Make(r, "tshirt"));
        inv.Wear(Make(r, "tshirt"));
        Assert.Equal("Can't wear 3 or more t-shirts at once.", inv.WhyCantWear(Make(r, "tshirt")));
        Assert.Equal("Putting on a apple would be tricky.", inv.WhyCantWear(Make(r, "apple")));
    }

    [Fact]
    public void LayeringAddsTheSmallerItemsOnALayerAgain()
    {
        var (_, r) = Room();
        var inv = new Inventory();
        inv.Wear(Make(r, "longshirt")); // normal layer, 5
        Assert.Equal(5, inv.Encumbrance("torso"));
        var shirt = Make(r, "longshirt");
        inv.Wear(shirt);
        Assert.Equal(5 + 5 + 5, inv.Encumbrance("torso")); // CDDA: the second adds clamp(5, 2, 10)
        inv.TakeOff(shirt);
        inv.Wear(Make(r, "tshirt")); // skintight: its own layer, no penalty
        Assert.Equal(5 + 3, inv.Encumbrance("torso"));
        Assert.Equal(10, inv.Warmth("torso"));

        // A backpack's encumbrance grows with its load.
        var pack = Make(r, "backpack");
        inv.Wear(pack);
        Assert.Equal(8 + 2, inv.Encumbrance("torso"));
        for (int i = 0; i < 4; i++)
            pack.Contents.Add(Make(r, "pillow")); // 12 of 25 L
        Assert.Equal(8 + 2 + (int)Math.Ceiling(12.0 / 25 * 23), inv.Encumbrance("torso"));
    }

    [Fact]
    public void WieldingSwapsWhatYouHoldIntoAPocketAndFightingUsesIt()
    {
        var (game, r) = Room();
        var p = game.Player;
        p.Inventory.Wear(Make(r, "backpack"));
        var bat = Make(r, "bat");
        var rock = Make(r, "rock");
        game.Map.Items.Add(p.Pos, bat);
        game.Map.Items.Add(p.Pos, rock);

        Assert.Equal(60, Melee.AttackCost(p)); // bare-handed
        Assert.True(game.Wield(rock));
        Assert.True(game.Wield(bat));
        Assert.Same(bat, p.Wielded);
        Assert.Equal("backpack", p.Inventory.ContainerOf(rock)!.Id); // the rock goes in the pack
        Assert.Equal(106, Melee.AttackCost(p));
        Assert.Equal(2 + 3, Melee.PlayerAccuracy(p)); // Dex 8 / 4 + to-hit 3

        // A 90 cm bat won't go in a backpack's 40 cm pocket, so swapping drops it.
        Assert.True(game.Wield(rock));
        Assert.Contains(bat, game.Map.Items.At(p.Pos));
        Assert.Contains(game.Messages, m => m.Text.Contains("no room for the baseball bat"));
        game.Map.Items.Remove(p.Pos, bat);
        p.Inventory.Remove(rock);

        // A bat hits much harder than a fist.
        var rng = new Random(4);
        double fists = Enumerable.Range(0, 2000).Average(_ => Melee.AfterArmor(Melee.PlayerDamage(new PlayerActor(), rng), default(Armor)));
        p.Inventory.Wield(bat);
        double batted = Enumerable.Range(0, 2000).Average(_ => Melee.AfterArmor(Melee.PlayerDamage(p, rng), default(Armor)));
        Assert.InRange(fists, 3, 4.5);
        Assert.InRange(batted, 10, 16);
        Assert.Contains("Take off", game.WhyCantWield(p.Worn[0]));
    }

    [Fact]
    public void TakingOffABagKeepsItsLoadAndDroppingIsFromAnywhere()
    {
        var (game, r) = Room();
        var p = game.Player;
        var pack = Make(r, "backpack");
        p.Inventory.Wear(Make(r, "jeans"));
        p.Inventory.Wear(pack);
        var apple = Make(r, "apple");
        pack.Contents.Add(apple);
        Assert.True(game.TakeOff(pack));
        Assert.Same(pack, p.Wielded); // too big for the jeans, so you hold it
        Assert.Same(apple, Assert.Single(pack.Contents));

        Assert.True(game.Drop(pack, p.Pos with { X = 6 }));
        Assert.Contains(pack, game.Map.Items.At(p.Pos with { X = 6 }));
        var jeans = p.Worn[0];
        Assert.True(game.Drop(jeans));
        Assert.Empty(p.Worn);
        Assert.Contains(jeans, game.Map.Items.At(p.Pos));
        Assert.Contains("don't have", game.WhyCantDrop(Make(r, "apple")));
    }

    // ---- needs ----------------------------------------------------------------------------

    [Fact]
    public void ThirstAndSleepinessTickEveryFiveMinutes()
    {
        var needs = new Needs();
        needs.Update(0, asleep: false);
        needs.Update(3600, asleep: false);
        Assert.Equal(12, needs.Thirst);
        Assert.Equal(12, needs.Sleepiness);
        needs.Update(7200, asleep: true);
        Assert.Equal(18, needs.Thirst); // half as fast asleep
        Assert.Equal(12 - 24, needs.Sleepiness); // 2 back per tick

        var day = new Needs();
        double start = day.StoredKcal + day.StomachKcal + day.GutsKcal;
        for (int i = 0; i < 288; i++)
            day.Tick(asleep: false);
        Assert.InRange(start - day.StoredKcal - day.StomachKcal - day.GutsKcal, 1700, 1740); // CDDA's BMR
    }

    [Fact]
    public void ThirstLabelsAndDrinkingThroughTheStomach()
    {
        var needs = new Needs { Thirst = 60 };
        Assert.Equal("Thirsty", needs.ThirstStatus.Text);
        needs.Consume(0, 50, 250);
        Assert.Equal(60, needs.Thirst);
        Assert.Equal("", needs.ThirstStatus.Text); // instant thirst counts the water in your stomach
        needs.Tick(asleep: false);
        Assert.Equal(60 + 1 - 50, needs.Thirst);
        Assert.Equal(new Needs { Thirst = 600 }.ThirstStatus, new NeedStatus("Parched", NeedSeverity.Critical));
    }

    [Fact]
    public void HungerFollowsYourStomachAndWhenYouAte()
    {
        var needs = new Needs();
        Assert.Equal("", needs.HungerStatus.Text);
        for (int i = 0; i < 36; i++) // 3 hours
            needs.Tick(asleep: false);
        // What the stomach held went into storage, leaving you a little above healthy: peckish.
        Assert.Equal("Peckish", needs.HungerStatus.Text);
        needs.StoredKcal = Needs.HealthyKcal - 100;
        Assert.Equal("Hungry", needs.HungerStatus.Text);
        needs.Consume(1600, 0, 1600);
        Assert.Equal("Satisfied", needs.HungerStatus.Text);
        needs.Consume(500, 0, 500);
        Assert.Equal("Engorged", needs.HungerStatus.Text);
        Assert.True(needs.SpeedPenalty <= -10);
    }

    [Fact]
    public void EatingAndDrinkingSpendCddasTimes()
    {
        var (game, r) = Room();
        var p = game.Player;
        p.Inventory.Wear(Make(r, "backpack"));
        var bottle = Make(r, "water_clean");
        p.Inventory.Stow(bottle);
        p.Needs.Thirst = 100;
        long before = game.Scheduler.Turn;
        Assert.True(game.Eat(bottle));
        Assert.Equal(16, game.Scheduler.Turn - before); // 250 ml / 15 per second
        Assert.Equal(1, bottle.Contents[0].Charges);
        game.Eat(bottle);
        Assert.Empty(bottle.Contents); // the empty bottle stays
        Assert.Equal("The plastic bottle is empty.", game.WhyCantEat(bottle));
        Assert.InRange(p.Needs.InstantThirst, 50, 51); // 100, less a fifth of the 500 ml drunk

        var cracker = Make(r, "cracker");
        p.Inventory.Stow(cracker);
        Assert.True(game.Eat(cracker));
        Assert.Equal(3, cracker.Charges);
        var pillow = Make(r, "pillow");
        Assert.Contains("out of reach", game.WhyCantEat(pillow));
        game.Map.Items.Add(p.Pos, pillow);
        Assert.Equal("You can't eat the pillow.", game.WhyCantEat(pillow));
    }

    [Fact]
    public void YouSleepInABedUntilRested()
    {
        var (game, _) = Room(seed: 3);
        var p = game.Player;
        Assert.Equal("You aren't tired enough to sleep.", game.WhyCantSleep());
        p.Pos = new TilePos(7, 7, 0);
        Assert.Equal(5, game.ComfortHere());
        p.Needs.Sleepiness = 200;
        long before = game.Scheduler.Turn;
        var outcome = game.Sleep();
        Assert.Equal(SleepOutcome.Rested, outcome);
        Assert.False(p.Asleep);
        Assert.True(p.Needs.Sleepiness <= 0);
        long slept = game.Scheduler.Turn - before;
        Assert.InRange(slept, 8 * 3600, 10 * 3600); // 200 / 2 a tick, plus falling asleep and waking
        Assert.Contains(game.Messages, m => m.Text == "You fall asleep.");
    }

    [Fact]
    public void FloorComfortIsTerrainLessMoveCostAndAPillowHelps()
    {
        var (game, r) = Room();
        Assert.Equal(1 - 2, game.ComfortHere());
        game.Map.Items.Add(game.Player.Pos, Make(r, "pillow"));
        Assert.Equal(1 - 2 + 4, game.ComfortHere());
    }

    [Fact]
    public void ClothesSoakUpBlows()
    {
        var (_, r) = Room();
        var inv = new Inventory();
        var jeans = Make(r, "jeans");
        inv.Wear(jeans);
        var rng = new Random(1);
        var (armor, hit) = inv.ArmorAgainst("leg_l", rng);
        Assert.Same(jeans, Assert.Single(hit)); // 100 % coverage
        Assert.Equal(0.75f, armor.Bash, 3);
        Assert.Equal(4, Melee.AfterClothing(new[] { (DamageType.Bash, 5) }, armor));
        Assert.Empty(inv.ArmorAgainst("torso", rng).Hit);
    }
}
