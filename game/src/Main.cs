using Cataclysm3D.Game.World;
using Cataclysm3D.Sim;
using Cataclysm3D.Sim.World;
using Godot;

namespace Cataclysm3D.Game;

/// <summary>
/// M1: generates a real CDDA house from its JSON mapgen, renders it as voxels and lets you walk it
/// in first person with 8-direction tile steps and free mouse-look, driven by the sim's turns.
/// </summary>
public partial class Main : Node3D
{
    /// <summary>Grass margin around the 24×24 house so you start outside it.</summary>
    private const int Margin = 8;
    private const float EyeHeight = 1.6f + VoxelVolume.VoxelSize;
    private const float StepSeconds = 0.12f;
    private const float MouseSensitivity = 0.003f;

    private Content _content = null!;
    private TurnScheduler _sim = null!;
    private TileGrid _map = null!;
    private WorldView _view = null!;
    private Node3D _body = null!;
    private Camera3D _camera = null!;
    private Label _hud = null!;

    private int _houseIndex;
    private int _seed = 1;
    private Vector2I _tile;
    private bool _stepping;
    private float _yaw;
    private float _pitch;
    private string _message = "";
    private IReadOnlyList<string> _warnings = Array.Empty<string>();

    public override void _Ready()
    {
        _content = new Content();
        _houseIndex = Math.Max(0, _content.Houses.ToList().IndexOf("bungalow01_1"));

        SetUpEnvironment();
        _view = new WorldView();
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

        LoadHouse();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        ParseShots();
    }

    // Screenshot mode for checking visuals without playing:
    //   godot --path game -- --shot=out.png,tileX,tileY,yawDeg,pitchDeg [--shot=...]
    // Tile coordinates are house-local (0..23); the game saves each shot, then quits.
    private readonly Queue<(string Path, Vector2I Tile, float Yaw, float Pitch)> _shots = new();
    private int _shotFrames;

    private void ParseShots()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--shot=", StringComparison.Ordinal))
                continue;
            var p = arg["--shot=".Length..].Split(',');
            _shots.Enqueue((p[0], new Vector2I(int.Parse(p[1]) + Margin, int.Parse(p[2]) + Margin),
                Mathf.DegToRad(float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture)),
                Mathf.DegToRad(float.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture))));
        }
        if (_shots.Count > 0)
            Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private bool RunShots()
    {
        if (_shots.Count == 0)
            return false;
        var shot = _shots.Peek();
        if (_shotFrames == 0)
        {
            _tile = shot.Tile;
            _body.Position = TileToWorld(_tile);
            _yaw = shot.Yaw;
            _pitch = shot.Pitch;
            ApplyLook();
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

    private void LoadHouse()
    {
        string house = _content.Houses[_houseIndex];
        var result = _content.Mapgen.Generate(house, _seed);
        _warnings = result.Warnings;

        _map = new TileGrid(_content.Registry, result.Map.Width + Margin * 2, result.Map.Height + Margin * 2, "t_grass");
        _map.Stamp(result.Map, Margin, Margin);
        _view.Display(_map);

        _sim = new TurnScheduler(new PlayerActor());
        _tile = new Vector2I(_map.Width / 2, _map.Height - Margin / 2);
        _body.Position = TileToWorld(_tile);
        _yaw = 0; // facing north, toward the house
        _pitch = 0;
        ApplyLook();
        _message = $"Loaded {house} (seed {_seed}). CDDA data loaded in {_content.LoadMilliseconds} ms.";
        UpdateHud();
    }

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
                _houseIndex = (_houseIndex + 1) % _content.Houses.Count;
                LoadHouse();
                break;
            case Key.B:
                _houseIndex = (_houseIndex - 1 + _content.Houses.Count) % _content.Houses.Count;
                LoadHouse();
                break;
            case Key.R:
                _seed++;
                LoadHouse();
                break;
            case Key.C:
                CloseFacing();
                break;
            case Key.F:
                SmashFacing();
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
        var target = _tile + step;
        int cost = _map.MoveCost(target.X, target.Y);

        if (cost > 0)
        {
            _tile = target;
            _message = "";
            // CDDA: entering a tile costs move_cost * 50 moves (100 on normal ground).
            _sim.SubmitPlayerAction(cost * 50);
            _stepping = true;
            var tween = CreateTween();
            tween.TweenProperty(_body, "position", TileToWorld(_tile), StepSeconds * cost / 2f)
                .SetTrans(Tween.TransitionType.Sine);
            tween.Finished += () => _stepping = false;
        }
        else if (_map.InBounds(target.X, target.Y) && _map.TerrainAt(target.X, target.Y) is var door && _map.TryOpen(target.X, target.Y, fromInside: _map.IsIndoors(_tile.X, _tile.Y)))
        {
            _message = door.Has("LOCKED") ? $"You unlock and open the {door.Name}." : $"You open the {door.Name}.";
            _sim.SubmitPlayerAction(100);
            Pause(0.2f);
        }
        else if (_map.InBounds(target.X, target.Y))
        {
            var ter = _map.TerrainAt(target.X, target.Y);
            var blocker = _map.FurnitureAt(target.X, target.Y) is { MoveCostMod: < 0 } f ? f.Name : ter.Name;
            _message = ter.Has("LOCKED") || ter.Has("OPENCLOSE_INSIDE")
                ? $"The {ter.Name} only opens from the inside. Smash it or a window (F), or find another door."
                : $"There's a {blocker} in the way.";
            Pause(0.2f);
        }
        UpdateHud();
    }

    private void CloseFacing()
    {
        var target = _tile + SnapToOctant(1, 0);
        if (_map.TryClose(target.X, target.Y))
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
        var target = _tile + SnapToOctant(1, 0);
        var (outcome, sound, name) = _map.TryBash(target.X, target.Y, UnarmedBashStrength, _rng);
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

    private static Vector3 TileToWorld(Vector2I tile) => new(tile.X + 0.5f, 0, tile.Y + 0.5f);

    private void UpdateHud()
    {
        var facing = _tile + SnapToOctant(1, 0);
        string ahead = "";
        if (_map.InBounds(facing.X, facing.Y))
        {
            var t = _map.TerrainAt(facing.X, facing.Y);
            ahead = _map.FurnitureAt(facing.X, facing.Y) is { } f ? $"{f.Name} on {t.Name}" : t.Name;
        }
        string house = _content.Houses[_houseIndex];
        _hud.Text =
            $"{house}  ·  seed {_seed}  ·  house {_houseIndex + 1}/{_content.Houses.Count}  ·  turn {_sim.Turn}\n" +
            $"Ahead: {ahead}\n" +
            (_warnings.Count > 0 ? $"Mapgen gaps: {_warnings.Count}\n" : "") +
            $"{_message}\n\n" +
            "WASD step · mouse look · walk into doors to open · C close · F smash · N/B next/prev house · R reroll · Esc mouse";
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
            DirectionalShadowMaxDistance = 60,
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
