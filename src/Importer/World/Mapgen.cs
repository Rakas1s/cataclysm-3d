using System.Text.Json.Nodes;
using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Importer.World;

/// <summary>Indexes CDDA JSON mapgen entries by overmap terrain id.</summary>
public sealed class MapgenLibrary
{
    private readonly Dictionary<string, List<JsonObject>> _byOmt = new(StringComparer.Ordinal);

    public MapgenLibrary(ContentDatabase db)
    {
        foreach (var entry in db.Unkeyed)
        {
            if (entry["type"]?.GetValue<string>() != "mapgen" || entry["object"] is not JsonObject)
                continue;
            // om_terrain is a string, a list of ids sharing one layout, or a 2D grid for
            // multi-tile buildings. The grid form is not supported yet.
            switch (entry["om_terrain"])
            {
                case JsonValue v when v.TryGetValue(out string? id):
                    Add(id, entry);
                    break;
                case JsonArray a when a.All(n => n is JsonValue):
                    foreach (var n in a)
                        Add(n!.GetValue<string>(), entry);
                    break;
            }
        }
    }

    private void Add(string omt, JsonObject entry)
    {
        if (!_byOmt.TryGetValue(omt, out var list))
            _byOmt[omt] = list = new List<JsonObject>();
        list.Add(entry);
    }

    public IEnumerable<string> OmTerrains => _byOmt.Keys;

    public IReadOnlyList<JsonObject> For(string omt) =>
        _byOmt.TryGetValue(omt, out var list) ? list : Array.Empty<JsonObject>();
}

/// <summary>A monster mapgen wants placed: a monster id or a monster group id, at a map tile.</summary>
public sealed record MonsterSpawn(string Id, bool IsGroup, int X, int Y);

public sealed record MapgenResult(TileGrid Map, IReadOnlyList<string> Warnings)
{
    /// <summary>Monsters rolled from <c>place_monsters</c> and <c>place_monster</c>, not yet resolved to types.</summary>
    public IReadOnlyList<MonsterSpawn> Spawns { get; init; } = Array.Empty<MonsterSpawn>();
}

/// <summary>
/// Runs CDDA JSON mapgen: <c>rows</c> + palettes (nested, parametrized) + <c>terrain</c> /
/// <c>furniture</c> / <c>mapping</c> symbol tables + <c>fill_ter</c>, and rolls
/// <c>place_monsters</c> / <c>place_monster</c> into <see cref="MonsterSpawn"/>s. Items, nested
/// chunks and loot are not placed yet.
/// </summary>
public sealed class MapgenGenerator
{
    private const string DefaultFill = "t_dirt";

    private readonly ContentDatabase _db;
    private readonly TileRegistry _registry;
    private readonly MapgenLibrary _library;

    /// <summary>Regional pseudo ids (t_region_groundcover, …) → weighted real ids, from region_settings.</summary>
    private readonly Dictionary<string, JsonObject> _regional = new(StringComparer.Ordinal);

    public MapgenGenerator(ContentDatabase db, TileRegistry registry, MapgenLibrary library, string region = "default")
    {
        _db = db;
        _registry = registry;
        _library = library;
        if (db.Contains("region_settings", region)
            && db.Get("region_settings", region)["region_terrain_and_furniture"] is JsonObject rtf)
        {
            foreach (var table in new[] { rtf["terrain"], rtf["furniture"] }.OfType<JsonObject>())
                foreach (var (pseudo, choices) in table)
                    if (choices is JsonObject c)
                        _regional[pseudo] = c;
        }
    }

    /// <summary>Replaces a regional pseudo id with a real one, picked by weight.</summary>
    private string Regionalize(string id, Context ctx)
    {
        for (int depth = 0; depth < 8 && _regional.TryGetValue(id, out var choices); depth++)
        {
            var options = choices
                .Select(kv => (kv.Key, kv.Value is JsonValue v && v.TryGetValue(out double w) ? (int)w : 1))
                .ToList();
            if (options.Count == 0)
                break;
            id = ctx.PickWeighted(options);
        }
        return id;
    }

