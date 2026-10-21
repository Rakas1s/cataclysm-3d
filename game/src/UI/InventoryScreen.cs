using Cataclysm3D.Game.World;
using Cataclysm3D.Sim;
using Cataclysm3D.Sim.Creatures;
using Cataclysm3D.Sim.Items;
using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game.UI;

/// <summary>
/// The inventory (CDDA's <c>i</c>), a modal over the 3D view built from Godot Controls. On the
/// left, your portrait with a slot per body part showing what you wear there, and your hands;
/// in the middle, what's in your pockets, by the bag or garment it's in; on the right, what lies
/// on your tile and the eight around it. Drag an item from one place to another to wear, wield,
/// take off, pick up or drop it: places that would take it light up green, the rest red, and
/// hovering a red one says why. Hover an item for its details, or click it to select it: a
/// details panel shows its sprite large, everything about it, and buttons for what you can do
/// with it. CDDA's keys work on the item under the mouse, or the selected one: w wield, W wear,
/// T take off, d drop, g pick up, E eat. Every action spends moves through the sim, so the world
/// goes on while you rummage.
/// </summary>
public partial class InventoryScreen : Control
{
    /// <summary>What a CDDA key or an armed action does to an item.</summary>
    public enum Verb { Wield, Wear, TakeOff, Drop, PickUp, Eat }

    /// <summary>Where an item is now.</summary>
    public enum Place { Ground, Carried, Worn, Wielded }

    /// <summary>One line in a list: an item (and the identical ones stacked with it) and where it is.</summary>
    public sealed record Entry(Item Item, IReadOnlyList<Item> Stack, Place Place, TilePos? Pos);

    public enum TargetKind { BodySlot, Hands, Pockets, Ground }

    /// <summary>A place you can drop an item onto.</summary>
    public sealed record Target(TargetKind Kind, string Name, string[] Parts, TilePos? Pos);

    /// <summary>The paper doll's slots: CDDA's body parts, left and right sides together.</summary>
    private static readonly (string Name, string[] Parts)[] Slots =
    {
        ("Head", new[] { "head" }), ("Eyes", new[] { "eyes" }), ("Mouth", new[] { "mouth" }), ("Torso", new[] { "torso" }),
        ("Arms", new[] { "arm_l", "arm_r" }), ("Hands", new[] { "hand_l", "hand_r" }), ("Legs", new[] { "leg_l", "leg_r" }), ("Feet", new[] { "foot_l", "foot_r" }),
    };

    internal static readonly Color Gold = new(1f, 0.92f, 0.7f);
    internal static readonly Color Dim = new(1, 1, 1, 0.55f);
    private static readonly Color Good = new(0.45f, 0.85f, 0.45f);
    private static readonly Color Bad = new(0.95f, 0.35f, 0.3f);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<DropZone> _zones = new();
    private readonly Label _status = new();
    private readonly Label _title = new();
    private readonly HBoxContainer _columns = new();
    private GameWorld? _game;
    private string _hint = "";
    private string? _lastRefusal;
    private Entry? _hovered;

    /// <summary>The item clicked on, shown in the details panel, and its line as last built.</summary>
    private Item? _selected;
    private Entry? _selectedEntry;
    private readonly VBoxContainer _details = new() { MouseFilter = MouseFilterEnum.Pass };
    private Verb? _armed;

    public TileArt? Art { get; set; }

    /// <summary>Raised after the sim changed (an action spent moves), so the HUD can catch up.</summary>
    public event Action? Changed;

    public bool IsOpen => Visible;

