using Cataclysm3D.Importer;

namespace Cataclysm3D.Tests;

public class ContentDatabaseTests
{
    private static ContentDatabase Load(string json)
    {
        var db = new ContentDatabase();
        db.LoadJsonText(json, "test.json");
        return db;
    }

    [Fact]
    public void CopyFromInheritsAndOverrides()
    {
        var db = Load("""
            [
              { "type": "GENERIC", "abstract": "base_knife", "weight": "200 g", "flags": ["STAB"] },
              { "type": "GENERIC", "id": "knife_steak", "copy-from": "base_knife", "name": "steak knife", "weight": "80 g" }
            ]
            """);

        var knife = db.Get("GENERIC", "knife_steak");
        Assert.Equal("steak knife", knife["name"]!.GetValue<string>());
        Assert.Equal("80 g", knife["weight"]!.GetValue<string>());
        Assert.Equal("STAB", knife["flags"]![0]!.GetValue<string>());
        Assert.Null(knife["copy-from"]);
        Assert.Null(knife["abstract"]);
    }

    [Fact]
    public void ItemTypesShareANamespace()
    {
        var db = Load("""
            [
              { "type": "GENERIC", "id": "pipe", "name": "pipe" },
              { "type": "TOOL", "id": "pipe_wrench", "copy-from": "pipe", "name": "pipe wrench" }
            ]
            """);
        Assert.Equal("pipe wrench", db.Get("TOOL", "pipe_wrench")["name"]!.GetValue<string>());
    }

    [Fact]
    public void ExtendDeleteRelativeProportional()
    {
        var db = Load("""
            [
              { "type": "MONSTER", "id": "mon_zombie", "hp": 80, "speed": 70, "flags": ["SEES", "HEARS", "SMELLS"],
                "melee_damage": { "bash": 4 } },
              { "type": "MONSTER", "id": "mon_zombie_fast", "copy-from": "mon_zombie",
                "extend": { "flags": ["CLIMBS"] }, "delete": { "flags": ["SMELLS"] },
                "relative": { "hp": -20, "melee_damage": { "bash": 2 } }, "proportional": { "speed": 2 } }
            ]
            """);

        var fast = db.Get("MONSTER", "mon_zombie_fast");
        Assert.Equal(60, fast["hp"]!.GetValue<double>());
        Assert.Equal(140, fast["speed"]!.GetValue<double>());
        Assert.Equal(6, fast["melee_damage"]!["bash"]!.GetValue<double>());
        var flags = fast["flags"]!.AsArray().Select(f => f!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "SEES", "HEARS", "CLIMBS" }, flags);

        // The parent is untouched.
        Assert.Equal(80, db.Get("MONSTER", "mon_zombie")["hp"]!.GetValue<int>());
    }

    [Fact]
    public void ProportionalAndRelativeUnderstandUnits()
    {
        var db = Load("""
            [
              { "type": "ITEM", "id": "rifle", "weight": "4 kg", "volume": "2 L", "price": "100 USD" },
              { "type": "ITEM", "id": "rifle_short", "copy-from": "rifle",
                "proportional": { "weight": 0.75, "price": 0.5 }, "relative": { "volume": "-500 ml" } }
            ]
            """);

        var item = db.Get("ITEM", "rifle_short");
        Assert.Equal(3_000_000, Units.ParseMassMilligrams(item["weight"]!.GetValue<string>()));
        Assert.Equal(1_500, Units.ParseVolumeMilliliters(item["volume"]!.GetValue<string>()));
        Assert.Equal(5_000, Units.Parse(item["price"]!.GetValue<string>(), Units.Money));
        Assert.Empty(db.Warnings);
    }

    [Fact]
    public void ProportionalDamageMatchesByDamageType()
    {
        var db = Load("""
            [
              { "type": "ITEM", "id": "ammo_a", "damage": { "damage_type": "bullet", "amount": 40, "armor_penetration": 2 } },
              { "type": "ITEM", "id": "ammo_b", "copy-from": "ammo_a",
                "proportional": { "damage": { "damage_type": "bullet", "amount": 0.5 } } }
            ]
            """);

        Assert.Equal(20, db.Get("ITEM", "ammo_b")["damage"]!["amount"]!.GetValue<double>());
        Assert.Empty(db.Warnings);
    }

    [Fact]
    public void ArrayIdsDefineEachId()
    {
        var db = Load("""[ { "type": "terrain", "id": ["t_wall", "t_wall_w"], "name": "wall" } ]""");
        Assert.Equal("t_wall_w", db.Get("terrain", "t_wall_w")["id"]!.GetValue<string>());
        Assert.True(db.Contains("terrain", "t_wall"));
    }

    [Fact]
    public void CopyFromCycleIsReported()
    {
        var db = Load("""
            [
              { "type": "terrain", "id": "a", "copy-from": "b" },
              { "type": "terrain", "id": "b", "copy-from": "a" }
            ]
            """);
        Assert.Throws<InvalidOperationException>(() => db.Get("terrain", "a"));
    }

    [Theory]
    [InlineData("250 g", 250_000L)]
    [InlineData("1 kg 200 g", 1_200_000L)]
    [InlineData("1.5 kg", 1_500_000L)]
    public void ParsesMass(string text, long mg) => Assert.Equal(mg, Units.ParseMassMilligrams(text));

    [Theory]
    [InlineData("500 ml", 500L)]
    [InlineData("1 L 250 ml", 1250L)]
    public void ParsesVolume(string text, long ml) => Assert.Equal(ml, Units.ParseVolumeMilliliters(text));
}