    public MapgenResult Generate(string omTerrain, int seed)
    {
        var candidates = _library.For(omTerrain);
        if (candidates.Count == 0)
            throw new KeyNotFoundException($"No mapgen for \"{omTerrain}\"");

        var ctx = new Context(new Random(seed), _db);
        var entry = ctx.PickWeighted(candidates.Select(c => (c, Weight(c))).ToList());
        var obj = (JsonObject)entry["object"]!;

        ctx.AddParameters(obj["parameters"] as JsonObject);
        foreach (var p in Palettes(obj))
            ctx.AddPalette(p);
        ctx.AddSymbols(obj);

        var rows = (obj["rows"] as JsonArray)?.Select(r => r!.GetValue<string>()).ToList() ?? new List<string>();
        int height = Math.Max(rows.Count, 1);
        int width = Math.Max(rows.Select(r => r.EnumerateRunes().Count()).DefaultIfEmpty(0).Max(), 1);

        string fill = ctx.Resolve(obj["fill_ter"]) is { } rawFill && Regionalize(rawFill, ctx) is var f
                      && f != TileRegistry.NullTerrain && _registry.TryTerrain(f, out _)
            ? f
            : DefaultFill;
        var map = new TileGrid(_registry, width, height, fill);

        var unknownSymbols = new SortedSet<string>(StringComparer.Ordinal);
        for (int y = 0; y < rows.Count; y++)
        {
            int x = 0;
            foreach (var rune in rows[y].EnumerateRunes())
            {
                PlaceSymbol(map, ctx, x, y, rune.ToString(), unknownSymbols);
                x++;
            }
        }

        foreach (var s in unknownSymbols)
            ctx.Warnings.Add($"symbol '{s}' has no terrain or furniture");
        return new MapgenResult(map, ctx.Warnings) { Spawns = Spawns(obj, ctx) };
    }

