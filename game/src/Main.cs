using Cataclysm3D.Game.UI;
using Cataclysm3D.Game.World;
using Cataclysm3D.Importer.Creatures;
using Cataclysm3D.Importer.World;
using Cataclysm3D.Sim;
using Cataclysm3D.Sim.Creatures;
using Cataclysm3D.Sim.Vision;
using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game;

/// <summary>
/// Generates a small town of real CDDA houses with every z-level of each (basements, upper
/// floors, roofs), renders it as stacked voxel levels and lets you walk it in first person with
/// 8-direction tile steps, stairs and free mouse-look, driven by the sim's turns. The sim's clock
/// drives the sun and moon, and its field of view decides what you see (M3). Zombies wander the
/// town, hunt you when they see you and fight you hand to hand (M4).
/// </summary>
public partial class Main : Node3D
{
    private const float EyeHeight = 1.6f + VoxelVolume.VoxelSize;
    private const float StepSeconds = 0.12f;
    private const float ClimbSeconds = 0.4f;
    private const float MouseSensitivity = 0.003f;

    /// <summary>How far up a flight of stairs you stand on its bottom tile, so you aren't inside the steps.</summary>
    private const float StairsLift = 0.6f;

    /// <summary>Height of the carried flashlight above the floor.</summary>
    private const float FlashlightHeight = 1.3f;

    /// <summary>How many lines of the message log the HUD shows.</summary>
    private const int LogLines = 6;

    private Content _content = null!;
    private GameWorld _game = null!;
    private readonly CreatureViews _creatures = new() { Name = "Creatures" };
    private readonly DayNight _dayNight = new();
    private readonly Node3D _lamps = new() { Name = "Lamps" };
    private int _lampsVersion = -1;
    private OmniLight3D _flashlightLight = null!;
    private bool _flashlight;
    private GameTime _startTime = GameTime.GameStart;
    private Town _town = null!;
    private WorldMap _world = null!;
    private WorldView _view = null!;
    private Node3D _body = null!;
    private Camera3D _camera = null!;
    private Label _hud = null!;
    private Label _log = null!;
    private Label _deathNotice = null!;
    private ColorRect _hurtFlash = null!;
    private readonly InteractionTooltip _tooltip = new();
    private string _tooltipState = "";

    private int _lotIndex;
    private int _seed = 1;
    private bool _noZombies;
    private readonly List<(int Dx, int Dy, string Id)> _extraZombies = new();
    private int _shotWaitTurns;
    private bool _shotFight;
    private bool _stepping;
    private float _yaw;
    private float _pitch;
    private string _message = "";
    private IReadOnlyList<string> _warnings = Array.Empty<string>();

    public override void _Ready()
    {
        _content = new Content();
        ParseOptions();

        AddChild(_dayNight);
        AddChild(_lamps);
        _view = new WorldView { Art = _content.Art };
        TileVoxelizer.Registry = _content.Registry;
        AddChild(_view);
        _creatures.Art = _content.Art;
        AddChild(_creatures);

        _body = new Node3D();
        AddChild(_body);
        _camera = new Camera3D { Position = new Vector3(0, EyeHeight, 0), Fov = 80, Near = 0.05f };
        _body.AddChild(_camera);
        // The carried flashlight: CDDA's lights shine all around, so this is an omni light at hand
        // height. No shadows: in doorways they blacked out the floor ahead, and the fog of war
        // already hides what the light reaches through walls.
        _flashlightLight = new OmniLight3D
        {
            Position = new Vector3(0, FlashlightHeight, 0),
            OmniRange = LightLevels.Range(LightLevels.Flashlight) + 1.5f,
            OmniAttenuation = 1.4f,
            LightEnergy = 1.6f,
            LightColor = new Color(1f, 0.96f, 0.88f),
            ShadowEnabled = false,
            Visible = false,
        };
        _body.AddChild(_flashlightLight);

        _hud = new Label { Position = new Vector2(16, 16), MouseFilter = Control.MouseFilterEnum.Ignore };
        _hud.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _hud.AddThemeConstantOverride("shadow_offset_x", 1);
        _hud.AddThemeConstantOverride("shadow_offset_y", 1);
        _log = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        _log.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _log.GrowVertical = Control.GrowDirection.Begin;
        _log.OffsetLeft = 16;
        _log.OffsetBottom = -16;
        _log.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _log.AddThemeConstantOverride("shadow_offset_x", 1);
        _log.AddThemeConstantOverride("shadow_offset_y", 1);
        _hurtFlash = new ColorRect { Color = new Color(0.8f, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _hurtFlash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _deathNotice = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visible = false,
            Text = "You die.\n\nPress Enter to start a new run.",
        };
        _deathNotice.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _deathNotice.AddThemeFontSizeOverride("font_size", 36);
        _deathNotice.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.8f));
        _deathNotice.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _deathNotice.AddThemeConstantOverride("shadow_offset_x", 2);
        _deathNotice.AddThemeConstantOverride("shadow_offset_y", 2);
        var canvas = new CanvasLayer();
        canvas.AddChild(_hurtFlash);
        canvas.AddChild(_hud);
        canvas.AddChild(_log);
        canvas.AddChild(_deathNotice);
        canvas.AddChild(Crosshair());
        canvas.AddChild(_tooltip);
        AddChild(canvas);

