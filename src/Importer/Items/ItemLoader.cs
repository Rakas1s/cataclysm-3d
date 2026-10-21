using System.Text.Json.Nodes;
using Cataclysm3D.Sim.Items;

namespace Cataclysm3D.Importer.Items;

/// <summary>
/// Builds <see cref="ItemType"/>s from CDDA's <c>ITEM</c> definitions (0.I folds most kinds into
/// <c>ITEM</c> with <c>subtypes</c>; the legacy types share the family) and
/// <see cref="ItemGroup"/>s from <c>item_group</c>, with <c>copy-from</c> resolved by the
/// <see cref="ContentDatabase"/>. Armor protection comes from <c>materials.json</c>.
/// </summary>
public static class ItemLoader
{
    /// <summary>Loads every item, item group and material from the snapshot's <c>data/json</c>.</summary>
    public static void LoadContent(ContentDatabase db, string jsonRoot)
    {
        db.LoadDirectory(Path.Combine(jsonRoot, "items"));
        db.LoadDirectory(Path.Combine(jsonRoot, "itemgroups"));
        db.LoadFile(Path.Combine(jsonRoot, "materials.json"));
    }

    /// <summary>A registry that builds types and groups the first time they're asked for.</summary>
    public static ItemRegistry Registry(ContentDatabase db)
    {
        var materials = new Dictionary<string, MaterialInfo?>(StringComparer.Ordinal);
        MaterialInfo? Material(string id)
        {
            if (!materials.TryGetValue(id, out var m))
                materials[id] = m = db.Contains("material", id) ? Info(db.Get("material", id)) : null;
            return m;
        }
        return new ItemRegistry(
            id => db.Contains(ContentDatabase.ItemFamily, id) ? Type(id, db.Get(ContentDatabase.ItemFamily, id), Material) : null,
            id => db.Contains("item_group", id) ? Group(id, db.Get("item_group", id)) : null);
    }

    /// <summary>What armor needs from a <c>material</c>: its resistance per mm, and whether it's soft.</summary>
    public sealed record MaterialInfo(ArmorValues Resist, bool Soft);

    /// <summary>A material's <c>resist</c> per mm of thickness. CDDA derives stab from cut (× 0.8) when it's missing.</summary>
    private static MaterialInfo Info(JsonObject material)
    {
        var r = material["resist"] as JsonObject;
        float cut = Float(r?["cut"]);
        return new MaterialInfo(new ArmorValues(Float(r?["bash"]), cut, r?["stab"] is null ? cut * 0.8f : Float(r["stab"])), Bool(material["soft"]));
    }

    public static ItemType Type(string id, JsonObject d, Func<string, MaterialInfo?>? material = null)
    {
        var subtypes = Json.Strings(d["subtypes"]);
        if (d["type"]?.GetValue<string>() is { } legacy && legacy != ContentDatabase.ItemFamily)
            subtypes.Add(legacy);
        var flags = Json.Strings(d["flags"]);
        var materials = Materials(d["material"]);
        bool comestible = subtypes.Contains("COMESTIBLE") || d["comestible_type"] is not null;
        bool armor = d["armor"] is JsonArray || subtypes.Contains("ARMOR") || subtypes.Contains("TOOL_ARMOR");
        var melee = d["melee_damage"] as JsonObject;
        return new ItemType
        {
            Id = id,
            Name = Json.Name(d, id),
            PluralName = PluralName(d),
            Description = Json.FirstString(d["description"]) ?? "",
            LooksLike = Json.FirstString(d["looks_like"]),
            Symbol = Json.FirstString(d["symbol"]) ?? "?",
            Color = Json.FirstString(d["color"]) ?? "white",
            Category = Json.FirstString(d["category"]) ?? "",
            Flags = flags,
            Materials = materials,
            WeightMg = Mass(d["weight"]),
            VolumeMl = Volume(d["volume"]),
            LongestSideMm = Length(d["longest_side"]),
            ChargesPerUnit = comestible ? Math.Max(1, Int(d["charges"], 1)) : 1,
            Bash = Int(melee?["bash"] ?? d["bashing"]),
            Cut = Int(melee?["cut"] ?? d["cutting"]),
            Stab = Int(melee?["stab"]),
            ToHit = ToHit(d["to_hit"]),
            MaxWorn = Int(d["max_worn"], 2),
            Armor = armor ? Armor(d, flags, materials, material) : null,
            Comestible = comestible ? Comestible(d) : null,
            Pockets = Pockets(d["pocket_data"]),
            DefaultContainer = Json.FirstString(d["container"]),
            IsLiquid = Json.FirstString(d["phase"]) == "liquid",
        };
    }