    /// <summary>
    /// Rolls the monster placements. <c>place_monsters</c> (a group) fires with a one-in-<c>chance</c>
    /// and places one monster per <c>repeat</c>; CDDA's <c>density</c> pack sizing is ignored.
    /// <c>place_monster</c> (a monster or group) fires per <c>repeat</c> with <c>chance</c> percent.
    /// </summary>
    private static List<MonsterSpawn> Spawns(JsonObject obj, Context ctx)
    {
        var spawns = new List<MonsterSpawn>();
        foreach (var e in (obj["place_monsters"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            if (e["monster"]?.GetValue<string>() is not { } group)
                continue;
            if (ctx.Roll(e["chance"], 1) is var oneIn && oneIn > 1 && ctx.Next(oneIn) != 0)
                continue;
            for (int i = ctx.Roll(e["repeat"], 1); i > 0; i--)
                spawns.Add(new MonsterSpawn(group, true, ctx.Roll(e["x"], 0), ctx.Roll(e["y"], 0)));
        }
        foreach (var e in (obj["place_monster"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            string? monster = e["monster"]?.GetValue<string>();
            string? group = e["group"]?.GetValue<string>();
            if ((monster ?? group) is not { } id)
                continue;
            int percent = ctx.Roll(e["chance"], 100);
            for (int i = ctx.Roll(e["repeat"], 1); i > 0; i--)
                if (ctx.Next(100) < percent)
                    spawns.Add(new MonsterSpawn(id, monster is null, ctx.Roll(e["x"], 0), ctx.Roll(e["y"], 0)));
        }
        return spawns;
    }

    private void PlaceSymbol(TileGrid map, Context ctx, int x, int y, string symbol, ISet<string> unknown)
    {
        bool known = false;
        if (ctx.Terrain.TryGetValue(symbol, out var terNode))
        {
            known = true;
            if (ctx.Resolve(terNode) is { } rawTer && Regionalize(rawTer, ctx) is var ter && ter != TileRegistry.NullTerrain)
            {
                if (_registry.TryTerrain(ter, out _))
                    map.SetTerrain(x, y, ter);
                else
                    ctx.Warn($"unknown terrain \"{ter}\"");
            }
        }
        if (ctx.Furniture.TryGetValue(symbol, out var furNode))
        {
            known = true;
            if (ctx.Resolve(furNode) is { } rawFur && Regionalize(rawFur, ctx) is var fur && fur != TileRegistry.NullFurniture)
            {
                if (_registry.TryFurniture(fur, out _))
                    map.SetFurniture(x, y, fur);
                else
                    ctx.Warn($"unknown furniture \"{fur}\"");
            }
        }
        if (!known && !ctx.OtherSymbols.Contains(symbol) && symbol != " " && symbol != ".")
            unknown.Add(symbol);
    }

    private static int Weight(JsonObject entry) =>
        entry["weight"] is JsonValue v && v.TryGetValue(out double w) ? (int)w : 1000;

    private static IEnumerable<JsonNode> Palettes(JsonObject obj) =>
        obj["palettes"] is JsonArray a ? a.OfType<JsonNode>() : Enumerable.Empty<JsonNode>();

    /// <summary>One mapgen run: random state, resolved parameters, merged symbol tables.</summary>
    private sealed class Context
    {
        private readonly Random _rng;
        private readonly ContentDatabase _db;
        private readonly Dictionary<string, JsonNode?> _paramDefaults = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _paramValues = new(StringComparer.Ordinal);
        private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
        private int _paletteDepth;

        public Dictionary<string, JsonNode> Terrain { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonNode> Furniture { get; } = new(StringComparer.Ordinal);

        /// <summary>Symbols used by tables we don't place yet (items, monsters, nested chunks, …).</summary>
        public HashSet<string> OtherSymbols { get; } = new(StringComparer.Ordinal);

        public List<string> Warnings { get; } = new();

        public Context(Random rng, ContentDatabase db)
        {
            _rng = rng;
            _db = db;
        }

        public int Next(int max) => _rng.Next(max);

        /// <summary>A mapgen number: an int, or [min, max] rolled inclusively.</summary>
        public int Roll(JsonNode? node, int fallback) => node switch
        {
            JsonValue v when v.TryGetValue(out double d) => (int)d,
            JsonArray { Count: >= 2 } a when a[0] is JsonValue lo && a[1] is JsonValue hi
                && lo.TryGetValue(out double l) && hi.TryGetValue(out double h) => _rng.Next((int)l, (int)Math.Max(l, h) + 1),
            JsonArray { Count: 1 } a => Roll(a[0], fallback),
            _ => fallback,
        };

        public void Warn(string message)
        {
            if (_warned.Add(message))
                Warnings.Add(message);
        }

        public T PickWeighted<T>(IReadOnlyList<(T Value, int Weight)> options)
        {
            int total = options.Sum(o => Math.Max(o.Weight, 0));
            if (total <= 0)
                return options[0].Value;
            int roll = _rng.Next(total);
            foreach (var (value, weight) in options)
            {
                roll -= Math.Max(weight, 0);
                if (roll < 0)
                    return value;
            }
            return options[^1].Value;
        }

        public void AddParameters(JsonObject? parameters)
        {
            if (parameters is null)
                return;
            // The outermost definition wins, matching CDDA's scoping.
            foreach (var (name, def) in parameters)
                _paramDefaults.TryAdd(name, (def as JsonObject)?["default"]);
        }

        /// <summary>Adds a palette: its parameters, then its sub-palettes, then its own symbols on top.</summary>
        public void AddPalette(JsonNode reference)
        {
            if (Resolve(reference) is not { } id)
                return;
            if (!_db.Contains("palette", id))
            {
                Warn($"unknown palette \"{id}\"");
                return;
            }
            if (++_paletteDepth > 32)
                throw new InvalidOperationException($"Palette nesting too deep at \"{id}\"");

            var palette = _db.Get("palette", id);
            AddParameters(palette["parameters"] as JsonObject);
            foreach (var child in Palettes(palette))
                AddPalette(child);
            AddSymbols(palette);
            _paletteDepth--;
        }

        public void AddSymbols(JsonObject source)
        {
            Merge(Terrain, source["terrain"] as JsonObject);
            Merge(Furniture, source["furniture"] as JsonObject);
            if (source["mapping"] is JsonObject mapping)
            {
                foreach (var (symbol, def) in mapping)
                {
                    if (def is not JsonObject m)
                        continue;
                    if (m["terrain"] is { } t)
                        Terrain[symbol] = t;
                    if (m["furniture"] is { } f)
                        Furniture[symbol] = f;
                }
            }
            // Shorthand tables that place one fixed piece of furniture or terrain per symbol.
            AddFixed(Furniture, source["toilets"] as JsonObject, "f_toilet");
            AddFixed(Furniture, source["vendingmachines"] as JsonObject, "f_vending_c");
            AddFixed(Terrain, source["gaspumps"] as JsonObject, "t_gas_pump");

            foreach (var (key, value) in source)
            {
                if (key is "terrain" or "furniture" or "parameters" || value is not JsonObject table)
                    continue;
                foreach (var (symbol, _) in table)
                    if (symbol.EnumerateRunes().Count() == 1)
                        OtherSymbols.Add(symbol);
            }
        }

        private static void Merge(Dictionary<string, JsonNode> target, JsonObject? source)
        {
            if (source is null)
                return;
            foreach (var (symbol, value) in source)
                if (value is not null)
                    target[symbol] = value;
        }

        private static void AddFixed(Dictionary<string, JsonNode> target, JsonObject? source, string id)
        {
            if (source is null)
                return;
            foreach (var (symbol, _) in source)
                target[symbol] = JsonValue.Create(id);
        }

        public string? Param(string name)
        {
            if (_paramValues.TryGetValue(name, out var cached))
                return cached;
            if (!_paramDefaults.TryGetValue(name, out var def))
                return null;
            _paramValues[name] = null; // guards against self-referencing defaults
            var value = Resolve(def);
            _paramValues[name] = value;
            return value;
        }

        /// <summary>
        /// Resolves a CDDA mapgen value to an id: a string, a weighted list (<c>["a", ["b", 3]]</c>),
        /// <c>{"param", "fallback"}</c>, <c>{"switch", "cases"}</c> or <c>{"distribution"}</c>.
        /// </summary>
        public string? Resolve(JsonNode? node)
        {
            switch (node)
            {
                case null:
                    return null;
                case JsonValue v when v.TryGetValue(out string? s):
                    return s;
                case JsonArray a:
                    return a.Count == 0 ? null : Resolve(PickFromList(a));
                case JsonObject o when o["param"] is JsonValue p:
                    return Param(p.GetValue<string>()) ?? Resolve(o["fallback"]);
                case JsonObject o when o["switch"] is JsonObject sw && o["cases"] is JsonObject cases:
                    return Resolve(sw) is { } key && cases[key] is { } chosen ? Resolve(chosen) : null;
                case JsonObject o when o["distribution"] is JsonArray dist:
                    return dist.Count == 0 ? null : Resolve(PickFromList(dist));
                default:
                    return null;
            }
        }

        private JsonNode? PickFromList(JsonArray list)
        {
            var options = new List<(JsonNode? Value, int Weight)>(list.Count);
            foreach (var item in list)
            {
                if (item is JsonArray pair && pair.Count == 2 && pair[1] is JsonValue w && w.TryGetValue(out double weight))
                    options.Add((pair[0], (int)weight));
                else
                    options.Add((item, 1));
            }
            return PickWeighted(options);
        }
    }
}
