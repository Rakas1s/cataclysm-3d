namespace Cataclysm3D.Sim.Creatures;

public enum DamageType { Bash, Cut, Stab }

/// <summary>Armor against each damage type; damage of that type is reduced by it, down to 0.</summary>
public readonly record struct Armor(int Bash, int Cut, int Stab)
{
    public int Against(DamageType type) => type switch
    {
        DamageType.Bash => Bash,
        DamageType.Cut => Cut,
        _ => Stab,
    };
}

/// <summary>
/// A CDDA <c>MONSTER</c> definition, with <c>copy-from</c> resolved: the fields M4 uses. Special
/// attacks, upgrades, death drops and the rest are left for later milestones.
/// </summary>
public sealed class MonsterType
{
    /// <summary>CDDA's defaults for monsters that don't say (<c>mtype.cpp</c>).</summary>
    public const int DefaultVisionDay = 40, DefaultVisionNight = 1;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? LooksLike { get; init; }
    public string Symbol { get; init; } = "?";
    public string Color { get; init; } = "white";
    public IReadOnlySet<string> Species { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> Flags { get; init; } = new HashSet<string>();

    public int Hp { get; init; } = 1;
    public int Speed { get; init; } = 100;

    /// <summary>100 attacks on sight; CDDA zombies are 100.</summary>
    public int Aggression { get; init; }

    public int MeleeSkill { get; init; }
    public int MeleeDice { get; init; }
    public int MeleeDiceSides { get; init; }

    /// <summary>Extra damage on top of the bash dice (<c>melee_damage</c>), e.g. a tough zombie's 1 cut.</summary>
    public IReadOnlyList<(DamageType Type, int Amount)> MeleeDamage { get; init; } = Array.Empty<(DamageType, int)>();

    /// <summary>Moves an attack costs (<c>attack_cost</c>).</summary>
    public int AttackCost { get; init; } = 100;

    public int Dodge { get; init; }
    public Armor Armor { get; init; }

    /// <summary>Sight range in tiles in full daylight and in total darkness.</summary>
    public int VisionDay { get; init; } = DefaultVisionDay;
    public int VisionNight { get; init; } = DefaultVisionNight;

    /// <summary>
    /// How hard it smashes doors and windows in its way. CDDA sets <c>bash_skill</c> to
    /// melee dice × sides unless the JSON says otherwise.
    /// </summary>
    public int BashSkill { get; init; }

    public bool Has(string flag) => Flags.Contains(flag);
    public bool IsZombie => Species.Contains("ZOMBIE");
    public bool IsHostile => Aggression > 0;
}

/// <summary>One line of a <c>monstergroup</c>: a monster or a nested group, with its weight.</summary>
/// <param name="StartsDay">The entry only spawns this many days in (CDDA's <c>starts</c>).</param>
public sealed record MonsterGroupEntry(string Id, bool IsGroup, int Weight, int StartsDay = 0);

public sealed record MonsterGroup(string Id, string? Default, IReadOnlyList<MonsterGroupEntry> Entries);

/// <summary>
/// Monster types and groups, looked up by id. Types are built on demand by the importer, since
/// CDDA defines about a thousand monsters and a town needs a handful.
/// </summary>
public sealed class MonsterRegistry
{
    private const int MaxGroupDepth = 6;

    private readonly Func<string, MonsterType?> _loadType;
    private readonly Func<string, MonsterGroup?> _loadGroup;
    private readonly Dictionary<string, MonsterType?> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MonsterGroup?> _groups = new(StringComparer.Ordinal);

    public MonsterRegistry(Func<string, MonsterType?> loadType, Func<string, MonsterGroup?> loadGroup)
    {
        _loadType = loadType;
        _loadGroup = loadGroup;
    }

    /// <summary>A registry over fixed lists, for tests.</summary>
    public MonsterRegistry(IEnumerable<MonsterType> types, IEnumerable<MonsterGroup>? groups = null)
        : this(_ => null, _ => null)
    {
        foreach (var t in types)
            _types[t.Id] = t;
        foreach (var g in groups ?? Enumerable.Empty<MonsterGroup>())
            _groups[g.Id] = g;
    }

    public MonsterType? Type(string id)
    {
        if (!_types.TryGetValue(id, out var t))
            _types[id] = t = _loadType(id);
        return t;
    }

    public MonsterGroup? Group(string id)
    {
        if (!_groups.TryGetValue(id, out var g))
            _groups[id] = g = _loadGroup(id);
        return g;
    }

    /// <summary>
    /// Rolls a monster from a group by weight, following nested groups, as of <paramref name="day"/>.
    /// Entries that don't resolve to a known monster passing <paramref name="filter"/> are re-rolled
    /// (a few times), then the group's default is tried. Null when nothing fits.
    /// </summary>
    public MonsterType? Pick(string groupId, Random rng, Func<MonsterType, bool>? filter = null, int day = 0)
    {
        for (int attempt = 0; attempt < 20; attempt++)
            if (Roll(groupId, rng, day, 0) is { } t && (filter?.Invoke(t) ?? true))
                return t;
        return Group(groupId)?.Default is { } d && Type(d) is { } def && (filter?.Invoke(def) ?? true) ? def : null;
    }

    private MonsterType? Roll(string groupId, Random rng, int day, int depth)
    {
        if (depth > MaxGroupDepth || Group(groupId) is not { } group)
            return null;
        var options = group.Entries.Where(e => e.StartsDay <= day && e.Weight > 0).ToList();
        int total = options.Sum(e => e.Weight);
        if (total == 0)
            return null;
        int roll = rng.Next(total);
        foreach (var e in options)
        {
            if ((roll -= e.Weight) >= 0)
                continue;
            return e.IsGroup ? Roll(e.Id, rng, day, depth + 1) : Type(e.Id);
        }
        return null;
    }
}
