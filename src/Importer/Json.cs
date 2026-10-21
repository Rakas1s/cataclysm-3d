using System.Text.Json.Nodes;

namespace Cataclysm3D.Importer;

/// <summary>Readers for CDDA's loosely typed JSON fields.</summary>
internal static class Json
{
    /// <summary>CDDA names are either a string or { "str": ... } / { "str_sp": ... } (same singular and plural).</summary>
    public static string Name(JsonObject data, string fallback) => data["name"] switch
    {
        JsonValue v when v.TryGetValue(out string? s) => s,
        JsonObject o when (o["str"] ?? o["str_sp"]) is JsonValue sv && sv.TryGetValue(out string? s2) => s2,
        _ => fallback,
    };

    /// <summary>A string, or the first of an array of them.</summary>
    public static string? FirstString(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out string? s) => s,
        JsonArray a when a.Count > 0 => FirstString(a[0]),
        _ => null,
    };

    /// <summary>A string or an array of strings, as a set.</summary>
    public static HashSet<string> Strings(JsonNode? node) => node switch
    {
        JsonArray a => a.Select(FirstString).OfType<string>().ToHashSet(StringComparer.Ordinal),
        JsonValue v when v.TryGetValue(out string? s) => new HashSet<string>(StringComparer.Ordinal) { s },
        _ => new HashSet<string>(StringComparer.Ordinal),
    };
}
