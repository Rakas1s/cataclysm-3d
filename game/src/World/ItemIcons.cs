using Cataclysm3D.Sim.Items;
using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// Item sprites as UI icons (the tileset's sprite for the item, through its <c>looks_like</c>
/// chain), and CDDA's color names, for items that have no sprite.
/// </summary>
public static class ItemIcons
{
    private static readonly Dictionary<string, Texture2D?> Cache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Texture2D> Cropped = new(StringComparer.Ordinal);

    public static TileArt? Art { get; set; }

    /// <summary>The icon for an item type, or a small square in its CDDA color.</summary>
    public static Texture2D Icon(ItemType type)
    {
        if (Cache.TryGetValue(type.Id, out var hit) && hit is not null)
            return hit;
        Texture2D? texture = null;
        if (Art?.ChooseAny(0, type.Id) is { } choice && Art.Pixels(choice.Tileset, choice.Fg, 0) is { } px)
            texture = ImageTexture.CreateFromImage(Image.CreateFromData(px.Width, px.Height, false, Image.Format.Rgba8, px.Rgba));
        if (texture is null)
        {
            var image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
            image.FillRect(new Rect2I(10, 10, 12, 12), CddaColor(type.Color));
            texture = ImageTexture.CreateFromImage(image);
        }
        Cache[type.Id] = texture;
        return texture;
    }

    /// <summary>
    /// The icon cropped to its opaque pixels (in a square, so it isn't stretched), for showing an
    /// item large: a bottle drawn small in its tile fills the frame.
    /// </summary>
    public static Texture2D LargeIcon(ItemType type)
    {
        if (Cropped.TryGetValue(type.Id, out var hit))
            return hit;
        Texture2D texture = Icon(type);
        if (Art?.ChooseAny(0, type.Id) is { } choice && Art.Pixels(choice.Tileset, choice.Fg, 0) is { } px && px.Bounds() is { } b)
        {
            int size = Math.Max(b.X1 - b.X0, b.Y1 - b.Y0);
            var square = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            var full = Image.CreateFromData(px.Width, px.Height, false, Image.Format.Rgba8, px.Rgba);
            square.BlitRect(full, new Rect2I(b.X0, b.Y0, b.X1 - b.X0, b.Y1 - b.Y0),
                new Vector2I((size - (b.X1 - b.X0)) / 2, (size - (b.Y1 - b.Y0)) / 2));
            texture = ImageTexture.CreateFromImage(square);
        }
        Cropped[type.Id] = texture;
        return texture;
    }

    /// <summary>CDDA's curses color names, roughly as its default palette draws them.</summary>
    public static Color CddaColor(string name) => name.Replace("light_", "l_").Replace("dark_", "d_") switch
    {
        "black" => new Color(0.15f, 0.15f, 0.15f),
        "red" => new Color(0.75f, 0.15f, 0.15f),
        "l_red" => new Color(1f, 0.4f, 0.4f),
        "green" => new Color(0.2f, 0.6f, 0.2f),
        "l_green" => new Color(0.45f, 0.9f, 0.45f),
        "blue" => new Color(0.2f, 0.3f, 0.8f),
        "l_blue" => new Color(0.45f, 0.6f, 1f),
        "cyan" => new Color(0.2f, 0.65f, 0.7f),
        "l_cyan" => new Color(0.5f, 0.9f, 0.95f),
        "magenta" or "pink" => new Color(0.75f, 0.3f, 0.7f),
        "l_magenta" => new Color(1f, 0.55f, 0.95f),
        "brown" => new Color(0.55f, 0.35f, 0.15f),
        "yellow" => new Color(0.95f, 0.85f, 0.3f),
        "l_gray" => new Color(0.72f, 0.72f, 0.72f),
        "d_gray" => new Color(0.4f, 0.4f, 0.4f),
        _ => new Color(0.9f, 0.9f, 0.9f),
    };
}
