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

    protected Actor(string name) => Name = name;

    /// <summary>Called by the scheduler while this actor has moves. Must spend moves.</summary>
    public abstract void Act(TurnScheduler scheduler);
}

/// <summary>The player never acts on its own; the presentation layer submits its actions.</summary>
public sealed class PlayerActor : Actor
{
    public PlayerActor(string name = "you") : base(name) { }

    public override void Act(TurnScheduler scheduler) =>
        throw new InvalidOperationException("Player actions are submitted via TurnScheduler.SubmitPlayerAction.");
}
