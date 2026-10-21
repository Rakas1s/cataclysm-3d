using Cataclysm3D.Sim.Creatures;
using Cataclysm3D.Sim.Items;
using Cataclysm3D.Sim.Vision;
using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Sim;

/// <summary>Something that happened in the sim that the presentation may want to show.</summary>
public abstract record SimEvent;
public sealed record MonsterMovedEvent(Monster Monster, TilePos From) : SimEvent;
public sealed record MonsterHitEvent(Monster Monster, int Damage) : SimEvent;
public sealed record MonsterDiedEvent(Monster Monster) : SimEvent;
public sealed record PlayerHitEvent(Monster By, string Part, int Damage) : SimEvent;
public sealed record PlayerDiedEvent : SimEvent;

/// <summary>What a dead monster leaves behind.</summary>
public sealed record Corpse(TilePos Pos, MonsterType Type, int Serial);

/// <summary>
/// One run: the map, the clock, the player, the monsters and what the player sees. The game
/// submits the player's actions here; everything else happens on the <see cref="TurnScheduler"/>.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>How many lines <see cref="Messages"/> keeps.</summary>
    public const int MaxMessages = 100;

    /// <summary>Path search budget, in tiles expanded; a town-sized detour fits.</summary>
    private const int PathBudget = 4000;

    private readonly List<Monster> _monsters = new();
    private readonly List<Corpse> _corpses = new();
    private readonly List<(long Turn, string Text)> _messages = new();
    private readonly List<SimEvent> _events = new();
    private int _nextSerial;

    public WorldMap Map { get; }
    public TurnScheduler Scheduler { get; }
    public PlayerActor Player => Scheduler.Player;
    public FieldOfView Sight { get; }
    public Random Rng { get; }

    public IReadOnlyList<Monster> Monsters => _monsters;
    public IReadOnlyList<Corpse> Corpses => _corpses;

    /// <summary>The message log, oldest first, with the turn each was written.</summary>
    public IReadOnlyList<(long Turn, string Text)> Messages => _messages;

    public GameWorld(WorldMap map, PlayerActor player, TilePos start, GameTime? startTime = null, int seed = 0)
    {
        Map = map;
        Rng = new Random(seed);
        player.Pos = start;
        Scheduler = new TurnScheduler(player, startTime);
        Sight = new FieldOfView(map);
        Scheduler.TurnStarted += () => UpdateSight();
        Scheduler.TurnStarted += UpdateNeeds;
        player.Needs.Update(Scheduler.Turn, player.Asleep);
        UpdateSight();
    }

    /// <summary>Events since the last call, oldest first.</summary>
    public List<SimEvent> DrainEvents()
    {
        var events = new List<SimEvent>(_events);
        _events.Clear();
        return events;
    }

    public void Log(string text)
    {
        _messages.Add((Scheduler.Turn, text));
        if (_messages.Count > MaxMessages)
            _messages.RemoveAt(0);
    }

    /// <summary>Brings the player's sight up to date (cheap when nothing changed).</summary>
    public void UpdateSight() => Sight.Update(Player.Pos, Player.CarriedLight, Scheduler.Now);

    public Monster Spawn(MonsterType type, TilePos pos)
    {
        var m = new Monster(this, type, pos, _nextSerial++);
        _monsters.Add(m);
        Scheduler.Add(m);
        return m;
    }

    public Monster? MonsterAt(TilePos p)
    {
        foreach (var m in _monsters)
            if (m.Pos == p && !m.IsDead)
                return m;
        return null;
    }

    /// <summary>Whether a tile is free for a creature to stand on: passable, no monster, not the player.</summary>
    public bool IsFree(TilePos p) =>
        Map.InBounds(p) && Map.Level(p.Z).IsPassable(p.X, p.Y) && MonsterAt(p) is null && Player.Pos != p;

    public bool PlayerSees(Monster m) => Sight.IsVisible(m.Pos);

    /// <summary>The monster's name as the player knows it: "the zombie", or "something" when unseen.</summary>
    private string Called(Monster m) => PlayerSees(m) ? $"the {m.Type.Name}" : "something";

    // ---- the player's actions -------------------------------------------------------------

    /// <summary>
    /// Attacks a monster next to you (CDDA: moving into a monster attacks it) with what you
    /// wield, or bare-handed. Costs <see cref="Melee.AttackCost"/> moves (more on a miss with a
    /// heavy weapon), then the world runs.
    /// </summary>
    public void PlayerAttack(Monster target)
    {
        var p = Player;
        string name = Called(target);
        string with = p.Wielded is { } weapon ? $" with your {weapon.Type.Name}" : "";
        int cost = Melee.AttackCost(p);
        target.Notice(p.Pos);
        if (Melee.HitRoll(Rng, Melee.PlayerAccuracy(p)) <= target.Type.Dodge * 5)
        {
            Log($"You miss {name}.");
            cost += Melee.StumbleCost(p);
        }
        else
        {
            int damage = Melee.AfterArmor(Melee.PlayerDamage(p, Rng), target.Type.Armor);
            target.Hp -= damage;
            _events.Add(new MonsterHitEvent(target, damage));
            if (target.Hp <= 0)
            {
                Log($"You kill {name}!");
                Kill(target);
            }
            else
            {
                Log(damage > 0 ? $"You hit {name}{with} for {damage} damage." : $"You hit {name}{with} but do no damage.");
            }
        }
        Spend(cost);
    }

    /// <summary>
    /// Spends time doing nothing, a turn at a time, until <paramref name="turns"/> pass or something
    /// interrupts: a hostile monster comes into view or you get hurt. Returns whether it was cut short.
    /// </summary>
    public bool Wait(int turns)
    {
        UpdateSight();
        var alreadySeen = _monsters.Where(m => m.Type.IsHostile && PlayerSees(m)).ToHashSet();
        int hp = TotalHp();
        for (int i = 0; i < turns && !Player.IsDead; i++)
        {
            Scheduler.SubmitPlayerAction(100);
            UpdateSight();
            if (_monsters.FirstOrDefault(m => !m.IsDead && m.Type.IsHostile && PlayerSees(m) && !alreadySeen.Contains(m)) is { } seen)
            {
                if (i + 1 < turns)
                    Log($"You see {Called(seen)}! You stop waiting.");
                return i + 1 < turns;
            }
            if (TotalHp() < hp)
                return i + 1 < turns;
        }
        return false;
    }

    private int TotalHp() => Enumerable.Range(0, Player.Body.Parts.Count).Sum(Player.Body.Hp);

    // ---- what monsters do (called by Monster) ----------------------------------------------

    internal bool MonsterSees(Monster m, PlayerActor p)
    {
        if (!m.Type.Has("SEES") || m.Pos.Z != p.Pos.Z)
            return false;
        int range = MonsterSight.Range(m.Type, Sight.LightAt(p.Pos));
        return MonsterSight.InSight(Map, m.Pos, p.Pos, Math.Max(1, range));
    }

    internal void MonsterMoved(Monster m, TilePos from) => _events.Add(new MonsterMovedEvent(m, from));

    internal void MonsterAttack(Monster m)
    {
        var p = Player;
        string name = PlayerSees(m) ? $"The {m.Type.Name}" : "Something";
        if (Melee.HitRoll(Rng, m.Type.MeleeSkill) <= Melee.PlayerDodge(p) * 5)
        {
            Log($"{name} misses you.");
            return;
        }
        int part = p.Body.RandomPart(Rng);
        // The blow lands on the main part or one of its minor parts (a hand, a foot), by hit size;
        // whatever you wear there may stop some of it.
        string slot = BodySlots.RandomSlot(p.Body.Parts[part].Id, Rng);
        var (armor, through) = p.Inventory.ArmorAgainst(slot, Rng);
        int damage = Melee.AfterClothing(Melee.MonsterDamage(m.Type, Rng), armor);
        int lost = p.Body.Damage(part, damage);
        string partName = p.Body.Parts[part].Name;
        string soaked = through.Count > 0 && damage == 0 ? $" Your {through[0].Type.Name} protects you." : "";
        Log(lost > 0 ? $"{name} hits your {partName} for {lost} damage."
            : damage == 0 ? $"{name} hits your {partName}.{soaked}"
            : $"{name} hits your {partName}, but it's already broken.");
        _events.Add(new PlayerHitEvent(m, partName, lost));
        if (p.Body.IsDead)
        {
            p.IsDead = true;
            Log("You die...");
            _events.Add(new PlayerDiedEvent());
        }
    }

    /// <summary>
    /// A monster smashes the door or window at <paramref name="at"/>. Monsters with CDDA's
    /// GROUP_BASH add the bash skill of every other such monster next to it.
    /// </summary>
    public void MonsterBash(Monster m, TilePos at)
    {
        int strength = m.Type.BashSkill;
        if (m.Type.Has("GROUP_BASH"))
            strength += _monsters
                .Where(o => o != m && !o.IsDead && o.Type.Has("GROUP_BASH") && o.Pos.Z == at.Z
                            && Math.Max(Math.Abs(o.Pos.X - at.X), Math.Abs(o.Pos.Y - at.Y)) == 1)
                .Sum(o => o.Type.BashSkill);
        var (outcome, sound, what) = Map.Level(at.Z).TryBash(at.X, at.Y, strength, Rng);
        bool seen = Sight.IsVisible(at) || PlayerSees(m);
        if (outcome == TileGrid.BashOutcome.Broke)
            Log(seen ? $"The {m.Type.Name} smashes the {what}! {Capitalize(sound)}" : $"You hear {sound}");
        else if (outcome == TileGrid.BashOutcome.Failed && seen)
            Log($"The {m.Type.Name} bashes the {what}. {Capitalize(sound)}");
    }

    internal static bool IsBashableObstacle(TileGrid level, int x, int y, int strength)
    {
        if (!level.InBounds(x, y) || level.FurnitureAt(x, y) is not null)
            return false;
        var t = level.TerrainAt(x, y);
        return (t.Has("DOOR") || t.Has("WINDOW") || t.Open is not null)
               && level.BashTarget(x, y) is { Bash: var b } && b.StrMin <= strength;
    }

    /// <summary>
    /// A path for a monster on its level. Tiles cost their move cost; other monsters add a little
    /// (so others route around a crowd); closed doors and windows it can smash cost about what
    /// smashing them takes, and ones it can't are walls.
    /// </summary>
    internal List<(int X, int Y)>? FindPath(Monster m, TilePos goal, bool bash)
    {
        if (goal.Z != m.Pos.Z)
            return null;
        var level = Map.Level(m.Pos.Z);
        int skill = m.Type.BashSkill;
        var crowd = _monsters.Where(o => o != m && !o.IsDead && o.Pos.Z == m.Pos.Z).Select(o => (o.Pos.X, o.Pos.Y)).ToHashSet();
        return Pathfinder.Find(Map.Width, Map.Height, (m.Pos.X, m.Pos.Y), (goal.X, goal.Y), (x, y) =>
        {
            int cost = level.MoveCost(x, y);
            if (cost > 0)
                return crowd.Contains((x, y)) ? cost + 6 : cost;
            if (m.Type.Has("CAN_OPEN_DOORS") && level.CanOpen(x, y))
                return 4;
            if (bash && IsBashableObstacle(level, x, y, skill) && level.BashTarget(x, y) is { Bash: var b })
            {
                // Expected smashes: the roll must land in the bottom (skill - min + 1) of (max - min + 1).
                float tries = (b.StrMax - b.StrMin + 1f) / Math.Max(1, skill - b.StrMin + 1);
                return 2 + (int)Math.Min(60, tries * 2);
            }
            return 0;
        }, PathBudget);
    }

    internal TilePos? RandomWanderGoal(Monster m)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var p = m.Pos with
            {
                X = m.Pos.X + Rng.Next(-Monster.WanderRadius, Monster.WanderRadius + 1),
                Y = m.Pos.Y + Rng.Next(-Monster.WanderRadius, Monster.WanderRadius + 1),
            };
            if (p != m.Pos && IsFree(p))
                return p;
        }
        return null;
    }

    private void Kill(Monster m)
    {
        m.IsDead = true;
        m.Hp = Math.Min(m.Hp, 0);
        _monsters.Remove(m);
        _corpses.Add(new Corpse(m.Pos, m.Type, m.Serial));
        _events.Add(new MonsterDiedEvent(m));
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
