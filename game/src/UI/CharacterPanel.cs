using Cataclysm3D.Game.World;
using Cataclysm3D.Sim;
using Cataclysm3D.Sim.Creatures;
using Cataclysm3D.Sim.Items;
using Godot;

namespace Cataclysm3D.Game.UI;

/// <summary>
/// The character panel in the bottom-right corner: a portrait of you drawn the way CDDA draws
/// the player (the tileset's base body with an overlay for everything you wear and wield), your
/// hit points per body part colored by health, your stats, hunger, thirst and sleepiness, and
/// your gear. Styled like
/// <see cref="InteractionTooltip"/> and, like it, ignores the mouse.
/// </summary>
public partial class CharacterPanel : PanelContainer
{
    /// <summary>Screen pixels per sprite pixel in the portrait.</summary>
    private const int PortraitScale = 3;

    /// <summary>How many worn items the panel names before "+N".</summary>
    private const int MaxWornNames = 2;

    private static readonly Color Dim = new(1, 1, 1, 0.55f);

    private readonly Label _stats = new();
    private readonly TextureRect _portrait = new();
    private readonly GridContainer _parts = new() { Columns = 2 };
    private readonly GridContainer _gear = new() { Columns = 2 };
    private readonly HBoxContainer _needs = new() { MouseFilter = MouseFilterEnum.Ignore };
    private string _state = "";
    private string _look = "";
    private int _lastHp = -1;

    public TileArt? Art { get; set; }