    public InventoryScreen()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);

        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Pass };
        panel.AddThemeStyleboxOverride("panel", PanelStyle(0.92f));
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.OffsetLeft = 60;
        panel.OffsetRight = -60;
        panel.OffsetTop = 40;
        panel.OffsetBottom = -40;
        AddChild(panel);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        box.AddThemeConstantOverride("separation", 10);
        _title.MouseFilter = MouseFilterEnum.Ignore;
        _title.AddThemeFontSizeOverride("font_size", 18);
        _title.AddThemeColorOverride("font_color", Gold);
        _columns.MouseFilter = MouseFilterEnum.Pass;
        _columns.SizeFlagsVertical = SizeFlags.ExpandFill;
        _columns.AddThemeConstantOverride("separation", 14);
        _status.MouseFilter = MouseFilterEnum.Ignore;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.AddThemeFontSizeOverride("font_size", 15);
        var help = Text("Drag items between the panels  ·  click an item for a closer look  ·  on the item under the mouse or the selected one:  w wield  ·  W wear  ·  T take off  ·  d drop  ·  g pick up  ·  E eat  ·  "
                        + "Shift-drag moves a whole stack  ·  i or Esc closes", 13, Dim);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_title);
        box.AddChild(_columns);
        box.AddChild(new HSeparator { MouseFilter = MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0.3f) });
        box.AddChild(_status);
        box.AddChild(help);
        panel.AddChild(box);
    }

    /// <summary>Opens the screen; with <paramref name="armed"/>, clicking an item does that to it ("Wear what?").</summary>
    public void Open(GameWorld game, Verb? armed = null)
    {
        _game = game;
        _armed = armed;
        _hint = armed switch
        {
            Verb.Eat => "Eat or drink what? Click something edible.",
            Verb.Wear => "Wear what? Click a piece of clothing.",
            Verb.TakeOff => "Take off what? Click something you wear.",
            Verb.Wield => "Wield what? Click an item.",
            _ => "",
        };
        _lastRefusal = null;
        Visible = true;
        Refresh();
    }

    /// <summary>Selects the first item with this id you have or can reach (screenshot mode).</summary>
    public void Select(string id)
    {
        var p = _game!.Player;
        _selected = p.Worn.Append(p.Wielded).Concat(p.Inventory.CarriedItems).OfType<Item>()
            .Concat(_game.ItemsInReach().Select(g => g.Item))
            .FirstOrDefault(i => i.Id == id);
        Refresh();
    }

    public void Close()
    {
        Visible = false;
        _hovered = null;
        _selected = null;
        _armed = null;
    }

    // ---- keys -------------------------------------------------------------------------

    /// <summary>Handles a key while the screen is open. Returns whether it used it.</summary>
    public bool HandleKey(InputEventKey key)
    {
        if (key.PhysicalKeycode is Key.Escape or Key.I)
        {
            Close();
            return true;
        }
        Verb? verb = key.PhysicalKeycode switch
        {
            Key.W when key.ShiftPressed => Verb.Wear,
            Key.W => Verb.Wield,
            Key.T => Verb.TakeOff,
            Key.D => Verb.Drop,
            Key.G => Verb.PickUp,
            Key.E => Verb.Eat,
            _ => null,
        };
        if (verb is not { } v)
            return false;
        if ((_hovered ?? _selectedEntry) is not { } target)
        {
            Say("Hover over or click an item first, then press the key.", Dim);
            return true;
        }
        Act(v, target);
        return true;
    }

    /// <summary>Does a key's action to an item and reports how it went.</summary>
    private void Act(Verb verb, Entry e)
    {
        var game = _game!;
        var item = e.Item;
        string? reason = verb switch
        {
            Verb.Wield => game.WhyCantWield(item),
            Verb.Wear => e.Place == Place.Worn ? $"You're already wearing the {item.Type.Name}." : game.WhyCantWear(item),
            Verb.TakeOff => game.WhyCantTakeOff(item),
            Verb.Drop => e.Place == Place.Ground ? $"The {item.DisplayName} is already on the ground." : game.WhyCantDrop(item),
            Verb.PickUp => e.Place switch
            {
                Place.Ground => game.WhyCantPickUp(item),
                Place.Wielded => game.WhyCantStow(item),
                _ => $"You already have the {item.DisplayName}.",
            },
            _ => game.WhyCantEat(item),
        };
        if (reason is not null)
        {
            Say(reason, Bad);
            return;
        }
        switch (verb)
        {
            case Verb.Wield: game.Wield(item); break;
            case Verb.Wear: game.Wear(item); break;
            case Verb.TakeOff: game.TakeOff(item); break;
            case Verb.Drop: game.Drop(item); break;
            case Verb.PickUp: game.Stow(item); break;
            default: game.Eat(item); break;
        }
        AfterAction();
    }

    // ---- drag and drop ----------------------------------------------------------------

    /// <summary>Why an item can't go to a place, or null if it can.</summary>
    public string? WhyNot(Entry e, Target t)
    {
        var game = _game!;
        var item = e.Item;
        switch (t.Kind)
        {
            case TargetKind.BodySlot:
                if (item.Type.Armor is not { } armor)
                    return game.WhyCantWear(item);
                if (!t.Parts.Any(armor.Covers))
                {
                    var goes = Slots.Where(s => s.Parts.Any(armor.Covers)).Select(s => s.Name.ToLowerInvariant()).ToList();
                    return $"The {item.Type.Name} doesn't go on your {t.Name.ToLowerInvariant()}" +
                           (goes.Count > 0 ? $"; it goes on your {string.Join(" and ", goes)}." : ".");
                }
                return e.Place == Place.Worn ? $"You're already wearing the {item.Type.Name}." : game.WhyCantWear(item);
            case TargetKind.Hands:
                return game.WhyCantWield(item);
            case TargetKind.Pockets:
                return e.Place == Place.Carried ? $"The {item.DisplayName} is already in your pockets." : game.WhyCantStow(item);
            default:
                if (e.Place == Place.Ground)
                    return e.Pos == t.Pos ? $"The {item.DisplayName} is already there." : "Pick it up first, then drop it where you want it.";
                return game.WhyCantDrop(item, t.Pos);
        }
    }

    /// <summary>Moves an item (or, with Shift held, its whole stack) to a place.</summary>
    private void Drop(Entry e, Target t)
    {
        var game = _game!;
        var items = Input.IsKeyPressed(Key.Shift) ? e.Stack : new[] { e.Item };
        foreach (var item in items)
        {
            var one = e with { Item = item };
            if (WhyNot(one, t) is { } reason)
            {
                Say(reason, Bad);
                break;
            }
            bool done = t.Kind switch
            {
                TargetKind.BodySlot => game.Wear(item),
                TargetKind.Hands => game.Wield(item),
                TargetKind.Pockets => game.Stow(item),
                _ => game.Drop(item, t.Pos),
            };
            if (!done || game.Player.IsDead)
                break;
        }
        // Rebuilding frees the zone being dropped on, so wait until Godot is done with it.
        Callable.From(AfterAction).CallDeferred();
    }

    internal Variant DragData(Entry e, Control preview)
    {
        string key = $"item:{e.Item.Serial}";
        _entries[key] = e;
        SetDragPreview(preview);
        return key;
    }

    internal Entry? EntryFor(Variant data) =>
        data.VariantType == Variant.Type.String && _entries.TryGetValue(data.AsString(), out var e) ? e : null;

    internal bool CanDrop(Target t, Variant data)
    {
        if (EntryFor(data) is not { } e)
            return false;
        string? reason = WhyNot(e, t);
        _lastRefusal = reason;
        if (reason is null)
            Say($"{VerbFor(e, t)} the {e.Item.DisplayName}.", Good);
        else
            Say(reason, Bad);
        return reason is null;
    }

    internal void DropOn(Target t, Variant data)
    {
        _lastRefusal = null;
        if (EntryFor(data) is { } e)
            Drop(e, t);
    }

    private static string VerbFor(Entry e, Target t) => t.Kind switch
    {
        TargetKind.BodySlot => "Wear",
        TargetKind.Hands => "Wield",
        TargetKind.Pockets => e.Place switch { Place.Ground => "Pick up", Place.Worn => "Take off", _ => "Put away" },
        _ => e.Place == Place.Worn ? "Take off and drop" : "Drop",
    };

    public override void _Notification(int what)
    {
        if (what == NotificationDragBegin && EntryFor(GetViewport().GuiGetDragData()) is { } e)
        {
            foreach (var zone in _zones)
                zone.Highlight(WhyNot(e, zone.Target) is null);
        }
        else if (what == NotificationDragEnd)
        {
            foreach (var zone in _zones)
                zone.Highlight(null);
            if (!GetViewport().GuiIsDragSuccessful() && _lastRefusal is { } reason)
                Say(reason, Bad);
            _lastRefusal = null;
        }
    }

    internal void Hover(Entry? e)
    {
        _hovered = e;
        if (e is not null && !GetViewport().GuiIsDragging())
            Say(_hint.Length > 0 ? _hint : $"{e.Item.DisplayName}: drag it somewhere, or press a key.", Dim);
    }

    /// <summary>
    /// A click on an item while an action is armed ("Wear what?") does it; otherwise it selects
    /// the item (or, clicked again, unselects it) for the details panel.
    /// </summary>
    internal void Clicked(Entry e)
    {
        if (_armed is { } verb)
        {
            Act(verb, e);
            return;
        }
        _selected = _selected == e.Item ? null : e.Item;
        // The click comes from a chip that rebuilding frees, so rebuild once Godot is done with it.
        Callable.From(Refresh).CallDeferred();
    }

    internal bool IsSelected(Entry e) => _selected is not null && e.Stack.Contains(_selected);

    internal bool Matches(Entry e) => _armed switch
    {
        Verb.Eat => e.Item.Edible is not null,
        Verb.Wear => e.Item.Type.IsArmor && e.Place != Place.Worn,
        Verb.TakeOff => e.Place == Place.Worn,
        _ => true,
    };

    private void AfterAction()
    {
        Changed?.Invoke();
        if (_game!.Player.IsDead)
        {
            Close();
            return;
        }
        Refresh();
        if (_game.Messages.Count > 0)
            Say(_game.Messages[^1].Text, Colors.White);
    }

    private void Say(string text, Color color)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", color);
    }

    // ---- building the screen ------------------------------------------------------------

    /// <summary>Rebuilds every panel from the sim.</summary>
    public void Refresh()
    {
        if (_game is null)
            return;
        var p = _game.Player;
        _entries.Clear();
        _zones.Clear();
        _hovered = null;
        _selectedEntry = null;
        _details.GetParent()?.RemoveChild(_details);
        foreach (var child in _details.GetChildren())
        {
            _details.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var child in _columns.GetChildren())
        {
            _columns.RemoveChild(child);
            child.QueueFree();
        }
        _title.Text = $"Inventory  ·  {_game.Scheduler.Now}  ·  speed {p.Speed}";
        _columns.AddChild(Column("You", 560, DollColumn(p)));
        _columns.AddChild(Column("Pockets", 420, PocketsColumn(p)));
        _columns.AddChild(Column("Nearby", 380, GroundColumn()));
        // Something eaten or gone out of reach drops out of the selection.
        if (_selectedEntry is null)
            _selected = null;
        BuildDetails();
        if (_status.Text.Length == 0 || _hint.Length > 0)
            Say(_hint.Length > 0 ? _hint : "Drag clothes onto your body to wear them, into your hands to wield them, or onto the ground to drop them.", Dim);
    }

    private static Control Column(string title, int width, Control content)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(width, 0) };
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(Text(title, 16, Gold));
        var scroll = new ScrollContainer
        {
            MouseFilter = MouseFilterEnum.Pass,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(content);
        box.AddChild(scroll);
        return box;
    }

    /// <summary>Your portrait between two columns of body slots, your hands below, and your state.</summary>
    private Control DollColumn(PlayerActor p)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        box.AddThemeConstantOverride("separation", 8);
        var doll = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        doll.AddThemeConstantOverride("separation", 8);
        var left = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(180, 0) };
        var right = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(180, 0) };
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        for (int i = 0; i < Slots.Length; i++)
            (i < 4 ? left : right).AddChild(SlotZone(p, Slots[i].Name, Slots[i].Parts));
        var portrait = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            TextureFilter = TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(160, 200),
            Texture = CharacterPanel.Portrait(Art, p),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        doll.AddChild(left);
        doll.AddChild(portrait);
        doll.AddChild(right);
        box.AddChild(doll);

        var hands = Zone(new Target(TargetKind.Hands, "Hands", Array.Empty<string>(), null),
            "Wielding", p.Wielded is null ? "fists" : $"to-hit {Signed(p.Wielded.Type.ToHit)}  ·  {Melee.AttackCost(p)} moves an attack");
        if (p.Wielded is { } held)
            hands.Items.AddChild(Chip(new Entry(held, new[] { held }, Place.Wielded, null)));
        else
            hands.Items.AddChild(Text("Drag a weapon here to wield it.", 13, Dim));
        box.AddChild(hands);

        var needs = new[] { p.Needs.HungerStatus, p.Needs.ThirstStatus, p.Needs.SleepinessStatus }.Where(n => n.Text.Length > 0).ToList();
        var state = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        state.AddThemeConstantOverride("separation", 12);
        state.AddChild(Text($"Str {p.Str}  ·  Dex {p.Dex}", 13, Dim));
        foreach (var n in needs)
            state.AddChild(Text(n.Text, 13, CharacterPanel.NeedColor(n.Severity)));
        box.AddChild(state);
        box.AddChild(_details);
        return box;
    }

    private DropZone SlotZone(PlayerActor p, string name, string[] parts)
    {
        var inv = p.Inventory;
        int enc = parts.Max(inv.Encumbrance);
        int warmth = parts.Max(inv.Warmth);
        var zone = Zone(new Target(TargetKind.BodySlot, name, parts, null), name, $"enc {enc}  ·  warmth {warmth}");
        var worn = p.Worn.Where(w => parts.Any(w.Type.Armor!.Covers)).Reverse().ToList();
        foreach (var w in worn)
            zone.Items.AddChild(Chip(new Entry(w, new[] { w }, Place.Worn, null), compact: true));
        if (worn.Count == 0)
            zone.Items.AddChild(Text("—", 13, Dim));
        return zone;
    }

    /// <summary>Your pockets, by the garment or bag they belong to, with how full you are.</summary>
    private Control PocketsColumn(PlayerActor p)
    {
        var inv = p.Inventory;
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(Meter("Weight", inv.WeightMg, p.WeightCapacityMg, Format.Weight));
        box.AddChild(Meter("Volume", inv.VolumeCarriedMl, inv.VolumeCapacityMl, Format.Volume));
        var zone = Zone(new Target(TargetKind.Pockets, "Pockets", Array.Empty<string>(), null), "Drop here to pick up, take off or put away", "");
        zone.SizeFlagsVertical = SizeFlags.ExpandFill;
        var containers = p.Worn.Where(w => w.Type.Pockets.Count > 0).ToList();
        if (containers.Count == 0)
            zone.Items.AddChild(Text("You have no pockets. Wear something with pockets, like jeans or a backpack.", 13, Dim));
        foreach (var c in containers)
        {
            zone.Items.AddChild(Text($"{c.Type.Name}  ·  {Format.Volume(c.ContentsVolumeMl)} / {Format.Volume(Inventory.PocketVolume(c))}", 13, Dim));
            var stacks = Stacks(c.Contents, Place.Carried, null);
            if (stacks.Count == 0)
                zone.Items.AddChild(Text("   empty", 13, new Color(1, 1, 1, 0.3f)));
            foreach (var e in stacks)
                zone.Items.AddChild(Chip(e));
        }
        box.AddChild(zone);
        return box;
    }

    /// <summary>What's on your tile and around you, one drop zone per tile.</summary>
    private Control GroundColumn()
    {
        var game = _game!;
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        box.AddThemeConstantOverride("separation", 8);
        var me = game.Player.Pos;
        foreach (var pos in game.Reach())
        {
            var items = game.Map.Items.At(pos);
            bool here = pos == me;
            var level = game.Map.Level(pos.Z);
            bool open = level.MoveCost(pos.X, pos.Y) > 0 || level.FurnitureAt(pos.X, pos.Y) is not null;
            if (!here && (items.Count == 0 || !open))
                continue;
            string where = here ? "Here" : Direction(pos.X - me.X, pos.Y - me.Y);
            string on = level.FurnitureAt(pos.X, pos.Y)?.Name ?? level.TerrainAt(pos.X, pos.Y).Name;
            var zone = Zone(new Target(TargetKind.Ground, where, Array.Empty<string>(), pos), where, on);
            foreach (var e in Stacks(items, Place.Ground, pos))
                zone.Items.AddChild(Chip(e));
            if (items.Count == 0)
                zone.Items.AddChild(Text("Drag things here to drop them.", 13, Dim));
            box.AddChild(zone);
        }
        return box;
    }

    /// <summary>Identical items side by side become one line, "socks ×4".</summary>
    private static List<Entry> Stacks(IEnumerable<Item> items, Place place, TilePos? pos) =>
        items.GroupBy(i => i.StackKey)
            .Select(g => new Entry(g.First(), g.ToList(), place, pos))
            .ToList();

    private static string Direction(int dx, int dy) => (dx, dy) switch
    {
        (0, -1) => "North", (1, -1) => "North-east", (1, 0) => "East", (1, 1) => "South-east",
        (0, 1) => "South", (-1, 1) => "South-west", (-1, 0) => "West", _ => "North-west",
    };

    private static string Signed(int n) => n >= 0 ? $"+{n}" : $"{n}";

    private DropZone Zone(Target target, string title, string detail)
    {
        var zone = new DropZone(this, target, title, detail);
        _zones.Add(zone);
        return zone;
    }

    private ItemChip Chip(Entry e, bool compact = false)
    {
        if (IsSelected(e))
            _selectedEntry = e with { Item = _selected! };
        return new(this, e, compact);
    }

    /// <summary>
    /// The details panel for the selected item: its sprite large, everything the tooltip says,
    /// and a button per thing you can do with it, greyed out with the reason when you can't.
    /// </summary>
    private void BuildDetails()
    {
        if (_selectedEntry is not { } e)
        {
            _details.AddChild(Text("Click an item to look at it closely.", 13, Dim));
            return;
        }
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Pass };
        panel.AddThemeStyleboxOverride("panel", PanelStyle(0.6f, Gold with { A = 0.5f }));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        box.AddThemeConstantOverride("separation", 8);
        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 12);
        var frame = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkBegin };
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(1, 1, 1, 0.06f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        });
        frame.AddChild(new TextureRect
        {
            Texture = ItemIcons.LargeIcon(e.Item.Type),
            MouseFilter = MouseFilterEnum.Ignore,
            TextureFilter = TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(128, 128),
        });
        top.AddChild(frame);
        var info = Details(e.Item, e.Place, 380);
        info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(info);
        box.AddChild(top);

        string where = e.Place switch
        {
            Place.Worn => "You're wearing it.",
            Place.Wielded => "You're holding it.",
            Place.Carried => $"In your {_game!.Player.Inventory.ContainerOf(e.Item)?.Type.Name ?? "pockets"}.",
            _ => e.Pos == _game!.Player.Pos ? "On the ground here." : "On the ground next to you.",
        };
        box.AddChild(Text(where + (e.Stack.Count > 1 ? $"  ({e.Stack.Count} of them)" : ""), 13, Dim));

        var buttons = new HFlowContainer { MouseFilter = MouseFilterEnum.Pass };
        buttons.AddThemeConstantOverride("h_separation", 6);
        buttons.AddThemeConstantOverride("v_separation", 6);
        var reasons = new List<string>();
        foreach (var (verb, label) in VerbsFor(e))
        {
            var button = ActionButton(e, verb, label);
            buttons.AddChild(button);
            if (button.Disabled)
                reasons.Add($"{label.Split("  ")[0]}: {button.TooltipText}");
        }
        box.AddChild(buttons);
        // Say why the greyed-out buttons won't work, without making you hover them.
        foreach (var reason in reasons)
        {
            var why = Text(reason, 12, Bad with { A = 0.85f });
            why.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            box.AddChild(why);
        }
        panel.AddChild(box);
        _details.AddChild(panel);
    }

    /// <summary>The things worth offering for an item where it is, with their CDDA keys.</summary>
    private static IEnumerable<(Verb Verb, string Label)> VerbsFor(Entry e)
    {
        var t = e.Item.Type;
        if (e.Place == Place.Ground)
            yield return (Verb.PickUp, "Pick up  [g]");
        if (e.Place == Place.Wielded)
            yield return (Verb.PickUp, "Put away  [g]");
        if (t.IsArmor && e.Place != Place.Worn)
            yield return (Verb.Wear, "Wear  [W]");
        if (e.Place == Place.Worn)
            yield return (Verb.TakeOff, "Take off  [T]");
        if (e.Place != Place.Wielded && e.Place != Place.Worn)
            yield return (Verb.Wield, "Wield  [w]");
        if (e.Item.Edible is { } food)
            yield return (Verb.Eat, food.Type.IsLiquid || food.Type.Comestible!.Kind == ComestibleKind.Drink ? "Drink  [E]" : "Eat  [E]");
        if (e.Place != Place.Ground)
            yield return (Verb.Drop, "Drop  [d]");
    }

    private Button ActionButton(Entry e, Verb verb, string label)
    {
        var game = _game!;
        var item = e.Item;
        string? why = verb switch
        {
            Verb.Wield => game.WhyCantWield(item),
            Verb.Wear => game.WhyCantWear(item),
            Verb.TakeOff => game.WhyCantTakeOff(item),
            Verb.Drop => game.WhyCantDrop(item),
            Verb.PickUp => e.Place == Place.Ground ? game.WhyCantPickUp(item) : game.WhyCantStow(item),
            _ => game.WhyCantEat(item),
        };
        var button = new Button
        {
            Text = label,
            Disabled = why is not null,
            TooltipText = why ?? "",
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Stop,
        };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.Pressed += () => Callable.From(() => Act(verb, e)).CallDeferred();
        return button;
    }

    private static Control Meter(string name, long used, long capacity, Func<long, string> format)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        var label = Text(name, 13, Dim);
        label.CustomMinimumSize = new Vector2(60, 0);
        var bar = new ProgressBar
        {
            MouseFilter = MouseFilterEnum.Ignore,
            MinValue = 0,
            MaxValue = Math.Max(1, capacity),
            Value = Math.Min(used, Math.Max(1, capacity)),
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 14),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        bool over = used > capacity;
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = over ? Bad : new Color(0.55f, 0.7f, 0.5f) });
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.08f) });
        row.AddChild(label);
        row.AddChild(bar);
        row.AddChild(Text($"{format(used)} / {format(capacity)}", 13, over ? Bad : Colors.White));
        return row;
    }

    internal static StyleBoxFlat PanelStyle(float alpha, Color? border = null) => new()
    {
        BgColor = new Color(0.05f, 0.05f, 0.06f, alpha),
        BorderColor = border ?? new Color(1, 1, 1, 0.12f),
        BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 10,
    };

    internal static Label Text(string text, int size, Color color)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>The tooltip for an item: what CDDA's item info shows that M5 uses.</summary>
    internal static Control Tooltip(Item item, Place place)
    {
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", PanelStyle(0.95f));
        panel.AddChild(Details(item, place, 320));
        return panel;
    }

    /// <summary>An item's name, description and stats, wrapped at <paramref name="width"/>.</summary>
    internal static VBoxContainer Details(Item item, Place place, int width)
    {
        var t = item.Type;
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 3);
        box.AddChild(Text(item.DisplayName, 15, Gold));
        if (t.Description.Length > 0)
        {
            var desc = Text(t.Description, 13, new Color(1, 1, 1, 0.8f));
            desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            desc.CustomMinimumSize = new Vector2(width, 0);
            box.AddChild(desc);
        }
        var lines = new List<string> { $"Weight {Format.Weight(item.WeightMg)}  ·  Volume {Format.Volume(item.VolumeMl)}" };
        if (t.Armor is { } a)
        {
            var parts = Slots.Where(s => s.Parts.Any(a.Covers)).Select(s => s.Name.ToLowerInvariant());
            var enc = a.Portions.Select(p => p.Encumbrance == p.MaxEncumbrance ? $"{p.Encumbrance}" : $"{p.Encumbrance}–{p.MaxEncumbrance}").Distinct();
            lines.Add($"Covers {string.Join(", ", parts)} ({a.Portions.Max(p => p.Coverage)} %)  ·  {a.Layer.ToString().ToLowerInvariant()} layer");
            lines.Add($"Encumbrance {string.Join("/", enc)}  ·  warmth {a.Warmth}");
            lines.Add($"Protection: bash {a.Protection.Bash:0.#}  ·  cut {a.Protection.Cut:0.#}  ·  stab {a.Protection.Stab:0.#}");
        }
        if (t.Bash > 0 || t.Cut > 0 || t.Stab > 0 || place == Place.Wielded)
            lines.Add($"Melee: bash {t.Bash}  ·  cut {t.Cut}  ·  pierce {t.Stab}  ·  to-hit {Signed(t.ToHit)}  ·  {Melee.AttackTime(item)} attack time");
        if (item.Edible is { } food)
        {
            var c = food.Type.Comestible!;
            lines.Add($"{(food.Type.IsLiquid || c.Kind == ComestibleKind.Drink ? "Drink" : "Food")}: {c.Calories} kcal  ·  quench {c.Quench}"
                      + (food.Charges > 1 ? $"  ·  {food.Charges} servings" : ""));
        }
        if (t.Pockets.Count > 0)
            lines.Add($"Holds {Format.Volume(item.ContentsVolumeMl)} of {Format.Volume(Inventory.PocketVolume(item))}"
                      + (item.Contents.Count > 0 ? $": {string.Join(", ", item.Contents.Select(c => c.DisplayName))}" : ""));
        foreach (var line in lines)
        {
            var l = Text(line, 13, Colors.White);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(width, 0);
            box.AddChild(l);
        }
        return box;
    }
}

