using System.Diagnostics;
using System.Text.Json.Nodes;
using Cataclysm3D.Importer.Tiles;
using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>A decoded sprite, already turned to the rotation it's drawn at. Pixels are RGBA8, row-major.</summary>
public sealed class SpritePixels
{
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>Pixels from the tile's top-left corner to the sprite's (CDDA sprite offsets).</summary>
    public int OffsetX { get; init; }
    public int OffsetY { get; init; }

    public required byte[] Rgba { get; init; }

    public byte Alpha(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? (byte)0 : Rgba[(y * Width + x) * 4 + 3];
    public bool Opaque(int x, int y) => Alpha(x, y) >= 128;

    public Color At(int x, int y)
    {
        int i = (Math.Clamp(y, 0, Height - 1) * Width + Math.Clamp(x, 0, Width - 1)) * 4;
        return Color.Color8(Rgba[i], Rgba[i + 1], Rgba[i + 2], Rgba[i + 3]);
    }

    private Color? _average;

    /// <summary>Mean color of the opaque pixels.</summary>
    public Color Average => _average ??= ComputeAverage();

    private Color ComputeAverage()
    {
        float r = 0, g = 0, b = 0;
        int n = 0;
        for (int i = 0; i < Rgba.Length; i += 4)
        {
            if (Rgba[i + 3] < 128)
                continue;
            r += Rgba[i];
            g += Rgba[i + 1];
            b += Rgba[i + 2];
            n++;
        }
        return n == 0 ? new Color(0.5f, 0.5f, 0.5f) : new Color(r / n / 255f, g / n / 255f, b / n / 255f);
    }

    /// <summary>Opaque bounds (x0, y0, x1, y1), exclusive, or null when fully transparent.</summary>
    public (int X0, int Y0, int X1, int Y1)? Bounds()
    {
        int x0 = Width, y0 = Height, x1 = -1, y1 = -1;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            if (!Opaque(x, y))
                continue;
            x0 = Math.Min(x0, x);
            y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x);
            y1 = Math.Max(y1, y);
        }
        return x1 < 0 ? null : (x0, y0, x1 + 1, y1 + 1);
    }
}

/// <summary>
/// The tileset side of rendering: resolves terrain and furniture ids to sprites through
/// <see cref="SpriteResolver"/> (tilesets from <c>config/tilesets.json</c>, then
/// <c>looks_like</c>), decodes the PNGs on demand and caches the pixels.
/// </summary>
public sealed class TileArt
{
    /// <summary>CDDA games start in spring; seasonal sprites follow it until the calendar exists.</summary>
    public const string Season = "spring";

    private readonly SpriteResolver _resolver;
    private readonly Dictionary<string, Image?> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<(Tileset, int, int), SpritePixels?> _pixels = new();

    public SurfaceAtlas Atlas { get; } = new();
    public FurnitureModels Models { get; }
    public long LoadMilliseconds { get; }
    public string Summary { get; }

    private TileArt(SpriteResolver resolver, long ms)
    {
        _resolver = resolver;
        Models = new FurnitureModels(this);
        LoadMilliseconds = ms;
        Summary = string.Join(", ", resolver.Tilesets.Select(t => $"{t.Name} ({t.Tiles.Count} ids)"));
    }

