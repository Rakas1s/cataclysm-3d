using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// Picks base colors for terrain and furniture. Material keywords in the id win (brick, grass,
/// concrete, …); otherwise CDDA's own color name is mapped to a muted, real-world-ish tone.
/// </summary>
public static class TilePalette
{
    private static readonly (string Keyword, Color Color)[] Materials =
    {
        ("brick", new Color(0.52f, 0.26f, 0.2f)),
        ("strconc", new Color(0.62f, 0.62f, 0.6f)),
        ("sconc", new Color(0.62f, 0.62f, 0.6f)),
        ("concrete", new Color(0.6f, 0.6f, 0.58f)),
        ("thconc", new Color(0.55f, 0.55f, 0.53f)),
        ("stairs", new Color(0.5f, 0.36f, 0.24f)),
        ("soil", new Color(0.36f, 0.27f, 0.19f)),
        ("sidewalk", new Color(0.62f, 0.62f, 0.6f)),
        ("pavement", new Color(0.25f, 0.25f, 0.26f)),
        ("grass_dead", new Color(0.55f, 0.5f, 0.3f)),
        ("grass", new Color(0.28f, 0.45f, 0.2f)),
        ("dirt", new Color(0.42f, 0.32f, 0.22f)),
        ("linoleum_white", new Color(0.85f, 0.85f, 0.82f)),
        ("linoleum_gray", new Color(0.62f, 0.63f, 0.62f)),
        ("carpet", new Color(0.45f, 0.35f, 0.3f)),
        ("t_floor", new Color(0.55f, 0.4f, 0.26f)),
        ("wood", new Color(0.5f, 0.35f, 0.22f)),
        ("shingle", new Color(0.3f, 0.28f, 0.27f)),
        ("roof", new Color(0.35f, 0.32f, 0.3f)),
        ("railing", new Color(0.6f, 0.55f, 0.45f)),
        ("curtains", new Color(0.45f, 0.3f, 0.3f)),
        ("fridge", new Color(0.88f, 0.88f, 0.86f)),
        ("washer", new Color(0.88f, 0.88f, 0.86f)),
        ("dryer", new Color(0.88f, 0.88f, 0.86f)),
        ("toilet", new Color(0.92f, 0.92f, 0.9f)),
        ("bathtub", new Color(0.92f, 0.92f, 0.9f)),
        ("sink", new Color(0.85f, 0.86f, 0.86f)),
        ("oven", new Color(0.3f, 0.3f, 0.32f)),
        ("sofa", new Color(0.4f, 0.32f, 0.45f)),
        ("armchair", new Color(0.45f, 0.3f, 0.25f)),
        ("counter", new Color(0.72f, 0.68f, 0.6f)),
        ("table", new Color(0.52f, 0.36f, 0.22f)),
        ("desk", new Color(0.48f, 0.34f, 0.22f)),
        ("chair", new Color(0.45f, 0.32f, 0.2f)),
        ("stool", new Color(0.4f, 0.3f, 0.2f)),
        ("cupboard", new Color(0.55f, 0.42f, 0.28f)),
        ("dresser", new Color(0.5f, 0.36f, 0.24f)),
        ("bed", new Color(0.7f, 0.7f, 0.75f)),
        ("plant", new Color(0.25f, 0.5f, 0.22f)),
        ("safe", new Color(0.2f, 0.22f, 0.22f)),
        ("cardboard", new Color(0.65f, 0.5f, 0.32f)),
    };

    private static readonly Dictionary<string, Color> CddaColors = new(StringComparer.Ordinal)
    {
        ["black"] = new(0.12f, 0.12f, 0.12f),
        ["white"] = new(0.86f, 0.86f, 0.84f),
        ["light_gray"] = new(0.66f, 0.66f, 0.64f),
        ["dark_gray"] = new(0.35f, 0.35f, 0.35f),
        ["red"] = new(0.55f, 0.2f, 0.18f),
        ["light_red"] = new(0.75f, 0.4f, 0.35f),
        ["green"] = new(0.28f, 0.45f, 0.22f),
        ["light_green"] = new(0.45f, 0.62f, 0.35f),
        ["blue"] = new(0.25f, 0.32f, 0.55f),
        ["light_blue"] = new(0.5f, 0.6f, 0.78f),
        ["cyan"] = new(0.3f, 0.55f, 0.58f),
        ["light_cyan"] = new(0.6f, 0.78f, 0.8f),
        ["magenta"] = new(0.55f, 0.3f, 0.55f),
        ["pink"] = new(0.8f, 0.55f, 0.65f),
        ["brown"] = new(0.45f, 0.32f, 0.2f),
        ["yellow"] = new(0.8f, 0.72f, 0.35f),
        ["brown_green"] = new(0.42f, 0.42f, 0.22f),
    };

    public static readonly Color Leaves = new(0.22f, 0.4f, 0.17f);
    public static readonly Color Bark = new(0.33f, 0.24f, 0.16f);
    public static readonly Color Glass = new(0.7f, 0.85f, 0.95f, 0.35f);
    public static readonly Color Ceiling = new(0.82f, 0.8f, 0.76f);

    public static Color For(TileType type)
    {
        // Painted walls keep their paint color rather than a material tone.
        if (!type.Id.StartsWith("t_wall_", StringComparison.Ordinal) || type.Id == "t_wall_wood")
        {
            foreach (var (keyword, color) in Materials)
                if (type.Id.Contains(keyword, StringComparison.Ordinal))
                    return color;
        }
        return FromCddaName(type.Color);
    }

    public static Color FromCddaName(string name)
    {
        // Strip curses-style prefixes: c_ (color), i_ (inverted), h_ (highlight).
        if (name.Length > 2 && name[1] == '_' && name[0] is 'c' or 'i' or 'h')
            name = name[2..];
        return CddaColors.TryGetValue(name, out var c) ? c : CddaColors["light_gray"];
    }
}
