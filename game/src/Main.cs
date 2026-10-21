using Cataclysm3D.Game.World;
using Cataclysm3D.Importer.World;
using Cataclysm3D.Sim;
using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game;

/// <summary>
/// M2: generates a small town of real CDDA houses with every z-level of each (basements, upper
/// floors, roofs), renders it as stacked voxel levels and lets you walk it in first person with
/// 8-direction tile steps, stairs and free mouse-look, driven by the sim's turns.
/// </summary>
public partial class Main : Node3D
{
    private const float EyeHeight = 1.6f + VoxelVolume.VoxelSize;
    private const float StepSeconds = 0.12f;
    private const float ClimbSeconds = 0.4f;
    private const float MouseSensitivity = 0.003f;

    /// <summary>How far up a flight of stairs you stand on its bottom tile, so you aren't inside the steps.</summary>
    private const float StairsLift = 0.6f;

    private Content _content = null!;
    private TurnScheduler _sim = null!;
    private Town _town = null!;
    private WorldMap _world = null!;
    private WorldView _view = null!;
    private Node3D _body = null!;
    private Camera3D _camera = null!;
    private Label _hud = null!;

    private int _lotIndex;
    private int _seed = 1;
    private TilePos _pos;
    private bool _stepping;
    private float _yaw;
    private float _pitch;
    private string _message = "";
    private IReadOnlyList<string> _warnings = Array.Empty<string>();

    public override void _Ready()
    {
        _content = new Content();

        SetUpEnvironment();
        _view = new WorldView { Art = _content.Art };
        TileVoxelizer.Registry = _content.Registry;
        AddChild(_view);

        _body = new Node3D();
        AddChild(_body);
        _camera = new Camera3D { Position = new Vector3(0, EyeHeight, 0), Fov = 80, Near = 0.05f };
        _body.AddChild(_camera);
        // A dim personal light so unlit interiors aren't pitch black (real lighting is M3).
        _camera.AddChild(new OmniLight3D { OmniRange = 7, LightEnergy = 0.5f, LightColor = new Color(1f, 0.92f, 0.8f) });

        _hud = new Label { Position = new Vector2(16, 16), MouseFilter = Control.MouseFilterEnum.Ignore };
        _hud.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _hud.AddThemeConstantOverride("shadow_offset_x", 1);
        _hud.AddThemeConstantOverride("shadow_offset_y", 1);
        var canvas = new CanvasLayer();
        canvas.AddChild(_hud);
        canvas.AddChild(Crosshair());
        AddChild(canvas);

        LoadTown();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        ParseShots();
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
                // --shot=out.png,@f_bed,dy,yaw,pitch: stand dy tiles south of the first f_bed in town.
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
    }

    private TilePos? FindFurniture(string id)
    {
        for (int z = _world.MinZ; z <= _world.MaxZ; z++)
        {
            var level = _world.Level(z);
            for (int y = 0; y < level.Height; y++)
            for (int x = 0; x < level.Width; x++)
                if (level.FurnitureAt(x, y)?.Id == id)
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
            _pos = shot.Pos;
            _body.Position = TileToWorld(_pos);
            _yaw = shot.Yaw;
            _pitch = shot.Pitch;
            ApplyLook();
            UpdateCutaway();
            UpdateHud();
        }
        // Give shadows, SSAO and the HUD a few frames to settle.
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
        _sim = new TurnScheduler(new PlayerActor());
        _message = $"Generated a town of {_town.Lots.Count} houses (seed {_seed}) in {clock.ElapsedMilliseconds} ms. " +
                   $"CDDA data loaded in {_content.LoadMilliseconds} ms. " +
                   (_content.Art is { } art
                       ? $"Tilesets {art.Summary} loaded in {art.LoadMilliseconds} ms; {art.Atlas.Count} surface sprites, {art.Models.Count} furniture models."
                       : "No tileset fetched: placeholder shapes.");
        GoToLot(_lotIndex);
    }

    /// <summary>Puts you on the sidewalk in front of a lot, looking at the house.</summary>
    private void GoToLot(int index)
    {
        _lotIndex = index;
        var lot = _town.Lots[index];
        bool facesNorth = lot.QuarterTurns == 0;
        _pos = new TilePos(lot.X + lot.Width / 2, facesNorth ? lot.Y - 1 : lot.Y + lot.Height, 0);
        _body.Position = TileToWorld(_pos);
        _yaw = facesNorth ? Mathf.Pi : 0; // yaw 0 looks north
        _pitch = 0;
        ApplyLook();
        UpdateCutaway();
        UpdateHud();
    }

    private TileGrid Level => _world.Level(_pos.Z);

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
        if (_stepping || !_sim.PlayerCanAct)
            return;

        float forward = Axis(Key.W, Key.S);
        float right = Axis(Key.D, Key.A);
        if (forward == 0 && right == 0)
            return;

        var step = SnapToOctant(forward, right);
        var target = _pos with { X = _pos.X + step.X, Y = _pos.Y + step.Y };
        var map = Level;
        int cost = map.MoveCost(target.X, target.Y);