    /// <summary>Loads the tilesets listed in <c>config/tilesets.json</c>; null if none are fetched.</summary>
    public static TileArt? Load(string repoRoot, TileRegistry registry)
    {
        var clock = Stopwatch.StartNew();
        var sets = new List<Tileset>();
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(repoRoot, "config", "tilesets.json")));
        foreach (var t in config?["tilesets"] as JsonArray ?? new JsonArray())
        {
            if (t?["path"]?.GetValue<string>() is not { } rel)
                continue;
            string path = Path.Combine(repoRoot, rel);
            if (!Directory.Exists(path))
            {
                GD.PushWarning($"Tileset {rel} not found; run tools/fetch-cdda-snapshot.ps1");
                continue;
            }
            sets.Add(Tileset.Load(path, t["name"]?.GetValue<string>()));
        }
        if (sets.Count == 0)
            return null;

        string? LooksLike(string id) =>
            registry.TryTerrain(id, out int t) ? registry.Terrain[t].LooksLike
            : registry.TryFurniture(id, out int f) ? registry.Furniture[f].LooksLike
            : null;
        return new TileArt(new SpriteResolver(sets, LooksLike), clock.ElapsedMilliseconds);
    }

    public ResolvedTile? Resolve(TileType type) => _resolver.Resolve(type.Id, Season);

    /// <summary>The sprite drawn for a tile, or null if no tileset has art for it.</summary>
    /// <param name="primaryOnly">Only take art from the first tileset (and its looks_like chain).</param>
    public SpriteChoice? Choose(TileType type, int x, int y, int connections = 0, int rotation = 0, bool primaryOnly = false)
    {
        if ((primaryOnly ? _resolver.ResolvePrimary(type.Id, Season) : Resolve(type)) is not { } hit)
            return null;
        var c = SpriteResolver.Choose(hit, x, y, connections, rotation);
        return c.Fg < 0 && c.Bg < 0 ? null : c;
    }

    /// <summary>Mean color of a tile's main sprite, for shapes that aren't drawn from the sprite itself.</summary>
    public Color? AverageColor(TileType type)
    {
        if (Choose(type, 0, 0) is not { } c)
            return null;
        var px = Pixels(c.Tileset, c.Fg >= 0 ? c.Fg : c.Bg, 0);
        return px?.Average;
    }

    /// <summary>Decoded pixels of a sprite, turned <paramref name="rotation"/> quarter turns clockwise.</summary>
    public SpritePixels? Pixels(Tileset set, int index, int rotation)
    {
        if (index < 0)
            return null;
        rotation &= 3;
        if (_pixels.TryGetValue((set, index, rotation), out var hit))
            return hit;
        SpritePixels? result = null;
        if (rotation != 0)
        {
            if (Pixels(set, index, 0) is { } upright)
                result = Rotate(upright, rotation);
        }
        else
        {
            var rect = set.Sprite(index);
            if (Image(rect.File) is { } image && rect.X + rect.Width <= image.GetWidth() && rect.Y + rect.Height <= image.GetHeight())
            {
                var region = image.GetRegion(new Rect2I(rect.X, rect.Y, rect.Width, rect.Height));
                result = new SpritePixels
                {
                    Width = rect.Width,
                    Height = rect.Height,
                    OffsetX = rect.OffsetX,
                    OffsetY = rect.OffsetY,
                    Rgba = region.GetData(),
                };
            }
        }
        _pixels[(set, index, rotation)] = result;
        return result;
    }

    private Image? Image(string file)
    {
        if (_files.TryGetValue(file, out var image))
            return image;
        image = Godot.Image.LoadFromFile(file);
        if (image is not null && !image.IsEmpty())
        {
            image.Decompress();
            image.Convert(Godot.Image.Format.Rgba8);
        }
        else
        {
            image = null;
        }
        _files[file] = image;
        return image;
    }

    /// <summary>Turns a sprite clockwise in quarter turns. Only square sprites keep their offsets.</summary>
    private static SpritePixels Rotate(SpritePixels s, int quarterTurns)
    {
        var src = s;
        for (int q = 0; q < quarterTurns; q++)
        {
            int w = src.Height, h = src.Width;
            var dst = new byte[src.Rgba.Length];
            for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                // (x, y) → (H-1-y, x)
                int nx = src.Height - 1 - y, ny = x;
                Array.Copy(src.Rgba, (y * src.Width + x) * 4, dst, (ny * w + nx) * 4, 4);
            }
            src = new SpritePixels { Width = w, Height = h, OffsetX = s.OffsetX, OffsetY = s.OffsetY, Rgba = dst };
        }
        return src;
    }
}

/// <summary>
/// One big texture holding the sprites drawn on voxel faces (floors, walls, roofs), in 32×32
/// slots. Slot 0 is plain white, for voxels that only have a color. Slots are added as tiles
/// need them and the texture is re-uploaded before the next mesh build.
/// </summary>
public sealed class SurfaceAtlas
{
    public const int SlotSize = 32;
    public const int Size = 2048;
    private const int PerRow = Size / SlotSize;

    private readonly Image _image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
    private readonly Dictionary<object, int> _slots = new();
    private bool _dirty = true;

    public ImageTexture Texture { get; }

    public SurfaceAtlas()
    {
        _image.FillRect(new Rect2I(0, 0, SlotSize, SlotSize), Colors.White);
        Texture = ImageTexture.CreateFromImage(_image);
    }

    public int Count => _slots.Count + 1;

    /// <summary>The slot for <paramref name="key"/>, painting it with <paramref name="paint"/> the first time.</summary>
    public int SlotFor(object key, Func<Image?> paint)
    {
        if (_slots.TryGetValue(key, out int slot))
            return slot;
        slot = 0;
        if (_slots.Count + 1 < PerRow * PerRow && paint() is { } img)
        {
            slot = _slots.Count + 1;
            var at = Origin(slot);
            _image.BlitRect(img, new Rect2I(0, 0, SlotSize, SlotSize), at);
            _dirty = true;
        }
        _slots[key] = slot;
        return slot;
    }

    public static Vector2I Origin(int slot) => new(slot % PerRow * SlotSize, slot / PerRow * SlotSize);

    /// <summary>Uploads newly added slots.</summary>
    public void Flush()
    {
        if (!_dirty)
            return;
        Texture.Update(_image);
        _dirty = false;
    }
}
