using Cataclysm3D.Sim;
using Cataclysm3D.Sim.Creatures;
using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game.World;

/// <summary>
/// Draws monsters as upright billboards of their tileset sprite and corpses as their corpse
/// sprite lying on the floor. Monsters glide between tiles and only show on tiles the sim's
/// <see cref="Sim.Vision.FieldOfView"/> says you can see; corpses also show, dimmed, where you
/// remember them. Hits flash the monster red; a dying one topples over into its corpse.
/// </summary>
public sealed partial class CreatureViews : Node3D
{
    /// <summary>
    /// Meters per sprite pixel. Tileset monsters are about 28 px tall for a human, so this stands a
    /// zombie about 1.75 m tall and keeps smaller monsters (dogs, children) in proportion.
    /// </summary>
    private const float PixelSize = 1f / 16;

    /// <summary>Gliding speed between tiles, in meters per second.</summary>
    private const float GlideSpeed = 7f;

    private static readonly Color Remembered = new(0.35f, 0.35f, 0.35f);

    private readonly Dictionary<Monster, Sprite3D> _monsters = new();
    private readonly Dictionary<int, Node3D> _corpses = new();
    private readonly Dictionary<(object, int), (ImageTexture Texture, int Height)?> _textures = new();

    public TileArt? Art { get; set; }

    /// <summary>Removes everything (a new town).</summary>
    public void Clear()
    {
        foreach (var child in GetChildren())
            child.QueueFree();
        _monsters.Clear();
        _corpses.Clear();
    }

    /// <summary>Plays hit and death feedback for sim events.</summary>
    public void Show(SimEvent e)
    {
        switch (e)
        {
            case MonsterHitEvent hit when _monsters.TryGetValue(hit.Monster, out var sprite):
                sprite.Modulate = new Color(1f, 0.25f, 0.2f);
                CreateTween().TweenProperty(sprite, "modulate", Colors.White, 0.3f);
                break;
            case MonsterDiedEvent died when _monsters.Remove(died.Monster, out var sprite):
                sprite.QueueFree();
                break;
        }
    }

    /// <summary>Brings every monster and corpse up to date with the sim. Call every frame.</summary>
    public void Sync(GameWorld game, Vector3 viewer, float delta)
    {
        foreach (var m in game.Monsters)
        {
            if (!_monsters.TryGetValue(m, out var sprite))
            {
                sprite = MonsterSprite(m);
                _monsters[m] = sprite;
                AddChild(sprite);
                sprite.Position = TileCenter(m.Pos);
            }
            sprite.Visible = game.Sight.IsVisible(m.Pos);
            var target = TileCenter(m.Pos);
            // Far jumps (long waits, teleports) snap; steps glide.
            sprite.Position = !sprite.Visible || sprite.Position.DistanceTo(target) > 3
                ? target
                : sprite.Position.MoveToward(target, GlideSpeed * delta);
        }
        foreach (var (m, sprite) in _monsters.Where(kv => kv.Key.IsDead).ToList())
        {
            sprite.QueueFree();
            _monsters.Remove(m);
        }

        foreach (var corpse in game.Corpses)
        {
            if (!_corpses.TryGetValue(corpse.Serial, out var node))
            {
                node = CorpseNode(corpse, viewer);
                _corpses[corpse.Serial] = node;
                AddChild(node);
            }
            bool visible = game.Sight.IsVisible(corpse.Pos);
            node.Visible = visible || game.Sight.HasSeen(corpse.Pos);
            if (node.GetChildOrNull<Sprite3D>(0) is { } s)
                s.Modulate = visible ? Colors.White : Remembered;
        }
    }

    private static Vector3 TileCenter(TilePos p) =>
        new(p.X + 0.5f, WorldView.LevelBase(p.Z) + VoxelVolume.VoxelSize, p.Y + 0.5f);

    private Sprite3D MonsterSprite(Monster m)
    {
        var sprite = NewSprite(m.Type, m.Serial, corpse: false);
        sprite.Billboard = BaseMaterial3D.BillboardModeEnum.FixedY;
        return sprite;
    }

    /// <summary>The corpse sprite, standing where the monster fell and toppling backwards onto the floor.</summary>
    private Node3D CorpseNode(Corpse corpse, Vector3 viewer)
    {
        var pivot = new Node3D { Position = TileCenter(corpse.Pos) + new Vector3(0, 0.02f, 0) };
        var toViewer = new Vector2(viewer.X - pivot.Position.X, viewer.Z - pivot.Position.Z);
        pivot.Rotation = new Vector3(0, Mathf.Atan2(toViewer.X, toViewer.Y), 0);
        var sprite = NewSprite(corpse.Type, corpse.Serial, corpse: true);
        pivot.AddChild(sprite);
        var tween = pivot.CreateTween();
        tween.TweenProperty(sprite, "rotation", new Vector3(-Mathf.Pi / 2, 0, 0), 0.35f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        return pivot;
    }

    /// <summary>
    /// A sprite for a monster (or its corpse) at <see cref="PixelSize"/>, its feet touching the
    /// node's origin. Corpses are drawn a little bigger than a tile and
    /// centered. Falls back to a plain colored quad when there's no art.
    /// </summary>
    private Sprite3D NewSprite(MonsterType type, int serial, bool corpse)
    {
        var sprite = new Sprite3D
        {
            Shaded = true,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Centered = true,
        };
        var ids = corpse ? new[] { $"corpse_{type.Id}", "corpse" } : new[] { type.Id };
        if (Texture(ids, serial) is { } art)
        {
            sprite.Texture = art.Texture;
            sprite.PixelSize = PixelSize;
            // Feet on the floor: the cropped image's bottom edge at the origin.
            sprite.Offset = new Vector2(0, corpse ? 0 : art.Height / 2f);
        }
        else
        {
            var image = Image.CreateEmpty(8, 16, false, Image.Format.Rgba8);
            image.Fill(corpse ? new Color(0.35f, 0.1f, 0.1f) : new Color(0.3f, 0.6f, 0.3f));
            sprite.Texture = ImageTexture.CreateFromImage(image);
            sprite.PixelSize = PixelSize * 1.75f;
            sprite.Offset = new Vector2(0, corpse ? 0 : 8);
        }
        return sprite;
    }

    /// <summary>The sprite cropped to its opaque pixels, and how many pixel rows tall it is.</summary>
    private (ImageTexture Texture, int Height)? Texture(string[] ids, int serial)
    {
        if (Art?.ChooseAny(serial, ids) is not { } choice)
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
