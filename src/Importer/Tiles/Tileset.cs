using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cataclysm3D.Importer.Tiles;

/// <summary>Where one sprite lives: a rectangle of a PNG, and how it's drawn relative to its tile.</summary>
/// <param name="File">Absolute path of the PNG.</param>
/// <param name="OffsetX">CDDA's <c>sprite_offset_x</c>: pixels from the tile's left edge to the sprite's.</param>
/// <param name="OffsetY">CDDA's <c>sprite_offset_y</c>: pixels from the tile's top edge to the sprite's (negative for tall sprites).</param>
public readonly record struct SpriteRect(string File, int X, int Y, int Width, int Height, int OffsetX, int OffsetY);

/// <summary>
/// One weighted choice in a <c>fg</c>/<c>bg</c> list. <see cref="Sprites"/> holds one sprite per
/// rotation (CDDA's list form), or a single sprite.
/// </summary>
public sealed record SpriteVariant(int Weight, IReadOnlyList<int> Sprites);

/// <summary>A <c>fg</c> or <c>bg</c> value: weighted variants of sprite indices into the tileset.</summary>
public sealed class SpriteList
{
    public static readonly SpriteList Empty = new(Array.Empty<SpriteVariant>());

    public SpriteList(IReadOnlyList<SpriteVariant> variants) => Variants = variants;

    public IReadOnlyList<SpriteVariant> Variants { get; }
    public bool IsEmpty => Variants.Count == 0;
}

/// <summary>One <c>tiles</c> entry: the sprites for an id, with multitile subtiles if it connects.</summary>
public sealed class TileEntry
{
    public required string Id { get; init; }
    public SpriteList Fg { get; init; } = SpriteList.Empty;
    public SpriteList Bg { get; init; } = SpriteList.Empty;
    public bool Rotates { get; init; }
    public bool Multitile { get; init; }

    /// <summary>Multitile pieces by name: center, corner, edge, end_piece, t_connection, unconnected, …</summary>
    public IReadOnlyDictionary<string, TileEntry> Subtiles { get; init; } = new Dictionary<string, TileEntry>();
}

/// <summary>
/// A CDDA tileset: tile size, sprites and the id → sprite table. Reads both layouts CDDA tilesets
/// ship in: a packed <c>tile_config.json</c> with sprite sheets (e.g. Ultica), and the unpacked
/// "compose" layout of one PNG + JSON per sprite under <c>pngs_*</c> folders with a
/// <c>tile_info.json</c> (e.g. UndeadPeople). No images are decoded here; <see cref="SpriteRect"/>
/// says where to find each sprite.
/// </summary>
public sealed class Tileset
{
    private readonly List<SpriteRect> _sprites = new();
    private readonly Dictionary<string, TileEntry> _tiles = new(StringComparer.Ordinal);

    public string Name { get; }
    public string Root { get; }
    public int TileWidth { get; private set; } = 32;
    public int TileHeight { get; private set; } = 32;

    public IReadOnlyDictionary<string, TileEntry> Tiles => _tiles;
    public IReadOnlyList<SpriteRect> Sprites => _sprites;

    private Tileset(string name, string root)
    {
        Name = name;
        Root = root;
    }

    public bool TryGet(string id, out TileEntry entry) => _tiles.TryGetValue(id, out entry!);

    public SpriteRect Sprite(int index) => _sprites[index];

    /// <summary>Loads whichever layout <paramref name="root"/> holds.</summary>
    public static Tileset Load(string root, string? name = null)
    {
        name ??= Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        if (File.Exists(Path.Combine(root, "tile_config.json")))
            return LoadPacked(root, name);
        if (File.Exists(Path.Combine(root, "tile_info.json")))
            return LoadUnpacked(root, name);
        throw new FileNotFoundException($"No tile_config.json or tile_info.json in {root}");
    }

    // ---- Packed: tile_config.json + sprite sheets -------------------------------------------

