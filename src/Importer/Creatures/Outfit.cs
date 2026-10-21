using Cataclysm3D.Sim.Creatures;
using Cataclysm3D.Sim.Items;

namespace Cataclysm3D.Importer.Creatures;

/// <summary>
/// How a new survivor looks: the clothes a CDDA profession starts you in (<c>professions.json</c>
/// and the armor item JSON) and a hair style trait (<c>appearance_hair_styles.json</c>).
/// </summary>
public static class Outfit
{
    public const string DefaultProfession = "unemployed";

    /// <summary>Hair colors a new survivor rolls from; CDDA also offers dyed ones (blue, green, pink, purple).</summary>
    public static readonly string[] NaturalHairColors = { "black", "blond", "brown", "red", "gray", "white" };

    /// <summary>Loads the profession and armor item JSON from the snapshot's <c>data/json</c>.</summary>
    public static void LoadContent(ContentDatabase db, string jsonRoot)
    {
        db.LoadFile(Path.Combine(jsonRoot, "professions.json"));
        db.LoadDirectory(Path.Combine(jsonRoot, "items", "armor"));
        db.LoadFile(Path.Combine(jsonRoot, "mutations", "appearance_hair_styles.json"));
    }

    /// <summary>
    /// A random hair style from CDDA's starting <c>hair_style</c> traits (not bald), in one of its
    /// natural color variants. Null when no hair styles are loaded.
    /// </summary>
    public static Hair? RandomHair(ContentDatabase db, Random rng)
    {
        var styles = new List<(string Id, List<string> Colors)>();
        foreach (var (id, data) in db.All("mutation"))
        {
            if (!Json.Strings(data["types"]).Contains("hair_style") || data["variants"] is not System.Text.Json.Nodes.JsonArray variants)
                continue;
            var colors = variants.Select(v => Json.FirstString(v?["id"])).OfType<string>()
                .Where(c => NaturalHairColors.Contains(c)).ToList();
            if (colors.Count > 0)
                styles.Add((id, colors));
        }
        if (styles.Count == 0)
            return null;
        styles.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        var (style, choices) = styles[rng.Next(styles.Count)];
        return new Hair(style, choices[rng.Next(choices.Count)]);
    }

    /// <summary>
    /// The wearable items in a profession's starting kit (its "both" entries plus the male or
    /// female ones), innermost layer first. Item groups and things you carry rather than wear
    /// (knives, phones, water) are left out.
    /// </summary>
    public static IReadOnlyList<Clothing> Starting(ContentDatabase db, string profession, bool male)
    {
        if (!db.Contains("profession", profession))
            return Array.Empty<Clothing>();
        var items = db.Get("profession", profession)["items"];
        var ids = new[] { "both", male ? "male" : "female" }
            .SelectMany(key => (items?[key]?["entries"] as System.Text.Json.Nodes.JsonArray) ?? new())
            .Select(e => Json.FirstString(e?["item"]))
            .OfType<string>();
        var worn = new List<Clothing>();
        foreach (var id in ids)
        {
            if (!db.Contains(ContentDatabase.ItemFamily, id))
                continue;
            var item = db.Get(ContentDatabase.ItemFamily, id);
            if (item["armor"] is null)
                continue;
            var flags = Json.Strings(item["flags"]);
            var layer = flags.Contains("SKINTIGHT") ? WornLayer.Skintight
                : flags.Contains("BELTED") ? WornLayer.Belted
                : WornLayer.Normal;
            worn.Add(new Clothing(id, Json.Name(item, id), layer));
        }
        // A stable sort keeps the kit's order within a layer, as CDDA does when you put things on.
        return worn.OrderBy(c => c.Layer).ToList();
    }
}
