using System.Globalization;
using System.Text.RegularExpressions;

namespace Cataclysm3D.Importer;

/// <summary>Parses CDDA unit strings such as "250 g", "1 L 500 ml", "2 kg" or "10 USD".</summary>
public static partial class Units
{
    [GeneratedRegex(@"(-?\d+(?:\.\d+)?)\s*([a-zA-Z_]+)")]
    private static partial Regex QuantityRegex();

    /// <summary>A unit dimension: unit name → factor to its base unit.</summary>
    public sealed record Dimension(string Name, string BaseUnit, IReadOnlyDictionary<string, double> Factors);

    public static readonly Dimension Mass = new("mass", "mg", new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["mg"] = 1, ["g"] = 1_000, ["kg"] = 1_000_000,
    });

    public static readonly Dimension Volume = new("volume", "ml", new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        ["ml"] = 1, ["L"] = 1_000,
    });

    public static readonly Dimension Money = new("money", "cent", new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["cent"] = 1, ["USD"] = 100, ["kUSD"] = 100_000,
    });

    public static readonly Dimension Energy = new("energy", "mJ", new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["mJ"] = 1, ["J"] = 1_000, ["kJ"] = 1_000_000,
    });

    public static readonly Dimension Length = new("length", "mm", new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["mm"] = 1, ["cm"] = 10, ["meter"] = 1_000, ["m"] = 1_000, ["km"] = 1_000_000,
    });

    public static readonly Dimension Time = new("time", "s", new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["turns"] = 1, ["turn"] = 1, ["t"] = 1, ["s"] = 1, ["seconds"] = 1,
        ["m"] = 60, ["minutes"] = 60, ["h"] = 3_600, ["hours"] = 3_600, ["d"] = 86_400, ["days"] = 86_400,
    });

    /// <summary>Tried in order when guessing a string's dimension; "m" is meters before minutes.</summary>
    private static readonly Dimension[] All = { Mass, Volume, Money, Energy, Length, Time };

    public static long ParseMassMilligrams(string text) => (long)Math.Round(Parse(text, Mass));

    public static long ParseVolumeMilliliters(string text) => (long)Math.Round(Parse(text, Volume));

    public static double Parse(string text, Dimension dimension)
    {
        if (!TryParse(text, dimension, out double value))
            throw new FormatException($"Not a {dimension.Name}: \"{text}\"");
        return value;
    }

    public static bool TryParse(string text, Dimension dimension, out double value)
    {
        value = 0;
        var matches = QuantityRegex().Matches(text);
        if (matches.Count == 0)
            return false;
        foreach (Match m in matches)
        {
            if (!dimension.Factors.TryGetValue(m.Groups[2].Value, out double factor))
                return false;
            value += double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * factor;
        }
        return true;
    }

    /// <summary>Finds the dimension of a unit string, e.g. "250 g" → mass.</summary>
    public static bool TryParseAny(string text, out Dimension dimension, out double value)
    {
        foreach (var d in All)
        {
            if (TryParse(text, d, out value))
            {
                dimension = d;
                return true;
            }
        }
        dimension = Mass;
        value = 0;
        return false;
    }

    public static string Format(double value, Dimension dimension) =>
        $"{Math.Round(value).ToString(CultureInfo.InvariantCulture)} {dimension.BaseUnit}";
}