    /// <summary>
    /// An <c>item_group</c>: <c>entries</c> / <c>items</c> / <c>groups</c> in any of CDDA's forms
    /// (an id, <c>[id, prob]</c>, or an object with <c>item</c>, <c>group</c>, or an inline
    /// <c>distribution</c> / <c>collection</c>).
    /// </summary>
    public static ItemGroup Group(string id, JsonObject d) =>
        new(id, Json.FirstString(d["subtype"]) == "collection" ? ItemGroupKind.Collection : ItemGroupKind.Distribution, Entries(d))
        {
            ContainerItem = Json.FirstString(d["container-item"]),
        };

    /// <summary>A group written inline in mapgen or another group: <c>{"subtype": ..., "entries": ...}</c>, or a list of entries.</summary>
    public static ItemGroup InlineGroup(JsonNode node, string context, ItemGroupKind kind = ItemGroupKind.Collection) => node switch
    {
        JsonObject o when o["subtype"] is not null || o["entries"] is not null || o["items"] is not null || o["groups"] is not null => Group(context, o),
        JsonArray a => new ItemGroup(context, kind, a.Select(e => Entry(e, isGroup: false)).OfType<ItemGroupEntry>().ToList()),
        _ => new ItemGroup(context, kind, Entry(node, isGroup: false) is { } e ? new[] { e } : Array.Empty<ItemGroupEntry>()),
    };

    private static List<ItemGroupEntry> Entries(JsonObject d)
    {
        var entries = new List<ItemGroupEntry>();
        foreach (var key in new[] { "entries", "items" })
            foreach (var e in d[key] as JsonArray ?? new JsonArray())
                if (Entry(e, isGroup: false) is { } entry)
                    entries.Add(entry);
        foreach (var e in d["groups"] as JsonArray ?? new JsonArray())
            if (Entry(e, isGroup: true) is { } entry)
                entries.Add(entry);
        return entries;
    }

    private static ItemGroupEntry? Entry(JsonNode? e, bool isGroup)
    {
        switch (e)
        {
            case JsonValue v when v.TryGetValue(out string? s):
                return isGroup ? new ItemGroupEntry(100, IntRange.One) { GroupId = s } : new ItemGroupEntry(100, IntRange.One) { ItemId = s };
            case JsonArray { Count: >= 1 } a when Json.FirstString(a[0]) is { } s:
            {
                int prob = a.Count > 1 ? Int(a[1], 100) : 100;
                return isGroup ? new ItemGroupEntry(prob, IntRange.One) { GroupId = s } : new ItemGroupEntry(prob, IntRange.One) { ItemId = s };
            }
            case JsonObject o:
            {
                int prob = Int(o["prob"], 100);
                var count = Range(o["count"], o["count-min"], o["count-max"]) ?? IntRange.One;
                var charges = Range(o["charges"], o["charges-min"], o["charges-max"]);
                string? container = Json.FirstString(o["container-item"]);
                if (o["distribution"] is JsonArray dist)
                    return new ItemGroupEntry(prob, count) { Inline = InlineGroup(dist, "inline", ItemGroupKind.Distribution) };
                if (o["collection"] is JsonArray coll)
                    return new ItemGroupEntry(prob, count) { Inline = InlineGroup(coll, "inline") };
                if (Json.FirstString(o["item"]) is { } item)
                    return new ItemGroupEntry(prob, count) { ItemId = item, Charges = charges, ContainerItem = container };
                if (Json.FirstString(o["group"]) is { } group)
                    return new ItemGroupEntry(prob, count) { GroupId = group };
                return null;
            }
            default:
                return null;
        }
    }

    /// <summary>A count or charges: <c>n</c>, <c>[min, max]</c>, or separate <c>-min</c> / <c>-max</c> fields.</summary>
    private static IntRange? Range(JsonNode? both, JsonNode? min, JsonNode? max) => both switch
    {
        JsonValue v when v.TryGetValue(out double n) => new IntRange((int)n, (int)n),
        JsonArray { Count: >= 2 } a => new IntRange(Int(a[0]), Int(a[1])),
        JsonArray { Count: 1 } a => new IntRange(Int(a[0]), Int(a[0])),
        _ when min is not null || max is not null => new IntRange(Int(min, 1), Int(max, Int(min, 1))),
        _ => null,
    };

