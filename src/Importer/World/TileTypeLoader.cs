using System.Text.Json.Nodes;
using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Importer.World;

/// <summary>Builds a <see cref="TileRegistry"/> from CDDA "terrain" and "furniture" definitions.</summary>
public static class TileTypeLoader
{
    public static TileRegistry Load(ContentDatabase db)
    {
        var registry = new TileRegistry();
        foreach (var (id, data) in db.All("terrain"))
        {
            if (id == TileRegistry.NullTerrain)
                continue;
            registry.Add(new TerrainType
            {
                Id = id,
                Name = Name(data, id),
                Symbol = FirstString(data["symbol"]) ?? "?",
                Color = FirstString(data["color"]) ?? "white",
                LooksLike = FirstString(data["looks_like"]),
                Coverage = Int(data["coverage"]),
                Flags = Flags(data),
                Open = FirstString(data["open"]),
                Close = FirstString(data["close"]),
                MoveCost = Int(data["move_cost"], 2),
                Roof = FirstString(data["roof"]),
                Bash = Bash(data),
                LightEmitted = Int(data["light_emitted"]),
            });
        }
        foreach (var (id, data) in db.All("furniture"))
        {
            if (id == TileRegistry.NullFurniture)
                continue;
            registry.Add(new FurnitureType
            {
                Id = id,
                Name = Name(data, id),
                Symbol = FirstString(data["symbol"]) ?? "?",
                Color = FirstString(data["color"]) ?? "white",
                LooksLike = FirstString(data["looks_like"]),
                Coverage = Int(data["coverage"]),
                Flags = Flags(data),
                Open = FirstString(data["open"]),
                Close = FirstString(data["close"]),
                MoveCostMod = Int(data["move_cost_mod"]),
                Bash = Bash(data),
                LightEmitted = Int(data["light_emitted"]),
            });
        }
        return registry;
    }

    private static BashInfo? Bash(JsonObject data)
    {
        if (data["bash"] is not JsonObject b)
            return null;
        // Sounds may be plain strings or translatable { "str": ... } objects.
        return new BashInfo(
            Int(b["str_min"]),
            Int(b["str_max"], Int(b["str_min"])),
            FirstString(b["ter_set"]),
            FirstString(b["furn_set"]),
            Text(b["sound"]) ?? "smash!",
            Text(b["sound_fail"]) ?? "thump!");
    }

    private static string? Text(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out string? s) => s,
        JsonObject o => FirstString(o["str"]),
        _ => null,
    };

    /// <summary>CDDA names are either a string or { "str": ..., "str_pl": ... }.</summary>
    private static string Name(JsonObject data, string fallback) => data["name"] switch
    {
        JsonValue v when v.TryGetValue(out string? s) => s,
        JsonObject o when o["str"] is JsonValue sv && sv.TryGetValue(out string? s2) => s2,
        _ => fallback,
    };

    /// <summary>Symbols and colors may be a string or a per-season array; take the first.</summary>
    private static string? FirstString(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out string? s) => s,
        JsonArray a when a.Count > 0 => FirstString(a[0]),
        _ => null,
    };

    private static int Int(JsonNode? node, int fallback = 0) =>
        node is JsonValue v && v.TryGetValue(out double d) ? (int)d : fallback;

    private static HashSet<string> Flags(JsonObject data) =>
        data["flags"] is JsonArray a
            ? a.Select(FirstString).OfType<string>().ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
}
