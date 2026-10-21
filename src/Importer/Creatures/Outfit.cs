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

    /// <summary>
    /// Loads the profession and hair style JSON from the snapshot's <c>data/json</c>. The items
    /// come from <see cref="Items.ItemLoader.LoadContent"/>.
    /// </summary>
    public static void LoadContent(ContentDatabase db, string jsonRoot)
    {
        db.LoadFile(Path.Combine(jsonRoot, "professions.json"));
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
    /// A profession's starting kit (its "both" entries plus the male or female ones, groups
    /// rolled), as items, in the order the JSON lists them.
    /// </summary>
    public static List<Item> Starting(ContentDatabase db, ItemRegistry items, string profession, bool male, Random rng)
    {
        var kit = new List<Item>();
        if (!db.Contains("profession", profession))
            return kit;
        var lists = db.Get("profession", profession)["items"];
        var entries = new[] { "both", male ? "male" : "female" }
            .SelectMany(key => (lists?[key]?["entries"] as System.Text.Json.Nodes.JsonArray) ?? new())
            .OfType<System.Text.Json.Nodes.JsonObject>();
        foreach (var e in entries)
        {
            if (Json.FirstString(e["item"]) is { } id && items.Create(id, rng: rng) is { } item)
                kit.Add(item);
            else if (Json.FirstString(e["group"]) is { } group)
                kit.AddRange(items.Roll(group, rng));
        }
        return kit;
    }

    /// <summary>
    /// Puts a kit on: everything wearable is worn (sorted into its layer, innermost first, keeping
    /// the kit's order within a layer as CDDA does), then the rest goes into pockets. Returns
    /// what didn't fit anywhere.
    /// </summary>
    public static List<Item> Dress(Inventory inventory, IEnumerable<Item> kit)
    {
        var rest = new List<Item>();
        foreach (var item in kit)
        {
            if (item.Type.IsArmor && inventory.WhyCantWear(item) is null)
                inventory.Wear(item);
            else
                rest.Add(item);
        }
        return rest.Where(item => inventory.Stow(item) is null).ToList();
    }
}
