using Cataclysm3D.Sim.Creatures;
using Cataclysm3D.Sim.Items;
using Cataclysm3D.Sim.World;

namespace Cataclysm3D.Sim;

/// <summary>
/// Anything that takes turns. Uses CDDA's move-point model: each turn an actor gains
/// <see cref="Speed"/> moves and may act while it has moves left.
/// </summary>
public abstract class Actor
{
    public const int BaseSpeed = 100;

    public string Name { get; }
    public int Speed { get; set; } = BaseSpeed;
    public int Moves { get; set; }
    public bool IsDead { get; set; }

    /// <summary>Where the actor stands.</summary>
    public TilePos Pos { get; set; }

    protected Actor(string name) => Name = name;

    /// <summary>Called by the scheduler while this actor has moves. Must spend moves.</summary>
    public abstract void Act(TurnScheduler scheduler);
}

/// <summary>The player never acts on its own; the presentation layer submits its actions.</summary>
public sealed class PlayerActor : Actor
{
    /// <summary>CDDA's default stats for a new survivor.</summary>
    public int Str { get; init; } = 8;
    public int Dex { get; init; } = 8;

    /// <summary>Per-body-part hit points.</summary>
    public Body Body { get; }

    /// <summary>Picks the male or female base sprite and gendered clothing art, as CDDA does.</summary>
    public bool Male { get; init; } = true;

    /// <summary>Your hair style and color, or null for bald.</summary>
    public Hair? Hair { get; init; }

    /// <summary>What you have on, innermost layer first.</summary>
    public IReadOnlyList<Clothing> Worn { get; init; } = Array.Empty<Clothing>();

    /// <summary>Luminance of the light the player carries (a flashlight that's on), or 0.</summary>
    public float CarriedLight { get; set; }

    public PlayerActor(string name = "you", IReadOnlyList<BodyPartType>? bodyParts = null) : base(name) =>
        Body = new Body(bodyParts ?? BodyPartType.Defaults);

    public override void Act(TurnScheduler scheduler) =>
        throw new InvalidOperationException("Player actions are submitted via TurnScheduler.SubmitPlayerAction.");
}