        if (cost > 0)
        {
            _message = "";
            // CDDA: entering a tile costs move_cost * 50 moves (100 on normal ground).
            _sim.SubmitPlayerAction(cost * 50);
            // Walking onto stairs (or a ladder) takes them, rather than waiting for < or >.
            // Arriving on the other end doesn't, so you can step off it.
            var ter = map.TerrainAt(target.X, target.Y);
            int dz = ter.Has("LIQUID") ? 0 : ter.Has("GOES_UP") ? 1 : ter.Has("GOES_DOWN") ? -1 : 0;
            MoveBodyTo(target, StepSeconds * cost / 2f, dz != 0 ? () => MoveVertically(dz) : null);
        }
        else if (map.InBounds(target.X, target.Y) && map.TerrainAt(target.X, target.Y) is var door
                 && map.TryOpen(target.X, target.Y, fromInside: map.IsIndoors(_pos.X, _pos.Y)))
        {
            _message = door.Has("LOCKED") ? $"You unlock and open the {door.Name}." : $"You open the {door.Name}.";
            _sim.SubmitPlayerAction(100);
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
        if (_stepping || !_sim.PlayerCanAct)
            return;
        var facing = Facing();
        if (!_world.TryPlanVerticalMove(_pos, dz, (facing.X, facing.Y), out var move, out var reason))
        {
            _message = reason;
            UpdateHud();
            return;
        }
        string dir = dz > 0 ? "up" : "down";
        _message = move.Kind == VerticalMoveKind.Stairs ? $"You walk {dir} the {move.Via}." : $"You climb {dir} the {move.Via}.";
        _sim.SubmitPlayerAction(move.Cost);
        MoveBodyTo(move.Destination, ClimbSeconds);
        UpdateHud();
    }

    private void MoveBodyTo(TilePos target, float seconds, Action? then = null)
    {
        _pos = target;
        _stepping = true;
        var tween = CreateTween();
        tween.TweenProperty(_body, "position", TileToWorld(_pos), seconds).SetTrans(Tween.TransitionType.Sine);
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
            nearStairs = map.InBounds(_pos.X + dx, _pos.Y + dy) && map.TerrainAt(_pos.X + dx, _pos.Y + dy).Has("GOES_UP");
        _view.SetCutaway(_pos, _world.IsCovered(_pos) && !nearStairs);
    }

    private Vector2I Facing() => new Vector2I(_pos.X, _pos.Y) + SnapToOctant(1, 0);

    private void CloseFacing()
    {
        var target = Facing();
        if (Level.TryClose(target.X, target.Y))
        {
            _message = $"You close the door.";
            _sim.SubmitPlayerAction(100);
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
        if (!_sim.PlayerCanAct)
            return;
        var target = Facing();
        var (outcome, sound, name) = Level.TryBash(target.X, target.Y, UnarmedBashStrength, _rng);
        switch (outcome)
        {
            case TileGrid.BashOutcome.NothingToSmash:
                _message = "There's nothing there you can smash.";
                break;
            case TileGrid.BashOutcome.Failed:
                _message = $"You smash the {name}. {Capitalize(sound)}";
                _sim.SubmitPlayerAction(100);
                break;
            case TileGrid.BashOutcome.Broke:
                _message = $"You break the {name}! {Capitalize(sound)}";
                _sim.SubmitPlayerAction(100);
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
        var here = map.TerrainAt(_pos.X, _pos.Y);
        string where = _town.Lots.FirstOrDefault(l => _pos.X >= l.X && _pos.X < l.X + l.Width && _pos.Y >= l.Y && _pos.Y < l.Y + l.Height)
            is { } lot ? lot.Building.Id : "street";
        string stairs = here.Has("GOES_UP") ? "  (< to go up)" : here.Has("GOES_DOWN") ? "  (> to go down)" : "";
        _hud.Text =
            $"{where}  ·  z {_pos.Z:+0;-0;0}  ·  seed {_seed}  ·  turn {_sim.Turn}\n" +
            $"Here: {here.Name}{stairs}\n" +
            $"Ahead: {ahead}\n" +
            (_warnings.Count > 0 ? $"Mapgen gaps: {_warnings.Count}\n" : "") +
            $"{_message}\n\n" +
            "WASD step · mouse look · walk into doors to open · C close · F smash · < > stairs, climb · N/B next/prev house · R reroll town · Esc mouse";
    }

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

    private void SetUpEnvironment()
    {
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-48, 35, 0),
            ShadowEnabled = true,
            LightEnergy = 1.1f,
            LightColor = new Color(1f, 0.96f, 0.88f),
            DirectionalShadowMaxDistance = 80,
        });
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.38f, 0.5f, 0.62f),
            SkyHorizonColor = new Color(0.68f, 0.7f, 0.7f),
            GroundBottomColor = new Color(0.2f, 0.2f, 0.18f),
            GroundHorizonColor = new Color(0.55f, 0.55f, 0.5f),
        };
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = sky },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.55f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                SsaoEnabled = true,
                SsaoRadius = 0.6f,
                SsaoIntensity = 1.6f,
                FogEnabled = true,
                FogLightColor = new Color(0.62f, 0.64f, 0.64f),
                FogDensity = 0.006f,
            },
        });
    }
}
