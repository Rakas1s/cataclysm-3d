using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Sim.Creatures;

/// <summary>
/// A monster on the turn scheduler, with CDDA-like basic AI: it wanders until it sees you, then
/// chases you to where it last saw you, smashing closed doors and windows in its way, and attacks
/// when next to you. It stays on its own z-level.
/// </summary>
public sealed class Monster : Actor
{
    /// <summary>How far a wandering monster strolls from where it stands.</summary>
    public const int WanderRadius = 6;

    /// <summary>Chance per turn an idle monster sets off somewhere.</summary>
    public const double WanderChance = 0.2;

    private readonly GameWorld _world;
    private List<(int X, int Y)>? _path;
    private (TilePos Goal, bool Bash)? _pathFor;
    private TilePos? _wanderGoal;

    public MonsterType Type { get; }

    /// <summary>Unique per world; the renderer uses it to tell monsters apart and pick sprite variants.</summary>
    public int Serial { get; }

    public int Hp { get; set; }

    /// <summary>Where it last saw the player while hunting, or null while wandering.</summary>
    public TilePos? Target { get; private set; }

    /// <summary>The last direction it stepped or attacked in.</summary>
    public (int X, int Y) Heading { get; private set; } = (0, 1);

    internal Monster(GameWorld world, MonsterType type, TilePos pos, int serial) : base(type.Name)
    {
        _world = world;
        Type = type;
        Pos = pos;
        Serial = serial;
        Hp = type.Hp;
        Speed = type.Speed;
    }

    public bool IsHunting => Target is not null;

    /// <summary>Starts hunting the player at <paramref name="at"/> (it saw them, or got hit).</summary>
    public void Notice(TilePos at)
    {
        if (Type.IsHostile && at.Z == Pos.Z)
            Target = at;
    }

    public override void Act(TurnScheduler scheduler)
    {
        int before = Moves;
        Think();
        // Whatever happened, a monster that acted spends at least the rest of this turn.
        if (Moves >= before)
            Moves = Math.Min(Moves, 0);
    }

    private void Think()
    {
        var player = _world.Player;
        if (!player.IsDead && _world.MonsterSees(this, player))
            Notice(player.Pos);

        if (Target is { } target)
        {
            if (!player.IsDead && IsAdjacent(player.Pos) && _world.MonsterSees(this, player))
            {
                Face(player.Pos);
                _world.MonsterAttack(this);
                Moves -= Type.AttackCost;
                return;
            }
            if (Pos == target || !StepToward(target, bash: Type.Has("BASHES")))
            {
                // Lost track: wander from here.
                Target = null;
                _wanderGoal = null;
            }
            else
            {
                return;
            }
        }
        Wander();
    }

    private void Wander()
    {
        if (_wanderGoal is not { } goal || goal == Pos)
        {
            _wanderGoal = null;
            if (_world.Rng.NextDouble() >= WanderChance || _world.RandomWanderGoal(this) is not { } next)
            {
                Moves = 0;
                return;
            }
            _wanderGoal = goal = next;
        }
        if (!StepToward(goal, bash: false))
        {
            _wanderGoal = null;
            Moves = 0;
        }
    }

    private bool IsAdjacent(TilePos p) =>
        p.Z == Pos.Z && Math.Max(Math.Abs(p.X - Pos.X), Math.Abs(p.Y - Pos.Y)) == 1;

    private void Face(TilePos p) => Heading = (Math.Sign(p.X - Pos.X), Math.Sign(p.Y - Pos.Y));

    /// <summary>Takes one step (or one smash) along a path to <paramref name="goal"/>. False if there's no way.</summary>
    private bool StepToward(TilePos goal, bool bash)
    {
        if (_pathFor != (goal, bash) || _path is not { Count: > 0 } || !IsNext(_path[0]))
        {
            _pathFor = (goal, bash);
            _path = _world.FindPath(this, goal, bash);
            if (_path is not { Count: > 0 })
                return false;
        }

        var (nx, ny) = _path[0];
        var next = new TilePos(nx, ny, Pos.Z);
        if (next == _world.Player.Pos)
        {
            // The player stepped into its path but out of sight: bump into them next turn.
            Moves = 0;
            return true;
        }
        if (_world.MonsterAt(next) is not null)
        {
            // Someone's in the way. Wait a moment, then look for a way around.
            _path = null;
            Moves = 0;
            return true;
        }

        var level = _world.Map.Level(Pos.Z);
        Face(next);
        int cost = level.MoveCost(nx, ny);
        if (cost > 0)
        {
            var from = Pos;
            Pos = next;
            _path.RemoveAt(0);
            Moves -= cost * 50;
            _world.MonsterMoved(this, from);
            return true;
        }
        if (Type.Has("CAN_OPEN_DOORS") && level.TryOpen(nx, ny))
        {
            Moves -= 100;
            return true;
        }
        if (bash && GameWorld.IsBashableObstacle(level, nx, ny, Type.BashSkill))
        {
            _world.MonsterBash(this, next);
            Moves -= 100;
            return true;
        }
        _path = null;
        return false;
    }

    private bool IsNext((int X, int Y) step) => Math.Max(Math.Abs(step.X - Pos.X), Math.Abs(step.Y - Pos.Y)) == 1;
}