    private static Tileset LoadPacked(string root, string name)
    {
        var set = new Tileset(name, root);
        var config = Parse(File.ReadAllText(Path.Combine(root, "tile_config.json")));
        if (config?["tile_info"] is JsonArray { Count: > 0 } info && info[0] is JsonObject ti)
        {
            set.TileWidth = Int(ti["width"], 32);
            set.TileHeight = Int(ti["height"], 32);
        }

        // Sprite indices run on across sheets in file order; each sheet is a grid of equal sprites.
        var sheets = new List<(JsonObject Sheet, bool Filler)>();
        foreach (var node in config?["tiles-new"] as JsonArray ?? new JsonArray())
        {
            if (node is not JsonObject sheet || sheet["file"]?.GetValue<string>() is not { } file)
                continue;
            int w = Int(sheet["sprite_width"], set.TileWidth), h = Int(sheet["sprite_height"], set.TileHeight);
            int ox = Int(sheet["sprite_offset_x"]), oy = Int(sheet["sprite_offset_y"]);
            string path = Path.Combine(root, file);
            // The fallback ASCII sheet has no sprite indices of its own.
            if (sheet["ascii"] is not null || !File.Exists(path))
            {
                sheets.Add((sheet, false));
                continue;
            }
            var (iw, ih) = PngSize(path);
            int cols = iw / w, rows = ih / h;
            for (int i = 0; i < cols * rows; i++)
                set._sprites.Add(new SpriteRect(path, i % cols * w, i / cols * h, w, h, ox, oy));
            sheets.Add((sheet, file.Contains("filler", StringComparison.OrdinalIgnoreCase)));
        }

        // Filler sheets only supply ids nothing else drew.
        foreach (bool filler in new[] { false, true })
        foreach (var (sheet, isFiller) in sheets)
        {
            if (isFiller != filler)
                continue;
            foreach (var t in sheet["tiles"] as JsonArray ?? new JsonArray())
                if (t is JsonObject o)
                    set.AddEntries(o, s => s is JsonValue v && v.TryGetValue(out int i) && i >= 0 && i < set._sprites.Count ? i : null, filler);
        }
        return set;
    }

    // ---- Unpacked: pngs_<sheet>_<w>x<h>/**.png + .json, tile_info.json ---------------------

    private static Tileset LoadUnpacked(string root, string name)
    {
        var set = new Tileset(name, root);
        var info = Parse(File.ReadAllText(Path.Combine(root, "tile_info.json"))) as JsonArray ?? new JsonArray();
        var sheetInfo = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in info)
        {
            if (node is not JsonObject o)
                continue;
            if (o.ContainsKey("width"))
            {
                set.TileWidth = Int(o["width"], 32);
                set.TileHeight = Int(o["height"], 32);
                continue;
            }
            foreach (var (file, value) in o)
                sheetInfo[Path.GetFileNameWithoutExtension(file)] = value as JsonObject ?? new JsonObject();
        }