/// <summary>
/// A place in the inventory screen you can drop items onto: a body slot, your hands, your
/// pockets, or a tile of ground. Lights up green or red while you drag, by whether it takes
/// what you're dragging.
/// </summary>
public partial class DropZone : PanelContainer
{
    private readonly InventoryScreen _screen;
    private readonly StyleBoxFlat _style = InventoryScreen.PanelStyle(0.5f);

    public InventoryScreen.Target Target { get; }

    /// <summary>Where the zone's items go.</summary>
    public VBoxContainer Items { get; } = new() { MouseFilter = MouseFilterEnum.Pass };

    public DropZone(InventoryScreen screen, InventoryScreen.Target target, string title, string detail)
    {
        _screen = screen;
        Target = target;
        MouseFilter = MouseFilterEnum.Stop;
        _style.ContentMarginTop = 5;
        _style.ContentMarginBottom = 6;
        _style.ContentMarginLeft = 8;
        _style.ContentMarginRight = 8;
        AddThemeStyleboxOverride("panel", _style);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        box.AddThemeConstantOverride("separation", 2);
        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", 8);
        head.AddChild(InventoryScreen.Text(title, 14, InventoryScreen.Gold));
        if (detail.Length > 0)
        {
            var d = InventoryScreen.Text(detail, 12, InventoryScreen.Dim);
            d.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            d.HorizontalAlignment = HorizontalAlignment.Right;
            d.ClipText = true;
            head.AddChild(d);
        }
        Items.AddThemeConstantOverride("separation", 1);
        box.AddChild(head);
        box.AddChild(Items);
        AddChild(box);
    }

