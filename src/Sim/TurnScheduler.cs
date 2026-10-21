namespace Cataclysm3D.Sim;

/// <summary>
/// Advances game time. The world waits for the player: after the player spends moves,
/// <see cref="RunUntilPlayerCanAct"/> lets every other actor use its moves and starts new
/// turns until the player has moves again.
/// </summary>
public sealed class TurnScheduler
{
    private readonly List<Actor> _actors = new();

    public PlayerActor Player { get; }

    /// <summary>Turns elapsed since the start of the game. One turn is one second.</summary>
    public long Turn { get; private set; }

    public IReadOnlyList<Actor> Actors => _actors;

    public bool PlayerCanAct => Player.Moves > 0 && !Player.IsDead;

    public TurnScheduler(PlayerActor player)
    {
        Player = player;
        _actors.Add(player);
        player.Moves = player.Speed;
    }

    public void Add(Actor actor)
    {
        _actors.Add(actor);
        actor.Moves = actor.Speed;
    }

    /// <summary>Spend the player's moves on an action, then run the world until the player can act.</summary>
    public void SubmitPlayerAction(int moveCost)
    {
        if (!PlayerCanAct)
            throw new InvalidOperationException("The player has no moves to spend.");
        if (moveCost <= 0)
            throw new ArgumentOutOfRangeException(nameof(moveCost), "Actions must cost moves.");

        Player.Moves -= moveCost;
        RunUntilPlayerCanAct();
    }

    public void RunUntilPlayerCanAct()
    {
        while (!PlayerCanAct && !Player.IsDead)
        {
            foreach (var actor in _actors.ToArray())
            {
                if (actor == Player)
                    continue;
                while (actor.Moves > 0 && !actor.IsDead)
                {
                    int before = actor.Moves;
                    actor.Act(this);
                    if (actor.Moves >= before)
                        throw new InvalidOperationException($"{actor.Name} acted without spending moves.");
                }
            }
            StartNewTurn();
        }
    }

    private void StartNewTurn()
    {
        Turn++;
        _actors.RemoveAll(a => a.IsDead && a != Player);
        foreach (var actor in _actors)
            actor.Moves += actor.Speed;
    }
}
