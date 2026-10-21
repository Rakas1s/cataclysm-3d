using Cataclysm3D.Sim;
using Godot;

namespace Cataclysm3D.Game;

/// <summary>
/// M0 test room: first-person 8-direction tile stepping with free mouse-look,
/// driven by the sim's turn scheduler. Replaced by real mapgen in M1.
/// </summary>
public partial class Main : Node3D
{
    private const int RoomSize = 16;
    private const float EyeHeight = 1.6f;
    private const float StepSeconds = 0.12f;
    private const float MouseSensitivity = 0.003f;
    private const int StepMoveCost = 100;

    private readonly HashSet<Vector2I> _walls = new();
    private TurnScheduler _sim = null!;
    private Node3D _body = null!;
    private Camera3D _camera = null!;
    private Label _hud = null!;
    private Vector2I _tile = new(RoomSize / 2, RoomSize / 2);
    private bool _stepping;
    private float _yaw;
    private float _pitch;

    public override void _Ready()
    {
        _sim = new TurnScheduler(new PlayerActor());
        BuildRoom();

        _body = new Node3D { Position = TileToWorld(_tile) };
        AddChild(_body);
        _camera = new Camera3D { Position = new Vector3(0, EyeHeight, 0), Fov = 80 };
        _body.AddChild(_camera);

        _hud = new Label { Position = new Vector2(16, 16) };
        var canvas = new CanvasLayer();
        canvas.AddChild(_hud);
        AddChild(canvas);
        UpdateHud();

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= motion.Relative.X * MouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, -1.45f, 1.45f);
            _body.Rotation = new Vector3(0, _yaw, 0);
            _camera.Rotation = new Vector3(_pitch, 0, 0);
        }
        else if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }
    }

    public override void _Process(double delta)
    {
        if (_stepping || !_sim.PlayerCanAct)
            return;

        float forward = Axis(Key.W, Key.S);
        float right = Axis(Key.D, Key.A);
        if (forward == 0 && right == 0)
            return;

        // Turn the input into a world direction relative to where the camera faces,
        // then snap it to one of the 8 neighboring tiles.
        var fwd = new Vector2(-Mathf.Sin(_yaw), -Mathf.Cos(_yaw));
        var rgt = new Vector2(Mathf.Cos(_yaw), -Mathf.Sin(_yaw));
        var dir = fwd * forward + rgt * right;
        int octant = Mathf.RoundToInt(Mathf.Atan2(dir.X, dir.Y) / (Mathf.Pi / 4));
        float snapped = octant * Mathf.Pi / 4;
        var step = new Vector2I(Mathf.RoundToInt(Mathf.Sin(snapped)), Mathf.RoundToInt(Mathf.Cos(snapped)));

        var target = _tile + step;
        if (_walls.Contains(target))
            return;

        _tile = target;
        _sim.SubmitPlayerAction(StepMoveCost);
        UpdateHud();

        _stepping = true;
        var tween = CreateTween();
        tween.TweenProperty(_body, "position", TileToWorld(_tile), StepSeconds)
            .SetTrans(Tween.TransitionType.Sine);
        tween.Finished += () => _stepping = false;
    }

    private static float Axis(Key positive, Key negative) =>
        (Input.IsPhysicalKeyPressed(positive) ? 1 : 0) - (Input.IsPhysicalKeyPressed(negative) ? 1 : 0);

    private static Vector3 TileToWorld(Vector2I tile) => new(tile.X + 0.5f, 0, tile.Y + 0.5f);

    private void UpdateHud() =>
        _hud.Text = $"Turn {_sim.Turn}   Tile {_tile}\nWASD step · mouse look · Esc release mouse";

    private void BuildRoom()
    {
        var floorMat = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.33f, 0.3f) };
        var wallMat = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.58f, 0.52f) };
        var floorMesh = new BoxMesh { Size = new Vector3(0.98f, 0.1f, 0.98f), Material = floorMat };
        var wallMesh = new BoxMesh { Size = new Vector3(1, 3, 1), Material = wallMat };

        for (int x = 0; x < RoomSize; x++)
        for (int y = 0; y < RoomSize; y++)
        {
            var tile = new Vector2I(x, y);
            bool edge = x == 0 || y == 0 || x == RoomSize - 1 || y == RoomSize - 1;
            bool pillar = x % 4 == 0 && y % 4 == 0;
            if (edge || pillar)
            {
                _walls.Add(tile);
                AddChild(new MeshInstance3D { Mesh = wallMesh, Position = TileToWorld(tile) + new Vector3(0, 1.5f, 0) });
            }
            else
            {
                AddChild(new MeshInstance3D { Mesh = floorMesh, Position = TileToWorld(tile) - new Vector3(0, 0.05f, 0) });
            }
        }

        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0), ShadowEnabled = true });
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.08f, 0.09f, 0.1f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.4f, 0.4f, 0.45f),
            },
        });
    }
}