    public CharacterPanel()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.06f, 0.72f),
            BorderColor = new Color(1, 1, 1, 0.12f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 10,
        });

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 6);

        var title = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeConstantOverride("separation", 12);
        var name = Text("You", 15, new Color(1f, 0.92f, 0.7f));
        _stats.MouseFilter = MouseFilterEnum.Ignore;
        _stats.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _stats.HorizontalAlignment = HorizontalAlignment.Right;
        _stats.AddThemeFontSizeOverride("font_size", 13);
        _stats.AddThemeColorOverride("font_color", Dim);
        title.AddChild(name);
        title.AddChild(_stats);

        var body = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        body.AddThemeConstantOverride("separation", 16);
        _portrait.MouseFilter = MouseFilterEnum.Ignore;
        _portrait.TextureFilter = TextureFilterEnum.Nearest;
        _portrait.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _portrait.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        _portrait.CustomMinimumSize = new Vector2(32, 32) * PortraitScale;
        _parts.MouseFilter = MouseFilterEnum.Ignore;
        _parts.AddThemeConstantOverride("h_separation", 10);
        _parts.AddThemeConstantOverride("v_separation", 0);
        _parts.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        body.AddChild(_portrait);
        body.AddChild(_parts);

        _gear.MouseFilter = MouseFilterEnum.Ignore;
        _gear.AddThemeConstantOverride("h_separation", 10);
        _gear.AddThemeConstantOverride("v_separation", 0);

        _needs.AddThemeConstantOverride("separation", 10);

        box.AddChild(title);
        box.AddChild(body);
        box.AddChild(_needs);
        box.AddChild(new HSeparator { MouseFilter = MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0.3f) });
        box.AddChild(_gear);
        AddChild(box);

        SetAnchorsPreset(LayoutPreset.BottomRight);
        GrowHorizontal = GrowDirection.Begin;
        GrowVertical = GrowDirection.Begin;
        OffsetRight = -16;
        OffsetBottom = -16;
        CustomMinimumSize = new Vector2(270, 0);
    }

    /// <summary>Shows the player's current state; rebuilds only when something changed.</summary>
    public void Refresh(PlayerActor player, bool flashlight)
    {
        var body = player.Body;
        var hp = Enumerable.Range(0, body.Parts.Count).Select(body.Hp).ToArray();
        string look = $"{player.Male}|{player.Hair}|{string.Join(",", player.Worn.Select(c => c.Id))}|{player.Wielded?.Id}";
        var needs = new[] { player.Needs.HungerStatus, player.Needs.ThirstStatus, player.Needs.SleepinessStatus };
        string state = $"{string.Join(",", hp)}|{player.Str}|{player.Dex}|{player.Speed}|{flashlight}|{look}|{player.Inventory.Version}|{string.Join(",", needs)}|{player.Asleep}";
        if (state == _state)
            return;
        _state = state;

        if (look != _look)
        {
            _look = look;
            _portrait.Texture = Portrait(Art, player);
        }
        // A red flash on the portrait when you're hurt.
        int total = hp.Sum();
        if (_lastHp >= 0 && total < _lastHp)
        {
            _portrait.Modulate = new Color(1f, 0.3f, 0.3f);
            CreateTween().TweenProperty(_portrait, "modulate", Colors.White, 0.4f);
        }
        _lastHp = total;

        _stats.Text = $"Str {player.Str}  ·  Dex {player.Dex}  ·  Speed {player.Speed}";

        Clear(_parts);
        for (int i = 0; i < body.Parts.Count; i++)
        {
            _parts.AddChild(Text(ShortName(body.Parts[i].Name), 13, Dim));
            var value = Text($"{body.Hp(i)}", 13, Health(body.Hp(i), body.MaxHp(i)));
            value.HorizontalAlignment = HorizontalAlignment.Right;
            _parts.AddChild(value);
        }

        Clear(_needs);
        foreach (var n in needs.Where(n => n.Text.Length > 0))
            _needs.AddChild(Text(n.Text, 13, NeedColor(n.Severity)));
        if (player.Asleep)
            _needs.AddChild(Text("Asleep", 13, new Color(0.6f, 0.7f, 1f)));
        _needs.Visible = _needs.GetChildCount() > 0;

        Clear(_gear);
        AddGear("Wielding", player.Wielded?.DisplayName ?? "fists");
        AddGear("Wearing", WornText(player.Worn));
        var inv = player.Inventory;
        AddGear("Carrying", $"{Format.Weight(inv.WeightMg)} / {Format.Weight(player.WeightCapacityMg)} · {Format.Volume(inv.VolumeCarriedMl)} / {Format.Volume(inv.VolumeCapacityMl)}");
        AddGear("Flashlight", flashlight ? "on" : "off");
    }

    /// <summary>
    /// The main garments first, then what's underneath, then accessories, on one line:
    /// "jeans, sneakers (pair) +5". The portrait shows the rest.
    /// </summary>
    private static string WornText(IReadOnlyList<Item> worn)
    {
        if (worn.Count == 0)
            return "nothing";
        var names = worn.OrderBy(c => c.Type.Armor?.Layer switch { WornLayer.Normal => 0, WornLayer.Outer => 0, WornLayer.Skintight => 1, _ => 2 })
            .Select(c => c.Type.Name).ToList();
        return names.Count <= MaxWornNames
            ? string.Join(", ", names)
            : $"{string.Join(", ", names.Take(MaxWornNames))} +{names.Count - MaxWornNames}";
    }

    /// <summary>
    /// The tileset's player body with an overlay per worn item on top, innermost first, as CDDA
    /// layers them, your hair over that, and what you wield on top (<c>overlay_wielded_*</c>). Gendered overlays (<c>overlay_male_worn_longshirt</c>) win over plain ones.
    /// Every layer comes from the tileset that drew the body, since each tileset's clothes are
    /// drawn to fit its own body; clothes it has no art for aren't drawn.
    /// </summary>
    public static Texture2D? Portrait(TileArt? art, PlayerActor player)
    {
        if (art is not { } Art)
            return null;
        string gender = player.Male ? "male" : "female";
        var canvas = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        if (Art.ChooseAny(0, $"player_{gender}") is not { } baseChoice)
            return null;
        var layers = new List<string[]> { new[] { $"player_{gender}" } };
        layers.AddRange(player.Worn.Select(c => new[] { $"overlay_{gender}_worn_{c.Id}", $"overlay_worn_{c.Id}" }));
        // Hair on top, so long styles fall over the shoulders of your shirt.
        if (player.Hair is { } hair)
            layers.Add(new[] { $"overlay_{gender}_mutation_{hair.Style}_var_{hair.Color}", $"overlay_mutation_{hair.Style}_var_{hair.Color}" });
        if (player.Wielded is { } held)
            layers.Add(new[] { $"overlay_{gender}_wielded_{held.Id}", $"overlay_wielded_{held.Id}" });
        foreach (var ids in layers)
        {
            if (Art.ChooseAny(0, baseChoice.Tileset, ids) is not { } choice || Art.Pixels(choice.Tileset, choice.Fg, 0) is not { } px)
                continue;
            var sprite = Image.CreateFromData(px.Width, px.Height, false, Image.Format.Rgba8, px.Rgba);
            canvas.BlendRect(sprite, new Rect2I(0, 0, px.Width, px.Height), new Vector2I(px.OffsetX, px.OffsetY));
        }
        return ImageTexture.CreateFromImage(canvas);
    }

    private void AddGear(string slot, string what)
    {
        _gear.AddChild(Text(slot, 13, Dim));
        _gear.GetChild<Label>(_gear.GetChildCount() - 1).SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var value = Text(what, 13, Colors.White);
        value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        value.CustomMinimumSize = new Vector2(170, 0);
        _gear.AddChild(value);
    }

    /// <summary>"left arm" → "L arm", like CDDA's sidebar headings.</summary>
    private static string ShortName(string name)
    {
        name = name.Replace("left ", "L ").Replace("right ", "R ");
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>White when fine, yellow, orange, red as a need gets worse.</summary>
    public static Color NeedColor(NeedSeverity severity) => severity switch
    {
        NeedSeverity.Mild => new Color(0.95f, 0.85f, 0.3f),
        NeedSeverity.Serious => new Color(0.95f, 0.55f, 0.2f),
        NeedSeverity.Critical => new Color(0.95f, 0.3f, 0.25f),
        _ => new Color(0.6f, 0.85f, 0.6f),
    };

    /// <summary>Green when whole, through yellow and orange to red, dark red when broken.</summary>
    private static Color Health(int hp, int max)
    {
        float f = max <= 0 ? 0 : (float)hp / max;
        return f switch
        {
            <= 0 => new Color(0.55f, 0.15f, 0.15f),
            < 0.25f => new Color(0.95f, 0.3f, 0.25f),
            < 0.5f => new Color(0.95f, 0.55f, 0.2f),
            < 0.8f => new Color(0.95f, 0.85f, 0.3f),
            _ => new Color(0.5f, 0.85f, 0.45f),
        };
    }

    private static Label Text(string text, int size, Color color)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
