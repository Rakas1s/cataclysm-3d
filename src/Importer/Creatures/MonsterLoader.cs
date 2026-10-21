using System.Text.Json.Nodes;
using Cataclysm3D.Sim.Creatures;

namespace Cataclysm3D.Importer.Creatures;

/// <summary>
/// Builds <see cref="MonsterType"/>s from CDDA <c>MONSTER</c> definitions (copy-from resolved by
/// the <see cref="ContentDatabase"/>), <see cref="MonsterGroup"/>s from <c>monstergroup</c>, and
/// the player's body parts from <c>body_part</c>.
/// </summary>
public static class MonsterLoader
{
    /// <summary>Loads the monster, monster group and body part JSON from the snapshot's <c>data/json</c>.</summary>
    public static void LoadContent(ContentDatabase db, string jsonRoot)
    {
        db.LoadDirectory(Path.Combine(jsonRoot, "monsters"));
        db.LoadDirectory(Path.Combine(jsonRoot, "monstergroups"));
        db.LoadFile(Path.Combine(jsonRoot, "body_parts.json"));
    }

    /// <summary>A registry that builds types and groups from the database the first time they're asked for.</summary>
    public static MonsterRegistry Registry(ContentDatabase db) => new(
        id => db.Contains("MONSTER", id) ? Type(id, db.Get("MONSTER", id)) : null,
        id => db.Contains("monstergroup", id) ? Group(id, db.Get("monstergroup", id)) : null);

    public static MonsterType Type(string id, JsonObject d)
    {
        int dice = Int(d["melee_dice"]), sides = Int(d["melee_dice_sides"]);
        return new MonsterType
        {
            Id = id,
            Name = Json.Name(d, id),
            LooksLike = Json.FirstString(d["looks_like"]),
            Symbol = Json.FirstString(d["symbol"]) ?? "?",
            Color = Json.FirstString(d["color"]) ?? "white",
            Species = Json.Strings(d["species"]),
            Flags = Json.Strings(d["flags"]),
            Hp = Math.Max(1, Int(d["hp"], 1)),
            Speed = Math.Max(1, Int(d["speed"], 100)),
            Aggression = Int(d["aggression"]),
            MeleeSkill = Int(d["melee_skill"]),
            MeleeDice = dice,
            MeleeDiceSides = sides,
            MeleeDamage = Damage(d["melee_damage"]),
            AttackCost = Int(d["attack_cost"], 100),
            Dodge = Int(d["dodge"]),
            Armor = Armor(d),
            VisionDay = Int(d["vision_day"], MonsterType.DefaultVisionDay),
            VisionNight = Int(d["vision_night"], MonsterType.DefaultVisionNight),
            BashSkill = Int(d["bash_skill"], dice * sides),
        };
    }

    public static MonsterGroup Group(string id, JsonObject d)
    {
        var entries = new List<MonsterGroupEntry>();
        foreach (var e in d["monsters"] as JsonArray ?? new JsonArray())
        {
            if (e is not JsonObject o)
                continue;
            int weight = Int(o["weight"] ?? o["freq"], 1);
            int starts = StartsDay(o["starts"]);
            if (Json.FirstString(o["monster"]) is { } mon)
                entries.Add(new MonsterGroupEntry(mon, false, weight, starts));
            else if (Json.FirstString(o["group"]) is { } grp)
                entries.Add(new MonsterGroupEntry(grp, true, weight, starts));
        }
        return new MonsterGroup(id, Json.FirstString(d["default"]), entries);
    }

    /// <summary>
    /// The main body parts with their hit points; minor parts (hands, feet, eyes, mouth) add their
    /// <c>hit_size</c> to their <c>main_part</c>. Falls back to <see cref="BodyPartType.Defaults"/>.
    /// </summary>
    public static IReadOnlyList<BodyPartType> BodyParts(ContentDatabase db)
    {
        var main = new List<(string Id, JsonObject Data)>();
        var sizes = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var id in BodyPartType.Defaults.Select(p => p.Id))
            if (db.Contains("body_part", id))
                main.Add((id, db.Get("body_part", id)));
        if (main.Count != BodyPartType.Defaults.Count)
            return BodyPartType.Defaults;
        foreach (var (id, data) in db.All("body_part"))
        {
            string owner = Json.FirstString(data["main_part"]) ?? id;
            if (data["hit_size"] is JsonValue v && v.TryGetValue(out double size))
                sizes[owner] = sizes.GetValueOrDefault(owner) + (float)size;
        }
        return main.Select(m => new BodyPartType(
            m.Id,
            Json.Name(m.Data, m.Id),
            Int(m.Data["base_hp"], 60),
            sizes.GetValueOrDefault(m.Id),
            m.Data["is_vital"] is JsonValue vital && vital.TryGetValue(out bool b) && b)).ToList();
    }

    private static IReadOnlyList<(DamageType, int)> Damage(JsonNode? node)
    {
        var list = new List<(DamageType, int)>();
        foreach (var e in node as JsonArray ?? new JsonArray())
        {
            if (e is not JsonObject o || DamageTypeOf(Json.FirstString(o["damage_type"])) is not { } type)
                continue;
            if (Int(o["amount"]) is > 0 and var amount)
                list.Add((type, amount));
        }
        return list;
    }

    private static DamageType? DamageTypeOf(string? s) => s switch
    {
        "bash" => DamageType.Bash,
        "cut" => DamageType.Cut,
        "stab" => DamageType.Stab,
        _ => null,
    };

    /// <summary>0.I's <c>armor</c> object, or the older <c>armor_bash</c> / <c>armor_cut</c> / <c>armor_stab</c> fields.</summary>
    private static Armor Armor(JsonObject d)
    {
        if (d["armor"] is JsonObject a)
            return new Armor(Int(a["bash"]), Int(a["cut"]), Int(a["stab"]));
        return new Armor(Int(d["armor_bash"]), Int(d["armor_cut"]), Int(d["armor_stab"]));
    }

    /// <summary>CDDA's <c>starts</c>: a duration ("120 days") or a number of turns, in whole days.</summary>
    private static int StartsDay(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out string? s) && Units.TryParse(s, Units.Time, out double seconds) => (int)(seconds / 86400),
        JsonValue v when v.TryGetValue(out double turns) => (int)(turns / 86400),
        _ => 0,
    };

    private static int Int(JsonNode? node, int fallback = 0) =>
        node is JsonValue v && v.TryGetValue(out double d) ? (int)d : fallback;
}