    /// <summary>Green for a place that takes the dragged item, red for one that doesn't, plain when nothing is dragged.</summary>
    public void Highlight(bool? valid)
    {
        _style.BorderColor = valid switch
        {
            true => new Color(0.45f, 0.85f, 0.45f, 0.9f),
            false => new Color(0.95f, 0.35f, 0.3f, 0.45f),
            _ => new Color(1, 1, 1, 0.12f),
        };
        _style.BgColor = valid switch
        {
            true => new Color(0.1f, 0.25f, 0.1f, 0.6f),
            false => new Color(0.2f, 0.06f, 0.06f, 0.5f),
            _ => new Color(0.05f, 0.05f, 0.06f, 0.5f),
        };
        int width = valid is true ? 2 : 1;
        _style.BorderWidthLeft = _style.BorderWidthTop = _style.BorderWidthRight = _style.BorderWidthBottom = width;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => _screen.CanDrop(Target, data);

    public override void _DropData(Vector2 atPosition, Variant data) => _screen.DropOn(Target, data);
}

/// <summary>One item (or stack) in the inventory screen: its sprite, name and weight. Drag it, hover it, click it.</summary>
public partial class ItemChip : PanelContainer
{
    private readonly InventoryScreen _screen;
    private readonly StyleBoxFlat _style = new() { BgColor = new Color(1, 1, 1, 0), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };

    public InventoryScreen.Entry Entry { get; }

    public ItemChip(InventoryScreen screen, InventoryScreen.Entry entry, bool compact)
    {
        _screen = screen;
        Entry = entry;
        MouseFilter = MouseFilterEnum.Pass;
        MouseDefaultCursorShape = CursorShape.Drag;
        TooltipText = entry.Item.DisplayName;
        _style.ContentMarginLeft = 2;
        _style.ContentMarginRight = 4;
        AddThemeStyleboxOverride("panel", _style);
        var row = Row(entry, compact);
        if (!screen.Matches(entry))
            row.Modulate = new Color(1, 1, 1, 0.35f);
        AddChild(row);
        bool selected = screen.IsSelected(entry);
        var resting = selected ? new Color(1f, 0.85f, 0.4f, 0.22f) : new Color(1, 1, 1, 0);
        _style.BgColor = resting;
        if (selected)
        {
            _style.BorderColor = new Color(1f, 0.85f, 0.4f, 0.8f);
            _style.BorderWidthLeft = _style.BorderWidthTop = _style.BorderWidthRight = _style.BorderWidthBottom = 1;
        }
        MouseEntered += () =>
        {
            _style.BgColor = selected ? new Color(1f, 0.85f, 0.4f, 0.3f) : new Color(1, 1, 1, 0.1f);
            _screen.Hover(Entry);
        };
        MouseExited += () =>
        {
            _style.BgColor = resting;
            _screen.Hover(null);
        };
    }