        // First pass: every PNG becomes a sprite, named by its file name (names are unique per tileset).
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        var jsonFiles = new List<(string Path, bool Filler)>();
        foreach (var dir in Directory.EnumerateDirectories(root, "pngs_*").Order(StringComparer.Ordinal))
        {
            string sheetName = SheetName(Path.GetFileName(dir));
            sheetInfo.TryGetValue(sheetName, out var si);
            if (si is not null && si["fallback"] is not null)
                continue;
            int w = Int(si?["sprite_width"], set.TileWidth), h = Int(si?["sprite_height"], set.TileHeight);
            int ox = Int(si?["sprite_offset_x"]), oy = Int(si?["sprite_offset_y"]);
            bool filler = si?["filler"] is JsonValue fv && fv.TryGetValue(out bool f) && f;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                if (file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    string spriteName = Path.GetFileNameWithoutExtension(file);
                    if (byName.ContainsKey(spriteName))
                        continue;
                    byName[spriteName] = set._sprites.Count;
                    set._sprites.Add(new SpriteRect(file, 0, 0, w, h, ox, oy));
                }
                else if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    jsonFiles.Add((file, filler));
                }
            }
        }

        int? Lookup(JsonNode? s) =>
            s is JsonValue v && v.TryGetValue(out string? n) && n is not null && byName.TryGetValue(n, out int i) ? i : null;
        foreach (bool filler in new[] { false, true })
        foreach (var (file, isFiller) in jsonFiles)
        {
            if (isFiller != filler)
                continue;
            JsonNode? doc;
            try
            {
                doc = Parse(File.ReadAllText(file));
            }
            catch (JsonException)
            {
                continue;
            }
            foreach (var o in doc is JsonArray a ? a.OfType<JsonObject>() : doc is JsonObject single ? new[] { single } : Array.Empty<JsonObject>())
                set.AddEntries(o, Lookup, filler);
        }
        return set;
    }

    /// <summary>"pngs_normal_terrain_32x32" → "normal_terrain", the key tile_info.json uses.</summary>
    public static string SheetName(string folder)
    {
        string s = folder.StartsWith("pngs_", StringComparison.Ordinal) ? folder[5..] : folder;
        int us = s.LastIndexOf('_');
        if (us > 0 && s[(us + 1)..].Contains('x') && char.IsDigit(s[us + 1]))
            s = s[..us];
        return s;
    }

    // ---- Entries --------------------------------------------------------------------------

    private void AddEntries(JsonObject o, Func<JsonNode?, int?> sprite, bool onlyIfMissing)
    {
        var ids = o["id"] switch
        {
            JsonArray arr => arr.Select(n => n?.GetValue<string>()).OfType<string>().ToList(),
            JsonValue v when v.TryGetValue(out string? s) => new List<string> { s },
            _ => new List<string>(),
        };
        foreach (var id in ids)
        {
            if (onlyIfMissing && _tiles.ContainsKey(id))
                continue;
            _tiles[id] = ParseEntry(id, o, sprite);
        }
    }

    private static TileEntry ParseEntry(string id, JsonObject o, Func<JsonNode?, int?> sprite)
    {
        var subtiles = new Dictionary<string, TileEntry>(StringComparer.Ordinal);
        foreach (var st in o["additional_tiles"] as JsonArray ?? new JsonArray())
            if (st is JsonObject so && so["id"]?.GetValue<string>() is { } sid)
                subtiles[sid] = ParseEntry(sid, so, sprite);
        return new TileEntry
        {
            Id = id,
            Fg = ParseList(o["fg"], sprite),
            Bg = ParseList(o["bg"], sprite),
            Rotates = Bool(o["rotates"]),
            Multitile = Bool(o["multitile"]) && subtiles.Count > 0,
            Subtiles = subtiles,
        };
    }

    /// <summary>
    /// fg/bg forms: a sprite; a list of sprites (one per rotation); or a list of
    /// <c>{ "weight", "sprite" }</c> where sprite is itself either form.
    /// </summary>
    internal static SpriteList ParseList(JsonNode? node, Func<JsonNode?, int?> sprite)
    {
        static List<int> Sprites(JsonNode? n, Func<JsonNode?, int?> sprite) => n is JsonArray a
            ? a.Select(sprite).OfType<int>().ToList()
            : sprite(n) is { } i ? new List<int> { i } : new List<int>();

        switch (node)
        {
            case null:
                return SpriteList.Empty;
            case JsonArray arr when arr.Count > 0 && arr[0] is JsonObject:
                var variants = new List<SpriteVariant>();
                foreach (var v in arr.OfType<JsonObject>())
                {
                    var s = Sprites(v["sprite"], sprite);
                    if (s.Count > 0)
                        variants.Add(new SpriteVariant(Math.Max(1, Int(v["weight"], 1)), s));
                }
                return variants.Count == 0 ? SpriteList.Empty : new SpriteList(variants);
            default:
                var list = Sprites(node, sprite);
                return list.Count == 0 ? SpriteList.Empty : new SpriteList(new[] { new SpriteVariant(1, list) });
        }
    }

    // ---- Helpers --------------------------------------------------------------------------

    private static readonly JsonDocumentOptions JsonOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static JsonNode? Parse(string text) => JsonNode.Parse(text, documentOptions: JsonOptions);

    private static int Int(JsonNode? n, int fallback = 0) =>
        n is JsonValue v && v.TryGetValue(out int i) ? i
        : n is JsonValue d && d.TryGetValue(out double x) ? (int)x
        : fallback;

    private static bool Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue(out bool b) && b;

    /// <summary>Width and height from a PNG's IHDR chunk, without decoding the image.</summary>
    internal static (int Width, int Height) PngSize(string path)
    {
        Span<byte> head = stackalloc byte[24];
        using var f = File.OpenRead(path);
        f.ReadExactly(head);
        static int Be(ReadOnlySpan<byte> b) => b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3];
        return (Be(head[16..20]), Be(head[20..24]));
    }
}