    private static string? PluralName(JsonObject d) => d["name"] switch
    {
        JsonObject o when o["str_sp"] is JsonValue sp && sp.TryGetValue(out string? s) => s,
        JsonObject o when o["str_pl"] is JsonValue pl && pl.TryGetValue(out string? s) => s,
        _ => null,
    };

    private static List<string> Materials(JsonNode? node) => node switch
    {
        JsonArray a => a.Select(m => m is JsonObject o ? Json.FirstString(o["type"]) : Json.FirstString(m)).OfType<string>().ToList(),
        JsonValue v when v.TryGetValue(out string? s) => new List<string> { s },
        _ => new List<string>(),
    };

    /// <summary>
    /// The <c>to_hit</c> bonus, a number or (0.I) an object scored by grip, length, striking
    /// surface and balance, as <c>item_factory.cpp</c>'s <c>melee_accuracy</c> sums them:
    /// -2 plus each part's score, each part defaulting to its neutral choice. Items that don't
    /// say get -2.
    /// </summary>
    public static int ToHit(JsonNode? node)
    {
        if (node is JsonValue v && v.TryGetValue(out double n))
            return (int)n;
        if (node is not JsonObject o)
            return ToHitBase;
        int grip = Json.FirstString(o["grip"]) switch { "bad" => -1, "none" => 0, "solid" => 1, _ => 2 };
        int length = Json.FirstString(o["length"]) switch { "short" => 1, "long" => 2, _ => 0 };
        int surface = Json.FirstString(o["surface"]) switch { "point" => -2, "line" => -1, "every" => 1, _ => 0 };
        int balance = Json.FirstString(o["balance"]) switch { "clumsy" => -2, "uneven" => -1, "good" => 1, _ => 0 };
        return ToHitBase + grip + length + surface + balance;
    }

    /// <summary>CDDA's <c>base_acc</c>, and the to-hit of an item with no <c>to_hit</c> at all.</summary>
    public const int ToHitBase = -2;

    private static ComestibleData Comestible(JsonObject d) => new(
        Json.FirstString(d["comestible_type"]) switch { "DRINK" => ComestibleKind.Drink, "MED" => ComestibleKind.Med, _ => ComestibleKind.Food },
        Int(d["calories"]),
        Int(d["quench"]),
        Int(d["fun"]));

