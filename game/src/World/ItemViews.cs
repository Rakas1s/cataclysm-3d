using Cataclysm3D.Sim;
using Cataclysm3D.Sim.Items;
using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// Draws the items lying on map tiles as small upright billboards of their tileset sprites that
/// turn to face you, like the monsters, a few per pile, standing on the floor or on top of the
/// furniture they're on (tables, counters, beds). Items inside container
/// furniture (fridges, dressers, cupboards) aren't drawn, as you can't see into those. Piles show
/// where you can see or remember them, like corpses; remembered ones are dimmed.
/// </summary>
public sealed partial class ItemViews : Node3D
{
    /// <summary>Meters per sprite pixel: a 32-pixel item sprite covers half a tile.</summary>
    private const float PixelSize = 1f / 64;

    /// <summary>How many items of a pile are drawn.</summary>
    private const int MaxShown = 3;

    private static readonly Color Remembered = new(0.35f, 0.35f, 0.35f);

    private readonly Dictionary<TilePos, Node3D> _piles = new();
    private readonly HashSet<TilePos> _dirty = new();
    private readonly Dictionary<(object, int), (ImageTexture Texture, int Height)?> _textures = new();
    private MapItems? _items;
    private WorldMap? _map;

    public TileArt? Art { get; set; }

    /// <summary>Shows a new map's items (a new town).</summary>
    public void Display(WorldMap map)
    {
        if (_items is not null)
            _items.Changed -= OnChanged;
        foreach (var child in GetChildren())
            child.QueueFree();
        _piles.Clear();
        _dirty.Clear();
        _map = map;
        _items = map.Items;
        _items.Changed += OnChanged;
        foreach (var (pos, _) in _items.All)
            _dirty.Add(pos);
    }

    private void OnChanged(TilePos p) => _dirty.Add(p);

    /// <summary>Rebuilds changed piles and applies fog. Call every frame.</summary>
    public void Sync(GameWorld game)
    {
        if (_items is null || _map is null)
            return;
        foreach (var p in _dirty)
        {
            if (_piles.Remove(p, out var old))
                old.QueueFree();
            if (Pile(p) is { } node)
            {
                _piles[p] = node;
                AddChild(node);
            }
        }
        _dirty.Clear();

        var sight = game.Sight;
        foreach (var (p, node) in _piles)
        {
            bool visible = sight.IsVisible(p);
            node.Visible = visible || sight.HasSeen(p);
            foreach (var child in node.GetChildren())
                if (child is SpriteBase3D s)
                    s.Modulate = visible ? Colors.White : Remembered;
        }
    }

    /// <summary>The sprites for a tile's pile, or null when nothing on it shows.</summary>
    private Node3D? Pile(TilePos p)
    {
        var items = _items!.At(p);
        if (items.Count == 0)
            return null;
        var furniture = _map!.Level(p.Z).FurnitureAt(p.X, p.Y);
        if (furniture is not null && furniture.Has("CONTAINER"))
            return null;
        var node = new Node3D { Position = new Vector3(p.X + 0.5f, WorldView.LevelBase(p.Z) + SurfaceHeight(furniture), p.Y + 0.5f) };
        // A stable scatter per tile, so piles don't jump around when they change.
        var rng = new Random(HashCode.Combine(p.X * 7919, p.Y * 104729, p.Z));
        int shown = 0;
        foreach (var item in items.Reverse())
        {
            if (shown == MaxShown)
                break;
            var sprite = Sprite(item);
            float spread = items.Count == 1 ? 0 : 0.22f;
            sprite.Position = new Vector3(((float)rng.NextDouble() - 0.5f) * 2 * spread, 0, ((float)rng.NextDouble() - 0.5f) * 2 * spread);
            node.AddChild(sprite);
            shown++;
        }
        return node;
    }

    /// <summary>Where things rest: the floor, a bed or sofa, or a table or counter top.</summary>
    private static float SurfaceHeight(FurnitureType? f) => f switch
    {
        null => VoxelVolume.VoxelSize,
        _ when f.Has("FLAT_SURF") => 0.85f,
        _ when f.Comfort > 0 => 0.5f,
        _ => 0.45f,
    };

    /// <summary>
    /// An item's sprite as an upright billboard with its bottom edge on the surface, or a small
    /// square in its CDDA color when there's no art.
    /// </summary>
    private Sprite3D Sprite(Item item)
    {
        var sprite = new Sprite3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            Shaded = true,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            PixelSize = PixelSize,
            Centered = true,
        };
        if (Texture(item) is { } art)
        {
            sprite.Texture = art.Texture;
            sprite.Offset = new Vector2(0, art.Height / 2f);
        }
        else
        {
            var image = Image.CreateEmpty(10, 10, false, Image.Format.Rgba8);
            image.Fill(ItemIcons.CddaColor(item.Type.Color));
            sprite.Texture = ImageTexture.CreateFromImage(image);
            sprite.Offset = new Vector2(0, 5);
        }
        return sprite;
    }

    /// <summary>The sprite cropped to its opaque pixels, and how many pixel rows tall it is.</summary>
    private (ImageTexture Texture, int Height)? Texture(Item item)
    {
        if (Art?.ChooseAny(item.Serial, item.Id) is not { } choice)
            return null;
        var key = ((object)choice.Tileset, choice.Fg);
        if (_textures.TryGetValue(key, out var hit))
            return hit;
        (ImageTexture, int)? result = null;
        if (Art.Pixels(choice.Tileset, choice.Fg, 0) is { } px && px.Bounds() is { } b)
        {
            var full = Image.CreateFromData(px.Width, px.Height, false, Image.Format.Rgba8, px.Rgba);
            var cropped = full.GetRegion(new Rect2I(b.X0, b.Y0, b.X1 - b.X0, b.Y1 - b.Y0));
            result = (ImageTexture.CreateFromImage(cropped), b.Y1 - b.Y0);
        }
        _textures[key] = result;
        return result;
    }
}