        LoadTown();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        ParseShots();
    }

    /// <summary>
    /// <c>--time=HH:MM</c> (or <c>--time=day,HH:MM</c>, days counted from 0) starts the clock
    /// there instead of 8:00 on spring day 1; <c>--flashlight</c> starts with it on.
    /// <c>--no-zombies</c> leaves the town empty; <c>--zombie=dx,dy[,id]</c> adds a monster
    /// (default mon_zombie) that many tiles east and south of where you start (or of the first
    /// shot). <c>--wait=N</c> waits N turns before each shot, so monsters can close in, and
    /// <c>--fight</c> then punches whatever is next to you until it or you die.
    /// </summary>
    private void ParseOptions()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--flashlight")
            {
                _flashlight = true;
            }
            else if (arg == "--no-zombies")
            {
                _noZombies = true;
            }
            else if (arg.StartsWith("--zombie=", StringComparison.Ordinal))
            {
                var parts = arg["--zombie=".Length..].Split(',');
                _extraZombies.Add((int.Parse(parts[0]), int.Parse(parts[1]), parts.Length > 2 ? parts[2] : "mon_zombie"));
            }
            else if (arg == "--fight")
            {
                _shotFight = true;
            }
            else if (arg.StartsWith("--wait=", StringComparison.Ordinal))
            {
                _shotWaitTurns = int.Parse(arg["--wait=".Length..]);
            }
            else if (arg.StartsWith("--time=", StringComparison.Ordinal))
            {
                var parts = arg["--time=".Length..].Split(',');
                var hm = parts[^1].Split(':');
                int day = parts.Length > 1 ? int.Parse(parts[0]) : 0;
                _startTime = GameTime.At(day, int.Parse(hm[0]), hm.Length > 1 ? int.Parse(hm[1]) : 0);
            }
        }
    }

    // Screenshot mode for checking visuals without playing:
    //   godot --path game -- --shot=out.png,tileX,tileY,yawDeg,pitchDeg[,z[,lot]] [--shot=...]
    // Tile coordinates are house-local (0..23, as the mapgen rows are drawn) on the given lot
    // (default 0, the first house); yaw is in the world (0 = north). The game saves each shot, then quits.
    private readonly Queue<(string Path, TilePos Pos, float Yaw, float Pitch)> _shots = new();
    private int _shotFrames;

    private void ParseShots()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--art-report")
            {
                ArtReport();
                if (!OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--shot=", StringComparison.Ordinal)))
                    GetTree().Quit();
                continue;
            }
            if (!arg.StartsWith("--shot=", StringComparison.Ordinal))
                continue;
            var p = arg["--shot=".Length..].Split(',');
            TilePos pos;
            if (p[1].StartsWith('@'))
            {
                // --shot=out.png,@f_bed,dy,yaw,pitch: stand dy tiles south of the first f_bed (or terrain id) in town.
                if (FindFurniture(p[1][1..]) is not { } at)
                {
                    GD.PushWarning($"No {p[1][1..]} in this town");
                    continue;
                }
                pos = at with { Y = at.Y + int.Parse(p[2]) };
            }
            else
            {
                int z = p.Length > 5 ? int.Parse(p[5]) : 0;
                var lot = _town.Lots[p.Length > 6 ? int.Parse(p[6]) : 0];
                var (x, y) = lot.ToTown(int.Parse(p[1]), int.Parse(p[2]));
                pos = new TilePos(x, y, z);
            }
            _shots.Enqueue((p[0], pos,
                Mathf.DegToRad(float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture)),
                Mathf.DegToRad(float.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture))));
        }
        if (_shots.Count > 0)
            Input.MouseMode = Input.MouseModeEnum.Visible;
        SpawnExtraZombies(_shots.Count > 0 ? _shots.Peek().Pos : Pos);
    }

    /// <summary>The monsters asked for with <c>--zombie=dx,dy[,id]</c>, placed around <paramref name="origin"/>.</summary>
    private void SpawnExtraZombies(TilePos origin)
    {
        foreach (var (dx, dy, id) in _extraZombies)
        {
            var at = origin with { X = origin.X + dx, Y = origin.Y + dy };
            if (_content.Monsters.Type(id) is not { } type)
                GD.PushWarning($"No monster {id}");
            else if (!_game.IsFree(at))
                GD.PushWarning($"Can't put a {id} at {at}: the tile isn't free");
            else
                _game.Spawn(type, at);
        }
    }

    private TilePos? FindFurniture(string id)
    {
        for (int z = _world.MinZ; z <= _world.MaxZ; z++)
        {
            var level = _world.Level(z);
            for (int y = 0; y < level.Height; y++)
            for (int x = 0; x < level.Width; x++)
                if (level.FurnitureAt(x, y)?.Id == id || level.TerrainAt(x, y).Id == id)
                    return new TilePos(x, y, z);
        }
        return null;
    }

    private bool RunShots()
    {
        if (_shots.Count == 0)
            return false;
        var shot = _shots.Peek();
        if (_shotFrames == 0)
        {
            Pos = shot.Pos;
            _body.Position = TileToWorld(Pos);
            if (_shotWaitTurns > 0 && !_game.Player.IsDead)
            {
                UpdateVision();
                _game.Wait(_shotWaitTurns);
            }
            for (int i = 0; _shotFight && i < 500 && !_game.Player.IsDead; i++)
            {
                if (_game.Monsters.FirstOrDefault(m => Math.Max(Math.Abs(m.Pos.X - Pos.X), Math.Abs(m.Pos.Y - Pos.Y)) == 1 && m.Pos.Z == Pos.Z) is not { } foe)
                    break;
                _game.PlayerAttack(foe);
            }
            HandleEvents();
            _yaw = shot.Yaw;
            _pitch = shot.Pitch;
            ApplyLook();
            UpdateCutaway();
            UpdateVision();
            UpdateHud();
        }
        // Give shadows, SSAO, the HUD and gliding monsters a few frames to settle.
        _creatures.Sync(_game, _body.Position, 1f / 30);
        if (++_shotFrames < 20)
            return true;
        GetViewport().GetTexture().GetImage().SavePng(shot.Path);
        GD.Print($"Saved {shot.Path}");
        _shots.Dequeue();
        _shotFrames = 0;
        if (_shots.Count == 0)
            GetTree().Quit();
        return true;
    }

    /// <summary>
    /// Prints which sprite every terrain and furniture id in the town is drawn from
    /// (<c>godot --path game -- --art-report</c>), so missing art is easy to spot.
    /// </summary>
    private void ArtReport()
    {
        var types = new SortedDictionary<string, (TileType Type, int Count)>(StringComparer.Ordinal);
        for (int z = _world.MinZ; z <= _world.MaxZ; z++)
        {
            var level = _world.Level(z);
            for (int y = 0; y < level.Height; y++)
            for (int x = 0; x < level.Width; x++)
            {
                foreach (TileType? t in new TileType?[] { level.TerrainAt(x, y), level.FurnitureAt(x, y) })
                    if (t is not null)
                        types[t.Id] = (t, types.GetValueOrDefault(t.Id).Count + 1);
            }
        }
        // Where each id first appears, in --shot coordinates (house-local x, y, z, lot).
        var where = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int li = 0; li < _town.Lots.Count; li++)
        for (int z = _world.MinZ; z <= _world.MaxZ; z++)
        for (int ly = 0; ly < _town.Lots[li].Height; ly++)
        for (int lx = 0; lx < _town.Lots[li].Width; lx++)
        {
            var (x, y) = _town.Lots[li].ToTown(lx, ly);
            if (_world.Level(z).FurnitureAt(x, y) is { } f)
                where.TryAdd(f.Id, $"{lx},{ly},{z},{li}");
        }
        foreach (var (id, (type, count)) in types)
        {
            var hit = _content.Art?.Resolve(type);
            string from = hit is null ? "NO ART (placeholder)" : $"{hit.Tileset.Name.Split(' ')[0]}:{hit.MatchedId}{(hit.Entry.Multitile ? " [multitile]" : "")}";
            GD.Print($"{id,-36} x{count,-5} {from,-48} {where.GetValueOrDefault(id, "")}");
        }
    }

    private void LoadTown()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _town = _content.GenerateTown(_seed);
        _world = _town.Map;
        _warnings = _town.Warnings;
        _view.Display(_world);
        // A new town keeps the clock running, unless you died.
        var now = _game is { Player.IsDead: false } ? _game.Scheduler.Now : _startTime;
        _game = new GameWorld(_world, new PlayerActor(bodyParts: _content.BodyParts), StartFor(_town.Lots[_lotIndex]), now, _seed);
        if (!_noZombies)
            TownPopulation.Populate(_game, _town, _content.Monsters, _seed);
        _creatures.Clear();
        _deathNotice.Visible = false;
        _hurtFlash.Color = _hurtFlash.Color with { A = 0 };
        _lampsVersion = -1;
        _message = $"Generated a town of {_town.Lots.Count} houses (seed {_seed}) in {clock.ElapsedMilliseconds} ms. " +
                   $"CDDA data loaded in {_content.LoadMilliseconds} ms. {_game.Monsters.Count} monsters about. " +
                   (_content.Art is { } art
                       ? $"Tilesets {art.Summary} loaded in {art.LoadMilliseconds} ms; {art.Atlas.Count} surface sprites, {art.Models.Count} furniture models."
                       : "No tileset fetched: placeholder shapes.");
        GoToLot(_lotIndex);
    }

    /// <summary>The sidewalk tile in front of a lot.</summary>
    private static TilePos StartFor(Lot lot) =>
        new(lot.X + lot.Width / 2, lot.QuarterTurns == 0 ? lot.Y - 1 : lot.Y + lot.Height, 0);

    /// <summary>Puts you on the sidewalk in front of a lot, looking at the house.</summary>
    private void GoToLot(int index)
    {
        _lotIndex = index;
        var lot = _town.Lots[index];
        bool facesNorth = lot.QuarterTurns == 0;
        if (_game.MonsterAt(StartFor(lot)) is not null)
        {
            _message = "There's a monster standing there.";
            UpdateHud();
            return;
        }
        Pos = StartFor(lot);
        _body.Position = TileToWorld(Pos);
        _yaw = facesNorth ? Mathf.Pi : 0; // yaw 0 looks north
        _pitch = 0;
        ApplyLook();
        UpdateCutaway();
        UpdateVision();
        UpdateHud();
    }

    /// <summary>Where you stand: the sim's player position.</summary>
    private TilePos Pos
    {
        get => _game.Player.Pos;
        set => _game.Player.Pos = value;
    }

    private TileGrid Level => _world.Level(Pos.Z);

    // Mouse-look is read in _Input, before any UI gets a chance to consume the motion.
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= motion.Relative.X * MouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, -1.45f, 1.45f);
            ApplyLook();
            UpdateHud();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            return;
        }
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (_game.Player.IsDead && key.PhysicalKeycode != Key.Escape)
        {
            if (key.PhysicalKeycode is Key.Enter or Key.KpEnter)
            {
                _seed++;
                LoadTown();
            }
            return;
        }

        switch (key.PhysicalKeycode)
        {
            case Key.Escape:
                Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                    ? Input.MouseModeEnum.Visible
                    : Input.MouseModeEnum.Captured;
                break;
            case Key.N:
                _message = "";
                GoToLot((_lotIndex + 1) % _town.Lots.Count);
                break;
            case Key.B:
                _message = "";
                GoToLot((_lotIndex - 1 + _town.Lots.Count) % _town.Lots.Count);
                break;
            case Key.R:
                _seed++;
                LoadTown();
                break;
            case Key.C:
                CloseFacing();
                break;
            case Key.F:
                SmashFacing();
                break;
            case Key.L:
                ToggleFlashlight();
                break;
            // CDDA's 5 (and .) waits a turn; | opens its wait menu. Period is taken by > here.
            case Key.Key5 or Key.Kp5:
                Wait(1);
                break;
            case Key.Backslash when key.ShiftPressed:
                WaitForDawnOrDusk();
                break;
            case Key.Backslash:
                Wait(30 * GameTime.SecondsPerMinute);
                break;
            // CDDA's < and >. The physical comma/period keys cover them shifted or not on US
            // layouts; other layouts land in the Unicode check below.
            case Key.Comma or Key.Pageup:
                MoveVertically(1);
                break;
            case Key.Period or Key.Pagedown:
                MoveVertically(-1);
                break;
            default:
                if (key.Unicode == '<')
                    MoveVertically(1);
                else if (key.Unicode == '>')
                    MoveVertically(-1);
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (RunShots())
            return;
        UpdateVision();
        HandleEvents();
        _creatures.Sync(_game, _body.Position, (float)delta);
        if (_stepping || !_game.Scheduler.PlayerCanAct)
            return;

        float forward = Axis(Key.W, Key.S);
        float right = Axis(Key.D, Key.A);
        if (forward == 0 && right == 0)
            return;

        var step = SnapToOctant(forward, right);
        var target = Pos with { X = Pos.X + step.X, Y = Pos.Y + step.Y };
        var map = Level;
        int cost = map.MoveCost(target.X, target.Y);

        if (_game.MonsterAt(target) is { } monster)
        {
            // CDDA: moving into a monster attacks it.
            _message = "";
            _game.PlayerAttack(monster);
            Pause(0.25f);
        }
        else if (cost > 0)
        {
            _message = "";
            // Walking onto stairs (or a ladder) takes them, rather than waiting for < or >.
            // Arriving on the other end doesn't, so you can step off it.
            var ter = map.TerrainAt(target.X, target.Y);
            int dz = ter.Has("LIQUID") ? 0 : ter.Has("GOES_UP") ? 1 : ter.Has("GOES_DOWN") ? -1 : 0;
            MoveBodyTo(target, StepSeconds * cost / 2f, dz != 0 ? () => MoveVertically(dz) : null);
            // CDDA: entering a tile costs move_cost * 50 moves (100 on normal ground). The world
            // moves on once you're on the new tile, so monsters chase where you are.
            _game.Scheduler.SubmitPlayerAction(cost * 50);
        }
        else if (map.InBounds(target.X, target.Y) && map.TerrainAt(target.X, target.Y) is var door
                 && map.TryOpen(target.X, target.Y, fromInside: map.IsIndoors(Pos.X, Pos.Y)))
        {
            _message = door.Has("LOCKED") ? $"You unlock and open the {door.Name}." : $"You open the {door.Name}.";
            _game.Scheduler.SubmitPlayerAction(100);
            Pause(0.2f);
        }
        else if (map.InBounds(target.X, target.Y))
        {
            var ter = map.TerrainAt(target.X, target.Y);
            var blocker = map.FurnitureAt(target.X, target.Y) is { MoveCostMod: < 0 } f ? f.Name : ter.Name;
            _message = ter.Has("NO_FLOOR")
                ? "You'd fall. (Falling isn't in yet.)"
                : ter.Has("LOCKED") || ter.Has("OPENCLOSE_INSIDE")
                    ? $"The {ter.Name} only opens from the inside. Smash it or a window (F), or find another door."
                    : $"There's a {blocker} in the way.";
            Pause(0.2f);
        }
        UpdateHud();
    }

    /// <summary>Takes stairs or a ladder under you, or climbs a downspout next to you (CDDA's &lt; and &gt;).</summary>
    private void MoveVertically(int dz)
    {
        if (_stepping || !_game.Scheduler.PlayerCanAct)
            return;
        var facing = Facing();
        if (!_world.TryPlanVerticalMove(Pos, dz, (facing.X, facing.Y), out var move, out var reason))
        {
            _message = reason;
            UpdateHud();
            return;
        }
        string dir = dz > 0 ? "up" : "down";
        if (_game.MonsterAt(move.Destination) is { } blocker)
        {
            _message = $"There's a {blocker.Type.Name} in the way!";
            UpdateHud();
            return;
        }
        _message = move.Kind == VerticalMoveKind.Stairs ? $"You walk {dir} the {move.Via}." : $"You climb {dir} the {move.Via}.";
        MoveBodyTo(move.Destination, ClimbSeconds);
        _game.Scheduler.SubmitPlayerAction(move.Cost);
        UpdateHud();
    }

    private void MoveBodyTo(TilePos target, float seconds, Action? then = null)
    {
        Pos = target;
        _stepping = true;
        var tween = CreateTween();
        tween.TweenProperty(_body, "position", TileToWorld(Pos), seconds).SetTrans(Tween.TransitionType.Sine);
        tween.Finished += () =>
        {
            _stepping = false;
            then?.Invoke();
        };
        UpdateCutaway();
    }

    /// <summary>
    /// Hides the levels above while indoors, except near stairs up: the stairwell has no ceiling,
    /// so you'd look up it into the sky.
    /// </summary>
    private void UpdateCutaway()
    {
        var map = Level;
        bool nearStairs = false;
        for (int dy = -2; dy <= 2 && !nearStairs; dy++)
        for (int dx = -2; dx <= 2 && !nearStairs; dx++)
            nearStairs = map.InBounds(Pos.X + dx, Pos.Y + dy) && map.TerrainAt(Pos.X + dx, Pos.Y + dy).Has("GOES_UP");
        _view.SetCutaway(Pos, _world.IsCovered(Pos) && !nearStairs);
    }

    private Vector2I Facing() => new Vector2I(Pos.X, Pos.Y) + SnapToOctant(1, 0);

    /// <summary>
    /// Brings sight, the sky and lamps up to date. Cheap when nothing changed: the sim only
    /// recomputes FOV when you moved, a tile changed, the flashlight was switched or the light shifted.
    /// </summary>
    private void UpdateVision()
    {
        var now = _game.Scheduler.Now;
        _game.Player.CarriedLight = _flashlight ? LightLevels.Flashlight : 0;
        _game.UpdateSight();
        _view.UpdateFog(_game.Sight);
        _dayNight.Show(now);
        _flashlightLight.Visible = _flashlight;
        if (_game.Sight.Light.Version != _lampsVersion)
        {
            _lampsVersion = _game.Sight.Light.Version;
            UpdateLamps();
        }
    }

    /// <summary>Shows what the sim reported: hit flashes, deaths, your own wounds.</summary>
    private void HandleEvents()
    {
        var events = _game.DrainEvents();
        if (events.Count == 0)
            return;
        foreach (var e in events)
        {
            _creatures.Show(e);
            switch (e)
            {
                case PlayerHitEvent { Damage: > 0 }:
                    _hurtFlash.Color = _hurtFlash.Color with { A = 0.35f };
                    CreateTween().TweenProperty(_hurtFlash, "color:a", 0f, 0.4f);
                    break;
                case PlayerDiedEvent:
                    _hurtFlash.Color = _hurtFlash.Color with { A = 0.5f };
                    _deathNotice.Visible = true;
                    break;
            }
        }
        UpdateHud();
    }

    /// <summary>A Godot light for every tile with CDDA's light_emitted.</summary>
    private void UpdateLamps()
    {
        foreach (var lamp in _lamps.GetChildren())
            lamp.QueueFree();
        foreach (var (pos, luminance) in _game.Sight.Light.Emitters)
        {
            _lamps.AddChild(new OmniLight3D
            {
                Position = new Vector3(pos.X + 0.5f, WorldView.LevelBase(pos.Z) + 1.8f, pos.Y + 0.5f),
                OmniRange = LightLevels.Range(luminance) + 1.5f,
                OmniAttenuation = 1.4f,
                LightEnergy = Mathf.Clamp(luminance / 150f, 0.3f, 2f),
                LightColor = new Color(1f, 0.85f, 0.65f),
                ShadowEnabled = true,
            });
        }
    }

    private void ToggleFlashlight()
    {
        if (_stepping || !_game.Scheduler.PlayerCanAct)
            return;
        _flashlight = !_flashlight;
        _message = _flashlight ? "You turn the flashlight on." : "You turn the flashlight off.";
        UpdateVision();
        _game.Scheduler.SubmitPlayerAction(100);
        UpdateHud();
    }

    /// <summary>
    /// Passes time doing nothing. A monster coming into view or hurting you cuts it short, as in
    /// CDDA. Returns false when it was cut short.
    /// </summary>
    private bool Wait(int seconds)
    {
        if (_stepping || !_game.Scheduler.PlayerCanAct)
            return false;
        long start = _game.Scheduler.Turn;
        bool interrupted = _game.Wait(seconds);
        long waited = _game.Scheduler.Turn - start;
        _message = interrupted
            ? $"You stop waiting after {(waited < GameTime.SecondsPerMinute ? $"{waited} seconds" : $"{waited / GameTime.SecondsPerMinute} minutes")}."
            : seconds < GameTime.SecondsPerMinute ? "You wait a moment." : $"You wait {seconds / GameTime.SecondsPerMinute} minutes.";
        UpdateHud();
        return !interrupted;
    }

    private void WaitForDawnOrDusk()
    {
        var now = _game.Scheduler.Now;
        var next = new[] { Celestial.Sunrise(now), Celestial.Sunset(now), Celestial.Sunrise(now.Plus(GameTime.SecondsPerDay)) }
            .Where(t => t > now).Min();
        bool dawn = next == Celestial.Sunrise(next);
        if (Wait((int)(next.Seconds - now.Seconds)))
            _message = dawn ? "You wait until dawn." : "You wait until dusk.";
        UpdateHud();
    }

    private void CloseFacing()
    {
        var target = Facing();
        if (_stepping || !_game.Scheduler.PlayerCanAct)
            return;
        if (_game.MonsterAt(new TilePos(target.X, target.Y, Pos.Z)) is { } blocker)
            _message = $"The {blocker.Type.Name} is in the way.";
        else if (Level.TryClose(target.X, target.Y))
        {
            _message = $"You close the door.";
            _game.Scheduler.SubmitPlayerAction(100);
        }
        else
        {
            _message = "There's nothing to close there.";
        }
        UpdateHud();
    }

    /// <summary>
    /// Unarmed smash strength. CDDA uses Str plus weapon bash damage; an unarmed Str 8 survivor
    /// lands around here until characters and weapons exist.
    /// </summary>
    private const int UnarmedBashStrength = 10;

    private readonly Random _rng = new();

    private void SmashFacing()
    {
        if (_stepping || !_game.Scheduler.PlayerCanAct)
            return;
        if (_game.MonsterAt(new TilePos(Facing().X, Facing().Y, Pos.Z)) is { } monster)
        {
            _message = "";
            _game.PlayerAttack(monster);
            Pause(0.25f);
            UpdateHud();
            return;
        }
        var target = Facing();
        var (outcome, sound, name) = Level.TryBash(target.X, target.Y, UnarmedBashStrength, _rng);
        switch (outcome)
        {
            case TileGrid.BashOutcome.NothingToSmash:
                _message = "There's nothing there you can smash.";
                break;
            case TileGrid.BashOutcome.Failed:
                _message = $"You smash the {name}. {Capitalize(sound)}";
                _game.Scheduler.SubmitPlayerAction(100);
                break;
            case TileGrid.BashOutcome.Broke:
                _message = $"You break the {name}! {Capitalize(sound)}";
                _game.Scheduler.SubmitPlayerAction(100);
                break;
        }
        UpdateHud();
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Blocks movement briefly so a held key doesn't spam bump messages.</summary>
    private void Pause(float seconds)
    {
        _stepping = true;
        GetTree().CreateTimer(seconds).Timeout += () => _stepping = false;
    }

    /// <summary>
    /// Turns movement input into one of the 8 neighboring tiles, relative to where the camera faces.
    /// Tile y grows south, which is +Z in Godot; the camera looks down -Z at yaw 0 (north).
    /// </summary>
    private Vector2I SnapToOctant(float forward, float right)
    {
        var fwd = new Vector2(-Mathf.Sin(_yaw), -Mathf.Cos(_yaw));
        var rgt = new Vector2(Mathf.Cos(_yaw), -Mathf.Sin(_yaw));
        var dir = fwd * forward + rgt * right;
        int octant = Mathf.RoundToInt(Mathf.Atan2(dir.X, dir.Y) / (Mathf.Pi / 4));
        float snapped = octant * Mathf.Pi / 4;
        return new Vector2I(Mathf.RoundToInt(Mathf.Sin(snapped)), Mathf.RoundToInt(Mathf.Cos(snapped)));
    }

    private void ApplyLook()
    {
        _body.Rotation = new Vector3(0, _yaw, 0);
        _camera.Rotation = new Vector3(_pitch, 0, 0);
    }

    private static float Axis(Key positive, Key negative) =>
        (Input.IsPhysicalKeyPressed(positive) ? 1 : 0) - (Input.IsPhysicalKeyPressed(negative) ? 1 : 0);

    private Vector3 TileToWorld(TilePos p)
    {
        var ter = _world.TerrainAt(p);
        bool onStairs = ter.Has("GOES_UP") && !ter.Has("DIFFICULT_Z") && !ter.Id.Contains("ladder", StringComparison.Ordinal);
        return new Vector3(p.X + 0.5f, WorldView.LevelBase(p.Z) + (onStairs ? StairsLift : 0), p.Y + 0.5f);
    }

    private void UpdateHud()
    {
        var map = Level;
        var facing = Facing();
        string ahead = "";
        if (map.InBounds(facing.X, facing.Y))
        {
            var t = map.TerrainAt(facing.X, facing.Y);
            ahead = map.FurnitureAt(facing.X, facing.Y) is { } f ? $"{f.Name} on {t.Name}" : t.Name;
        }
        var here = map.TerrainAt(Pos.X, Pos.Y);
        string where = _town.Lots.FirstOrDefault(l => Pos.X >= l.X && Pos.X < l.X + l.Width && Pos.Y >= l.Y && Pos.Y < l.Y + l.Height)
            is { } lot ? lot.Building.Id : "street";
        string stairs = here.Has("GOES_UP") ? "  (< to go up)" : here.Has("GOES_DOWN") ? "  (> to go down)" : "";
        var now = _game.Scheduler.Now;
        var (rise, set) = (Celestial.Sunrise(now), Celestial.Sunset(now));
        float light = _game.Sight.LightAt(Pos);
        string lightWord = light >= LightLevels.AmbientLit ? "bright" : light >= LightLevels.AmbientLow ? "dim" : "dark";
        _hud.Text =
            $"{now}  ·  sunrise {rise.Hour:00}:{rise.Minute:00}, sunset {set.Hour:00}:{set.Minute:00}  ·  " +
            $"{lightWord} ({light:0}){(_flashlight ? "  ·  flashlight on" : "")}\n" +
            $"{where}  ·  z {Pos.Z:+0;-0;0}  ·  seed {_seed}  ·  turn {_game.Scheduler.Turn}\n" +
            $"{BodyStatus()}\n" +
            $"Here: {here.Name}{stairs}\n" +
            $"Ahead: {ahead}\n" +
            (_warnings.Count > 0 ? $"Mapgen gaps: {_warnings.Count}\n" : "") +
            $"{_message}\n\n" +
            "WASD step · walk into a monster to punch it · mouse look · walk into doors to open · C close · F smash · < > stairs, climb · L flashlight · 5 wait · \\ wait 30 min · | wait till dawn/dusk · N/B next/prev house · R reroll town · Esc mouse";
        UpdateTooltip(facing);
        _log.Text = string.Join("\n", _game.Messages.Skip(Math.Max(0, _game.Messages.Count - LogLines)).Select(m => m.Text));
    }

    /// <summary>Hit points per body part, like CDDA's sidebar: "head 60  torso 52  left arm 60 …".</summary>
    private string BodyStatus()
    {
        var body = _game.Player.Body;
        return string.Join("  ", body.Parts.Select((p, i) => $"{p.Name} {body.Hp(i)}"));
    }

    /// <summary>Lists the top three things you can do to the tile you face, with their keys.</summary>
    private void UpdateTooltip(Vector2I facing)
    {
        var map = Level;
        if (_game.MonsterAt(new TilePos(facing.X, facing.Y, Pos.Z)) is { } monster && _game.PlayerSees(monster))
        {
            double odds = Melee.HitChance(Melee.PlayerAccuracy(_game.Player), monster.Type.Dodge);
            string health = monster.Hp * 3 > monster.Type.Hp * 2 ? "" : monster.Hp * 3 > monster.Type.Hp ? " (hurt)" : " (badly hurt)";
            string monsterState = $"monster {monster.Serial}{health}";
            if (monsterState == _tooltipState)
                return;
            _tooltipState = monsterState;
            _tooltip.Show(Capitalize(monster.Type.Name) + health, new List<(string, string)> { ("W", $"Punch it ({odds:P0} to hit)") });
            return;
        }
        var actions = map.InBounds(facing.X, facing.Y)
            ? Interactions.For(_world, Pos, (facing.X, facing.Y), UnarmedBashStrength)
            : Array.Empty<Interaction>();
        string title = actions.Count == 0 ? "" : map.FurnitureAt(facing.X, facing.Y)?.Name ?? map.TerrainAt(facing.X, facing.Y).Name;
        // Mouse-look calls this every frame; only rebuild the panel when its content changes.
        string state = title + string.Concat(actions.Select(a => "|" + a.Text));
        if (state == _tooltipState)
            return;
        _tooltipState = state;
        _tooltip.Show(Capitalize(title), actions.Select(a => (KeyFor(a.Kind), a.Text)).ToList());
    }

    private static string KeyFor(InteractionKind kind) => kind switch
    {
        InteractionKind.Open or InteractionKind.WalkOnto => "W", // you face it, so stepping forward bumps into it
        InteractionKind.Close => "C",
        InteractionKind.Smash => "F",
        InteractionKind.ClimbUp => "<",
        InteractionKind.ClimbDown => ">",
        _ => "?",
    };

    private static Control Crosshair()
    {
        // MouseFilter.Ignore: the dot sits under the captured cursor and would otherwise swallow mouse motion.
        var dot = new ColorRect
        {
            Color = new Color(1, 1, 1, 0.6f),
            CustomMinimumSize = new Vector2(4, 4),
            Size = new Vector2(4, 4),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        dot.SetAnchorsPreset(Control.LayoutPreset.Center);
        dot.Position -= new Vector2(2, 2);
        return dot;
    }
}