    private static IReadOnlyList<Pocket> Pockets(JsonNode? node)
    {
        var list = new List<Pocket>();
        foreach (var p in (node as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            if ((Json.FirstString(p["pocket_type"]) ?? "CONTAINER") != "CONTAINER")
                continue;
            long volume = Volume(p["max_contains_volume"]);
            if (volume <= 0)
                continue;
            list.Add(new Pocket(
                volume,
                Mass(p["max_contains_weight"]),
                Length(p["max_item_length"]),
                Bool(p["watertight"]),
                Bool(p["rigid"]),
                Bool(p["holster"]),
                Int(p["moves"], 100)));
        }
        return list;
    }

    /// <summary>
    /// The <c>armor</c> list merged per set of parts: entries covering the same parts (0.I splits
    /// a sneaker into heel, sole and ankle) take the best coverage and add their encumbrance.
    /// </summary>
    private static ArmorData Armor(JsonObject d, IReadOnlySet<string> flags, IReadOnlyList<string> materials, Func<string, MaterialInfo?>? material)
    {
        var merged = new Dictionary<string, (List<string> Covers, int Coverage, int Enc, int MaxEnc, bool Rigid)>(StringComparer.Ordinal);
        bool itemHard = materials.Any(m => material?.Invoke(m) is { Soft: false });
        string? portionLayer = null;
        var portionMaterial = new List<(string Type, float Thickness, float Share)>();
        foreach (var a in (d["armor"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var covers = Json.Strings(a["covers"]).Where(BodySlots.All.Contains).OrderBy(p => Array.IndexOf(BodySlots.All, p)).ToList();
            if (covers.Count == 0)
                continue;
            portionLayer ??= Json.FirstString(a["layers"]);
            if (portionMaterial.Count == 0)
                foreach (var m in (a["material"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                    if (Json.FirstString(m["type"]) is { } t)
                        portionMaterial.Add((t, Float(m["thickness"]), m["covered_by_mat"] is null ? 1 : Float(m["covered_by_mat"]) / 100f));
            int coverage = Int(a["coverage"]);
            var own = (a["material"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Where(m => m["covered_by_mat"] is null || Float(m["covered_by_mat"]) > 0)
                .Select(m => Json.FirstString(m["type"])).OfType<string>().ToList();
            bool rigid = flags.Contains("HARD") || (!flags.Contains("SOFT")
                && (own.Count > 0 ? own.Any(m => material?.Invoke(m) is { Soft: false }) : itemHard));
            var (enc, maxEnc) = a["encumbrance"] switch
            {
                JsonArray { Count: >= 2 } e => (Int(e[0]), Int(e[1])),
                var e => (Int(e), Int(e)),
            };
            string key = string.Join(",", covers);
            merged[key] = merged.TryGetValue(key, out var m0)
                ? (covers, Math.Max(m0.Coverage, coverage), m0.Enc + enc, m0.MaxEnc + Math.Max(enc, maxEnc), m0.Rigid || rigid)
                : (covers, coverage, enc, Math.Max(enc, maxEnc), rigid);
        }

        var layer = flags.Contains("PERSONAL") ? WornLayer.Personal
            : flags.Contains("SKINTIGHT") ? WornLayer.Skintight
            : flags.Contains("WAIST") ? WornLayer.Waist
            : flags.Contains("OUTER") ? WornLayer.Outer
            : flags.Contains("BELTED") ? WornLayer.Belted
            : flags.Contains("AURA") ? WornLayer.Aura
            : portionLayer switch
            {
                "PERSONAL" => WornLayer.Personal,
                "SKINTIGHT" => WornLayer.Skintight,
                "WAIST" => WornLayer.Waist,
                "OUTER" => WornLayer.Outer,
                "BELTED" => WornLayer.Belted,
                "AURA" => WornLayer.Aura,
                _ => WornLayer.Normal,
            };

        bool canBeRigid = layer is WornLayer.Skintight or WornLayer.Normal or WornLayer.Outer;

        // Protection: per-portion materials with their own thickness, else the item's materials
        // sharing its material_thickness.
        var protection = default(ArmorValues);
        if (material is not null)
        {
            if (portionMaterial.Count > 0)
            {
                foreach (var (type, thickness, share) in portionMaterial)
                    if (material(type) is { } r)
                        protection += Scale(r.Resist, thickness * share);
            }
            else if (materials.Count > 0)
            {
                float thickness = Float(d["material_thickness"]);
                foreach (var type in materials)
                    if (material(type) is { } r)
                        protection += Scale(r.Resist, thickness / materials.Count);
            }
        }

        return new ArmorData(
            merged.Values.Select(m => new ArmorPortion(m.Covers, m.Coverage, m.Enc, m.MaxEnc, m.Rigid && canBeRigid)).ToList(),
            layer,
            Int(d["warmth"]),
            protection);
    }

    private static ArmorValues Scale(ArmorValues v, float f) => new(v.Bash * f, v.Cut * f, v.Stab * f);

    private static long Mass(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out string? s) && Units.TryParse(s, Units.Mass, out double mg) => (long)mg,
        JsonValue v when v.TryGetValue(out double grams) => (long)(grams * 1000),
        _ => 0,
    };

    private static long Volume(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out string? s) && Units.TryParse(s, Units.Volume, out double ml) => (long)ml,
        // Legacy unitless volumes are in 250 ml units.
        JsonValue v when v.TryGetValue(out double units) => (long)(units * 250),
        _ => 0,
    };

    private static long Length(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out string? s) && Units.TryParse(s, Units.Length, out double mm) => (long)mm,
        _ => 0,
    };

    private static bool Bool(JsonNode? node) => node is JsonValue v && v.TryGetValue(out bool b) && b;

    private static float Float(JsonNode? node) => node is JsonValue v && v.TryGetValue(out double d) ? (float)d : 0;

    private static int Int(JsonNode? node, int fallback = 0) =>
        node is JsonValue v && v.TryGetValue(out double d) ? (int)d : fallback;
}