    private static HBoxContainer Row(InventoryScreen.Entry e, bool compact)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new TextureRect
        {
            Texture = ItemIcons.Icon(e.Item.Type),
            MouseFilter = MouseFilterEnum.Ignore,
            TextureFilter = TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(compact ? 22 : 28, compact ? 22 : 28),
        });
        string count = e.Stack.Count > 1 ? $"  ×{e.Stack.Count}" : "";
        // Worn bags and pockets show in the Pockets column, so the doll names them plainly.
        string label = e.Place == InventoryScreen.Place.Worn ? e.Item.Type.Name : e.Item.DisplayName;
        var name = InventoryScreen.Text(label + count, compact ? 13 : 14, Colors.White);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        name.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(name);
        if (!compact)
        {
            long weight = e.Stack.Sum(i => i.WeightMg);
            var w = InventoryScreen.Text(Format.Weight(weight), 12, InventoryScreen.Dim);
            w.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(w);
        }
        return row;
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        var preview = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0.85f) };
        preview.AddThemeStyleboxOverride("panel", InventoryScreen.PanelStyle(0.9f));
        preview.AddChild(Row(Entry, compact: false));
        return _screen.DragData(Entry, preview);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } && !GetViewport().GuiIsDragging())
            _screen.Clicked(Entry);
    }

    public override GodotObject _MakeCustomTooltip(string forText) => InventoryScreen.Tooltip(Entry.Item, Entry.Place);
}
